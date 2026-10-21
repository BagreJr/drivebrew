using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DndCompendium.Core.Models;
using DndCompendium.Infrastructure.Updates;

namespace DndCompendium.Infrastructure.Tests;

public sealed class HttpAppUpdateServiceTests
{
    [Fact]
    public async Task Check_DetectsOnlyHigherStableVersion()
    {
        using var directory = new TemporaryDirectory();
        var manifest = CreateManifest(4, false);
        using var service = CreateService(directory.Path, manifest, Encoding.UTF8.GetBytes("apk"));

        var newer = await service.CheckAsync(Request(3));
        var same = await service.CheckAsync(Request(4));

        Assert.True(newer.IsUpdateAvailable);
        Assert.False(same.IsUpdateAvailable);
    }

    [Fact]
    public async Task Check_DoesNotOfferOlderVersion()
    {
        using var directory = new TemporaryDirectory();
        using var service = CreateService(directory.Path, CreateManifest(3, false), Encoding.UTF8.GetBytes("apk"));

        var result = await service.CheckAsync(Request(4));

        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task Check_IgnoresPrereleaseForStableChannel()
    {
        using var directory = new TemporaryDirectory();
        using var service = CreateService(directory.Path, CreateManifest(5, true), Encoding.UTF8.GetBytes("apk"));

        var result = await service.CheckAsync(Request(3));

        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task Download_VerifiesSha256AndReportsProgress()
    {
        using var directory = new TemporaryDirectory();
        var apk = Encoding.UTF8.GetBytes(new string('x', 200_000));
        var manifest = CreateManifest(4, false, apk);
        using var service = CreateService(directory.Path, manifest, apk);
        var progress = new List<AppUpdateDownloadProgress>();

        var path = await service.DownloadAsync(manifest, new InlineProgress<AppUpdateDownloadProgress>(progress.Add));

        Assert.True(File.Exists(path));
        Assert.Equal(apk, await File.ReadAllBytesAsync(path));
        Assert.NotEmpty(progress);
        Assert.Equal(1, progress[^1].Fraction);
    }

    [Fact]
    public async Task Download_RejectsCorruptedApkAndLeavesNoPartialFile()
    {
        using var directory = new TemporaryDirectory();
        var expected = Encoding.UTF8.GetBytes("expected");
        var manifest = CreateManifest(4, false, expected);
        using var service = CreateService(directory.Path, manifest, Encoding.UTF8.GetBytes("corrupt"));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(manifest));

        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Download_HonorsCancellationWithoutLeavingPartialFile()
    {
        using var directory = new TemporaryDirectory();
        var apk = Encoding.UTF8.GetBytes("apk");
        var manifest = CreateManifest(4, false, apk);
        using var service = CreateService(directory.Path, manifest, apk);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(manifest, cancellationToken: cancellation.Token));

        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Check_ServerFailureDoesNotCreateLocalUpdateState()
    {
        using var directory = new TemporaryDirectory();
        var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var service = new HttpAppUpdateService(directory.Path, new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => service.CheckAsync(Request(3)));

        Assert.False(Directory.Exists(Path.Combine(directory.Path, "app-updates")));
    }

    [Fact]
    public async Task Check_RejectsInsecureManifestUrlBeforeSendingRequest()
    {
        using var directory = new TemporaryDirectory();
        var handler = new DelegateHandler((_, _) => throw new InvalidOperationException("HTTP request must not be sent."));
        using var service = new HttpAppUpdateService(directory.Path, new HttpClient(handler));
        var request = Request(3) with { ManifestUrl = new Uri("http://example.test/app-update.json") };

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync(request));
    }

    [Fact]
    public async Task Check_RejectsManifestRedirectedToInsecureTransport()
    {
        using var directory = new TemporaryDirectory();
        var manifest = CreateManifest(4, false);
        var handler = new DelegateHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.test/app-update.json"),
            Content = new StringContent(JsonSerializer.Serialize(manifest), Encoding.UTF8, "application/json")
        }));
        using var service = new HttpAppUpdateService(directory.Path, new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync(Request(3)));
    }

    [Fact]
    public async Task Check_RejectsManifestForDifferentPackage()
    {
        using var directory = new TemporaryDirectory();
        var manifest = CreateManifest(4, false) with { PackageId = "example.impostor" };
        using var service = CreateService(directory.Path, manifest, Encoding.UTF8.GetBytes("apk"));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CheckAsync(Request(3)));
    }

    [Fact]
    public async Task Download_RejectsExcessiveDeclaredSizeWithoutCreatingApk()
    {
        using var directory = new TemporaryDirectory();
        var apk = Encoding.UTF8.GetBytes("apk");
        var manifest = CreateManifest(4, false, apk);
        var handler = new DelegateHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (request.RequestUri!.AbsolutePath.EndsWith(".json", StringComparison.Ordinal))
                response.Content = new StringContent(JsonSerializer.Serialize(manifest));
            else
            {
                response.Content = new ByteArrayContent(apk);
                response.Content.Headers.ContentLength = 501L * 1024 * 1024;
            }
            return Task.FromResult(response);
        });
        using var service = new HttpAppUpdateService(directory.Path, new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(manifest));

        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.apk", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.part", SearchOption.AllDirectories));
    }

    private static HttpAppUpdateService CreateService(string root, AppUpdateManifest manifest, byte[] apk)
    {
        var handler = new DelegateHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith(".json", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(manifest), Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(apk) }));
        return new HttpAppUpdateService(root, new HttpClient(handler));
    }

    private static AppUpdateManifest CreateManifest(int code, bool prerelease, byte[]? apk = null)
    {
        apk ??= Encoding.UTF8.GetBytes("apk");
        return new AppUpdateManifest
        {
            VersionCode = code, VersionName = $"1.{code - 1}", PackageId = "com.march.dndcompendium",
            ApkUrl = new Uri("https://example.test/DndCompendium.apk"),
            Sha256 = Convert.ToHexString(SHA256.HashData(apk)).ToLowerInvariant(),
            IsPrerelease = prerelease, Channel = "stable"
        };
    }

    private static AppUpdateCheckRequest Request(int currentCode)
        => new(new Uri("https://example.test/app-update.json"), currentCode, "com.march.dndcompendium");

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
