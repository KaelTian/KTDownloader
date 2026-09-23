using KT.Downloader.Cli.Downloading;

namespace KT.Downloader.Cli.Progress;

public sealed class ConsoleProgressBar : IProgress<DownloadProgress>
{
    private static readonly TimeSpan SpeedWindow = TimeSpan.FromSeconds(2);

    private readonly long _startTimestamp;
    private readonly SlidingSpeedMeter _speedMeter = new(SpeedWindow);
    private TimeSpan _lastRenderAt;
    private int _lastLineLength;

    public ConsoleProgressBar(
        TextWriter output,
        TimeProvider clock,
        TimeSpan renderInterval,
        int barWidth = ProgressLine.DefaultBarWidth)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(clock);

        Output = output;
        Clock = clock;
        RenderInterval = renderInterval;
        BarWidth = barWidth;
        _startTimestamp = clock.GetTimestamp();
    }

    public TextWriter Output { get; }
    public TimeProvider Clock { get; }
    public TimeSpan RenderInterval { get; }
    public int BarWidth { get; }

    public void Report(DownloadProgress value)
    {
        var elapsed = Clock.GetElapsedTime(_startTimestamp);
        _speedMeter.AddSample(value.BytesReceived, elapsed);

        // 收尾那一帧必须画出来，否则进度条会永远停在 99.6% 这种数字上
        var isFinalFrame = value.TotalBytes is { } totalBytes && value.BytesReceived >= totalBytes;

        if (_lastLineLength > 0 && !isFinalFrame && elapsed - _lastRenderAt < RenderInterval)
            return;

        _lastRenderAt = elapsed;

        var bytesPerSecond = _speedMeter.BytesPerSecond;
        var line = ProgressLine.Render(value, bytesPerSecond, EstimateRemaining(value, bytesPerSecond), BarWidth);

        Output.Write('\r');
        Output.Write(line);

        // 上一次画得更长的话，尾巴上会留下残字，用空格盖掉
        if (_lastLineLength > line.Length)
            Output.Write(new string(' ', _lastLineLength - line.Length));

        _lastLineLength = line.Length;
    }

    public void Finish()
    {
        if (_lastLineLength == 0)
            return;

        Output.WriteLine();
    }

    private static TimeSpan? EstimateRemaining(DownloadProgress progress, double bytesPerSecond)
    {
        if (progress.TotalBytes is not { } totalBytes || bytesPerSecond <= 0)
            return null;

        var secondsRemaining = (totalBytes - progress.BytesReceived) / bytesPerSecond;

        return secondsRemaining <= 0 ? null : TimeSpan.FromSeconds(secondsRemaining);
    }
}
