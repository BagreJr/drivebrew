using System.Text.Json;
using DndCompendium.Core.Models;

namespace DndCompendium.Core.Tests;

public sealed class CharacterSheetDataTests
{
    [Fact]
    public void LegacyJson_LoadsWithoutLosingPdfFields()
    {
        const string legacy = """
        {
          "TextFields": { "CharacterName": "Nyx", "STR": "15", "Backstory": "Kept" },
          "CheckedFields": [ "Check Box 26" ],
          "UpdatedAt": "2026-10-20T00:00:00Z"
        }
        """;

        var data = JsonSerializer.Deserialize<CharacterSheetData>(legacy)!;

        Assert.Equal(CharacterSheetData.CurrentSchemaVersion, data.SchemaVersion);
        Assert.Equal("2014", data.RuleSet);
        Assert.Equal("Nyx", data.TextFields["CharacterName"]);
        Assert.Equal("Kept", data.TextFields["Backstory"]);
        Assert.Contains("Check Box 26", data.CheckedFields);
        Assert.Empty(data.Modifiers);
        Assert.Empty(data.Features);
        Assert.Empty(data.KnownSpellIds);
    }

    [Fact]
    public void StructuredData_RoundTripsSourcesAndOverrides()
    {
        var original = new CharacterSheetData
        {
            RuleSet = "Homebrew",
            AbilityBaseScores = new Dictionary<string, int> { ["STR"] = 15 },
            ProficiencyRanks = new Dictionary<string, int> { ["Athletics"] = 2 },
            OtherBonuses = new Dictionary<string, decimal> { ["Athletics"] = 1 },
            Overrides = new Dictionary<string, string> { ["AC"] = "18" },
            Modifiers =
            [
                new CharacterSheetModifier { SourceName = "Feat", Target = "STR", Value = 1 }
            ],
            Features =
            [
                new CharacterFeatureReference { Name = "Chronal Shift", Kind = "SubclassFeature" }
            ]
        };

        var restored = JsonSerializer.Deserialize<CharacterSheetData>(JsonSerializer.Serialize(original))!;

        Assert.Equal("Homebrew", restored.RuleSet);
        Assert.Equal(15, restored.AbilityBaseScores["STR"]);
        Assert.Equal(2, restored.ProficiencyRanks["Athletics"]);
        Assert.Equal(1, restored.OtherBonuses["Athletics"]);
        Assert.Equal("18", restored.Overrides["AC"]);
        Assert.Single(restored.Modifiers);
        Assert.Single(restored.Features);
    }
}
