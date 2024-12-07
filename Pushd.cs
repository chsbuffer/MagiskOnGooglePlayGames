namespace hpesuperpower;
sealed class Pushd : IDisposable
{
	public string StartDirectory { get; }
	public Pushd(string dir)
	{
		StartDirectory = Environment.CurrentDirectory;
		Environment.CurrentDirectory = dir;
	}

	public void Dispose()
	{
		Environment.CurrentDirectory = StartDirectory;
	}
}
