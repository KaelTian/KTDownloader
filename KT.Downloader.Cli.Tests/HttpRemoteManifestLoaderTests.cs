using System.Net;
using System.Text;
using KT.Downloader.Cli.Manifests;

namespace KT.Downloader.Cli.Tests;

public sealed class HttpRemoteManifestLoaderTests
{
    // 和你 IIS 上那个真实地址同一个形态：清单就放在站点根目录。
    private static readonly Uri ManifestUrl = new("http://192.168.0.189:9526/files.json");

    [Fact]
    public async Task LoadAsync_ResolvesEachEntryAgainstTheManifestDirectory()
    {
        var loader = CreateLoader("""
            { "files": [ { "path": "download_test/Docker Desktop Installer.exe" } ] }
            """);

        var entries = await loader.LoadAsync(ManifestUrl, CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal(
            "http://192.168.0.189:9526/download_test/Docker%20Desktop%20Installer.exe",
            entry.Url.AbsoluteUri);
    }

    [Fact]
    public async Task LoadAsync_KeepsTheRelativePath_SoTheLocalLayoutMirrorsTheRemoteOne()
    {
        var loader = CreateLoader("""
            { "files": [ { "path": "download_test/driver/setup.msi" } ] }
            """);

        var entries = await loader.LoadAsync(ManifestUrl, CancellationToken.None);

        Assert.Equal("download_test/driver/setup.msi", Assert.Single(entries).RelativePath);
    }

    [Fact]
    public async Task LoadAsync_ReadsSize_WhenTheManifestProvidesIt()
    {
        var loader = CreateLoader("""
            { "files": [ { "path": "download_test/app.exe", "size": 660773808 } ] }
            """);

        var entries = await loader.LoadAsync(ManifestUrl, CancellationToken.None);

        Assert.Equal(660773808, Assert.Single(entries).Size);
    }

    [Fact]
    public async Task LoadAsync_TreatsSizeAsOptional()
    {
        var loader = CreateLoader("""
            { "files": [ { "path": "download_test/app.exe" } ] }
            """);

        var entries = await loader.LoadAsync(ManifestUrl, CancellationToken.None);

        Assert.Null(Assert.Single(entries).Size);
    }

    [Fact]
    public async Task LoadAsync_KeepsTheOrderTheManifestLists()
    {
        var loader = CreateLoader("""
            { "files": [ { "path": "b.zip" }, { "path": "a.zip" }, { "path": "c.zip" } ] }
            """);

        var entries = await loader.LoadAsync(ManifestUrl, CancellationToken.None);

        Assert.Equal(["b.zip", "a.zip", "c.zip"], entries.Select(entry => entry.RelativePath));
    }

    [Fact]
    public async Task LoadAsync_SkipsBlankPaths()
    {
        var loader = CreateLoader("""
            { "files": [ { "path": "" }, { "path": "   " }, { "path": "a.zip" } ] }
            """);

        var entries = await loader.LoadAsync(ManifestUrl, CancellationToken.None);

        Assert.Equal("a.zip", Assert.Single(entries).RelativePath);
    }

    [Fact]
    public async Task LoadAsync_AcceptsBackslashSeparators()
    {
        // 清单是手写的，Windows 上很容易写成反斜杠。URL 里它必须是正斜杠。
        var loader = CreateLoader("""
            { "files": [ { "path": "download_test\\driver\\setup.msi" } ] }
            """);

        var entries = await loader.LoadAsync(ManifestUrl, CancellationToken.None);

        var entry = Assert.Single(entries);
        Assert.Equal("download_test/driver/setup.msi", entry.RelativePath);
        Assert.Equal("http://192.168.0.189:9526/download_test/driver/setup.msi", entry.Url.AbsoluteUri);
    }

    [Fact]
    public async Task LoadAsync_ResolvesAgainstTheManifestLocation_NotTheSiteRoot()
    {
        var loader = CreateLoader("""
            { "files": [ { "path": "a.zip" } ] }
            """);

        var entries = await loader.LoadAsync(
            new Uri("http://192.168.0.189:9526/download_test/files.json"), CancellationToken.None);

        Assert.Equal("http://192.168.0.189:9526/download_test/a.zip", Assert.Single(entries).Url.AbsoluteUri);
    }

    [Fact]
    public async Task LoadAsync_RejectsPathsThatEscapeTheManifestDirectory()
    {
        // 清单是远端给的，不能因为里面写了 ../ 就往目标目录外面落文件。
        var loader = CreateLoader("""
            { "files": [ { "path": "download_test/../../escape.exe" } ] }
            """);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => loader.LoadAsync(ManifestUrl, CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_RejectsAbsoluteUrlsInEntries()
    {
        // 相对路径是约定；混进绝对地址说明清单不可信，宁可报错。
        var loader = CreateLoader("""
            { "files": [ { "path": "http://evil.example/payload.exe" } ] }
            """);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => loader.LoadAsync(ManifestUrl, CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmpty_WhenTheManifestListsNoFiles()
    {
        var loader = CreateLoader("""{ "files": [] }""");

        var entries = await loader.LoadAsync(ManifestUrl, CancellationToken.None);

        Assert.Empty(entries);
    }

    [Fact]
    public async Task LoadAsync_Fails_WhenTheManifestIsNotThere()
    {
        var loader = CreateLoader("""{ "files": [] }""", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => loader.LoadAsync(ManifestUrl, CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_Fails_WhenTheManifestIsNotJson()
    {
        var loader = CreateLoader("<html>这不是清单，是 IIS 的错误页</html>");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => loader.LoadAsync(ManifestUrl, CancellationToken.None));
    }

    private static HttpRemoteManifestLoader CreateLoader(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return new HttpRemoteManifestLoader(
            new HttpClient(StubHttpMessageHandler.Returns(statusCode, content)));
    }
}
