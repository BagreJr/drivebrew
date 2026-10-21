namespace DndCompendium.Core.Models;

/// <summary>
/// A validated local resource ready to be transferred to the renderer. The presentation layer can
/// encode <see cref="Content"/> as base64 and create a Blob URL without granting the WebView network access.
/// </summary>
public sealed record LocalDocumentResource(
    string SourceUrl,
    string MimeType,
    string Sha256,
    string RelativePath,
    byte[] Content);
