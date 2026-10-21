using System.Net;
using System.Text;
using DndCompendium.Core.Models;
using DndCompendium.Infrastructure.Sources;

namespace DndCompendium.Infrastructure.Tests;

public sealed class GoogleDriveDocumentSourceTests
{
    [Fact]
    public async Task Catalog_ContainsAllDriveTextDocuments()
    {
        using var client = new HttpClient(new DriveHandler()) { Timeout = TimeSpan.FromSeconds(5) };
        var source = new GoogleDriveDocumentSource(client, new EmbeddedDocumentSource());

        var catalog = await source.GetCatalogAsync();

        Assert.Equal(42, catalog.Count);
        Assert.Equal(35, catalog.Count(document => document.Category == DocumentCategory.Classes));
        Assert.Contains(catalog, document => document.Id == "alchemist");
        Assert.Contains(catalog, document => document.Id == "wizard");
        Assert.Contains(catalog, document => document.Id == "extra-subclasses");
        Assert.Contains(catalog, document => document.Id == "races");
        Assert.All(catalog, document =>
        {
            Assert.Empty(document.Checksum);
            Assert.StartsWith("drive:", document.Version);
            Assert.Equal(321, document.SizeBytes);
            Assert.Equal(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero), document.ModifiedAt);
        });
    }

    [Fact]
    public async Task OpenRead_DownloadsCurrentDriveText()
    {
        using var client = new HttpClient(new DriveHandler()) { Timeout = TimeSpan.FromSeconds(5) };
        var source = new GoogleDriveDocumentSource(client, new EmbeddedDocumentSource());

        await using var stream = await source.OpenReadAsync("wizard");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        Assert.Equal("# Current Drive document", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Catalog_UsesBaselineWhenMetadataTransportFails()
    {
        using var client = new HttpClient(new FailingMetadataHandler()) { Timeout = TimeSpan.FromSeconds(5) };
        var source = new GoogleDriveDocumentSource(client, new EmbeddedDocumentSource());

        var catalog = await source.GetCatalogAsync();

        Assert.Equal(42, catalog.Count);
        Assert.All(catalog, document => Assert.False(document.IsVersionAuthoritative));
    }

    private sealed class DriveHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = request.Method == HttpMethod.Head
                ? new ByteArrayContent([])
                : new ByteArrayContent(Encoding.UTF8.GetBytes("# Current Drive document"));
            content.Headers.ContentLength = request.Method == HttpMethod.Head ? 321 : content.Headers.ContentLength;
            content.Headers.LastModified = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class FailingMetadataHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("Socket closed");
    }
}
