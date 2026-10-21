using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;

namespace DndCompendium.App.Views;

public partial class RulesAssistantPage : ContentView
{
    public const string NotebookUrl = "https://notebook.google.com/notebook/26d8e824-88d9-423d-9a40-61c032d80e88";

    public RulesAssistantPage()
    {
        InitializeComponent();
    }

    public static Task LoadAsync() => Task.CompletedTask;

    private async void OnOpenAssistantClicked(object? sender, EventArgs e)
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            await Shell.Current.DisplayAlertAsync(
                "Sin conexión",
                "El Asistente de Reglas necesita Internet. El resto de la aplicación continúa disponible offline.",
                "Entendido");
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(new Uri(NotebookUrl));
        }
        catch (Exception)
        {
            await Shell.Current.DisplayAlertAsync(
                "No se pudo abrir",
                "Copiá el enlace y abrilo manualmente en Chrome o Safari.",
                "Entendido");
        }
    }

    private async void OnCopyLinkClicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(NotebookUrl);
        await Shell.Current.DisplayAlertAsync("Enlace copiado", "El enlace del Asistente de Reglas está en el portapapeles.", "Aceptar");
    }
}
