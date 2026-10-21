using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.Infrastructure.Sources;

/// <summary>
/// Public, read-only catalog backed by the project owner's shared Google Drive folder.
/// Stable Drive file ids let an edited TXT be refreshed without changing the application.
/// </summary>
public sealed class GoogleDriveDocumentSource : IDocumentSource
{
    private const long MaximumDocumentBytes = 4 * 1024 * 1024;
    private const string DownloadUrlFormat =
        "https://drive.usercontent.google.com/download?id={0}&export=download&confirm=t";
    private static readonly IReadOnlyDictionary<string, DriveDocumentDefinition> Definitions =
        CreateDefinitions().ToDictionary(definition => definition.Id, StringComparer.Ordinal);
    private static readonly HashSet<string> EmbeddedFallbackIds =
        new(["magus", "psion", "backgrounds-feats-equipment"], StringComparer.Ordinal);

    private readonly HttpClient _httpClient;
    private readonly EmbeddedDocumentSource _embeddedFallback;

    public GoogleDriveDocumentSource(HttpClient httpClient, EmbeddedDocumentSource embeddedFallback)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _embeddedFallback = embeddedFallback ?? throw new ArgumentNullException(nameof(embeddedFallback));
    }

    public async Task<IReadOnlyList<SourceDocument>> GetCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        using var concurrency = new SemaphoreSlim(6, 6);
        var documents = await Task.WhenAll(Definitions.Values.Select(async definition =>
        {
            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ReadMetadataAsync(definition, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                concurrency.Release();
            }
        })).ConfigureAwait(false);

        return documents.OrderBy(document => document.Title, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<Stream> OpenReadAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (!Definitions.TryGetValue(documentId, out var definition))
        {
            throw new KeyNotFoundException($"Drive document '{documentId}' does not exist in the catalog.");
        }

        try
        {
            Exception? lastError = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    return await DownloadDocumentAsync(definition, cancellationToken).ConfigureAwait(false);
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

            throw new IOException("No se pudo descargar el TXT de Drive después de tres intentos.", lastError);
        }
        catch (Exception exception) when (
            EmbeddedFallbackIds.Contains(documentId)
            && exception is HttpRequestException or IOException
                or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            return await _embeddedFallback.OpenReadAsync(documentId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<Stream> DownloadDocumentAsync(
        DriveDocumentDefinition definition,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CreateDownloadUrl(definition.FileId));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (string.Equals(
            response.Content.Headers.ContentType?.MediaType,
            "text/html",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Google Drive devolvió una página HTML en lugar del TXT.");
        }

        var announcedLength = response.Content.Headers.ContentLength;
        if (announcedLength is > MaximumDocumentBytes)
        {
            throw new InvalidDataException("El TXT de Drive supera el tamaño máximo permitido.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var result = new MemoryStream(announcedLength is > 0 ? checked((int)announcedLength.Value) : 0);
        await source.CopyToAsync(result, cancellationToken).ConfigureAwait(false);
        if (result.Length > MaximumDocumentBytes)
        {
            result.Dispose();
            throw new InvalidDataException("El TXT de Drive supera el tamaño máximo permitido.");
        }

        result.Position = 0;
        return result;
    }

    private async Task<SourceDocument> ReadMetadataAsync(
        DriveDocumentDefinition definition,
        CancellationToken cancellationToken)
    {
        var size = definition.SizeBytes;
        var modifiedAt = definition.ModifiedAt;
        var isAuthoritative = false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, CreateDownloadUrl(definition.FileId));
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                if (response.Content.Headers.ContentLength is long remoteSize
                    && response.Content.Headers.LastModified is DateTimeOffset remoteModifiedAt)
                {
                    size = remoteSize;
                    modifiedAt = remoteModifiedAt;
                    isAuthoritative = true;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Metadata is best-effort. Android network stacks can surface transient transport
            // failures as platform-specific exception types (for example "Socket closed").
            // The baseline catalog must remain usable; actual downloads still report errors.
        }

        return new SourceDocument
        {
            Id = definition.Id,
            Title = definition.Title,
            FileName = definition.FileName,
            Category = definition.Category,
            Description = definition.Description,
            SizeBytes = size,
            ModifiedAt = modifiedAt,
            Checksum = string.Empty,
            Version = $"drive:{definition.FileId}:{modifiedAt.ToUnixTimeMilliseconds()}:{size}",
            IsVersionAuthoritative = isAuthoritative
        };
    }

    private static Uri CreateDownloadUrl(string fileId)
        => new(string.Format(System.Globalization.CultureInfo.InvariantCulture, DownloadUrlFormat, fileId));

    private static IEnumerable<DriveDocumentDefinition> CreateDefinitions()
    {
        yield return Class("alchemist", "Alchemist", "18YplwZBKecWbSZjVXwzB10-Rk8kqfwfu", 134522, "2026-06-01T19:28:00.737Z");
        yield return Class("artificer", "Artificer", "10K3-gHNd50zW5RWbnnC1n15u2ux2hs8c", 181293, "2026-07-12T21:41:18.908Z");
        yield return Class("barbarian", "Barbarian", "1-PhUM2cwhuTDcmjlPgiaBz-HSAEqT1fb", 211066, "2026-10-02T22:03:59.341Z");
        yield return Class("bard", "Bard", "1ofSkCKPgx_XlDOtn9kqaZkM9ajvyL7vd", 191402, "2026-10-03T14:14:43.254Z");
        yield return Class("binder", "Binder", "1076i1JVbPbJ5vGO_sPP3NUPgrnqH8Q9m", 168437, "2024-07-27T06:28:37.432Z");
        yield return Class("blood-hunter", "Blood Hunter", "14dYho679ng7hdN1UF80Po7RY-zO1MulH", 105073, "2026-02-17T14:53:20.726Z");
        yield return Class("captain", "Captain", "1hfPBX2hMnEn3AVmbodna1NEhsFk5k4CO", 210324, "2026-05-10T01:44:24.896Z");
        yield return Class("cleric", "Cleric", "1r5QD0a02uK00cc4Ho79wO_l_tsSLoNzn", 167262, "2026-09-14T18:38:18.917Z");
        yield return Class("commoner", "Commoner", "1fz1e4XWmyEbqQpMs2urnLOUZfw7-25H-", 36165, "2026-03-09T17:14:29.666Z");
        yield return Class("craftsman", "Craftsman", "15T9DEuM8E6B5D4Ga9zJmcp2n0kHxgxK1", 224053, "2026-07-12T07:05:34.169Z");
        yield return Class("druid", "Druid", "1nyIghESSxiD_ValZMeA23n1Iur-e0tIW", 167748, "2026-09-14T18:52:27.568Z");
        yield return Class("fighter", "Fighter", "1LmjnVXajtI0zEKn9tLc3o-8ZymapArfS", 251014, "2026-07-19T00:15:50.847Z");
        yield return Class("gadgeteer", "Gadgeteer", "1CoAiX3E7bfcB1B96c_F6nNBSj8OtJ_bP", 119096, "2026-07-04T16:06:52.903Z");
        yield return Class("gunslinger", "Gunslinger", "1nLzmgD2_lnYt1i1Jc5QNTIQQcqJr4Exi", 115207, "2026-05-08T18:00:54.173Z");
        yield return Class("illrigger", "Illrigger", "1PjyH7QxjGm2GPCpWyGziG1TR_nF98k2B", 165582, "2026-08-27T21:01:17.231Z");
        yield return Class("investigator", "Investigator", "1xfcrMhDs8oc2AoboNHHBQPTglJKtXwNp", 77886, "2025-11-21T21:54:37.063Z");
        yield return Class("magus", "Magus", "1Lt24mPs0pGIDqmm5aLQ_XlgPOFghYsPV", 137250, "2026-10-08T15:42:54.276Z");
        yield return Class("martyr", "Martyr", "1jHIibn2g5UBTr07E63r5Y4UKKRh-Aa9a", 88444, "2026-07-19T02:13:31.460Z");
        yield return Class("monk", "Monk", "11ZIvWZpXO8YiuVP98W_TcJC1HisCtOIp", 142855, "2026-07-19T05:02:17.565Z");
        yield return Class("necromancer", "Necromancer", "1GkmtNxouyRhPGyj3a41FM2eVadQ1xp19", 117734, "2026-04-27T00:11:00.228Z");
        yield return Class("paladin", "Paladin", "1TCEG6t44MyloVlMy9WTlbQ9S5Zn_dUU6", 232664, "2026-09-14T19:13:28.684Z");
        yield return Class("psion", "Psion", "1Zc4cwAUVU28oWiUAErfVQk_q833n5q0X", 132973, "2026-10-12T23:39:31.501Z");
        yield return Class("ranger", "Ranger", "1xcFkCpEIz4-PLV8ZpdXil4BBaqoyQhWN", 202477, "2026-07-21T22:29:21.753Z");
        yield return Class("rogue", "Rogue", "1yL3uTtJAskxvL7Eb9KjzFHgoAoDKsHL8", 208620, "2026-07-19T00:26:58.431Z");
        yield return Class("savant", "Savant", "1ZrOzeqrjNE-r4IMOWV35kN7z8JEKDvpy", 122902, "2026-08-27T05:13:34.170Z");
        yield return Class("shaman", "Shaman", "1iPDVbgdzcgW095d2hE6Ch47fbPsFf8n-", 93463, "2026-03-09T14:47:26.614Z");
        yield return Class("sorcerer", "Sorcerer", "1HHvxABKQnXEheDSRWU1S6QBzggoPOE08", 189266, "2026-07-19T03:56:32.277Z");
        yield return Class("vagabond", "Vagabond", "1-cFQaaSM7mMvkl6sPl5aJc0v0G_VKbm9", 96464, "2026-06-01T10:13:43.837Z");
        yield return Class("vessel", "Vessel", "1He6FHxmfGh537lTcL8eZpP4PEQaHaxQ1", 181183, "2026-09-26T02:55:27.305Z");
        yield return Class("warden", "Warden", "18DU3h_ymwtb1MIPe_4fyKCMmaRtxQG4J", 139538, "2026-05-05T16:22:20.285Z");
        yield return Class("warlock", "Warlock", "1C_OPgQxxP2U6drO9rbcLqzDUhhrQlPof", 207266, "2026-09-14T19:53:55.587Z");
        yield return Class("warlord", "Warlord", "11aNc9jF3nkDuEWRpl8A1yvxlVgn2dVA2", 93748, "2026-03-09T16:42:27.371Z");
        yield return Class("warmage", "Warmage", "1ILGs1z_pxyNS8LxZznsaoEC9tHRpJszp", 95577, "2026-05-24T06:54:34.315Z");
        yield return Class("witch", "Witch", "1ZkklOTe3cQKV30PbhHUaGTXemv56-9WO", 186122, "2026-06-07T04:44:38.995Z");
        yield return Class("wizard", "Wizard", "11IusCtEgvIWBsbbbK_KM8MVIdmEb0r76", 198399, "2026-09-14T20:26:14.364Z");
        yield return Supplement(
            "chronicles-lito-urls",
            "The Chronicles of Lito and Urls Across the Multiverse",
            "1 The Chronicles of Lito and Urls Across the Multiverse.txt",
            "1uRqIhf85bMRF6nvPWPxags8Z7q5tCJdz",
            328332,
            "2026-07-18T00:59:13.113Z",
            DocumentCategory.Unclassified,
            "Crónica Homebrewery V3 del Drive compartido.");
        yield return Supplement(
            "weird-arcana-table",
            "Weird Arcana Table",
            "Weird Arcana Table.txt",
            "1txdWvf0jzr-3GSCFzoP6HJvPTvTUGrvC",
            13749,
            "2026-08-05T14:41:56.010Z",
            DocumentCategory.Unclassified,
            "Tabla de arcanos extraños del Drive compartido.");
        yield return Supplement(
            "wild-magic-surge-table",
            "Wild Magic Surge Table",
            "Wild Magic Surge Table.txt",
            "1dBL0ZZIfDnoY29flQY5F4NiLTKQROMS4",
            33502,
            "2026-08-05T14:41:24.746Z",
            DocumentCategory.Unclassified,
            "Tabla de magia salvaje del Drive compartido.");
        yield return Supplement(
            "manual-summary",
            "Resumen del Manual",
            "Resumen del Manual.txt",
            "1udMjIEerLxqMCyUpKbfw1gxOwnaUbtFJ",
            8003,
            "2026-07-12T18:38:47.138Z",
            DocumentCategory.Unclassified,
            "Resumen del manual del Drive compartido.");
        yield return Supplement(
            "extra-subclasses",
            "Subclases Extras",
            "Subclases Extras.txt",
            "1rIYVcNGT-uo5XNz5DoNE5Hn_SxTEALij",
            26546,
            "2026-06-30T01:23:30.983Z",
            DocumentCategory.Subclasses,
            "Subclases adicionales del Drive compartido.");
        yield return Supplement(
            "races",
            "Razas",
            "Razas.txt",
            "1ddAAJ1KG2P2uMMn43lEPJLOJiptpnfsV",
            174379,
            "2026-04-18T07:59:56.016Z",
            DocumentCategory.Races,
            "Razas Homebrewery V3 del Drive compartido.");
        yield return new DriveDocumentDefinition(
            "backgrounds-feats-equipment",
            "Backgrounds, Feats & Equipment",
            "Backgrounds, Feats & Equipment.txt",
            "1he3Fc00DQZVKi58eB6vj_AKhe2mK6SYt",
            289316,
            DateTimeOffset.Parse("2026-08-05T22:59:51Z", System.Globalization.CultureInfo.InvariantCulture),
            DocumentCategory.Equipment,
            "Trasfondos, dotes y equipamiento Homebrewery V3 del Drive compartido.");
    }

    private static DriveDocumentDefinition Class(
        string id,
        string title,
        string fileId,
        long sizeBytes,
        string modifiedAt)
        => new(
            id,
            title,
            $"{title}.txt",
            fileId,
            sizeBytes,
            DateTimeOffset.Parse(modifiedAt, System.Globalization.CultureInfo.InvariantCulture),
            DocumentCategory.Classes,
            "Clase Homebrewery V3 del Drive compartido; se puede descargar y actualizar.");

    private static DriveDocumentDefinition Supplement(
        string id,
        string title,
        string fileName,
        string fileId,
        long sizeBytes,
        string modifiedAt,
        DocumentCategory category,
        string description)
        => new(
            id,
            title,
            fileName,
            fileId,
            sizeBytes,
            DateTimeOffset.Parse(modifiedAt, System.Globalization.CultureInfo.InvariantCulture),
            category,
            description);

    private sealed record DriveDocumentDefinition(
        string Id,
        string Title,
        string FileName,
        string FileId,
        long SizeBytes,
        DateTimeOffset ModifiedAt,
        DocumentCategory Category,
        string Description);
}
