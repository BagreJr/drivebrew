using DndCompendium.Core.Models;

namespace DndCompendium.Core.Abstractions;

public interface IDocumentResourceManager
{
    Task<DocumentResourceManifest> SynchronizeAsync(
        string documentId,
        string sourceText,
        CancellationToken cancellationToken = default);
    Task<DocumentResourceManifest> GetManifestAsync(
        string documentId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalDocumentResource>> ReadAvailableAsync(
        string documentId,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(string documentId, CancellationToken cancellationToken = default);
}
