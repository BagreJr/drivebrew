using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using Microsoft.Maui.ApplicationModel;

namespace DndCompendium.App.ViewModels;

public partial class DownloadsViewModel : ObservableObject
{
    private readonly ILocalDocumentRepository _repository;
    private readonly IDownloadManager _downloadManager;
    private readonly ISpellRepository _spellRepository;
    private readonly IExploitRepository _exploitRepository;

    public DownloadsViewModel(
        ILocalDocumentRepository repository,
        IDownloadManager downloadManager,
        ISpellRepository spellRepository,
        IExploitRepository exploitRepository)
    {
        _repository = repository;
        _downloadManager = downloadManager;
        _spellRepository = spellRepository;
        _exploitRepository = exploitRepository;
        _downloadManager.ProgressChanged += OnDownloadProgressChanged;
    }

    public ObservableCollection<DownloadJobItemViewModel> Jobs { get; } = [];

    public bool HasJobs => Jobs.Count > 0;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private string storageUsageText = "Calculando…";

    [ObservableProperty]
    private bool isUpdatingSpells;

    [ObservableProperty]
    private bool isUpdatingExploits;

    [ObservableProperty]
    private string spellsCatalogStatus = "Descargá la versión más reciente desde Drive.";

    [ObservableProperty]
    private string exploitsCatalogStatus = "Descargá la versión más reciente desde Drive.";

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task UpdateSpellsAsync()
    {
        IsUpdatingSpells = true;
        ErrorMessage = null;
        try
        {
            var spells = await _spellRepository.UpdateFromDriveAsync();
            SpellsCatalogStatus = $"Actualizado: {spells.Count} spells · {DateTime.Now:g}";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo actualizar spells.json: {exception.Message}";
        }
        finally
        {
            IsUpdatingSpells = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task UpdateExploitsAsync()
    {
        IsUpdatingExploits = true;
        ErrorMessage = null;
        try
        {
            var exploits = await _exploitRepository.UpdateFromDriveAsync();
            ExploitsCatalogStatus = $"Actualizado: {exploits.Count} exploits · {DateTime.Now:g}";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo actualizar exploit.json: {exception.Message}";
        }
        finally
        {
            IsUpdatingExploits = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await _repository.InitializeAsync();
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudieron cargar las descargas: {exception.Message}";
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
        IsRefreshing = true;
        await LoadAsync();
    }

    [RelayCommand]
    private void Pause(DownloadJobItemViewModel? job)
    {
        if (job is null || !job.CanPause)
        {
            return;
        }

        ErrorMessage = null;
        _downloadManager.Pause(job.DocumentId);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ResumeAsync(DownloadJobItemViewModel? job)
    {
        if (job is null || !job.CanResume)
        {
            return;
        }

        await RunJobActionAsync(
            () => _downloadManager.ResumeAsync(job.DocumentId),
            $"No se pudo reanudar «{job.Title}»");
    }

    [RelayCommand]
    private void Cancel(DownloadJobItemViewModel? job)
    {
        if (job is null || !job.CanCancel)
        {
            return;
        }

        ErrorMessage = null;
        _downloadManager.Cancel(job.DocumentId);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RetryAsync(DownloadJobItemViewModel? job)
    {
        if (job is null || !job.CanRetry)
        {
            return;
        }

        await RunJobActionAsync(
            () => _downloadManager.RetryAsync(job.DocumentId),
            $"No se pudo reintentar «{job.Title}»");
    }

    private async Task RunJobActionAsync(Func<Task> action, string errorPrefix)
    {
        ErrorMessage = null;

        try
        {
            await action();
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"{errorPrefix}: {exception.Message}";
        }
    }

    private async Task ReloadAsync()
    {
        var jobs = await _repository.GetDownloadJobsAsync();
        var storageBytes = await _repository.GetStorageUsageAsync();

        Jobs.Clear();
        foreach (var job in jobs.OrderByDescending(job => job.UpdatedAt))
        {
            Jobs.Add(new DownloadJobItemViewModel(job));
        }

        StorageUsageText = $"{ByteSizeFormatter.Format(storageBytes)} guardados en el dispositivo";
        OnPropertyChanged(nameof(HasJobs));
    }

    private void OnDownloadProgressChanged(object? sender, DownloadJob job)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var item = Jobs.FirstOrDefault(existing => existing.DocumentId == job.DocumentId);
            if (item is null)
            {
                Jobs.Insert(0, new DownloadJobItemViewModel(job));
                OnPropertyChanged(nameof(HasJobs));
            }
            else
            {
                item.Apply(job);
            }
        });
    }
}
