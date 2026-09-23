namespace KT.Downloader.Cli.Bulk;

public sealed class LocalPathMapper
{
    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    private readonly string _rootDirectory;

    public LocalPathMapper(string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("根目录不能为空。", nameof(rootDirectory));

        // 去掉末尾多余的分隔符（但别把 "D:\" 退化成 "D:"）
        string trimmed = rootDirectory.TrimEnd(Separators);
        _rootDirectory = trimmed.EndsWith(Path.VolumeSeparatorChar) || trimmed.Length == 0
            ? rootDirectory
            : trimmed;
    }

    public string MapTo(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("相对路径不能为空。", nameof(relativePath));

        // 统一成分隔符，方便后面按段检查（远端清单里是 /，Windows 上可能是 \）
        string normalized = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        // 1) 绝对路径 / 根相对路径直接拒绝。
        //    Path.Combine 遇到这些会把根丢掉，所以必须在 Combine 之前拦下。
        if (Path.IsPathRooted(normalized))
            throw new InvalidDataException($"不允许绝对路径：{relativePath}");

        // 2) 按目录段检查穿越。只拒绝恰为 ".." 的段，
        //    文件名里带点（archive..tar.gz）不受影响。
        string[] segments = normalized.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (Array.Exists(segments, static s => s == ".."))
            throw new InvalidDataException($"路径越出根目录：{relativePath}");

        // 3) 手动用当前平台的分隔符拼接。
        //    Path.Combine 本身不会统一分隔符，直接 Combine 会得到
        //    "D:\downloads\L1/D1/..." 这种混合形态，字符串断言会失败。
        string relative = string.Join(Path.DirectorySeparatorChar, segments);

        // 4) 兜底：算出完整路径后确认仍落在根目录内（防掉 trailing 段之外的边角情况）。
        string rootFull = Path.GetFullPath(_rootDirectory);
        string full = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"路径越出根目录：{relativePath}");
        }

        return Path.Combine(_rootDirectory, relative);
    }
}