namespace DndCompendium.Core.Models;

public sealed record SourceDocument
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string FileName { get; init; }
    public required DocumentCategory Category { get; init; }
    public required string Description { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTimeOffset ModifiedAt { get; init; }
    public required string Checksum { get; init; }

    /// <summary>
    /// Source revision token used to detect updates when the provider does not expose a
    /// cryptographic content hash. Empty keeps backward compatibility by using Checksum.
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// False when a remote metadata probe failed and Version only contains bundled fallback data.
    /// Existing stored metadata must not be replaced in that case.
    /// </summary>
    public bool IsVersionAuthoritative { get; init; } = true;

    public string EffectiveVersion => string.IsNullOrWhiteSpace(Version) ? Checksum : Version;
}
