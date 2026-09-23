using System.Net;
using System.Text;
using KT.Downloader.Cli.Downloading;

namespace KT.Downloader.Cli.Tests;

public sealed class HttpFileDownloaderTests : IDisposable
{
    private const string FileName = "microsoft-copilot-7R3PqLcVnzQ-unsplash.jpg";

    private static readonly Uri AnyUrl = new("http://192.168.0.189:9526");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ktdl-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<HttpClient> _clients = [];

    public HttpFileDownloaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var client in _clients)
            client.Dispose();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task DownloadAsync_SavesContent_AndReportsByteCount()
    {
        var payload = Encoding.UTF8.GetBytes("hello ktdl");
        var downloader = CreateDownloader(StubHttpMessageHandler.Returns(HttpStatusCode.OK, new ByteArrayContent(payload)));
        var outputPath = PathFor(FileName);

        var result = await downloader.DownloadAsync(Request(outputPath), CancellationToken.None);

        var completed = Assert.IsType<DownloadResult.Completed>(result);
        Assert.Equal(payload.Length, completed.BytesWritten);
        Assert.Equal(payload, await File.ReadAllBytesAsync(outputPath));
        AssertNoPartialFileLeftBehind(outputPath);
    }

    [Fact]
    public async Task DownloadAsync_CreatesOutputDirectory_WhenItDoesNotExist()
    {
        var downloader = CreateDownloader(StubHttpMessageHandler.Returns(HttpStatusCode.OK, new ByteArrayContent([1, 2, 3])));
        var outputPath = Path.Combine(_root, "nested", "deep", FileName);

        var result = await downloader.DownloadAsync(Request(outputPath), CancellationToken.None);

        Assert.IsType<DownloadResult.Completed>(result);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task DownloadAsync_OverwritesExistingOutputFile()
    {
        var outputPath = PathFor(FileName);
        await File.WriteAllTextAsync(outputPath, "上一轮下载残留的旧内容");

        var payload = Encoding.UTF8.GetBytes("fresh content");
        var downloader = CreateDownloader(StubHttpMessageHandler.Returns(HttpStatusCode.OK, new ByteArrayContent(payload)));

        var result = await downloader.DownloadAsync(Request(outputPath), CancellationToken.None);

        Assert.IsType<DownloadResult.Completed>(result);
        Assert.Equal(payload, await File.ReadAllBytesAsync(outputPath));
    }

    [Fact]
    public async Task DownloadAsync_LeavesNothingBehind_WhenServerReturnsErrorStatus()
    {
        var downloader = CreateDownloader(StubHttpMessageHandler.Returns(HttpStatusCode.NotFound, new ByteArrayContent([1, 2, 3])));
        var outputPath = PathFor(FileName);

        var result = await downloader.DownloadAsync(Request(outputPath), CancellationToken.None);

        var failed = Assert.IsType<DownloadResult.Failed>(result);
        Assert.NotEmpty(failed.Message);
        AssertNoFilesLeftBehind(outputPath);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsFailed_WhenConnectionThrows()
    {
        var downloader = CreateDownloader(StubHttpMessageHandler.Throws(new HttpRequestException("名字解析失败")));
        var outputPath = PathFor(FileName);

        var result = await downloader.DownloadAsync(Request(outputPath), CancellationToken.None);

        Assert.IsType<DownloadResult.Failed>(result);
        AssertNoFilesLeftBehind(outputPath);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsFailed_WhenRequestTimesOut()
    {
        var handler = new StubHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        });
        var downloader = CreateDownloader(handler, requestTimeout: TimeSpan.FromMilliseconds(50));
        var outputPath = PathFor(FileName);

        var result = await downloader.DownloadAsync(Request(outputPath), CancellationToken.None);

        Assert.IsType<DownloadResult.Failed>(result);
        AssertNoFilesLeftBehind(outputPath);
    }

    [Fact]
    public async Task DownloadAsync_ReturnsCanceled_AndKeepsThePartialFile_WhenCanceledMidStream()
    {
        var gate = new GatedStream(new byte[256 * 1024]);
        var downloader = CreateDownloader(StubHttpMessageHandler.Returns(HttpStatusCode.OK, new StreamContent(gate)));
        var outputPath = PathFor(FileName);
        using var cts = new CancellationTokenSource();

        var downloadTask = downloader.DownloadAsync(Request(outputPath), cts.Token);
        await gate.FirstRead.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();

        var result = await downloadTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<DownloadResult.Canceled>(result);
        Assert.False(File.Exists(outputPath), "取消时不能留下最终文件。");
        Assert.True(File.Exists(PartialPathFor(outputPath)), "取消后要留着 .part，否则 -c 根本没法续。");
    }

    [Fact]
    public async Task DownloadAsync_ReportsAByteCount_ThatMatchesThePartialFile_WhenCanceled()
    {
        var gate = new GatedStream(new byte[256 * 1024]);
        var downloader = CreateDownloader(StubHttpMessageHandler.Returns(HttpStatusCode.OK, new StreamContent(gate)));
        var outputPath = PathFor(FileName);
        var partialPath = PartialPathFor(outputPath);
        using var cts = new CancellationTokenSource();

        var downloadTask = downloader.DownloadAsync(Request(outputPath), cts.Token);
        await gate.FirstRead.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(
            () => File.Exists(partialPath) && new FileInfo(partialPath).Length > 0,
            TimeSpan.FromSeconds(5));

        await cts.CancelAsync();

        var result = await downloadTask.WaitAsync(TimeSpan.FromSeconds(5));

        var canceled = Assert.IsType<DownloadResult.Canceled>(result);
        Assert.True(canceled.BytesOnDisk > 0, "明明已经落盘了却报 0，那用户看到的断点就是假的。");
        Assert.Equal(new FileInfo(partialPath).Length, canceled.BytesOnDisk);
    }

    [Fact]
    public async Task DownloadAsync_KeepsThePartialFile_WhenTheStreamDiesMidway()
    {
        var chunk = new byte[64 * 1024];
        var downloader = CreateDownloader(
            StubHttpMessageHandler.Returns(HttpStatusCode.OK, new StreamContent(new ChunkThenFailStream(chunk))));
        var outputPath = PathFor(FileName);

        var result = await downloader.DownloadAsync(Request(outputPath), CancellationToken.None);

        Assert.IsType<DownloadResult.Failed>(result);
        Assert.False(File.Exists(outputPath), "失败时不能把半成品当成最终文件。");
        Assert.Equal(chunk.Length, new FileInfo(PartialPathFor(outputPath)).Length);
    }

    [Fact]
    public async Task DownloadAsync_WritesBytesToDisk_WhileTheStreamIsStillOpen()
    {
        var gate = new GatedStream(new byte[256 * 1024]);
        var downloader = CreateDownloader(StubHttpMessageHandler.Returns(HttpStatusCode.OK, new StreamContent(gate)));
        var outputPath = PathFor(FileName);
        var partialPath = PartialPathFor(outputPath);
        using var cts = new CancellationTokenSource();

        var downloadTask = downloader.DownloadAsync(Request(outputPath), cts.Token);
        try
        {
            await gate.FirstRead.WaitAsync(TimeSpan.FromSeconds(5));

            await WaitUntilAsync(
                () => File.Exists(partialPath) && new FileInfo(partialPath).Length > 0,
                TimeSpan.FromSeconds(5));

            Assert.False(File.Exists(outputPath), "下载还没结束，不应该出现最终文件。");
        }
        finally
        {
            await cts.CancelAsync();
            await IgnoreFailureAsync(downloadTask);
        }
    }

    [Fact]
    public async Task DownloadAsync_ReportsProgress_ForEveryChunkWritten()
    {
        var payload = new byte[200 * 1024];
        var downloader = CreateDownloader(StubHttpMessageHandler.Returns(HttpStatusCode.OK, new ByteArrayContent(payload)));
        var reports = new List<DownloadProgress>();

        var result = await downloader.DownloadAsync(
            Request(PathFor(FileName)),
            CancellationToken.None,
            new CollectingProgress(reports));

        Assert.IsType<DownloadResult.Completed>(result);
        Assert.True(reports.Count > 1, "至少要报告不止一次，否则不算边下边报。");

        var received = reports.Select(report => report.BytesReceived).ToList();
        Assert.Equal(received.OrderBy(bytes => bytes), received);
        Assert.Equal(payload.Length, received[^1]);
        Assert.Equal(payload.Length, reports[^1].TotalBytes);
    }

    [Fact]
    public async Task DownloadAsync_MakesProgressOptional()
    {
        var downloader = CreateDownloader(StubHttpMessageHandler.Returns(HttpStatusCode.OK, new ByteArrayContent([1, 2, 3])));

        var result = await downloader.DownloadAsync(Request(PathFor(FileName)), CancellationToken.None);

        Assert.IsType<DownloadResult.Completed>(result);
    }

    [Fact]
    public async Task DownloadAsync_SendsARangeRequest_StartingWhereThePartialFileEnds()
    {
        var everything = Encoding.UTF8.GetBytes("0123456789ABCDEF");
        var outputPath = PathFor(FileName);
        await File.WriteAllBytesAsync(PartialPathFor(outputPath), everything[..7]);

        string? seenRange = null;
        var downloader = CreateDownloader(new StubHttpMessageHandler((request, _) =>
        {
            seenRange = request.Headers.Range?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([]),
            });
        }));

        await downloader.DownloadAsync(Request(outputPath, resume: true), CancellationToken.None);

        Assert.Equal("bytes=7-", seenRange);
    }

    [Fact]
    public async Task DownloadAsync_AppendsTheRest_SoTheFinalFileIsWhole()
    {
        var everything = Encoding.UTF8.GetBytes("0123456789ABCDEF");
        var outputPath = PathFor(FileName);
        await File.WriteAllBytesAsync(PartialPathFor(outputPath), everything[..10]);

        var downloader = CreateDownloader(StubHttpMessageHandler.ServingRangeAware(everything));

        var result = await downloader.DownloadAsync(Request(outputPath, resume: true), CancellationToken.None);

        Assert.IsType<DownloadResult.Completed>(result);
        Assert.Equal(everything, await File.ReadAllBytesAsync(outputPath));
        AssertNoPartialFileLeftBehind(outputPath);
    }

    [Fact]
    public async Task DownloadAsync_StartsOverFromScratch_WhenTheServerIgnoresTheRange()
    {
        var everything = Encoding.UTF8.GetBytes("0123456789ABCDEF");
        var outputPath = PathFor(FileName);
        await File.WriteAllBytesAsync(PartialPathFor(outputPath), everything[..10]);

        // 服务器不支持续传，要了 Range 也照样回 200 + 完整正文
        var downloader = CreateDownloader(StubHttpMessageHandler.ServingRangeAware(everything, honorRange: false));

        var result = await downloader.DownloadAsync(Request(outputPath, resume: true), CancellationToken.None);

        Assert.IsType<DownloadResult.Completed>(result);
        // 天真地追加会拼出 26 字节的垃圾；必须是干净的 16 字节
        Assert.Equal(everything, await File.ReadAllBytesAsync(outputPath));
    }

    [Fact]
    public async Task DownloadAsync_ReportsTheResumedOffset_SoTheBarStartsWhereItLeftOff()
    {
        var everything = new byte[1000];
        var outputPath = PathFor(FileName);
        await File.WriteAllBytesAsync(PartialPathFor(outputPath), everything[..400]);
        var reports = new List<DownloadProgress>();

        var downloader = CreateDownloader(StubHttpMessageHandler.ServingRangeAware(everything));

        await downloader.DownloadAsync(
            Request(outputPath, resume: true),
            CancellationToken.None,
            new CollectingProgress(reports));

        Assert.Equal(400, reports[0].BytesReceived);
        Assert.Equal(1000, reports[^1].BytesReceived);
        Assert.Equal(1000, reports[^1].TotalBytes);
    }

    [Fact]
    public async Task DownloadAsync_CountsOnlyTheBytesItTransferred_WhenResuming()
    {
        var everything = new byte[1000];
        var outputPath = PathFor(FileName);
        await File.WriteAllBytesAsync(PartialPathFor(outputPath), everything[..400]);

        var downloader = CreateDownloader(StubHttpMessageHandler.ServingRangeAware(everything));

        var result = await downloader.DownloadAsync(Request(outputPath, resume: true), CancellationToken.None);

        var completed = Assert.IsType<DownloadResult.Completed>(result);
        Assert.Equal(1000, completed.BytesWritten);
        Assert.Equal(600, completed.BytesTransferred);
    }

    [Fact]
    public async Task DownloadAsync_CountsTheWholePayload_WhenNothingWasResumed()
    {
        var payload = Encoding.UTF8.GetBytes("hello ktdl");
        var downloader = CreateDownloader(StubHttpMessageHandler.Returns(HttpStatusCode.OK, new ByteArrayContent(payload)));

        var result = await downloader.DownloadAsync(Request(PathFor(FileName)), CancellationToken.None);

        var completed = Assert.IsType<DownloadResult.Completed>(result);
        Assert.Equal(payload.Length, completed.BytesWritten);
        Assert.Equal(payload.Length, completed.BytesTransferred);
    }

    [Fact]
    public async Task DownloadAsync_JustFinishes_WhenThePartialFileIsAlreadyComplete()
    {
        var everything = Encoding.UTF8.GetBytes("0123456789ABCDEF");
        var outputPath = PathFor(FileName);
        await File.WriteAllBytesAsync(PartialPathFor(outputPath), everything);

        var downloader = CreateDownloader(StubHttpMessageHandler.ServingRangeAware(everything));

        var result = await downloader.DownloadAsync(Request(outputPath, resume: true), CancellationToken.None);

        var completed = Assert.IsType<DownloadResult.Completed>(result);
        Assert.Equal(0, completed.BytesTransferred);
        Assert.Equal(everything, await File.ReadAllBytesAsync(outputPath));
    }

    [Fact]
    public async Task DownloadAsync_Fails_WhenTheRemoteFileIsSmallerThanTheLocalPartialFile()
    {
        var remote = Encoding.UTF8.GetBytes("0123456789");
        var outputPath = PathFor(FileName);
        await File.WriteAllBytesAsync(PartialPathFor(outputPath), new byte[64]);

        var downloader = CreateDownloader(StubHttpMessageHandler.ServingRangeAware(remote));

        var result = await downloader.DownloadAsync(Request(outputPath, resume: true), CancellationToken.None);

        var failed = Assert.IsType<DownloadResult.Failed>(result);
        Assert.Contains("断点", failed.Message);
    }

    private static DownloadRequest Request(string outputPath, bool resume = false) => new(AnyUrl, outputPath, resume);

    private HttpFileDownloader CreateDownloader(StubHttpMessageHandler handler, TimeSpan? requestTimeout = null)
    {
        var client = new HttpClient(handler);

        if (requestTimeout is { } timeout)
            client.Timeout = timeout;

        _clients.Add(client);
        return new HttpFileDownloader(client);
    }

    private string PathFor(string fileName) => Path.Combine(_root, fileName);

    private static string PartialPathFor(string outputPath) => outputPath + HttpFileDownloader.PartialFileSuffix;

    private static void AssertNoFilesLeftBehind(string outputPath)
    {
        Assert.False(File.Exists(outputPath), $"不应该留下 {outputPath}");
        AssertNoPartialFileLeftBehind(outputPath);
    }

    private static void AssertNoPartialFileLeftBehind(string outputPath)
    {
        var partialPath = PartialPathFor(outputPath);
        Assert.False(File.Exists(partialPath), $"不应该留下 {partialPath}");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(25);
        }

        Assert.Fail($"等了 {timeout.TotalSeconds:0} 秒也没等到条件成立。");
    }

    private static async Task IgnoreFailureAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            // 清理阶段，不关心下载任务最后是成功还是失败。
        }
    }
}
