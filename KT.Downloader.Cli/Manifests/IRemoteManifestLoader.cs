namespace KT.Downloader.Cli.Manifests;

public interface IRemoteManifestLoader
{
    Task<IReadOnlyList<RemoteFileEntry>> LoadAsync(Uri manifestUrl, CancellationToken cancellationToken);
}
