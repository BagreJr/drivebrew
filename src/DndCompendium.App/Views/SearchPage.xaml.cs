using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class SearchPage : ContentPage
{
    private readonly SearchViewModel _viewModel;
    public SearchPage(SearchViewModel viewModel)
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
