using System.Text.Json;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using DndCompendium.Infrastructure.Persistence;

namespace DndCompendium.Linux;

internal static class DesktopApi
{
    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (Exception ex) when (ex is IOException or HttpRequestException or ArgumentException or InvalidOperationException or JsonException or KeyNotFoundException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        api.MapGet("/ping", () => Results.NoContent());
        api.MapPost("/exit", (IHostApplicationLifetime lifetime, HttpContext context) =>
        {
            context.Response.OnCompleted(() => { lifetime.StopApplication(); return Task.CompletedTask; });
            return Results.NoContent();
        });
        api.MapGet("/library", async (ILocalDocumentRepository library, IReadingHistoryStore history, CancellationToken ct) =>
            new { documents = await library.GetDocumentsAsync(ct), recent = await history.GetRecentAsync(ct) });
        api.MapPost("/library/download", (IDownloadManager downloads, CancellationToken ct) => downloads.DownloadAllAsync(ct));
        api.MapPost("/library/update", (IDownloadManager downloads, CancellationToken ct) => downloads.UpdateInstalledDocumentsAsync(ct));
        api.MapPost("/library/{id}/download", (string id, IDownloadManager downloads, CancellationToken ct) => downloads.DownloadAsync(id, ct));
        api.MapPost("/library/import", async (string name, HttpRequest request, ILocalDocumentRepository library, CancellationToken ct) =>
        {
            var file = Path.GetFileName(name);
            if (file != name || file.Contains('\\') || file.Length > 160 || !file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { error = "Elegí un archivo TXT de Homebrewery." });
            if (request.ContentLength is not (> 0 and <= 4L * 1024 * 1024))
                return Results.BadRequest(new { error = "El TXT debe tener entre 1 byte y 4 MB." });
            var id = $"local-{Guid.NewGuid():N}";
            var document = new SourceDocument
            {
                Id = id, Title = Path.GetFileNameWithoutExtension(file), FileName = file,
                Category = DocumentCategory.Unclassified, Description = "TXT importado en este equipo.",
                SizeBytes = request.ContentLength.Value, ModifiedAt = DateTimeOffset.UtcNow,
                Checksum = "", Version = id
            };
            await library.InstallAsync(document, request.Body, cancellationToken: ct);
            return Results.Ok(new { id });
        });
        api.MapGet("/reader/{id}", async (string id, ILocalDocumentRepository library, IDocumentResourceManager resources,
            IReadingHistoryStore history, CancellationToken ct) =>
        {
            var item = await library.ReadAsync(id, ct);
            if (item is null) return Results.NotFound(new { error = "Descargá el documento para leerlo sin conexión." });
            var manifest = await resources.GetManifestAsync(id, ct);
            await history.RecordOpenedAsync(id, item.Title, ct);
            var available = manifest.Resources.Select((resource, index) => (resource, index))
                .Where(pair => pair.resource.State == DocumentResourceState.Available)
                .Select(pair => new { index = pair.index, sha = pair.resource.Sha256 });
            return Results.Ok(new { item.Title, content = item.SourceText, state = manifest.OfflineState, resources = available });
        });
        api.MapGet("/reader/{id}/resource/{index:int}", async (string id, int index, string sha,
            IDocumentResourceManager resources, CancellationToken ct) =>
        {
            var manifest = await resources.GetManifestAsync(id, ct);
            if ((uint)index >= (uint)manifest.Resources.Count || manifest.Resources[index].Sha256 != sha)
                return Results.NotFound();
            var item = await resources.ReadResourceAsync(manifest.Resources[index], ct);
            return item is null ? Results.NotFound()
                : Results.Ok(new { source = item.SourceUrl, item.MimeType, base64 = Convert.ToBase64String(item.Content) });
        });
        api.MapGet("/search", async (string q, SearchContentKind? kind, IUniversalSearchService search, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q)) return Results.Ok(Array.Empty<object>());
            if (q.Length > 250) return Results.BadRequest(new { error = "La consulta supera los 250 caracteres." });
            await search.RefreshIndexAsync(ct);
            return Results.Ok(await search.SearchAsync(q, kind, 80, ct));
        });

        api.MapGet("/spells", async (ISpellRepository spells, CancellationToken ct) =>
            await spells.GetAllAsync(false, ct));
        api.MapGet("/exploits", async (IExploitRepository exploits, CancellationToken ct) =>
            await exploits.GetAllAsync(false, ct));
        api.MapPost("/spells/update", (ISpellRepository spells, CancellationToken ct) => spells.UpdateFromDriveAsync(ct));
        api.MapPost("/exploits/update", (IExploitRepository exploits, CancellationToken ct) => exploits.UpdateFromDriveAsync(ct));
        api.MapGet("/saved", async (ICharacterProfileStore profiles, CancellationToken ct) => new
        {
            favorite = await profiles.GetFavoriteOrderAsync(ct),
            prepared = await profiles.GetPreparedOrderAsync(ct)
        });
        api.MapPut("/saved/{kind}/{state}/{id}", (SavedContentKind kind, SavedStateKind state, string id, SavedChange change,
            ICharacterProfileStore profiles, CancellationToken ct) => profiles.SetSavedAsync(kind, state, id, change.Enabled, ct));
        api.MapPut("/saved/{state}/order", (SavedStateKind state, SavedItemReference[] order,
            ICharacterProfileStore profiles, CancellationToken ct) => state == SavedStateKind.Prepared
            ? profiles.SetPreparedOrderAsync(order, ct) : profiles.SetFavoriteOrderAsync(order, ct));

        api.MapGet("/characters", async (ICharacterProfileStore profiles, CancellationToken ct) => new
        {
            items = await profiles.GetAllAsync(ct),
            active = await profiles.GetActiveAsync(ct)
        });
        api.MapPost("/characters", (NameChange change, ICharacterProfileStore profiles, CancellationToken ct) => profiles.CreateAsync(change.Name, ct));
        api.MapPut("/characters/{id}/name", (string id, NameChange change, ICharacterProfileStore profiles, CancellationToken ct) =>
            profiles.RenameAsync(id, change.Name, ct));
        api.MapPut("/characters/{id}/active", (string id, ICharacterProfileStore profiles, CancellationToken ct) => profiles.SetActiveAsync(id, ct));
        api.MapDelete("/characters/{id}", (string id, ICharacterProfileStore profiles, CancellationToken ct) => profiles.DeleteAsync(id, ct));
        api.MapGet("/characters/export", async (JsonCharacterProfileStore profiles, AppFiles files, CancellationToken ct) =>
            Results.File(await files.ExportAsync(profiles, ct), "application/json", "Drivebrew-personaje.json"));
        api.MapGet("/characters/backup", (JsonCharacterProfileStore profiles) => File.Exists(profiles.PreviousCharacterBackupPath)
            ? Results.File(profiles.PreviousCharacterBackupPath, "application/json", "Drivebrew-respaldo-anterior.json")
            : Results.NotFound(new { error = "Todavía no hay un respaldo anterior." }));
        api.MapPost("/characters/import", (HttpRequest request, JsonCharacterProfileStore profiles, AppFiles files, CancellationToken ct) =>
            files.ImportAsync(profiles, request.Body, ct));
        api.MapGet("/characters/{id}/sheet", async (string id, ICharacterProfileStore profiles, ISpellRepository spells, AppFiles files, CancellationToken ct) =>
        {
            var character = await GetCharacterAsync(id, profiles, ct);
            var data = await files.ReadAsync(character, ct);
            IReadOnlyList<Spell> allSpells;
            try { allSpells = await spells.GetAllAsync(false, ct); }
            catch (IOException) { allSpells = []; }
            var prepared = await profiles.GetSavedIdsAsync(SavedContentKind.Spell, SavedStateKind.Prepared, ct);
            return new
            {
                characterId = character.Id, characterName = character.Name,
                data.HtmlSheetJson, data.TextFields, data.AbilityBaseScores, data.ClassLevels, data.Attacks,
                preparedSpells = character.Id == (await profiles.GetActiveAsync(ct)).Id
                    ? allSpells.Where(spell => prepared.Contains(spell.Id)) : [],
                knownSpells = allSpells.Where(spell => data.KnownSpellIds.Contains(spell.Id))
            };
        });
        api.MapPut("/characters/{id}/sheet", async (string id, SheetChange change, ICharacterProfileStore profiles, AppFiles files, CancellationToken ct) =>
            await files.SaveAsync(await GetCharacterAsync(id, profiles, ct), change.SheetJson, profiles, ct));
        api.MapGet("/resources", (ICharacterProfileStore profiles, CancellationToken ct) => profiles.GetResourcesAsync(ct));
        api.MapPut("/resources", (CharacterResource item, ICharacterProfileStore profiles, CancellationToken ct) => profiles.UpsertResourceAsync(item, ct));
        api.MapPost("/resources/{id}/change", (string id, ResourceChange change, ICharacterProfileStore profiles, CancellationToken ct) =>
            profiles.AdjustResourceAsync(id, change.Delta, ct));
        api.MapDelete("/resources/{id}", (string id, ICharacterProfileStore profiles, CancellationToken ct) => profiles.DeleteResourceAsync(id, ct));
        api.MapPost("/resources/rest/{rest}", (RestKind rest, ICharacterProfileStore profiles, CancellationToken ct) => profiles.ApplyRestAsync(rest, ct));
    }

    private static async Task<CharacterProfile> GetCharacterAsync(string id, ICharacterProfileStore store, CancellationToken ct)
        => (await store.GetAllAsync(ct)).FirstOrDefault(character => character.Id == id)
            ?? throw new KeyNotFoundException("Ese personaje ya no existe.");

    private sealed record NameChange(string Name);
    private sealed record SheetChange(string SheetJson);
    private sealed record SavedChange(bool Enabled);
    private sealed record ResourceChange(int Delta);
}
