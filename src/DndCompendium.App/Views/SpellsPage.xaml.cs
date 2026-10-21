using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class SpellsPage : ContentPage
{
    private readonly SpellsViewModel _viewModel;

    public SpellsPage(SpellsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
