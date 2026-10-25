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
            case "modifierUpsert":
                _viewModel.UpsertModifier(
                    root.TryGetProperty("id", out var modifierId) ? modifierId.GetString() : null,
                    root.TryGetProperty("sourceName", out var sourceName) ? sourceName.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("target", out var target) ? target.GetString() ?? string.Empty : string.Empty,
                    root.TryGetProperty("value", out var modifierValue) && modifierValue.TryGetDecimal(out var decimalValue) ? decimalValue : 0,
                    root.TryGetProperty("bonusType", out var bonusType) ? bonusType.GetString() : null,
                    root.TryGetProperty("condition", out var condition) ? condition.GetString() : null,
                    root.TryGetProperty("duration", out var duration) ? duration.GetString() : null,
                    !root.TryGetProperty("isActive", out var active) || active.GetBoolean());
                break;
            case "modifierDelete":
                if (root.TryGetProperty("id", out var removedId)) _viewModel.RemoveModifier(removedId.GetString() ?? string.Empty);
                break;
            case "adjustResource":
                if (root.TryGetProperty("id", out var resourceId)
                    && root.TryGetProperty("delta", out var delta)
                    && delta.TryGetInt32(out var resourceDelta))
                    await _viewModel.AdjustResourceAsync(resourceId.GetString() ?? string.Empty, resourceDelta);
                break;
            case "setPreparedSpell":
                if (root.TryGetProperty("id", out var spellId))
                    await _viewModel.SetPreparedSpellAsync(
                        spellId.GetString() ?? string.Empty,
                        !root.TryGetProperty("prepared", out var prepared) || prepared.GetBoolean());
                break;
            case "setKnownSpell":
                if (root.TryGetProperty("id", out var knownSpellId))
                    _viewModel.SetKnownSpell(
                        knownSpellId.GetString() ?? string.Empty,
                        !root.TryGetProperty("known", out var known) || known.GetBoolean());
                break;
            case "spellSearch":
                var spellQuery = StringProperty(root, "query");
                int? spellLevel = root.TryGetProperty("level", out var levelElement)
                    && levelElement.ValueKind == JsonValueKind.Number
                    && levelElement.TryGetInt32(out var parsedLevel)
                        ? parsedLevel
                        : null;
                SheetWebView.SendRawMessage(await _viewModel.SearchSpellsPayloadJsonAsync(spellQuery, spellLevel));
                break;
            case "openSpell":
                if (root.TryGetProperty("id", out var openedSpellId))
                    await Shell.Current.GoToAsync(
                        $"{AppRoutes.SpellDetail}?spellId={Uri.EscapeDataString(openedSpellId.GetString() ?? string.Empty)}");
                break;
            case "openSpells":
                await Shell.Current.GoToAsync("//tabs/spells");
                break;
            case "featureSearch":
                var featureQuery = root.TryGetProperty("query", out var query) ? query.GetString() ?? string.Empty : string.Empty;
                SheetWebView.SendRawMessage(await _viewModel.SearchFeaturesPayloadJsonAsync(featureQuery));
                break;
            case "identitySearch":
                var identityQuery = root.TryGetProperty("query", out var identity) ? identity.GetString() ?? string.Empty : string.Empty;
                SheetWebView.SendRawMessage(await _viewModel.SearchIdentityPayloadJsonAsync(identityQuery));
                break;
            case "featureAdd":
                _viewModel.AddFeatureReference(
                    StringProperty(root, "id"),
                    StringProperty(root, "name"),
                    StringProperty(root, "kind"),
                    NullableStringProperty(root, "sourceKey"),
                    NullableStringProperty(root, "documentId"),
                    NullableStringProperty(root, "target"),
                    NullableStringProperty(root, "level"));
                break;
            case "featureDelete":
                _viewModel.RemoveFeatureReference(StringProperty(root, "id"));
                break;
            case "openFeature":
                var documentId = NullableStringProperty(root, "documentId");
                if (!string.IsNullOrWhiteSpace(documentId))
                {
                    var featureTarget = NullableStringProperty(root, "target");
                    var targetQuery = string.IsNullOrWhiteSpace(featureTarget)
                        ? string.Empty
                        : $"&target={Uri.EscapeDataString(featureTarget)}";
                    await Shell.Current.GoToAsync(
                        $"{AppRoutes.Reader}?documentId={Uri.EscapeDataString(documentId)}{targetQuery}");
                }
                break;
            case "error":
                _viewModel.ReportEditorError(
                    root.TryGetProperty("message", out var errorElement)
                        ? errorElement.GetString() ?? "Error desconocido"
                        : "Error desconocido");
                break;
        }
    }

    private static string StringProperty(JsonElement root, string name)
        => root.TryGetProperty(name, out var property) ? property.GetString() ?? string.Empty : string.Empty;

    private static string? NullableStringProperty(JsonElement root, string name)
        => root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

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

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_webViewReady || string.IsNullOrWhiteSpace(_characterId)) return;
        await _viewModel.RefreshSessionDataAsync();
        SendSheetPayload();
    }
}
