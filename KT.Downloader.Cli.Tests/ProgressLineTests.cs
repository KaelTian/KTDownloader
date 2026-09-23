using KT.Downloader.Cli.Downloading;
using KT.Downloader.Cli.Progress;

namespace KT.Downloader.Cli.Tests;

public sealed class ProgressLineTests
{
    private const int BarWidth = 10;

    [Theory]
    [InlineData(0, "[>         ]")]
    [InlineData(500, "[=====>    ]")]
    [InlineData(1000, "[==========]")]
    public void Render_DrawsTheBar(long received, string expectedBar)
    {
        var line = Render(received, total: 1000);

        Assert.StartsWith(expectedBar, line);
    }

    [Fact]
    public void Render_KeepsTheBarWidthConstant_AllTheWayThrough()
    {
        foreach (var received in new long[] { 0, 1, 250, 999, 1000 })
        {
            var line = Render(received, total: 1000);
            var bar = line[..(line.IndexOf(']') + 1)];

            Assert.Equal(BarWidth + 2, bar.Length);
        }
    }

    [Fact]
    public void Render_ClampsTheBar_WhenMoreBytesArriveThanContentLengthPromised()
    {
        var line = Render(received: 1500, total: 1000);

        Assert.StartsWith("[==========]", line);
        Assert.Contains("100.0%", line);
    }

    [Fact]
    public void Render_ShowsThePercentage()
    {
        var line = Render(received: 452, total: 1000);

        Assert.Contains("45.2%", line);
    }

    [Fact]
    public void Render_ShowsTheSmoothedSpeed()
    {
        var line = ProgressLine.Render(
            new DownloadProgress(500, 1000),
            bytesPerSecond: 1.5 * 1024 * 1024,
            remaining: null,
            barWidth: BarWidth);

        Assert.Contains("1.5 MB/s", line);
    }

    [Fact]
    public void Render_ShowsRemainingTime()
    {
        var line = ProgressLine.Render(
            new DownloadProgress(500, 1000),
            bytesPerSecond: 1024,
            remaining: TimeSpan.FromSeconds(12),
            barWidth: BarWidth);

        Assert.Contains("剩余 00:12", line);
    }

    [Fact]
    public void Render_OmitsPercentageAndRemainingTime_WhenContentLengthIsUnknown()
    {
        var line = ProgressLine.Render(
            new DownloadProgress(500, null),
            bytesPerSecond: 1024,
            remaining: null,
            barWidth: BarWidth);

        Assert.Contains("1.0 KB/s", line);
        Assert.DoesNotContain("%", line);
        Assert.DoesNotContain("剩余", line);
    }

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(512, "512 B/s")]
    [InlineData(1024, "1.0 KB/s")]
    [InlineData(1536, "1.5 KB/s")]
    [InlineData(1048576, "1.0 MB/s")]
    [InlineData(1572864, "1.5 MB/s")]
    [InlineData(1073741824, "1.0 GB/s")]
    public void FormatSpeed_UsesTheLargestUnitThatFits(double bytesPerSecond, string expected)
    {
        Assert.Equal(expected, ProgressLine.FormatSpeed(bytesPerSecond));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(62914560, "60.0 MB")]
    [InlineData(158209336, "150.9 MB")]
    public void FormatBytes_UsesTheLargestUnitThatFits(long bytes, string expected)
    {
        Assert.Equal(expected, ProgressLine.FormatBytes(bytes));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(12, "00:12")]
    [InlineData(65, "01:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "01:00:00")]
    [InlineData(3725, "01:02:05")]
    public void FormatDuration_UsesMinutesUntilTheHourRollsOver(int seconds, string expected)
    {
        Assert.Equal(expected, ProgressLine.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }

    private static string Render(long received, long? total)
        => ProgressLine.Render(new DownloadProgress(received, total), bytesPerSecond: 0, remaining: null, barWidth: BarWidth);
}
