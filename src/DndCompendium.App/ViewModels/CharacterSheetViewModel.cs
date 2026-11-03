using System.Text.Json;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.App.Services;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using DndCompendium.Core.Services;

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
    private IReadOnlySet<string> _preparedSpellIds = new HashSet<string>(StringComparer.Ordinal);
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
            SyncEffectProficiencyChecks();
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
                selections = _data.Selections.Where(item => item.IsActive),
                effects = _data.Effects,
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
            if (rank == 0) _data.ManualProficiencyRanks.Remove(name);
            else _data.ManualProficiencyRanks[name] = rank;
            CharacterSheetEffectEngine.Rebuild(_data);
            if (ProficiencyToggles.TryGetValue(name, out var toggle))
            {
                if (rank > 0) _data.CheckedFields.Add(toggle);
                else _data.CheckedFields.Remove(toggle);
            }
        }
        else if (string.Equals(kind, "bonus", StringComparison.OrdinalIgnoreCase))
        {
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var bonus) && bonus != 0)
                _data.ManualOtherBonuses[name] = bonus;
            else
                _data.ManualOtherBonuses.Remove(name);
            CharacterSheetEffectEngine.Rebuild(_data);
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
            _data.GeneratedCheckedFields.Remove(name);
            if (isChecked) _data.CheckedFields.Add(name);
            else _data.CheckedFields.Remove(name);
        }
        else if (string.IsNullOrEmpty(value))
        {
            _data.GeneratedTextFields.Remove(name);
            _data.TextFields.Remove(name);
        }
        else
        {
            _data.GeneratedTextFields.Remove(name);
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
        ProjectSpellsToPdf();
        HasUnsavedChanges = true;
    }

    public void SetKnownSpell(string id, bool isKnown)
    {
        if (_character is null || string.IsNullOrWhiteSpace(id)) return;
        if (isKnown) _data.KnownSpellIds.Add(id);
        else _data.KnownSpellIds.Remove(id);
        SyncSpellSnapshots();
        ProjectSpellsToPdf();
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
            SearchContentKind.Feat,
            SearchContentKind.Equipment
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
                description = result.Entry.Description,
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
                description = result.Entry.Description,
                sourceKey = result.Entry.SourceKey,
                documentId = result.Entry.DocumentId,
                target = result.Entry.Target
            });
        return JsonSerializer.Serialize(new { type = "identityResults", results });
    }

    public async Task ApplyCompendiumSelectionAsync(
        string referenceId,
        string name,
        string kind,
        string? description,
        string? sourceKey,
        string? documentId,
        string? target,
        string? parentClass,
        string? parentName)
    {
        if (_character is null || string.IsNullOrWhiteSpace(referenceId)
            || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(kind)) return;

        if (string.Equals(kind, nameof(SearchContentKind.Subclass), StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(parentClass)
            && !CharacterSelectionRules.IsSubclassCompatible(_data.Selections, parentClass))
            throw new InvalidOperationException($"La subclase {name} requiere la clase {parentClass}.");
        if (string.Equals(kind, "Subrace", StringComparison.Ordinal)
            && !_data.Selections.Any(item => item.IsActive && item.Kind == nameof(SearchContentKind.Race)))
            throw new InvalidOperationException("Elegí primero la raza principal antes de agregar una subraza.");

        var singleton = kind is nameof(SearchContentKind.Class) or nameof(SearchContentKind.Subclass)
            or nameof(SearchContentKind.Race) or "Subrace" or nameof(SearchContentKind.Background);
        var replaced = _data.Selections
            .Where(item => singleton && item.IsActive
                && (string.Equals(item.Kind, kind, StringComparison.Ordinal)
                    || kind == nameof(SearchContentKind.Race) && item.Kind == "Subrace"))
            .Concat(_data.Selections.Where(item => singleton && item.IsActive
                && kind == nameof(SearchContentKind.Class) && item.Kind == nameof(SearchContentKind.Subclass)))
            .DistinctBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        foreach (var old in replaced)
        {
            old.IsActive = false;
            CharacterSheetEffectEngine.RemoveSelectionEffects(_data, old.Id);
        }
        SyncEffectProficiencyChecks();

        var selectionId = $"selection:{kind}:{referenceId}";
        var selection = new CharacterCompendiumSelection
        {
            Id = selectionId,
            ReferenceId = referenceId.Trim(),
            Name = name.Trim(),
            Kind = kind.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            SourceKey = sourceKey,
            DocumentId = documentId,
            Target = target,
            ParentClass = parentClass,
            ParentName = parentName,
            Level = kind is nameof(SearchContentKind.Class) or nameof(SearchContentKind.Subclass)
                ? LevelFromClassText(_data.TextFields.GetValueOrDefault("ClassLevel"))
                : null,
            IsActive = true
        };
        var existing = _data.Selections.FindIndex(item => string.Equals(item.Id, selectionId, StringComparison.Ordinal));
        if (existing >= 0) _data.Selections[existing] = selection;
        else _data.Selections.Add(selection);

        if (kind == nameof(SearchContentKind.Feat)
            && !_data.Features.Any(item => string.Equals(item.Id, referenceId, StringComparison.Ordinal)))
        {
            _data.Features.Add(new CharacterFeatureReference
            {
                Id = referenceId, Name = name, Kind = kind, SourceKey = sourceKey,
                DocumentId = documentId, Target = target, SelectionId = selection.Id
            });
        }

        await AddRelatedFeaturesAsync(selection);
        ProjectSelectionFields();
        HasUnsavedChanges = true;
        StatusText = "Cambios sin guardar";
    }

    public async Task LevelUpAsync(int nextLevel)
    {
        if (_character is null || nextLevel is < 2 or > 20) return;
        var selectedClass = _data.Selections.LastOrDefault(item => item.IsActive
            && item.Kind == nameof(SearchContentKind.Class));
        if (selectedClass is null) return;
        selectedClass.Level = nextLevel;
        foreach (var subclass in _data.Selections.Where(item => item.IsActive
                     && item.Kind == nameof(SearchContentKind.Subclass))) subclass.Level = nextLevel;
        SetGeneratedText("ClassLevel", $"{selectedClass.Name} {nextLevel}", force: true);
        await AddRelatedFeaturesAsync(selectedClass);
        foreach (var subclass in _data.Selections.Where(item => item.IsActive
                     && item.Kind == nameof(SearchContentKind.Subclass))) await AddRelatedFeaturesAsync(subclass);
        ProjectSelectionFields();
        HasUnsavedChanges = true;
        StatusText = "Cambios sin guardar";
    }

    public void RemoveCompendiumSelection(string selectionId)
    {
        var selection = _data.Selections.FirstOrDefault(item => string.Equals(item.Id, selectionId, StringComparison.Ordinal));
        if (selection is null || !selection.IsActive) return;
        selection.IsActive = false;
        CharacterSheetEffectEngine.RemoveSelectionEffects(_data, selection.Id);
        if (selection.Kind == nameof(SearchContentKind.Race))
        {
            foreach (var subrace in _data.Selections.Where(item => item.IsActive && item.Kind == "Subrace"))
            {
                subrace.IsActive = false;
                CharacterSheetEffectEngine.RemoveSelectionEffects(_data, subrace.Id);
            }
        }
        if (selection.Kind == nameof(SearchContentKind.Class))
        {
            foreach (var subclass in _data.Selections.Where(item => item.IsActive
                         && item.Kind == nameof(SearchContentKind.Subclass)))
            {
                subclass.IsActive = false;
                CharacterSheetEffectEngine.RemoveSelectionEffects(_data, subclass.Id);
            }
        }
        SyncEffectProficiencyChecks();
        ProjectSelectionFields();
        HasUnsavedChanges = true;
        StatusText = "Cambios sin guardar";
    }

    public void UpsertSelectionEffect(
        string? id,
        string selectionId,
        string effectKind,
        string target,
        decimal value,
        int rank,
        string? activation,
        string? condition,
        string? choiceLabel)
    {
        var selection = _data.Selections.FirstOrDefault(item => item.IsActive
            && string.Equals(item.Id, selectionId, StringComparison.Ordinal));
        if (selection is null || string.IsNullOrWhiteSpace(target)) return;
        if (effectKind is not (CharacterSheetEffectEngine.AbilityBonus
            or CharacterSheetEffectEngine.NumericBonus
            or CharacterSheetEffectEngine.Proficiency)) return;
        var effectId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
        CharacterSheetEffectEngine.UpsertEffect(_data, new CharacterSheetEffect
        {
            Id = effectId,
            SourceSelectionId = selection.Id,
            SourceName = selection.Name,
            EffectKind = effectKind,
            Target = target.Trim(),
            Value = value,
            Rank = Math.Clamp(rank, 0, 2),
            Activation = string.IsNullOrWhiteSpace(activation) ? "Permanent" : activation.Trim(),
            Condition = string.IsNullOrWhiteSpace(condition) ? null : condition.Trim(),
            ChoiceLabel = string.IsNullOrWhiteSpace(choiceLabel) ? null : choiceLabel.Trim(),
            IsActive = true
        });
        SyncEffectProficiencyChecks();
        HasUnsavedChanges = true;
        StatusText = "Cambios sin guardar";
    }

    public void RemoveSelectionEffect(string effectId)
    {
        if (!CharacterSheetEffectEngine.RemoveEffect(_data, effectId)) return;
        SyncEffectProficiencyChecks();
        HasUnsavedChanges = true;
        StatusText = "Cambios sin guardar";
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
        SyncSpellSnapshots();
        ProjectSpellsToPdf();
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
            _preparedSpellIds = preparedIds;
            _allSpells = await spells.GetAllAsync(false);
            _preparedSpells = _allSpells.Where(spell => preparedIds.Contains(spell.Id))
                .OrderBy(spell => spell.Level).ThenBy(spell => spell.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            SyncSpellSnapshots();
            ProjectSpellsToPdf();
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

    private async Task AddRelatedFeaturesAsync(CharacterCompendiumSelection selection)
    {
        var expectedKind = selection.Kind switch
        {
            nameof(SearchContentKind.Class) => SearchContentKind.ClassFeature,
            nameof(SearchContentKind.Subclass) => SearchContentKind.SubclassFeature,
            nameof(SearchContentKind.Race) => SearchContentKind.RaceFeature,
            "Subrace" => SearchContentKind.RaceFeature,
            _ => (SearchContentKind?)null
        };
        if (expectedKind is null) return;
        var candidates = await search.SearchAsync(selection.Name, limit: 250);
        foreach (var result in candidates.Where(result => result.Entry.Kind == expectedKind
                     && BelongsToSelection(result.Entry, selection) && IsAvailableAtLevel(result.Entry, selection)))
        {
            if (_data.Features.Any(item => string.Equals(item.Id, result.Entry.Id, StringComparison.Ordinal))) continue;
            _data.Features.Add(new CharacterFeatureReference
            {
                Id = result.Entry.Id,
                Name = result.Entry.Name,
                Kind = result.Entry.Kind.ToString(),
                SourceKey = result.Entry.SourceKey,
                DocumentId = result.Entry.DocumentId,
                Target = result.Entry.Target,
                Level = result.Entry.Level,
                SelectionId = selection.Id
            });
        }
    }

    private static bool IsAvailableAtLevel(SearchIndexEntry entry, CharacterCompendiumSelection selection)
        => CharacterSelectionRules.IsAvailableAtLevel(entry.Level, selection.Level);

    private static bool BelongsToSelection(SearchIndexEntry entry, CharacterCompendiumSelection selection)
    {
        static bool Same(string? left, string right)
            => !string.IsNullOrWhiteSpace(left) && string.Equals(left.Trim(), right.Trim(), StringComparison.CurrentCultureIgnoreCase);
        return selection.Kind switch
        {
            nameof(SearchContentKind.Class) => Same(entry.ParentClass, selection.Name) || Same(entry.Origin, selection.Name),
            nameof(SearchContentKind.Subclass) => Same(entry.ParentSubclass, selection.Name)
                || Same(entry.Parent, selection.Name) || Same(entry.Origin, selection.Name),
            nameof(SearchContentKind.Race) => Same(entry.Parent, selection.Name) || Same(entry.Origin, selection.Name),
            "Subrace" => Same(entry.Parent, selection.Name) || Same(entry.Origin, selection.Name),
            _ => false
        };
    }

    private void ProjectSelectionFields()
    {
        var race = _data.Selections.LastOrDefault(item => item.IsActive && item.Kind == nameof(SearchContentKind.Race));
        var subrace = _data.Selections.LastOrDefault(item => item.IsActive && item.Kind == "Subrace");
        if (race is null) ClearGeneratedText("Race ");
        else SetGeneratedText("Race ", subrace is null ? race.Name : $"{race.Name} ({subrace.Name})", force: true);
        if (subrace is null) ClearGeneratedText("Digital.Subrace");
        else SetGeneratedText("Digital.Subrace", subrace.Name, force: true);
        SetIdentity(nameof(SearchContentKind.Background), "Background");
        SetIdentity(nameof(SearchContentKind.Subclass), "Digital.Subclass");
        var selectedClass = _data.Selections.LastOrDefault(item => item.IsActive && item.Kind == nameof(SearchContentKind.Class));
        if (selectedClass is not null)
        {
            var level = rulesLevel(_data.TextFields.GetValueOrDefault("ClassLevel"));
            SetGeneratedText("ClassLevel", $"{selectedClass.Name} {level}", force: true);
        }
        else ClearGeneratedText("ClassLevel");

        ProjectFeatureSummary(nameof(SearchContentKind.ClassFeature), "Digital.ClassFeatures");
        ProjectFeatureSummary(nameof(SearchContentKind.SubclassFeature), "Digital.SubclassFeatures");
        ProjectFeatureSummary(nameof(SearchContentKind.RaceFeature), "Digital.RacialTraits");
        ProjectFeatureSummary(nameof(SearchContentKind.Feat), "Digital.Feats");
        var equipment = string.Join('\n', _data.Selections.Where(item => item.IsActive
                && item.Kind == nameof(SearchContentKind.Equipment))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).Select(item => item.Name));
        if (string.IsNullOrWhiteSpace(equipment)) ClearGeneratedText("Equipment");
        else SetGeneratedText("Equipment", equipment);

        void SetIdentity(string kind, string field)
        {
            var selected = _data.Selections.LastOrDefault(item => item.IsActive && item.Kind == kind);
            if (selected is null) ClearGeneratedText(field);
            else SetGeneratedText(field, selected.Name, force: true);
        }
        static int rulesLevel(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 1;
            return LevelFromClassText(text);
        }
    }

    private static int LevelFromClassText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 1;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\b(?:[1-9]|1\d|20)\b");
        return match.Success && int.TryParse(match.Value, out var level) ? level : 1;
    }

    private void ProjectFeatureSummary(string kind, string field)
    {
        var text = string.Join('\n', _data.Features.Where(item => item.Kind == kind && !string.IsNullOrWhiteSpace(item.SelectionId))
            .OrderBy(item => int.TryParse(item.Level, out var level) ? level : int.MaxValue)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => string.IsNullOrWhiteSpace(item.Level) ? item.Name : $"{item.Name} (nivel {item.Level})"));
        if (string.IsNullOrWhiteSpace(text)) ClearGeneratedText(field);
        else SetGeneratedText(field, text);
    }

    private void SyncSpellSnapshots()
    {
        var prepared = _preparedSpellIds;
        var available = _allSpells.ToDictionary(item => item.Id, StringComparer.Ordinal);
        foreach (var spell in _allSpells.Where(item => prepared.Contains(item.Id) || _data.KnownSpellIds.Contains(item.Id)))
        {
            var snapshot = new CharacterSpellReference
            {
                Id = spell.Id, Name = spell.Name, Level = spell.Level, School = spell.School,
                IsKnown = _data.KnownSpellIds.Contains(spell.Id), IsPrepared = prepared.Contains(spell.Id)
            };
            var index = _data.Spells.FindIndex(item => string.Equals(item.Id, spell.Id, StringComparison.Ordinal));
            if (index >= 0) _data.Spells[index] = snapshot;
            else _data.Spells.Add(snapshot);
        }
        foreach (var snapshot in _data.Spells.Where(item => !available.ContainsKey(item.Id)))
        {
            snapshot.IsKnown = snapshot.IsKnown && _data.KnownSpellIds.Contains(snapshot.Id);
            snapshot.IsPrepared = snapshot.IsPrepared && prepared.Contains(snapshot.Id);
        }
        _data.Spells.RemoveAll(item => !item.IsKnown && !item.IsPrepared);
    }

    private void ProjectSpellsToPdf()
    {
        foreach (var pair in _data.GeneratedTextFields.Where(pair => pair.Key.StartsWith("Spells ", StringComparison.Ordinal)
                     || pair.Key.StartsWith("Slots", StringComparison.Ordinal)).ToArray())
            ClearGeneratedText(pair.Key);
        var spellToggleNames = CharacterSheetSchema.Sections.Where(section => section.Name == "Cantrips"
                || section.Name.StartsWith("Spells - Nivel ", StringComparison.Ordinal))
            .SelectMany(section => section.Fields).Select(field => field.ToggleFieldName)
            .Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var name in _data.GeneratedCheckedFields.Where(spellToggleNames.Contains).ToArray())
        {
            _data.CheckedFields.Remove(name);
            _data.GeneratedCheckedFields.Remove(name);
        }

        for (var level = 0; level <= 9; level++)
        {
            var sectionName = level == 0 ? "Cantrips" : $"Spells - Nivel {level}";
            var fields = CharacterSheetSchema.Sections.Single(section => section.Name == sectionName).Fields
                .Where(field => field.FieldName.StartsWith("Spells ", StringComparison.Ordinal)).ToArray();
            var selected = _data.Spells.Where(item => item.Level == level && (item.IsKnown || item.IsPrepared))
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).Take(fields.Length).ToArray();
            for (var index = 0; index < selected.Length; index++)
            {
                SetGeneratedText(fields[index].FieldName, selected[index].Name);
                if (selected[index].IsPrepared && !string.IsNullOrWhiteSpace(fields[index].ToggleFieldName))
                {
                    _data.CheckedFields.Add(fields[index].ToggleFieldName!);
                    _data.GeneratedCheckedFields.Add(fields[index].ToggleFieldName!);
                }
            }
        }

        foreach (var slot in _resources.Where(item => item.Kind == CharacterResourceKind.SpellSlot
                     && item.Level is >= 1 and <= 9).GroupBy(item => item.Level!.Value))
        {
            var resource = slot.OrderByDescending(item => item.Maximum).First();
            var suffix = resource.Level + 18;
            SetGeneratedText($"SlotsTotal {suffix}", resource.Maximum.ToString(CultureInfo.InvariantCulture));
            SetGeneratedText($"SlotsRemaining {suffix}", resource.Current.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void SyncEffectProficiencyChecks()
    {
        var effectTargets = _data.Effects.Where(effect => effect.IsActive
                && effect.EffectKind == CharacterSheetEffectEngine.Proficiency && effect.Rank > 0)
            .Select(effect => effect.Target).ToHashSet(StringComparer.Ordinal);
        foreach (var (target, toggle) in ProficiencyToggles)
        {
            if (effectTargets.Contains(target))
            {
                _data.CheckedFields.Add(toggle);
                _data.GeneratedCheckedFields.Add(toggle);
            }
            else if (_data.GeneratedCheckedFields.Remove(toggle))
            {
                _data.CheckedFields.Remove(toggle);
            }
        }
    }

    private void SetGeneratedText(string field, string value, bool force = false)
    {
        var canReplace = force || !_data.TextFields.TryGetValue(field, out var current)
            || (_data.GeneratedTextFields.TryGetValue(field, out var previous) && current == previous);
        if (!canReplace) return;
        _data.TextFields[field] = value;
        _data.GeneratedTextFields[field] = value;
    }

    private void ClearGeneratedText(string field)
    {
        if (!_data.GeneratedTextFields.Remove(field, out var previous)) return;
        if (_data.TextFields.TryGetValue(field, out var current) && current == previous)
            _data.TextFields.Remove(field);
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
