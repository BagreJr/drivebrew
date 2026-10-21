using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using DndCompendium.Infrastructure.Persistence;

namespace DndCompendium.Infrastructure.Sources;

public sealed class GoogleDriveSpellRepository : ISpellRepository, IDisposable
{
    public const string SpellFileId = "1aEya3508gRqRNIVJuiQ9dCBe2iY_tGg6";
    private const long MaximumJsonBytes = 8L * 1024 * 1024;
    private static readonly Uri DownloadUri = new(
        $"https://drive.usercontent.google.com/download?id={SpellFileId}&export=download&confirm=t");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static readonly (string JsonName, string DisplayName)[] ClassFields =
    [
        ("artificer", "Artificer"),
        ("bardo", "Bard"),
        ("clerigo", "Cleric"),
        ("druida", "Druid"),
        ("investigator", "Investigator"),
        ("magus", "Magus"),
        ("martyrs", "Martyr"),
        ("necromancer", "Necromancer"),
        ("paladin", "Paladin"),
        ("ranger", "Ranger"),
        ("shaman", "Shaman"),
        ("sorcerer", "Sorcerer"),
        ("vessel", "Vessel"),
        ("warlock", "Warlock"),
        ("warmage", "Warmage"),
        ("witch", "Witch"),
        ("wizard", "Wizard")
    ];

    private readonly HttpClient _httpClient;
    private readonly ICharacterProfileStore _profiles;
    private readonly string _catalogPath;
    private readonly string _metadataPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<Spell>? _cachedSpells;
    private DateTimeOffset _lastRemoteCheck;
    private bool _disposed;

    public GoogleDriveSpellRepository(
        string rootPath,
        HttpClient httpClient,
        ICharacterProfileStore? profiles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _profiles = profiles ?? new JsonCharacterProfileStore(rootPath);
        var directory = Path.Combine(Path.GetFullPath(rootPath), "spells");
        _catalogPath = Path.Combine(directory, "spells.json");
        _metadataPath = Path.Combine(directory, "spells.meta.json");
    }

    public async Task<IReadOnlyList<Spell>> GetAllAsync(
        bool refreshFromDrive = true,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);
            if (refreshFromDrive && DateTimeOffset.UtcNow - _lastRemoteCheck > TimeSpan.FromMinutes(2))
            {
                _lastRemoteCheck = DateTimeOffset.UtcNow;
                try
                {
                    await RefreshFromDriveAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception) when (File.Exists(_catalogPath))
                {
                    // A validated local copy always remains usable when Drive is unavailable.
                }
            }

            if (_cachedSpells is not null)
            {
                return _cachedSpells;
            }

            if (!File.Exists(_catalogPath))
            {
                throw new IOException("No se pudo descargar spells.json y todavía no existe una copia offline.");
            }

            var bytes = await File.ReadAllBytesAsync(_catalogPath, cancellationToken).ConfigureAwait(false);
            _cachedSpells = Parse(bytes);
            return _cachedSpells;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<Spell>> UpdateFromDriveAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);
            _lastRemoteCheck = DateTimeOffset.UtcNow;
            await RefreshFromDriveAsync(cancellationToken, forceDownload: true).ConfigureAwait(false);
            return _cachedSpells
                ?? throw new IOException("La actualización no produjo un catálogo de spells válido.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlySet<string>> GetPreparedIdsAsync(CancellationToken cancellationToken = default)
        => await _profiles.GetSavedIdsAsync(
            SavedContentKind.Spell,
            SavedStateKind.Prepared,
            cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlySet<string>> GetFavoriteIdsAsync(CancellationToken cancellationToken = default)
        => await _profiles.GetSavedIdsAsync(
            SavedContentKind.Spell,
            SavedStateKind.Favorite,
            cancellationToken).ConfigureAwait(false);

    public async Task SetPreparedAsync(
        string spellId,
        bool isPrepared,
        CancellationToken cancellationToken = default)
        => await _profiles.SetSavedAsync(
            SavedContentKind.Spell,
            SavedStateKind.Prepared,
            spellId,
            isPrepared,
            cancellationToken).ConfigureAwait(false);

    public async Task SetFavoriteAsync(
        string spellId,
        bool isFavorite,
        CancellationToken cancellationToken = default)
        => await _profiles.SetSavedAsync(
            SavedContentKind.Spell,
            SavedStateKind.Favorite,
            spellId,
            isFavorite,
            cancellationToken).ConfigureAwait(false);

    private async Task RefreshFromDriveAsync(
        CancellationToken cancellationToken,
        bool forceDownload = false)
    {
        var metadata = await ReadMetadataAsync(cancellationToken).ConfigureAwait(false);
        var remote = await ReadRemoteMetadataAsync(cancellationToken).ConfigureAwait(false);
        if (!forceDownload
            && File.Exists(_catalogPath)
            && remote is not null
            && metadata is not null
            && remote.SizeBytes == metadata.SizeBytes
            && remote.ModifiedAt == metadata.ModifiedAt)
        {
            return;
        }

        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, DownloadUri);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Google Drive devolvió HTML en lugar de spells.json.");
                }

                if (response.Content.Headers.ContentLength is > MaximumJsonBytes)
                {
                    throw new InvalidDataException("spells.json supera el tamaño máximo permitido.");
                }

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var output = new MemoryStream();
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                if (output.Length == 0 || output.Length > MaximumJsonBytes)
                {
                    throw new InvalidDataException("spells.json está vacío o supera el tamaño máximo permitido.");
                }

                var bytes = output.ToArray();
                var parsed = Parse(bytes);
                await WriteBytesAtomicallyAsync(_catalogPath, bytes, cancellationToken).ConfigureAwait(false);
                var installedMetadata = remote ?? new SpellCatalogMetadata(
                    bytes.LongLength,
                    response.Content.Headers.LastModified ?? DateTimeOffset.UtcNow);
                await WriteJsonAtomicallyAsync(_metadataPath, installedMetadata, cancellationToken).ConfigureAwait(false);
                _cachedSpells = parsed;
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
            {
                lastError = exception;
                if (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(350 * attempt), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw new IOException("No se pudo descargar spells.json después de tres intentos.", lastError);
    }

    private async Task<SpellCatalogMetadata?> ReadRemoteMetadataAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, DownloadUri);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode
                && response.Content.Headers.ContentLength is long size
                && response.Content.Headers.LastModified is DateTimeOffset modifiedAt)
            {
                return new SpellCatalogMetadata(size, modifiedAt);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
        }

        return null;
    }

    private async Task<SpellCatalogMetadata?> ReadMetadataAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_metadataPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_metadataPath);
            return await JsonSerializer.DeserializeAsync<SpellCatalogMetadata>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<Spell> Parse(ReadOnlySpan<byte> json)
    {
        var rows = JsonSerializer.Deserialize<SpellRow[]>(json, JsonOptions)
            ?? throw new InvalidDataException("spells.json no contiene una lista válida.");
        if (rows.Length == 0)
        {
            throw new InvalidDataException("spells.json no contiene conjuros.");
        }

        var spells = new List<Spell>(rows.Length);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Name) || !int.TryParse(row.Level, out var level) || level is < 0 or > 9)
            {
                continue;
            }

            var classValues = row.ClassValues;
            var classes = ClassFields
                .Where(field => IsEnabled(classValues.GetValueOrDefault(field.JsonName)))
                .Select(field => field.DisplayName)
                .ToArray();
            var origins = new List<string>(3);
            if (IsEnabled(row.Arcane)) origins.Add("Arcano");
            if (IsEnabled(row.Divine)) origins.Add("Divino");
            if (IsEnabled(row.Primal)) origins.Add("Primal");

            spells.Add(new Spell
            {
                Id = CreateSpellId(row.Name, level),
                Name = row.Name.Trim(),
                Level = level,
                School = Normalize(row.School),
                CastingTime = Normalize(row.CastingTime),
                Range = Normalize(row.Range),
                Duration = Normalize(row.Duration),
                IsConcentration = IsYes(row.Concentration),
                IsRitual = IsYes(row.Ritual),
                HasVerbalComponent = IsYes(row.Verbal),
                HasSomaticComponent = IsYes(row.Somatic),
                HasMaterialComponent = IsYes(row.Material),
                MaterialDescription = Normalize(row.MaterialDescription),
                Effect = Normalize(row.Effect),
                HigherLevels = Normalize(row.HigherLevels),
                Classes = classes,
                Origins = origins
            });
        }

        if (spells.Count == 0)
        {
            throw new InvalidDataException("spells.json no contiene filas reconocibles.");
        }

        return spells.OrderBy(spell => spell.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string CreateSpellId(string name, int level)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{name.Trim().ToLowerInvariant()}\n{level}")))
            [..24].ToLowerInvariant();

    private static bool IsEnabled(string? value) => string.Equals(value?.Trim(), "1", StringComparison.OrdinalIgnoreCase);

    private static bool IsYes(string? value) => string.Equals(value?.Trim(), "si", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), "sí", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), "yes", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), "1", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static async Task WriteBytesAtomicallyAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes.ToArray(), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task WriteJsonAtomicallyAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        await WriteBytesAtomicallyAsync(path, bytes, cancellationToken).ConfigureAwait(false);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
        _httpClient.Dispose();
    }

    private sealed record SpellCatalogMetadata(long SizeBytes, DateTimeOffset ModifiedAt);

    private sealed class SpellRow
    {
        [JsonPropertyName("Nombre")] public string? Name { get; init; }
        [JsonPropertyName("Nivel")] public string? Level { get; init; }
        [JsonPropertyName("D")] public string? Divine { get; init; }
        [JsonPropertyName("P")] public string? Primal { get; init; }
        [JsonPropertyName("A")] public string? Arcane { get; init; }
        [JsonPropertyName("Escuela")] public string? School { get; init; }
        [JsonPropertyName("Casting time")] public string? CastingTime { get; init; }
        [JsonPropertyName("Range")] public string? Range { get; init; }
        [JsonPropertyName("duracion")] public string? Duration { get; init; }
        [JsonPropertyName("concentracion")] public string? Concentration { get; init; }
        [JsonPropertyName("efecto")] public string? Effect { get; init; }
        [JsonPropertyName("level alto")] public string? HigherLevels { get; init; }
        [JsonPropertyName("verbal")] public string? Verbal { get; init; }
        [JsonPropertyName("somatico")] public string? Somatic { get; init; }
        [JsonPropertyName("material")] public string? Material { get; init; }
        [JsonPropertyName("descripcion material")] public string? MaterialDescription { get; init; }
        [JsonPropertyName("ritual")] public string? Ritual { get; init; }
        [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonIgnore]
        public IReadOnlyDictionary<string, string?> ClassValues => Extra.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ValueKind == JsonValueKind.String ? pair.Value.GetString() : pair.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);
    }
}
