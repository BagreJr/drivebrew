using DndCompendium.Core.Models;
using DndCompendium.Core.Services;

namespace DndCompendium.Core.Tests;

public sealed class CharacterRuleSuggestionParserTests
{
    [Fact]
    public void ParsesOnlyExplicitAbilityAndSpeedBonuses()
    {
        var selection = new CharacterCompendiumSelection
        {
            Id = "selection:race:elf", Name = "Fleet Elf",
            Description = "Increase your Dexterity score by 2. Your walking speed increases by 5 feet."
        };

        var suggestions = CharacterRuleSuggestionParser.Parse(selection);

        Assert.Contains(suggestions, item => item.EffectKind == CharacterSheetEffectEngine.AbilityBonus
            && item.Target == "DEX" && item.Value == 2);
        Assert.Contains(suggestions, item => item.EffectKind == CharacterSheetEffectEngine.NumericBonus
            && item.Target == "Speed" && item.Value == 5);
    }

    [Fact]
    public void AmbiguousDescription_DoesNotInventEffects()
    {
        var selection = new CharacterCompendiumSelection
        {
            Id = "selection:feat:mystery", Name = "Mystery",
            Description = "You become quicker and unusually perceptive. Choose an appropriate benefit."
        };

        Assert.Empty(CharacterRuleSuggestionParser.Parse(selection));
    }
}
