namespace DndCompendium.Core.Models;

public sealed record LibraryDocument
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string FileName { get; init; }
    public required DocumentCategory Category { get; init; }
    public required string Description { get; init; }
    public required long SourceSizeBytes { get; init; }
    public required DateTimeOffset SourceModifiedAt { get; init; }
    public required string SourceChecksum { get; init; }
    public string SourceVersion { get; init; } = string.Empty;
    public DocumentDownloadState DownloadState { get; init; }
    public double Progress { get; init; }
    public long LocalSizeBytes { get; init; }
    public DateTimeOffset? DownloadedAt { get; init; }
    public string? RelativePath { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Indicates that a validated local copy exists. A failed or paused update does not make
    /// the previously installed copy unreadable, so this is deliberately independent from the
    /// state of the most recent download attempt.
    /// </summary>
    public bool IsDownloaded => DownloadedAt.HasValue
        && !string.IsNullOrWhiteSpace(RelativePath);

    public bool HasLocalCopy => IsDownloaded;
}
