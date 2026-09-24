using KT.Downloader.Cli.Bulk;
using KT.Downloader.Cli.Downloading;
using KT.Downloader.Cli.Manifests;

namespace KT.Downloader.Cli.Tests;

public sealed class BatchDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ktdl-batch-" + Guid.NewGuid().ToString("N"));

    public BatchDownloaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task DownloadAsync_PlacesEveryEntryUnderTheRoot_KeepingTheRemoteLayout()
    {
        var downloader = new RecordingFileDownloader(_ => new DownloadResult.Completed(10, 10));
        var batch = CreateBatch(downloader);

        await batch.DownloadAsync([Entry("a.jpg"), Entry("L1/D1/b.jpg")], CancellationToken.None);

        Assert.Equal(
            [Path.Combine(_root, "a.jpg"), Path.Combine(_root, "L1", "D1", "b.jpg")],
            downloader.Requests.Select(request => request.OutputPath));
    }

    [Fact]
    public async Task DownloadAsync_AsksForResume_SoARerunContinuesWhatIsAlreadyOnDisk()
    {
        var downloader = new RecordingFileDownloader(_ => new DownloadResult.Completed(10, 10));
        var batch = CreateBatch(downloader);

        await batch.DownloadAsync([Entry("a.jpg")], CancellationToken.None);

        Assert.All(downloader.Requests, request => Assert.True(request.Resume));
    }

    [Fact]
    public async Task DownloadAsync_ReturnsOneResultPerEntry_InTheOrderTheyWereGiven()
    {
        var downloader = new RecordingFileDownloader(_ => new DownloadResult.Completed(10, 10));
        var batch = CreateBatch(downloader);

        var results = await batch.DownloadAsync(
            [Entry("a.jpg"), Entry("b.jpg"), Entry("c.jpg")], CancellationToken.None);

        Assert.Equal(
            ["a.jpg", "b.jpg", "c.jpg"],
            results.Select(result => result.Entry.RelativePath));
    }

    [Fact]
    public async Task DownloadAsync_KeepsDownloading_WhenOneFileFails()
    {
        // 一个文件 404 不该让整批停在原地，后面的还得下
        var downloader = new RecordingFileDownloader(
            request => request.OutputPath.EndsWith("broken.jpg")
                ? new DownloadResult.Failed("远端没有这个文件。")
                : new DownloadResult.Completed(10, 10));
        var batch = CreateBatch(downloader);

        var results = await batch.DownloadAsync(
            [Entry("broken.jpg"), Entry("after.jpg")], CancellationToken.None);

        Assert.Equal(2, downloader.Requests.Count);
        Assert.IsType<DownloadResult.Failed>(results[0].Result);
        Assert.IsType<DownloadResult.Completed>(results[1].Result);
    }

    [Fact]
    public async Task DownloadAsync_ReportsTheLocalPath_SoTheCallerDoesNotHaveToMapItAgain()
    {
        var downloader = new RecordingFileDownloader(_ => new DownloadResult.Completed(10, 10));
        var batch = CreateBatch(downloader);

        var results = await batch.DownloadAsync([Entry("L1/D1/b.jpg")], CancellationToken.None);

        Assert.Equal(Path.Combine(_root, "L1", "D1", "b.jpg"), Assert.Single(results).LocalPath);
    }

    [Fact]
    public async Task DownloadAsync_TagsProgressWithTheFileItBelongsTo()
    {
        var downloader = new RecordingFileDownloader(_ => new DownloadResult.Completed(10, 10));
        var batch = CreateBatch(downloader);
        var reports = new List<BatchProgress>();

        await batch.DownloadAsync(
            [Entry("a.jpg"), Entry("b.jpg")],
            CancellationToken.None,
            new CollectingProgress<BatchProgress>(reports));

        Assert.Equal([0, 1], reports.Select(report => report.Index));
        Assert.All(reports, report => Assert.Equal(2, report.Count));
        Assert.Equal(["a.jpg", "b.jpg"], reports.Select(report => report.Entry.RelativePath));
    }

    [Fact]
    public async Task DownloadAsync_ReportsEachFile_AsSoonAsItFinishes()
    {
        // 完成状态不能靠「进度到没到 100%」去猜：失败的文件永远到不了 100%，
        // 跳过的文件压根没有进度。所以每个文件收工都得单独通知一次。
        var downloader = new RecordingFileDownloader(
            request => request.OutputPath.EndsWith("broken.jpg")
                ? new DownloadResult.Failed("远端没有这个文件。")
                : new DownloadResult.Completed(10, 10));
        var batch = CreateBatch(downloader);
        var finished = new List<BatchItemResult>();

        await batch.DownloadAsync(
            [Entry("broken.jpg"), Entry("after.jpg")],
            CancellationToken.None,
            fileFinished: new CollectingProgress<BatchItemResult>(finished));

        Assert.Equal([0, 1], finished.Select(item => item.Index));
        Assert.IsType<DownloadResult.Failed>(finished[0].Result);
        Assert.IsType<DownloadResult.Completed>(finished[1].Result);
    }

    [Fact]
    public async Task DownloadAsync_FinishesEachFile_BeforeStartingTheNextOne()
    {
        // 不是攒到最后一起报，而是下一个文件开工之前，上一个的状态就已经送达了
        var finished = new List<BatchItemResult>();
        var reportedWhenEachDownloadStarted = new List<int>();

        var downloader = new RecordingFileDownloader(_ =>
        {
            reportedWhenEachDownloadStarted.Add(finished.Count);
            return new DownloadResult.Completed(10, 10);
        });
        var batch = CreateBatch(downloader);

        await batch.DownloadAsync(
            [Entry("a.jpg"), Entry("b.jpg"), Entry("c.jpg")],
            CancellationToken.None,
            fileFinished: new CollectingProgress<BatchItemResult>(finished));

        Assert.Equal([0, 1, 2], reportedWhenEachDownloadStarted);
    }

    [Fact]
    public async Task DownloadAsync_ReportsSkippedFiles_Too()
    {
        await PlaceFileAsync("done.jpg", bytes: 7);

        var downloader = new RecordingFileDownloader(_ => new DownloadResult.Completed(10, 10));
        var batch = CreateBatch(downloader);
        var finished = new List<BatchItemResult>();

        await batch.DownloadAsync(
            [Entry("done.jpg", size: 7)],
            CancellationToken.None,
            fileFinished: new CollectingProgress<BatchItemResult>(finished));

        var item = Assert.Single(finished);
        Assert.True(item.Skipped);
        Assert.IsType<DownloadResult.Completed>(item.Result);
    }

    [Fact]
    public async Task DownloadAsync_StopsBeforeTheNextFile_WhenCanceled()
    {
        using var cancellation = new CancellationTokenSource();

        var downloader = new RecordingFileDownloader(_ =>
        {
            cancellation.Cancel();
            return new DownloadResult.Canceled(0, null);
        });
        var batch = CreateBatch(downloader);

        await batch.DownloadAsync(
            [Entry("a.jpg"), Entry("b.jpg"), Entry("c.jpg")], cancellation.Token);

        Assert.Single(downloader.Requests);
    }

    [Fact]
    public async Task DownloadAsync_DoesNothing_WhenTheManifestIsEmpty()
    {
        var downloader = new RecordingFileDownloader(_ => new DownloadResult.Completed(10, 10));
        var batch = CreateBatch(downloader);

        var results = await batch.DownloadAsync([], CancellationToken.None);

        Assert.Empty(results);
        Assert.Empty(downloader.Requests);
    }

    [Fact]
    public async Task DownloadAsync_SkipsAFileThatIsAlreadyOnDiskWithTheRightSize()
    {
        var entry = Entry("already-here.jpg", size: 11);
        await PlaceFileAsync("already-here.jpg", bytes: 11);

        var downloader = new RecordingFileDownloader(_ => new DownloadResult.Completed(11, 11));
        var batch = CreateBatch(downloader);

        var results = await batch.DownloadAsync([entry], CancellationToken.None);

        Assert.Empty(downloader.Requests);

        var result = Assert.Single(results);
        Assert.True(result.Skipped);

        var completed = Assert.IsType<DownloadResult.Completed>(result.Result);
        Assert.Equal(11, completed.BytesWritten);
        Assert.Equal(0, completed.BytesTransferred);
    }

    [Fact]
    public async Task DownloadAsync_DownloadsAgain_WhenTheLocalCopyHasTheWrongSize()
    {
        // 半个文件不能当完成，否则重跑一次就永远少一截
        var entry = Entry("half.jpg", size: 11);
        await PlaceFileAsync("half.jpg", bytes: 5);

        var downloader = new RecordingFileDownloader(_ => new DownloadResult.Completed(11, 11));
        var batch = CreateBatch(downloader);

        var results = await batch.DownloadAsync([entry], CancellationToken.None);

        Assert.Single(downloader.Requests);
        Assert.False(Assert.Single(results).Skipped);
    }

    [Fact]
    public async Task DownloadAsync_Downloads_WhenTheManifestDoesNotSayHowBigTheFileIs()
    {
        // 清单没给大小就判断不出完整性，宁可重下一遍
        var entry = Entry("unknown-size.jpg");
        await PlaceFileAsync("unknown-size.jpg", bytes: 11);

        var downloader = new RecordingFileDownloader(_ => new DownloadResult.Completed(11, 11));
        var batch = CreateBatch(downloader);

        await batch.DownloadAsync([entry], CancellationToken.None);

        Assert.Single(downloader.Requests);
    }

    [Fact]
    public async Task DownloadAsync_SkipsOnlyTheOnesAlreadyComplete_WhenTheBatchIsMixed()
    {
        await PlaceFileAsync("L1/done.jpg", bytes: 7);

        var downloader = new RecordingFileDownloader(_ => new DownloadResult.Completed(10, 10));
        var batch = CreateBatch(downloader);

        var results = await batch.DownloadAsync(
            [Entry("L1/done.jpg", size: 7), Entry("L1/todo.jpg", size: 9)],
            CancellationToken.None);

        Assert.Single(downloader.Requests);
        Assert.True(results[0].Skipped);
        Assert.False(results[1].Skipped);
    }

    private BatchDownloader CreateBatch(IFileDownloader downloader)
        => new(downloader, new LocalPathMapper(_root));

    private static RemoteFileEntry Entry(string relativePath, long? size = null)
        => new(relativePath, new Uri("http://192.168.0.189:9526/" + relativePath), size);

    private async Task PlaceFileAsync(string relativePath, int bytes)
    {
        var localPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await File.WriteAllBytesAsync(localPath, new byte[bytes]);
    }
}
