using DndCompendium.Core.Models;
using DndCompendium.Core.Services;

namespace DndCompendium.Core.Tests;

public sealed class CharacterResourceRulesTests
{
    [Fact]
    public void AdjustClampsBetweenZeroAndMaximum()
    {
        var resource = Resource("dice", 5, 3);

        Assert.Equal(0, CharacterResourceRules.Adjust(resource, -20).Current);
        Assert.Equal(5, CharacterResourceRules.Adjust(resource, 20).Current);
    }

    [Fact]
    public void RestUsesOnlyExplicitTriggersAndPartialAmounts()
    {
        var resources = new[]
        {
            Resource("short", 5, 1) with { Recovery = ResourceRecoveryKind.ShortRest, RecoveryAmount = 2 },
            Resource("long", 8, 2) with { Recovery = ResourceRecoveryKind.LongRest },
            Resource("both", 3, 0) with { Recovery = ResourceRecoveryKind.ShortAndLongRest, RecoveryAmount = 1 },
            Resource("manual", 10, 1) with { Recovery = ResourceRecoveryKind.None }
        };

        var shortRest = CharacterResourceRules.PreviewRest(resources, RestKind.Short);
        Assert.Collection(shortRest,
            change => { Assert.Equal("short", change.ResourceName); Assert.Equal(3, change.Current); },
            change => { Assert.Equal("both", change.ResourceName); Assert.Equal(1, change.Current); });

        var longRest = CharacterResourceRules.PreviewRest(resources, RestKind.Long);
        Assert.Collection(longRest,
            change => { Assert.Equal("long", change.ResourceName); Assert.Equal(8, change.Current); },
            change => { Assert.Equal("both", change.ResourceName); Assert.Equal(1, change.Current); });
    }

    [Fact]
    public void SlotLevelMustBeExplicitAndValid()
    {
        var slot = Resource("Slot", 2, 2) with { Kind = CharacterResourceKind.SpellSlot, Level = null };
        Assert.Throws<ArgumentOutOfRangeException>(() => CharacterResourceRules.Normalize(slot));
    }

    private static CharacterResource Resource(string name, int maximum, int current) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        Maximum = maximum,
        Current = current,
        Kind = CharacterResourceKind.Custom,
        Recovery = ResourceRecoveryKind.None
    };
}
