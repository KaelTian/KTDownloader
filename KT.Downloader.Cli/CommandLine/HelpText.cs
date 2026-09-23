namespace KT.Downloader.Cli.CommandLine;

public static class HelpText
{
    public const string Usage = """
        ktdl —— 终端下载管理器

        用法：
          ktdl [选项] <下载地址> <保存路径>

        参数：
          <下载地址>    http:// 或 https:// 开头的完整地址
          <保存路径>    文件保存到哪里，可以是相对路径

        选项：
          -c, --continue    接着上次没下完的进度继续下。没有断点就从头下
          -h, --help        显示本帮助

        示例：
          ktdl https://example.com/big.bin ./big.bin
          ktdl -c https://example.com/big.bin ./big.bin

        退出码：
          0    成功
          1    下载失败
          2    参数用法错误
          130  被 Ctrl+C 中断
        """;
}
