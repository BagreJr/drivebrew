using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class PreparedSpellsPage : ContentView
{
    private const double ReorderStep = 80;
    private readonly PreparedSpellsViewModel _viewModel;
    private SavedItemViewModel? _pannedItem;
    private int _panOriginIndex;
    private int _panTargetIndex;
    private bool _panMovePending;

    public PreparedSpellsPage(PreparedSpellsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public Task LoadAsync() => _viewModel.LoadCommand.ExecuteAsync(null);

    private async void OnPreparedPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _pannedItem = (sender as PanGestureRecognizer)?.BindingContext as SavedItemViewModel;
                _panOriginIndex = _pannedItem is null ? -1 : _viewModel.Items.IndexOf(_pannedItem);
                _panTargetIndex = _panOriginIndex;
                break;
            case GestureStatus.Running when _pannedItem is not null && _panOriginIndex >= 0 && !_panMovePending:
                var targetIndex = Math.Clamp(
                    _panOriginIndex + (int)Math.Round(e.TotalY / ReorderStep, MidpointRounding.AwayFromZero),
                    0,
                    _viewModel.Items.Count - 1);
                if (targetIndex == _panTargetIndex) break;
                _panTargetIndex = targetIndex;
                _panMovePending = true;
                try { await _viewModel.MoveToAsync(_pannedItem, _viewModel.Items[targetIndex]); }
                finally { _panMovePending = false; }
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _pannedItem = null;
                _panOriginIndex = -1;
                _panTargetIndex = -1;
                break;
        }
    }
}
