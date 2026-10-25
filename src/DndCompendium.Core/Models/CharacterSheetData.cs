namespace DndCompendium.Core.Models;

public sealed class CharacterSheetData
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string RuleSet { get; set; } = "2014";

    public Dictionary<string, string> TextFields { get; init; } = new(StringComparer.Ordinal);

    public HashSet<string> CheckedFields { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Original, player-entered ability scores. TextFields keeps the calculated
    /// total used by the visual sheet and PDF export.
    /// </summary>
    public Dictionary<string, int> AbilityBaseScores { get; init; } = new(StringComparer.Ordinal);

    /// <summary>0 = not proficient, 1 = proficient, 2 = expertise.</summary>
    public Dictionary<string, int> ProficiencyRanks { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, decimal> OtherBonuses { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Overrides { get; init; } = new(StringComparer.Ordinal);

    public List<CharacterSheetModifier> Modifiers { get; init; } = [];

    public List<CharacterFeatureReference> Features { get; init; } = [];

    public HashSet<string> KnownSpellIds { get; init; } = new(StringComparer.Ordinal);

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CharacterSheetModifier
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string SourceName { get; init; } = string.Empty;
    public string? SourceId { get; init; }
    public string Target { get; init; } = string.Empty;
    public decimal Value { get; init; }
    public string BonusType { get; init; } = "untyped";
    public string? Condition { get; init; }
    public string? Duration { get; init; }
    public bool IsActive { get; set; } = true;
}

public sealed class CharacterFeatureReference
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string? SourceKey { get; init; }
    public string? DocumentId { get; init; }
    public string? Target { get; init; }
    public string? Level { get; init; }
    public string? PersonalNotes { get; set; }
}
