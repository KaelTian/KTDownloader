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

        JsonNode? root = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken) ??
        throw new InvalidDataException("清单内容为空。"));

        var result = new List<RemoteFileEntry>();

        foreach (JsonNode? node in root!["files"]?.AsArray() ??
            Enumerable.Empty<JsonNode?>())
        {
            string? path = node?["path"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(path))
                continue;

            long? size = node?["size"]?.GetValue<long>();
            string relative = path.Replace('\\', '/').TrimStart('/');
            string escaped = string.Join('/',
            relative.Split('/').Select(Uri.EscapeDataString));

            result.Add(new RemoteFileEntry(relative, new Uri(manifestUrl, escaped), size));
        }

        return result;
    }
}
