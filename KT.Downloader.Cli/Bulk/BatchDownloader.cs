using KT.Downloader.Cli.Downloading;
using KT.Downloader.Cli.Manifests;

namespace KT.Downloader.Cli.Bulk;

public sealed class BatchDownloader
{
    private readonly IFileDownloader _downloader;
    private readonly LocalPathMapper _mapper;

    public BatchDownloader(IFileDownloader downloader, LocalPathMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(mapper);

        _downloader = downloader;
        _mapper = mapper;
    }

    /// <param name="progress">字节级进度，用来画进度条。</param>
    /// <param name="fileFinished">
    /// 每个文件一收工就回调一次，带上它的最终结果。
    /// 光靠 progress 判断不出「这个文件完了」——失败的文件永远到不了 100%，
    /// 跳过的文件压根没有进度，所以完成状态只能由这里给。
    /// </param>
    public async Task<IReadOnlyList<BatchItemResult>> DownloadAsync(
        IReadOnlyList<RemoteFileEntry> entries,
        CancellationToken cancellationToken,
        IProgress<BatchProgress>? progress = null,
        IProgress<BatchItemResult>? fileFinished = null)
    {
        var results = new List<BatchItemResult>(entries.Count);

        for (var index = 0; index < entries.Count; index++)
        {
            // 取消只在开工之前看。已经下完的照旧留在 results 里，没轮到的一个都不碰。
            if (cancellationToken.IsCancellationRequested)
                break;

            var entry = entries[index];
            var localPath = _mapper.MapTo(entry.RelativePath);

            // 上一轮下全了的就别再来一遍 —— 清单里那个 660 MB 的文件重下一次是分钟级的浪费。
            // 清单没给大小就无从判断完整性，只能当它不完整。
            if (entry.Size is { } expectedSize && IsComplete(localPath, expectedSize))
            {
                Report(
                    new BatchItemResult(
                        index, entry, localPath, new DownloadResult.Completed(expectedSize, 0), Skipped: true));
            }
            else
            {
                var result = await _downloader.DownloadAsync(
                    new DownloadRequest(entry.Url, localPath, Resume: true),
                    cancellationToken,
                    progress is null ? null : new FileProgressRelay(progress, index, entries.Count, entry));

                // 单个文件的成败都记下来，循环不停 —— 一个 404 不该让剩下的十几个人干等着。
                Report(new BatchItemResult(index, entry, localPath, result));
            }
        }

        return results;

        void Report(BatchItemResult item)
        {
            results.Add(item);
            fileFinished?.Report(item);
        }
    }

    private static bool IsComplete(string localPath, long expectedSize)
    {
        var local = new FileInfo(localPath);

        return local.Exists && local.Length == expectedSize;
    }

    // 把单个文件的字节进度补上「这是第几个、属于哪个文件」再转发出去。
    //
    // 刻意不用 Progress<T>：它把回调异步投递出去，DownloadAsync 都返回了回调可能还没跑完，
    // 收集到的进度会残缺。这里 Report 直接同步转发。
    private sealed class FileProgressRelay(
        IProgress<BatchProgress> progress,
        int index,
        int count,
        RemoteFileEntry entry) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value)
            => progress.Report(new BatchProgress(index, count, entry, value));
    }
}
