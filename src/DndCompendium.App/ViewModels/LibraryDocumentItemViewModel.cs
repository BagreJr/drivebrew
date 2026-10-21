using CommunityToolkit.Mvvm.ComponentModel;
using DndCompendium.Core.Models;

namespace DndCompendium.App.ViewModels;

public partial class LibraryDocumentItemViewModel : ObservableObject
{
    public LibraryDocumentItemViewModel(
        LibraryDocument document,
        DocumentResourceManifest? resourceManifest = null)
    {
        Id = document.Id;
        Title = document.Title;
        Description = document.Description;
        Category = document.Category;
        SourceSizeBytes = document.SourceSizeBytes;
        DownloadState = document.DownloadState;
        Progress = document.Progress;
        ErrorMessage = document.ErrorMessage;
        HasLocalCopy = document.HasLocalCopy;
        ResourceCount = resourceManifest?.Resources.Count ?? 0;
        MissingResourceCount = resourceManifest is null
            ? 0
            : resourceManifest.Resources.Count(resource => resource.IsRequired
                && resource.State != DocumentResourceState.Available);
        ResourceOfflineState = resourceManifest?.OfflineState;
    }

    public string Id { get; }

    public string Title { get; }

    public string Description { get; }

    public DocumentCategory Category { get; }

    public long SourceSizeBytes { get; }

    public int ResourceCount { get; }

    public int MissingResourceCount { get; }

    public DocumentOfflineState? ResourceOfflineState { get; }

    public bool HasLocalCopy { get; }

    public string CategoryText => Category switch
    {
        DocumentCategory.Classes => "Clases",
        DocumentCategory.Subclasses => "Subclases",
        DocumentCategory.Races => "Razas",
        DocumentCategory.Feats => "Dotes",
        DocumentCategory.Spells => "Hechizos",
        DocumentCategory.Weapons => "Armas",
        DocumentCategory.Equipment => "Equipamiento",
        DocumentCategory.Backgrounds => "Trasfondos",
        _ => "Sin clasificar"
    };

    public string SizeText => ByteSizeFormatter.Format(SourceSizeBytes);

    public string StatusText => DownloadState switch
    {
        DocumentDownloadState.NotDownloaded => "Solo en la biblioteca",
        DocumentDownloadState.Queued => "En cola",
        DocumentDownloadState.Downloading => $"Descargando {Progress:P0}",
        DocumentDownloadState.Downloaded when ResourceCount == 0 => "Recursos sin preparar",
        DocumentDownloadState.Downloaded when ResourceOfflineState == DocumentOfflineState.Complete => "Offline completo",
        DocumentDownloadState.Downloaded when ResourceOfflineState == DocumentOfflineState.Pending => "Preparando recursos",
        DocumentDownloadState.Downloaded => $"Faltan {MissingResourceCount} recurso(s)",
        DocumentDownloadState.UpdateAvailable => "Actualización disponible",
        DocumentDownloadState.Paused => $"Pausado {Progress:P0}",
        DocumentDownloadState.Error => "No se pudo descargar",
        _ => DownloadState.ToString()
    };

    public string DownloadActionText => DownloadState switch
    {
        DocumentDownloadState.UpdateAvailable => "Actualizar",
        DocumentDownloadState.Error => "Reintentar",
        _ => "Descargar"
    };

    public bool CanRead => HasLocalCopy;

    public bool CanDownload => DownloadState is DocumentDownloadState.NotDownloaded
        or DocumentDownloadState.UpdateAvailable
        or DocumentDownloadState.Error;

    public bool CanDelete => HasLocalCopy;

    public bool IsProgressVisible => DownloadState is DocumentDownloadState.Queued
        or DocumentDownloadState.Downloading
        or DocumentDownloadState.Paused;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(DownloadActionText))]
    [NotifyPropertyChangedFor(nameof(CanRead))]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(IsProgressVisible))]
    private DocumentDownloadState downloadState;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private double progress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public void Apply(DownloadJob job)
    {
        DownloadState = job.State;
        Progress = Math.Clamp(job.Progress, 0, 1);
        ErrorMessage = job.ErrorMessage;
    }
}

internal static class ByteSizeFormatter
{
    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }

        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.#} {units[unit]}";
    }
}
