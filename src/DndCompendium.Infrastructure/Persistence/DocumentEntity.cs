using SQLite;

namespace DndCompendium.Infrastructure.Persistence;

[Table("library_documents")]
internal sealed class DocumentEntity
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [Indexed]
    public string Title { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    [Indexed]
    public int Category { get; set; }

    public string Description { get; set; } = string.Empty;

    public long SourceSizeBytes { get; set; }

    public long SourceModifiedAtUnixMilliseconds { get; set; }

    public string SourceChecksum { get; set; } = string.Empty;

    public string SourceVersion { get; set; } = string.Empty;

    public string? InstalledSourceVersion { get; set; }

    [Indexed]
    public int DownloadState { get; set; }

    public double Progress { get; set; }

    public long LocalSizeBytes { get; set; }

    public long? DownloadedAtUnixMilliseconds { get; set; }

    public string? RelativePath { get; set; }

    public string? LocalSha256 { get; set; }

    public string? SearchContent { get; set; }

    public string? ErrorMessage { get; set; }

    public long StateUpdatedAtUnixMilliseconds { get; set; }
}
