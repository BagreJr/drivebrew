using System.ComponentModel;
using System.Text.Json;
using DndCompendium.App.ViewModels;
using Microsoft.Maui.ApplicationModel;

namespace DndCompendium.App.Views;

public partial class ReaderPage : ContentPage, IQueryAttributable
{
    private static readonly HashSet<string> LocalWebViewHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "0.0.0.0",
        "0.0.0.1",
        "127.0.0.1",
        "localhost",
        "appassets.androidplatform.net"
    };

    private readonly ReaderViewModel _viewModel;
    private bool _webViewReady;
    private int _payloadGeneration;
    private string? _pendingTarget;

    public ReaderPage(ReaderViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ReaderMessageRequested += OnReaderMessageRequested;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _pendingTarget = query.TryGetValue("target", out var rawTarget)
            && !string.IsNullOrWhiteSpace(Convert.ToString(rawTarget))
                ? Uri.UnescapeDataString(Convert.ToString(rawTarget)!)
                : null;
        if (!query.TryGetValue("documentId", out var rawDocumentId)
            || string.IsNullOrWhiteSpace(Convert.ToString(rawDocumentId)))
        {
            return;
        }

        var documentId = Uri.UnescapeDataString(Convert.ToString(rawDocumentId)!);
        _ = _viewModel.LoadAsync(documentId);
    }

    private async void OnRawMessageReceived(object? sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        // Android raises HybridWebView messages on its JavaBridge thread. Any
        // resulting binding change must be marshalled to MAUI's UI thread.
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => HandleRawMessageAsync(e.Message));
        }
        catch (Exception exception)
        {
            MainThread.BeginInvokeOnMainThread(() =>
                _viewModel.ReportHostWarning($"El visor no pudo procesar un mensaje: {exception.Message}"));
        }
    }

    private async Task HandleRawMessageAsync(string? message)
    {
        _viewModel.HandleReaderMessage(message);

        if (_pendingTarget is not null && IsMessageType(message, "rendered"))
        {
            var target = _pendingTarget;
            _pendingTarget = null;
            TrySendRawMessage(JsonSerializer.Serialize(new
            {
                type = "navigate",
                payload = new { target }
            }));
        }

        if (IsReadyMessage(message))
        {
            _webViewReady = true;
            SendDocumentPayload();
            return;
        }

        if (TryGetExternalLink(message, out var uri))
        {
            var open = await DisplayAlertAsync(
                "Abrir enlace externo",
                $"El enlace se abrirá fuera del lector y puede necesitar internet.\n\n{uri.Host}",
                "Abrir",
                "Cancelar");
            if (open)
            {
                await Launcher.Default.TryOpenAsync(uri);
            }
        }
    }

    private static bool IsReadyMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        if (string.Equals(message.Trim(), "ready", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(message);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("type", out var type)
                && string.Equals(type.GetString(), "ready", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsMessageType(string? message, string expectedType)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        try
        {
            using var document = JsonDocument.Parse(message);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("type", out var type)
                && string.Equals(type.GetString(), expectedType, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
    }

    private static bool TryGetExternalLink(string? message, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type)
                || !string.Equals(type.GetString(), "link", StringComparison.OrdinalIgnoreCase)
                || !root.TryGetProperty("href", out var href)
                || !Uri.TryCreate(href.GetString(), UriKind.Absolute, out var parsed)
                || parsed.Scheme is not ("http" or "https"))
            {
                return false;
            }

            uri = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void OnWebResourceRequested(object? sender, WebViewWebResourceRequestedEventArgs e)
    {
        if (e.Uri.Scheme is not ("http" or "https")
            || e.Uri.IsLoopback
            || LocalWebViewHosts.Contains(e.Uri.Host))
        {
            return;
        }

        // The reader is deliberately local-only. Remote assets must be downloaded
        // and rewritten by the repository before a document reaches this screen.
        e.Handled = true;
        e.SetResponse(403, "Offline reader: external network requests are blocked");
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ReaderViewModel.HasContent))
        {
            SendDocumentPayload();
        }
        else if (e.PropertyName is nameof(ReaderViewModel.Mode))
        {
            SendModePayload();
        }
    }

    private void OnReaderMessageRequested(object? sender, string payload)
    {
        if (!_webViewReady || !_viewModel.HasContent)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => TrySendRawMessage(payload));
    }

    private async void SendDocumentPayload()
    {
        try
        {
            if (!_webViewReady || !_viewModel.HasContent)
            {
                return;
            }

            var generation = Interlocked.Increment(ref _payloadGeneration);
            await MainThread.InvokeOnMainThreadAsync(() =>
                TrySendRawMessage("{\"type\":\"clearResources\"}"));

            foreach (var resource in _viewModel.LocalResources)
            {
                if (generation != _payloadGeneration)
                {
                    return;
                }

                var resourcePayload = await Task.Run(() => ReaderViewModel.CreateResourcePayloadJson(resource));
                await MainThread.InvokeOnMainThreadAsync(() => TrySendRawMessage(resourcePayload));
                await Task.Yield();
            }

            if (generation == _payloadGeneration)
            {
                var payload = _viewModel.CreatePayloadJson();
                await MainThread.InvokeOnMainThreadAsync(() => TrySendRawMessage(payload));
            }
        }
        catch (Exception exception)
        {
            MainThread.BeginInvokeOnMainThread(() =>
                _viewModel.ReportHostWarning($"No se pudo completar el render local: {exception.Message}"));
        }
    }

    private void SendModePayload()
    {
        if (!_webViewReady || !_viewModel.HasContent)
        {
            return;
        }

        var payload = _viewModel.CreateModePayloadJson();
        MainThread.BeginInvokeOnMainThread(() => TrySendRawMessage(payload));
    }

    private bool TrySendRawMessage(string payload)
    {
        if (ReaderWebView.Handler is null)
        {
            return false;
        }

        try
        {
            ReaderWebView.SendRawMessage(payload);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            _viewModel.ReportHostWarning("El visor se cerró antes de terminar de cargar el documento.");
            return false;
        }
    }
}
