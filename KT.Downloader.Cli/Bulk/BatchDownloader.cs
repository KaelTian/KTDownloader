using KT.Downloader.Cli.Downloading;
using KT.Downloader.Cli.Manifests;

namespace KT.Downloader.Cli.Bulk;

public sealed class BatchDownloader
{
    private readonly IFileDownloader _downloader;
    private readonly LocalPathMapper _mapper;

    public BatchDownloader(IFileDownloader downloader, LocalPathMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(mapper);

        _downloader = downloader;
        _mapper = mapper;
    }

    public Task<IReadOnlyList<BatchItemResult>> DownloadAsync(
        IReadOnlyList<RemoteFileEntry> entries,
        CancellationToken cancellationToken,
        IProgress<BatchProgress>? progress = null)
        => throw new NotImplementedException();
}
