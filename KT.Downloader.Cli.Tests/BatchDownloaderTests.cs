using KT.Downloader.Cli.Bulk;
using KT.Downloader.Cli.Downloading;
using KT.Downloader.Cli.Manifests;

namespace KT.Downloader.Cli.Tests;

public sealed class BatchDownloaderTests
{
    private const string Root = @"D:\downloads";

    [Fact]
    public async Task DownloadAsync_PlacesEveryEntryUnderTheRoot_KeepingTheRemoteLayout()
    {
        var downloader = new RecordingFileDownloader(_ => new DownloadResult.Completed(10, 10));
        var batch = CreateBatch(downloader);

        await batch.DownloadAsync([Entry("a.jpg"), Entry("L1/D1/b.jpg")], CancellationToken.None);

        Assert.Equal(
            [@"D:\downloads\a.jpg", @"D:\downloads\L1\D1\b.jpg"],
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

        Assert.Equal(@"D:\downloads\L1\D1\b.jpg", Assert.Single(results).LocalPath);
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

    private static BatchDownloader CreateBatch(IFileDownloader downloader)
        => new(downloader, new LocalPathMapper(Root));

    private static RemoteFileEntry Entry(string relativePath, long? size = null)
        => new(relativePath, new Uri("http://192.168.0.189:9526/" + relativePath), size);
}
