using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using hpesuperpower;

var prog = "hpesuperpower.exe";
var head = @"HPE Superpower tool.
Google Play Games Emulator modding tool.
Author: ChsBuffer
".Trim();

var rootCmd = new RootCommand(head)
{
	Name = prog,
};

// --dev
var variantOption = new Option<bool>("--dev", "(Global option) Apply to Google Play Games Emulator for Developers");
rootCmd.AddGlobalOption(variantOption);

// Auto Patch (Unlock+Extract+Patch+Flash)
var magiskArgument = new Argument<FileInfo>("apkfile", "Magisk APK file path");
var magiskCmd = new Command("magisk", "Root your Google Play Games with single command. Tested on Magisk v28.0, v28.1")
{
	magiskArgument
};

rootCmd.AddCommand(magiskCmd);

// Unlock
var unlockCmd = new Command("unlock", "Unlock bootloader");
rootCmd.AddCommand(unlockCmd);

// List
var humanOption = new Option<bool>("-h", "Human readable");
var listCommand = new Command("ls", "List partitions in aggregate.img"){
	humanOption
};
rootCmd.AddCommand(listCommand);

var partitionNameArg = new Argument<string>("partname", "Partition name");
var partitionFileArg = new Argument<FileInfo>("file", "Path to the partition image file");
// Extract
var extractCmd = new Command("extract", "Extract partition from aggregate.img") {
	partitionNameArg,
	partitionFileArg
};
rootCmd.AddCommand(extractCmd);

// Flash
var superpowerOption = new Option<bool>("--superpower", "Patch magisk patched boot image again to remove install restriction");
var flashCmd = new Command("flash", "Flash partition to aggregate.img") {
	partitionNameArg,
	partitionFileArg,
	superpowerOption
};
rootCmd.AddCommand(flashCmd);
var flashExample = @$"
Example:
  {prog} flash boot_a magisk_patched.img --superpower
  {prog} flash boot_a boot_a.img.bak".Trim();

// Restore
var restoreCmd = new Command("restore", "Undo all changes made by this tool");
rootCmd.AddCommand(restoreCmd);

var parser = new CommandLineBuilder(rootCmd)
		.UseEnvironmentVariableDirective()
		.UseParseDirective()
		.UseTypoCorrections()
		.UseParseErrorReporting()
		.UseExceptionHandler()
		.CancelOnProcessTermination()
		.UseHelp(ctx =>
		{
			if (ctx.Command == flashCmd)
			{
				ctx.HelpBuilder.CustomizeLayout(_ =>
					HelpBuilder.Default
					.GetLayout()
					.Append(_ => _.Output.WriteLine(flashExample))
				);
			}
		})
		.Build();

magiskCmd.SetHandler((dev, magisk) =>
{
	var m = new HPEInstallation(dev);
	if (!m.Check(write: true)) return;
	m.Unlock();
	m.Patch(magisk);
}, variantOption, magiskArgument);

unlockCmd.SetHandler((dev, magisk) =>
{
	var m = new HPEInstallation(dev);
	if (!m.Check(write: true)) return;
	m.Unlock();
}, variantOption, magiskArgument);

listCommand.SetHandler((dev, human) =>
{
	var m = new HPEInstallation(dev);
	if (!m.Check(write: false)) return;
	PartitionCommand.List(m.AggregateImg, human);
}, variantOption, humanOption);

extractCmd.SetHandler((dev, partname, outfile) =>
{
	var m = new HPEInstallation(dev);
	if (!m.Check(write: false)) return;
	PartitionCommand.Extract(m.AggregateImg, partname, outfile);
}, variantOption, partitionNameArg, partitionFileArg);

flashCmd.SetHandler((dev, partname, infile, superpower) =>
{
	var m = new HPEInstallation(dev);
	if (!m.Check(write: true)) return;
	m.FlashCommand(partname, infile, superpower);
}, variantOption, partitionNameArg, partitionFileArg, superpowerOption);

restoreCmd.SetHandler((dev) =>
{
	var m = new HPEInstallation(dev);
	if (!m.Check(write: true)) return;
	m.Restore();
}, variantOption);

parser.Invoke(args);
