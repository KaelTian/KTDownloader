using System.Net;
using System.Net.Http.Headers;

namespace KT.Downloader.Cli.Downloading;

public sealed class HttpFileDownloader : IFileDownloader
{
    public const string PartialFileSuffix = ".part";

    // 一次读 64KB，兼顾内存占用和磁盘/网络吞吐
    private const int BufferSize = 64 * 1024;

    private readonly HttpClient _httpClient;

    public HttpFileDownloader(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        CancellationToken cancellationToken,
        IProgress<DownloadProgress>? progress = null)
    {
        var tempPath = request.OutputPath + PartialFileSuffix;
        long? totalBytes = null;

        try
        {
            var resumeFrom = GetResumeOffset(request, tempPath);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, request.Url);
            if (resumeFrom > 0)
                httpRequest.Headers.Range = new RangeHeaderValue(resumeFrom, null);

            using var response = await _httpClient.SendAsync(
                httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                return FinishFromPartialFile(response, tempPath, request.OutputPath, resumeFrom);

            // 要了 Range 却回了 200：服务器不支持续传。此时正文是完整的，
            // 再往 .part 后面追加就会拼出一个又长又烂的文件，只能从头写。
            if (resumeFrom > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                resumeFrom = 0;

            response.EnsureSuccessStatusCode();

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);

            var directory = Path.GetDirectoryName(request.OutputPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            totalBytes = ResolveTotalBytes(response, resumeFrom);

            long transferred;

            // 花括号划出作用域，好让文件句柄在改名之前就释放掉
            {
                await using var output = new FileStream(
                    tempPath,
                    resumeFrom > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                transferred = await CopyAsync(input, output, resumeFrom, totalBytes, progress, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, request.OutputPath, overwrite: true);

            return new DownloadResult.Completed(new FileInfo(request.OutputPath).Length, transferred);
        }
        catch (OperationCanceledException)
        {
            // 「取消」这一格装着两种语义相反的来源，靠 token 分流：
            // 用户按了 Ctrl+C → Canceled；HttpClient 自己超时 → Failed。
            // .part 一律留着，下次 -c 可以从这里接着下。
            // 走到这里时 FileStream 已经 dispose 过，磁盘上的长度就是真实断点。
            return cancellationToken.IsCancellationRequested
                ? new DownloadResult.Canceled(PartialFileLength(tempPath), totalBytes)
                : new DownloadResult.Failed("请求超时。");
        }
        catch (Exception ex)
        {
            // 网络错误、IO 错误等。同样留着 .part，能续多少算多少。
            return new DownloadResult.Failed(ex.Message);
        }
    }

    private static long GetResumeOffset(DownloadRequest request, string tempPath)
        => request.Resume ? PartialFileLength(tempPath) : 0;

    private static long PartialFileLength(string tempPath)
    {
        var partialFile = new FileInfo(tempPath);

        return partialFile.Exists ? partialFile.Length : 0;
    }

    private static long? ResolveTotalBytes(HttpResponseMessage response, long resumeFrom)
    {
        // 206 的 Content-Length 是「这一段」的大小，总大小在 Content-Range 里
        if (response.StatusCode == HttpStatusCode.PartialContent)
            return response.Content.Headers.ContentRange?.Length
                ?? (response.Content.Headers.ContentLength is { } remaining ? resumeFrom + remaining : null);

        return response.Content.Headers.ContentLength;
    }

    private static DownloadResult FinishFromPartialFile(
        HttpResponseMessage response,
        string tempPath,
        string outputPath,
        long resumeFrom)
    {
        // 416 有两种可能：断点文件恰好已经完整，或者远端文件变小了。
        // 416 响应里的 Content-Range 形如 "bytes */总大小"，拿它对一下就知道是哪种。
        var remoteTotal = response.Content.Headers.ContentRange?.Length;

        if (remoteTotal != resumeFrom)
        {
            return new DownloadResult.Failed(
                $"远端文件已经和本地断点对不上了（远端 {remoteTotal?.ToString() ?? "未知"} 字节，本地断点 {resumeFrom} 字节）。" +
                $"删掉 {tempPath} 再重下。");
        }

        File.Move(tempPath, outputPath, overwrite: true);

        return new DownloadResult.Completed(resumeFrom, 0);
    }

    private static async Task<long> CopyAsync(
        Stream input,
        Stream output,
        long bytesAlreadyOnDisk,
        long? totalBytes,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        var bytesReceived = bytesAlreadyOnDisk;

        // 续传时先报一次断点位置，进度条才会从该在的地方起步，而不是从 0% 跳过去
        if (bytesAlreadyOnDisk > 0)
            progress?.Report(new DownloadProgress(bytesReceived, totalBytes));

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;

            // 最后一块通常不满，只写实际读到的那么多
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

            bytesReceived += read;
            progress?.Report(new DownloadProgress(bytesReceived, totalBytes));
        }

        // bytesAlreadyOnDisk 是上一轮留下来的，不算这一次的传输量
        return bytesReceived - bytesAlreadyOnDisk;
    }
}
