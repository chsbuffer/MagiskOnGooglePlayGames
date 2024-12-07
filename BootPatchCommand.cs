using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

using static Helper;


namespace hpesuperpower;


sealed class BootPatchCommand
{
	static void ExtractResource(string key, string path)
	{
#if false
		File.Copy("..\\" + key, path);
#else
		var assembly = System.Reflection.Assembly.GetExecutingAssembly();
		using var stream = assembly
		  .GetManifestResourceStream($"{nameof(hpesuperpower)}.Resources.{key}")!;
		using var fs = File.OpenWrite(path);
		stream.CopyTo(fs);
#endif
	}

	const string TempDirName = "hpesuperpower_temp";
	public static FileInfo BootPatch(FileInfo magiskApk, FileInfo boot_img)
	{
		Console.WriteLine("WARNING!");
		Console.WriteLine("The process has only been tested on Magisk v28.0, if the patch fails please follow the official patch guide.");

		using var popPwd = Prepare(boot_img);

		#region boot_patch.sh
		using var magisk = magiskApk.OpenRead();
		var zip = new ZipArchive(magisk);
		zip.GetEntry("lib/x86_64/libmagisk.so")!.ExtractToFile("magisk");
		zip.GetEntry("assets/stub.apk")!.ExtractToFile("stub.apk");
		zip.GetEntry("lib/x86_64/libinit-ld.so")!.ExtractToFile("init-ld");
		zip.GetEntry("lib/x86_64/libmagiskinit.so")!.ExtractToFile("magiskinit");

		Run($"magiskboot.exe unpack boot.img").Z();
		string sha1;
		var status = Run("magiskboot.exe cpio ramdisk.cpio test");
		switch (status & 3)
		{
			case 0:
				Console.WriteLine("- Stock boot image detected");
#pragma warning disable CA5350 // 不要使用弱加密算法
				var sha1b = SHA1.HashData(File.ReadAllBytes(boot_img.FullName));
#pragma warning restore CA5350 // 不要使用弱加密算法
				sha1 = Convert.ToHexString(sha1b).ToLowerInvariant();
				break;
			case 1:
				Console.WriteLine("- Magisk patched boot image detected");
				Run("magiskboot.exe",
"""
cpio ramdisk.cpio 
"extract .backup/.magisk config.orig" 
"restore"
""".NoEOL()).Z();
				sha1 = File.ReadAllLines("config.orig").Single(x => x.StartsWith("SHA1=", StringComparison.InvariantCulture))["SHA1=".Length..];
				break;
			default: // case 2
				Console.WriteLine("! Boot image patched by unsupported programs");
				throw new InvalidOperationException();
		}

		File.Copy("ramdisk.cpio", "ramdisk.cpio.orig");

		Run("magiskboot.exe compress=xz magisk magisk.xz").Z();
		Run("magiskboot.exe compress=xz stub.apk stub.xz").Z();
		Run("magiskboot.exe compress=xz init-ld init-ld.xz").Z();

		var config = new Dictionary<string, string>(){
{"KEEPVERITY","true"},
{"KEEPFORCEENCRYPT","true"},
{"RECOVERYMODE","false"},
{"PREINITDEVICE","metadata"},
{"SHA1",sha1},
};

		File.WriteAllText("config", string.Join("\n", config.Select(p => $"{p.Key}={p.Value}")));
		var steps = """
cpio ramdisk.cpio 
"add 0750 init magiskinit" 
"mkdir 0750 overlay.d" 
"mkdir 0750 overlay.d/sbin" 
"add 0644 overlay.d/sbin/magisk.xz magisk.xz" 
"add 0644 overlay.d/sbin/stub.xz stub.xz" 
"add 0644 overlay.d/sbin/init-ld.xz init-ld.xz" 
"patch" 
"backup ramdisk.cpio.orig" 
"mkdir 000 .backup" 
"add 000 .backup/.magisk config" 
""".NoEOL();

		Run("magiskboot.exe", steps, config).Z();
		#endregion

		SuperpowerPatch();
		Run("magiskboot.exe repack boot.img").Z();
		var newboot = new FileInfo("new-boot.img");
		if (!newboot.Exists)
			throw new InvalidOperationException("Patched file not found!");

		return newboot;
	}

	static Pushd Prepare(FileInfo boot_img)
	{
		try
		{
			Directory.Delete(TempDirName, true);
		}
		catch { }
		var dir = Directory.CreateDirectory(TempDirName);
		var popd = new Pushd(dir.FullName);
		boot_img.CopyTo("boot.img");
		ExtractResource("magiskboot.exe", "magiskboot.exe");
		return popd;
	}

	public static void Cleanup()
	{
		foreach (var fsi in new DirectoryInfo(TempDirName).GetFileSystemInfos("*", SearchOption.AllDirectories))
		{
			// remove read-only flags. magiskboot.exe cpio extract does this, don't know why.
			fsi.Attributes = FileAttributes.Normal;
		}

		Directory.Delete(TempDirName, true);
	}

	public static FileInfo SuperpowerPatch(FileInfo boot_img)
	{
		using var popPwd = Prepare(boot_img);
		Run($"magiskboot.exe unpack boot.img").Z();
		SuperpowerPatch();
		Run("magiskboot.exe repack boot.img").Z();
		var newboot = new FileInfo("new-boot.img");
		if (!newboot.Exists)
			throw new InvalidOperationException("Patched file not found!");

		return newboot;
	}

	static void SuperpowerPatch()
	{
		var status = Run("magiskboot.exe cpio ramdisk.cpio test");
		if (status != 1)
			throw new InvalidOperationException("Given file is not a magisk patched boot image!");

		// remove no_install_unknown_sources_globally restriction
		ExtractResource("superpower.apk", "superpower.apk");
		ExtractResource("custom.rc", "custom.rc");

		var steps = """
cpio ramdisk.cpio 
"add 0644 overlay.d/custom.rc custom.rc" 
"add 0755 overlay.d/sbin/superpower.apk superpower.apk" 
""".NoEOL();

		Run("magiskboot.exe", steps).Z();
	}

}
