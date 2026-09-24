namespace KT.Downloader.Cli.Bulk;

public sealed class LocalPathMapper
{
    private readonly string _rootDirectory;

    public LocalPathMapper(string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(rootDirectory);
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("根目录不能为空。", nameof(rootDirectory));

        _rootDirectory = rootDirectory;
    }

    public string MapTo(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("相对路径不能为空。", nameof(relativePath));

        // Path.Combine 碰到绝对路径会把前面的根整个丢掉，所以必须在 Combine 之前拦下。
        if (Path.IsPathRooted(relativePath))
            throw new InvalidDataException($"不允许绝对路径：{relativePath}");

        // 末尾的分隔符要去掉，否则根是 "D:\downloads\" 时会拼出 "D:\downloads\\" 这样的边界，
        // 把根底下的正常路径全判成越界。根目录本身（"D:\"）不受影响，Trim 不会碰它。
        string rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_rootDirectory));

        // GetFullPath 一举多得：消解 ..、统一分隔符、转成绝对路径。
        // 所以越界判定只要比对最终结果，不用去猜输入长什么样。
        string full = Path.GetFullPath(Path.Combine(rootFull, relativePath));

        // 目标就是驱动器根（"D:\"）时，末尾那个分隔符本来就是根的一部分，不能再补一个。
        string boundary = Path.EndsInDirectorySeparator(rootFull)
            ? rootFull
            : rootFull + Path.DirectorySeparatorChar;

        if (!full.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"路径越出根目录：{relativePath}");

        return full;
    }
}
