using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using DndCompendium.App.Services;
using Microsoft.Maui.ApplicationModel;

namespace DndCompendium.App.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly ILocalDocumentRepository _repository;
    private readonly IDownloadManager _downloadManager;
    private readonly IDocumentResourceManager _resourceManager;
    private readonly ISpellRepository _spellRepository;
    private readonly IExploitRepository _exploitRepository;
    private readonly IReadingHistoryStore _readingHistory;
    private readonly ContentUpdateStatus _contentUpdates;
    private readonly List<LibraryDocumentItemViewModel> _allDocuments = [];
    private bool _initialized;

    public LibraryViewModel(
        ILocalDocumentRepository repository,
        IDownloadManager downloadManager,
        IDocumentResourceManager resourceManager,
        ISpellRepository spellRepository,
        IExploitRepository exploitRepository,
        IReadingHistoryStore readingHistory,
        ContentUpdateStatus contentUpdates)
    {
        _repository = repository;
        _downloadManager = downloadManager;
        _resourceManager = resourceManager;
        _spellRepository = spellRepository;
        _exploitRepository = exploitRepository;
        _readingHistory = readingHistory;
        _contentUpdates = contentUpdates;
        _downloadManager.ProgressChanged += OnDownloadProgressChanged;
        _contentUpdates.Changed += OnContentUpdateChanged;
    }

    public ObservableCollection<LibraryDocumentItemViewModel> Documents { get; } = [];
    public ObservableCollection<RecentDocumentItemViewModel> RecentDocuments { get; } = [];

    public bool HasDocuments => Documents.Count > 0;
    public bool HasRecentDocuments => RecentDocuments.Count > 0;
    public bool IsContentStatusChecking => _contentUpdates.State == ContentUpdateState.Checking;
    public bool IsContentCurrent => _contentUpdates.State == ContentUpdateState.Current;
    public bool HasContentUpdateProblem => _contentUpdates.State == ContentUpdateState.Problem;
    public string ContentStatusText => _contentUpdates.Label;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string ResultSummary => Documents.Count switch
    {
        0 => "No hay documentos para mostrar",
        1 => "1 documento",
        _ => $"{Documents.Count} documentos"
    };

    public string BulkDownloadText => IsBulkDownloading ? "Descargando…" : "Descargar todo";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BulkDownloadText))]
    private bool isBulkDownloading;

    [ObservableProperty]
    private string searchQuery = string.Empty;

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await _repository.InitializeAsync();

            try
            {
                await _downloadManager.InitializeLibraryAsync();
            }
            catch (OperationCanceledException)
            {
                // A transient Android lifecycle/network cancellation is harmless: the
                // locally persisted catalog remains complete and readable.
            }
            catch (Exception exception)
            {
                // A remote/catalog refresh must never make the local library unusable.
                ErrorMessage = $"La biblioteca local está disponible, pero no se pudo actualizar: {exception.Message}";
            }

            await ReloadDocumentsAsync();
            _initialized = true;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo abrir la biblioteca local: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RefreshAsync()
    {
        if (!_initialized)
        {
            await LoadAsync();
            return;
        }

        IsRefreshing = true;
        ErrorMessage = null;

        try
        {
            await _downloadManager.InitializeLibraryAsync();
            await ReloadDocumentsAsync();
        }
        catch (OperationCanceledException)
        {
            await ReloadDocumentsAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo actualizar la vista: {exception.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task DownloadAsync(LibraryDocumentItemViewModel? document)
    {
        if (document is null)
        {
            return;
        }

        ErrorMessage = null;

        try
        {
            if (document.DownloadState is DocumentDownloadState.Error)
            {
                await _downloadManager.RetryAsync(document.Id);
            }
            else
            {
                await _downloadManager.DownloadAsync(document.Id);
            }

            await ReloadDocumentsAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo descargar «{document.Title}»: {exception.Message}";
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task DownloadAllAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        IsBulkDownloading = true;

        try
        {
            await _downloadManager.DownloadAllAsync();
            await _spellRepository.GetAllAsync(refreshFromDrive: true);
            await _exploitRepository.GetAllAsync(refreshFromDrive: true);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo completar la descarga de la biblioteca: {exception.Message}";
        }
        finally
        {
            try
            {
                await ReloadDocumentsAsync();

                var failed = _allDocuments.Count(document => document.DownloadState == DocumentDownloadState.Error);
                if (ErrorMessage is null && failed > 0)
                {
                    ErrorMessage = $"Se intentaron todas las descargas; {failed} documento(s) necesitan reintento.";
                }
            }
            catch (Exception exception)
            {
                ErrorMessage ??= $"Las descargas terminaron, pero no se pudo actualizar la vista: {exception.Message}";
            }

            IsBulkDownloading = false;
            IsBusy = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task DeleteAsync(LibraryDocumentItemViewModel? document)
    {
        if (document is null)
        {
            return;
        }

        ErrorMessage = null;

        try
        {
            await _repository.DeleteLocalCopyAsync(document.Id);
            await _resourceManager.DeleteAsync(document.Id);
            await ReloadDocumentsAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo eliminar la copia local de «{document.Title}»: {exception.Message}";
        }
    }

    [RelayCommand]
    private static async Task ReadAsync(LibraryDocumentItemViewModel? document)
    {
        if (document is null || !document.CanRead)
        {
            return;
        }

        var documentId = Uri.EscapeDataString(document.Id);
        await Shell.Current.GoToAsync($"{AppRoutes.Reader}?documentId={documentId}");
    }

    [RelayCommand]
    private static async Task ReadRecentAsync(RecentDocumentItemViewModel? document)
    {
        if (document is null) return;
        var documentId = Uri.EscapeDataString(document.Id);
        await Shell.Current.GoToAsync($"{AppRoutes.Reader}?documentId={documentId}");
    }

    private async Task ReloadDocumentsAsync()
    {
        var documents = await _repository.GetDocumentsAsync();
        _allDocuments.Clear();
        foreach (var document in documents
                     .OrderBy(document => document.Category)
                     .ThenBy(document => document.Title, StringComparer.OrdinalIgnoreCase))
        {
            DocumentResourceManifest? manifest = null;
            if (document.HasLocalCopy)
            {
                manifest = await _resourceManager.GetManifestAsync(document.Id);
            }

            _allDocuments.Add(new LibraryDocumentItemViewModel(document, manifest));
        }
        ApplyFilter();
        await ReloadRecentDocumentsAsync();
    }

    private async Task ReloadRecentDocumentsAsync()
    {
        var installedIds = _allDocuments.Where(document => document.CanRead)
            .Select(document => document.Id).ToHashSet(StringComparer.Ordinal);
        var recent = await _readingHistory.GetRecentAsync();
        RecentDocuments.Clear();
        foreach (var document in recent.Where(document => installedIds.Contains(document.DocumentId)))
        {
            RecentDocuments.Add(new RecentDocumentItemViewModel(document.DocumentId, document.Title));
        }
        OnPropertyChanged(nameof(HasRecentDocuments));
    }

    private void ApplyFilter()
    {
        var query = SearchQuery.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allDocuments
            : _allDocuments.Where(document =>
                document.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || document.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || document.CategoryText.Contains(query, StringComparison.CurrentCultureIgnoreCase));

        Documents.Clear();
        foreach (var document in filtered)
        {
            Documents.Add(document);
        }

        OnPropertyChanged(nameof(HasDocuments));
        OnPropertyChanged(nameof(ResultSummary));
    }

    private void OnDownloadProgressChanged(object? sender, DownloadJob job)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var document = _allDocuments.FirstOrDefault(item => item.Id == job.DocumentId);
            document?.Apply(job);
        });
    }

    private void OnContentUpdateChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            OnPropertyChanged(nameof(IsContentStatusChecking));
            OnPropertyChanged(nameof(IsContentCurrent));
            OnPropertyChanged(nameof(HasContentUpdateProblem));
            OnPropertyChanged(nameof(ContentStatusText));
        });
    }
}

public sealed record RecentDocumentItemViewModel(string Id, string Title);
