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
}
