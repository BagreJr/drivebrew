using System.Reflection;
using System.Security.Cryptography;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.Infrastructure.Sources;

/// <summary>
/// Local Phase 2 catalog built from the three byte-for-byte Homebrewery exports supplied by the
/// project owner. Each call to <see cref="OpenReadAsync"/> returns a fresh stream and never contacts Drive.
/// </summary>
public sealed class EmbeddedDocumentSource : IDocumentSource
{
    private readonly IReadOnlyDictionary<string, Fixture> _fixtures;

    public EmbeddedDocumentSource()
    {
        var definitions = new[]
        {
            new FixtureDefinition(
                "magus",
                "Magus",
                "Magus.txt",
                DocumentCategory.Classes,
                "Documento Homebrewery V3 real, disponible para lectura offline.",
                "Magus.txt",
                new DateTimeOffset(2026, 10, 8, 15, 42, 54, TimeSpan.Zero)),
            new FixtureDefinition(
                "psion",
                "Psion",
                "Psion.txt",
                DocumentCategory.Classes,
                "Documento Homebrewery V3 real, disponible para lectura offline.",
                "Psion.txt",
                new DateTimeOffset(2026, 10, 12, 23, 39, 31, TimeSpan.Zero)),
            new FixtureDefinition(
                "backgrounds-feats-equipment",
                "Backgrounds, Feats & Equipment",
                "Backgrounds, Feats & Equipment.txt",
                DocumentCategory.Equipment,
                "Documento Homebrewery V3 real con trasfondos, dotes y equipamiento.",
                "Backgrounds, Feats & Equipment.txt",
                new DateTimeOffset(2026, 8, 5, 22, 59, 51, TimeSpan.Zero))
        };

        _fixtures = definitions
            .Select(CreateFixture)
            .ToDictionary(fixture => fixture.Document.Id, StringComparer.Ordinal);
    }

    public Task<IReadOnlyList<SourceDocument>> GetCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<SourceDocument> result = _fixtures.Values
            .Select(fixture => fixture.Document)
            .OrderBy(document => document.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<Stream> OpenReadAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_fixtures.TryGetValue(documentId, out var fixture))
        {
            throw new KeyNotFoundException($"Embedded fixture '{documentId}' does not exist.");
        }

        Stream stream = new MemoryStream(fixture.Content, writable: false);
        return Task.FromResult(stream);
    }

    private static Fixture CreateFixture(FixtureDefinition definition)
    {
        var assembly = typeof(EmbeddedDocumentSource).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith($".RealDocuments.{definition.ResourceFileName}", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded fixture '{definition.ResourceFileName}' is missing.");
        using var resource = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded fixture stream '{resourceName}' is missing.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        return new Fixture(
            new SourceDocument
            {
                Id = definition.Id,
                Title = definition.Title,
                FileName = definition.FileName,
                Category = definition.Category,
                Description = definition.Description,
                SizeBytes = bytes.LongLength,
                ModifiedAt = definition.ModifiedAt,
                Checksum = checksum
            },
            bytes);
    }

    private sealed record Fixture(SourceDocument Document, byte[] Content);

    private sealed record FixtureDefinition(
        string Id,
        string Title,
        string FileName,
        DocumentCategory Category,
        string Description,
        string ResourceFileName,
        DateTimeOffset ModifiedAt);
}
