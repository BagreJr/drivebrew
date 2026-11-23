using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class PreparedSpellsPage : ContentView
{
    private readonly PreparedSpellsViewModel _viewModel;
    private Action<string>? _selectFilterOption;
#if ANDROID
    private readonly AndroidSavedItemsReorderController _reorderController;
#else
    private readonly SavedItemsPanReorderController _reorderController;
#endif

    public PreparedSpellsPage(PreparedSpellsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _reorderController = new(viewModel);
    }

    public Task LoadAsync() => _viewModel.LoadCommand.ExecuteAsync(null);

    private void OnExploitTypeFilterClicked(object? sender, EventArgs e)
        => ShowFilterMenu("Tipo",
            _viewModel.ExploitTypeOptions.Select(value => new SavedFilterOption(value, true)),
            value => _viewModel.SelectedExploitType = value);

    private void OnExploitClassFilterClicked(object? sender, EventArgs e)
        => ShowFilterMenu("Clase", _viewModel.GetExploitClassFilterOptions(),
            value => _viewModel.SelectedExploitClass = value);

    private void OnExploitLevelFilterClicked(object? sender, EventArgs e)
        => ShowFilterMenu("Nivel", _viewModel.GetExploitLevelFilterOptions(),
            value => _viewModel.SelectedExploitLevel = value);

    private void ShowFilterMenu(string title, IEnumerable<SavedFilterOption> options, Action<string> select)
    {
        FilterMenuTitle.Text = title;
        FilterOptionsList.ItemsSource = options.ToArray();
        _selectFilterOption = select;
        FilterOverlay.IsVisible = true;
    }

    private void OnFilterOptionClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string value }) _selectFilterOption?.Invoke(value);
        CloseFilterMenu();
    }

    private void OnCloseFilterMenuClicked(object? sender, EventArgs e) => CloseFilterMenu();

    private void CloseFilterMenu()
    {
        FilterOverlay.IsVisible = false;
        _selectFilterOption = null;
    }

    private void OnPreparedHandleLoaded(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is Button handle) _reorderController.Attach(handle);
#endif
    }

    private void OnPreparedHandleUnloaded(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is Button handle) _reorderController.Detach(handle);
#endif
    }

    private async void OnPreparedPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
#if ANDROID
        return;
#else
        await _reorderController.HandleAsync(sender, e);
#endif
    }
}
