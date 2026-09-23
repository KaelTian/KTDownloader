using KT.Downloader.Cli.CommandLine;

namespace KT.Downloader.Cli.Tests;

public sealed class CommandLineParserTests
{
    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Parse_ReturnsHelp_WhenHelpFlagIsPresent(string flag)
    {
        var result = CommandLineParser.Parse([flag]);

        Assert.IsType<ParseResult.Help>(result);
    }

    [Fact]
    public void Parse_ReturnsHelp_WhenHelpFlagFollowsPositionalArguments()
    {
        var result = CommandLineParser.Parse(["https://example.com/a.bin", "a.bin", "--help"]);

        Assert.IsType<ParseResult.Help>(result);
    }

    [Fact]
    public void Parse_ReturnsInvalid_WhenNoArgumentsGiven()
    {
        var result = CommandLineParser.Parse([]);

        var invalid = Assert.IsType<ParseResult.Invalid>(result);
        Assert.Contains("缺少参数", invalid.Message);
    }

    [Fact]
    public void Parse_ReturnsInvalid_WhenArgumentsAreTooFew()
    {
        var result = CommandLineParser.Parse(["https://example.com/a.bin"]);

        var invalid = Assert.IsType<ParseResult.Invalid>(result);
        Assert.Contains("参数不够", invalid.Message);
    }

    [Fact]
    public void Parse_ReturnsInvalid_WhenArgumentsAreTooMany()
    {
        var result = CommandLineParser.Parse(["https://example.com/a.bin", "a.bin", "extra.bin"]);

        var invalid = Assert.IsType<ParseResult.Invalid>(result);
        Assert.Contains("参数太多", invalid.Message);
    }

    [Fact]
    public void Parse_ReturnsInvalid_WhenUnknownOptionIsGiven()
    {
        var result = CommandLineParser.Parse(["https://example.com/a.bin", "a.bin", "-x"]);

        var invalid = Assert.IsType<ParseResult.Invalid>(result);
        Assert.Contains("-x", invalid.Message);
    }

    [Fact]
    public void Parse_ReturnsSuccess_WithUrlAndOutputPath()
    {
        var result = CommandLineParser.Parse(["https://example.com/a.bin", "./downloads/a.bin"]);

        var success = Assert.IsType<ParseResult.Success>(result);
        Assert.Equal("https://example.com/a.bin", success.Options.Url.ToString());
        Assert.Equal("./downloads/a.bin", success.Options.OutputPath);
    }

    [Fact]
    public void Parse_ReturnsSuccess_WithResumeFalse_ByDefault()
    {
        var result = CommandLineParser.Parse(["https://example.com/a.bin", "a.bin"]);

        var success = Assert.IsType<ParseResult.Success>(result);
        Assert.False(success.Options.Continue);
    }

    [Theory]
    [InlineData("-c")]
    [InlineData("--continue")]
    public void Parse_ReturnsSuccess_WithResumeTrue_WhenContinueFlagIsGiven(string flag)
    {
        var result = CommandLineParser.Parse([flag, "https://example.com/a.bin", "a.bin"]);

        var success = Assert.IsType<ParseResult.Success>(result);
        Assert.True(success.Options.Continue);
    }

    [Fact]
    public void Parse_AcceptsTheContinueFlag_AfterThePositionalArguments()
    {
        var result = CommandLineParser.Parse(["https://example.com/a.bin", "a.bin", "-c"]);

        var success = Assert.IsType<ParseResult.Success>(result);
        Assert.True(success.Options.Continue);
        Assert.Equal("a.bin", success.Options.OutputPath);
    }

    [Theory]
    [InlineData("ftp://example.com/a.bin")]
    [InlineData("file:///c:/a.bin")]
    public void Parse_ReturnsInvalid_WhenSchemeIsNotHttpOrHttps(string url)
    {
        var result = CommandLineParser.Parse([url, "a.bin"]);

        var invalid = Assert.IsType<ParseResult.Invalid>(result);
        Assert.Contains("http://", invalid.Message);
    }

    [Fact]
    public void Parse_ReturnsInvalid_WhenUrlIsNotAbsolute()
    {
        var result = CommandLineParser.Parse(["not a url", "a.bin"]);

        Assert.IsType<ParseResult.Invalid>(result);
    }

    [Fact]
    public void Parse_ReturnsInvalid_WhenOutputPathIsBlank()
    {
        var result = CommandLineParser.Parse(["https://example.com/a.bin", "   "]);

        var invalid = Assert.IsType<ParseResult.Invalid>(result);
        Assert.Contains("保存路径", invalid.Message);
    }
}
