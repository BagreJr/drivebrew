using DndCompendium.Core.Models;

namespace DndCompendium.Core.Abstractions;

public interface IDocumentSource
{
    Task<IReadOnlyList<SourceDocument>> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string documentId, CancellationToken cancellationToken = default);
}
