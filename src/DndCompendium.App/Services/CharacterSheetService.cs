using System.Text.Json;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
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
                data = await ReadDataAsync(dataPath, cancellationToken).ConfigureAwait(false);
            }
            else if (File.Exists(pdfPath))
            {
                await using var pdf = File.OpenRead(pdfPath);
                data = await pdfService.ReadAsync(pdf, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                data = new CharacterSheetData();
            }

            var changed = EnsureCharacterName(data, character.Name);
            // Writing the normalized JSON also performs additive schema migration.
            // The original values and PDF fields are never removed by this step.
            await WriteDataAtomicallyAsync(dataPath, data, cancellationToken).ConfigureAwait(false);
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

        return new CharacterSheetData
        {
            SchemaVersion = CharacterSheetData.CurrentSchemaVersion,
            RuleSet = data.RuleSet is "2024" or "Homebrew" ? data.RuleSet : "2014",
            TextFields = textFields,
            CheckedFields = checkedFields,
            AbilityBaseScores = abilityBases,
            ProficiencyRanks = proficiencyRanks,
            OtherBonuses = (data.OtherBonuses ?? [])
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
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
            KnownSpellIds = (data.KnownSpellIds ?? [])
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal),
            UpdatedAt = data.UpdatedAt
        };
    }

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
