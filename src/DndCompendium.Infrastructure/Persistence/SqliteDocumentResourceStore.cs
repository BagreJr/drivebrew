using System.Security.Cryptography;
using System.Text;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using SQLite;

namespace DndCompendium.Infrastructure.Persistence;

public sealed class SqliteDocumentResourceStore : IDocumentResourceStore, IAsyncDisposable
{
    private const string DatabaseRelativePath = "metadata/dnd-compendium.db3";
    private readonly string _rootDirectory;
    private readonly SQLiteAsyncConnection _database;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _initializationSync = new();
    private Task? _initializationTask;

    public SqliteDocumentResourceStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _database = new SQLiteAsyncConnection(
            ResolvePath(DatabaseRelativePath),
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex | SQLiteOpenFlags.SharedCache,
            storeDateTimeAsTicks: true);
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task initialization;
        lock (_initializationSync)
        {
            initialization = _initializationTask ??= InitializeCoreAsync();
        }

        return initialization.WaitAsync(cancellationToken);
    }

    public async Task ReplaceReferencesAsync(
        string documentId,
        IReadOnlyCollection<ExternalResourceReference> references,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(references);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var normalized = references
            .GroupBy(reference => reference.SourceUrl, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToDictionary(reference => reference.SourceUrl, StringComparer.Ordinal);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await _database.Table<DocumentResourceEntity>()
                .Where(entity => entity.DocumentId == documentId)
                .ToListAsync().ConfigureAwait(false);
            foreach (var stale in existing.Where(entity => !normalized.ContainsKey(entity.SourceUrl)))
            {
                await _database.DeleteAsync(stale).ConfigureAwait(false);
                TryDelete(stale.RelativePath);
            }

            foreach (var reference in normalized.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entity = existing.FirstOrDefault(candidate => candidate.SourceUrl == reference.SourceUrl);
                if (entity is null)
                {
                    entity = new DocumentResourceEntity
                    {
                        Id = CreateId(documentId, reference.SourceUrl),
                        DocumentId = documentId,
                        SourceUrl = reference.SourceUrl,
                        State = (int)DocumentResourceState.Pending,
                        UpdatedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                }
                else if (entity.Kind != (int)reference.Kind || entity.IsRequired != reference.IsRequired)
                {
                    // A material reference change should be visible in the manifest, but merely
                    // scanning an unchanged document must not reset the retry age of a failed URL.
                    entity.UpdatedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }

                entity.Kind = (int)reference.Kind;
                entity.IsRequired = reference.IsRequired;
                await _database.InsertOrReplaceAsync(entity).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DocumentResourceManifest> GetManifestAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entities = await _database.Table<DocumentResourceEntity>()
                .Where(entity => entity.DocumentId == documentId)
                .ToListAsync().ConfigureAwait(false);
            var changed = false;
            foreach (var entity in entities.Where(entity => entity.State == (int)DocumentResourceState.Available))
            {
                if (string.IsNullOrWhiteSpace(entity.RelativePath) || !File.Exists(ResolvePath(entity.RelativePath)))
                {
                    entity.State = (int)DocumentResourceState.Unavailable;
                    entity.ErrorMessage = "The local resource file is missing.";
                    entity.RelativePath = null;
                    entity.UpdatedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    await _database.InsertOrReplaceAsync(entity).ConfigureAwait(false);
                    changed = true;
                }
            }
            _ = changed;
            return new DocumentResourceManifest
            {
                DocumentId = documentId,
                Resources = entities.OrderBy(entity => entity.SourceUrl, StringComparer.Ordinal).Select(ToModel).ToArray()
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateStateAsync(
        string documentId,
        string sourceUrl,
        DocumentResourceState state,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entity = await _database.FindAsync<DocumentResourceEntity>(CreateId(documentId, sourceUrl)).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Resource reference is not present in the manifest.");
            entity.State = (int)state;
            entity.ErrorMessage = errorMessage;
            entity.UpdatedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _database.InsertOrReplaceAsync(entity).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StoreAsync(
        string documentId,
        string sourceUrl,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        if (content.IsEmpty) throw new InvalidDataException("External resources cannot be empty.");
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var sha256 = Convert.ToHexString(SHA256.HashData(content.Span)).ToLowerInvariant();
        var extension = ExtensionFor(mimeType);
        var relativePath = Portable(Path.Combine("resources", ShortHash(documentId), $"{sha256}{extension}"));
        var finalPath = ResolvePath(relativePath);
        var stagingPath = ResolvePath(Portable(Path.Combine(".staging", $"resource-{Guid.NewGuid():N}.part")));
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);

        try
        {
            await using (var stream = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            if (!File.Exists(finalPath)) File.Move(stagingPath, finalPath);
            else File.Delete(stagingPath);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var entity = await _database.FindAsync<DocumentResourceEntity>(CreateId(documentId, sourceUrl)).ConfigureAwait(false)
                    ?? throw new KeyNotFoundException("Resource reference is not present in the manifest.");
                var oldPath = entity.RelativePath;
                entity.State = (int)DocumentResourceState.Available;
                entity.MimeType = mimeType;
                entity.Sha256 = sha256;
                entity.ByteLength = content.Length;
                entity.RelativePath = relativePath;
                entity.ErrorMessage = null;
                entity.UpdatedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await _database.InsertOrReplaceAsync(entity).ConfigureAwait(false);
                if (!string.Equals(oldPath, relativePath, StringComparison.Ordinal)) TryDelete(oldPath);
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            if (File.Exists(stagingPath)) File.Delete(stagingPath);
        }
    }

    public async Task<IReadOnlyList<LocalDocumentResource>> ReadAvailableAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        var manifest = await GetManifestAsync(documentId, cancellationToken).ConfigureAwait(false);
        var resources = new List<LocalDocumentResource>();
        foreach (var resource in manifest.Resources.Where(resource => resource.State == DocumentResourceState.Available))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (resource.RelativePath is null || resource.MimeType is null || resource.Sha256 is null) continue;
            var bytes = await File.ReadAllBytesAsync(ResolvePath(resource.RelativePath), cancellationToken).ConfigureAwait(false);
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(resource.Sha256), Encoding.ASCII.GetBytes(actualHash)))
            {
                await UpdateStateAsync(documentId, resource.SourceUrl, DocumentResourceState.Unavailable, "Local resource checksum mismatch.", cancellationToken).ConfigureAwait(false);
                continue;
            }
            resources.Add(new LocalDocumentResource(resource.SourceUrl, resource.MimeType, resource.Sha256, resource.RelativePath, bytes));
        }
        return resources;
    }

    public async Task DeleteResourcesAsync(string documentId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entities = await _database.Table<DocumentResourceEntity>()
                .Where(entity => entity.DocumentId == documentId).ToListAsync().ConfigureAwait(false);
            foreach (var entity in entities)
            {
                await _database.DeleteAsync(entity).ConfigureAwait(false);
                TryDelete(entity.RelativePath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _database.CloseAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task InitializeCoreAsync()
    {
        Directory.CreateDirectory(_rootDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(ResolvePath(DatabaseRelativePath))!);
        Directory.CreateDirectory(ResolvePath("resources"));
        Directory.CreateDirectory(ResolvePath(".staging"));
        await _database.CreateTableAsync<DocumentResourceEntity>().ConfigureAwait(false);
    }

    private static DocumentResource ToModel(DocumentResourceEntity entity) => new()
    {
        DocumentId = entity.DocumentId,
        SourceUrl = entity.SourceUrl,
        Kind = (DocumentResourceKind)entity.Kind,
        IsRequired = entity.IsRequired,
        State = (DocumentResourceState)entity.State,
        RelativePath = entity.RelativePath,
        MimeType = entity.MimeType,
        Sha256 = entity.Sha256,
        ByteLength = entity.ByteLength,
        ErrorMessage = entity.ErrorMessage,
        UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(entity.UpdatedAtUnixMilliseconds)
    };

    private static string CreateId(string documentId, string sourceUrl)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{documentId}\n{sourceUrl}"))).ToLowerInvariant();

    private static string ShortHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();

    private static string ExtensionFor(string mimeType) => mimeType.Split(';', 2)[0].Trim().ToLowerInvariant() switch
    {
        "image/png" => ".png", "image/jpeg" => ".jpg", "image/gif" => ".gif", "image/webp" => ".webp",
        "image/avif" => ".avif", "font/woff" => ".woff", "font/woff2" => ".woff2", "font/ttf" => ".ttf",
        "font/otf" => ".otf", "text/css" => ".css", _ => ".bin"
    };

    private string ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) throw new InvalidDataException("Resource paths must be relative.");
        var resolved = Path.GetFullPath(Path.Combine(_rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
        var prefix = Path.TrimEndingDirectorySeparator(_rootDirectory) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Resource path escaped application storage.");
        return resolved;
    }

    private void TryDelete(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return;
        try { var path = ResolvePath(relativePath); if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (InvalidDataException) { }
    }

    private static string Portable(string path) => path.Replace('\\', '/');
}
