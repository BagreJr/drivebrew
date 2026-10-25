using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class PreparedSpellsPage : ContentView
{
    private const double ReorderStep = 80;
    private readonly PreparedSpellsViewModel _viewModel;
#if !ANDROID
    private SavedItemViewModel? _pannedItem;
    private VisualElement? _pannedVisual;
    private int _panOriginIndex;
    private int _panTargetIndex;
    private double _panStep = ReorderStep;
#endif
#if ANDROID
    private const long LongPressDurationMilliseconds = 260;
    private readonly Dictionary<Android.Views.View, EventHandler<Android.Views.View.TouchEventArgs>> _androidTouchHandlers = [];
    private SavedItemViewModel? _androidPannedItem;
    private Android.Views.View? _androidDraggedView;
    private Android.Views.View? _androidScrollHost;
    private int _androidOriginIndex = -1;
    private int _androidTargetIndex = -1;
    private float _androidStartY;
    private float _androidScrolledPixels;
    private bool _androidDragActive;
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
                _androidScrolledPixels = 0;
                _androidDragActive = false;
                _androidDraggedView = FindDraggedVisual(handle)?.Handler?.PlatformView as Android.Views.View;
                _androidScrollHost = FindAndroidScrollHost(native);
                args.Handled = true;
                break;
            case Android.Views.MotionEventActions.Move:
                native.Parent?.RequestDisallowInterceptTouchEvent(true);
                if (_androidPannedItem is not null && _androidOriginIndex >= 0)
                {
                    var density = DeviceDisplay.MainDisplayInfo.Density;
                    if (!_androidDragActive && motion.EventTime - motion.DownTime >= LongPressDurationMilliseconds)
                    {
                        _androidDragActive = true;
                        native.PerformHapticFeedback(Android.Views.FeedbackConstants.LongPress);
                        if (_androidDraggedView is not null)
                        {
                            _androidDraggedView.Alpha = 0.94f;
                            _androidDraggedView.ScaleX = 1.02f;
                            _androidDraggedView.ScaleY = 1.02f;
                            _androidDraggedView.TranslationZ = (float)(12 * density);
                        }
                    }
                    if (!_androidDragActive)
                    {
                        args.Handled = true;
                        break;
                    }

                    _androidScrolledPixels += AutoScrollAndroid(_androidScrollHost, motion.RawY, density);
                    var deltaPixels = motion.RawY - _androidStartY + _androidScrolledPixels;
                    if (_androidDraggedView is not null) _androidDraggedView.TranslationY = deltaPixels;
                    var stepPixels = Math.Max(_androidDraggedView?.Height ?? 0, 48 * density);
                    var targetIndex = Math.Clamp(
                        _androidOriginIndex + (int)Math.Round(deltaPixels / stepPixels, MidpointRounding.AwayFromZero),
                        0,
                        _viewModel.Items.Count - 1);
                    _androidTargetIndex = targetIndex;
                }
                args.Handled = true;
                break;
            case Android.Views.MotionEventActions.Up:
            case Android.Views.MotionEventActions.Cancel:
                native.Parent?.RequestDisallowInterceptTouchEvent(false);
                var movedItem = _androidPannedItem;
                var movedTo = _androidTargetIndex;
                var shouldMove = motion.ActionMasked == Android.Views.MotionEventActions.Up
                    && _androidDragActive
                    && movedItem is not null
                    && movedTo >= 0
                    && movedTo != _androidOriginIndex;
                ResetAndroidDrag();
                if (shouldMove) _ = _viewModel.MoveToAsync(movedItem, _viewModel.Items[movedTo]);
                args.Handled = true;
                break;
        }
    }

    private void ResetAndroidDrag()
    {
        if (_androidDraggedView is not null)
        {
            _androidDraggedView.TranslationY = 0;
            _androidDraggedView.TranslationZ = 0;
            _androidDraggedView.ScaleX = 1;
            _androidDraggedView.ScaleY = 1;
            _androidDraggedView.Alpha = 1;
        }
        _androidPannedItem = null;
        _androidDraggedView = null;
        _androidScrollHost = null;
        _androidOriginIndex = -1;
        _androidTargetIndex = -1;
        _androidScrolledPixels = 0;
        _androidDragActive = false;
    }
#endif

    private async void OnPreparedPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
#if ANDROID
        return;
#else
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _pannedItem = (sender as PanGestureRecognizer)?.BindingContext as SavedItemViewModel;
                _panOriginIndex = _pannedItem is null ? -1 : _viewModel.Items.IndexOf(_pannedItem);
                _panTargetIndex = _panOriginIndex;
                _pannedVisual = FindDraggedVisual((sender as PanGestureRecognizer)?.Parent);
                _panStep = Math.Max(_pannedVisual?.Height ?? 0, ReorderStep);
                if (_pannedVisual is not null)
                {
                    _pannedVisual.ZIndex = 1000;
                    _pannedVisual.Scale = 1.02;
                    _pannedVisual.Opacity = 0.94;
                }
                break;
            case GestureStatus.Running when _pannedItem is not null && _panOriginIndex >= 0:
                if (_pannedVisual is not null) _pannedVisual.TranslationY = e.TotalY;
                var targetIndex = Math.Clamp(
                    _panOriginIndex + (int)Math.Round(e.TotalY / _panStep, MidpointRounding.AwayFromZero),
                    0,
                    _viewModel.Items.Count - 1);
                _panTargetIndex = targetIndex;
                break;
            case GestureStatus.Completed:
                var movedItem = _pannedItem;
                var movedTo = _panTargetIndex;
                ResetPanVisual();
                if (movedItem is not null && movedTo >= 0 && movedTo != _panOriginIndex)
                    await _viewModel.MoveToAsync(movedItem, _viewModel.Items[movedTo]);
                _pannedItem = null;
                _panOriginIndex = -1;
                _panTargetIndex = -1;
                break;
            case GestureStatus.Canceled:
                ResetPanVisual();
                _pannedItem = null;
                _panOriginIndex = -1;
                _panTargetIndex = -1;
                break;
        }
#endif
    }

#if !ANDROID
    private void ResetPanVisual()
    {
        if (_pannedVisual is not null)
        {
            _pannedVisual.TranslationY = 0;
            _pannedVisual.Scale = 1;
            _pannedVisual.Opacity = 1;
            _pannedVisual.ZIndex = 0;
        }
        _pannedVisual = null;
    }
#endif

    private static VisualElement? FindDraggedVisual(Element? element)
    {
        for (var current = element; current is not null; current = current.Parent)
            if (current is Border border) return border;
        return element as VisualElement;
    }

#if ANDROID
    private static Android.Views.View? FindAndroidScrollHost(Android.Views.View view)
    {
        for (var current = view.Parent; current is not null; current = current.Parent)
            if (current is AndroidX.RecyclerView.Widget.RecyclerView
                or AndroidX.Core.Widget.NestedScrollView)
                return current as Android.Views.View;
        return null;
    }

    private static float AutoScrollAndroid(Android.Views.View? host, float rawY, double density)
    {
        if (host is null) return 0;
        var bounds = new Android.Graphics.Rect();
        if (!host.GetGlobalVisibleRect(bounds)) return 0;
        var edge = (float)Math.Min(84 * density, bounds.Height() * 0.24);
        var amount = (int)Math.Max(8, 14 * density);
        var scroll = rawY < bounds.Top + edge ? -amount : rawY > bounds.Bottom - edge ? amount : 0;
        if (scroll == 0) return 0;
        var before = host switch
        {
            AndroidX.RecyclerView.Widget.RecyclerView recycler => recycler.ComputeVerticalScrollOffset(),
            AndroidX.Core.Widget.NestedScrollView nested => nested.ScrollY,
            _ => host.ScrollY
        };
        host.ScrollBy(0, scroll);
        var after = host switch
        {
            AndroidX.RecyclerView.Widget.RecyclerView recycler => recycler.ComputeVerticalScrollOffset(),
            AndroidX.Core.Widget.NestedScrollView nested => nested.ScrollY,
            _ => host.ScrollY
        };
        return after - before;
    }
#endif
}
