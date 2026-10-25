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
#if ANDROID
    private readonly Dictionary<Android.Views.View, EventHandler<Android.Views.View.TouchEventArgs>> _androidTouchHandlers = [];
    private SavedItemViewModel? _androidPannedItem;
    private int _androidOriginIndex = -1;
    private int _androidTargetIndex = -1;
    private float _androidStartY;
    private bool _androidMovePending;
#endif

    public PreparedSpellsPage(PreparedSpellsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public Task LoadAsync() => _viewModel.LoadCommand.ExecuteAsync(null);

    private void OnPreparedHandleLoaded(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is not Button { Handler.PlatformView: Android.Views.View native } handle
            || _androidTouchHandlers.ContainsKey(native)) return;
        EventHandler<Android.Views.View.TouchEventArgs> handler = (_, args) =>
            HandleAndroidTouch(handle, native, args);
        _androidTouchHandlers[native] = handler;
        native.Touch += handler;
#endif
    }

    private void OnPreparedHandleUnloaded(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is not Button { Handler.PlatformView: Android.Views.View native }
            || !_androidTouchHandlers.Remove(native, out var handler)) return;
        native.Touch -= handler;
#endif
    }

#if ANDROID
    private void HandleAndroidTouch(
        Button handle,
        Android.Views.View native,
        Android.Views.View.TouchEventArgs args)
    {
        var motion = args.Event;
        if (motion is null) return;
        switch (motion.ActionMasked)
        {
            case Android.Views.MotionEventActions.Down:
                native.Parent?.RequestDisallowInterceptTouchEvent(true);
                _androidPannedItem = handle.BindingContext as SavedItemViewModel;
                _androidOriginIndex = _androidPannedItem is null ? -1 : _viewModel.Items.IndexOf(_androidPannedItem);
                _androidTargetIndex = _androidOriginIndex;
                _androidStartY = motion.RawY;
                args.Handled = true;
                break;
            case Android.Views.MotionEventActions.Move:
                native.Parent?.RequestDisallowInterceptTouchEvent(true);
                if (_androidPannedItem is not null && _androidOriginIndex >= 0 && !_androidMovePending)
                {
                    var density = DeviceDisplay.MainDisplayInfo.Density;
                    var delta = (motion.RawY - _androidStartY) / density;
                    var targetIndex = Math.Clamp(
                        _androidOriginIndex + (int)Math.Round(delta / ReorderStep, MidpointRounding.AwayFromZero),
                        0,
                        _viewModel.Items.Count - 1);
                    if (targetIndex != _androidTargetIndex)
                    {
                        _androidTargetIndex = targetIndex;
                        _androidMovePending = true;
                        _ = MoveAndroidPreparedAsync(_androidPannedItem, _viewModel.Items[targetIndex]);
                    }
                }
                args.Handled = true;
                break;
            case Android.Views.MotionEventActions.Up:
            case Android.Views.MotionEventActions.Cancel:
                native.Parent?.RequestDisallowInterceptTouchEvent(false);
                _androidPannedItem = null;
                _androidOriginIndex = -1;
                _androidTargetIndex = -1;
                args.Handled = true;
                break;
        }
    }

    private async Task MoveAndroidPreparedAsync(SavedItemViewModel item, SavedItemViewModel target)
    {
        try { await _viewModel.MoveToAsync(item, target); }
        finally { _androidMovePending = false; }
    }
#endif

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
