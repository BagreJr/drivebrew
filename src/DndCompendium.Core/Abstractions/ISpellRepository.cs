using DndCompendium.Core.Models;

namespace DndCompendium.Core.Abstractions;

public interface ISpellRepository
{
    Task<IReadOnlyList<Spell>> GetAllAsync(
        bool refreshFromDrive = true,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Spell>> UpdateFromDriveAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlySet<string>> GetPreparedIdsAsync(CancellationToken cancellationToken = default);

    Task SetPreparedAsync(
        string spellId,
        bool isPrepared,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<string>> GetFavoriteIdsAsync(CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(
        string spellId,
        bool isFavorite,
        CancellationToken cancellationToken = default);
}
