using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.App.Services;

public sealed class UnsupportedAppPackageInstaller : IAppPackageInstaller
{
    public Task<AppPackageValidationResult> ValidateAsync(string apkPath, AppUpdateManifest manifest, CancellationToken cancellationToken = default)
        => Task.FromResult(new AppPackageValidationResult(false, "La instalación de APK solamente está disponible en Android."));

    public Task InstallAsync(string apkPath, CancellationToken cancellationToken = default)
        => throw new PlatformNotSupportedException("La instalación de APK solamente está disponible en Android.");
}

public sealed class NoopAppUpdateScheduler : IAppUpdateScheduler
{
    public void Configure(bool enabled) { }
}
