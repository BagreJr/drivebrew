using DndCompendium.Core.Models;

namespace DndCompendium.Core.Abstractions;

public interface IExternalResourceDownloader
{
    Task<ExternalResourceDownload> DownloadAsync(
        ExternalResourceReference resource,
        CancellationToken cancellationToken = default);
}
