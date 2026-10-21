using SQLite;

namespace DndCompendium.Infrastructure.Persistence;

[Table("document_resources")]
internal sealed class DocumentResourceEntity
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    [Indexed]
    public string DocumentId { get; set; } = string.Empty;

    public string SourceUrl { get; set; } = string.Empty;

    public int Kind { get; set; }

    public bool IsRequired { get; set; }

    [Indexed]
    public int State { get; set; }

    public string? RelativePath { get; set; }

    public string? MimeType { get; set; }

    public string? Sha256 { get; set; }

    public long ByteLength { get; set; }

    public string? ErrorMessage { get; set; }

    public long UpdatedAtUnixMilliseconds { get; set; }
}
