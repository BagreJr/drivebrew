using DndCompendium.Core.Models;

namespace DndCompendium.Core.Abstractions;

public interface IUniversalSearchService
{
    Task RefreshIndexAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        SearchContentKind? kind = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchResult>> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default);

    Task<SearchFilterOptions> GetFilterOptionsAsync(
        SearchContentKind? kind = null,
        CancellationToken cancellationToken = default);

    Task<int> GetEntryCountAsync(CancellationToken cancellationToken = default);
}
