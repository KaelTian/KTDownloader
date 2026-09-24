using KT.Downloader.Cli.Downloading;
using KT.Downloader.Cli.Manifests;

namespace KT.Downloader.Cli.Bulk;

// 带上 LocalPath 是为了让调用方（比如 UI）不用自己再映射一遍就知道文件落在哪。
public sealed record BatchItemResult(RemoteFileEntry Entry, string LocalPath, DownloadResult Result);
