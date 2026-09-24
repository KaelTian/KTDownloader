using System.Text.Json;

namespace KT.Downloader.Cli.Manifests;

// 扫描一个本地目录，生成 HttpRemoteManifestLoader 认得的清单 —— 是列表那一端的反面。
// 用 IIS 手工搭站点时，这里要选的就是站点根（站点根就是普通文件夹）。
public sealed class LocalManifestBuilder
{
    public const string DefaultFileName = "files.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public IReadOnlyList<ManifestFileEntry> Scan(string rootDirectory, string manifestFileName = DefaultFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestFileName);

        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var root = new DirectoryInfo(rootFull);

        if (!root.Exists)
            throw new DirectoryNotFoundException($"目录不存在：{rootFull}");

        var manifestFull = Path.Combine(rootFull, manifestFileName);
        var entries = new List<ManifestFileEntry>();

        foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            // 清单自己就在根目录下。扫进去的话下载端会去下它自己，而它每次生成都在变。
            if (string.Equals(file.FullName, manifestFull, StringComparison.OrdinalIgnoreCase))
                continue;

            // 正斜杠 + 相对清单所在目录：这两个约定是为了对上加载端的解析规则。
            var relative = Path.GetRelativePath(rootFull, file.FullName).Replace('\\', '/');

            entries.Add(new ManifestFileEntry(relative, file.Length));
        }

        // 按序号排序。同一个目录重跑多次生成的清单字节一致，改了哪个文件一眼能从 diff 看出来。
        entries.Sort(static (left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));

        return entries;
    }

    public static string ToJson(IReadOnlyList<ManifestFileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var document = new ManifestDocument(
            [.. entries.Select(entry => new ManifestItem(entry.RelativePath, entry.Size))]);

        return JsonSerializer.Serialize(document, JsonOptions);
    }

    // 靠命名策略（CamelCase）把属性名落成 files / path / size，省掉一堆 JsonPropertyName。
    private sealed record ManifestDocument(IReadOnlyList<ManifestItem> Files);

    private sealed record ManifestItem(string Path, long Size);
}
