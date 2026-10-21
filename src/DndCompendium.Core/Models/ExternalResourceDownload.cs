namespace DndCompendium.Core.Models;

public sealed record ExternalResourceDownload(
    string MimeType,
    byte[] Content,
    Uri FinalUri);
