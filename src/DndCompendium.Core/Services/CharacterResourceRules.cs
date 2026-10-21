using DndCompendium.Core.Models;

namespace DndCompendium.Core.Services;

public static class CharacterResourceRules
{
    public static CharacterResource Normalize(CharacterResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var name = resource.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("El recurso necesita un nombre.", nameof(resource));
        if (name.Length > 80) throw new ArgumentException("El nombre no puede superar 80 caracteres.", nameof(resource));
        if (resource.Maximum < 0) throw new ArgumentOutOfRangeException(nameof(resource), "El máximo no puede ser negativo.");
        if (resource.RecoveryAmount is < 0) throw new ArgumentOutOfRangeException(nameof(resource), "La recuperación no puede ser negativa.");
        if (resource.Kind is CharacterResourceKind.SpellSlot or CharacterResourceKind.PactMagicSlot
            && resource.Level is not (>= 1 and <= 9))
        {
            throw new ArgumentOutOfRangeException(nameof(resource), "El nivel del slot debe estar entre 1 y 9.");
        }

        return resource with
        {
            Id = string.IsNullOrWhiteSpace(resource.Id) ? Guid.NewGuid().ToString("N") : resource.Id.Trim(),
            Name = name,
            Current = Math.Clamp(resource.Current, 0, resource.Maximum),
            Unit = EmptyToNull(resource.Unit, 30),
            Description = EmptyToNull(resource.Description, 500),
            Level = resource.Kind == CharacterResourceKind.Custom ? null : resource.Level
        };
    }

    public static CharacterResource Adjust(CharacterResource resource, int delta)
    {
        var current = (int)Math.Clamp((long)resource.Current + delta, 0, resource.Maximum);
        return Normalize(resource with { Current = current });
    }

    public static IReadOnlyList<ResourceRestChange> PreviewRest(
        IEnumerable<CharacterResource> resources,
        RestKind restKind)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var changes = new List<ResourceRestChange>();
        foreach (var rawResource in resources)
        {
            var resource = Normalize(rawResource);
            if (!RecoversOn(resource.Recovery, restKind) || resource.Current >= resource.Maximum) continue;
            var amount = resource.RecoveryAmount ?? resource.Maximum;
            var next = Math.Min(resource.Maximum, resource.Current + amount);
            if (next == resource.Current) continue;
            changes.Add(new ResourceRestChange(resource.Id, resource.Name, resource.Current, next));
        }
        return changes;
    }

    public static bool RecoversOn(ResourceRecoveryKind recovery, RestKind restKind) => restKind switch
    {
        RestKind.Short => recovery is ResourceRecoveryKind.ShortRest or ResourceRecoveryKind.ShortAndLongRest,
        RestKind.Long => recovery is ResourceRecoveryKind.LongRest or ResourceRecoveryKind.ShortAndLongRest,
        _ => false
    };

    private static string? EmptyToNull(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength) throw new ArgumentException($"El texto no puede superar {maximumLength} caracteres.");
        return normalized;
    }
}
