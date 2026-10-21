namespace DndCompendium.Core.Models;

public sealed record DocumentResource
{
    public required string DocumentId { get; init; }
    public required string SourceUrl { get; init; }
    public required DocumentResourceKind Kind { get; init; }
    public required bool IsRequired { get; init; }
    public required DocumentResourceState State { get; init; }
    public string? RelativePath { get; init; }
    public string? MimeType { get; init; }
    public string? Sha256 { get; init; }
    public long ByteLength { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
