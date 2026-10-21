using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using SQLite;

namespace DndCompendium.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed metadata and private, durable document storage. Every path persisted in SQLite
/// is relative to <see cref="RootDirectory"/>, allowing a mobile OS to relocate its sandbox.
/// </summary>
public sealed class LocalDocumentRepository : ILocalDocumentRepository, IAsyncDisposable
{
    private const string DatabaseRelativePath = "metadata/dnd-compendium.db3";
    private const int CopyBufferSize = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly char[] AdditionalInvalidFileNameCharacters = ['/', '\\', ':'];
    private readonly SQLiteAsyncConnection _database;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _initializationSync = new();
    private Task? _initializationTask;
    private bool _disposed;

    public LocalDocumentRepository(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);

        DatabasePath = ResolvePath(DatabaseRelativePath);
        _database = new SQLiteAsyncConnection(
            DatabasePath,
            SQLiteOpenFlags.ReadWrite
            | SQLiteOpenFlags.Create
            | SQLiteOpenFlags.FullMutex
            | SQLiteOpenFlags.SharedCache,
            storeDateTimeAsTicks: true);
    }

    public string RootDirectory { get; }

    public string DatabasePath { get; }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        Task initialization;
        lock (_initializationSync)
        {
            initialization = _initializationTask ??= InitializeCoreAsync();
        }

        return initialization.WaitAsync(cancellationToken);
    }

    public async Task UpsertCatalogAsync(
        IEnumerable<SourceDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var catalog = documents.ToArray();
        ValidateCatalog(catalog);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var document in catalog)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entity = await _database.FindAsync<DocumentEntity>(document.Id).ConfigureAwait(false);
                if (entity is null)
                {
                    entity = CreateEntity(document);
                }
                else
                {
                    UpdateSourceMetadata(entity, document);
                    ReconcileLocalCopy(entity);
                }

                await _database.InsertOrReplaceAsync(entity).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<LibraryDocument>> GetDocumentsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var entities = await _database.Table<DocumentEntity>()
            .OrderBy(entity => entity.Title)
            .ToListAsync()
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        return entities.Select(ToLibraryDocument).ToArray();
    }

    public async Task<LibraryDocument?> GetDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var entity = await _database.FindAsync<DocumentEntity>(documentId).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return entity is null ? null : ToLibraryDocument(entity);
    }

    public async Task<IReadOnlyList<LibraryDocument>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var needle = query.Trim();
        var entities = await _database.Table<DocumentEntity>().ToListAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return entities
            .Where(entity => entity.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (entity.SearchContent?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(entity => entity.Title, StringComparer.OrdinalIgnoreCase)
            .Select(ToLibraryDocument)
            .ToArray();
    }

    public async Task InstallAsync(
        SourceDocument document,
        Stream content,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(content);
        ValidateSourceDocument(document);
        if (!content.CanRead)
        {
            throw new ArgumentException("The document stream must be readable.", nameof(content));
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var stagingRelativePath = $".staging/{Guid.NewGuid():N}.part";
        var stagingPath = ResolvePath(stagingRelativePath);
        string? installedPath = null;
        string? installedRelativePath = null;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stagingPath)!);
            var (actualSha256, localSize) = await CopyToStagingAsync(
                content,
                stagingPath,
                document.SizeBytes,
                progress,
                cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(document.Checksum))
            {
                ValidateChecksum(document.Checksum, actualSha256);
            }

            var bytes = await File.ReadAllBytesAsync(stagingPath, cancellationToken).ConfigureAwait(false);
            var sourceText = DecodeAndValidateText(bytes);
            cancellationToken.ThrowIfCancellationRequested();

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var entity = await _database.FindAsync<DocumentEntity>(document.Id).ConfigureAwait(false)
                    ?? CreateEntity(document);
                var previousRelativePath = entity.RelativePath;

                installedRelativePath = CreateInstalledRelativePath(document, actualSha256);
                installedPath = ResolvePath(installedRelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(installedPath)!);
                File.Move(stagingPath, installedPath);

                UpdateSourceMetadata(entity, document);
                entity.DownloadState = (int)DocumentDownloadState.Downloaded;
                entity.Progress = 1;
                entity.LocalSizeBytes = localSize;
                entity.DownloadedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                entity.RelativePath = ToPortableRelativePath(installedRelativePath);
                entity.LocalSha256 = actualSha256;
                entity.InstalledSourceVersion = NormalizeVersion(document.EffectiveVersion);
                entity.SearchContent = sourceText;
                entity.ErrorMessage = null;
                entity.StateUpdatedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                try
                {
                    await _database.RunInTransactionAsync(connection => connection.InsertOrReplace(entity))
                        .ConfigureAwait(false);
                }
                catch
                {
                    TryDeleteFile(installedPath);
                    installedPath = null;
                    throw;
                }

                if (!string.IsNullOrWhiteSpace(previousRelativePath)
                    && !PathsEqual(previousRelativePath, entity.RelativePath))
                {
                    TryDeleteStoredFile(previousRelativePath);
                }
            }
            finally
            {
                _gate.Release();
            }

            progress?.Report(1);
        }
        finally
        {
            TryDeleteFile(stagingPath);
        }
    }

    public async Task<LocalDocumentContent?> ReadAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var entity = await _database.FindAsync<DocumentEntity>(documentId).ConfigureAwait(false);
        if (entity is null || string.IsNullOrWhiteSpace(entity.RelativePath))
        {
            return null;
        }

        var fullPath = ResolvePath(entity.RelativePath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var sourceText = await File.ReadAllTextAsync(fullPath, StrictUtf8, cancellationToken)
            .ConfigureAwait(false);
        return new LocalDocumentContent(
            entity.Id,
            entity.Title,
            sourceText,
            ToPortableRelativePath(entity.RelativePath),
            entity.LocalSha256 ?? ComputeSha256(StrictUtf8.GetBytes(sourceText)));
    }

    public async Task DeleteLocalCopyAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        string? relativePath = null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entity = await _database.FindAsync<DocumentEntity>(documentId).ConfigureAwait(false);
            if (entity is null)
            {
                return;
            }

            relativePath = entity.RelativePath;
            entity.DownloadState = (int)DocumentDownloadState.NotDownloaded;
            entity.Progress = 0;
            entity.LocalSizeBytes = 0;
            entity.DownloadedAtUnixMilliseconds = null;
            entity.RelativePath = null;
            entity.LocalSha256 = null;
            entity.InstalledSourceVersion = null;
            entity.SearchContent = null;
            entity.ErrorMessage = null;
            entity.StateUpdatedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _database.InsertOrReplaceAsync(entity).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            TryDeleteStoredFile(relativePath);
        }
    }

    public async Task UpdateDownloadStateAsync(
        string documentId,
        DocumentDownloadState state,
        double progress = 0,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entity = await _database.FindAsync<DocumentEntity>(documentId).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"Document '{documentId}' is not present in the catalog.");
            entity.DownloadState = (int)state;
            entity.Progress = Math.Clamp(progress, 0, 1);
            entity.ErrorMessage = errorMessage;
            entity.StateUpdatedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _database.InsertOrReplaceAsync(entity).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<DownloadJob>> GetDownloadJobsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var entities = await _database.Table<DocumentEntity>().ToListAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return entities
            .Where(entity => entity.DownloadState != (int)DocumentDownloadState.NotDownloaded)
            .OrderByDescending(entity => entity.StateUpdatedAtUnixMilliseconds)
            .Select(entity => new DownloadJob
            {
                DocumentId = entity.Id,
                Title = entity.Title,
                State = (DocumentDownloadState)entity.DownloadState,
                Progress = entity.Progress,
                BytesTransferred = entity.DownloadState == (int)DocumentDownloadState.Downloaded
                    ? entity.LocalSizeBytes
                    : (long)Math.Round(entity.SourceSizeBytes * entity.Progress),
                TotalBytes = entity.SourceSizeBytes,
                ErrorMessage = entity.ErrorMessage,
                UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(entity.StateUpdatedAtUnixMilliseconds)
            })
            .ToArray();
    }

    public async Task<long> GetStorageUsageAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        var entities = await _database.Table<DocumentEntity>().ToListAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return entities.Sum(entity => entity.LocalSizeBytes);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _database.CloseAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task InitializeCoreAsync()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        Directory.CreateDirectory(ResolvePath("documents"));
        Directory.CreateDirectory(ResolvePath(".staging"));

        await _database.CreateTableAsync<DocumentEntity>().ConfigureAwait(false);
        // journal_mode returns the selected mode as a result row. Calling ExecuteAsync
        // (ExecuteNonQuery) makes some SQLite builds surface the misleading
        // "not an error" exception even though WAL was enabled successfully.
        _ = await _database.ExecuteScalarAsync<string>("PRAGMA journal_mode=WAL;")
            .ConfigureAwait(false);
        await _database.ExecuteAsync("PRAGMA foreign_keys=ON;").ConfigureAwait(false);
    }

    private async Task<(string Sha256, long Size)> CopyToStagingAsync(
        Stream source,
        string stagingPath,
        long expectedSize,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var destination = new FileStream(
                stagingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var totalSize = expectedSize > 0
                ? expectedSize
                : source.CanSeek ? Math.Max(0, source.Length - source.Position) : 0;
            long copied = 0;
            progress?.Report(0);

            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                copied += read;

                if (totalSize > 0)
                {
                    progress?.Report(Math.Min(0.99, (double)copied / totalSize));
                }
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
            return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), copied);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string DecodeAndValidateText(byte[] bytes)
    {
        try
        {
            var text = StrictUtf8.GetString(bytes);
            if (text.IndexOf('\0') >= 0)
            {
                throw new InvalidDataException("The downloaded document contains binary data.");
            }

            return text;
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The downloaded document is not valid UTF-8 text.", exception);
        }
    }

    private void ReconcileLocalCopy(DocumentEntity entity)
    {
        if (string.IsNullOrWhiteSpace(entity.RelativePath))
        {
            return;
        }

        bool exists;
        try
        {
            exists = File.Exists(ResolvePath(entity.RelativePath));
        }
        catch (InvalidDataException)
        {
            exists = false;
        }

        if (!exists)
        {
            entity.RelativePath = null;
            entity.LocalSha256 = null;
            entity.LocalSizeBytes = 0;
            entity.DownloadedAtUnixMilliseconds = null;
            entity.SearchContent = null;
            entity.DownloadState = (int)DocumentDownloadState.Error;
            entity.Progress = 0;
            entity.ErrorMessage = "The local document file is missing.";
            return;
        }

        var isCurrent = !string.IsNullOrWhiteSpace(entity.InstalledSourceVersion)
            ? string.Equals(
                NormalizeVersion(entity.InstalledSourceVersion),
                NormalizeVersion(entity.SourceVersion),
                StringComparison.Ordinal)
            : !string.IsNullOrWhiteSpace(entity.LocalSha256)
                && !string.IsNullOrWhiteSpace(entity.SourceChecksum)
                && string.Equals(
                    entity.LocalSha256,
                    NormalizeChecksum(entity.SourceChecksum),
                    StringComparison.OrdinalIgnoreCase);
        entity.DownloadState = (int)(isCurrent
            ? DocumentDownloadState.Downloaded
            : DocumentDownloadState.UpdateAvailable);
        entity.Progress = isCurrent ? 1 : 0;
        entity.ErrorMessage = null;
    }

    private static void UpdateSourceMetadata(DocumentEntity entity, SourceDocument source)
    {
        entity.Title = source.Title;
        entity.FileName = source.FileName;
        entity.Category = (int)source.Category;
        entity.Description = source.Description;
        if (source.IsVersionAuthoritative)
        {
            entity.SourceSizeBytes = source.SizeBytes;
            entity.SourceModifiedAtUnixMilliseconds = source.ModifiedAt.ToUnixTimeMilliseconds();
            entity.SourceChecksum = NormalizeChecksum(source.Checksum);
            entity.SourceVersion = NormalizeVersion(source.EffectiveVersion);
        }
    }

    private static DocumentEntity CreateEntity(SourceDocument source)
    {
        ValidateSourceDocument(source);
        return new DocumentEntity
        {
            Id = source.Id,
            Title = source.Title,
            FileName = source.FileName,
            Category = (int)source.Category,
            Description = source.Description,
            SourceSizeBytes = source.SizeBytes,
            SourceModifiedAtUnixMilliseconds = source.ModifiedAt.ToUnixTimeMilliseconds(),
            SourceChecksum = NormalizeChecksum(source.Checksum),
            SourceVersion = NormalizeVersion(source.EffectiveVersion),
            DownloadState = (int)DocumentDownloadState.NotDownloaded,
            StateUpdatedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static LibraryDocument ToLibraryDocument(DocumentEntity entity)
        => new()
        {
            Id = entity.Id,
            Title = entity.Title,
            FileName = entity.FileName,
            Category = (DocumentCategory)entity.Category,
            Description = entity.Description,
            SourceSizeBytes = entity.SourceSizeBytes,
            SourceModifiedAt = DateTimeOffset.FromUnixTimeMilliseconds(entity.SourceModifiedAtUnixMilliseconds),
            SourceChecksum = entity.SourceChecksum,
            SourceVersion = entity.SourceVersion,
            DownloadState = (DocumentDownloadState)entity.DownloadState,
            Progress = entity.Progress,
            LocalSizeBytes = entity.LocalSizeBytes,
            DownloadedAt = entity.DownloadedAtUnixMilliseconds.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(entity.DownloadedAtUnixMilliseconds.Value)
                : null,
            RelativePath = entity.RelativePath,
            ErrorMessage = entity.ErrorMessage
        };

    private string CreateInstalledRelativePath(SourceDocument document, string sha256)
    {
        var idSegment = $"{SanitizeFileName(document.Id, "document")}-{ShortHash(document.Id)}";
        var fileName = SanitizeFileName(document.FileName, "document.txt");
        return ToPortableRelativePath(
            Path.Combine("documents", idSegment, $"{sha256[..16]}-{Guid.NewGuid():N}-{fileName}"));
    }

    private static string SanitizeFileName(string value, string fallback)
    {
        var invalid = Path.GetInvalidFileNameChars()
            .Concat(AdditionalInvalidFileNameCharacters)
            .ToHashSet();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) || char.IsControl(character) ? '_' : character);
        }

        var result = builder.ToString().Trim().Trim('.');
        while (result.Contains("..", StringComparison.Ordinal))
        {
            result = result.Replace("..", "_", StringComparison.Ordinal);
        }
        if (string.IsNullOrWhiteSpace(result))
        {
            result = fallback;
        }

        return result.Length <= 80 ? result : result[..80];
    }

    private static string ShortHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12].ToLowerInvariant();

    private string ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Stored document paths must be non-empty and relative.");
        }

        var platformRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(RootDirectory, platformRelativePath));
        var rootPrefix = Path.TrimEndingDirectorySeparator(RootDirectory) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!resolved.StartsWith(rootPrefix, comparison))
        {
            throw new InvalidDataException("A stored document path attempted to escape the application data directory.");
        }

        return resolved;
    }

    private static string ToPortableRelativePath(string path)
        => path.Replace('\\', '/');

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            ToPortableRelativePath(left),
            ToPortableRelativePath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private void TryDeleteStoredFile(string relativePath)
    {
        try
        {
            TryDeleteFile(ResolvePath(relativePath));
        }
        catch (InvalidDataException)
        {
            // Never follow a corrupted or hostile relative path outside the injected root.
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // The database is already authoritative. A harmless orphan can be reclaimed later.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve database consistency even if a platform temporarily locks the old file.
        }
    }

    private static void ValidateCatalog(IReadOnlyCollection<SourceDocument> documents)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            ValidateSourceDocument(document);
            if (!ids.Add(document.Id))
            {
                throw new ArgumentException($"The source catalog contains duplicate id '{document.Id}'.", nameof(documents));
            }
        }
    }

    private static void ValidateSourceDocument(SourceDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.FileName);
        if (document.SizeBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(document), "Document size cannot be negative.");
        }
    }

    private static void ValidateChecksum(string expected, string actual)
    {
        var normalized = NormalizeChecksum(expected);
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("The source checksum is not a valid SHA-256 value.");
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(normalized),
                Encoding.ASCII.GetBytes(actual)))
        {
            throw new InvalidDataException("SHA-256 validation failed for the downloaded document.");
        }
    }

    private static string NormalizeChecksum(string checksum)
    {
        var normalized = checksum?.Trim() ?? string.Empty;
        const string prefix = "sha256:";
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[prefix.Length..];
        }

        return normalized.ToLowerInvariant();
    }

    private static string NormalizeVersion(string version)
        => version?.Trim() ?? string.Empty;

    private static string ComputeSha256(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
