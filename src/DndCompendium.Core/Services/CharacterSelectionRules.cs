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
}
