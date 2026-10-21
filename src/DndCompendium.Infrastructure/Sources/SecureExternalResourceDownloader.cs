using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.Infrastructure.Sources;

public sealed class SecureExternalResourceDownloader : IExternalResourceDownloader
{
    public const long DefaultMaximumResourceBytes = 16L * 1024 * 1024;
    private const int MaximumRedirects = 5;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly HttpClient _httpClient;
    private readonly long _maximumResourceBytes;

    public SecureExternalResourceDownloader(HttpClient httpClient, long maximumResourceBytes = DefaultMaximumResourceBytes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _maximumResourceBytes = maximumResourceBytes > 0 ? maximumResourceBytes : throw new ArgumentOutOfRangeException(nameof(maximumResourceBytes));
    }

    public async Task<ExternalResourceDownload> DownloadAsync(
        ExternalResourceReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!Uri.TryCreate(reference.SourceUrl, UriKind.Absolute, out var current)) throw new InvalidDataException("External resource URL is invalid.");

        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            await ValidateUriAsync(current, reference.Kind, cancellationToken).ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.AcceptEncoding.Clear();
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            request.Headers.UserAgent.ParseAdd("DndCompendium-OfflineResource/2.0");
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (IsRedirect(response.StatusCode))
            {
                if (redirect == MaximumRedirects || response.Headers.Location is null) throw new HttpRequestException("External resource exceeded the redirect limit.");
                current = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(current, response.Headers.Location);
                continue;
            }

            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"External resource returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
            if (response.Content.Headers.ContentLength > _maximumResourceBytes) throw new InvalidDataException("External resource is larger than the configured offline limit.");

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (output.Length + read > _maximumResourceBytes) throw new InvalidDataException("External resource is larger than the configured offline limit.");
                output.Write(buffer, 0, read);
            }

            var content = output.ToArray();
            if (content.Length == 0) throw new InvalidDataException("External resource is empty.");
            var declaredMime = response.Content.Headers.ContentType?.MediaType;
            var validatedMime = ValidateContent(reference.Kind, declaredMime, content);
            return new ExternalResourceDownload(validatedMime, content, current);
        }

        throw new HttpRequestException("External resource redirect processing failed.");
    }

    private static async Task ValidateUriAsync(Uri uri, DocumentResourceKind kind, CancellationToken cancellationToken)
    {
        if (uri.AbsoluteUri.Length > 2048 || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("External resource URL is not allowed.");
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && kind == DocumentResourceKind.Image)) throw new InvalidDataException("Only HTTPS resources are allowed; HTTP is limited to non-executable images.");
        if (!uri.IsDefaultPort && uri.Port is not (80 or 443)) throw new InvalidDataException("External resource port is not allowed.");

        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal)) addresses = [literal];
        else addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(IsPrivateOrReserved)) throw new InvalidDataException("External resource resolves to a private or reserved network address.");
    }

    private static bool IsPrivateOrReserved(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal) return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6) return address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any);
        var bytes = address.GetAddressBytes();
        return bytes[0] is 0 or 10 or 127 || bytes[0] >= 224
            || (bytes[0] == 169 && bytes[1] == 254)
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
            || (bytes[0] == 198 && bytes[1] is 18 or 19);
    }

    private static string ValidateContent(DocumentResourceKind kind, string? declaredMime, byte[] content)
        => kind switch
        {
            DocumentResourceKind.Image => DetectImageMime(content) ?? throw new InvalidDataException($"Unsupported or invalid image resource ({declaredMime ?? "unknown MIME"})."),
            DocumentResourceKind.Font => DetectFontMime(content) ?? throw new InvalidDataException($"Unsupported or invalid font resource ({declaredMime ?? "unknown MIME"})."),
            DocumentResourceKind.Style => ValidateCss(content),
            _ => throw new InvalidDataException("Unsupported external resource kind.")
        };

    private static string? DetectImageMime(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return "image/gif";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (bytes.Length >= 12 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8) && (bytes.Slice(8, 4).SequenceEqual("avif"u8) || bytes.Slice(8, 4).SequenceEqual("avis"u8))) return "image/avif";
        return null;
    }

    private static string? DetectFontMime(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("wOFF"u8)) return "font/woff";
        if (bytes.StartsWith("wOF2"u8)) return "font/woff2";
        if (bytes.StartsWith("OTTO"u8)) return "font/otf";
        if (bytes.StartsWith(new byte[] { 0x00, 0x01, 0x00, 0x00 })) return "font/ttf";
        return null;
    }

    private static string ValidateCss(byte[] content)
    {
        string css;
        try { css = StrictUtf8.GetString(content); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("External stylesheet is not valid UTF-8.", exception); }
        if (css.IndexOf('\0') >= 0 || css.Contains("<html", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("External stylesheet content is invalid.");
        return "text/css";
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}
