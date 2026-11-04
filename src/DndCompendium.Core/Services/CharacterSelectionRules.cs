using DndCompendium.Core.Models;

namespace DndCompendium.Core.Services;

public static class CharacterSelectionRules
{
    public static bool IsSubclassCompatible(
        IEnumerable<CharacterCompendiumSelection> selections,
        string? parentClass)
    {
        if (string.IsNullOrWhiteSpace(parentClass)) return true;
        return selections.Any(item => item.IsActive
            && string.Equals(item.Kind, nameof(SearchContentKind.Class), StringComparison.Ordinal)
            && string.Equals(item.Name.Trim(), parentClass.Trim(), StringComparison.CurrentCultureIgnoreCase));
    }

    public static bool IsAvailableAtLevel(string? requiredLevel, int? characterLevel)
        => characterLevel is null || !int.TryParse(requiredLevel, out var parsed) || parsed <= characterLevel;

    public static string? GetUnavailableReason(
        SearchContentKind kind,
        string? parentClass,
        string? parentName,
        string? requiredLevel,
        string? requirements,
        IEnumerable<CharacterCompendiumSelection> selections,
        int characterLevel)
    {
        var active = selections.Where(item => item.IsActive).ToArray();
        if (kind == SearchContentKind.Subclass && !IsSubclassCompatible(active, parentClass))
            return $"Requiere elegir primero la clase {parentClass}.";
        if (kind == SearchContentKind.ClassFeature && !HasSelection(active, nameof(SearchContentKind.Class), parentClass))
            return $"Requiere la clase {parentClass}.";
        if (kind == SearchContentKind.SubclassFeature
            && !HasSelection(active, nameof(SearchContentKind.Subclass), parentName))
            return $"Requiere la subclase {parentName}.";
        var explicitLevel = ParseExplicitLevel(requiredLevel) ?? ParseExplicitLevel(requirements);
        return explicitLevel > characterLevel
            ? $"Requiere nivel {explicitLevel}. Tu personaje es nivel {characterLevel}."
            : null;
    }

    public static int? ParseExplicitLevel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (int.TryParse(value.Trim(), out var exact) && exact is >= 1 and <= 20) return exact;
        var match = System.Text.RegularExpressions.Regex.Match(
            value,
            @"(?i)\b(?:level|nivel)\s*(?:of\s*)?(?<level>[1-9]|1\d|20)\b");
        if (!match.Success)
            match = System.Text.RegularExpressions.Regex.Match(
                value,
                @"(?i)\b(?<level>[1-9]|1\d|20)(?:st|nd|rd|th)[ -]level\b");
        return match.Success && int.TryParse(match.Groups["level"].Value, out var parsed) ? parsed : null;
    }

    private static bool HasSelection(
        IEnumerable<CharacterCompendiumSelection> selections,
        string kind,
        string? name)
        => string.IsNullOrWhiteSpace(name) || selections.Any(item =>
            string.Equals(item.Kind, kind, StringComparison.Ordinal)
            && string.Equals(item.Name.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase));
}
