namespace KT.Downloader.Cli.Downloading;

public sealed record DownloadRequest(Uri Url, string OutputPath, bool Resume = false);
