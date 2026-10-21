using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class SettingsPage : ContentView
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public Task LoadAsync() => _viewModel.LoadCommand.ExecuteAsync(null);

    public Task BeginUpdateAsync() => _viewModel.UpdateAppNowCommand.ExecuteAsync(null);
}
