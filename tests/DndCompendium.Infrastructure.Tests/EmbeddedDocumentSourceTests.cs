using System.Text;
using DndCompendium.Infrastructure.Sources;

namespace DndCompendium.Infrastructure.Tests;

public sealed class EmbeddedDocumentSourceTests
{
    [Fact]
    public async Task Catalog_ContainsThreeRealHomebreweryExportsWithVerifiedHashes()
    {
        var source = new EmbeddedDocumentSource();

        var catalog = await source.GetCatalogAsync();

        Assert.Equal(3, catalog.Count);
        Assert.Contains(catalog, document => document.Id == "magus" && document.Checksum == "9292467d8b80ee4639d72ce374dbf499ae4e239cd22485123f6775b8ed5e8d05");
        Assert.Contains(catalog, document => document.Id == "psion" && document.Checksum == "7fc6a2b6929861ddb6f0a68847e748506bb42d7ac515f8d7c9e3da640761deb0");
        Assert.Contains(catalog, document => document.Id == "backgrounds-feats-equipment" && document.Checksum == "1aaafcf500c7ea8c4abc215217be44cff718e4bbd1b536fef2d0f1823c079d5f");
        Assert.All(catalog, document => Assert.Equal(64, document.Checksum.Length));
    }

    [Fact]
    public async Task PsionExport_ContainsMetadataCssPagesAndRealImageReferences()
    {
        var source = new EmbeddedDocumentSource();
        await using var stream = await source.OpenReadAsync("psion");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var text = await reader.ReadToEndAsync();

        Assert.StartsWith("```metadata", text);
        Assert.Contains("```css", text);
        Assert.Contains("### Utopian Mind", text);
        Assert.Contains("\\page", text);
        Assert.Contains("https://i.imgur.com/6gQDRuK.png", text);
    }
}
