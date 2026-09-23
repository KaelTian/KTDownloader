using System.Diagnostics;
using KT.Downloader.Cli.CommandLine;
using KT.Downloader.Cli.Downloading;
using KT.Downloader.Cli.Progress;

namespace KT.Downloader.Cli;

public static class App
{
    private static readonly TimeSpan ProgressBarRenderInterval = TimeSpan.FromMilliseconds(100);

    public static Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IFileDownloader downloader,
        CancellationToken cancellationToken)
    {
        return CommandLineParser.Parse(args) switch
        {
            ParseResult.Success success => RunDownloadAsync(success.Options, output, error, downloader, cancellationToken),
            ParseResult.Help => Task.FromResult(ShowHelp(output)),
            ParseResult.Invalid invalid => Task.FromResult(ShowError(invalid.Message, error)),
            _ => throw new UnreachableException("解析结果里出现了未处理的类型。"),
        };
    }

    private static async Task<int> RunDownloadAsync(
        CommandLineOptions options,
        TextWriter output,
        TextWriter error,
        IFileDownloader downloader,
        CancellationToken cancellationToken)
    {
        var progressBar = new ConsoleProgressBar(output, TimeProvider.System, ProgressBarRenderInterval);
        var request = new DownloadRequest(options.Url, options.OutputPath, options.Continue);

        AnnounceBreakPoint(options, output);

        var result = await downloader.DownloadAsync(request, cancellationToken, progressBar);
        progressBar.Finish();

        return result switch
        {
            DownloadResult.Completed completed => ReportCompleted(options, completed, output),
            DownloadResult.Canceled canceled => ReportCanceled(options, canceled, error),
            DownloadResult.Failed failed => ReportFailed(failed, error),
            _ => throw new UnreachableException("下载结果里出现了未处理的类型。"),
        };
    }

    private static string PartialPathFor(CommandLineOptions options)
        => options.OutputPath + HttpFileDownloader.PartialFileSuffix;

    // 进度条开始重绘之前先滚一行出去，这样「这次是续传」不会一闪就被 \r 盖掉
    private static void AnnounceBreakPoint(CommandLineOptions options, TextWriter output)
    {
        if (!options.Continue)
            return;

        var partialFile = new FileInfo(PartialPathFor(options));

        if (!partialFile.Exists || partialFile.Length == 0)
            return;

        output.WriteLine(
            $"发现断点 {PartialPathFor(options)}（已有 {ProgressLine.FormatBytes(partialFile.Length)}），从这里继续下载。");
    }

    private static int ReportCompleted(CommandLineOptions options, DownloadResult.Completed completed, TextWriter output)
    {
        output.WriteLine(
            $"下载完成：{options.OutputPath}"
            + $"（共 {ProgressLine.FormatBytes(completed.BytesWritten)}，"
            + $"本次传输 {ProgressLine.FormatBytes(completed.BytesTransferred)}）");

        return ExitCodes.Success;
    }

    private static int ReportCanceled(CommandLineOptions options, DownloadResult.Canceled canceled, TextWriter error)
    {
        if (canceled.BytesOnDisk == 0)
        {
            error.WriteLine("已取消下载。");
            return ExitCodes.Canceled;
        }

        var total = canceled.TotalBytes is { } totalBytes
            ? $" / {ProgressLine.FormatBytes(totalBytes)}"
            : string.Empty;

        error.WriteLine(
            $"已取消下载。断点已保存：{PartialPathFor(options)}"
            + $"（{ProgressLine.FormatBytes(canceled.BytesOnDisk)}{total}）");

        error.WriteLine($"继续下载：ktdl -c \"{options.Url}\" \"{options.OutputPath}\"");

        return ExitCodes.Canceled;
    }

    private static int ReportFailed(DownloadResult.Failed failed, TextWriter error)
    {
        error.WriteLine($"错误：{failed.Message}");
        return ExitCodes.DownloadFailed;
    }

    private static int ShowHelp(TextWriter output)
    {
        output.WriteLine(HelpText.Usage);
        return ExitCodes.Success;
    }

    private static int ShowError(string message, TextWriter error)
    {
        error.WriteLine($"错误：{message}");
        error.WriteLine();
        error.WriteLine(HelpText.Usage);
        return ExitCodes.UsageError;
    }
}
