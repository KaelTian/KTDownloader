using KT.Downloader.Cli.Downloading;
using KT.Downloader.Cli.Progress;

namespace KT.Downloader.Cli.Tests;

public sealed class ConsoleProgressBarTests
{
    private static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void Report_RedrawsInPlace_InsteadOfScrollingTheTerminal()
    {
        using var output = new StringWriter();
        var bar = CreateBar(output, new FakeTimeProvider());

        bar.Report(new DownloadProgress(500, 1000));

        var text = output.ToString();
        Assert.StartsWith("\r", text);
        Assert.DoesNotContain("\n", text);
    }

    [Fact]
    public void Report_Throttles_SoABurstOfUpdatesDoesNotFloodTheTerminal()
    {
        using var output = new StringWriter();
        var bar = CreateBar(output, new FakeTimeProvider());

        bar.Report(new DownloadProgress(100, 1000));
        bar.Report(new DownloadProgress(200, 1000));
        bar.Report(new DownloadProgress(300, 1000));

        Assert.Equal(1, CountRedraws(output.ToString()));
    }

    [Fact]
    public void Report_DrawsAgain_OnceTheRenderIntervalHasPassed()
    {
        using var output = new StringWriter();
        var clock = new FakeTimeProvider();
        var bar = CreateBar(output, clock);

        bar.Report(new DownloadProgress(100, 1000));
        clock.Advance(RenderInterval);
        bar.Report(new DownloadProgress(200, 1000));

        Assert.Equal(2, CountRedraws(output.ToString()));
    }

    [Fact]
    public void Report_ShowsPercentageAndSmoothedSpeed()
    {
        using var output = new StringWriter();
        var clock = new FakeTimeProvider();
        var bar = new ConsoleProgressBar(output, clock, renderInterval: TimeSpan.Zero, barWidth: 10);

        bar.Report(new DownloadProgress(0, 4096));
        clock.Advance(TimeSpan.FromSeconds(1));
        bar.Report(new DownloadProgress(2048, 4096));

        var text = output.ToString();
        Assert.Contains("50.0%", text);
        Assert.Contains("2.0 KB/s", text);
    }

    [Fact]
    public void Report_ShowsRemainingTime_OnceSpeedIsKnown()
    {
        using var output = new StringWriter();
        var clock = new FakeTimeProvider();
        var bar = new ConsoleProgressBar(output, clock, renderInterval: TimeSpan.Zero, barWidth: 10);

        bar.Report(new DownloadProgress(0, 4096));
        clock.Advance(TimeSpan.FromSeconds(1));
        bar.Report(new DownloadProgress(1024, 4096));

        // 还剩 3072 字节，速度 1024 B/s
        Assert.Contains("剩余 00:03", output.ToString());
    }

    [Fact]
    public void Report_AlwaysDrawsTheFinalFrame_EvenInsideTheThrottleWindow()
    {
        using var output = new StringWriter();
        var bar = CreateBar(output, new FakeTimeProvider());

        bar.Report(new DownloadProgress(900, 1000));
        bar.Report(new DownloadProgress(1000, 1000)); // 和上一次同一时刻，但不能被节流吃掉

        Assert.Equal(2, CountRedraws(output.ToString()));
        Assert.Contains("100.0%", output.ToString());
    }

    [Fact]
    public void Report_PadsShorterLines_SoNoLeftoversStayOnScreen()
    {
        using var output = new StringWriter();
        var clock = new FakeTimeProvider();
        var bar = new ConsoleProgressBar(output, clock, renderInterval: TimeSpan.Zero, barWidth: 10);

        bar.Report(new DownloadProgress(0, 4_000_000));
        clock.Advance(TimeSpan.FromSeconds(1));
        bar.Report(new DownloadProgress(1_000_000, 4_000_000)); // 976.6 KB/s，这一帧最长
        clock.Advance(TimeSpan.FromSeconds(1));
        bar.Report(new DownloadProgress(4_000_000, 4_000_000)); // 1.9 MB/s，反而变短了

        var frames = output.ToString().Split('\r', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(
            frames[^1].Length >= frames[^2].Length,
            $"最后一帧 {frames[^1].Length} 字符，比上一帧 {frames[^2].Length} 字符短，屏幕上会留残字。");
    }

    [Fact]
    public void Finish_EndsTheLine_WhenSomethingWasDrawn()
    {
        using var output = new StringWriter();
        var bar = CreateBar(output, new FakeTimeProvider());

        bar.Report(new DownloadProgress(500, 1000));
        bar.Finish();

        Assert.EndsWith(output.NewLine, output.ToString());
    }

    [Fact]
    public void Finish_WritesNothing_WhenNothingWasEverDrawn()
    {
        using var output = new StringWriter();
        var bar = CreateBar(output, new FakeTimeProvider());

        bar.Finish();

        Assert.Equal(string.Empty, output.ToString());
    }

    private static ConsoleProgressBar CreateBar(TextWriter output, TimeProvider clock)
        => new(output, clock, RenderInterval, barWidth: 10);

    private static int CountRedraws(string text) => text.Count(character => character == '\r');
}
