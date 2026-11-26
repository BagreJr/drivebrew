#if WINDOWS
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace DndCompendium.App.Services;

public sealed class WindowsUpdateService
{
    private const string Repository = "BagreJr/drivebrew";
    private const string AssetName = "Drivebrew-AppContainer-x64.msix";
    private const string LastCheckKey = "windows.last-app-check";
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(12) };

    public WindowsUpdateService()
    {
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("DndCompendium-Windows/1.0");
    }

    public async Task<WindowsUpdate?> CheckAsync(bool automatic = false)
    {
        var installed = typeof(WindowsUpdateService).Assembly.GetName().Version;
        if (installed is null || RuntimeInformation.ProcessArchitecture != Architecture.X64) return null;
        if (automatic && (DateTime.UtcNow - new DateTime(Preferences.Default.Get(LastCheckKey, 0L), DateTimeKind.Utc)
            < TimeSpan.FromHours(12) || Connectivity.Current.NetworkAccess != NetworkAccess.Internet)) return null;

        var releases = await _client.GetFromJsonAsync<GitHubRelease[]>(
            $"https://api.github.com/repos/{Repository}/releases?per_page=30") ?? [];
        Preferences.Default.Set(LastCheckKey, DateTime.UtcNow.Ticks);
        return releases.Where(release => !release.Prerelease && !release.Draft)
            .Where(release => release.Assets.Any(asset => asset.Name == AssetName))
            .Select(release => Version.TryParse(release.Tag.TrimStart('v'), out var parsed)
                ? new WindowsUpdate(new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build), Math.Max(0, parsed.Revision)),
                    new Uri($"https://github.com/{Repository}/releases/tag/{Uri.EscapeDataString(release.Tag)}"))
                : null)
            .OfType<WindowsUpdate>()
            .Where(update => update.Version > installed)
            .OrderByDescending(update => update.Version)
            .FirstOrDefault();
    }

    public sealed record WindowsUpdate(Version Version, Uri ReleaseUri);

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string Tag,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("assets")] GitHubAsset[] Assets);

    private sealed record GitHubAsset([property: JsonPropertyName("name")] string Name);
}
#endif
