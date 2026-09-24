using KT.Downloader.Cli.Downloading;
using KT.Downloader.Cli.Manifests;

namespace KT.Downloader.Cli.Bulk;

// Index 是这一条在批次里的位置，调用方（比如 UI 的列表）可以直接拿它对上第几行。
// LocalPath 是它最终落到的本地路径，省得调用方自己再映射一遍。
// Skipped 为 true 表示本地已经有完整副本，这次压根没碰网络（此时 Result 是传输 0 字节的 Completed）。
public sealed record BatchItemResult(
    int Index,
    RemoteFileEntry Entry,
    string LocalPath,
    DownloadResult Result,
    bool Skipped = false);
