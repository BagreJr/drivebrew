namespace DndCompendium.Core.Models;

public sealed record DocumentResourceManifest
{
    public required string DocumentId { get; init; }
    public required IReadOnlyList<DocumentResource> Resources { get; init; }

    public int RequiredCount => Resources.Count(resource => resource.IsRequired);

    public int AvailableCount => Resources.Count(resource =>
        resource.IsRequired && resource.State == DocumentResourceState.Available);

    public int MissingRequiredCount => Resources.Count(resource =>
        resource.IsRequired && resource.State == DocumentResourceState.Unavailable);

    public DocumentOfflineState OfflineState
    {
        get
        {
            if (Resources.Any(resource => resource.IsRequired
                && resource.State is DocumentResourceState.Pending or DocumentResourceState.Downloading))
            {
                return DocumentOfflineState.Pending;
            }

            return Resources.Any(resource => resource.IsRequired
                && resource.State != DocumentResourceState.Available)
                ? DocumentOfflineState.Incomplete
                : DocumentOfflineState.Complete;
        }
    }
}
