namespace KT.Downloader.Cli.Downloading;

public interface IFileDownloader
{
    Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        CancellationToken cancellationToken,
        IProgress<DownloadProgress>? progress = null);
}
