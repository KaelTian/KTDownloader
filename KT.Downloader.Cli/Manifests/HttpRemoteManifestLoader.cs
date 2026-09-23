using System.Text.Json;
using System.Text.Json.Nodes;

namespace KT.Downloader.Cli.Manifests;

public sealed class HttpRemoteManifestLoader : IRemoteManifestLoader
{
    private readonly HttpClient _httpClient;

    public HttpRemoteManifestLoader(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<RemoteFileEntry>> LoadAsync(Uri manifestUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifestUrl);

        using HttpResponseMessage response = await _httpClient.GetAsync(
            manifestUrl, HttpCompletionOption.ResponseContentRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (JsonException ex)
        {
            // 拿到的大概率是 IIS 的错误页而不是清单，别把解析器的异常原样漏给调用方。
            throw new InvalidDataException("清单不是合法的 JSON。", ex);
        }

        if (root is null)
            throw new InvalidDataException("清单内容为空。");

        // 条目一律相对「清单所在目录」解析，这个目录同时也是逃逸判定的边界。
        var baseDirectory = new Uri(manifestUrl, ".");

        var result = new List<RemoteFileEntry>();

        foreach (JsonNode? node in root["files"]?.AsArray() ?? [])
        {
            string? path = node?["path"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(path))
                continue;

            // 清单是远端给的。绝对地址能决定请求打到哪台机器，.. 能决定文件落到哪个目录，
            // 这两样都不能由远端说了算。
            if (Uri.TryCreate(path, UriKind.Absolute, out _))
                throw new InvalidDataException($"清单里的条目必须是相对路径：{path}");

            string relative = path.Replace('\\', '/').TrimStart('/');

            if (relative.Split('/').Contains(".."))
                throw new InvalidDataException($"清单里的条目不能跳出清单所在目录：{path}");

            // 逐段编码：空格变 %20，中文变 %XX，但分隔符 / 要留着，否则整个路径会被压成一段。
            string escaped = string.Join('/',
                relative.Split('/').Select(Uri.EscapeDataString));

            long? size = node?["size"]?.GetValue<long>();

            result.Add(new RemoteFileEntry(relative, new Uri(baseDirectory, escaped), size));
        }

        return result;
    }
}
