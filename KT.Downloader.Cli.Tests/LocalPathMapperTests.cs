using KT.Downloader.Cli.Bulk;

namespace KT.Downloader.Cli.Tests;

public sealed class LocalPathMapperTests
{
    private const string Root = @"D:\downloads";

    [Fact]
    public void MapTo_KeepsTheRemoteDirectoryStructure()
    {
        var mapper = new LocalPathMapper(Root);

        var local = mapper.MapTo("L1/D1/2026/202605/20260520/1.jpg");

        Assert.Equal(@"D:\downloads\L1\D1\2026\202605\20260520\1.jpg", local);
    }

    [Fact]
    public void MapTo_HandlesAPathWithoutAnyDirectory()
    {
        var mapper = new LocalPathMapper(Root);

        Assert.Equal(@"D:\downloads\软件测试界面.png", mapper.MapTo("软件测试界面.png"));
    }

    [Fact]
    public void MapTo_IgnoresATrailingSeparatorOnTheRoot()
    {
        var mapper = new LocalPathMapper(@"D:\downloads\");

        Assert.Equal(@"D:\downloads\a.jpg", mapper.MapTo("a.jpg"));
    }

    [Fact]
    public void MapTo_HandlesADriveRootAsTheTargetDirectory()
    {
        // 根就是 "D:\" 时，末尾那个分隔符是根的一部分，边界不能再补一个
        var mapper = new LocalPathMapper(@"D:\");

        Assert.Equal(@"D:\L1\a.jpg", mapper.MapTo("L1/a.jpg"));
    }

    [Fact]
    public void MapTo_AcceptsBackslashSeparators()
    {
        var mapper = new LocalPathMapper(Root);

        Assert.Equal(@"D:\downloads\L1\a.jpg", mapper.MapTo(@"L1\a.jpg"));
    }

    [Fact]
    public void MapTo_KeepsNamesThatLookLikeFileExtensions()
    {
        // 文件名里带点不是路径穿越，别被 .. 的检查误伤
        var mapper = new LocalPathMapper(Root);

        Assert.Equal(@"D:\downloads\archive..tar.gz", mapper.MapTo("archive..tar.gz"));
    }

    [Fact]
    public void MapTo_RejectsPathsThatClimbOutOfTheRoot()
    {
        var mapper = new LocalPathMapper(Root);

        Assert.Throws<InvalidDataException>(() => mapper.MapTo("../../../escape.exe"));
    }

    [Fact]
    public void MapTo_RejectsPathsThatClimbOutAndComeBackIn()
    {
        // 进去又出来：开头看着正常，落点却在根外面
        var mapper = new LocalPathMapper(Root);

        Assert.Throws<InvalidDataException>(() => mapper.MapTo("sub/../../escape.exe"));
    }

    [Fact]
    public void MapTo_RejectsAnAbsolutePath()
    {
        // Path.Combine 碰到绝对路径会把前面的根直接丢掉，这是最容易漏的一处
        var mapper = new LocalPathMapper(Root);

        Assert.Throws<InvalidDataException>(() => mapper.MapTo(@"C:\Windows\System32\evil.dll"));
    }

    [Fact]
    public void MapTo_RejectsARootRelativePath()
    {
        var mapper = new LocalPathMapper(Root);

        Assert.Throws<InvalidDataException>(() => mapper.MapTo(@"\Windows\System32\evil.dll"));
    }
}
