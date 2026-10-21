using DndCompendium.Core.Models;

namespace DndCompendium.Core.Abstractions;

public interface IReadingHistoryStore
{
    Task<IReadOnlyList<RecentDocument>> GetRecentAsync(CancellationToken cancellationToken = default);

    Task RecordOpenedAsync(
        string documentId,
        string title,
        CancellationToken cancellationToken = default);
}
