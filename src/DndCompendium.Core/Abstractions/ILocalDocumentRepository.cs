using DndCompendium.Core.Models;

namespace DndCompendium.Core.Abstractions;

public interface ILocalDocumentRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task UpsertCatalogAsync(IEnumerable<SourceDocument> documents, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LibraryDocument>> GetDocumentsAsync(CancellationToken cancellationToken = default);
    Task<LibraryDocument?> GetDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LibraryDocument>> SearchAsync(string query, CancellationToken cancellationToken = default);
    Task InstallAsync(SourceDocument document, Stream content, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
    Task<LocalDocumentContent?> ReadAsync(string documentId, CancellationToken cancellationToken = default);
    Task DeleteLocalCopyAsync(string documentId, CancellationToken cancellationToken = default);
    Task UpdateDownloadStateAsync(string documentId, DocumentDownloadState state, double progress = 0, string? errorMessage = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DownloadJob>> GetDownloadJobsAsync(CancellationToken cancellationToken = default);
    Task<long> GetStorageUsageAsync(CancellationToken cancellationToken = default);
}
