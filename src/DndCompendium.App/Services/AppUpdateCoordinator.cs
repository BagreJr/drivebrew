using System.Text.Json;
using System.Reflection;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using DndCompendium.Core.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace DndCompendium.App.Services;

public sealed class AppUpdateCoordinator
{
    public const string ManifestUrlKey = "app-updates.manifest-url";
    public const string AutomaticChecksKey = "app-updates.automatic-checks";
    public const string AutomaticDownloadsKey = "app-updates.automatic-downloads";
    public const string LastCheckKey = "app-updates.last-check";
    private const string LatestManifestKey = "app-updates.latest-manifest";
    private const string PostponedVersionKey = "app-updates.postponed-version";
    private const string PostponedAtKey = "app-updates.postponed-at";
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(12);
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromHours(24);
    private readonly IAppUpdateService _service;
    private readonly IAppPackageInstaller _installer;
    private readonly IAppUpdateScheduler _scheduler;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private CancellationTokenSource? _downloadCancellation;
    private string? _downloadedApkPath;
    private int _downloadedVersionCode;

    public AppUpdateCoordinator(
        IAppUpdateService service,
        IAppPackageInstaller installer,
        IAppUpdateScheduler scheduler)
    {
        _service = service;
        _installer = installer;
        _scheduler = scheduler;
        AutomaticChecks = Preferences.Default.Get(AutomaticChecksKey, true);
        AutomaticDownloads = Preferences.Default.Get(AutomaticDownloadsKey, false);
        var savedManifestUrl = Preferences.Default.Get(ManifestUrlKey, string.Empty);
        ManifestUrl = string.IsNullOrWhiteSpace(savedManifestUrl)
            ? ReadBuildManifestUrl()
            : savedManifestUrl;
        LatestManifest = ReadStoredManifest();
        _scheduler.Configure(AutomaticChecks);
    }

    public event EventHandler? StateChanged;
    public string ManifestUrl { get; private set; }
    public bool AutomaticChecks { get; private set; }
    public bool AutomaticDownloads { get; private set; }
    public AppUpdateManifest? LatestManifest { get; private set; }
    public DateTimeOffset? LastCheck => DateTimeOffset.TryParse(
        Preferences.Default.Get(LastCheckKey, string.Empty), out var parsed)
        ? parsed
        : null;
    public bool IsChecking { get; private set; }
    public bool IsDownloading { get; private set; }
    public double DownloadProgress { get; private set; }
    public string? StatusMessage { get; private set; }
    public bool IsUpdateAvailable => LatestManifest?.VersionCode > AppInfo.Current.BuildStringAsInt() && IsAcceptedChannel(LatestManifest);

    public void Configure(string manifestUrl, bool automaticChecks, bool automaticDownloads)
    {
        ManifestUrl = manifestUrl.Trim();
        AutomaticChecks = automaticChecks;
        AutomaticDownloads = automaticDownloads;
        Preferences.Default.Set(ManifestUrlKey, ManifestUrl);
        Preferences.Default.Set(AutomaticChecksKey, automaticChecks);
        Preferences.Default.Set(AutomaticDownloadsKey, automaticDownloads);
        _scheduler.Configure(automaticChecks);
        OnStateChanged();
    }

    public async Task<AppUpdateCheckResult?> CheckAsync(bool manual, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(ManifestUrl, UriKind.Absolute, out var manifestUri)
            || manifestUri.Scheme != Uri.UriSchemeHttps)
        {
            StatusMessage = manual
                ? "Configurá una URL HTTPS para el manifiesto de actualizaciones."
                : null;
            OnStateChanged();
            return null;
        }
        if (!manual && (!AutomaticChecks || !IsCheckDue())) return null;
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            StatusMessage = manual
                ? "No hay conexión a Internet. La aplicación y tus datos offline siguen disponibles."
                : null;
            OnStateChanged();
            return null;
        }
        await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IsChecking = true;
            StatusMessage = "Comprobando actualizaciones…";
            OnStateChanged();
            var request = new AppUpdateCheckRequest(
                manifestUri,
                AppInfo.Current.BuildStringAsInt(),
                AppInfo.Current.PackageName,
                "stable");
            var result = await _service.CheckAsync(request, cancellationToken).ConfigureAwait(false);
            LatestManifest = result.Manifest;
            var now = DateTimeOffset.UtcNow;
            Preferences.Default.Set(LastCheckKey, now.ToString("O"));
            Preferences.Default.Set(LatestManifestKey, JsonSerializer.Serialize(result.Manifest));
            StatusMessage = result.IsUpdateAvailable
                ? $"Versión {result.Manifest.VersionName} disponible."
                : "D&D Compendium is up to date.";
            if (result.IsUpdateAvailable && AutomaticDownloads)
                _downloadedApkPath = await DownloadVerifiedAsync(result.Manifest, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            StatusMessage = manual
                ? $"No se pudo comprobar la actualización: {exception.Message}"
                : "No se pudo comprobar la actualización. La aplicación sigue disponible offline.";
            return null;
        }
        finally
        {
            IsChecking = false;
            OnStateChanged();
            _checkGate.Release();
        }
    }

    public async Task DownloadAndInstallAsync(CancellationToken cancellationToken = default)
    {
        var manifest = LatestManifest;
        if (manifest is null || !IsUpdateAvailable)
            throw new InvalidOperationException("No hay una actualización válida disponible.");
        try
        {
            var path = _downloadedVersionCode == manifest.VersionCode && File.Exists(_downloadedApkPath)
                ? _downloadedApkPath!
                : await DownloadVerifiedAsync(manifest, cancellationToken).ConfigureAwait(false);
            StatusMessage = "Verificando paquete, versión y firma…";
            OnStateChanged();
            var validation = await _installer.ValidateAsync(path, manifest, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid) throw new InvalidDataException(validation.Message);
            StatusMessage = "Abriendo el instalador seguro de Android…";
            OnStateChanged();
            await _installer.InstallAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_downloadCancellation?.IsCancellationRequested == true || cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Descarga cancelada.";
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            throw;
        }
        finally
        {
            if (!IsDownloading) OnStateChanged();
        }
    }

    public void CancelDownload() => _downloadCancellation?.Cancel();

    private async Task<string> DownloadVerifiedAsync(AppUpdateManifest manifest, CancellationToken cancellationToken)
    {
        _downloadCancellation?.Cancel();
        _downloadCancellation?.Dispose();
        _downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        IsDownloading = true;
        DownloadProgress = 0;
        StatusMessage = $"Descargando D&D Compendium {manifest.VersionName}…";
        OnStateChanged();
        try
        {
            var progress = new Progress<AppUpdateDownloadProgress>(value =>
            {
                DownloadProgress = value.Fraction;
                StatusMessage = value.TotalBytes is > 0
                    ? $"Descargando… {value.Fraction:P0}"
                    : $"Descargando… {value.BytesReceived / 1024:N0} KB";
                OnStateChanged();
            });
            var path = await _service.DownloadAsync(manifest, progress, _downloadCancellation.Token).ConfigureAwait(false);
            _downloadedVersionCode = manifest.VersionCode;
            _downloadedApkPath = path;
            StatusMessage = "APK descargada y SHA-256 verificado. Lista para instalar.";
            return path;
        }
        catch (OperationCanceledException) when (_downloadCancellation.IsCancellationRequested)
        {
            StatusMessage = "Descarga cancelada.";
            throw;
        }
        finally
        {
            IsDownloading = false;
            OnStateChanged();
        }
    }

    public bool ShouldShowStartupNotice(AppUpdateManifest manifest)
    {
        var postponedVersion = Preferences.Default.Get(PostponedVersionKey, 0);
        var postponedAtText = Preferences.Default.Get(PostponedAtKey, string.Empty);
        var postponedAt = DateTimeOffset.TryParse(postponedAtText, out var parsed)
            ? parsed
            : (DateTimeOffset?)null;
        return AppUpdateReminderPolicy.ShouldNotify(
            manifest.VersionCode,
            postponedVersion,
            postponedAt,
            DateTimeOffset.UtcNow,
            ReminderInterval);
    }

    public void Postpone(AppUpdateManifest manifest)
    {
        Preferences.Default.Set(PostponedVersionKey, manifest.VersionCode);
        Preferences.Default.Set(PostponedAtKey, DateTimeOffset.UtcNow.ToString("O"));
    }

    private bool IsCheckDue() => LastCheck is not { } last || DateTimeOffset.UtcNow - last >= AutomaticCheckInterval;
    private static bool IsAcceptedChannel(AppUpdateManifest manifest)
        => !manifest.IsPrerelease && string.Equals(manifest.Channel, "stable", StringComparison.OrdinalIgnoreCase);

    private static AppUpdateManifest? ReadStoredManifest()
    {
        try
        {
            var json = Preferences.Default.Get(LatestManifestKey, string.Empty);
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<AppUpdateManifest>(json);
        }
        catch (JsonException) { return null; }
    }

    private static string ReadBuildManifestUrl()
        => typeof(AppUpdateCoordinator).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "AppUpdateManifestUrl")
            ?.Value?.Trim() ?? string.Empty;

    private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}

internal static class AppInfoExtensions
{
    public static int BuildStringAsInt(this IAppInfo appInfo)
        => int.TryParse(appInfo.BuildString, out var value) ? value : 0;
}
