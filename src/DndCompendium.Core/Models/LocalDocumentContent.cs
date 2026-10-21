namespace DndCompendium.Core.Models;

public sealed record LocalDocumentContent(
    string DocumentId,
    string Title,
    string SourceText,
    string RelativePath,
    string Sha256);
