using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

#if ANDROID
internal sealed class AndroidSavedItemsReorderController(SavedItemsViewModel viewModel)
{
    private const long LongPressDurationMilliseconds = 260;
    private readonly Dictionary<Android.Views.View, EventHandler<Android.Views.View.TouchEventArgs>> _touchHandlers = [];
    private AndroidX.RecyclerView.Widget.RecyclerView? _recycler;
    private AndroidX.RecyclerView.Widget.ItemTouchHelper? _touchHelper;
    private SavedItemsReorderCallback? _reorderCallback;
    private Android.Views.View? _pendingDragHandle;
    private int _pendingDragToken;

    public void Attach(Button handle)
    {
        if (handle.Handler?.PlatformView is not Android.Views.View native
            || _touchHandlers.ContainsKey(native)) return;
        var recycler = FindRecyclerView(native);
        if (recycler is null) return;

        EventHandler<Android.Views.View.TouchEventArgs> handler = (_, args) =>
            HandleTouch(handle, native, recycler, args);
        _touchHandlers[native] = handler;
        native.Touch += handler;

        // Loaded is raised while CollectionView can still be calculating its rows.
        // ItemTouchHelper adds an item decoration, which Android forbids during layout.
        ScheduleAttach(recycler);
    }

    public void Detach(Button handle)
    {
        if (handle.Handler?.PlatformView is not Android.Views.View native
            || !_touchHandlers.Remove(native, out var handler)) return;
        native.Touch -= handler;
        if (ReferenceEquals(_pendingDragHandle, native))
        {
            _pendingDragHandle = null;
            _pendingDragToken++;
        }
    }

    private void ScheduleAttach(AndroidX.RecyclerView.Widget.RecyclerView recycler)
    {
        recycler.Post(() =>
        {
            if (!recycler.IsAttachedToWindow) return;
            if (recycler.IsComputingLayout)
            {
                ScheduleAttach(recycler);
                return;
            }
            EnsureAttached(recycler);
        });
    }

    private void EnsureAttached(AndroidX.RecyclerView.Widget.RecyclerView recycler)
    {
        if (ReferenceEquals(_recycler, recycler) && _touchHelper is not null) return;
        if (_recycler is { IsComputingLayout: false }) _touchHelper?.AttachToRecyclerView(null);
        _recycler = recycler;
        _reorderCallback = new SavedItemsReorderCallback(viewModel);
        _touchHelper = new AndroidX.RecyclerView.Widget.ItemTouchHelper(_reorderCallback);
        _touchHelper.AttachToRecyclerView(recycler);
    }

    private void HandleTouch(
        Button handle,
        Android.Views.View native,
        AndroidX.RecyclerView.Widget.RecyclerView recycler,
        Android.Views.View.TouchEventArgs args)
    {
        var motion = args.Event;
        if (motion is null) return;
        switch (motion.ActionMasked)
        {
            case Android.Views.MotionEventActions.Down:
                var token = ++_pendingDragToken;
                _pendingDragHandle = native;
                var item = handle.BindingContext as SavedItemViewModel;
                native.PostDelayed(
                    () => BeginDrag(native, recycler, item, token),
                    LongPressDurationMilliseconds);
                args.Handled = true;
                break;
            case Android.Views.MotionEventActions.Move:
                args.Handled = true;
                break;
            case Android.Views.MotionEventActions.Up:
            case Android.Views.MotionEventActions.Cancel:
                if (ReferenceEquals(_pendingDragHandle, native))
                {
                    _pendingDragHandle = null;
                    _pendingDragToken++;
                }
                args.Handled = true;
                break;
        }
    }

    private void BeginDrag(
        Android.Views.View native,
        AndroidX.RecyclerView.Widget.RecyclerView recycler,
        SavedItemViewModel? item,
        int token)
    {
        if (token != _pendingDragToken
            || !ReferenceEquals(_pendingDragHandle, native)
            || item is null) return;
        if (!ReferenceEquals(_recycler, recycler) || _touchHelper is null || _reorderCallback is null)
        {
            ScheduleAttach(recycler);
            return;
        }

        var itemView = FindRecyclerItemView(native, recycler);
        if (itemView is null) return;
        var holder = recycler.GetChildViewHolder(itemView);
        if (holder is null
            || holder.BindingAdapterPosition == AndroidX.RecyclerView.Widget.RecyclerView.NoPosition) return;

        _pendingDragHandle = null;
        _reorderCallback.Begin(item);
        native.PerformHapticFeedback(Android.Views.FeedbackConstants.LongPress);
        _touchHelper.StartDrag(holder);
    }

    private static AndroidX.RecyclerView.Widget.RecyclerView? FindRecyclerView(Android.Views.View view)
    {
        for (var current = view.Parent; current is not null; current = current.Parent)
            if (current is AndroidX.RecyclerView.Widget.RecyclerView recycler) return recycler;
        return null;
    }

    private static Android.Views.View? FindRecyclerItemView(
        Android.Views.View view,
        AndroidX.RecyclerView.Widget.RecyclerView recycler)
    {
        var current = view;
        while (current.Parent is Android.Views.View parent)
        {
            if (ReferenceEquals(parent, recycler)) return current;
            current = parent;
        }
        return null;
    }

    private sealed class SavedItemsReorderCallback(SavedItemsViewModel viewModel)
        : AndroidX.RecyclerView.Widget.ItemTouchHelper.Callback
    {
        private SavedItemViewModel? _draggedItem;
        private bool _orderChanged;

        public override bool IsLongPressDragEnabled => false;
        public override bool IsItemViewSwipeEnabled => false;

        public void Begin(SavedItemViewModel item)
        {
            _draggedItem = item;
            _orderChanged = false;
        }

        public override int GetMovementFlags(
            AndroidX.RecyclerView.Widget.RecyclerView recyclerView,
            AndroidX.RecyclerView.Widget.RecyclerView.ViewHolder viewHolder)
            => MakeMovementFlags(
                AndroidX.RecyclerView.Widget.ItemTouchHelper.Up
                    | AndroidX.RecyclerView.Widget.ItemTouchHelper.Down,
                0);

        public override bool OnMove(
            AndroidX.RecyclerView.Widget.RecyclerView recyclerView,
            AndroidX.RecyclerView.Widget.RecyclerView.ViewHolder viewHolder,
            AndroidX.RecyclerView.Widget.RecyclerView.ViewHolder target)
        {
            var targetPosition = target.BindingAdapterPosition;
            if (_draggedItem is null
                || targetPosition == AndroidX.RecyclerView.Widget.RecyclerView.NoPosition) return false;
            _orderChanged |= viewModel.MoveToIndex(_draggedItem, targetPosition);
            return true;
        }

        public override void OnSwiped(
            AndroidX.RecyclerView.Widget.RecyclerView.ViewHolder viewHolder,
            int direction)
        {
        }

        public override void OnSelectedChanged(
            AndroidX.RecyclerView.Widget.RecyclerView.ViewHolder? viewHolder,
            int actionState)
        {
            base.OnSelectedChanged(viewHolder, actionState);
            if (viewHolder is null
                || actionState != AndroidX.RecyclerView.Widget.ItemTouchHelper.ActionStateDrag) return;
            viewHolder.ItemView.Alpha = 0.94f;
            viewHolder.ItemView.ScaleX = 1.02f;
            viewHolder.ItemView.ScaleY = 1.02f;
            viewHolder.ItemView.TranslationZ = (float)(12 * DeviceDisplay.MainDisplayInfo.Density);
        }

        public override void ClearView(
            AndroidX.RecyclerView.Widget.RecyclerView recyclerView,
            AndroidX.RecyclerView.Widget.RecyclerView.ViewHolder viewHolder)
        {
            base.ClearView(recyclerView, viewHolder);
            viewHolder.ItemView.Alpha = 1;
            viewHolder.ItemView.ScaleX = 1;
            viewHolder.ItemView.ScaleY = 1;
            viewHolder.ItemView.TranslationZ = 0;
            _draggedItem = null;
            if (_orderChanged) _ = viewModel.PersistOrderAsync();
            _orderChanged = false;
        }
    }
}
#endif

#if !ANDROID
internal sealed class SavedItemsPanReorderController(SavedItemsViewModel viewModel)
{
    private const double ReorderStep = 80;
    private SavedItemViewModel? _pannedItem;
    private VisualElement? _pannedVisual;
    private int _panOriginIndex;
    private int _panTargetIndex;
    private double _panStep = ReorderStep;

    public async Task HandleAsync(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _pannedItem = (sender as PanGestureRecognizer)?.BindingContext as SavedItemViewModel;
                _panOriginIndex = _pannedItem is null ? -1 : viewModel.Items.IndexOf(_pannedItem);
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
                _panTargetIndex = Math.Clamp(
                    _panOriginIndex + (int)Math.Round(e.TotalY / _panStep, MidpointRounding.AwayFromZero),
                    0,
                    viewModel.Items.Count - 1);
                break;
            case GestureStatus.Completed:
                var movedItem = _pannedItem;
                var movedTo = _panTargetIndex;
                ResetVisual();
                if (movedItem is not null && movedTo >= 0 && movedTo != _panOriginIndex)
                    await viewModel.MoveToAsync(movedItem, viewModel.Items[movedTo]);
                ResetState();
                break;
            case GestureStatus.Canceled:
                ResetVisual();
                ResetState();
                break;
        }
    }

    private void ResetVisual()
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

    private void ResetState()
    {
        _pannedItem = null;
        _panOriginIndex = -1;
        _panTargetIndex = -1;
    }

    private static VisualElement? FindDraggedVisual(Element? element)
    {
        for (var current = element; current is not null; current = current.Parent)
            if (current is Border border) return border;
        return element as VisualElement;
    }
}
#endif
