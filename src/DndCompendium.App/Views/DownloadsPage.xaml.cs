using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class DownloadsPage : ContentView
{
    private readonly DownloadsViewModel _viewModel;

    public DownloadsPage(DownloadsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public Task LoadAsync() => _viewModel.LoadCommand.ExecuteAsync(null);
}
