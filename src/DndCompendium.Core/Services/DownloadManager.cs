using System.Collections.Concurrent;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.Core.Services;

/// <summary>
/// Coordinates source reads and durable local installation. The source contract does not expose
/// HTTP ranges, therefore resume restarts the transfer while keeping the pause/resume semantics
/// predictable. A future range-capable source can retain partial transfers behind the repository
/// contract without changing callers.
/// </summary>
public sealed class DownloadManager : IDownloadManager, IDisposable
{
    private readonly IDocumentSource _source;
    private readonly ILocalDocumentRepository _repository;
    private readonly IDocumentResourceManager? _resourceManager;
    private readonly ConcurrentDictionary<string, DownloadControl> _active = new(StringComparer.Ordinal);
    private bool _disposed;

    public DownloadManager(
        IDocumentSource source,
        ILocalDocumentRepository repository,
        IDocumentResourceManager? resourceManager = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _resourceManager = resourceManager;
    }

    public event EventHandler<DownloadJob>? ProgressChanged;

    public async Task InitializeLibraryAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _repository.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var catalog = await _source.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        await _repository.UpsertCatalogAsync(catalog, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentUpdateResult> UpdateInstalledDocumentsAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await InitializeLibraryAsync(cancellationToken).ConfigureAwait(false);
        var documents = await _repository.GetDocumentsAsync(cancellationToken).ConfigureAwait(false);
        var updates = documents
            .Where(document => document.HasLocalCopy
                && document.DownloadState == DocumentDownloadState.UpdateAvailable)
            .OrderBy(document => document.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var updated = new List<string>(updates.Length);
        var failed = new List<string>();

        foreach (var document in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DownloadInternalAsync(
                    document.Id,
                    synchronizeResources: true,
                    cancellationToken).ConfigureAwait(false);
                updated.Add(document.FileName);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                failed.Add(document.FileName);
            }
        }

        return new DocumentUpdateResult(updated, failed);
    }

    public Task DownloadAsync(string documentId, CancellationToken cancellationToken = default)
        => DownloadInternalAsync(documentId, synchronizeResources: true, cancellationToken);

    private async Task DownloadInternalAsync(
        string documentId,
        bool synchronizeResources,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        await _repository.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var localDocument = await _repository.GetDocumentAsync(documentId, cancellationToken).ConfigureAwait(false);
        if (localDocument is null)
        {
            await InitializeLibraryAsync(cancellationToken).ConfigureAwait(false);
            localDocument = await _repository.GetDocumentAsync(documentId, cancellationToken).ConfigureAwait(false);
        }

        if (localDocument is null)
        {
            throw new KeyNotFoundException($"Document '{documentId}' is not present in the source catalog.");
        }

        var control = new DownloadControl(cancellationToken);
        if (!_active.TryAdd(documentId, control))
        {
            control.Dispose();
            throw new InvalidOperationException($"A download for document '{documentId}' is already active.");
        }

        var sourceDocument = ToSourceDocument(localDocument);
        var hadLocalCopy = localDocument.HasLocalCopy;

        try
        {
            await ChangeStateAsync(
                sourceDocument,
                DocumentDownloadState.Queued,
                0,
                null,
                cancellationToken).ConfigureAwait(false);

            control.Token.ThrowIfCancellationRequested();
            await ChangeStateAsync(
                sourceDocument,
                DocumentDownloadState.Downloading,
                0,
                null,
                control.Token).ConfigureAwait(false);

            await using var content = await _source.OpenReadAsync(documentId, control.Token).ConfigureAwait(false);
            var progress = new SynchronousProgress(value =>
            {
                var normalized = Math.Clamp(value, 0, 1);
                control.LastProgress = normalized;
                ChangeStateAsync(
                        sourceDocument,
                        DocumentDownloadState.Downloading,
                        normalized,
                        null,
                        control.Token)
                    .GetAwaiter()
                    .GetResult();
            });

            await _repository.InstallAsync(sourceDocument, content, progress, control.Token).ConfigureAwait(false);
            control.LastProgress = 1;
            await ChangeStateAsync(
                sourceDocument,
                DocumentDownloadState.Downloaded,
                1,
                null,
                CancellationToken.None).ConfigureAwait(false);

            if (synchronizeResources && _resourceManager is not null)
            {
                try
                {
                    var installed = await _repository.ReadAsync(documentId, control.Token).ConfigureAwait(false);
                    if (installed is not null)
                    {
                        await _resourceManager.SynchronizeAsync(documentId, installed.SourceText, control.Token)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (control.Token.IsCancellationRequested)
                {
                    // The validated TXT remains installed. Its manifest stays pending and can be retried.
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException)
                {
                    // Resource failures are represented by the resource manifest and never invalidate the TXT.
                }
            }
        }
        catch (OperationCanceledException) when (control.PauseRequested)
        {
            await ChangeStateAsync(
                sourceDocument,
                DocumentDownloadState.Paused,
                control.LastProgress,
                null,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (control.CancelRequested)
        {
            var state = hadLocalCopy
                ? DocumentDownloadState.UpdateAvailable
                : DocumentDownloadState.NotDownloaded;
            await ChangeStateAsync(
                sourceDocument,
                state,
                0,
                null,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var state = hadLocalCopy
                ? DocumentDownloadState.UpdateAvailable
                : DocumentDownloadState.NotDownloaded;
            await ChangeStateAsync(
                sourceDocument,
                state,
                0,
                null,
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await ChangeStateAsync(
                sourceDocument,
                DocumentDownloadState.Error,
                control.LastProgress,
                exception.Message,
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _active.TryRemove(new KeyValuePair<string, DownloadControl>(documentId, control));
            control.Dispose();
        }
    }

    public async Task DownloadAllAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await InitializeLibraryAsync(cancellationToken).ConfigureAwait(false);
        var documents = await _repository.GetDocumentsAsync(cancellationToken).ConfigureAwait(false);

        // Install every TXT first. External image/font hosts are less reliable than Drive
        // and must not delay access to the remaining classes.
        foreach (var document in documents.Where(NeedsDownload))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await DownloadInternalAsync(
                    document.Id,
                    synchronizeResources: false,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // DownloadAsync already persisted the error for this document. Continue so a
                // single unavailable TXT or image host cannot stop the rest of the library.
            }
        }

        if (_resourceManager is null)
        {
            return;
        }

        // Once all readable documents are installed, complete their offline resources.
        // Each manifest records unavailable resources independently, so one bad host never
        // prevents the rest of the library from being attempted.
        var installedDocuments = await _repository.GetDocumentsAsync(cancellationToken).ConfigureAwait(false);
        using var resourceConcurrency = new SemaphoreSlim(3, 3);
        var resourceTasks = installedDocuments
            .Where(document => document.HasLocalCopy)
            .Select(async document =>
            {
                await resourceConcurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var installed = await _repository.ReadAsync(document.Id, cancellationToken).ConfigureAwait(false);
                    if (installed is not null)
                    {
                        await _resourceManager.SynchronizeAsync(
                            document.Id,
                            installed.SourceText,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    // Continue with the next manifest. Expected failures are already persisted by
                    // DocumentResourceManager and an unexpected per-document failure is isolated.
                }
                finally
                {
                    resourceConcurrency.Release();
                }
            });
        await Task.WhenAll(resourceTasks).ConfigureAwait(false);
    }

    public Task RetryAsync(string documentId, CancellationToken cancellationToken = default)
        => DownloadAsync(documentId, cancellationToken);

    public void Pause(string documentId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (_active.TryGetValue(documentId, out var control))
        {
            control.RequestPause();
        }
    }

    public Task ResumeAsync(string documentId, CancellationToken cancellationToken = default)
        => DownloadAsync(documentId, cancellationToken);

    public void Cancel(string documentId)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (_active.TryGetValue(documentId, out var control))
        {
            control.RequestCancel();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var control in _active.Values)
        {
            control.RequestCancel();
        }
    }

    private async Task ChangeStateAsync(
        SourceDocument document,
        DocumentDownloadState state,
        double progress,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var normalized = Math.Clamp(progress, 0, 1);
        await _repository.UpdateDownloadStateAsync(
            document.Id,
            state,
            normalized,
            errorMessage,
            cancellationToken).ConfigureAwait(false);

        var job = new DownloadJob
        {
            DocumentId = document.Id,
            Title = document.Title,
            State = state,
            Progress = normalized,
            BytesTransferred = (long)Math.Round(document.SizeBytes * normalized),
            TotalBytes = document.SizeBytes,
            ErrorMessage = errorMessage,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        RaiseProgressChanged(job);
    }

    private void RaiseProgressChanged(DownloadJob job)
    {
        var handlers = ProgressChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<DownloadJob> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, job);
            }
            catch
            {
                // A presentation-layer listener must not corrupt a durable transfer.
            }
        }
    }

    private static bool NeedsDownload(LibraryDocument document)
        => document.DownloadState is not DocumentDownloadState.Downloaded
            || !document.HasLocalCopy;

    private static SourceDocument ToSourceDocument(LibraryDocument document)
        => new()
        {
            Id = document.Id,
            Title = document.Title,
            FileName = document.FileName,
            Category = document.Category,
            Description = document.Description,
            SizeBytes = document.SourceSizeBytes,
            ModifiedAt = document.SourceModifiedAt,
            Checksum = document.SourceChecksum,
            Version = document.SourceVersion,
            IsVersionAuthoritative = true
        };

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed class DownloadControl : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;

        public DownloadControl(CancellationToken cancellationToken)
        {
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        public CancellationToken Token => _cancellation.Token;
        public bool PauseRequested { get; private set; }
        public bool CancelRequested { get; private set; }
        public double LastProgress { get; set; }

        public void RequestPause()
        {
            PauseRequested = true;
            _cancellation.Cancel();
        }

        public void RequestCancel()
        {
            CancelRequested = true;
            PauseRequested = false;
            _cancellation.Cancel();
        }

        public void Dispose() => _cancellation.Dispose();
    }
}
