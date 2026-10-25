using System.Text.Json;
using DndCompendium.App.ViewModels;
using Microsoft.Maui.ApplicationModel;

namespace DndCompendium.App.Views;

public partial class CharacterSheetPage : ContentPage, IQueryAttributable
{
    private static readonly HashSet<string> LocalWebViewHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "0.0.0.0", "0.0.0.1", "127.0.0.1", "localhost", "appassets.androidplatform.net"
    };

    private readonly CharacterSheetViewModel _viewModel;
    private string? _characterId;
    private bool _webViewReady;

    public CharacterSheetPage(CharacterSheetViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("characterId", out var value)) return;
        var characterId = Uri.UnescapeDataString(Convert.ToString(value) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(characterId) || characterId == _characterId) return;
        _characterId = characterId;
        _ = LoadAndSendAsync(characterId);
    }

    private async Task LoadAndSendAsync(string characterId)
    {
        await _viewModel.LoadAsync(characterId);
        await MainThread.InvokeOnMainThreadAsync(SendSheetPayload);
    }

    private async void OnRawMessageReceived(object? sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => HandleRawMessageAsync(e.Message));
        }
        catch (Exception exception)
        {
            MainThread.BeginInvokeOnMainThread(() => _viewModel.ReportEditorError(exception.Message));
        }
    }

    private async Task HandleRawMessageAsync(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var typeElement)) return;
        switch (typeElement.GetString())
        {
            case "ready":
                _webViewReady = true;
                SendSheetPayload();
                break;
            case "change":
                var name = root.GetProperty("name").GetString() ?? string.Empty;
                var kind = root.TryGetProperty("kind", out var kindElement) ? kindElement.GetString() ?? "text" : "text";
                var value = root.TryGetProperty("value", out var valueElement) ? valueElement.GetString() : null;
                var isChecked = root.TryGetProperty("checked", out var checkedElement) && checkedElement.GetBoolean();
                _viewModel.ApplyEditorChange(name, kind, value, isChecked);
                break;
            case "save":
                await _viewModel.SaveIfNeededAsync();
                break;
            case "error":
                _viewModel.ReportEditorError(
                    root.TryGetProperty("message", out var errorElement)
                        ? errorElement.GetString() ?? "Error desconocido"
                        : "Error desconocido");
                break;
        }
    }

    private void SendSheetPayload()
    {
        if (!_webViewReady || !_viewModel.HasCharacter || SheetWebView.Handler is null) return;
        try
        {
            SheetWebView.SendRawMessage(_viewModel.CreateEditorPayloadJson());
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            _viewModel.ReportEditorError("El editor se cerró antes de terminar de cargar.");
        }
    }

    private static void OnWebResourceRequested(object? sender, WebViewWebResourceRequestedEventArgs e)
    {
        if (e.Uri.Scheme is not ("http" or "https") || e.Uri.IsLoopback || LocalWebViewHosts.Contains(e.Uri.Host)) return;
        e.Handled = true;
        e.SetResponse(403, "Editor offline: las conexiones externas están bloqueadas");
    }

    protected override async void OnDisappearing()
    {
        await _viewModel.SaveIfNeededAsync();
        base.OnDisappearing();
    }
}
