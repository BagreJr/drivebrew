using DndCompendium.Core.Abstractions;
using Microsoft.Maui.ApplicationModel;
using DndCompendium.App.Services;

namespace DndCompendium.App;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly IDownloadManager _downloadManager;
    private readonly AppUpdateCoordinator _appUpdates;
    private int _startupUpdateStarted;

    public App(AppShell shell, IDownloadManager downloadManager, AppUpdateCoordinator appUpdates)
    {
        InitializeComponent();
        _shell = shell;
        _downloadManager = downloadManager;
        _appUpdates = appUpdates;
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
        try
        {
            var result = await _downloadManager.UpdateInstalledDocumentsAsync();
            var message = BuildUpdateMessage(result.UpdatedFileNames, result.FailedFileNames);
            await MainThread.InvokeOnMainThreadAsync(() =>
                _shell.DisplayAlertAsync("Actualización de archivos TXT", message, "Aceptar"));
        }
        catch (Exception exception)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                _shell.DisplayAlertAsync(
                    "Actualización de archivos TXT",
                    $"No se pudieron comprobar las actualizaciones: {exception.Message}",
                    "Aceptar"));
        }

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
    }

    private static string BuildUpdateMessage(
        IReadOnlyList<string> updated,
        IReadOnlyList<string> failed)
    {
        var sections = new List<string>
        {
            updated.Count == 0
                ? "No se actualizó ningún archivo TXT descargado."
                : $"Archivos actualizados:\n• {string.Join("\n• ", updated)}"
        };
        if (failed.Count > 0)
        {
            sections.Add($"No se pudieron actualizar:\n• {string.Join("\n• ", failed)}");
        }

        return string.Join("\n\n", sections);
    }
}
