using DndCompendium.Core.Models;

namespace DndCompendium.Core.Services;

public static class AppPackageCompatibility
{
    public static AppPackageValidationResult Validate(
        AppPackageIdentity installed,
        AppPackageIdentity candidate,
        AppUpdateManifest manifest,
        int currentAndroidApi)
    {
        if (!string.Equals(candidate.PackageId, installed.PackageId, StringComparison.Ordinal)
            || !string.Equals(candidate.PackageId, manifest.PackageId, StringComparison.Ordinal))
            return new(false, "La APK descargada pertenece a otra aplicación.", candidate.VersionCode, candidate.PackageId);
        if (candidate.VersionCode != manifest.VersionCode || candidate.VersionCode <= installed.VersionCode)
            return new(false, "La APK no tiene un versionCode superior y válido.", candidate.VersionCode, candidate.PackageId);
        if (currentAndroidApi < manifest.MinimumAndroidApi)
            return new(false, $"Esta actualización requiere Android API {manifest.MinimumAndroidApi} o superior.", candidate.VersionCode, candidate.PackageId);
        if (installed.SigningCertificateSha256.Count == 0
            || !installed.SigningCertificateSha256.Overlaps(candidate.SigningCertificateSha256))
            return new(false, "La firma de la APK no coincide con la aplicación instalada.", candidate.VersionCode, candidate.PackageId);
        return new(true, "Paquete, versión y firma verificados.", candidate.VersionCode, candidate.PackageId);
    }
}
