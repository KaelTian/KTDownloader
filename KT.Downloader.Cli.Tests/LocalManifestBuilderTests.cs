using System.Net;
using System.Text;
using KT.Downloader.Cli.Manifests;

namespace KT.Downloader.Cli.Tests;

public sealed class LocalManifestBuilderTests : IDisposable
{
    // 和你 IIS 上那个真实站点同一个形态：清单就放在站点根，根就是普通文件夹。
    private static readonly Uri ManifestUrl = new("http://192.168.0.189:9526/files.json");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ktdl-manifest-tests-" + Guid.NewGuid().ToString("N"));
    private readonly LocalManifestBuilder _builder = new();

    public LocalManifestBuilderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Scan_ReturnsPathsRelativeToTheRoot_WithForwardSlashes()
    {
        WriteFile("L1/D1/2026/202605/20260520/1.jpg", 128);

        var entry = Assert.Single(_builder.Scan(_root));

        Assert.Equal("L1/D1/2026/202605/20260520/1.jpg", entry.RelativePath);
    }

    [Fact]
    public void Scan_KeepsSpacesAndChineseInPaths()
    {
        WriteFile("download_test/Docker Desktop Installer.exe", 16);
        WriteFile("L1/D1/2026/条码-IRR.jpg", 8);

        var paths = _builder.Scan(_root).Select(entry => entry.RelativePath);

        Assert.Equal(
            ["L1/D1/2026/条码-IRR.jpg", "download_test/Docker Desktop Installer.exe"],
            paths);
    }

    [Fact]
    public void Scan_ReportsTheRealSizeOfEachFile()
    {
        WriteFile("4.jpg", 9739098);

        Assert.Equal(9739098, Assert.Single(_builder.Scan(_root)).Size);
    }

    [Fact]
    public void Scan_LeavesTheManifestFileOutOfItsOwnEntries()
    {
        // 清单是上一轮生成的、就躺在站点根。把自己列进去，下载端就会去下它自己。
        WriteFile(LocalManifestBuilder.DefaultFileName, 2048);
        WriteFile("1.jpg", 4);

        Assert.Equal("1.jpg", Assert.Single(_builder.Scan(_root)).RelativePath);
    }

    [Fact]
    public void Scan_OnlyLeavesOutTheManifestItself()
    {
        WriteFile(LocalManifestBuilder.DefaultFileName, 2);
        WriteFile("files.json.bak", 2);

        Assert.Equal("files.json.bak", Assert.Single(_builder.Scan(_root)).RelativePath);
    }

    [Fact]
    public void Scan_IsStableAcrossRuns()
    {
        WriteFile("b.zip", 1);
        WriteFile("a.zip", 1);
        WriteFile("nested/c.zip", 1);

        var first = LocalManifestBuilder.ToJson(_builder.Scan(_root));
        var second = LocalManifestBuilder.ToJson(_builder.Scan(_root));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Scan_ReturnsNothing_WhenTheRootIsEmpty()
    {
        Assert.Empty(_builder.Scan(_root));
    }

    [Fact]
    public void Scan_Fails_WhenTheRootIsNotThere()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => _builder.Scan(Path.Combine(_root, "并不存在的目录")));
    }

    // 这条是这个文件里最要紧的：生成端写出来的 JSON，加载端必须原样认得。
    // 两边一旦把字段名或路径语义改成不一致，只有这里会红。
    [Fact]
    public async Task ToJson_ProducesAManifestTheLoaderReadsBack()
    {
        WriteFile("L1/D1/2026/条码.jpg", 7);
        WriteFile("download_test/Docker Desktop Installer.exe", 11);

        var json = LocalManifestBuilder.ToJson(_builder.Scan(_root));
        var loader = new HttpRemoteManifestLoader(new HttpClient(
            StubHttpMessageHandler.Returns(
                HttpStatusCode.OK,
                new StringContent(json, Encoding.UTF8, "application/json"))));

        var entries = await loader.LoadAsync(ManifestUrl, CancellationToken.None);

        Assert.Equal(
            ["L1/D1/2026/条码.jpg", "download_test/Docker Desktop Installer.exe"],
            entries.Select(entry => entry.RelativePath));
        Assert.Equal([7L, 11L], entries.Select(entry => entry.Size));
        Assert.Equal(
            "http://192.168.0.189:9526/download_test/Docker%20Desktop%20Installer.exe",
            entries[1].Url.AbsoluteUri);
    }

    [Fact]
    public async Task ToJson_ProducesAnEmptyList_WhenThereIsNothingToDownload()
    {
        var json = LocalManifestBuilder.ToJson(_builder.Scan(_root));
        var loader = new HttpRemoteManifestLoader(new HttpClient(
            StubHttpMessageHandler.Returns(
                HttpStatusCode.OK,
                new StringContent(json, Encoding.UTF8, "application/json"))));

        Assert.Empty(await loader.LoadAsync(ManifestUrl, CancellationToken.None));
    }

    private void WriteFile(string relativePath, int sizeInBytes)
    {
        var fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, new byte[sizeInBytes]);
    }
}
