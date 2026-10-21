using DndCompendium.Core.Models;

namespace DndCompendium.Core.Abstractions;

public interface IAppUpdateService
{
    Task<AppUpdateCheckResult> CheckAsync(
        AppUpdateCheckRequest request,
        CancellationToken cancellationToken = default);

    Task<string> DownloadAsync(
        AppUpdateManifest manifest,
        IProgress<AppUpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IAppPackageInstaller
{
    Task<AppPackageValidationResult> ValidateAsync(
        string apkPath,
        AppUpdateManifest manifest,
        CancellationToken cancellationToken = default);

    Task InstallAsync(string apkPath, CancellationToken cancellationToken = default);
}

public interface IAppUpdateScheduler
{
    void Configure(bool enabled);
}
