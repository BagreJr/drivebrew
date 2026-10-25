namespace DndCompendium.Core.Models;

public sealed record CharacterProfile(
    string Id,
    string Name,
    DateTimeOffset CreatedAt);

public enum SavedContentKind
{
    Spell,
    Exploit
}

public enum SavedStateKind
{
    Favorite,
    Prepared
}

public sealed record SavedItemReference(
    SavedContentKind ContentKind,
    string ItemId);
