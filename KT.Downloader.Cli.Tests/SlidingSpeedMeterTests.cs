using KT.Downloader.Cli.Progress;

namespace KT.Downloader.Cli.Tests;

public sealed class SlidingSpeedMeterTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    [Fact]
    public void Constructor_Throws_WhenWindowIsNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingSpeedMeter(TimeSpan.Zero));
    }

    [Fact]
    public void BytesPerSecond_IsZero_BeforeAnySample()
    {
        var meter = new SlidingSpeedMeter(Window);

        Assert.Equal(0, meter.BytesPerSecond);
    }

    [Fact]
    public void BytesPerSecond_IsZero_WithASingleSample()
    {
        var meter = new SlidingSpeedMeter(Window);

        meter.AddSample(totalBytes: 4096, timestamp: TimeSpan.FromSeconds(1));

        Assert.Equal(0, meter.BytesPerSecond);
    }

    [Fact]
    public void BytesPerSecond_IsZero_WhenEverySampleSharesTheSameTimestamp()
    {
        var meter = new SlidingSpeedMeter(Window);

        meter.AddSample(totalBytes: 0, timestamp: TimeSpan.FromSeconds(1));
        meter.AddSample(totalBytes: 4096, timestamp: TimeSpan.FromSeconds(1));

        Assert.Equal(0, meter.BytesPerSecond);
    }

    [Fact]
    public void BytesPerSecond_IsTheRateAcrossWhateverTheWindowStillHolds()
    {
        var meter = new SlidingSpeedMeter(Window);

        meter.AddSample(totalBytes: 0, timestamp: TimeSpan.Zero);
        meter.AddSample(totalBytes: 2000, timestamp: TimeSpan.FromSeconds(1));
        meter.AddSample(totalBytes: 6000, timestamp: TimeSpan.FromSeconds(3));

        // 0 秒那次已被挤出窗口，只剩 1 秒到 3 秒：4000 字节 / 2 秒
        Assert.Equal(2000, meter.BytesPerSecond);
    }

    [Fact]
    public void BytesPerSecond_ForgetsSamplesThatFellOutOfTheWindow()
    {
        var meter = new SlidingSpeedMeter(Window);

        meter.AddSample(totalBytes: 0, timestamp: TimeSpan.Zero);
        meter.AddSample(totalBytes: 1000, timestamp: TimeSpan.FromSeconds(10));
        meter.AddSample(totalBytes: 201_000, timestamp: TimeSpan.FromSeconds(11));

        // 0 秒那次要是没被忘掉，答案会被稀释成 18272 B/s
        Assert.Equal(200_000, meter.BytesPerSecond);
    }

    [Fact]
    public void BytesPerSecond_SmoothsABriefBurst_InsteadOfJumpingToIt()
    {
        var meter = new SlidingSpeedMeter(Window);

        meter.AddSample(totalBytes: 0, timestamp: TimeSpan.Zero);
        meter.AddSample(totalBytes: 20_000, timestamp: TimeSpan.FromSeconds(1));
        meter.AddSample(totalBytes: 40_000, timestamp: TimeSpan.FromSeconds(2));
        meter.AddSample(totalBytes: 1_040_000, timestamp: TimeSpan.FromSeconds(2.1));

        // 最后 100 毫秒涌进来 1MB，瞬时值高达 10MB/s
        Assert.InRange(meter.BytesPerSecond, 0, 2_000_000);
    }

    [Fact]
    public void BytesPerSecond_KeepsThePreviousValue_WhenTheWindowHoldsOnlyOneSample()
    {
        var meter = new SlidingSpeedMeter(Window);

        meter.AddSample(totalBytes: 0, timestamp: TimeSpan.Zero);
        meter.AddSample(totalBytes: 2000, timestamp: TimeSpan.FromSeconds(1));
        var beforeTheStall = meter.BytesPerSecond;

        // 中间卡了 10 秒没有任何采样，窗口里只剩它自己
        meter.AddSample(totalBytes: 2000, timestamp: TimeSpan.FromSeconds(11));

        Assert.Equal(beforeTheStall, meter.BytesPerSecond);
    }
}
