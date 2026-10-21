namespace DndCompendium.Core.Models;

public sealed record DownloadJob
{
    public required string DocumentId { get; init; }
    public required string Title { get; init; }
    public required DocumentDownloadState State { get; init; }
    public double Progress { get; init; }
    public long BytesTransferred { get; init; }
    public long TotalBytes { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
