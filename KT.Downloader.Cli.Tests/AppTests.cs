using KT.Downloader.Cli.Downloading;

namespace KT.Downloader.Cli.Tests;

public sealed class AppTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ktdl-app-tests-" + Guid.NewGuid().ToString("N"));

    public AppTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string PathFor(string fileName) => Path.Combine(_root, fileName);

    private static string PartialPathFor(string outputPath) => outputPath + HttpFileDownloader.PartialFileSuffix;

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string[] args,
        IFileDownloader? downloader = null,
        CancellationToken cancellationToken = default)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await App.RunAsync(
            args,
            output,
            error,
            downloader ?? new StubFileDownloader(new DownloadResult.Canceled(0, null)),
            cancellationToken);

        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task RunAsync_PrintsUsageToStdout_AndExitsZero_WhenHelpRequested()
    {
        var (exitCode, output, error) = await RunAsync(["--help"]);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("用法", output);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public async Task RunAsync_PrintsMessageAndUsageToStderr_AndExitsTwo_WhenArgumentsAreInvalid()
    {
        var (exitCode, output, error) = await RunAsync(["https://example.com/a.bin"]);

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Empty(output);
        Assert.Contains("错误：", error);
        Assert.Contains("用法", error);
    }

    [Fact]
    public async Task RunAsync_DownloadsToParsedTarget_AndPrintsByteCount_WhenDownloadSucceeds()
    {
        var downloader = new StubFileDownloader(new DownloadResult.Completed(1048576, 1048576));

        var (exitCode, output, error) = await RunAsync(["https://example.com/a.bin", "./downloads/a.bin"], downloader);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("https://example.com/a.bin", downloader.LastRequest?.Url.ToString());
        Assert.Equal("./downloads/a.bin", downloader.LastRequest?.OutputPath);
        Assert.False(downloader.LastRequest?.Resume);
        Assert.Contains("./downloads/a.bin", output);
        Assert.Contains("1.0 MB", output);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public async Task RunAsync_PassesCancellationTokenThrough_ToTheDownloader()
    {
        var downloader = new StubFileDownloader(new DownloadResult.Completed(0, 0));
        using var cts = new CancellationTokenSource();

        await RunAsync(["https://example.com/a.bin", "./a.bin"], downloader, cts.Token);

        Assert.Equal(cts.Token, downloader.LastCancellationToken);
    }

    [Fact]
    public async Task RunAsync_PassesTheResumeFlagThrough_WhenContinueWasRequested()
    {
        var downloader = new StubFileDownloader(new DownloadResult.Completed(1, 1));

        await RunAsync(["-c", "https://example.com/a.bin", PathFor("a.bin")], downloader);

        Assert.True(downloader.LastRequest?.Resume);
    }

    [Fact]
    public async Task RunAsync_HandsTheDownloader_AProgressSink()
    {
        var downloader = new StubFileDownloader(new DownloadResult.Completed(1, 1));

        await RunAsync(["https://example.com/a.bin", "./a.bin"], downloader);

        Assert.NotNull(downloader.LastProgress);
    }

    [Fact]
    public async Task RunAsync_ExitsOne_AndPrintsReasonToStderr_WhenDownloadFails()
    {
        var downloader = new StubFileDownloader(new DownloadResult.Failed("服务器返回 404。"));

        var (exitCode, _, error) = await RunAsync(["https://example.com/a.bin", "./a.bin"], downloader);

        Assert.Equal(ExitCodes.DownloadFailed, exitCode);
        Assert.Contains("服务器返回 404。", error);
    }

    [Fact]
    public async Task RunAsync_Exits130_AndPrintsCancelNotice_WhenDownloadIsCanceled()
    {
        var downloader = new StubFileDownloader(new DownloadResult.Canceled(0, null));

        var (exitCode, _, error) = await RunAsync(["https://example.com/a.bin", "./a.bin"], downloader);

        Assert.Equal(ExitCodes.Canceled, exitCode);
        Assert.Contains("取消", error);
        Assert.DoesNotContain("-c", error);
    }

    [Fact]
    public async Task RunAsync_AnnouncesTheResumePoint_WhenThePartialFileIsThere()
    {
        var outputPath = PathFor("a.bin");
        await File.WriteAllBytesAsync(PartialPathFor(outputPath), new byte[4096]);
        var downloader = new StubFileDownloader(new DownloadResult.Completed(8192, 4096));

        var (_, output, _) = await RunAsync(["-c", "https://example.com/a.bin", outputPath], downloader);

        Assert.Contains(PartialPathFor(outputPath), output);
        Assert.Contains("4.0 KB", output);
    }

    [Fact]
    public async Task RunAsync_StaysQuietAboutResuming_WhenThereIsNoPartialFile()
    {
        var outputPath = PathFor("a.bin");
        var downloader = new StubFileDownloader(new DownloadResult.Completed(8192, 8192));

        var (_, output, _) = await RunAsync(["-c", "https://example.com/a.bin", outputPath], downloader);

        Assert.DoesNotContain(PartialPathFor(outputPath), output);
    }

    [Fact]
    public async Task RunAsync_StaysQuietAboutResuming_WhenContinueWasNotAskedFor()
    {
        var outputPath = PathFor("a.bin");
        await File.WriteAllBytesAsync(PartialPathFor(outputPath), new byte[4096]);
        var downloader = new StubFileDownloader(new DownloadResult.Completed(8192, 8192));

        var (_, output, _) = await RunAsync(["https://example.com/a.bin", outputPath], downloader);

        Assert.DoesNotContain(PartialPathFor(outputPath), output);
    }

    [Fact]
    public async Task RunAsync_ShowsBothTotals_SoYouCanSeeHowMuchWasActuallyPulled()
    {
        var downloader = new StubFileDownloader(new DownloadResult.Completed(158209336, 95294776));

        var (_, output, _) = await RunAsync(["https://example.com/a.bin", PathFor("a.bin")], downloader);

        Assert.Contains("150.9 MB", output);
        Assert.Contains("90.9 MB", output);
    }

    [Fact]
    public async Task RunAsync_TellsWhereTheBreakIs_AndHowToResume_WhenCanceled()
    {
        var outputPath = PathFor("a.bin");
        var downloader = new StubFileDownloader(new DownloadResult.Canceled(62914560, 158209336));

        var (exitCode, _, error) = await RunAsync(["https://example.com/a.bin", outputPath], downloader);

        Assert.Equal(ExitCodes.Canceled, exitCode);
        Assert.Contains(PartialPathFor(outputPath), error);
        Assert.Contains("60.0 MB", error);
        Assert.Contains("150.9 MB", error);
        Assert.Contains("-c", error);
        Assert.Contains("https://example.com/a.bin", error);
    }
}
