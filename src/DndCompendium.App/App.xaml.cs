using DndCompendium.Core.Abstractions;
using Microsoft.Maui.ApplicationModel;
using DndCompendium.App.Services;

namespace DndCompendium.App;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly IDownloadManager _downloadManager;
    private readonly AppUpdateCoordinator _appUpdates;
    private readonly ContentUpdateStatus _contentUpdates;
#if WINDOWS
    private readonly WindowsUpdateService _windowsUpdates;
    private static readonly TimeSpan WindowsCheckInterval = TimeSpan.FromHours(12);
    private const string WindowsLastLibraryCheck = "windows.last-library-check";
#endif
    private int _startupUpdateStarted;

    public App(
        AppShell shell,
        IDownloadManager downloadManager,
        AppUpdateCoordinator appUpdates,
        ContentUpdateStatus contentUpdates
#if WINDOWS
        , WindowsUpdateService windowsUpdates
#endif
        )
    {
        InitializeComponent();
        _shell = shell;
        _downloadManager = downloadManager;
        _appUpdates = appUpdates;
        _contentUpdates = contentUpdates;
#if WINDOWS
        _windowsUpdates = windowsUpdates;
#endif
        UserAppTheme = AppTheme.Dark;
        _shell.Loaded += OnShellLoaded;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_shell);
    }

    private async void OnShellLoaded(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _startupUpdateStarted, 1) != 0)
        {
            return;
        }

        _shell.Loaded -= OnShellLoaded;
#if WINDOWS
        await RefreshWindowsInBackgroundAsync();
#else
        _contentUpdates.SetChecking();
        try
        {
            var result = await _downloadManager.UpdateInstalledDocumentsAsync();
            _contentUpdates.SetResult(result);
        }
        catch (Exception)
        {
            _contentUpdates.SetUnavailable();
        }

#if ANDROID
        var appUpdate = await _appUpdates.CheckAsync(manual: false);
        var availableManifest = appUpdate is { IsUpdateAvailable: true }
            ? appUpdate.Manifest
            : _appUpdates.IsUpdateAvailable
                ? _appUpdates.LatestManifest
                : null;
        if (availableManifest is not null
            && _appUpdates.ShouldShowStartupNotice(availableManifest))
        {
            var notes = string.IsNullOrWhiteSpace(availableManifest.ReleaseNotes)
                ? "Sin notas de versión."
                : availableManifest.ReleaseNotes;
            var updateNow = await MainThread.InvokeOnMainThreadAsync(() =>
                _shell.DisplayAlertAsync(
                    "D&D Compendium — Update Available",
                    $"Current Version: {AppInfo.Current.VersionString}\nNew Version: {availableManifest.VersionName}\n\nWhat's New:\n{notes}\n\nAndroid solicitará tu confirmación para instalar.",
                    "Update Now",
                    "Later"));
            if (updateNow)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                    _shell.GoToAsync("//tabs/more?section=settings&update=true"));
            }
            else _appUpdates.Postpone(availableManifest);
        }
#endif
#endif
    }

#if WINDOWS
    private async Task RefreshWindowsInBackgroundAsync()
    {
        var lastCheck = new DateTime(Preferences.Default.Get(WindowsLastLibraryCheck, 0L), DateTimeKind.Utc);
        if (DateTime.UtcNow - lastCheck < WindowsCheckInterval)
        {
            _contentUpdates.SetResult(new([], []));
        }
        else if (Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
        {
            // Let Windows paint the local catalog before checking network metadata.
            await Task.Delay(1500);
            try
            {
                var result = await _downloadManager.UpdateInstalledDocumentsAsync();
                if (result.FailedFileNames.Count == 0)
                    Preferences.Default.Set(WindowsLastLibraryCheck, DateTime.UtcNow.Ticks);
                _contentUpdates.SetResult(result);
            }
            catch (Exception)
            {
                _contentUpdates.SetUnavailable();
            }
        }
        else
        {
            _contentUpdates.SetUnavailable();
        }
        await NotifyWindowsUpdateAsync();
    }

    private async Task NotifyWindowsUpdateAsync()
    {
        try
        {
            var update = await _windowsUpdates.CheckAsync(automatic: true);
            if (update is null) return;
            var download = await _shell.DisplayAlertAsync("Actualización disponible",
                $"Hay un anuncio para D&D Compendium {update.Version.ToString(2)}. Verificá la firma y el editor del MSIX antes de instalar.",
                "Ver publicación", "Más tarde");
            if (download) await Launcher.Default.OpenAsync(update.ReleaseUri);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            System.Diagnostics.Debug.WriteLine(exception.Message);
        }
    }
#endif
}
