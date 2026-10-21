namespace DndCompendium.Core.Models;

public enum DocumentDownloadState
{
    NotDownloaded,
    Queued,
    Downloading,
    Downloaded,
    UpdateAvailable,
    Paused,
    Error
}
