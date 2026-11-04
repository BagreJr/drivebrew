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

    [Fact]
    public void VersionFourData_RoundTripsDraftProgressionAndPlayState()
    {
        var original = new CharacterSheetData
        {
            IsCreationDraft = true,
            CreatorStep = 6,
            PortraitDataUrl = "data:image/jpeg;base64,YQ==",
            ClassLevels = [new CharacterClassLevel { Name = "Psion", Level = 3, IsPrimary = true }],
            Conditions = [new CharacterCondition { Name = "Restrained", IsActive = true }],
            Attacks = [new CharacterAttack { Name = "Mind Blade", AttackBonus = "+5", Damage = "1d8 psychic" }],
            Inventory = [new CharacterInventoryItem { Name = "Crystal focus", Quantity = 1 }]
        };

        var restored = JsonSerializer.Deserialize<CharacterSheetData>(JsonSerializer.Serialize(original))!;

        Assert.True(restored.IsCreationDraft);
        Assert.Equal(6, restored.CreatorStep);
        Assert.Equal("Psion", Assert.Single(restored.ClassLevels).Name);
        Assert.Equal("Restrained", Assert.Single(restored.Conditions).Name);
        Assert.Equal("Mind Blade", Assert.Single(restored.Attacks).Name);
        Assert.Equal("Crystal focus", Assert.Single(restored.Inventory).Name);
    }

    [Fact]
    public void ManualPlayValues_RoundTripCurrentMaximumAndTemporaryHitPointsIndependently()
    {
        var original = new CharacterSheetData
        {
            TextFields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HPCurrent"] = "17",
                ["HPMax"] = "31",
                ["HPTemp"] = "6",
                ["HD"] = "3d8",
                ["HDTotal"] = "5d8",
                ["Inspiration"] = "1"
            }
        };

        var restored = JsonSerializer.Deserialize<CharacterSheetData>(JsonSerializer.Serialize(original))!;

        Assert.Equal("17", restored.TextFields["HPCurrent"]);
        Assert.Equal("31", restored.TextFields["HPMax"]);
        Assert.Equal("6", restored.TextFields["HPTemp"]);
        Assert.Equal("3d8", restored.TextFields["HD"]);
        Assert.Equal("5d8", restored.TextFields["HDTotal"]);
        Assert.Equal("1", restored.TextFields["Inspiration"]);
    }
}
