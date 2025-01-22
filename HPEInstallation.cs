using System.Diagnostics;
using System.Security.Principal;

namespace hpesuperpower;

sealed class HPEInstallation
{
	public const string StablePath = @"C:\Program Files\Google\Play Games\current";
	public const string DevPath = @"C:\Program Files\Google\Play Games Developer Emulator\current";

	public readonly bool Dev;
	public readonly string installPath;

	public readonly string bios;
	public readonly string serviceExe;
	public readonly string serviceLib;
	public readonly string serviceConfig;
	public readonly string aggregateImg;

	public readonly string stockBios;
	public readonly string stockServiceExe;
	public readonly string stockServiceLib;
	public readonly string stockServiceConfig;
	public readonly string stockBootImg;

	public HPEInstallation(bool dev)
	{
		Dev = dev;
		installPath = dev ? DevPath : StablePath;

		bios = Path.Combine(installPath, @"emulator\avd\bios.rom");
		serviceExe = Path.Combine(installPath, @"service\Service.exe");
		serviceLib = Path.Combine(installPath, @"service\ServiceLib.dll");
		serviceConfig = Path.Combine(installPath, @"service\Service.exe.config");
		aggregateImg = Path.Combine(installPath, @"emulator\avd\aggregate.img");

		stockBios = Path.Combine(installPath, @"emulator\avd\bios.rom.bak");
		stockServiceExe = Path.Combine(installPath, @"service\Service.exe.bak");
		stockServiceLib = Path.Combine(installPath, @"service\ServiceLib.dll.bak");
		stockServiceConfig = Path.Combine(installPath, @"service\Service.exe.config.bak");
		stockBootImg = Path.Combine(installPath, @"emulator\avd\boot_a.img");
	}

	static bool CanWrite()
	{
		bool isElevated;
		using (var identity = WindowsIdentity.GetCurrent())
		{
			var principal = new WindowsPrincipal(identity);
			isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
		}

		return isElevated;
	}

	public bool Check(bool write)
	{
		if (!Directory.Exists(installPath))
		{
			Console.WriteLine(
				"Google Play Games have not installed. Get it at https://play.google.com/googleplaygames or https://developer.android.com/games/playgames/emulator");
			return false;
		}

		if (write && Process.GetProcesses().Any(p => p.GetFileNameOrDefault() == serviceExe))
		{
			Console.WriteLine($"Please quit Google Play Games.");
			return false;
		}

		if (write && !CanWrite())
		{
			Console.WriteLine("Please run this program from Elevated Command Prompt");
			return false;
		}

		return true;
	}

	public void Unlock()
	{
		if (!File.Exists(stockBios))
		{
			Console.WriteLine("\n\n############# Backup bios.rom");
			File.Copy(bios, stockBios);
		}

		Console.WriteLine("\n\n############# Patch bios.rom");
		UnlockCommand.PatchBios(bios);

		if (!File.Exists(stockServiceConfig))
		{
			Console.WriteLine("\n\n############# Backup Service.exe.config");
			File.Copy(serviceConfig, stockServiceConfig);
		}

		Console.WriteLine("\n\n############# Patch Service.exe.config");
		UnlockCommand.PatchKernelCmdline(serviceConfig);

		if (!File.Exists(stockServiceExe))
		{
			Console.WriteLine("\n\n############# Backup Service.exe");
			File.Copy(serviceExe, stockServiceExe);
		}

		if (!File.Exists(stockServiceLib))
		{
			Console.WriteLine("\n\n############# Backup ServiceLib.dll");
			File.Copy(serviceLib, stockServiceLib);
		}

		if (!Dev)
		{
			if (File.Exists(serviceLib))
			{
				Console.WriteLine("\n\n############# Patch ServiceLib.dll");
				UnlockCommand.PatchServiceExe(stockServiceLib, serviceLib);
			}
			else
			{
				Console.WriteLine("\n\n############# Patch Service.exe");
				UnlockCommand.PatchServiceExe(stockServiceExe, serviceExe);
			}
		}
	}

	public void Patch(FileInfo magiskApk)
	{
		var stockBoot = new FileInfo(stockBootImg);
		if (!stockBoot.Exists)
		{
			Console.WriteLine("\n\n############# Extract boot.img");
			PartitionCommand.Extract(aggregateImg, "boot_a", stockBoot);
		}

		Console.WriteLine("\n\n############# Patch boot.img");
		var newBoot = BootPatchCommand.BootPatch(magiskApk, stockBoot);

		Console.WriteLine("\n\n############# Flash patched boot img");
		PartitionCommand.Flash(aggregateImg, "boot_a", newBoot);

		Console.WriteLine("\n\n############# Cleanup");
		BootPatchCommand.Cleanup();
	}

	public void FlashCommand(string partname, FileInfo infile, bool superpower)
	{
		bool patch = partname == "boot_a" && superpower;

		if (patch)
			infile = BootPatchCommand.SuperpowerPatch(infile);

		PartitionCommand.Flash(aggregateImg, partname, infile);

		if (patch)
			BootPatchCommand.Cleanup();
	}

	public void Restore()
	{
		if (!File.Exists(stockBios))
		{
			Console.WriteLine("No backup founded.");
			return;
		}

		Console.WriteLine("\n\n############# Restore bios.rom");
		File.Copy(stockBios, bios, true);
		Console.WriteLine("\n\n############# Restore Service.exe");
		File.Copy(stockServiceExe, serviceExe, true);
		Console.WriteLine("\n\n############# Restore Service.exe");
		File.Copy(stockServiceLib, serviceLib, true);
		Console.WriteLine("\n\n############# Restore Service.exe.config");
		File.Copy(stockServiceConfig, serviceConfig, true);

		if (File.Exists(stockBootImg))
		{
			Console.WriteLine("\n\n############# Restore stock boot");
			PartitionCommand.Flash(aggregateImg, "boot_a", new FileInfo(stockBootImg));
		}
		else
		{
			Console.WriteLine("Warning: stock boot image not found, skipped.");
		}
	}
}