using DndCompendium.Core.Models;

namespace DndCompendium.Core.Abstractions;

public interface IDownloadManager
{
    event EventHandler<DownloadJob>? ProgressChanged;

    Task InitializeLibraryAsync(CancellationToken cancellationToken = default);
    Task<DocumentUpdateResult> UpdateInstalledDocumentsAsync(CancellationToken cancellationToken = default);
    Task DownloadAsync(string documentId, CancellationToken cancellationToken = default);
    Task DownloadAllAsync(CancellationToken cancellationToken = default);
    Task RetryAsync(string documentId, CancellationToken cancellationToken = default);
    void Pause(string documentId);
    Task ResumeAsync(string documentId, CancellationToken cancellationToken = default);
    void Cancel(string documentId);
}
