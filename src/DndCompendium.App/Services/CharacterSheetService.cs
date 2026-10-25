using DndCompendium.Core.Models;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace DndCompendium.App.Services;

public interface ICharacterSheetEditor
{
    Task OpenAsync(string pdfPath, string title, CancellationToken cancellationToken = default);
}

public interface ICharacterSheetService
{
    Task EnsureCreatedAsync(CharacterProfile character, CancellationToken cancellationToken = default);

    Task OpenAsync(CharacterProfile character, CancellationToken cancellationToken = default);

    Task<bool> PickAndImportAsync(CharacterProfile character, CancellationToken cancellationToken = default);

    Task ShareAsync(CharacterProfile character, CancellationToken cancellationToken = default);

    Task DeleteAsync(string characterId, CancellationToken cancellationToken = default);
}

public sealed class CharacterSheetService(ICharacterSheetEditor editor) : ICharacterSheetService
{
    private const string TemplateFileName = "character_sheet_template.pdf";
    private const long MaximumImportBytes = 40L * 1024L * 1024L;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _sheetDirectory = Path.Combine(FileSystem.Current.AppDataDirectory, "character-sheets");

    public async Task EnsureCreatedAsync(
        CharacterProfile character,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        var destination = SheetPath(character.Id);
        if (File.Exists(destination)) return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(destination)) return;
            Directory.CreateDirectory(_sheetDirectory);
            await using var template = await FileSystem.Current
                .OpenAppPackageFileAsync(TemplateFileName)
                .ConfigureAwait(false);
            await WritePdfAtomicallyAsync(destination, template, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task OpenAsync(
        CharacterProfile character,
        CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(character, cancellationToken);
        await editor.OpenAsync(
            SheetPath(character.Id),
            $"Hoja de {character.Name}",
            cancellationToken);
    }

    public async Task<bool> PickAndImportAsync(
        CharacterProfile character,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        var options = new PickOptions
        {
            PickerTitle = $"Elegir la hoja PDF de {character.Name}",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.Android] = ["application/pdf"],
                [DevicePlatform.iOS] = ["com.adobe.pdf"],
                [DevicePlatform.MacCatalyst] = ["com.adobe.pdf"],
                [DevicePlatform.WinUI] = [".pdf"]
            })
        };
        var selected = await FilePicker.Default.PickAsync(options).ConfigureAwait(false);
        if (selected is null) return false;

        await using var input = await selected.OpenReadAsync().ConfigureAwait(false);
        if (input.CanSeek && input.Length > MaximumImportBytes)
        {
            throw new InvalidDataException("La hoja PDF supera el límite de 40 MB.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_sheetDirectory);
            await WritePdfAtomicallyAsync(SheetPath(character.Id), input, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        return true;
    }

    public async Task ShareAsync(
        CharacterProfile character,
        CancellationToken cancellationToken = default)
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
        var path = SheetPath(characterId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string SheetPath(string characterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        if (characterId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(characterId, Path.GetFileName(characterId), StringComparison.Ordinal))
        {
            throw new ArgumentException("El identificador del personaje no es válido.", nameof(characterId));
        }
        return Path.Combine(_sheetDirectory, $"{characterId}.pdf");
    }

    private static async Task WritePdfAtomicallyAsync(
        string destination,
        Stream input,
        CancellationToken cancellationToken)
    {
        var temporary = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
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
        {
            throw new InvalidDataException("El archivo elegido no es una hoja PDF válida.");
        }

        var header = new byte[5];
        await using var input = File.OpenRead(path);
        var read = await input.ReadAsync(header, cancellationToken).ConfigureAwait(false);
        if (read != header.Length || !header.AsSpan().SequenceEqual("%PDF-"u8))
        {
            throw new InvalidDataException("El archivo elegido no es un PDF válido.");
        }
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
