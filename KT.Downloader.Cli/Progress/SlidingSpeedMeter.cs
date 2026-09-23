namespace KT.Downloader.Cli.Progress;

public sealed class SlidingSpeedMeter
{
    private readonly List<(long TotalBytes, TimeSpan Timestamp)> _samples = [];
    private double _bytesPerSecond;

    public SlidingSpeedMeter(TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        Window = window;
    }

    public TimeSpan Window { get; }

    public double BytesPerSecond => _bytesPerSecond;

    public void AddSample(long totalBytes, TimeSpan timestamp)
    {
        _samples.Add((totalBytes, timestamp));

        // 窗口外的采样一律丢掉，它们会稀释当前速度
        while (_samples.Count > 0 && timestamp - _samples[0].Timestamp > Window)
            _samples.RemoveAt(0);

        // 只剩一条时算不出斜率，保留上一次的值，别抖回 0
        if (_samples.Count >= 2)
            _bytesPerSecond = ComputeSpeed();
    }

    private double ComputeSpeed()
    {
        var (oldestBytes, oldestAt) = _samples[0];
        var (newestBytes, newestAt) = _samples[^1];

        var elapsed = newestAt - oldestAt;

        return elapsed <= TimeSpan.Zero ? _bytesPerSecond : (newestBytes - oldestBytes) / elapsed.TotalSeconds;
    }
}
