using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using DndCompendium.Core.Services;

namespace DndCompendium.Infrastructure.Persistence;

public sealed class UniversalSearchService : IUniversalSearchService, IDisposable
{
    private const int IndexVersion = 6;
    private static readonly IReadOnlyDictionary<string, string> SearchAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dex save"] = "dexterity saving throw",
            ["dexterity save"] = "dexterity saving throw",
            ["dexterity saves"] = "dexterity saving throw",
            ["con save"] = "constitution saving throw",
            ["constitution save"] = "constitution saving throw",
            ["constitution saves"] = "constitution saving throw",
            ["bonus actions"] = "bonus action",
            ["hp"] = "hit points",
            ["hit point"] = "hit points"
        };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ILocalDocumentRepository _documents;
    private readonly ISpellRepository _spells;
    private readonly IExploitRepository _exploits;
    private readonly ICharacterProfileStore _profiles;
    private readonly string _indexPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SearchIndexState? _state;
    private bool _disposed;

    public UniversalSearchService(
        string rootPath,
        ILocalDocumentRepository documents,
        ISpellRepository spells,
        IExploitRepository exploits,
        ICharacterProfileStore profiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _documents = documents;
        _spells = spells;
        _exploits = exploits;
        _profiles = profiles;
        _indexPath = Path.Combine(Path.GetFullPath(rootPath), "search", "universal-index.json");
    }

    public async Task RefreshIndexAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            var changed = false;
            await _documents.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var library = await _documents.GetDocumentsAsync(cancellationToken).ConfigureAwait(false);
            var currentDocumentKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var document in library.Where(item => item.HasLocalCopy))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var content = await _documents.ReadAsync(document.Id, cancellationToken).ConfigureAwait(false);
                if (content is null) continue;
                var sourceKey = $"document:{document.Id}";
                currentDocumentKeys.Add(sourceKey);
                if (state.Sources.TryGetValue(sourceKey, out var stored)
                    && string.Equals(stored.Fingerprint, content.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                state.Sources[sourceKey] = new IndexedSource(
                    content.Sha256,
                    HomebrewSearchIndexer.IndexDocument(document, content).ToList());
                changed = true;
            }

            foreach (var obsolete in state.Sources.Keys
                         .Where(key => key.StartsWith("document:", StringComparison.Ordinal)
                             && !currentDocumentKeys.Contains(key)).ToArray())
            {
                state.Sources.Remove(obsolete);
                changed = true;
            }

            changed |= await RefreshSpellsAsync(state, cancellationToken).ConfigureAwait(false);
            changed |= await RefreshExploitsAsync(state, cancellationToken).ConfigureAwait(false);
            if (changed) await SaveStateAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        SearchContentKind? kind = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
        => await SearchAsync(new SearchRequest { Query = query, Kind = kind, Limit = limit }, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query) || request.Limit <= 0) return [];
        SearchIndexEntry[] entries;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            entries = state.Sources.Values.SelectMany(source => source.Entries).ToArray();
        }
        finally { _gate.Release(); }

        var favoriteSpellTask = SafeFavoritesAsync(SavedContentKind.Spell, cancellationToken);
        var favoriteExploitTask = SafeFavoritesAsync(SavedContentKind.Exploit, cancellationToken);
        await Task.WhenAll(favoriteSpellTask, favoriteExploitTask).ConfigureAwait(false);
        var favoriteSpells = await favoriteSpellTask.ConfigureAwait(false);
        var favoriteExploits = await favoriteExploitTask.ConfigureAwait(false);
        var query = QueryPlan.Create(request.Query);
        var deduplicated = new Dictionary<string, SearchResult>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Kind.HasValue && !MatchesKind(entry.Kind, request.Kind.Value)) continue;
            if (!MatchesOrigin(entry, request.Origin) || !MatchesLevel(entry, request.Level)) continue;
            var score = Score(entry, query);
            if (score <= 0) continue;
            var favorite = entry.Kind switch
            {
                SearchContentKind.Spell => entry.ReferenceId is not null && favoriteSpells.Contains(entry.ReferenceId),
                SearchContentKind.Exploit => entry.ReferenceId is not null && favoriteExploits.Contains(entry.ReferenceId),
                _ => false
            };
            var result = new SearchResult(entry, score, CreateSnippet(entry, query), favorite)
            {
                MatchedTerms = MatchTerms(request.Query, query.Canonical)
            };
            var key = DeduplicationKey(entry);
            if (!deduplicated.TryGetValue(key, out var previous) || result.Score > previous.Score)
                deduplicated[key] = result;
        }
        return deduplicated.Values.OrderByDescending(result => result.Score)
            .ThenBy(result => result.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Min(request.Limit, 250)).ToArray();
    }

    public async Task<SearchFilterOptions> GetFilterOptionsAsync(
        SearchContentKind? kind = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            var entries = state.Sources.Values.SelectMany(source => source.Entries)
                .Where(entry => !kind.HasValue || MatchesKind(entry.Kind, kind.Value)).ToArray();
            var origins = entries.SelectMany(OriginValues).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            var levels = entries.Select(entry => entry.Level).Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(LevelSortKey).ThenBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
            return new SearchFilterOptions(origins, levels);
        }
        finally { _gate.Release(); }
    }

    public async Task<int> GetEntryCountAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await EnsureStateAsync(cancellationToken).ConfigureAwait(false);
            return state.Sources.Values.Sum(source => source.Entries.Count);
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> RefreshSpellsAsync(SearchIndexState state, CancellationToken cancellationToken)
    {
        IReadOnlyList<Spell> spells;
        try { spells = await _spells.GetAllAsync(false, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return false; }
        var fingerprint = Fingerprint(spells.Select(spell => string.Join('|',
            spell.Id, spell.Name, spell.Level, spell.School, spell.CastingTime, spell.Range, spell.Duration,
            spell.IsConcentration, spell.IsRitual, spell.HasVerbalComponent, spell.HasSomaticComponent,
            spell.HasMaterialComponent, spell.MaterialDescription, spell.Effect, spell.HigherLevels,
            string.Join(',', spell.Classes), string.Join(',', spell.Origins))));
        const string sourceKey = "catalog:spells";
        if (state.Sources.TryGetValue(sourceKey, out var stored) && stored.Fingerprint == fingerprint) return false;
        state.Sources[sourceKey] = new IndexedSource(fingerprint, spells.Select(spell => new SearchIndexEntry
        {
            Id = $"spell:{spell.Id}", SourceKey = sourceKey, Kind = SearchContentKind.Spell,
            Name = spell.Name,
            Description = SpellSearchText(spell),
            Origin = string.Join(", ", spell.Classes.Concat(spell.Origins).Distinct(StringComparer.OrdinalIgnoreCase)),
            Parent = spell.School,
            Level = spell.Level.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Requirements = SpellRequirements(spell),
            ReferenceId = spell.Id
        }).ToList());
        return true;
    }

    private async Task<bool> RefreshExploitsAsync(SearchIndexState state, CancellationToken cancellationToken)
    {
        IReadOnlyList<ExploitEntry> exploits;
        try { exploits = await _exploits.GetAllAsync(false, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return false; }
        var fingerprint = Fingerprint(exploits.Select(item => string.Join('|', item.Id, item.Name, item.Level,
            item.LevelLabel, item.Type, item.Prerequisite, item.Effect, string.Join(',', item.Classes))));
        const string sourceKey = "catalog:exploits";
        if (state.Sources.TryGetValue(sourceKey, out var stored) && stored.Fingerprint == fingerprint) return false;
        state.Sources[sourceKey] = new IndexedSource(fingerprint, exploits.Select(item => new SearchIndexEntry
        {
            Id = $"exploit:{item.Id}", SourceKey = sourceKey, Kind = SearchContentKind.Exploit,
            Name = item.Name, Description = ExploitSearchText(item),
            Origin = item.Classes.Count > 0 ? string.Join(", ", item.Classes) : item.Type,
            Parent = item.Type, Level = item.LevelLabel,
            Requirements = item.Prerequisite == "—" ? null : item.Prerequisite,
            ReferenceId = item.Id
        }).ToList());
        return true;
    }

    private async Task<SearchIndexState> EnsureStateAsync(CancellationToken cancellationToken)
    {
        if (_state is not null) return _state;
        if (File.Exists(_indexPath))
        {
            try
            {
                await using var input = File.OpenRead(_indexPath);
                var stored = await JsonSerializer.DeserializeAsync<SearchIndexState>(input, JsonOptions, cancellationToken).ConfigureAwait(false);
                if (stored?.Version == IndexVersion && stored.Sources is not null) return _state = stored;
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return _state = new SearchIndexState { Version = IndexVersion };
    }

    private async Task SaveStateAsync(SearchIndexState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_indexPath)!);
        var temporary = $"{_indexPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                await JsonSerializer.SerializeAsync(output, state, JsonOptions, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _indexPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<IReadOnlySet<string>> SafeFavoritesAsync(SavedContentKind kind, CancellationToken token)
    {
        try { return await _profiles.GetSavedIdsAsync(kind, SavedStateKind.Favorite, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return new HashSet<string>(StringComparer.Ordinal); }
    }

    private static double Score(SearchIndexEntry entry, QueryPlan query)
    {
        var rawName = HomebrewSearchIndexer.NormalizeForSearch(entry.Name);
        var rawDescription = HomebrewSearchIndexer.NormalizeForSearch(entry.Description);
        var rawContext = HomebrewSearchIndexer.NormalizeForSearch(
            $"{entry.Origin} {entry.ParentClass} {entry.ParentSubclass} {entry.Parent} {entry.Level} {entry.Requirements}");
        var name = Canonicalize(entry.Name);
        var description = Canonicalize(entry.Description);
        var context = Canonicalize($"{entry.Origin} {entry.ParentClass} {entry.ParentSubclass} {entry.Parent} {entry.Level} {entry.Requirements}");
        var content = $"{name} {context} {description}";
        if (rawName == query.Normalized) return 1500;
        if (name == query.Canonical) return 1450;
        if (rawName.StartsWith(query.Normalized, StringComparison.Ordinal)) return 1320 - Math.Min(100, rawName.Length - query.Normalized.Length);
        if (name.StartsWith(query.Canonical, StringComparison.Ordinal)) return 1280 - Math.Min(100, name.Length - query.Canonical.Length);
        if (rawName.Contains(query.Normalized, StringComparison.Ordinal)) return 1190 - Math.Min(100, rawName.IndexOf(query.Normalized, StringComparison.Ordinal));
        if (name.Contains(query.Canonical, StringComparison.Ordinal)) return 1150 - Math.Min(100, name.IndexOf(query.Canonical, StringComparison.Ordinal));
        if (query.Tokens.Count > 1 && query.Tokens.All(token => ContainsWholeOrPartialWord(name, token))) return 1080;
        if (rawDescription.Contains(query.Normalized, StringComparison.Ordinal)) return 960;
        if (rawContext.Contains(query.Normalized, StringComparison.Ordinal)) return 930;
        if (description.Contains(query.Canonical, StringComparison.Ordinal)) return 900;
        if (context.Contains(query.Canonical, StringComparison.Ordinal)) return 870;
        if (query.Tokens.All(token => ContainsWholeWord(content, token))) return 740;
        if (query.Tokens.All(token => ContainsWholeOrPartialWord(content, token))) return 620;
        if (query.Tokens.Count <= 3 && IsSmallTypoMatch(name, query.Tokens)) return 350;
        return 0;
    }

    private static bool ContainsWholeWord(string value, string token)
        => $" {value} ".Contains($" {token} ", StringComparison.Ordinal);

    private static bool ContainsWholeOrPartialWord(string value, string token)
    {
        if (token.Length < 3) return ContainsWholeWord(value, token);
        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.Contains(token, StringComparison.Ordinal));
    }

    private static bool IsSmallTypoMatch(string name, IReadOnlyList<string> tokens)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.All(query => query.Length >= 4 && words.Any(candidate =>
            Math.Abs(candidate.Length - query.Length) <= 1 && EditDistanceAtMostOne(candidate, query)));
    }

    private static bool EditDistanceAtMostOne(string left, string right)
    {
        if (left == right) return true;
        if (Math.Abs(left.Length - right.Length) > 1) return false;
        if (left.Length > right.Length) (left, right) = (right, left);
        var i = 0; var j = 0; var edits = 0;
        while (i < left.Length && j < right.Length)
        {
            if (left[i] == right[j]) { i++; j++; continue; }
            if (++edits > 1) return false;
            if (left.Length == right.Length) i++;
            j++;
        }
        if (i < left.Length || j < right.Length) edits++;
        return edits <= 1;
    }

    private static string CreateSnippet(SearchIndexEntry entry, QueryPlan query)
    {
        var text = string.IsNullOrWhiteSpace(entry.Description) ? entry.Origin : entry.Description;
        if (text.Length <= 220) return text;
        var map = NormalizedTextMap.Create(text);
        var normalizedPosition = map.Text.IndexOf(query.Normalized, StringComparison.Ordinal);
        if (normalizedPosition < 0) normalizedPosition = map.Text.IndexOf(query.Canonical, StringComparison.Ordinal);
        if (normalizedPosition < 0)
        {
            var token = query.Tokens.FirstOrDefault(candidate => map.Text.Contains(candidate, StringComparison.Ordinal));
            normalizedPosition = token is null ? 0 : map.Text.IndexOf(token, StringComparison.Ordinal);
        }
        var position = map.OriginalPosition(normalizedPosition);
        var start = Math.Max(0, Math.Min(text.Length - 1, position - 65));
        if (start > 0) { var space = text.IndexOf(' ', start); if (space >= 0) start = space + 1; }
        var length = Math.Min(220, text.Length - start);
        return $"{(start > 0 ? "…" : string.Empty)}{text.Substring(start, length).Trim()}{(start + length < text.Length ? "…" : string.Empty)}";
    }

    private static string Canonicalize(string value)
    {
        var normalized = HomebrewSearchIndexer.NormalizeForSearch(value);
        foreach (var alias in SearchAliases.OrderByDescending(item => item.Key.Length))
        {
            normalized = ReplaceWholePhrase(normalized, alias.Key, alias.Value);
        }
        return normalized;
    }

    private static string ReplaceWholePhrase(string value, string oldValue, string newValue)
    {
        var padded = $" {value} ";
        return padded.Replace($" {oldValue} ", $" {newValue} ", StringComparison.Ordinal).Trim();
    }

    private static IReadOnlyList<string> MatchTerms(string originalQuery, string canonicalQuery)
        => new[] { originalQuery.Trim(), canonicalQuery }
            .Concat(canonicalQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(token => token.Length >= 3))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(term => term.Length).ToArray();

    private static string DeduplicationKey(SearchIndexEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.ReferenceId))
            return $"{entry.Kind}:reference:{entry.ReferenceId}";
        var identity = string.Join('|', entry.Kind, Canonicalize(entry.Name), Canonicalize(entry.ParentClass ?? string.Empty),
            Canonicalize(entry.ParentSubclass ?? string.Empty), Canonicalize(entry.Level ?? string.Empty),
            Canonicalize(entry.Description));
        return $"semantic:{Fingerprint([identity])}";
    }

    private static string SpellSearchText(Spell spell)
    {
        var components = string.Join(", ", new[]
        {
            spell.HasVerbalComponent ? "Verbal" : null,
            spell.HasSomaticComponent ? "Somatic" : null,
            spell.HasMaterialComponent ? "Material" : null
        }.Where(value => value is not null));
        return string.Join(' ', new[]
        {
            spell.Effect,
            spell.HigherLevels == "—" ? null : $"At higher levels: {spell.HigherLevels}",
            $"School: {spell.School}.", $"Casting time: {spell.CastingTime}.", $"Range: {spell.Range}.",
            $"Duration: {spell.Duration}.", $"Components: {components}.",
            spell.HasMaterialComponent && spell.MaterialDescription != "—" ? $"Materials: {spell.MaterialDescription}." : null,
            spell.IsConcentration ? "Concentration." : null, spell.IsRitual ? "Ritual." : null,
            spell.Classes.Count > 0 ? $"Classes: {string.Join(", ", spell.Classes)}." : null,
            spell.Origins.Count > 0 ? $"Origins: {string.Join(", ", spell.Origins)}." : null
        }.Where(value => !string.IsNullOrWhiteSpace(value))!);
    }

    private static string SpellRequirements(Spell spell)
    {
        var components = string.Concat(spell.HasVerbalComponent ? "V" : string.Empty,
            spell.HasSomaticComponent ? "S" : string.Empty,
            spell.HasMaterialComponent ? "M" : string.Empty);
        return spell.HasMaterialComponent && spell.MaterialDescription != "—"
            ? $"{components} · {spell.MaterialDescription}"
            : components;
    }

    private static string ExploitSearchText(ExploitEntry item)
        => string.Join(' ', new[]
        {
            item.Effect, $"Type: {item.Type}.", $"Level: {item.LevelLabel}.",
            item.Prerequisite == "—" ? null : $"Prerequisite: {item.Prerequisite}.",
            item.Classes.Count > 0 ? $"Classes: {string.Join(", ", item.Classes)}." : null
        }.Where(value => !string.IsNullOrWhiteSpace(value))!);

    private static bool MatchesOrigin(SearchIndexEntry entry, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return true;
        var target = HomebrewSearchIndexer.NormalizeForSearch(requested);
        return OriginValues(entry).Any(value => HomebrewSearchIndexer.NormalizeForSearch(value) == target);
    }

    private static bool MatchesKind(SearchContentKind actual, SearchContentKind requested)
        => requested == SearchContentKind.Subclass
            ? actual is SearchContentKind.Subclass or SearchContentKind.SubclassFeature
            : actual == requested;

    private static IEnumerable<string> OriginValues(SearchIndexEntry entry)
    {
        foreach (var value in entry.Origin.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return value;
        if (!string.IsNullOrWhiteSpace(entry.ParentClass)) yield return entry.ParentClass;
    }

    private static bool MatchesLevel(SearchIndexEntry entry, string? requested)
        => string.IsNullOrWhiteSpace(requested)
            || string.Equals(entry.Level, requested, StringComparison.OrdinalIgnoreCase);

    private static int LevelSortKey(string value)
        => int.TryParse(new string(value.TakeWhile(char.IsDigit).ToArray()), out var level) ? level : int.MaxValue;

    private static string Fingerprint(IEnumerable<string> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var row in rows) { hash.AppendData(Encoding.UTF8.GetBytes(row)); hash.AppendData([0]); }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private sealed record QueryPlan(string Normalized, string Canonical, IReadOnlyList<string> Tokens)
    {
        public static QueryPlan Create(string query)
        {
            var normalized = HomebrewSearchIndexer.NormalizeForSearch(query);
            var canonical = Canonicalize(query);
            return new QueryPlan(normalized, canonical,
                canonical.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.Ordinal).ToArray());
        }
    }

    private sealed record NormalizedTextMap(string Text, IReadOnlyList<int> Positions)
    {
        public int OriginalPosition(int normalizedPosition)
            => normalizedPosition >= 0 && normalizedPosition < Positions.Count ? Positions[normalizedPosition] : 0;

        public static NormalizedTextMap Create(string value)
        {
            var text = new StringBuilder(value.Length);
            var positions = new List<int>(value.Length);
            var previousWasSpace = false;
            for (var index = 0; index < value.Length; index++)
            {
                var decomposed = value[index].ToString().Normalize(NormalizationForm.FormD);
                foreach (var character in decomposed)
                {
                    if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
                    if (char.IsLetterOrDigit(character))
                    {
                        text.Append(char.ToLowerInvariant(character));
                        positions.Add(index);
                        previousWasSpace = false;
                    }
                    else if (!previousWasSpace && text.Length > 0)
                    {
                        text.Append(' ');
                        positions.Add(index);
                        previousWasSpace = true;
                    }
                }
            }
            if (text.Length > 0 && text[^1] == ' ') { text.Length--; positions.RemoveAt(positions.Count - 1); }
            return new NormalizedTextMap(text.ToString(), positions);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose() { if (_disposed) return; _disposed = true; _gate.Dispose(); }

    private sealed class SearchIndexState
    {
        public int Version { get; set; }
        public Dictionary<string, IndexedSource> Sources { get; set; } = new(StringComparer.Ordinal);
    }
    private sealed class IndexedSource
    {
        public IndexedSource() { }

        public IndexedSource(string fingerprint, List<SearchIndexEntry> entries)
        {
            Fingerprint = fingerprint;
            Entries = entries;
        }

        public string Fingerprint { get; set; } = string.Empty;
        public List<SearchIndexEntry> Entries { get; set; } = [];
    }
}
