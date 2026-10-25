using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class PreparedSpellsPage : ContentView
{
    private readonly PreparedSpellsViewModel _viewModel;
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
