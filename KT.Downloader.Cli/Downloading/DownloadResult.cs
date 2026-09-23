namespace KT.Downloader.Cli.Downloading;

public abstract record DownloadResult
{
    private DownloadResult() { }

    // BytesWritten 是最终文件的大小；BytesTransferred 是本次真正从网络收下来的字节数，续传时后者更小。
    public sealed record Completed(long BytesWritten, long BytesTransferred) : DownloadResult;

    // BytesOnDisk 等于 .part 文件的长度，也就是下次 -c 的起点。
    public sealed record Canceled(long BytesOnDisk, long? TotalBytes) : DownloadResult;

    public sealed record Failed(string Message) : DownloadResult;
}
