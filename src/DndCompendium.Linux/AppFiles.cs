using System.Text.Json;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using DndCompendium.Infrastructure.Persistence;

namespace DndCompendium.Linux;

internal sealed class AppFiles(string root)
{
    private static readonly JsonSerializerOptions SheetJson = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public static string DataDirectory()
    {
        var specified = Environment.GetEnvironmentVariable("DRIVEBREW_DATA_DIR");
        if (!string.IsNullOrEmpty(specified) && Path.IsPathFullyQualified(specified)) return specified;
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(xdg) || !Path.IsPathFullyQualified(xdg))
            xdg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        return Path.Combine(xdg, "drivebrew");
    }

    public async Task<CharacterSheetData> ReadAsync(CharacterProfile character, CancellationToken ct)
    {
        var path = SheetPath(character.Id);
        if (!File.Exists(path)) return new CharacterSheetData { IsCreationDraft = true };
        await using var input = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CharacterSheetData>(input, SheetJson, ct)
            ?? new CharacterSheetData();
    }

    public async Task SaveAsync(CharacterProfile character, string snapshot, ICharacterProfileStore profiles, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(snapshot) || snapshot.Length > 2_000_000)
            throw new InvalidDataException("La hoja está vacía o supera el tamaño permitido.");
        using var parsed = JsonDocument.Parse(snapshot);
        if (!parsed.RootElement.TryGetProperty("chars", out var chars)
            || chars.ValueKind != JsonValueKind.Array || chars.GetArrayLength() != 1
            || chars[0].ValueKind != JsonValueKind.Object
            || !chars[0].TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Formato de hoja inválido.");
        if (!chars[0].TryGetProperty("id", out var id) || id.GetString() != character.Id)
            throw new InvalidDataException("La hoja pertenece a otro personaje.");

        await _gate.WaitAsync(ct);
        try
        {
            if (!(await profiles.GetAllAsync(ct)).Any(item => item.Id == character.Id))
                throw new KeyNotFoundException("Ese personaje ya no existe.");
            var data = await ReadAsync(character, ct);
            data.HtmlSheetJson = snapshot;
            data.UpdatedAt = DateTimeOffset.UtcNow;
            if (info.TryGetProperty("name", out var nameField) && nameField.ValueKind == JsonValueKind.String)
            {
                var name = nameField.GetString()?.Trim();
                if (!string.IsNullOrEmpty(name) && name.Length <= 60 && name != character.Name)
                {
                    try { await profiles.RenameAsync(character.Id, name, ct); }
                    catch (InvalidOperationException) { name = character.Name; }
                }
                data.TextFields["CharacterName"] = string.IsNullOrEmpty(name) ? character.Name : name;
                data.TextFields["CharacterName 2"] = data.TextFields["CharacterName"];
            }
            var path = SheetPath(character.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var output = File.Create(temporary))
                    await JsonSerializer.SerializeAsync(output, data, SheetJson, ct);
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { _gate.Release(); }
    }

    public async Task<string> ImportAsync(JsonCharacterProfileStore profiles, Stream stream, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return await profiles.ImportCharacterAsync(stream); }
        finally { _gate.Release(); }
    }

    public async Task<byte[]> ExportAsync(JsonCharacterProfileStore profiles, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var content = new MemoryStream();
            await profiles.ExportActiveCharacterAsync(content);
            return content.ToArray();
        }
        finally { _gate.Release(); }
    }

    private string SheetPath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out var guid) || guid.ToString("N") != id)
            throw new InvalidDataException("Identificador de personaje inválido.");
        return Path.Combine(root, "character-sheets", $"{id}.json");
    }
}
