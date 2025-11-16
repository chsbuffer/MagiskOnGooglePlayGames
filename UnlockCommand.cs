using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace hpesuperpower;

static class UnlockCommand
{
	public static void PatchBios(string path)
	{
		// disable secure boot

		var from = " verified_boot_android"u8;
		var to = "          boot_android"u8;

		var bios = File.ReadAllBytes(path);

		var off = bios.AsSpan().IndexOf(from);
		if (off == -1)
		{
			Console.WriteLine("hex not found, the bios might already been patched, nothing to do.");
			return;
		}

		Console.WriteLine($"bios patched at {off:X8}");
		to.CopyTo(bios.AsSpan(off));

		File.WriteAllBytes(path, bios);
	}

	public static void PatchKernelCmdline(string serviceConfigPath)
	{
		// pass kernel-space AVB.

		const string to = "androidboot.verifiedbootstate=orange ";
		var xml = new XmlDocument();
		xml.Load(serviceConfigPath);
		var value = xml.SelectNodes(
				"/configuration/applicationSettings/Google.Hpe.Service.Properties.EmulatorSettings/setting[@name='EmulatorGuestParameters']/value")
			!.Item(0)!;

		var oldParam = value.InnerText;
		if (oldParam.Contains(to))
		{
			Console.WriteLine("Service.exe.config already modified, nothing to do.");
			return;
		}

		value.InnerText = to + value.InnerText;
		xml.Save(serviceConfigPath);
		Console.WriteLine("Service.exe.config modified.");
	}

	public static void PatchServiceExe(string exePath, string outPath, bool isDev)
	{
		// remove foreground white-list

		using var popCwd = new Pushd(Path.GetDirectoryName(exePath)!);

		var assembly = AssemblyDefinition.ReadAssembly(exePath);
		var module = assembly.MainModule;

		Console.WriteLine($"Version: {assembly.Name.FullName}");

		#region Foreground app limit

		if (!isDev)
		{
			Console.WriteLine("* Remove Foreground app limit. *");
			RemoveForegroundLimit();
		}

		void RemoveForegroundLimit()
		{
			// System.Void Google.Hpe.Service.AppSession.AppSessionScope::HandleEmulatorSurfaceStateUpdate(Google.Hpe.Service.Emulator.Surface.EmulatorSurfaceState,Google.Hpe.Service.Emulator.Surface.EmulatorSurfaceState)
			var AppSessionScope = module.GetType("Google.Hpe.Service.AppSession.AppSessionScope");
			var method = AppSessionScope.Methods.Single(x => x.Name == "HandleEmulatorSurfaceStateUpdate");
			var instructions = method.Body.Instructions;
			var begin = instructions.FirstOrDefault(p =>
				p.Operand is FieldDefinition { Name: "_transientForegroundPackages" });

			if (begin == null)
			{
				Console.WriteLine("nothing to patch.");
				return;
			}

			var idx = instructions.IndexOf(begin);
			Console.WriteLine($"Patch Instruction at idx {idx}, offset IL_{begin.Offset:X4}");

			while (instructions[idx].OpCode != OpCodes.Leave_S)
			{
				instructions.RemoveAt(idx);
			}
		}

		#endregion Foreground app limit

		#region Houdini

		Console.WriteLine("* Enable ARM translation layer (libhoudini). *");
		EnableHoudini(isDev);

		void EnableHoudini(bool dev)
		{
			string className = dev
				? "Google.Hpe.Service.KiwiEmulator.EmulatorFeaturePolicyDev"
				: "Google.Hpe.Service.KiwiEmulator.EmulatorFeaturePolicyProd";

			var clazz = module.GetType(className);
			var field = clazz.Fields.SingleOrDefault(it => it.Name.Contains("IsHoudiniEnabled"));
			if (field == null)
			{
				Console.WriteLine("nothing to patch.");
				return;
			}

			foreach (var ctor in clazz.Methods.Where(it => it.IsConstructor && !it.IsStatic))
			{
				var proc = ctor.Body.GetILProcessor();
				var ret = ctor.Body.Instructions.Last();
				proc.InsertBefore(ret, proc.Create(OpCodes.Ldarg_0));
				proc.InsertBefore(ret, proc.Create(OpCodes.Ldc_I4_1));
				proc.InsertBefore(ret, proc.Create(OpCodes.Stfld, field));
			}
		}

		#endregion Houdini

		assembly.Write(outPath);
	}
}