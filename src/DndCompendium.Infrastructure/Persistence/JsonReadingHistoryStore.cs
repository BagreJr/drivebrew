using System.Text.Json;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.Infrastructure.Persistence;

public sealed class JsonReadingHistoryStore : IReadingHistoryStore, IDisposable
{
    private const int MaximumEntries = 3;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<RecentDocument>? _entries;
    private bool _disposed;

    public JsonReadingHistoryStore(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _path = Path.Combine(Path.GetFullPath(rootPath), "history", "recent-documents.json");
    }

    public async Task<IReadOnlyList<RecentDocument>> GetRecentAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            return entries.OrderByDescending(entry => entry.OpenedAt).Take(MaximumEntries).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordOpenedAsync(
        string documentId,
        string title,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            entries.RemoveAll(entry => string.Equals(entry.DocumentId, documentId, StringComparison.Ordinal));
            entries.Add(new RecentDocument
            {
                DocumentId = documentId.Trim(),
                Title = title.Trim(),
                OpenedAt = DateTimeOffset.UtcNow
            });
            entries.Sort((left, right) => right.OpenedAt.CompareTo(left.OpenedAt));
            if (entries.Count > MaximumEntries)
            {
                entries.RemoveRange(MaximumEntries, entries.Count - MaximumEntries);
            }
            await SaveAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<RecentDocument>> EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_entries is not null) return _entries;
        if (!File.Exists(_path)) return _entries = [];
        try
        {
            await using var input = File.OpenRead(_path);
            var stored = await JsonSerializer.DeserializeAsync<List<RecentDocument>>(
                input, JsonOptions, cancellationToken).ConfigureAwait(false);
            return _entries = (stored ?? [])
                .Where(entry => !string.IsNullOrWhiteSpace(entry.DocumentId)
                    && !string.IsNullOrWhiteSpace(entry.Title))
                .OrderByDescending(entry => entry.OpenedAt)
                .Take(MaximumEntries)
                .ToList();
        }
        catch (JsonException)
        {
            return _entries = [];
        }
        catch (IOException)
        {
            return _entries = [];
        }
    }

    private async Task SaveAsync(List<RecentDocument> entries, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(output, entries, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
    }
}
