using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using Microsoft.Maui.Storage;

namespace DndCompendium.App.ViewModels;

public partial class ReaderViewModel : ObservableObject
{
    private const string ModePreference = "reader.mode.v2";
    private const string ZoomPreference = "reader.zoom";
    private const string FontScalePreference = "reader.fontScale";
    private readonly ILocalDocumentRepository _repository;
    private readonly IDocumentResourceManager _resourceManager;
    private readonly IReadingHistoryStore _readingHistory;
    private CancellationTokenSource? _loadCancellation;
    private string? _documentId;

    public ReaderViewModel(
        ILocalDocumentRepository repository,
        IDocumentResourceManager resourceManager,
        IReadingHistoryStore readingHistory)
    {
        _repository = repository;
        _resourceManager = resourceManager;
        _readingHistory = readingHistory;
        mode = Preferences.Default.Get(ModePreference, "mobile");
        zoom = Math.Clamp(Preferences.Default.Get(ZoomPreference, 1d), 0.35, 2.5);
        fontScale = Math.Clamp(Preferences.Default.Get(FontScalePreference, 1d), 0.75, 1.8);
    }

    public event EventHandler<string>? ReaderMessageRequested;

    public bool HasContent => SourceText is not null;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasRendererWarning => !string.IsNullOrWhiteSpace(RendererWarning);

    public bool IsBookMode => Mode == "book";

    public bool IsMobileMode => Mode == "mobile";

    public bool CanGoPrevious => HasContent && CurrentPage > 1;

    public bool CanGoNext => HasContent && PageCount > 0 && CurrentPage < PageCount;

    public string ModeDescription => IsBookMode
        ? "Diseño Homebrewery original"
        : "Una columna adaptable";

    public string PageLabel => PageCount > 0 ? $"{CurrentPage} / {PageCount}" : "— / —";

    public string ZoomLabel => $"{Zoom:P0}";

    public string FontScaleLabel => $"{FontScale:P0}";

    public IReadOnlyList<LocalDocumentResource> LocalResources { get; private set; } = [];

    [ObservableProperty]
    private string title = "Lector";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContent))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private string? sourceText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRendererWarning))]
    private string? rendererWarning;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string offlineStatus = "Comprobando recursos offline…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBookMode))]
    [NotifyPropertyChangedFor(nameof(IsMobileMode))]
    [NotifyPropertyChangedFor(nameof(ModeDescription))]
    private string mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int currentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int pageCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomLabel))]
    private double zoom;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FontScaleLabel))]
    private double fontScale;

    public async Task LoadAsync(string documentId)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;

        IsBusy = true;
        ErrorMessage = null;
        RendererWarning = null;
        SourceText = null;
        LocalResources = [];
        OfflineStatus = "Comprobando recursos offline…";
        PageCount = 0;
        CurrentPage = 1;
        _documentId = documentId;

        try
        {
            await _repository.InitializeAsync(cancellationToken);
            var content = await _repository.ReadAsync(documentId, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (content is null)
            {
                ErrorMessage = "Este documento no tiene una copia local válida. Descargalo antes de abrirlo.";
                return;
            }

            Title = content.Title;
            var manifest = await _resourceManager.GetManifestAsync(documentId, cancellationToken);
            LocalResources = await _resourceManager.ReadAvailableAsync(documentId, cancellationToken);
            OfflineStatus = manifest.OfflineState switch
            {
                DocumentOfflineState.Complete => "Offline completo",
                DocumentOfflineState.Pending => $"{manifest.RequiredCount - manifest.AvailableCount} recurso(s) pendiente(s)",
                _ => $"Disponible parcialmente: faltan {manifest.RequiredCount - manifest.AvailableCount} recurso(s)"
            };
            SourceText = content.SourceText;
            try
            {
                await _readingHistory.RecordOpenedAsync(documentId, content.Title, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Reading history is optional and must never prevent the document from opening.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer navigation request superseded this load.
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo abrir el documento local: {exception.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) IsBusy = false;
        }
    }

    public string CreatePayloadJson() => JsonSerializer.Serialize(new
    {
        type = "document",
        payload = new
        {
            id = _documentId,
            title = Title,
            content = SourceText ?? string.Empty,
            mode = Mode,
            zoom = Zoom,
            fontScale = FontScale
        }
    });

    public string CreateModePayloadJson() => JsonSerializer.Serialize(new
    {
        type = "mode",
        payload = new { mode = Mode, zoom = Zoom, fontScale = FontScale }
    });

    public static string CreateResourcePayloadJson(LocalDocumentResource resource)
        => JsonSerializer.Serialize(new
        {
            type = "resource",
            payload = new
            {
                source = resource.SourceUrl,
                mimeType = resource.MimeType,
                base64 = Convert.ToBase64String(resource.Content)
            }
        });

    public void HandleReaderMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeProperty)) return;
            switch (typeProperty.GetString()?.ToLowerInvariant())
            {
                case "rendered":
                    if (root.TryGetProperty("pageCount", out var renderedCount)) PageCount = Math.Max(0, renderedCount.GetInt32());
                    CurrentPage = PageCount > 0 ? 1 : 0;
                    RendererWarning = BuildRendererWarning(root);
                    break;
                case "pagechanged":
                    if (root.TryGetProperty("pageCount", out var count)) PageCount = Math.Max(0, count.GetInt32());
                    if (root.TryGetProperty("page", out var page)) CurrentPage = Math.Clamp(page.GetInt32(), 1, Math.Max(1, PageCount));
                    break;
                case "modechanged":
                    if (root.TryGetProperty("zoom", out var reportedZoom)) Zoom = reportedZoom.GetDouble();
                    if (root.TryGetProperty("fontScale", out var reportedScale)) FontScale = reportedScale.GetDouble();
                    break;
                case "error":
                    if (root.TryGetProperty("message", out var error)) RendererWarning = $"El visor informó un problema: {error.GetString()}";
                    break;
                case "resourcemissing":
                case "resourceerror":
                    RendererWarning = "Este documento todavía tiene uno o más recursos visuales no disponibles offline.";
                    break;
            }
        }
        catch (JsonException)
        {
            // Ignore malformed renderer diagnostics; the local document remains readable.
        }
    }

    public void ReportHostWarning(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            RendererWarning = message;
        }
    }

    partial void OnModeChanged(string value)
    {
        Preferences.Default.Set(ModePreference, value);
    }

    partial void OnZoomChanged(double value)
    {
        Preferences.Default.Set(ZoomPreference, value);
    }

    partial void OnFontScaleChanged(double value)
    {
        Preferences.Default.Set(FontScalePreference, value);
    }

    [RelayCommand]
    private void UseBookMode() => Mode = "book";

    [RelayCommand]
    private void UseMobileMode() => Mode = "mobile";

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void PreviousPage() => NavigateToPage(CurrentPage - 1);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextPage() => NavigateToPage(CurrentPage + 1);

    [RelayCommand]
    private void ZoomOut() => SetZoom(Zoom - 0.1);

    [RelayCommand]
    private void ZoomIn() => SetZoom(Zoom + 0.1);

    [RelayCommand]
    private void ResetZoom() => SetZoom(1);

    [RelayCommand]
    private void DecreaseFont() => SetFontScale(FontScale - 0.1);

    [RelayCommand]
    private void IncreaseFont() => SetFontScale(FontScale + 0.1);

    [RelayCommand]
    private void ResetFont() => SetFontScale(1);

    private void NavigateToPage(int page)
    {
        if (PageCount <= 0) return;
        var target = Math.Clamp(page, 1, PageCount);
        ReaderMessageRequested?.Invoke(this, JsonSerializer.Serialize(new
        {
            type = "navigate",
            payload = new { page = target }
        }));
    }

    private void SetZoom(double value)
    {
        Zoom = Math.Round(Math.Clamp(value, 0.35, 2.5), 2);
        ReaderMessageRequested?.Invoke(this, JsonSerializer.Serialize(new
        {
            type = "zoom",
            payload = new { zoom = Zoom }
        }));
    }

    private void SetFontScale(double value)
    {
        FontScale = Math.Round(Math.Clamp(value, 0.75, 1.8), 2);
        ReaderMessageRequested?.Invoke(this, JsonSerializer.Serialize(new
        {
            type = "fontScale",
            payload = new { fontScale = FontScale }
        }));
    }

    private static string? BuildRendererWarning(JsonElement root)
    {
        var pageErrors = root.TryGetProperty("pageErrors", out var errors) && errors.ValueKind == JsonValueKind.Array
            ? errors.GetArrayLength()
            : 0;
        var diagnostics = root.TryGetProperty("diagnostics", out var notes) && notes.ValueKind == JsonValueKind.Array
            ? notes.GetArrayLength()
            : 0;
        if (pageErrors > 0) return $"{pageErrors} página(s) no pudieron interpretarse por completo.";
        if (diagnostics > 0) return "El documento contiene metadata o CSS con advertencias; se conservó el contenido legible.";
        return null;
    }
}
