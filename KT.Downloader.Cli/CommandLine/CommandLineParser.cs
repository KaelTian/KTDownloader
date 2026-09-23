namespace KT.Downloader.Cli.CommandLine;

public static class CommandLineParser
{
    private const int RequiredArgumentCount = 2;

    public static ParseResult Parse(string[] args)
    {
        if (args.Length == 0)
            return new ParseResult.Invalid("缺少参数：需要提供下载地址和保存路径。");

        var positional = new List<string>(RequiredArgumentCount);
        var resume = false;

        foreach (var arg in args)
        {
            if (IsHelpFlag(arg))
                return new ParseResult.Help();

            if (IsContinueFlag(arg))
            {
                resume = true;
                continue;
            }

            if (arg.StartsWith('-'))
                return new ParseResult.Invalid($"未知参数：\"{arg}\"。");

            positional.Add(arg);
        }

        return positional.Count == RequiredArgumentCount
            ? BuildOptions(positional[0], positional[1], resume)
            : new ParseResult.Invalid(DescribeArgumentCount(positional.Count));
    }

    private static ParseResult BuildOptions(string rawUrl, string rawOutputPath, bool resume)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var url))
            return new ParseResult.Invalid($"下载地址不是合法的 URL：\"{rawUrl}\"。");

        if (url.Scheme is not ("http" or "https"))
            return new ParseResult.Invalid($"下载地址必须以 http:// 或 https:// 开头，当前是 \"{url.Scheme}://\"。");

        if (string.IsNullOrEmpty(url.Host))
            return new ParseResult.Invalid($"下载地址里没有主机名：\"{rawUrl}\"。");

        if (string.IsNullOrWhiteSpace(rawOutputPath))
            return new ParseResult.Invalid("保存路径不能为空。");

        return new ParseResult.Success(new CommandLineOptions(url, rawOutputPath, resume));
    }

    private static bool IsHelpFlag(string arg) => arg is "-h" or "--help";

    private static bool IsContinueFlag(string arg) => arg is "-c" or "--continue";

    private static string DescribeArgumentCount(int actual) => actual < RequiredArgumentCount
        ? $"参数不够：需要下载地址和保存路径，实际只给了 {actual} 个。"
        : $"参数太多：只需要下载地址和保存路径，实际给了 {actual} 个。";
}
