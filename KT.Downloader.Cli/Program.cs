using System.Text;
using KT.Downloader.Cli;
using KT.Downloader.Cli.Downloading;

Console.OutputEncoding = Encoding.UTF8;

using var httpClient = new HttpClient();
var downloader = new HttpFileDownloader(httpClient);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    // 第二次 Ctrl+C 不再拦截，留一个强制退出的出口
    if (cancellation.IsCancellationRequested)
        return;

    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await App.RunAsync(args, Console.Out, Console.Error, downloader, cancellation.Token);
