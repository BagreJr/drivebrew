namespace DndCompendium.Core.Models;

public sealed class CharacterSheetData
{
    public Dictionary<string, string> TextFields { get; init; } = new(StringComparer.Ordinal);

    public HashSet<string> CheckedFields { get; init; } = new(StringComparer.Ordinal);

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
