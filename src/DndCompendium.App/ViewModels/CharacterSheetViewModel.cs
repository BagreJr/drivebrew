using System.Text.Json;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.App.Services;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.App.ViewModels;

public partial class CharacterSheetViewModel(
    ICharacterProfileStore profiles,
    ICharacterSheetService characterSheets,
    ISpellRepository spells,
    IUniversalSearchService search) : ObservableObject
{
    private static readonly IReadOnlyDictionary<string, string> FieldLabels = BuildFieldLabels();
    private static readonly HashSet<string> AbilityNames = ["STR", "DEX", "CON", "INT", "WIS", "CHA"];
    private static readonly IReadOnlyDictionary<string, string> ProficiencyToggles = CharacterSheetSchema.Sections
        .SelectMany(section => section.Fields)
        .Where(field => !string.IsNullOrWhiteSpace(field.FieldName) && !string.IsNullOrWhiteSpace(field.ToggleFieldName))
        .GroupBy(field => field.FieldName, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First().ToggleFieldName!, StringComparer.Ordinal);
    private static readonly IReadOnlyList<string> NumericFields = CharacterSheetSchema.Sections
        .SelectMany(section => section.Fields)
        .Where(field => field.IsNumeric && !string.IsNullOrWhiteSpace(field.FieldName))
        .Select(field => field.FieldName)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private CharacterProfile? _character;
    private CharacterSheetData _data = new();
    private IReadOnlyList<Spell> _preparedSpells = [];
    private IReadOnlyList<Spell> _allSpells = [];
    private IReadOnlyList<CharacterResource> _resources = [];

    public bool HasCharacter => _character is not null;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharacter))]
    private string characterName = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private string statusText = "Hoja editable offline";

    [ObservableProperty]
    private bool hasUnsavedChanges;

    public async Task LoadAsync(string characterId)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(characterId)) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            _character = (await profiles.GetAllAsync()).SingleOrDefault(item => item.Id == characterId)
                ?? throw new KeyNotFoundException("El personaje ya no existe.");
            var active = await profiles.GetActiveAsync();
            if (!string.Equals(active.Id, _character.Id, StringComparison.Ordinal))
                await profiles.SetActiveAsync(_character.Id);
            CharacterName = _character.Name;
            _data = await characterSheets.LoadDataAsync(_character);
            await ReloadSessionDataAsync();
            HasUnsavedChanges = false;
            StatusText = _data.UpdatedAt == default
                ? "Hoja lista para completar"
                : $"Guardado: {_data.UpdatedAt.ToLocalTime():g}";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo abrir la hoja: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string CreateEditorPayloadJson()
        => JsonSerializer.Serialize(new
        {
            type = "load",
            payload = new
            {
                characterName = CharacterName,
                textFields = _data.TextFields,
                checkedFields = _data.CheckedFields,
                fieldLabels = FieldLabels,
                numericFields = NumericFields,
                schemaVersion = _data.SchemaVersion,
                ruleSet = _data.RuleSet,
                abilityBaseScores = _data.AbilityBaseScores,
                proficiencyRanks = _data.ProficiencyRanks,
                otherBonuses = _data.OtherBonuses,
                overrides = _data.Overrides,
                modifiers = _data.Modifiers,
                features = _data.Features,
                knownSpellIds = _data.KnownSpellIds,
                preparedSpells = _preparedSpells.Select(spell => new
                {
                    id = spell.Id,
                    name = spell.Name,
                    level = spell.Level,
                    school = spell.School,
                    castingTime = spell.CastingTime,
                    range = spell.Range,
                    duration = spell.Duration,
                    components = string.Join(", ", new[]
                    {
                        spell.HasVerbalComponent ? "V" : null,
                        spell.HasSomaticComponent ? "S" : null,
                        spell.HasMaterialComponent ? "M" : null
                    }.Where(value => value is not null))
                }),
                knownSpells = _allSpells.Where(spell => _data.KnownSpellIds.Contains(spell.Id)).Select(spell => new
                {
                    id = spell.Id,
                    name = spell.Name,
                    level = spell.Level,
                    school = spell.School,
                    castingTime = spell.CastingTime,
                    range = spell.Range,
                    duration = spell.Duration,
                    components = string.Join(", ", new[]
                    {
                        spell.HasVerbalComponent ? "V" : null,
                        spell.HasSomaticComponent ? "S" : null,
                        spell.HasMaterialComponent ? "M" : null
                    }.Where(value => value is not null))
                }),
                resources = _resources.Select(resource => new
                {
                    id = resource.Id,
                    name = resource.Name,
                    kind = resource.Kind.ToString(),
                    level = resource.Level,
                    current = resource.Current,
                    maximum = resource.Maximum,
                    unit = resource.Unit,
                    recovery = resource.Recovery.ToString()
                }),
                editSections = CharacterSheetSchema.EditSections.Select(section => new
                {
                    id = section.Id,
                    name = section.Name,
                    helpText = section.HelpText,
                    fields = section.Fields.Select(field => new
                    {
                        name = string.IsNullOrWhiteSpace(field.FieldName) ? field.ToggleFieldName : field.FieldName,
                        label = field.Label,
                        kind = string.IsNullOrWhiteSpace(field.FieldName) ? "toggle" : field.IsMultiline ? "textarea" : "input",
                        numeric = field.IsNumeric,
                        toggleName = field.ToggleFieldName,
                        toggleLabel = field.ToggleLabel
                    })
                })
            }
        });

    public void ApplyEditorChange(string name, string kind, string? value, bool isChecked)
    {
        if (string.IsNullOrWhiteSpace(name) || _character is null) return;
        if (string.Equals(kind, "base", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var score))
                _data.AbilityBaseScores[name] = Math.Clamp(score, 1, 30);
            else
                _data.AbilityBaseScores.Remove(name);
        }
        else if (string.Equals(kind, "proficiency", StringComparison.OrdinalIgnoreCase))
        {
            var rank = int.TryParse(value, out var parsedRank) ? Math.Clamp(parsedRank, 0, 2) : 0;
            if (rank == 0) _data.ProficiencyRanks.Remove(name);
            else _data.ProficiencyRanks[name] = rank;
            if (ProficiencyToggles.TryGetValue(name, out var toggle))
            {
                if (rank > 0) _data.CheckedFields.Add(toggle);
                else _data.CheckedFields.Remove(toggle);
            }
        }
        else if (string.Equals(kind, "bonus", StringComparison.OrdinalIgnoreCase))
        {
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var bonus) && bonus != 0)
                _data.OtherBonuses[name] = bonus;
            else
                _data.OtherBonuses.Remove(name);
        }
        else if (string.Equals(kind, "override", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(value)) _data.Overrides.Remove(name);
            else _data.Overrides[name] = value;
        }
        else if (string.Equals(kind, "ruleset", StringComparison.OrdinalIgnoreCase))
        {
            _data.RuleSet = value is "2024" or "Homebrew" ? value : "2014";
        }
        else if (string.Equals(kind, "checkbox", StringComparison.OrdinalIgnoreCase))
        {
            if (isChecked) _data.CheckedFields.Add(name);
            else _data.CheckedFields.Remove(name);
        }
        else if (string.IsNullOrEmpty(value))
        {
            _data.TextFields.Remove(name);
        }
        else
        {
            _data.TextFields[name] = value;
            if (AbilityNames.Contains(name)
                && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var score))
                _data.AbilityBaseScores[name] = Math.Clamp(score, 1, 30);
        }

        HasUnsavedChanges = true;
        StatusText = "Cambios sin guardar";
    }

    public void UpsertModifier(
        string? id,
        string sourceName,
        string target,
        decimal value,
        string? bonusType,
        string? condition,
        string? duration,
        bool isActive)
    {
        if (_character is null || string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(target)) return;
        var safeId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
        var replacement = new CharacterSheetModifier
        {
            Id = safeId,
            SourceName = sourceName.Trim(),
            Target = target.Trim(),
            Value = value,
            BonusType = string.IsNullOrWhiteSpace(bonusType) ? "untyped" : bonusType.Trim(),
            Condition = string.IsNullOrWhiteSpace(condition) ? null : condition.Trim(),
            Duration = string.IsNullOrWhiteSpace(duration) ? null : duration.Trim(),
            IsActive = isActive
        };
        var index = _data.Modifiers.FindIndex(item => string.Equals(item.Id, safeId, StringComparison.Ordinal));
        if (index >= 0) _data.Modifiers[index] = replacement;
        else _data.Modifiers.Add(replacement);
        HasUnsavedChanges = true;
        StatusText = "Cambios sin guardar";
    }

    public void RemoveModifier(string id)
    {
        if (_character is null || string.IsNullOrWhiteSpace(id)) return;
        if (_data.Modifiers.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal)) == 0) return;
        HasUnsavedChanges = true;
        StatusText = "Cambios sin guardar";
    }

    public async Task AdjustResourceAsync(string id, int delta)
    {
        if (_character is null || string.IsNullOrWhiteSpace(id) || delta == 0) return;
        await profiles.AdjustResourceAsync(id, delta);
        _resources = await profiles.GetResourcesAsync();
    }

    public async Task SetPreparedSpellAsync(string id, bool isPrepared)
    {
        if (_character is null || string.IsNullOrWhiteSpace(id)) return;
        await spells.SetPreparedAsync(id, isPrepared);
        await ReloadSessionDataAsync();
    }

    public void SetKnownSpell(string id, bool isKnown)
    {
        if (_character is null || string.IsNullOrWhiteSpace(id)) return;
        if (isKnown) _data.KnownSpellIds.Add(id);
        else _data.KnownSpellIds.Remove(id);
        HasUnsavedChanges = true;
        StatusText = "Cambios sin guardar";
    }

    public Task<string> SearchSpellsPayloadJsonAsync(string query, int? level)
    {
        var normalized = query.Trim();
        var results = _allSpells
            .Where(spell => !level.HasValue || spell.Level == level.Value)
            .Where(spell => string.IsNullOrWhiteSpace(normalized)
                || spell.Name.Contains(normalized, StringComparison.CurrentCultureIgnoreCase)
                || spell.Effect.Contains(normalized, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(spell => spell.Level)
            .ThenBy(spell => spell.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(60)
            .Select(spell => new
            {
                id = spell.Id,
                name = spell.Name,
                level = spell.Level,
                school = spell.School,
                castingTime = spell.CastingTime,
                range = spell.Range,
                duration = spell.Duration,
                components = string.Join(", ", new[]
                {
                    spell.HasVerbalComponent ? "V" : null,
                    spell.HasSomaticComponent ? "S" : null,
                    spell.HasMaterialComponent ? "M" : null
                }.Where(value => value is not null)),
                effect = spell.Effect.Length <= 220 ? spell.Effect : $"{spell.Effect[..217]}…"
            });
        return Task.FromResult(JsonSerializer.Serialize(new { type = "spellResults", results }));
    }

    public async Task<string> SearchFeaturesPayloadJsonAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return JsonSerializer.Serialize(new { type = "featureResults", results = Array.Empty<object>() });
        var supportedKinds = new HashSet<SearchContentKind>
        {
            SearchContentKind.ClassFeature,
            SearchContentKind.SubclassFeature,
            SearchContentKind.RaceFeature,
            SearchContentKind.Feat
        };
        var results = (await search.SearchAsync(query.Trim(), limit: 100))
            .Where(result => supportedKinds.Contains(result.Entry.Kind))
            .Take(30)
            .Select(result => new
            {
                id = result.Entry.Id,
                name = result.Entry.Name,
                kind = result.Entry.Kind.ToString(),
                origin = result.Entry.Origin,
                parent = result.Entry.Parent,
                level = result.Entry.Level,
                snippet = result.Snippet,
                sourceKey = result.Entry.SourceKey,
                documentId = result.Entry.DocumentId,
                target = result.Entry.Target
            });
        return JsonSerializer.Serialize(new { type = "featureResults", results });
    }

    public async Task<string> SearchIdentityPayloadJsonAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return JsonSerializer.Serialize(new { type = "identityResults", results = Array.Empty<object>() });
        var supportedKinds = new HashSet<SearchContentKind>
        {
            SearchContentKind.Class,
            SearchContentKind.Subclass,
            SearchContentKind.Race,
            SearchContentKind.Background
        };
        var results = (await search.SearchAsync(query.Trim(), limit: 100))
            .Where(result => supportedKinds.Contains(result.Entry.Kind))
            .Take(30)
            .Select(result => new
            {
                id = result.Entry.Id,
                name = result.Entry.Name,
                kind = result.Entry.Kind.ToString(),
                origin = result.Entry.Origin,
                parentClass = result.Entry.ParentClass,
                parent = result.Entry.Parent,
                snippet = result.Snippet,
                documentId = result.Entry.DocumentId,
                target = result.Entry.Target
            });
        return JsonSerializer.Serialize(new { type = "identityResults", results });
    }

    public void AddFeatureReference(
        string id,
        string name,
        string kind,
        string? sourceKey,
        string? documentId,
        string? target,
        string? level)
    {
        if (_character is null || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return;
        if (_data.Features.Any(item => string.Equals(item.Id, id, StringComparison.Ordinal))) return;
        _data.Features.Add(new CharacterFeatureReference
        {
            Id = id.Trim(),
            Name = name.Trim(),
            Kind = kind.Trim(),
            SourceKey = sourceKey,
            DocumentId = documentId,
            Target = target,
            Level = level
        });
        HasUnsavedChanges = true;
        StatusText = "Cambios sin guardar";
    }

    public void RemoveFeatureReference(string id)
    {
        if (_character is null || string.IsNullOrWhiteSpace(id)) return;
        if (_data.Features.RemoveAll(item => string.Equals(item.Id, id, StringComparison.Ordinal)) == 0) return;
        HasUnsavedChanges = true;
        StatusText = "Cambios sin guardar";
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SaveAsync()
    {
        if (_character is null || IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await SaveCoreAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo guardar la hoja: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenPdfAsync()
    {
        if (_character is null || IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await SaveCoreAsync();
            await characterSheets.OpenAsync(_character);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo abrir el PDF: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ShareAsync()
    {
        if (_character is null || IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await SaveCoreAsync();
            await characterSheets.ShareAsync(_character);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo guardar la copia: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveIfNeededAsync()
    {
        if (!HasUnsavedChanges || _character is null || IsBusy) return;
        await SaveAsync();
    }

    public async Task RefreshSessionDataAsync()
    {
        if (_character is null || IsBusy) return;
        await ReloadSessionDataAsync();
    }

    public void ReportEditorError(string message)
        => ErrorMessage = $"No se pudo mostrar la hoja: {message}";

    private async Task SaveCoreAsync()
    {
        if (_character is null) return;
        _data.UpdatedAt = DateTimeOffset.UtcNow;
        await characterSheets.SaveDataAsync(_character, _data);
        HasUnsavedChanges = false;
        StatusText = $"Guardado: {_data.UpdatedAt.ToLocalTime():g}";
    }

    private async Task ReloadSessionDataAsync()
    {
        try
        {
            var preparedIds = await spells.GetPreparedIdsAsync();
            _allSpells = await spells.GetAllAsync(false);
            _preparedSpells = _allSpells.Where(spell => preparedIds.Contains(spell.Id))
                .OrderBy(spell => spell.Level).ThenBy(spell => spell.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        catch
        {
            _preparedSpells = [];
            _allSpells = [];
        }
        try
        {
            _resources = await profiles.GetResourcesAsync();
        }
        catch
        {
            _resources = [];
        }
    }

    private static IReadOnlyDictionary<string, string> BuildFieldLabels()
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in CharacterSheetSchema.Sections.SelectMany(section => section.Fields)
                     .Concat(CharacterSheetSchema.EditSections.SelectMany(section => section.Fields)))
        {
            if (!string.IsNullOrWhiteSpace(field.FieldName)) labels.TryAdd(field.FieldName, field.Label);
            foreach (var mirror in field.MirrorFieldNames ?? []) labels.TryAdd(mirror, field.Label);
            if (!string.IsNullOrWhiteSpace(field.ToggleFieldName))
            {
                labels.TryAdd(
                    field.ToggleFieldName,
                    string.IsNullOrWhiteSpace(field.ToggleLabel) ? $"{field.Label}: selección" : field.ToggleLabel);
            }
        }
        return labels;
    }
}
