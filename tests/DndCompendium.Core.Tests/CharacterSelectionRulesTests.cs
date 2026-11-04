using DndCompendium.Core.Models;
using DndCompendium.Core.Services;

namespace DndCompendium.Core.Tests;

public sealed class CharacterSelectionRulesTests
{
    [Fact]
    public void Subclass_RequiresItsActiveParentClass()
    {
        var selections = new[]
        {
            new CharacterCompendiumSelection
            {
                Kind = nameof(SearchContentKind.Class), ReferenceId = "wizard", Name = "Wizard", IsActive = true
            }
        };

        Assert.True(CharacterSelectionRules.IsSubclassCompatible(selections, "wizard"));
        Assert.False(CharacterSelectionRules.IsSubclassCompatible(selections, "Fighter"));
        Assert.True(CharacterSelectionRules.IsSubclassCompatible(selections, null));
    }

    [Theory]
    [InlineData("3", 2, false)]
    [InlineData("3", 3, true)]
    [InlineData("Level varies", 1, true)]
    public void FeatureLevel_IsCheckedOnlyWhenStructured(string required, int level, bool expected)
        => Assert.Equal(expected, CharacterSelectionRules.IsAvailableAtLevel(required, level));

    [Fact]
    public void Eligibility_BlocksSubclassAndExplicitFutureLevel()
    {
        var selections = new[]
        {
            new CharacterCompendiumSelection
            {
                Kind = nameof(SearchContentKind.Class), Name = "Fighter", IsActive = true
            }
        };

        Assert.Contains("Wizard", CharacterSelectionRules.GetUnavailableReason(
            SearchContentKind.Subclass, "Wizard", null, null, null, selections, 3));
        Assert.Contains("nivel 4", CharacterSelectionRules.GetUnavailableReason(
            SearchContentKind.Feat, null, null, null, "Requires level 4", selections, 3));
        Assert.Null(CharacterSelectionRules.GetUnavailableReason(
            SearchContentKind.Feat, null, null, null, "Requires level 4", selections, 4));
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("Requires level 8", 8)]
    [InlineData("Prerequisite: 8th-level", 8)]
    [InlineData("Requisito: nivel 12", 12)]
    [InlineData("Level varies", null)]
    public void ExplicitLevel_IsParsedWithoutGuessing(string text, int? expected)
        => Assert.Equal(expected, CharacterSelectionRules.ParseExplicitLevel(text));
}
