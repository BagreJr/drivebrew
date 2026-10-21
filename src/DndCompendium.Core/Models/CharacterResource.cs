namespace DndCompendium.Core.Models;

public enum CharacterResourceKind
{
    Custom,
    SpellSlot,
    PactMagicSlot
}

public enum ResourceRecoveryKind
{
    None,
    ShortRest,
    LongRest,
    ShortAndLongRest
}

public enum RestKind
{
    Short,
    Long
}

public sealed record CharacterResource
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public CharacterResourceKind Kind { get; init; }
    public int? Level { get; init; }
    public int Maximum { get; init; }
    public int Current { get; init; }
    public string? Unit { get; init; }
    public string? Description { get; init; }
    public ResourceRecoveryKind Recovery { get; init; }
    public int? RecoveryAmount { get; init; }
}

public sealed record ResourceRestChange(
    string ResourceId,
    string ResourceName,
    int Previous,
    int Current);
