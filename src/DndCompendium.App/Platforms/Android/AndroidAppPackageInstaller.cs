using System.Security.Cryptography;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using DndCompendium.Core.Services;
using Microsoft.Maui.ApplicationModel;

namespace DndCompendium.App;

public sealed class AndroidAppPackageInstaller : IAppPackageInstaller
{
    public Task<AppPackageValidationResult> ValidateAsync(
        string apkPath,
        AppUpdateManifest manifest,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(apkPath))
            return Task.FromResult(new AppPackageValidationResult(false, "La APK descargada ya no existe."));
        var context = Android.App.Application.Context;
        var packageManager = context.PackageManager;
        if (packageManager is null)
            return Task.FromResult(new AppPackageValidationResult(false, "Android no permitió validar el paquete."));
#pragma warning disable CA1416
        var signatureFlags = Build.VERSION.SdkInt >= BuildVersionCodes.P
            ? PackageInfoFlags.SigningCertificates
#pragma warning disable CA1422
            : PackageInfoFlags.Signatures;
#pragma warning restore CA1416
        var candidate = packageManager.GetPackageArchiveInfo(apkPath, signatureFlags);
        var installed = packageManager.GetPackageInfo(context.PackageName!, signatureFlags);
#pragma warning restore CA1422
        if (candidate is null || installed is null)
            return Task.FromResult(new AppPackageValidationResult(false, "La descarga no es una APK Android válida."));
        var candidateVersion = GetVersionCode(candidate);
        var installedVersion = GetVersionCode(installed);
        var installedCertificates = GetSigningCertificates(installed);
        var candidateCertificates = GetSigningCertificates(candidate);
        return Task.FromResult(AppPackageCompatibility.Validate(
            new AppPackageIdentity(context.PackageName!, installedVersion, installedCertificates),
            new AppPackageIdentity(candidate.PackageName!, candidateVersion, candidateCertificates),
            manifest,
            (int)Build.VERSION.SdkInt));
    }

    public Task InstallAsync(string apkPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = Android.App.Application.Context;
#pragma warning disable CA1416
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O
            && context.PackageManager?.CanRequestPackageInstalls() != true)
        {
            var permissionIntent = new Intent(Settings.ActionManageUnknownAppSources)
                .SetData(Android.Net.Uri.Parse($"package:{context.PackageName}"))
                .AddFlags(ActivityFlags.NewTask);
            context.StartActivity(permissionIntent);
            throw new InvalidOperationException("Android abrió ‘Instalar apps desconocidas’. Permití D&D Compendium, volvé a la aplicación y tocá Update Now nuevamente.");
        }
#pragma warning restore CA1416
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(
            context,
            $"{context.PackageName}.updateprovider",
            new Java.IO.File(apkPath));
        var installIntent = new Intent(Intent.ActionView)
            .SetDataAndType(uri, "application/vnd.android.package-archive")
            .AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
        context.StartActivity(installIntent);
        return Task.CompletedTask;
    }

#pragma warning disable CA1416
    private static int GetVersionCode(PackageInfo package)
        => Build.VERSION.SdkInt >= BuildVersionCodes.P
            ? checked((int)package.LongVersionCode)
#pragma warning disable CA1422
            : package.VersionCode;
#pragma warning restore CA1422
#pragma warning restore CA1416

    private static string CertificateDigest(Signature signature)
        => Convert.ToHexString(SHA256.HashData(signature.ToByteArray() ?? [])).ToLowerInvariant();

    private static HashSet<string> GetSigningCertificates(PackageInfo package)
    {
#pragma warning disable CA1416
        if (Build.VERSION.SdkInt >= BuildVersionCodes.P && package.SigningInfo is { } signingInfo)
        {
            var signatures = signingInfo.HasMultipleSigners
                ? signingInfo.GetApkContentsSigners()
                : signingInfo.GetSigningCertificateHistory();
            return (signatures ?? []).Select(CertificateDigest).ToHashSet(StringComparer.Ordinal);
        }
#pragma warning restore CA1416
#pragma warning disable CA1422
        return (package.Signatures ?? []).Select(CertificateDigest).ToHashSet(StringComparer.Ordinal);
#pragma warning restore CA1422
    }
}
