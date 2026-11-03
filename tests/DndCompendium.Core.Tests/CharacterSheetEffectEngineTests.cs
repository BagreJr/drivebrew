using DndCompendium.Core.Models;
using DndCompendium.Core.Services;

namespace DndCompendium.Core.Tests;

public sealed class CharacterSheetEffectEngineTests
{
    [Fact]
    public void SelectionEffects_AreAppliedOnceAndRemovedWithoutTouchingManualValues()
    {
        var data = new CharacterSheetData
        {
            ManualProficiencyRanks = new Dictionary<string, int> { ["Arcana"] = 1 },
            ManualOtherBonuses = new Dictionary<string, decimal> { ["Initiative"] = 2 },
            Effects =
            [
                new CharacterSheetEffect
                {
                    Id = "racial-dex", SourceSelectionId = "race:elf", SourceName = "Elf",
                    EffectKind = CharacterSheetEffectEngine.AbilityBonus, Target = "DEX", Value = 2
                },
                new CharacterSheetEffect
                {
                    Id = "racial-perception", SourceSelectionId = "race:elf", SourceName = "Elf",
                    EffectKind = CharacterSheetEffectEngine.Proficiency, Target = "Perception ", Rank = 1
                }
            ]
        };

        CharacterSheetEffectEngine.Rebuild(data);
        CharacterSheetEffectEngine.Rebuild(data);

        Assert.Equal(1, data.ProficiencyRanks["Arcana"]);
        Assert.Equal(1, data.ProficiencyRanks["Perception "]);
        Assert.Equal(2, data.OtherBonuses["Initiative"]);
        Assert.Single(data.Modifiers, item => item.EffectId == "racial-dex");

        CharacterSheetEffectEngine.RemoveSelectionEffects(data, "race:elf");

        Assert.Equal(1, data.ProficiencyRanks["Arcana"]);
        Assert.False(data.ProficiencyRanks.ContainsKey("Perception "));
        Assert.Equal(2, data.OtherBonuses["Initiative"]);
        Assert.DoesNotContain(data.Modifiers, item => item.EffectId is not null);
    }

    [Fact]
    public void ExplicitChoice_IsPersistedWithActivationAndCondition()
    {
        var data = new CharacterSheetData();
        CharacterSheetEffectEngine.UpsertEffect(data, new CharacterSheetEffect
        {
            Id = "choice", SourceSelectionId = "feat:skilled", SourceName = "Skilled",
            EffectKind = CharacterSheetEffectEngine.Proficiency, Target = "Stealth ", Rank = 2,
            ChoiceLabel = "Stealth", Activation = "Permanent", Condition = "Confirmed by player"
        });

        var effect = Assert.Single(data.Effects);
        Assert.Equal("Stealth", effect.ChoiceLabel);
        Assert.Equal("Confirmed by player", effect.Condition);
        Assert.Equal(2, data.ProficiencyRanks["Stealth "]);
    }
}
