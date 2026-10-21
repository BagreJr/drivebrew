using System.Net;
using System.Net.Sockets;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using DndCompendium.Core.Services;
using DndCompendium.Infrastructure.Persistence;
using DndCompendium.Infrastructure.Sources;

namespace DndCompendium.Infrastructure.Tests;

public sealed class DocumentResourceTests
{
    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    [Fact]
    public void Extractor_FindsRenderResourcesButIgnoresNavigationLinks()
    {
        const string source = """
            ![Cover](https://assets.example.test/cover.png#preview)
            <img src="https://assets.example.test/portrait.jpg">
            <source src='https://assets.example.test/map.webp'>
            <link rel="stylesheet" href="https://assets.example.test/theme.css">
            @import url('https://assets.example.test/imported.css');
            .title { background: url(https://assets.example.test/cover.png); }
            @font-face { src: url("https://assets.example.test/body.woff2"); }
            [Ordinary link](https://assets.example.test/rules)
            [Internal](#chapter-two)
            """;

        var references = new HomebrewResourceExtractor().Extract(source);

        Assert.Equal(6, references.Count);
        Assert.Single(references, reference => reference.SourceUrl == "https://assets.example.test/cover.png");
        Assert.Contains(references, reference => reference.SourceUrl.EndsWith("body.woff2") && reference.Kind == DocumentResourceKind.Font);
        Assert.Equal(2, references.Count(reference => reference.Kind == DocumentResourceKind.Style));
        Assert.DoesNotContain(references, reference => reference.SourceUrl.EndsWith("/rules"));
    }

    [Fact]
    public async Task Store_PersistsAndVerifiesAvailableResourcesAcrossRestart()
    {
        using var directory = new TemporaryDirectory();
        const string documentId = "persistent-document";
        const string sourceUrl = "https://93.184.216.34/cover.png";

        await using (var first = new SqliteDocumentResourceStore(directory.Path))
        {
            await first.ReplaceReferencesAsync(documentId,
                [new ExternalResourceReference(sourceUrl, DocumentResourceKind.Image)]);
            await first.StoreAsync(documentId, sourceUrl, "image/png", PngBytes);
            Assert.Equal(DocumentOfflineState.Complete, (await first.GetManifestAsync(documentId)).OfflineState);
        }

        await using (var reopened = new SqliteDocumentResourceStore(directory.Path))
        {
            var resources = await reopened.ReadAvailableAsync(documentId);
            var resource = Assert.Single(resources);
            Assert.Equal(sourceUrl, resource.SourceUrl);
            Assert.Equal(PngBytes, resource.Content);
            await reopened.DeleteResourcesAsync(documentId);
            Assert.Empty((await reopened.GetManifestAsync(documentId)).Resources);
        }
    }

    [Fact]
    public async Task Manager_KeepsSuccessfulResourcesAndReportsIndividualFailures()
    {
        using var directory = new TemporaryDirectory();
        await using var store = new SqliteDocumentResourceStore(directory.Path);
        var attemptedDownloads = 0;
        var downloader = new StubDownloader(reference =>
        {
            attemptedDownloads++;
            return reference.SourceUrl switch
            {
                var url when url.Contains("missing", StringComparison.Ordinal) =>
                    throw new HttpRequestException("simulated HTTP failure"),
                var url when url.Contains("dns-error", StringComparison.Ordinal) =>
                    throw new SocketException((int)SocketError.HostNotFound),
                _ => new ExternalResourceDownload("image/png", PngBytes, new Uri(reference.SourceUrl))
            };
        });
        var manager = new DocumentResourceManager(new HomebrewResourceExtractor(), downloader, store);

        var manifest = await manager.SynchronizeAsync("mixed", """
            ![Available](https://93.184.216.34/available.png)
            ![Missing](https://93.184.216.34/missing.png)
            ![DNS error](https://93.184.216.34/dns-error.png)
            """);

        Assert.Equal(DocumentOfflineState.Incomplete, manifest.OfflineState);
        Assert.Equal(1, manifest.AvailableCount);
        Assert.Equal(2, manifest.MissingRequiredCount);
        Assert.Single(await manager.ReadAvailableAsync("mixed"));

        var secondManifest = await manager.SynchronizeAsync("mixed", """
            ![Available](https://93.184.216.34/available.png)
            ![Missing](https://93.184.216.34/missing.png)
            ![DNS error](https://93.184.216.34/dns-error.png)
            """);

        Assert.Equal(manifest.AvailableCount, secondManifest.AvailableCount);
        Assert.Equal(manifest.MissingRequiredCount, secondManifest.MissingRequiredCount);
        Assert.Equal(3, attemptedDownloads);
    }

    [Fact]
    public async Task Downloader_ValidatesContentAndRejectsPrivateRedirects()
    {
        using var validClient = new HttpClient(new DelegateHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(PngBytes)
            };
            response.Content.Headers.ContentType = new("application/octet-stream");
            return response;
        }));
        var downloader = new SecureExternalResourceDownloader(validClient);
        var downloaded = await downloader.DownloadAsync(
            new ExternalResourceReference("https://93.184.216.34/image.png", DocumentResourceKind.Image));
        Assert.Equal("image/png", downloaded.MimeType);

        using var redirectClient = new HttpClient(new DelegateHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("http://127.0.0.1/private.png");
            return response;
        }));
        var guardedDownloader = new SecureExternalResourceDownloader(redirectClient);
        await Assert.ThrowsAsync<InvalidDataException>(() => guardedDownloader.DownloadAsync(
            new ExternalResourceReference("https://93.184.216.34/redirect.png", DocumentResourceKind.Image)));
    }

    private sealed class StubDownloader(
        Func<ExternalResourceReference, ExternalResourceDownload> download) : IExternalResourceDownloader
    {
        public Task<ExternalResourceDownload> DownloadAsync(
            ExternalResourceReference reference,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(download(reference));
        }
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(handler(request));
        }
    }
}
