using System.Diagnostics;
using System.Security.Principal;

namespace hpesuperpower;

interface IBackup
{
	void Backup();
	void Restore();
}

class BackupFile(string installPath, string relativePath, string? name = null) : IBackup
{
	private readonly string Name = name ?? relativePath;
	public readonly string SourcePath = Path.Combine(installPath, relativePath);
	public readonly string BackupPath = Path.Combine(installPath, relativePath + ".bak");
	public bool Exists()
	{
		return File.Exists(SourcePath);
	}
	public bool BackupExists()
	{
		return File.Exists(BackupPath);
	}

	public void Backup()
	{
		if (BackupExists())
		{
			return;
		}

		Console.WriteLine($"\n\n############# Backup {Name}");
		if (!Exists())
		{
			Console.WriteLine($"Warning: {Name} not found, skipped.");
			return;
		}

		File.Copy(SourcePath, BackupPath, true);
	}

	public void Restore()
	{
		Console.WriteLine($"\n\n############# Restore {Name}");
		if (BackupExists())
		{
			File.Copy(BackupPath, SourcePath, true);
		}
		else
		{
			Console.WriteLine($"Warning: {Name} not found, skipped.");
		}
	}
}

class PartitionFile(string installPath) : IBackup
{
	public readonly string PartName = "boot_a";
	public readonly string AggregateImgPath = Path.Combine(installPath, @"emulator\avd\aggregate.img");
	public readonly string ExtractPartitionPath = Path.Combine(installPath, @"emulator\avd\boot_a.img");

	public void Backup()
	{
		if (File.Exists(ExtractPartitionPath))
		{
			return;
		}

		Console.WriteLine("\n\n############# Extract boot_a.img");
		PartitionCommand.Extract(AggregateImgPath, PartName, new FileInfo(ExtractPartitionPath));
	}

	public void Restore()
	{
		Console.WriteLine("\n\n############# Restore stock boot");
		if (!File.Exists(ExtractPartitionPath))
		{
			Console.WriteLine("Warning: stock boot image not found, skipped.");
			return;
		}

		PartitionCommand.Flash(AggregateImgPath, PartName, new FileInfo(ExtractPartitionPath));
	}
}

sealed class HPEInstallation
{
	private readonly BackupFile bios;
	private readonly BackupFile serviceExe;
	private readonly BackupFile serviceLib;
	private readonly BackupFile serviceConfig;
	private readonly PartitionFile bootImg;

	public const string StablePath = @"C:\Program Files\Google\Play Games\current";
	public const string DevPath = @"C:\Program Files\Google\Play Games Developer Emulator\current";

	public readonly bool Dev;
	public readonly string InstallPath;

	public string AggregateImg => bootImg.AggregateImgPath;

	public HPEInstallation(bool dev)
	{
		Dev = dev;
		InstallPath = dev ? DevPath : StablePath;

		bios = new(InstallPath, @"emulator\avd\bios.rom");
		serviceExe = new(InstallPath, @"service\Service.exe");
		serviceLib = new(InstallPath, @"service\ServiceLib.dll");
		serviceConfig = new(InstallPath, @"service\Service.exe.config");
		bootImg = new(InstallPath);
	}

	private static bool IsElevated()
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
		if (!Directory.Exists(InstallPath))
		{
			Console.WriteLine(
				"Google Play Games have not installed. Get it at https://play.google.com/googleplaygames or https://developer.android.com/games/playgames/emulator");
			return false;
		}

		if (write && Process.GetProcesses().Any(p => p.GetFileNameOrDefault() == serviceExe.SourcePath))
		{
			Console.WriteLine($"Please quit Google Play Games.");
			return false;
		}

		if (write && !IsElevated())
		{
			Console.WriteLine("Please run this program from Elevated Command Prompt");
			return false;
		}

		return true;
	}

	public void Unlock()
	{
		bios.Backup();
		Console.WriteLine("\n\n############# Patch bios.rom");
		UnlockCommand.PatchBios(bios.SourcePath);

		serviceExe.Backup();
		Console.WriteLine("\n\n############# Patch Service.exe.config");
		UnlockCommand.PatchKernelCmdline(serviceConfig.SourcePath);

		serviceLib.Backup();

		if (!Dev)
		{
			if (serviceLib.Exists())
			{
				Console.WriteLine("\n\n############# Patch ServiceLib.dll");
				UnlockCommand.PatchServiceExe(serviceLib.BackupPath, serviceLib.SourcePath);
			}
			else
			{
				Console.WriteLine("\n\n############# Patch Service.exe");
				UnlockCommand.PatchServiceExe(serviceExe.BackupPath, serviceExe.SourcePath);
			}
		}
	}

	public void Patch(FileInfo magiskApk)
	{
		bootImg.Backup();

		Console.WriteLine("\n\n############# Patch boot.img");
		var newBoot = BootPatchCommand.BootPatch(magiskApk, new FileInfo(bootImg.ExtractPartitionPath));

		Console.WriteLine("\n\n############# Flash patched boot img");
		PartitionCommand.Flash(bootImg.AggregateImgPath, "boot_a", newBoot);

		Console.WriteLine("\n\n############# Cleanup");
		BootPatchCommand.Cleanup();
	}

	public void FlashCommand(string partname, FileInfo infile, bool superpower)
	{
		bool patch = partname == "boot_a" && superpower;

		if (patch)
			infile = BootPatchCommand.SuperpowerPatch(infile);

		PartitionCommand.Flash(bootImg.AggregateImgPath, partname, infile);

		if (patch)
			BootPatchCommand.Cleanup();
	}

	public void Restore()
	{
		if (!bios.BackupExists())
		{
			Console.WriteLine("No backup founded.");
			return;
		}

		bios.Restore();
		serviceExe.Restore();
		serviceLib.Restore();
		serviceConfig.Restore();

		bootImg.Restore();
	}
}