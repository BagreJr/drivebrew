namespace DndCompendium.Core.Models;

public sealed record Spell
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required int Level { get; init; }
    public required string School { get; init; }
    public required string CastingTime { get; init; }
    public required string Range { get; init; }
    public required string Duration { get; init; }
    public required bool IsConcentration { get; init; }
    public required bool IsRitual { get; init; }
    public required bool HasVerbalComponent { get; init; }
    public required bool HasSomaticComponent { get; init; }
    public required bool HasMaterialComponent { get; init; }
    public required string MaterialDescription { get; init; }
    public required string Effect { get; init; }
    public required string HigherLevels { get; init; }
    public required IReadOnlyList<string> Classes { get; init; }
    public required IReadOnlyList<string> Origins { get; init; }
}
