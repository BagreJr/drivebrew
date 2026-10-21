using CommunityToolkit.Mvvm.ComponentModel;
using DndCompendium.Core.Models;

namespace DndCompendium.App.ViewModels;

public partial class DownloadJobItemViewModel : ObservableObject
{
    public DownloadJobItemViewModel(DownloadJob job)
    {
        DocumentId = job.DocumentId;
        Title = job.Title;
        Apply(job);
    }

    public string DocumentId { get; }

    public string Title { get; }

    public string StateText => State switch
    {
        DocumentDownloadState.Queued => "En cola",
        DocumentDownloadState.Downloading => "Descargando",
        DocumentDownloadState.Downloaded => "Completado",
        DocumentDownloadState.UpdateAvailable => "Actualización disponible",
        DocumentDownloadState.Paused => "Pausado",
        DocumentDownloadState.Error => "Error recuperable",
        _ => "Pendiente"
    };

    public string ProgressText => TotalBytes > 0
        ? $"{ByteSizeFormatter.Format(BytesTransferred)} de {ByteSizeFormatter.Format(TotalBytes)} · {Progress:P0}"
        : $"{Progress:P0}";

    public string UpdatedText => $"Actualizado {UpdatedAt.ToLocalTime():HH:mm}";

    public bool CanPause => State is DocumentDownloadState.Downloading
        or DocumentDownloadState.Queued;

    public bool CanResume => State is DocumentDownloadState.Paused;

    public bool CanCancel => State is DocumentDownloadState.Downloading
        or DocumentDownloadState.Queued
        or DocumentDownloadState.Paused;

    public bool CanRetry => State is DocumentDownloadState.Error;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(CanPause))]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private DocumentDownloadState state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private double progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private long bytesTransferred;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private long totalBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedText))]
    private DateTimeOffset updatedAt;

    public void Apply(DownloadJob job)
    {
        State = job.State;
        Progress = Math.Clamp(job.Progress, 0, 1);
        BytesTransferred = job.BytesTransferred;
        TotalBytes = job.TotalBytes;
        ErrorMessage = job.ErrorMessage;
        UpdatedAt = job.UpdatedAt;
    }
}
