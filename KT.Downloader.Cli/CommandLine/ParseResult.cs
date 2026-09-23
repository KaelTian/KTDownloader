namespace KT.Downloader.Cli.CommandLine;

public abstract record ParseResult
{
    private ParseResult() { }

    public sealed record Success(CommandLineOptions Options) : ParseResult;

    public sealed record Help : ParseResult;

    public sealed record Invalid(string Message) : ParseResult;
}
