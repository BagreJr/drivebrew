using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using System.Net.Sockets;

namespace DndCompendium.Core.Services;

public sealed class DocumentResourceManager : IDocumentResourceManager
{
    public const long DefaultMaximumDocumentResourceBytes = 64L * 1024 * 1024;
    public static readonly TimeSpan UnavailableResourceRetryDelay = TimeSpan.FromHours(6);

    private readonly IExternalResourceExtractor _extractor;
    private readonly IExternalResourceDownloader _downloader;
    private readonly IDocumentResourceStore _store;
    private readonly long _maximumDocumentResourceBytes;

    public DocumentResourceManager(
        IExternalResourceExtractor extractor,
        IExternalResourceDownloader downloader,
        IDocumentResourceStore store,
        long maximumDocumentResourceBytes = DefaultMaximumDocumentResourceBytes)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _maximumDocumentResourceBytes = maximumDocumentResourceBytes > 0
            ? maximumDocumentResourceBytes
            : throw new ArgumentOutOfRangeException(nameof(maximumDocumentResourceBytes));
    }

    public async Task<DocumentResourceManifest> SynchronizeAsync(
        string documentId,
        string sourceText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(sourceText);

        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var references = _extractor.Extract(sourceText);
        await _store.ReplaceReferencesAsync(documentId, references, cancellationToken).ConfigureAwait(false);

        var manifest = await _store.GetManifestAsync(documentId, cancellationToken).ConfigureAwait(false);
        var storedBytes = manifest.Resources
            .Where(resource => resource.State == DocumentResourceState.Available)
            .Sum(resource => resource.ByteLength);

        var retryBefore = DateTimeOffset.UtcNow - UnavailableResourceRetryDelay;
        foreach (var resource in manifest.Resources.Where(resource =>
                     resource.State != DocumentResourceState.Available &&
                     (resource.State != DocumentResourceState.Unavailable || resource.UpdatedAt <= retryBefore)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _store.UpdateStateAsync(
                documentId,
                resource.SourceUrl,
                DocumentResourceState.Downloading,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            try
            {
                var download = await _downloader.DownloadAsync(
                    new ExternalResourceReference(resource.SourceUrl, resource.Kind, resource.IsRequired),
                    cancellationToken).ConfigureAwait(false);
                if (storedBytes + download.Content.LongLength > _maximumDocumentResourceBytes)
                {
                    throw new InvalidDataException(
                        $"Document resources exceed the {_maximumDocumentResourceBytes}-byte offline limit.");
                }

                await _store.StoreAsync(
                    documentId,
                    resource.SourceUrl,
                    download.MimeType,
                    download.Content,
                    cancellationToken).ConfigureAwait(false);
                storedBytes += download.Content.LongLength;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await _store.UpdateStateAsync(
                    documentId,
                    resource.SourceUrl,
                    DocumentResourceState.Pending,
                    "Resource synchronization was canceled.",
                    CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (OperationCanceledException exception)
            {
                // HttpClient reports a request timeout as OperationCanceledException even
                // though the caller did not cancel the bulk operation. Isolate that host and
                // continue with the next resource.
                await _store.UpdateStateAsync(
                    documentId,
                    resource.SourceUrl,
                    DocumentResourceState.Unavailable,
                    Truncate(exception.Message, 500),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsExpectedResourceFailure(exception))
            {
                await _store.UpdateStateAsync(
                    documentId,
                    resource.SourceUrl,
                    DocumentResourceState.Unavailable,
                    Truncate(exception.Message, 500),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        return await _store.GetManifestAsync(documentId, cancellationToken).ConfigureAwait(false);
    }

    public Task<DocumentResourceManifest> GetManifestAsync(
        string documentId,
        CancellationToken cancellationToken = default)
        => _store.GetManifestAsync(documentId, cancellationToken);

    public Task<IReadOnlyList<LocalDocumentResource>> ReadAvailableAsync(
        string documentId,
        CancellationToken cancellationToken = default)
        => _store.ReadAvailableAsync(documentId, cancellationToken);

    public Task DeleteAsync(string documentId, CancellationToken cancellationToken = default)
        => _store.DeleteResourcesAsync(documentId, cancellationToken);

    private static bool IsExpectedResourceFailure(Exception exception)
        => exception is HttpRequestException
            or SocketException
            or IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or NotSupportedException;

    private static string Truncate(string value, int maximumLength)
        => value.Length <= maximumLength ? value : value[..maximumLength];
}
