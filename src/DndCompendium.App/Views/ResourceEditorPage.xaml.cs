using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class ResourceEditorPage : ContentPage, IQueryAttributable
{
    private readonly ResourceEditorViewModel _viewModel;

    public ResourceEditorPage(ResourceEditorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        static string? Value(IDictionary<string, object> values, string key)
            => values.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(Convert.ToString(raw))
                ? Uri.UnescapeDataString(Convert.ToString(raw)!)
                : null;
        _ = _viewModel.LoadAsync(Value(query, "resourceId"), Value(query, "name"), Value(query, "kind"));
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (await DisplayAlertAsync("Eliminar recurso", "¿Eliminar este recurso del personaje activo?", "Eliminar", "Cancelar"))
            await _viewModel.DeleteAsync();
    }
}
