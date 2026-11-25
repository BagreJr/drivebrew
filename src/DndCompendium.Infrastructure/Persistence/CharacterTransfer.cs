using System.Text.Json;
using DndCompendium.Core.Models;

namespace DndCompendium.Infrastructure.Persistence;

public sealed partial class JsonCharacterProfileStore
{
    private const int TransferVersion = 1;
    private const int TransferLimit = 32 * 1024 * 1024;

    public async Task ExportActiveCharacterAsync(Stream output)
    {
        await _gate.WaitAsync();
        try
        {
            var state = await EnsureStateAsync(default);
            var copy = await CreateTransferAsync(state, FindCharacter(state, state.ActiveCharacterId));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(copy, JsonOptions);
            if (bytes.Length > TransferLimit) throw new InvalidDataException("La copia supera los 32 MB.");
            await output.WriteAsync(bytes);
        }
        finally { _gate.Release(); }
    }

    public async Task<string> ImportCharacterAsync(Stream input)
    {
        var copy = await ReadTransferAsync(input);
        await _gate.WaitAsync();
        try
        {
            var state = await EnsureStateAsync(default);
            var character = copy.Character;
            var existing = state.Characters.FirstOrDefault(item => item.Id == character.Id);
            var next = new ProfileState
            {
                ActiveCharacterId = character.Id,
                Characters = [.. state.Characters.Where(item => item.Id != character.Id)],
                Saved = [.. state.Saved.Where(item => item.CharacterId != character.Id), copy.Saved]
            };
            var originalName = character.Name;
            for (var suffix = 2; next.Characters.Any(item => string.Equals(item.Name, character.Name, StringComparison.CurrentCultureIgnoreCase)); suffix++)
                character.Name = $"{originalName[..Math.Min(originalName.Length, 50)]} ({suffix})";
            next.Characters.Add(character);
            NormalizeState(next);

            if (existing is not null)
            {
                var previous = await CreateTransferAsync(state, existing);
                await WriteTransferFileAsync(PreviousCharacterBackupPath, JsonSerializer.SerializeToUtf8Bytes(previous, JsonOptions));
            }

            var sheetPath = TransferSheetPath(character.Id);
            byte[]? previousSheet = File.Exists(sheetPath) ? await File.ReadAllBytesAsync(sheetPath) : null;
            if (copy.Sheet is not null)
                await WriteTransferFileAsync(sheetPath, JsonSerializer.SerializeToUtf8Bytes(copy.Sheet, JsonOptions));
            try
            {
                await SaveStateAsync(next, default);
                _state = next;
            }
            catch
            {
                if (copy.Sheet is not null)
                {
                    if (previousSheet is null) File.Delete(sheetPath);
                    else await WriteTransferFileAsync(sheetPath, previousSheet);
                }
                throw;
            }
        }
        finally { _gate.Release(); }
        ActiveCharacterChanged?.Invoke(this, EventArgs.Empty);
        return copy.Character.Name;
    }

    public string PreviousCharacterBackupPath => Path.Combine(_rootPath, "profiles", "previous-character.json");

    private async Task<CharacterTransfer> CreateTransferAsync(ProfileState state, CharacterData character)
    {
        var path = TransferSheetPath(character.Id);
        CharacterSheetData? sheet = null;
        if (File.Exists(path))
        {
            await using var stream = File.OpenRead(path);
            sheet = await JsonSerializer.DeserializeAsync<CharacterSheetData>(stream, JsonOptions);
        }
        return new CharacterTransfer(TransferVersion, character, GetSavedData(state, character.Id), sheet);
    }

    private static async Task<CharacterTransfer> ReadTransferAsync(Stream input)
    {
        using var content = new MemoryStream();
        var buffer = new byte[81920];
        int length;
        while ((length = await input.ReadAsync(buffer)) > 0)
        {
            if (content.Length + length > TransferLimit) throw new InvalidDataException("La copia supera los 32 MB.");
            content.Write(buffer, 0, length);
        }
        content.Position = 0;
        var copy = await JsonSerializer.DeserializeAsync<CharacterTransfer>(content, JsonOptions);
        if (copy is not { Version: TransferVersion, Character: { } character, Saved: { } saved }
            || !Guid.TryParseExact(character.Id, "N", out var id) || character.Id != id.ToString("N")
            || saved.CharacterId != character.Id
            || string.IsNullOrWhiteSpace(character.Name) || character.Name.Length > 60
            || copy.Sheet is { SchemaVersion: > CharacterSheetData.CurrentSchemaVersion })
            throw new InvalidDataException("La copia del personaje es inválida o requiere una versión más nueva.");
        return copy;
    }

    private string TransferSheetPath(string id) => Path.Combine(_rootPath, "character-sheets", $"{id}.json");

    private static async Task WriteTransferFileAsync(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private sealed record CharacterTransfer(int Version, CharacterData Character, CharacterSavedData Saved, CharacterSheetData? Sheet);
}
