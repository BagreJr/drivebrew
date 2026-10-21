namespace DndCompendium.Core.Models;

public sealed record AppUpdateManifest
{
    public required int VersionCode { get; init; }
    public required string VersionName { get; init; }
    public required string PackageId { get; init; }
    public required Uri ApkUrl { get; init; }
    public required string Sha256 { get; init; }
    public string ReleaseNotes { get; init; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; init; }
    public int MinimumAndroidApi { get; init; } = 21;
    public string Channel { get; init; } = "stable";
    public bool IsPrerelease { get; init; }
}

public sealed record AppUpdateCheckRequest(
    Uri ManifestUrl,
    int CurrentVersionCode,
    string CurrentPackageId,
    string Channel = "stable");

public sealed record AppUpdateCheckResult(
    AppUpdateManifest Manifest,
    bool IsUpdateAvailable);

public sealed record AppUpdateDownloadProgress(long BytesReceived, long? TotalBytes)
{
    public double Fraction => TotalBytes is > 0
        ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1)
        : 0;
}

public sealed record AppPackageValidationResult(
    bool IsValid,
    string Message,
    int VersionCode = 0,
    string? PackageId = null);

public sealed record AppPackageIdentity(
    string PackageId,
    int VersionCode,
    IReadOnlySet<string> SigningCertificateSha256);
