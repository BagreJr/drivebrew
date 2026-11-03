using DndCompendium.Core.Models;

namespace DndCompendium.Core.Services;

public static class CharacterSheetEffectEngine
{
    public const string AbilityBonus = "AbilityBonus";
    public const string NumericBonus = "NumericBonus";
    public const string Proficiency = "Proficiency";

    public static void Rebuild(CharacterSheetData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        data.ProficiencyRanks.Clear();
        foreach (var (target, rank) in data.ManualProficiencyRanks)
            data.ProficiencyRanks[target] = Math.Clamp(rank, 0, 2);

        data.OtherBonuses.Clear();
        foreach (var (target, bonus) in data.ManualOtherBonuses)
            if (bonus != 0) data.OtherBonuses[target] = bonus;

        data.Modifiers.RemoveAll(modifier => !string.IsNullOrWhiteSpace(modifier.EffectId));
        foreach (var effect in data.Effects.Where(effect => effect.IsActive))
        {
            if (string.Equals(effect.EffectKind, Proficiency, StringComparison.Ordinal))
            {
                var rank = Math.Clamp(effect.Rank, 0, 2);
                if (!data.ProficiencyRanks.TryGetValue(effect.Target, out var current) || rank > current)
                    data.ProficiencyRanks[effect.Target] = rank;
                continue;
            }

            if (effect.EffectKind is AbilityBonus or NumericBonus)
            {
                data.Modifiers.Add(new CharacterSheetModifier
                {
                    Id = effect.Id,
                    EffectId = effect.Id,
                    SourceId = effect.SourceSelectionId,
                    SourceName = effect.SourceName,
                    Target = effect.Target,
                    Value = effect.Value,
                    BonusType = effect.EffectKind == AbilityBonus ? "selection" : "confirmed",
                    Condition = effect.Condition,
                    Duration = effect.Activation,
                    IsActive = true
                });
            }
        }
    }

    public static void UpsertEffect(CharacterSheetData data, CharacterSheetEffect effect)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(effect);
        var index = data.Effects.FindIndex(item => string.Equals(item.Id, effect.Id, StringComparison.Ordinal));
        if (index >= 0) data.Effects[index] = effect;
        else data.Effects.Add(effect);
        Rebuild(data);
    }

    public static bool RemoveEffect(CharacterSheetData data, string effectId)
    {
        ArgumentNullException.ThrowIfNull(data);
        var removed = data.Effects.RemoveAll(effect => string.Equals(effect.Id, effectId, StringComparison.Ordinal)) > 0;
        if (removed) Rebuild(data);
        return removed;
    }

    public static void RemoveSelectionEffects(CharacterSheetData data, string selectionId)
    {
        ArgumentNullException.ThrowIfNull(data);
        data.Effects.RemoveAll(effect => string.Equals(effect.SourceSelectionId, selectionId, StringComparison.Ordinal));
        data.Features.RemoveAll(feature => string.Equals(feature.SelectionId, selectionId, StringComparison.Ordinal));
        Rebuild(data);
    }
}
