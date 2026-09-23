namespace KT.Downloader.Cli.Bulk;

public sealed class LocalPathMapper
{
    private readonly string _rootDirectory;

    public LocalPathMapper(string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);
        _rootDirectory = rootDirectory;
    }

    public string MapTo(string relativePath) => throw new NotImplementedException();
}
