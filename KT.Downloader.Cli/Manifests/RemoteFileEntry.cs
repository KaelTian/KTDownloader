namespace KT.Downloader.Cli.Manifests;

// RelativePath 保持清单里的原样（镜像远端目录结构用），Url 已经解析成可直接下载的绝对地址。
public sealed record RemoteFileEntry(string RelativePath, Uri Url, long? Size);
