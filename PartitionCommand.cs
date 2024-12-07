using DiskPartitionInfo.Gpt;

using static DiskPartitionInfo.DiskPartitionInfo;

namespace hpesuperpower;

static class PartitionEntryExtension
{
	public const int LBS = 512; // Logical block size

	public static ulong SizeBytes(this PartitionEntry part) => (part.LastLba - part.FirstLba + 1) * LBS;
	public static ulong Offset(this PartitionEntry part) => part.FirstLba * LBS;
}

sealed class PartitionCommand
{
	static void CopyExact(Stream src, Stream dest, int size, int bufferSize = 4096)
	{
		var buffer = new byte[bufferSize];
		var readed = 0;
		while (readed < size)
		{
			var read = Math.Min(size - readed, bufferSize);
			src.ReadExactly(buffer, 0, read);
			dest.Write(buffer, 0, read);
			readed += read;
		}
	}

	static string Humanize(ulong d)
	{
		string[] Suffix = { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };
		const double step = 1024.00;

		byte level = 0;
		double size = d;
		while (size > step)
		{
			if (level >= Suffix.Length - 1)
				break;

			level++;
			size /= step;
		}

		return $@"{size:0.##}{Suffix[level]}";
	}

	public static void List(string diskPath, bool humanize)
	{
		var gpt = ReadGpt().Primary().FromPath(diskPath);

		foreach (var part in gpt.Partitions.TakeWhile(x => x.Type != Guid.Empty))
		{
			if (humanize)
				Console.WriteLine($"{part.Name}\t{Humanize(part.SizeBytes())}");
			else
				Console.WriteLine($"{part.Name}\t{part.SizeBytes()}");
		}
	}

	static List<PartitionEntry> GetPartitions(string diskPath)
	{
		var gpt = ReadGpt().Primary().FromPath(diskPath);
		return gpt.Partitions.TakeWhile(x => x.Type != Guid.Empty).ToList();
	}

	static PartitionEntry? GetPartition(string diskPath, string partname)
	{
		return ReadGpt().Primary().FromPath(diskPath).
			Partitions.TakeWhile(x => x.Type != Guid.Empty)
			.FirstOrDefault(x => x.Name == partname);
	}

	public static void Extract(string diskPath, string partname, FileInfo imgFile)
	{
		var part = GetPartition(diskPath, partname);
		if (part == null)
		{
			Console.WriteLine($"Partition {partname} not found.");
			return;
		}

		if (imgFile.Exists)
		{
			throw new InvalidOperationException($"file already exists: {imgFile.FullName}");
		}

		using var outfile = imgFile.Create();
		using var fs = File.OpenRead(diskPath);
		fs.Seek((long)part.Offset(), SeekOrigin.Begin);
		CopyExact(fs, outfile, (int)part.SizeBytes());
		Console.WriteLine($"Extracted: {partname} -> {imgFile.FullName}");
	}

	public static void Flash(string diskPath, string partname, FileInfo imgFile)
	{
		var part = GetPartition(diskPath, partname);
		if (part == null)
		{
			Console.WriteLine($"Partition {partname} not found.");
			return;
		}
		Console.WriteLine($"{partname} at {part.Offset():X8}");

		if (part.SizeBytes() != (ulong)imgFile.Length)
			throw new InvalidOperationException($"partition size not matches, image file size {imgFile.Length}, partition size {part.SizeBytes()}");

		using var fs = File.OpenWrite(diskPath);
		using var s = imgFile.OpenRead();

		fs.Seek((long)part.Offset(), SeekOrigin.Begin);
		s.CopyTo(fs);
		Console.WriteLine($"Flashed: {partname} <- {imgFile.FullName}");
	}
}
