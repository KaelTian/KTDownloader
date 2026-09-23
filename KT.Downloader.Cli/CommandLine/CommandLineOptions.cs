namespace KT.Downloader.Cli.CommandLine;

public sealed record CommandLineOptions(Uri Url, string OutputPath, bool Continue);
