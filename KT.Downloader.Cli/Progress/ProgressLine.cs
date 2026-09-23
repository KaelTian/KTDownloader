using System.Globalization;
using KT.Downloader.Cli.Downloading;

namespace KT.Downloader.Cli.Progress;

public static class ProgressLine
{
    public const int DefaultBarWidth = 30;

    private const long Kilobyte = 1024;
    private const long Megabyte = Kilobyte * 1024;
    private const long Gigabyte = Megabyte * 1024;

    public static string Render(
        DownloadProgress progress,
        double bytesPerSecond,
        TimeSpan? remaining,
        int barWidth = DefaultBarWidth)
    {
        var parts = new List<string>(capacity: 4) { DrawBar(progress, barWidth) };

        if (progress.TotalBytes is { } total)
            parts.Add(FormatPercent(progress.BytesReceived, total));

        parts.Add(FormatSpeed(bytesPerSecond));

        if (remaining is { } countdown)
            parts.Add($"剩余 {FormatDuration(countdown)}");

        return string.Join("  ", parts);
    }

    public static string FormatSpeed(double bytesPerSecond)
        => bytesPerSecond switch
        {
            >= Gigabyte => Format(bytesPerSecond / Gigabyte, "GB/s"),
            >= Megabyte => Format(bytesPerSecond / Megabyte, "MB/s"),
            >= Kilobyte => Format(bytesPerSecond / Kilobyte, "KB/s"),
            _ => Format(bytesPerSecond, "B/s", decimals: 0),
        };

    public static string FormatBytes(long bytes)
        => bytes switch
        {
            >= Gigabyte => Format(bytes / (double)Gigabyte, "GB"),
            >= Megabyte => Format(bytes / (double)Megabyte, "MB"),
            >= Kilobyte => Format(bytes / (double)Kilobyte, "KB"),
            _ => Format(bytes, "B", decimals: 0),
        };

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        return duration.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}",
                (int)duration.TotalHours, duration.Minutes, duration.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}",
                (int)duration.TotalMinutes, duration.Seconds);
    }

    private static string DrawBar(DownloadProgress progress, int barWidth)
    {
        var fraction = 0.0;

        if (progress.TotalBytes is { } total && total > 0)
            fraction = Math.Clamp((double)progress.BytesReceived / total, 0, 1);

        var filled = (int)(barWidth * fraction);

        return filled >= barWidth
            ? $"[{new string('=', barWidth)}]"
            : $"[{new string('=', filled)}>{new string(' ', barWidth - filled - 1)}]";
    }

    private static string FormatPercent(long bytesReceived, long totalBytes)
    {
        var percent = totalBytes <= 0
            ? 100.0
            : Math.Clamp((double)bytesReceived / totalBytes * 100, 0, 100);

        return string.Format(CultureInfo.InvariantCulture, "{0:0.0}%", percent);
    }

    private static string Format(double value, string unit, int decimals = 1)
        => string.Format(CultureInfo.InvariantCulture, "{0:F" + decimals + "} {1}", value, unit);
}
