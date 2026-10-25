using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class PreparedSpellsPage : ContentView
{
    private readonly PreparedSpellsViewModel _viewModel;
    private SavedItemViewModel? _draggedItem;

    public PreparedSpellsPage(PreparedSpellsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public Task LoadAsync() => _viewModel.LoadCommand.ExecuteAsync(null);

    private void OnPreparedDragStarting(object? sender, DragStartingEventArgs e)
    {
        _draggedItem = (sender as DragGestureRecognizer)?.BindingContext as SavedItemViewModel;
        if (_draggedItem is null) e.Cancel = true;
    }

    private async void OnPreparedDrop(object? sender, DropEventArgs e)
    {
        var target = (sender as DropGestureRecognizer)?.BindingContext as SavedItemViewModel;
        var source = _draggedItem;
        _draggedItem = null;
        await _viewModel.MoveToAsync(source, target);
    }
}
