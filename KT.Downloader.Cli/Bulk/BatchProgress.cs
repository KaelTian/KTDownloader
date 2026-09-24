using KT.Downloader.Cli.Downloading;
using KT.Downloader.Cli.Manifests;

namespace KT.Downloader.Cli.Bulk;

// Index 从 0 开始；Download 是当前这个文件自己的字节进度。
public sealed record BatchProgress(int Index, int Count, RemoteFileEntry Entry, DownloadProgress Download);
