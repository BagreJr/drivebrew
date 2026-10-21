namespace DndCompendium.Core.Models;

public sealed record RecentDocument
{
    public required string DocumentId { get; init; }
    public required string Title { get; init; }
    public required DateTimeOffset OpenedAt { get; init; }
}
