using DndCompendium.Core.Abstractions;

namespace DndCompendium.App.ViewModels;

public sealed class FavoritesViewModel(
    ISpellRepository spellRepository,
    IExploitRepository exploitRepository,
    ICharacterProfileStore profileStore)
    : SavedItemsViewModel(spellRepository, exploitRepository, profileStore, preparedOnly: false);
