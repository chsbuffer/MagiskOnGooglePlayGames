using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;

using hpesuperpower;

var head = @"HPE Superpower tool.
Google Play Games Emulator modding tool.
Author: ChsBuffer
".Trim();

var rootCmd = new RootCommand(head);
var prog = rootCmd.Name;

// --dev
var variantOption = new Option<bool>("--dev")
{
	Description = "(Global option) Apply to Google Play Games Emulator for Developers", Recursive = true
};
rootCmd.Options.Add(variantOption);

// Auto Patch (Unlock+Extract+Patch+Flash)
var magiskArgument = new Argument<FileInfo>("apkfile") { Description = "Magisk APK file path", };
var magiskCmd =
	new Command("magisk", "Root your Google Play Games with single command. Tested on Magisk v28.0, v28.1")
	{
		magiskArgument
	};

rootCmd.Subcommands.Add(magiskCmd);

// Unlock
var unlockCmd = new Command("unlock", "Unlock bootloader");
rootCmd.Subcommands.Add(unlockCmd);

// List
var humanOption = new Option<bool>("--human", "-h") { Description = "Human readable" };
var listCommand = new Command("ls", "List partitions in aggregate.img") { humanOption };
rootCmd.Subcommands.Add(listCommand);

var partitionNameArg = new Argument<string>("partname") { Description = "Partition name" };
var partitionFileArg = new Argument<FileInfo>("file") { Description = "Path to the partition image file" };
// Extract
var extractCmd = new Command("extract", "Extract partition from aggregate.img") { partitionNameArg, partitionFileArg };
rootCmd.Subcommands.Add(extractCmd);

// Flash
var superpowerOption = new Option<bool>("--superpower")
{
	Description = "Patch magisk patched boot image again to remove install restriction"
};
var flashCmd =
	new Command("flash", "Flash partition to aggregate.img") { partitionNameArg, partitionFileArg, superpowerOption };
rootCmd.Subcommands.Add(flashCmd);
var flashExample = @$"
Example:
  {prog} flash boot_a magisk_patched.img --superpower
  {prog} flash boot_a boot_a.img.bak".Trim();

// Restore
var restoreCmd = new Command("restore", "Undo all changes made by this tool");
rootCmd.Subcommands.Add(restoreCmd);

if (rootCmd.Options.FirstOrDefault(x => x is HelpOption) is HelpOption defaultHelpOption)
{
	defaultHelpOption.Action = new CustomHelpAction(new Dictionary<Command, string> { [flashCmd] = flashExample },
		(HelpAction)defaultHelpOption.Action!);
}

magiskCmd.SetAction(parseResult =>
{
	var dev = parseResult.GetRequiredValue(variantOption);
	var magisk = parseResult.GetRequiredValue(magiskArgument);

	var m = new HPEInstallation(dev);
	if (!m.Check(write: true)) return 1;
	m.Unlock();
	m.Patch(magisk);
	return 0;
});

unlockCmd.SetAction(parseResult =>
{
	var dev = parseResult.GetValue(variantOption);

	var m = new HPEInstallation(dev);
	if (!m.Check(write: true)) return 1;
	m.Unlock();
	return 0;
});

listCommand.SetAction(parseResult =>
{
	var dev = parseResult.GetValue(variantOption);
	var human = parseResult.GetValue(humanOption);

	var m = new HPEInstallation(dev);
	if (!m.Check(write: false)) return 1;
	PartitionCommand.List(m.AggregateImg, human);
	return 0;
});

extractCmd.SetAction(parseResult =>
{
	var dev = parseResult.GetValue(variantOption);
	var partname = parseResult.GetRequiredValue(partitionNameArg);
	var outfile = parseResult.GetRequiredValue(partitionFileArg);

	var m = new HPEInstallation(dev);
	if (!m.Check(write: false)) return 1;
	PartitionCommand.Extract(m.AggregateImg, partname, outfile);
	return 0;
});

flashCmd.SetAction(parseResult =>
{
	var dev = parseResult.GetValue(variantOption);
	var partname = parseResult.GetRequiredValue(partitionNameArg);
	var infile = parseResult.GetRequiredValue(partitionFileArg);
	var superpower = parseResult.GetRequiredValue(superpowerOption);

	var m = new HPEInstallation(dev);
	if (!m.Check(write: true)) return 1;
	m.FlashCommand(partname, infile, superpower);
	return 0;
});

restoreCmd.SetAction(parseResult =>
{
	var dev = parseResult.GetValue(variantOption);

	var m = new HPEInstallation(dev);
	if (!m.Check(write: true)) return 1;
	m.Restore();
	return 0;
});

return rootCmd.Parse(args).Invoke();

internal sealed class CustomHelpAction(Dictionary<Command, string> additionalHelp, HelpAction action)
	: SynchronousCommandLineAction
{
	public override int Invoke(ParseResult parseResult)
	{
		int result = action.Invoke(parseResult);
		if (additionalHelp.TryGetValue(parseResult.CommandResult.Command, out var additionalHelpMessage))
		{
			parseResult.InvocationConfiguration.Output.WriteLine(additionalHelpMessage);
		}

		return result;
	}
}