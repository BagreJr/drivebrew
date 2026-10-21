using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class SpellDetailPage : ContentPage, IQueryAttributable
{
    private readonly SpellDetailViewModel _viewModel;

    public SpellDetailPage(SpellDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("spellId", out var raw) && !string.IsNullOrWhiteSpace(Convert.ToString(raw)))
        {
            _ = _viewModel.LoadAsync(Uri.UnescapeDataString(Convert.ToString(raw)!));
        }
    }
}
