namespace KT.Downloader.Cli.Manifests;

// 生成端的条目：只有相对路径和字节数。
// Url 是加载端（HttpRemoteManifestLoader）拿清单地址 + 相对路径才算得出来的东西，这里没有。
public sealed record ManifestFileEntry(string RelativePath, long Size);
