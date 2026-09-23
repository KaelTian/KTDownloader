namespace KT.Downloader.Cli.Downloading;

public sealed record DownloadProgress(long BytesReceived, long? TotalBytes);
