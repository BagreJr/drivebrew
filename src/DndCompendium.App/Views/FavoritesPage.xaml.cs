using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class FavoritesPage : ContentView
{
    private readonly FavoritesViewModel _viewModel;
#if ANDROID
    private readonly AndroidSavedItemsReorderController _reorderController;
#else
    private readonly SavedItemsPanReorderController _reorderController;
#endif

    public FavoritesPage(FavoritesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _reorderController = new(viewModel);
    }

    public Task LoadAsync() => _viewModel.LoadCommand.ExecuteAsync(null);

    private void OnFavoriteHandleLoaded(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is Button handle) _reorderController.Attach(handle);
#endif
    }

    private void OnFavoriteHandleUnloaded(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is Button handle) _reorderController.Detach(handle);
#endif
    }

    private async void OnFavoritePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
#if ANDROID
        return;
#else
        await _reorderController.HandleAsync(sender, e);
#endif
    }
}
