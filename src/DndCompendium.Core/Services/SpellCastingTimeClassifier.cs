namespace DndCompendium.Core.Services;

public enum SpellCastingTimeKind
{
    Action,
    BonusAction,
    Reaction,
    Other
}

public static class SpellCastingTimeClassifier
{
    public static SpellCastingTimeKind Classify(string? castingTime)
    {
        var normalized = HomebrewSearchIndexer.NormalizeForSearch(castingTime ?? string.Empty);
        if (normalized.Contains("bonus action", StringComparison.Ordinal)
            || normalized.Contains("accion adicional", StringComparison.Ordinal))
            return SpellCastingTimeKind.BonusAction;
        if (normalized.Contains("reaction", StringComparison.Ordinal)
            || normalized.Contains("reaccion", StringComparison.Ordinal))
            return SpellCastingTimeKind.Reaction;
        if (normalized.Contains("action", StringComparison.Ordinal)
            || normalized.Contains("accion", StringComparison.Ordinal))
            return SpellCastingTimeKind.Action;
        return SpellCastingTimeKind.Other;
    }
}
