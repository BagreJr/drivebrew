namespace DndCompendium.Core.Models;

public sealed class CharacterSheetData
{
    public const int CurrentSchemaVersion = 3;

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

    public Dictionary<string, int> ManualProficiencyRanks { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, decimal> OtherBonuses { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, decimal> ManualOtherBonuses { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Overrides { get; init; } = new(StringComparer.Ordinal);

    public List<CharacterSheetModifier> Modifiers { get; init; } = [];

    public List<CharacterFeatureReference> Features { get; init; } = [];

    public List<CharacterCompendiumSelection> Selections { get; init; } = [];

    public List<CharacterSheetEffect> Effects { get; init; } = [];

    public HashSet<string> KnownSpellIds { get; init; } = new(StringComparer.Ordinal);

    public List<CharacterSpellReference> Spells { get; init; } = [];

    public Dictionary<string, string> GeneratedTextFields { get; init; } = new(StringComparer.Ordinal);

    public HashSet<string> GeneratedCheckedFields { get; init; } = new(StringComparer.Ordinal);

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CharacterSheetModifier
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string SourceName { get; init; } = string.Empty;
    public string? SourceId { get; init; }
    public string? EffectId { get; init; }
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
    public string? SelectionId { get; init; }
}

public sealed class CharacterCompendiumSelection
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Kind { get; init; } = string.Empty;
    public string ReferenceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? SourceKey { get; init; }
    public string? DocumentId { get; init; }
    public string? Target { get; init; }
    public string? ParentClass { get; init; }
    public string? ParentName { get; init; }
    public int? Level { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset SelectedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CharacterSheetEffect
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string SourceSelectionId { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string EffectKind { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public decimal Value { get; init; }
    public int Rank { get; init; }
    public string Activation { get; init; } = "Permanent";
    public string? Condition { get; init; }
    public string? ChoiceLabel { get; init; }
    public bool IsActive { get; set; } = true;
}

public sealed class CharacterSpellReference
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Level { get; init; }
    public string? School { get; init; }
    public bool IsKnown { get; set; }
    public bool IsPrepared { get; set; }
}
