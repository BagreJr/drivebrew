using DndCompendium.App.Services;
using DndCompendium.App.ViewModels;
using DndCompendium.Infrastructure.Persistence;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace DndCompendium.App.Views;

public partial class SettingsPage : ContentView
{
    private readonly SettingsViewModel _viewModel;
    private readonly JsonCharacterProfileStore _profiles;
    private readonly ICharacterSheetService _sheets;
#if WINDOWS
    private readonly WindowsUpdateService _windowsUpdates;
#endif

    public SettingsPage(SettingsViewModel viewModel, JsonCharacterProfileStore profiles, ICharacterSheetService sheets
#if WINDOWS
        , WindowsUpdateService windowsUpdates
#endif
    )
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _profiles = profiles;
        _sheets = sheets;
        PreviousBackupButton.IsVisible = File.Exists(_profiles.PreviousCharacterBackupPath);
        AndroidUpdatesTitle.IsVisible = AndroidUpdatesCard.IsVisible = DeviceInfo.Platform == DevicePlatform.Android;
#if WINDOWS
        _windowsUpdates = windowsUpdates;
        WindowsUpdatesCard.IsVisible = true;
        WindowsUpdateStatus.Text = $"Versión instalada {AppInfo.Current.VersionString}";
#endif
    }

    public Task LoadAsync() => _viewModel.LoadCommand.ExecuteAsync(null);

    public Task BeginUpdateAsync() => _viewModel.UpdateAppNowCommand.ExecuteAsync(null);

    private async void OnExportCharacter(object? sender, EventArgs e) => await RunTransferAsync(() =>
        SaveTransferAsync($"Dnd-personaje-{DateTime.Now:yyyyMMdd-HHmmss}", _sheets.ExportActiveCharacterAsync));

    private async void OnExportPreviousBackup(object? sender, EventArgs e) => await RunTransferAsync(() =>
        SaveTransferAsync("Dnd-respaldo-anterior", async output =>
        {
            await using var input = File.OpenRead(_profiles.PreviousCharacterBackupPath);
            await input.CopyToAsync(output);
        }));

    private async void OnImportCharacter(object? sender, EventArgs e) => await RunTransferAsync(async () =>
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Elegí la copia del personaje",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.WinUI] = [".json"],
                [DevicePlatform.Android] = ["application/json", "application/octet-stream"],
                [DevicePlatform.iOS] = ["public.json"]
            })
        });
        if (file is null) return null;
        if (!await Shell.Current.DisplayAlertAsync("Importar personaje",
                "Si el personaje ya está vinculado, la copia elegida reemplazará sus datos. Podés recuperar la versión anterior con «Guardar respaldo anterior».",
                "Importar", "Cancelar")) return null;

        await using var input = await file.OpenReadAsync();
        var name = await _sheets.ImportCharacterAsync(input);
        return $"{name} se importó y quedó como personaje activo.";
    });

    private async Task RunTransferAsync(Func<Task<string?>> transfer)
    {
        if (!CharacterTransferCard.IsEnabled) return;
        CharacterTransferCard.IsEnabled = false;
        try { CharacterTransferStatus.Text = await transfer() ?? string.Empty; }
        catch (Exception exception) { CharacterTransferStatus.Text = $"No se pudo completar la transferencia. {exception.Message}"; }
        finally
        {
            PreviousBackupButton.IsVisible = File.Exists(_profiles.PreviousCharacterBackupPath);
            CharacterTransferCard.IsEnabled = true;
        }
    }

    private async Task<string?> SaveTransferAsync(string name, Func<Stream, Task> write)
    {
        using var content = new MemoryStream();
        await write(content);
        content.Position = 0;
#if WINDOWS
        var window = (Microsoft.UI.Xaml.Window)Window.Handler!.PlatformView!;
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = name };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
        picker.FileTypeChoices.Add("Copia de personaje", [".json"]);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return null;
        await using var output = await file.OpenStreamForWriteAsync();
        output.SetLength(0);
        await content.CopyToAsync(output);
        return $"Copia guardada en {file.Name}.";
#else
        var path = Path.Combine(FileSystem.Current.CacheDirectory, $"{name}.json");
        await using (var output = File.Create(path)) await content.CopyToAsync(output);
        await Share.Default.RequestAsync(new ShareFileRequest("Compartir copia del personaje", new ShareFile(path, "application/json")));
        return null;
#endif
    }

    private async void OnCheckWindowsUpdate(object? sender, EventArgs e)
    {
#if WINDOWS
        WindowsUpdateCheckButton.IsEnabled = false;
        WindowsUpdateStatus.Text = "Buscando actualizaciones…";
        try
        {
            var update = await _windowsUpdates.CheckAsync();
            WindowsUpdateStatus.Text = update is null
                ? $"Estás usando la versión {AppInfo.Current.VersionString}."
                : $"Versión {update.Version.ToString(2)} disponible.";
            if (update is not null && await Shell.Current.DisplayAlertAsync("Actualización de Windows",
                    "Verificá la firma y el editor del MSIX antes de instalar.",
                    "Ver publicación", "Más tarde"))
                await Launcher.Default.OpenAsync(update.ReleaseUri);
        }
        catch (Exception exception) { WindowsUpdateStatus.Text = $"No se pudo buscar la actualización. {exception.Message}"; }
        finally { WindowsUpdateCheckButton.IsEnabled = true; }
#else
        await Task.CompletedTask;
#endif
    }
}
