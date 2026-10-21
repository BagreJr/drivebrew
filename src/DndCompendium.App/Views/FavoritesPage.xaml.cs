using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class FavoritesPage : ContentView
{
    private readonly FavoritesViewModel _viewModel;

    public FavoritesPage(FavoritesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public Task LoadAsync() => _viewModel.LoadCommand.ExecuteAsync(null);
}
