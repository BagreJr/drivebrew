using System.Text.Json;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using DndCompendium.Core.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace DndCompendium.App.Services;

public interface ICharacterSheetEditor
{
    Task OpenAsync(string pdfPath, string title, CancellationToken cancellationToken = default);
}

public interface ICharacterSheetService
{
    Task EnsureCreatedAsync(CharacterProfile character, CancellationToken cancellationToken = default);
    Task<CharacterSheetData> LoadDataAsync(CharacterProfile character, CancellationToken cancellationToken = default);
    Task SaveDataAsync(CharacterProfile character, CharacterSheetData data, CancellationToken cancellationToken = default);
    Task DuplicateAsync(CharacterProfile source, CharacterProfile target, CancellationToken cancellationToken = default);
    Task UpdateCharacterNameAsync(CharacterProfile character, CancellationToken cancellationToken = default);
    Task OpenAsync(CharacterProfile character, CancellationToken cancellationToken = default);
    Task<bool> PickAndImportAsync(CharacterProfile character, CancellationToken cancellationToken = default);
    Task ShareAsync(CharacterProfile character, CancellationToken cancellationToken = default);
    Task DeleteAsync(string characterId, CancellationToken cancellationToken = default);
}

public sealed class CharacterSheetService(
    ICharacterSheetEditor editor,
    ICharacterSheetPdfService pdfService) : ICharacterSheetService
{
    private const string TemplateFileName = "character_sheet_template.pdf";
    private const long MaximumImportBytes = 40L * 1024L * 1024L;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _sheetDirectory = Path.Combine(FileSystem.Current.AppDataDirectory, "character-sheets");

    public async Task EnsureCreatedAsync(CharacterProfile character, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_sheetDirectory);
            var pdfPath = SheetPath(character.Id);
            var dataPath = DataPath(character.Id);
            CharacterSheetData data;
            if (File.Exists(dataPath))
            {
                await BackupBeforeV4MigrationAsync(character.Id, dataPath, pdfPath, cancellationToken).ConfigureAwait(false);
                data = await ReadDataAsync(dataPath, cancellationToken).ConfigureAwait(false);
            }
            else if (File.Exists(pdfPath))
            {
                await using var pdf = File.OpenRead(pdfPath);
                data = await pdfService.ReadAsync(pdf, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                data = new CharacterSheetData { IsCreationDraft = true, CreatorStep = 1 };
            }

            var changed = EnsureCharacterName(data, character.Name);
            // Writing the normalized JSON also performs additive schema migration.
            // The original values and PDF fields are never removed by this step.
            await WriteDataAtomicallyAsync(dataPath, data, cancellationToken).ConfigureAwait(false);
            MarkV4MigrationComplete(character.Id);
            if (!File.Exists(pdfPath) || changed)
                await GeneratePdfAtomicallyAsync(pdfPath, data, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CharacterSheetData> LoadDataAsync(CharacterProfile character, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(character, cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadDataAsync(DataPath(character.Id), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveDataAsync(
        CharacterProfile character,
        CharacterSheetData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(data);
        data.UpdatedAt = DateTimeOffset.UtcNow;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_sheetDirectory);
            await WriteDataAtomicallyAsync(DataPath(character.Id), data, cancellationToken).ConfigureAwait(false);
            await GeneratePdfAtomicallyAsync(SheetPath(character.Id), data, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateCharacterNameAsync(CharacterProfile character, CancellationToken cancellationToken = default)
    {
        var data = await LoadDataAsync(character, cancellationToken).ConfigureAwait(false);
        data.TextFields["CharacterName"] = character.Name;
        data.TextFields["CharacterName 2"] = character.Name;
        await SaveDataAsync(character, data, cancellationToken).ConfigureAwait(false);
    }

    public async Task DuplicateAsync(
        CharacterProfile source,
        CharacterProfile target,
        CancellationToken cancellationToken = default)
    {
        var data = await LoadDataAsync(source, cancellationToken).ConfigureAwait(false);
        data.TextFields["CharacterName"] = target.Name;
        data.TextFields["CharacterName 2"] = target.Name;
        data.IsCreationDraft = false;
        await SaveDataAsync(target, data, cancellationToken).ConfigureAwait(false);
    }

    public async Task OpenAsync(CharacterProfile character, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(character, cancellationToken);
        await editor.OpenAsync(SheetPath(character.Id), $"Hoja de {character.Name}", cancellationToken);
    }

    public async Task<bool> PickAndImportAsync(CharacterProfile character, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        var selected = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = $"Elegir la hoja PDF de {character.Name}",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = ["application/pdf"],
                [DevicePlatform.iOS] = ["com.adobe.pdf"],
                [DevicePlatform.MacCatalyst] = ["com.adobe.pdf"],
                [DevicePlatform.WinUI] = [".pdf"]
            })
        });
        if (selected is null) return false;

        await using var input = await selected.OpenReadAsync();
        if (input.CanSeek && input.Length > MaximumImportBytes)
            throw new InvalidDataException("La hoja PDF supera el límite de 40 MB.");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_sheetDirectory);
            var pdfPath = SheetPath(character.Id);
            BackupCurrentSheet(character.Id, "before-import");
            await WritePdfAtomicallyAsync(pdfPath, input, cancellationToken).ConfigureAwait(false);
            await using var imported = File.OpenRead(pdfPath);
            var data = await pdfService.ReadAsync(imported, cancellationToken).ConfigureAwait(false);
            EnsureCharacterName(data, character.Name);
            await WriteDataAtomicallyAsync(DataPath(character.Id), data, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        return true;
    }

    public async Task ShareAsync(CharacterProfile character, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(character, cancellationToken);
        var exportDirectory = Path.Combine(FileSystem.Current.CacheDirectory, "character-sheets");
        Directory.CreateDirectory(exportDirectory);
        var exportPath = Path.Combine(exportDirectory, $"{SafeFileName(character.Name)} - Character Sheet.pdf");
        File.Copy(SheetPath(character.Id), exportPath, overwrite: true);
        cancellationToken.ThrowIfCancellationRequested();
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = $"Guardar o compartir la hoja de {character.Name}",
            File = new ShareFile(exportPath, "application/pdf")
        });
    }

    public async Task DeleteAsync(string characterId, CancellationToken cancellationToken = default)
    {
        var pdfPath = SheetPath(characterId);
        var dataPath = DataPath(characterId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(pdfPath)) File.Delete(pdfPath);
            if (File.Exists(dataPath)) File.Delete(dataPath);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task GeneratePdfAtomicallyAsync(string destination, CharacterSheetData data, CancellationToken cancellationToken)
    {
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using var template = await FileSystem.Current.OpenAppPackageFileAsync(TemplateFileName).ConfigureAwait(false);
            await using (var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await pdfService.WriteAsync(template, output, data, cancellationToken).ConfigureAwait(false);
            }
            await ValidatePdfAsync(temporary, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task<CharacterSheetData> ReadDataAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var input = File.OpenRead(path);
            var data = await JsonSerializer.DeserializeAsync<CharacterSheetData>(input, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            return NormalizeData(data ?? new CharacterSheetData());
        }
        catch (JsonException)
        {
            var backup = $"{path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            File.Copy(path, backup, overwrite: false);
            return new CharacterSheetData();
        }
    }

    private static async Task WriteDataAtomicallyAsync(
        string destination,
        CharacterSheetData data,
        CancellationToken cancellationToken)
    {
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(output, NormalizeData(data), JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static CharacterSheetData NormalizeData(CharacterSheetData data)
    {
        var textFields = (data.TextFields ?? [])
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrEmpty(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var checkedFields = (data.CheckedFields ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
        var abilityBases = (data.AbilityBaseScores ?? [])
            .Where(pair => IsAbility(pair.Key) && pair.Value is >= 1 and <= 30)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var ability in AbilityNames)
        {
            if (!abilityBases.ContainsKey(ability)
                && textFields.TryGetValue(ability, out var value)
                && int.TryParse(value, out var score)
                && score is >= 1 and <= 30)
                abilityBases[ability] = score;
        }

        var proficiencyRanks = (data.ProficiencyRanks ?? [])
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is >= 0 and <= 2)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var field in CharacterSheetSchema.Sections.SelectMany(section => section.Fields))
        {
            if (!string.IsNullOrWhiteSpace(field.FieldName)
                && !string.IsNullOrWhiteSpace(field.ToggleFieldName)
                && checkedFields.Contains(field.ToggleFieldName)
                && !proficiencyRanks.ContainsKey(field.FieldName))
                proficiencyRanks[field.FieldName] = 1;
        }

        var manualProficiencyRanks = (data.ManualProficiencyRanks ?? [])
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is >= 0 and <= 2)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var manualOtherBonuses = (data.ManualOtherBonuses ?? [])
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var effects = (data.Effects ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Id)
                && !string.IsNullOrWhiteSpace(item.SourceSelectionId)
                && !string.IsNullOrWhiteSpace(item.Target))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        // v1/v2 stored the user's entries directly in the effective dictionaries.
        // They become the manual baseline once, so later automatic effects stay reversible.
        if (effects.Count == 0)
        {
            if (manualProficiencyRanks.Count == 0)
                foreach (var pair in proficiencyRanks) manualProficiencyRanks[pair.Key] = pair.Value;
            if (manualOtherBonuses.Count == 0)
                foreach (var pair in data.OtherBonuses ?? []) manualOtherBonuses[pair.Key] = pair.Value;
        }

        var normalized = new CharacterSheetData
        {
            SchemaVersion = CharacterSheetData.CurrentSchemaVersion,
            RuleSet = data.RuleSet is "2024" or "Homebrew" ? data.RuleSet : "2014",
            IsCreationDraft = data.IsCreationDraft,
            CreatorStep = Math.Clamp(data.CreatorStep, 1, 8),
            PortraitDataUrl = IsSafePortrait(data.PortraitDataUrl) ? data.PortraitDataUrl : null,
            TextFields = textFields,
            CheckedFields = checkedFields,
            AbilityBaseScores = abilityBases,
            ProficiencyRanks = proficiencyRanks,
            ManualProficiencyRanks = manualProficiencyRanks,
            OtherBonuses = (data.OtherBonuses ?? [])
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            ManualOtherBonuses = manualOtherBonuses,
            Overrides = (data.Overrides ?? [])
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            Modifiers = (data.Modifiers ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Id)
                    && !string.IsNullOrWhiteSpace(item.SourceName)
                    && !string.IsNullOrWhiteSpace(item.Target))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList(),
            Features = (data.Features ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList(),
            Selections = (data.Selections ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Id)
                    && !string.IsNullOrWhiteSpace(item.ReferenceId)
                    && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList(),
            Effects = effects,
            KnownSpellIds = (data.KnownSpellIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal),
            Spells = (data.Spells ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList(),
            ClassLevels = (data.ClassLevels ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Id)
                    && !string.IsNullOrWhiteSpace(item.Name) && item.Level is >= 1 and <= 20)
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList(),
            Conditions = (data.Conditions ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList(),
            Attacks = (data.Attacks ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList(),
            Inventory = (data.Inventory ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList(),
            GeneratedTextFields = (data.GeneratedTextFields ?? [])
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            GeneratedCheckedFields = (data.GeneratedCheckedFields ?? [])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.Ordinal),
            UpdatedAt = data.UpdatedAt
        };
        EnsureClassProgression(normalized);
        CharacterSheetEffectEngine.Rebuild(normalized);
        return normalized;
    }

    private static void EnsureClassProgression(CharacterSheetData data)
    {
        if (data.ClassLevels.Count > 0) return;
        var selectedClass = data.Selections.LastOrDefault(item => item.IsActive
            && item.Kind == nameof(SearchContentKind.Class));
        if (selectedClass is null) return;
        var level = selectedClass.Level ?? 1;
        if (data.TextFields.TryGetValue("ClassLevel", out var classText))
        {
            var match = System.Text.RegularExpressions.Regex.Match(classText, @"\b(?:[1-9]|1\d|20)\b");
            if (match.Success && int.TryParse(match.Value, out var parsed)) level = parsed;
        }
        var subclass = data.Selections.LastOrDefault(item => item.IsActive && item.Kind == nameof(SearchContentKind.Subclass));
        data.ClassLevels.Add(new CharacterClassLevel
        {
            Name = selectedClass.Name,
            SelectionId = selectedClass.Id,
            SubclassName = subclass?.Name,
            SubclassSelectionId = subclass?.Id,
            Level = Math.Clamp(level, 1, 20),
            IsPrimary = true
        });
    }

    private async Task BackupBeforeV4MigrationAsync(
        string characterId,
        string dataPath,
        string pdfPath,
        CancellationToken cancellationToken)
    {
        var marker = MigrationMarkerPath(characterId);
        if (File.Exists(marker)) return;
        var schemaVersion = await ReadStoredSchemaVersionAsync(dataPath, cancellationToken).ConfigureAwait(false);
        if (schemaVersion >= CharacterSheetData.CurrentSchemaVersion)
        {
            MarkV4MigrationComplete(characterId);
            return;
        }

        var backupDirectory = Path.Combine(_sheetDirectory, "backups", SafeCharacterId(characterId));
        Directory.CreateDirectory(backupDirectory);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        File.Copy(dataPath, Path.Combine(backupDirectory, $"sheet-v{schemaVersion}-{stamp}.json"), overwrite: false);
        if (File.Exists(pdfPath))
            File.Copy(pdfPath, Path.Combine(backupDirectory, $"sheet-v{schemaVersion}-{stamp}.pdf"), overwrite: false);
    }

    private static async Task<int> ReadStoredSchemaVersionAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var input = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(input, cancellationToken: cancellationToken).ConfigureAwait(false);
            return document.RootElement.TryGetProperty("SchemaVersion", out var version)
                && version.TryGetInt32(out var parsed) ? parsed : 1;
        }
        catch (JsonException)
        {
            return 1;
        }
    }

    private void MarkV4MigrationComplete(string characterId)
    {
        var marker = MigrationMarkerPath(characterId);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        if (!File.Exists(marker)) File.WriteAllText(marker, CharacterSheetData.CurrentSchemaVersion.ToString());
    }

    private void BackupCurrentSheet(string characterId, string reason)
    {
        var dataPath = DataPath(characterId);
        var pdfPath = SheetPath(characterId);
        if (!File.Exists(dataPath) && !File.Exists(pdfPath)) return;
        var directory = Path.Combine(_sheetDirectory, "backups", SafeCharacterId(characterId), reason);
        Directory.CreateDirectory(directory);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        if (File.Exists(dataPath)) File.Copy(dataPath, Path.Combine(directory, $"sheet-{stamp}.json"), overwrite: false);
        if (File.Exists(pdfPath)) File.Copy(pdfPath, Path.Combine(directory, $"sheet-{stamp}.pdf"), overwrite: false);
    }

    private string MigrationMarkerPath(string characterId)
        => Path.Combine(_sheetDirectory, "backups", SafeCharacterId(characterId), "v4.complete");

    private static bool IsSafePortrait(string? value)
        => string.IsNullOrWhiteSpace(value)
            || value.Length <= 2_800_000
            && (value.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal)
                || value.StartsWith("data:image/png;base64,", StringComparison.Ordinal)
                || value.StartsWith("data:image/webp;base64,", StringComparison.Ordinal));

    private static readonly string[] AbilityNames = ["STR", "DEX", "CON", "INT", "WIS", "CHA"];

    private static bool IsAbility(string value)
        => AbilityNames.Contains(value, StringComparer.Ordinal);

    private static bool EnsureCharacterName(CharacterSheetData data, string name)
    {
        var changed = false;
        if (!data.TextFields.ContainsKey("CharacterName"))
        {
            data.TextFields["CharacterName"] = name;
            changed = true;
        }
        if (!data.TextFields.ContainsKey("CharacterName 2"))
        {
            data.TextFields["CharacterName 2"] = name;
            changed = true;
        }
        return changed;
    }

    private string SheetPath(string characterId) => Path.Combine(_sheetDirectory, $"{SafeCharacterId(characterId)}.pdf");
    private string DataPath(string characterId) => Path.Combine(_sheetDirectory, $"{SafeCharacterId(characterId)}.json");

    private static string SafeCharacterId(string characterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        if (characterId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(characterId, Path.GetFileName(characterId), StringComparison.Ordinal))
            throw new ArgumentException("El identificador del personaje no es válido.", nameof(characterId));
        return characterId;
    }

    private static async Task WritePdfAtomicallyAsync(string destination, Stream input, CancellationToken cancellationToken)
    {
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
            await ValidatePdfAsync(temporary, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static async Task ValidatePdfAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length < 8 || info.Length > MaximumImportBytes)
            throw new InvalidDataException("El archivo elegido no es una hoja PDF válida.");
        var header = new byte[5];
        await using var input = File.OpenRead(path);
        var read = await input.ReadAsync(header, cancellationToken).ConfigureAwait(false);
        if (read != header.Length || !header.AsSpan().SequenceEqual("%PDF-"u8))
            throw new InvalidDataException("El archivo elegido no es un PDF válido.");
    }

    private static string SafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "Personaje" : sanitized;
    }
}

public sealed class DefaultCharacterSheetEditor : ICharacterSheetEditor
{
    public Task OpenAsync(string pdfPath, string title, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Launcher.Default.OpenAsync(new OpenFileRequest(title, new ReadOnlyFile(pdfPath)));
    }
}
