using System.Text.Json;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using DndCompendium.Core.Services;

namespace DndCompendium.Infrastructure.Persistence;

public sealed class JsonCharacterProfileStore : ICharacterProfileStore
{
    private const int ProfileVersion = 4;
    private const string DefaultCharacterName = "Mi personaje";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _rootPath;
    private readonly string _statePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ProfileState? _state;

    public JsonCharacterProfileStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = Path.GetFullPath(rootPath);
        _statePath = Path.Combine(_rootPath, "profiles", "characters.json");
    }

    public event EventHandler? ActiveCharacterChanged;

    public async Task<IReadOnlyList<CharacterProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            return state.Characters
                .OrderBy(character => character.CreatedAt)
                .Select(ToModel)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterProfile> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            return ToModel(state.Characters.Single(character => character.Id == state.ActiveCharacterId));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterProfile> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        CharacterData created;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            EnsureUniqueName(state, normalizedName);
            created = new CharacterData
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = normalizedName,
                CreatedAt = DateTimeOffset.UtcNow
            };
            state.Characters.Add(created);
            state.Saved.Add(new CharacterSavedData { CharacterId = created.Id });
            state.ActiveCharacterId = created.Id;
            await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        ActiveCharacterChanged?.Invoke(this, EventArgs.Empty);
        return ToModel(created);
    }

    public async Task RenameAsync(
        string characterId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        var normalizedName = NormalizeName(name);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            var character = FindCharacter(state, characterId);
            EnsureUniqueName(state, normalizedName, characterId);
            character.Name = normalizedName;
            await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        ActiveCharacterChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteAsync(string characterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        var activeChanged = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            if (state.Characters.Count == 1)
            {
                throw new InvalidOperationException("Debe existir al menos un personaje.");
            }

            var character = FindCharacter(state, characterId);
            state.Characters.Remove(character);
            state.Saved.RemoveAll(saved => saved.CharacterId == characterId);
            if (state.ActiveCharacterId == characterId)
            {
                state.ActiveCharacterId = state.Characters.OrderBy(item => item.CreatedAt).First().Id;
                activeChanged = true;
            }
            await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (activeChanged)
        {
            ActiveCharacterChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task SetActiveAsync(string characterId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        var changed = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            FindCharacter(state, characterId);
            if (state.ActiveCharacterId != characterId)
            {
                state.ActiveCharacterId = characterId;
                changed = true;
                await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
        {
            ActiveCharacterChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<IReadOnlySet<string>> GetSavedIdsAsync(
        SavedContentKind contentKind,
        SavedStateKind stateKind,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            var saved = GetSavedData(state, state.ActiveCharacterId);
            return SelectIds(saved, contentKind, stateKind).ToHashSet(StringComparer.Ordinal);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetSavedAsync(
        SavedContentKind contentKind,
        SavedStateKind stateKind,
        string itemId,
        bool isSaved,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            var saved = GetSavedData(state, state.ActiveCharacterId);
            var ids = SelectIds(saved, contentKind, stateKind);
            if (isSaved)
            {
                if (!ids.Contains(itemId, StringComparer.Ordinal)) ids.Add(itemId);
            }
            else
            {
                ids.RemoveAll(id => string.Equals(id, itemId, StringComparison.Ordinal));
            }
            ids.Sort(StringComparer.Ordinal);
            var order = stateKind == SavedStateKind.Prepared
                ? saved.PreparedItemOrder
                : saved.FavoriteItemOrder;
            var key = SavedItemKey(contentKind, itemId);
            if (isSaved)
            {
                if (!order.Contains(key, StringComparer.Ordinal))
                {
                    order.Add(key);
                }
            }
            else
            {
                order.RemoveAll(item => string.Equals(item, key, StringComparison.Ordinal));
            }
            await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<SavedItemReference>> GetPreparedOrderAsync(
        CancellationToken cancellationToken = default)
        => GetSavedOrderAsync(SavedStateKind.Prepared, cancellationToken);

    public Task SetPreparedOrderAsync(
        IReadOnlyList<SavedItemReference> items,
        CancellationToken cancellationToken = default)
        => SetSavedOrderAsync(SavedStateKind.Prepared, items, cancellationToken);

    public Task<IReadOnlyList<SavedItemReference>> GetFavoriteOrderAsync(
        CancellationToken cancellationToken = default)
        => GetSavedOrderAsync(SavedStateKind.Favorite, cancellationToken);

    public Task SetFavoriteOrderAsync(
        IReadOnlyList<SavedItemReference> items,
        CancellationToken cancellationToken = default)
        => SetSavedOrderAsync(SavedStateKind.Favorite, items, cancellationToken);

    private async Task<IReadOnlyList<SavedItemReference>> GetSavedOrderAsync(
        SavedStateKind stateKind,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            var saved = GetSavedData(state, state.ActiveCharacterId);
            var order = stateKind == SavedStateKind.Prepared
                ? saved.PreparedItemOrder
                : saved.FavoriteItemOrder;
            return order
                .Select(ParseSavedItemKey)
                .Where(item => item is not null)
                .Cast<SavedItemReference>()
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SetSavedOrderAsync(
        SavedStateKind stateKind,
        IReadOnlyList<SavedItemReference> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            var saved = GetSavedData(state, state.ActiveCharacterId);
            var valid = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in SelectIds(saved, SavedContentKind.Spell, stateKind))
                valid.Add(SavedItemKey(SavedContentKind.Spell, id));
            foreach (var id in SelectIds(saved, SavedContentKind.Exploit, stateKind))
                valid.Add(SavedItemKey(SavedContentKind.Exploit, id));

            var order = items
                .Where(item => !string.IsNullOrWhiteSpace(item.ItemId))
                .Select(item => SavedItemKey(item.ContentKind, item.ItemId))
                .Where(valid.Contains)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            foreach (var key in valid)
            {
                if (!order.Contains(key, StringComparer.Ordinal))
                {
                    order.Add(key);
                }
            }
            if (stateKind == SavedStateKind.Prepared) saved.PreparedItemOrder = order;
            else saved.FavoriteItemOrder = order;
            await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CharacterResource>> GetResourcesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            return GetSavedData(state, state.ActiveCharacterId).Resources
                .ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task SetResourceOrderAsync(
        IReadOnlyList<string> resourceIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceIds);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            var resources = GetSavedData(state, state.ActiveCharacterId).Resources;
            var byId = resources.ToDictionary(resource => resource.Id, StringComparer.Ordinal);
            var reordered = resourceIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .ToList();
            reordered.AddRange(resources.Where(resource =>
                !reordered.Any(existing => string.Equals(existing.Id, resource.Id, StringComparison.Ordinal))));
            resources.Clear();
            resources.AddRange(reordered);
            await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<CharacterResource> UpsertResourceAsync(
        CharacterResource resource,
        CancellationToken cancellationToken = default)
    {
        var normalized = CharacterResourceRules.Normalize(resource);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            var resources = GetSavedData(state, state.ActiveCharacterId).Resources;
            var conflictingSlot = resources.FirstOrDefault(item => item.Id != normalized.Id
                && item.Kind == normalized.Kind
                && item.Kind != CharacterResourceKind.Custom
                && item.Level == normalized.Level);
            if (conflictingSlot is not null)
            {
                throw new InvalidOperationException("Ese nivel de slots ya está configurado.");
            }
            var index = resources.FindIndex(item => item.Id == normalized.Id);
            if (index >= 0) resources[index] = normalized;
            else resources.Add(normalized);
            await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
            return normalized;
        }
        finally { _gate.Release(); }
    }

    public async Task<CharacterResource> AdjustResourceAsync(
        string resourceId,
        int delta,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            var resources = GetSavedData(state, state.ActiveCharacterId).Resources;
            var index = resources.FindIndex(item => item.Id == resourceId);
            if (index < 0) throw new KeyNotFoundException("El recurso ya no existe.");
            var adjusted = CharacterResourceRules.Adjust(resources[index], delta);
            resources[index] = adjusted;
            await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
            return adjusted;
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteResourceAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            var resources = GetSavedData(state, state.ActiveCharacterId).Resources;
            if (resources.RemoveAll(item => item.Id == resourceId) == 0) return;
            await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<ResourceRestChange>> PreviewRestAsync(
        RestKind restKind,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            return CharacterResourceRules.PreviewRest(
                GetSavedData(state, state.ActiveCharacterId).Resources,
                restKind);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<ResourceRestChange>> ApplyRestAsync(
        RestKind restKind,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            var resources = GetSavedData(state, state.ActiveCharacterId).Resources;
            var changes = CharacterResourceRules.PreviewRest(resources, restKind);
            foreach (var change in changes)
            {
                var index = resources.FindIndex(item => item.Id == change.ResourceId);
                if (index >= 0) resources[index] = resources[index] with { Current = change.Current };
            }
            if (changes.Count > 0) await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
            return changes;
        }
        finally { _gate.Release(); }
    }

    private async Task<ProfileState> EnsureStateAsync(CancellationToken cancellationToken)
    {
        if (_state is not null) return _state;

        if (File.Exists(_statePath))
        {
            try
            {
                var input = await File.ReadAllBytesAsync(_statePath, cancellationToken).ConfigureAwait(false);
                var stored = JsonSerializer.Deserialize<ProfileState>(input, JsonOptions);
                if (stored is not null && stored.Characters.Count > 0)
                {
                    var requiresMigration = stored.Version < ProfileVersion;
                    NormalizeState(stored);
                    _state = stored;
                    if (requiresMigration) await SaveStateAsync(stored, cancellationToken).ConfigureAwait(false);
                    return stored;
                }
            }
            catch (JsonException)
            {
                var backup = $"{_statePath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
                File.Copy(_statePath, backup, overwrite: false);
            }
        }

        var character = new CharacterData
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = DefaultCharacterName,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var migrated = new CharacterSavedData
        {
            CharacterId = character.Id,
            FavoriteSpellIds = await ReadLegacyIdsAsync("spells", "favorite-spells.json", cancellationToken).ConfigureAwait(false),
            PreparedSpellIds = await ReadLegacyIdsAsync("spells", "prepared-spells.json", cancellationToken).ConfigureAwait(false),
            FavoriteExploitIds = await ReadLegacyIdsAsync("exploits", "favorite-exploits.json", cancellationToken).ConfigureAwait(false),
            PreparedExploitIds = await ReadLegacyIdsAsync("exploits", "prepared-exploits.json", cancellationToken).ConfigureAwait(false)
        };
        _state = new ProfileState
        {
            ActiveCharacterId = character.Id,
            Characters = [character],
            Saved = [migrated]
        };
        await SaveStateAsync(_state, cancellationToken).ConfigureAwait(false);
        return _state;
    }

    private async Task<List<string>> ReadLegacyIdsAsync(
        string directory,
        string fileName,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_rootPath, directory, fileName);
        if (!File.Exists(path)) return [];
        try
        {
            await using var input = File.OpenRead(path);
            return (await JsonSerializer.DeserializeAsync<string[]>(input, JsonOptions, cancellationToken)
                    .ConfigureAwait(false) ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void NormalizeState(ProfileState state)
    {
        state.Characters = state.Characters
            .Where(character => !string.IsNullOrWhiteSpace(character.Id) && !string.IsNullOrWhiteSpace(character.Name))
            .GroupBy(character => character.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        if (state.Characters.Count == 0) throw new JsonException("No hay personajes válidos.");
        if (!state.Characters.Any(character => character.Id == state.ActiveCharacterId))
        {
            state.ActiveCharacterId = state.Characters[0].Id;
        }
        state.Saved ??= [];
        foreach (var character in state.Characters)
        {
            var saved = GetSavedData(state, character.Id);
            saved.Resources ??= [];
            saved.FavoriteSpellIds ??= [];
            saved.PreparedSpellIds ??= [];
            saved.FavoriteExploitIds ??= [];
            saved.PreparedExploitIds ??= [];
            saved.PreparedItemOrder ??= [];
            saved.FavoriteItemOrder ??= [];
            saved.PreparedItemOrder = NormalizeSavedOrder(saved, SavedStateKind.Prepared, saved.PreparedItemOrder);
            saved.FavoriteItemOrder = NormalizeSavedOrder(saved, SavedStateKind.Favorite, saved.FavoriteItemOrder);
            var normalizedResources = new List<CharacterResource>();
            foreach (var resource in saved.Resources)
            {
                try { normalizedResources.Add(CharacterResourceRules.Normalize(resource)); }
                catch (ArgumentException) { }
            }
            saved.Resources = normalizedResources;
        }
        state.Version = ProfileVersion;
    }

    private static CharacterSavedData GetSavedData(ProfileState state, string characterId)
    {
        var saved = state.Saved.FirstOrDefault(item => item.CharacterId == characterId);
        if (saved is not null) return saved;
        saved = new CharacterSavedData { CharacterId = characterId };
        state.Saved.Add(saved);
        return saved;
    }

    private static List<string> SelectIds(
        CharacterSavedData saved,
        SavedContentKind contentKind,
        SavedStateKind stateKind)
        => (contentKind, stateKind) switch
        {
            (SavedContentKind.Spell, SavedStateKind.Favorite) => saved.FavoriteSpellIds,
            (SavedContentKind.Spell, SavedStateKind.Prepared) => saved.PreparedSpellIds,
            (SavedContentKind.Exploit, SavedStateKind.Favorite) => saved.FavoriteExploitIds,
            (SavedContentKind.Exploit, SavedStateKind.Prepared) => saved.PreparedExploitIds,
            _ => throw new ArgumentOutOfRangeException()
        };

    private static List<string> NormalizeSavedOrder(
        CharacterSavedData saved,
        SavedStateKind stateKind,
        IEnumerable<string> existingOrder)
    {
        var valid = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in SelectIds(saved, SavedContentKind.Spell, stateKind)
                     .Where(id => !string.IsNullOrWhiteSpace(id)))
            valid.Add(SavedItemKey(SavedContentKind.Spell, id));
        foreach (var id in SelectIds(saved, SavedContentKind.Exploit, stateKind)
                     .Where(id => !string.IsNullOrWhiteSpace(id)))
            valid.Add(SavedItemKey(SavedContentKind.Exploit, id));

        var normalized = existingOrder
            .Where(valid.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var key in valid.Order(StringComparer.Ordinal))
        {
            if (!normalized.Contains(key, StringComparer.Ordinal)) normalized.Add(key);
        }
        return normalized;
    }

    private static string SavedItemKey(SavedContentKind contentKind, string itemId)
        => $"{(contentKind == SavedContentKind.Spell ? "spell" : "exploit")}:{itemId}";

    private static SavedItemReference? ParseSavedItemKey(string key)
    {
        if (key.StartsWith("spell:", StringComparison.Ordinal))
            return new(SavedContentKind.Spell, key[6..]);
        if (key.StartsWith("exploit:", StringComparison.Ordinal))
            return new(SavedContentKind.Exploit, key[8..]);
        return null;
    }

    private static CharacterData FindCharacter(ProfileState state, string characterId)
        => state.Characters.FirstOrDefault(character => character.Id == characterId)
            ?? throw new KeyNotFoundException("El personaje ya no existe.");

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (normalized.Length > 60) throw new ArgumentException("El nombre no puede superar 60 caracteres.", nameof(name));
        return normalized;
    }

    private static void EnsureUniqueName(ProfileState state, string name, string? excludingId = null)
    {
        if (state.Characters.Any(character => character.Id != excludingId
            && string.Equals(character.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new InvalidOperationException("Ya existe un personaje con ese nombre.");
        }
    }

    private async Task SaveStateAsync(ProfileState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var temporary = $"{_statePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static CharacterProfile ToModel(CharacterData character)
        => new(character.Id, character.Name, character.CreatedAt);

    private sealed class ProfileState
    {
        public int Version { get; set; } = ProfileVersion;
        public string ActiveCharacterId { get; set; } = string.Empty;
        public List<CharacterData> Characters { get; set; } = [];
        public List<CharacterSavedData> Saved { get; set; } = [];
    }

    private sealed class CharacterData
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class CharacterSavedData
    {
        public string CharacterId { get; set; } = string.Empty;
        public List<string> FavoriteSpellIds { get; set; } = [];
        public List<string> PreparedSpellIds { get; set; } = [];
        public List<string> FavoriteExploitIds { get; set; } = [];
        public List<string> PreparedExploitIds { get; set; } = [];
        public List<string> PreparedItemOrder { get; set; } = [];
        public List<string> FavoriteItemOrder { get; set; } = [];
        public List<CharacterResource> Resources { get; set; } = [];
    }
}
