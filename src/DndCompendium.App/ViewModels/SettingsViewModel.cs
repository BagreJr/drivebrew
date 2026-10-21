using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.Core.Abstractions;
using DndCompendium.App.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace DndCompendium.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string AutomaticSyncKey = "settings.automatic-sync";
    private const string WifiOnlyKey = "settings.wifi-only";
    private const string AutoUpdateDownloadedKey = "settings.auto-update-downloaded";
    private readonly ILocalDocumentRepository _repository;
    private readonly AppUpdateCoordinator _appUpdates;

    public SettingsViewModel(ILocalDocumentRepository repository, AppUpdateCoordinator appUpdates)
    {
        _repository = repository;
        _appUpdates = appUpdates;
        automaticSync = Preferences.Default.Get(AutomaticSyncKey, false);
        wifiOnly = Preferences.Default.Get(WifiOnlyKey, true);
        autoUpdateDownloaded = Preferences.Default.Get(AutoUpdateDownloadedKey, false);
        updateManifestUrl = appUpdates.ManifestUrl;
        automaticallyCheckForAppUpdates = appUpdates.AutomaticChecks;
        automaticallyDownloadAppUpdates = appUpdates.AutomaticDownloads;
        _appUpdates.StateChanged += OnAppUpdateStateChanged;
        SyncAppUpdateState();
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    private bool automaticSync;

    [ObservableProperty]
    private bool wifiOnly;

    [ObservableProperty]
    private bool autoUpdateDownloaded;

    [ObservableProperty]
    private string updateManifestUrl = string.Empty;

    [ObservableProperty]
    private bool automaticallyCheckForAppUpdates;

    [ObservableProperty]
    private bool automaticallyDownloadAppUpdates;

    [ObservableProperty]
    private string currentAppVersionText = string.Empty;

    [ObservableProperty]
    private string latestAppVersionText = "No comprobada";

    [ObservableProperty]
    private string lastAppUpdateCheckText = "Nunca";

    [ObservableProperty]
    private string appUpdateStatusText = "Configurá una fuente para comprobar actualizaciones.";

    [ObservableProperty]
    private string appReleaseNotes = "Todavía no hay notas de versión disponibles.";

    [ObservableProperty]
    private bool isCheckingForAppUpdate;

    [ObservableProperty]
    private bool isDownloadingAppUpdate;

    [ObservableProperty]
    private bool isAppUpdateAvailable;

    [ObservableProperty]
    private double appUpdateProgress;

    [ObservableProperty]
    private string storageUsageText = "Calculando…";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    partial void OnAutomaticSyncChanged(bool value) =>
        Preferences.Default.Set(AutomaticSyncKey, value);

    partial void OnWifiOnlyChanged(bool value) =>
        Preferences.Default.Set(WifiOnlyKey, value);

    partial void OnAutoUpdateDownloadedChanged(bool value) =>
        Preferences.Default.Set(AutoUpdateDownloadedKey, value);

    partial void OnUpdateManifestUrlChanged(string value) =>
        Preferences.Default.Set(AppUpdateCoordinator.ManifestUrlKey, value.Trim());
    partial void OnAutomaticallyCheckForAppUpdatesChanged(bool value) => SaveAppUpdateConfiguration();
    partial void OnAutomaticallyDownloadAppUpdatesChanged(bool value) => SaveAppUpdateConfiguration();

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await _repository.InitializeAsync();
            var bytes = await _repository.GetStorageUsageAsync();
            StorageUsageText = $"{ByteSizeFormatter.Format(bytes)} usados por documentos sin conexión";
            SyncAppUpdateState();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo calcular el almacenamiento: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task CheckForAppUpdatesAsync()
    {
        ErrorMessage = null;
        SaveAppUpdateConfiguration();
        await _appUpdates.CheckAsync(manual: true);
        SyncAppUpdateState();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task UpdateAppNowAsync()
    {
        ErrorMessage = null;
        try { await _appUpdates.DownloadAndInstallAsync(); }
        catch (Exception exception) { ErrorMessage = exception.Message; }
        finally { SyncAppUpdateState(); }
    }

    [RelayCommand]
    private void CancelAppUpdate() => _appUpdates.CancelDownload();

    private void SaveAppUpdateConfiguration()
        => _appUpdates.Configure(UpdateManifestUrl, AutomaticallyCheckForAppUpdates, AutomaticallyDownloadAppUpdates);

    private void OnAppUpdateStateChanged(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(SyncAppUpdateState);

    private void SyncAppUpdateState()
    {
        CurrentAppVersionText = $"{AppInfo.Current.VersionString} (code {AppInfo.Current.BuildString})";
        LatestAppVersionText = _appUpdates.LatestManifest is { } manifest
            ? $"{manifest.VersionName} (code {manifest.VersionCode})"
            : "No comprobada";
        LastAppUpdateCheckText = _appUpdates.LastCheck?.ToLocalTime().ToString("g") ?? "Nunca";
        AppUpdateStatusText = _appUpdates.StatusMessage
            ?? (string.IsNullOrWhiteSpace(_appUpdates.ManifestUrl)
                ? "Configurá una fuente para comprobar actualizaciones."
                : _appUpdates.IsUpdateAvailable
                    ? "D&D Compendium — Update Available"
                    : _appUpdates.LastCheck is null
                        ? "Todavía no se comprobó si hay actualizaciones."
                        : "D&D Compendium is up to date.");
        AppReleaseNotes = string.IsNullOrWhiteSpace(_appUpdates.LatestManifest?.ReleaseNotes)
            ? "Todavía no hay notas de versión disponibles."
            : _appUpdates.LatestManifest.ReleaseNotes;
        IsCheckingForAppUpdate = _appUpdates.IsChecking;
        IsDownloadingAppUpdate = _appUpdates.IsDownloading;
        IsAppUpdateAvailable = _appUpdates.IsUpdateAvailable;
        AppUpdateProgress = _appUpdates.DownloadProgress;
    }
}
