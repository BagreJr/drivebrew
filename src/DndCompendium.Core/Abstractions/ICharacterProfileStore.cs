using DndCompendium.Core.Models;

namespace DndCompendium.Core.Abstractions;

public interface ICharacterProfileStore
{
    event EventHandler? ActiveCharacterChanged;

    Task<IReadOnlyList<CharacterProfile>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<CharacterProfile> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<CharacterProfile> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task RenameAsync(string characterId, string name, CancellationToken cancellationToken = default);

    Task DeleteAsync(string characterId, CancellationToken cancellationToken = default);

    Task SetActiveAsync(string characterId, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<string>> GetSavedIdsAsync(
        SavedContentKind contentKind,
        SavedStateKind stateKind,
        CancellationToken cancellationToken = default);

    Task SetSavedAsync(
        SavedContentKind contentKind,
        SavedStateKind stateKind,
        string itemId,
        bool isSaved,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedItemReference>> GetPreparedOrderAsync(
        CancellationToken cancellationToken = default);

    Task SetPreparedOrderAsync(
        IReadOnlyList<SavedItemReference> items,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterResource>> GetResourcesAsync(CancellationToken cancellationToken = default);

    Task<CharacterResource> UpsertResourceAsync(
        CharacterResource resource,
        CancellationToken cancellationToken = default);

    Task<CharacterResource> AdjustResourceAsync(
        string resourceId,
        int delta,
        CancellationToken cancellationToken = default);

    Task DeleteResourceAsync(string resourceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResourceRestChange>> PreviewRestAsync(
        RestKind restKind,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResourceRestChange>> ApplyRestAsync(
        RestKind restKind,
        CancellationToken cancellationToken = default);
}
