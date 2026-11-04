using System.Text.RegularExpressions;
using DndCompendium.Core.Models;

namespace DndCompendium.Core.Services;

public sealed record CharacterRuleSuggestion(
    string Id,
    string SelectionId,
    string SourceName,
    string EffectKind,
    string Target,
    decimal Value,
    int Rank,
    string Explanation);

public static partial class CharacterRuleSuggestionParser
{
    private static readonly IReadOnlyDictionary<string, string> Abilities =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["strength"] = "STR", ["fuerza"] = "STR",
            ["dexterity"] = "DEX", ["destreza"] = "DEX",
            ["constitution"] = "CON", ["constitución"] = "CON", ["constitucion"] = "CON",
            ["intelligence"] = "INT", ["inteligencia"] = "INT",
            ["wisdom"] = "WIS", ["sabiduría"] = "WIS", ["sabiduria"] = "WIS",
            ["charisma"] = "CHA", ["carisma"] = "CHA"
        };

    public static IReadOnlyList<CharacterRuleSuggestion> Parse(CharacterCompendiumSelection selection)
    {
        if (string.IsNullOrWhiteSpace(selection.Description)) return [];
        var suggestions = new List<CharacterRuleSuggestion>();
        foreach (Match match in AbilityIncreaseRegex().Matches(selection.Description))
        {
            if (!Abilities.TryGetValue(match.Groups["ability"].Value, out var target)
                || !decimal.TryParse(match.Groups["value"].Value, out var value)) continue;
            suggestions.Add(Create(selection, CharacterSheetEffectEngine.AbilityBonus, target, value, 0,
                $"{target} +{value} indicado explícitamente por {selection.Name}."));
        }

        foreach (Match match in SpeedIncreaseRegex().Matches(selection.Description))
        {
            if (!decimal.TryParse(match.Groups["value"].Value, out var value)) continue;
            suggestions.Add(Create(selection, CharacterSheetEffectEngine.NumericBonus, "Speed", value, 0,
                $"Velocidad +{value} indicado explícitamente por {selection.Name}."));
        }

        return suggestions.DistinctBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    private static CharacterRuleSuggestion Create(
        CharacterCompendiumSelection selection,
        string effectKind,
        string target,
        decimal value,
        int rank,
        string explanation)
        => new(
            $"suggestion:{selection.Id}:{effectKind}:{target}:{value}:{rank}", selection.Id, selection.Name,
            effectKind, target, value, rank, explanation);

    [GeneratedRegex(@"(?ix)\b(?:increase\s+your|aumenta(?:s)?\s+(?:tu|la))\s+(?<ability>strength|dexterity|constitution|intelligence|wisdom|charisma|fuerza|destreza|constituci[oó]n|inteligencia|sabidur[ií]a|carisma)(?:\s+score|\s+puntuaci[oó]n)?\s+(?:by|en)\s+(?<value>[1-9]\d*)\b")]
    private static partial Regex AbilityIncreaseRegex();

    [GeneratedRegex(@"(?ix)\b(?:your\s+(?:walking\s+)?speed\s+increases\s+by|tu\s+velocidad(?:\s+al\s+caminar)?\s+aumenta\s+en)\s+(?<value>[1-9]\d*)\s*(?:feet|ft\.?|pies)\b")]
    private static partial Regex SpeedIncreaseRegex();
}
