using System.Security.Cryptography;
using System.Text.Json;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.Infrastructure.Updates;

public sealed class HttpAppUpdateService : IAppUpdateService, IDisposable
{
    private const long MaximumApkBytes = 500L * 1024 * 1024;
    private static readonly TimeSpan ManifestRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly HttpClient _client;
    private readonly string _downloadDirectory;
    private readonly bool _ownsClient;

    public HttpAppUpdateService(string rootPath, HttpClient? client = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _client = client ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            UseCookies = false,
            UseDefaultCredentials = false
        }) { Timeout = TimeSpan.FromMinutes(10) };
        _ownsClient = client is null;
        _downloadDirectory = Path.Combine(Path.GetFullPath(rootPath), "app-updates");
    }

    public async Task<AppUpdateCheckResult> CheckAsync(
        AppUpdateCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireHttps(request.ManifestUrl, "manifest");
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCancellation.CancelAfter(ManifestRequestTimeout);
        using var response = await _client.GetAsync(
            request.ManifestUrl,
            HttpCompletionOption.ResponseHeadersRead,
            requestCancellation.Token).ConfigureAwait(false);
        RequireHttps(response.RequestMessage?.RequestUri ?? request.ManifestUrl, "manifest");
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(requestCancellation.Token).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<AppUpdateManifest>(content, JsonOptions, requestCancellation.Token).ConfigureAwait(false)
            ?? throw new InvalidDataException("El manifiesto de actualización está vacío.");
        ValidateManifest(manifest, request);
        var correctChannel = string.Equals(manifest.Channel, request.Channel, StringComparison.OrdinalIgnoreCase);
        var acceptedRelease = !(manifest.IsPrerelease
            && string.Equals(request.Channel, "stable", StringComparison.OrdinalIgnoreCase));
        return new AppUpdateCheckResult(
            manifest,
            correctChannel && acceptedRelease && manifest.VersionCode > request.CurrentVersionCode);
    }

    public async Task<string> DownloadAsync(
        AppUpdateManifest manifest,
        IProgress<AppUpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        RequireHttps(manifest.ApkUrl, "APK");
        Directory.CreateDirectory(_downloadDirectory);
        var finalPath = Path.Combine(_downloadDirectory, $"dnd-compendium-{manifest.VersionCode}.apk");
        var temporaryPath = $"{finalPath}.{Guid.NewGuid():N}.part";
        try
        {
            using var response = await _client.GetAsync(
                manifest.ApkUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            RequireHttps(response.RequestMessage?.RequestUri ?? manifest.ApkUrl, "APK");
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            if (total > MaximumApkBytes)
                throw new InvalidDataException("La APK publicada supera el límite de seguridad de 500 MB.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[131072];
            long received = 0;
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            {
                while (true)
                {
                    var count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (count == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, count);
                    received += count;
                    if (received > MaximumApkBytes)
                        throw new InvalidDataException("La descarga superó el límite de seguridad de 500 MB.");
                    progress?.Report(new AppUpdateDownloadProgress(received, total));
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actual),
                    Convert.FromHexString(manifest.Sha256)))
                throw new InvalidDataException("El SHA-256 de la APK descargada no coincide con el publicado.");
            File.Move(temporaryPath, finalPath, overwrite: true);
            return finalPath;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void ValidateManifest(AppUpdateManifest manifest, AppUpdateCheckRequest request)
    {
        if (manifest.VersionCode <= 0 || string.IsNullOrWhiteSpace(manifest.VersionName))
            throw new InvalidDataException("El manifiesto no contiene una versión válida.");
        if (!string.Equals(manifest.PackageId, request.CurrentPackageId, StringComparison.Ordinal))
            throw new InvalidDataException("El manifiesto pertenece a otro paquete Android.");
        RequireHttps(manifest.ApkUrl, "APK");
        if (manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("El SHA-256 publicado no es válido.");
    }

    private static void RequireHttps(Uri uri, string resource)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException($"La URL de {resource} debe usar HTTPS.");
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
