using DndCompendium.Core.Models;

namespace DndCompendium.Core.Abstractions;

public interface IDocumentResourceStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ReplaceReferencesAsync(
        string documentId,
        IReadOnlyCollection<ExternalResourceReference> references,
        CancellationToken cancellationToken = default);
    Task<DocumentResourceManifest> GetManifestAsync(
        string documentId,
        CancellationToken cancellationToken = default);
    Task UpdateStateAsync(
        string documentId,
        string sourceUrl,
        DocumentResourceState state,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);
    Task StoreAsync(
        string documentId,
        string sourceUrl,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalDocumentResource>> ReadAvailableAsync(
        string documentId,
        CancellationToken cancellationToken = default);
    Task DeleteResourcesAsync(
        string documentId,
        CancellationToken cancellationToken = default);
}
