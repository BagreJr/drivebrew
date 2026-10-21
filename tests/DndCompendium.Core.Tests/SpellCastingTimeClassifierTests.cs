using DndCompendium.Core.Services;

namespace DndCompendium.Core.Tests;

public sealed class SpellCastingTimeClassifierTests
{
    [Theory]
    [InlineData("action", SpellCastingTimeKind.Action)]
    [InlineData("1 Action", SpellCastingTimeKind.Action)]
    [InlineData("bonus action", SpellCastingTimeKind.BonusAction)]
    [InlineData("Acción adicional", SpellCastingTimeKind.BonusAction)]
    [InlineData("reaction", SpellCastingTimeKind.Reaction)]
    [InlineData("1 reaction, which you take when an ally is hit", SpellCastingTimeKind.Reaction)]
    [InlineData("Reacción", SpellCastingTimeKind.Reaction)]
    [InlineData("1 minute", SpellCastingTimeKind.Other)]
    [InlineData("8 hours", SpellCastingTimeKind.Other)]
    public void Classify_RecognizesSupportedCastingTimes(string value, SpellCastingTimeKind expected)
        => Assert.Equal(expected, SpellCastingTimeClassifier.Classify(value));
}
