namespace DndCompendium.Core.Models;

public sealed record ExternalResourceReference(
    string SourceUrl,
    DocumentResourceKind Kind,
    bool IsRequired = true);
