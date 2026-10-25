using DndCompendium.App.ViewModels;
using DndCompendium.Core.Models;

namespace DndCompendium.App.Views;

public partial class ResourceManagerPage : ContentView
{
    private const double ReorderStep = 80;
    private static readonly string[] KnownResources =
    [
        "Spell Slots", "Pact Magic Slots", "Exploit Dice", "Sorcery Points",
        "Ki Points / Focus Points", "Bardic Inspiration", "Channel Divinity",
        "Lay on Hands", "Rage", "Wild Shape", "Action Surge", "Second Wind",
        "Hit Dice", "Recurso personalizado"
    ];
    private readonly ResourceManagerViewModel _viewModel;
#if !ANDROID
    private ResourceItemViewModel? _pannedResource;
    private VisualElement? _pannedResourceVisual;
    private int _resourceOriginIndex = -1;
    private int _resourceTargetIndex = -1;
    private double _resourcePanStep = ReorderStep;
    private PreparedResourceItemViewModel? _pannedPrepared;
    private VisualElement? _pannedPreparedVisual;
    private int _preparedOriginIndex = -1;
    private int _preparedTargetIndex = -1;
    private double _preparedPanStep = ReorderStep;
#endif
#if ANDROID
    private const long LongPressDurationMilliseconds = 260;
    private readonly Dictionary<Android.Views.View, EventHandler<Android.Views.View.TouchEventArgs>> _androidTouchHandlers = [];
    private object? _androidPannedItem;
    private Android.Views.View? _androidDraggedView;
    private Android.Views.View? _androidScrollHost;
    private int _androidOriginIndex = -1;
    private int _androidTargetIndex = -1;
    private float _androidStartY;
    private float _androidScrolledPixels;
    private bool _androidDragActive;
#endif

    public ResourceManagerPage(ResourceManagerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public Task LoadAsync() => _viewModel.LoadCommand.ExecuteAsync(null);

    private void OnResourceHandleLoaded(object? sender, EventArgs e)
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

    private void OnResourceHandleUnloaded(object? sender, EventArgs e)
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
                _androidPannedItem = handle.BindingContext;
                _androidOriginIndex = _androidPannedItem switch
                {
                    ResourceItemViewModel resource => ResourceCollectionFor(resource).IndexOf(resource),
                    PreparedResourceItemViewModel prepared => _viewModel.PreparedItems.IndexOf(prepared),
                    _ => -1
                };
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
                    var count = _androidPannedItem is ResourceItemViewModel resource
                        ? ResourceCollectionFor(resource).Count
                        : _viewModel.PreparedItems.Count;
                    var stepPixels = Math.Max(_androidDraggedView?.Height ?? 0, 48 * density);
                    var targetIndex = Math.Clamp(
                        _androidOriginIndex + (int)Math.Round(deltaPixels / stepPixels, MidpointRounding.AwayFromZero),
                        0,
                        count - 1);
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
                if (shouldMove) _ = MoveAndroidItemAsync(movedItem!, movedTo);
                args.Handled = true;
                break;
        }
    }

    private async Task MoveAndroidItemAsync(object item, int targetIndex)
    {
        if (item is ResourceItemViewModel resource)
            await _viewModel.MoveResourceAsync(resource, ResourceCollectionFor(resource)[targetIndex]);
        else if (item is PreparedResourceItemViewModel prepared)
            await _viewModel.MovePreparedAsync(prepared, _viewModel.PreparedItems[targetIndex]);
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

    private async void OnResourcePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
#if ANDROID
        return;
#else
        if (e.StatusType == GestureStatus.Started)
        {
            _pannedResource = (sender as PanGestureRecognizer)?.BindingContext as ResourceItemViewModel;
            var collection = ResourceCollectionFor(_pannedResource);
            _resourceOriginIndex = _pannedResource is null ? -1 : collection.IndexOf(_pannedResource);
            _resourceTargetIndex = _resourceOriginIndex;
            _pannedResourceVisual = FindDraggedVisual((sender as PanGestureRecognizer)?.Parent);
            _resourcePanStep = BeginPanVisual(_pannedResourceVisual);
            return;
        }
        if (e.StatusType == GestureStatus.Running && _pannedResource is not null && _resourceOriginIndex >= 0)
        {
            if (_pannedResourceVisual is not null) _pannedResourceVisual.TranslationY = e.TotalY;
            var collection = ResourceCollectionFor(_pannedResource);
            _resourceTargetIndex = Math.Clamp(
                _resourceOriginIndex + (int)Math.Round(e.TotalY / _resourcePanStep, MidpointRounding.AwayFromZero),
                0,
                collection.Count - 1);
            return;
        }
        if (e.StatusType == GestureStatus.Completed)
        {
            var movedItem = _pannedResource;
            var movedTo = _resourceTargetIndex;
            var origin = _resourceOriginIndex;
            ResetPanVisual(ref _pannedResourceVisual);
            if (movedItem is not null && movedTo >= 0 && movedTo != origin)
                await _viewModel.MoveResourceAsync(movedItem, ResourceCollectionFor(movedItem)[movedTo]);
            _pannedResource = null;
            _resourceOriginIndex = -1;
            _resourceTargetIndex = -1;
            return;
        }
        if (e.StatusType == GestureStatus.Canceled)
        {
            ResetPanVisual(ref _pannedResourceVisual);
            _pannedResource = null;
            _resourceOriginIndex = -1;
            _resourceTargetIndex = -1;
        }
#endif
    }

    private async void OnPreparedResourcePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
#if ANDROID
        return;
#else
        if (e.StatusType == GestureStatus.Started)
        {
            _pannedPrepared = (sender as PanGestureRecognizer)?.BindingContext as PreparedResourceItemViewModel;
            _preparedOriginIndex = _pannedPrepared is null ? -1 : _viewModel.PreparedItems.IndexOf(_pannedPrepared);
            _preparedTargetIndex = _preparedOriginIndex;
            _pannedPreparedVisual = FindDraggedVisual((sender as PanGestureRecognizer)?.Parent);
            _preparedPanStep = BeginPanVisual(_pannedPreparedVisual);
            return;
        }
        if (e.StatusType == GestureStatus.Running && _pannedPrepared is not null && _preparedOriginIndex >= 0)
        {
            if (_pannedPreparedVisual is not null) _pannedPreparedVisual.TranslationY = e.TotalY;
            _preparedTargetIndex = Math.Clamp(
                _preparedOriginIndex + (int)Math.Round(e.TotalY / _preparedPanStep, MidpointRounding.AwayFromZero),
                0,
                _viewModel.PreparedItems.Count - 1);
            return;
        }
        if (e.StatusType == GestureStatus.Completed)
        {
            var movedItem = _pannedPrepared;
            var movedTo = _preparedTargetIndex;
            var origin = _preparedOriginIndex;
            ResetPanVisual(ref _pannedPreparedVisual);
            if (movedItem is not null && movedTo >= 0 && movedTo != origin)
                await _viewModel.MovePreparedAsync(movedItem, _viewModel.PreparedItems[movedTo]);
            _pannedPrepared = null;
            _preparedOriginIndex = -1;
            _preparedTargetIndex = -1;
            return;
        }
        if (e.StatusType == GestureStatus.Canceled)
        {
            ResetPanVisual(ref _pannedPreparedVisual);
            _pannedPrepared = null;
            _preparedOriginIndex = -1;
            _preparedTargetIndex = -1;
        }
#endif
    }

    private static double BeginPanVisual(VisualElement? visual)
    {
        if (visual is null) return ReorderStep;
        visual.ZIndex = 1000;
        visual.Scale = 1.02;
        visual.Opacity = 0.94;
        return Math.Max(visual.Height, ReorderStep);
    }

    private static void ResetPanVisual(ref VisualElement? visual)
    {
        if (visual is not null)
        {
            visual.TranslationY = 0;
            visual.Scale = 1;
            visual.Opacity = 1;
            visual.ZIndex = 0;
        }
        visual = null;
    }

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

    private IList<ResourceItemViewModel> ResourceCollectionFor(ResourceItemViewModel? item) => item?.Kind switch
    {
        CharacterResourceKind.SpellSlot => _viewModel.SpellSlots,
        CharacterResourceKind.PactMagicSlot => _viewModel.PactSlots,
        _ => _viewModel.OtherResources
    };

    private async void OnAddClicked(object? sender, EventArgs e)
    {
        var choice = await Shell.Current.DisplayActionSheetAsync("Agregar recurso", "Cancelar", null, KnownResources);
        if (string.IsNullOrWhiteSpace(choice) || choice == "Cancelar") return;
        var kind = choice switch
        {
            "Spell Slots" => nameof(CharacterResourceKind.SpellSlot),
            "Pact Magic Slots" => nameof(CharacterResourceKind.PactMagicSlot),
            _ => nameof(CharacterResourceKind.Custom)
        };
        var name = choice == "Recurso personalizado" || kind != nameof(CharacterResourceKind.Custom) ? string.Empty : choice;
        await Shell.Current.GoToAsync($"{AppRoutes.ResourceEditor}?kind={kind}&name={Uri.EscapeDataString(name)}");
    }

    private async void OnEditClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: ResourceItemViewModel item }) return;
        await Shell.Current.GoToAsync($"{AppRoutes.ResourceEditor}?resourceId={Uri.EscapeDataString(item.Id)}");
    }

    private async void OnShortRestClicked(object? sender, EventArgs e) => await ConfirmRestAsync(RestKind.Short);
    private async void OnLongRestClicked(object? sender, EventArgs e) => await ConfirmRestAsync(RestKind.Long);

    private async Task ConfirmRestAsync(RestKind kind)
    {
        var changes = await _viewModel.PreviewRestAsync(kind);
        if (changes.Count == 0)
        {
            await Shell.Current.DisplayAlertAsync("Sin cambios", "Ningún recurso está configurado para recuperarse con este descanso o todos están completos.", "Aceptar");
            return;
        }
        var summary = string.Join('\n', changes.Select(change => $"• {change.ResourceName}: {change.Previous} → {change.Current}"));
        var title = kind == RestKind.Short ? "Descanso corto" : "Descanso largo";
        if (await Shell.Current.DisplayAlertAsync(title, $"Se aplicarán estos cambios:\n\n{summary}", "Aplicar", "Cancelar"))
            await _viewModel.ApplyRestAsync(kind);
    }

    private async void OnUsePreparedClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: PreparedResourceItemViewModel item }) return;
        if (item.IsSpell) await UseSpellAsync(item);
        else await UseExploitAsync(item);
    }

    private async Task UseSpellAsync(PreparedResourceItemViewModel item)
    {
        if (item.Level == 0)
        {
            await Shell.Current.DisplayAlertAsync(item.Name, "Los trucos no gastan slots. No se modificó ningún recurso.", "Aceptar");
            return;
        }
        var slots = _viewModel.AvailableSlotsFor(item.Level);
        if (slots.Count == 0)
        {
            await Shell.Current.DisplayAlertAsync("Sin slots", "No hay slots disponibles de un nivel válido. Podés ajustar los contadores manualmente.", "Aceptar");
            return;
        }
        var labels = slots.Select(slot => $"{slot.Name} ({slot.Current}/{slot.Maximum})").ToArray();
        var choice = await Shell.Current.DisplayActionSheetAsync($"Slot para {item.Name}", "Cancelar", null, labels);
        var index = Array.IndexOf(labels, choice);
        if (index < 0) return;
        if (await Shell.Current.DisplayAlertAsync("Confirmar gasto", $"¿Gastar 1 de {slots[index].Name}?", "Gastar", "Cancelar"))
            await _viewModel.SpendAsync(slots[index].Id, 1);
    }

    private async Task UseExploitAsync(PreparedResourceItemViewModel item)
    {
        var resources = _viewModel.SpendableManualResources();
        if (resources.Count == 0)
        {
            await Shell.Current.DisplayAlertAsync(item.Name, "El catálogo no declara un coste estructurado y no hay recursos disponibles. No se descontó nada automáticamente.", "Aceptar");
            return;
        }
        var labels = resources.Select(resource => $"{resource.Name} ({resource.Current}/{resource.Maximum})").ToArray();
        var choice = await Shell.Current.DisplayActionSheetAsync($"Recurso para {item.Name}", "Cancelar", null, labels);
        var index = Array.IndexOf(labels, choice);
        if (index < 0) return;
        var amountText = await Shell.Current.DisplayPromptAsync("Cantidad", "El JSON actual no declara el coste. Ingresá manualmente la cantidad validada por tu regla.", "Continuar", "Cancelar", "1", 3, Keyboard.Numeric, "1");
        if (!int.TryParse(amountText, out var amount) || amount <= 0 || amount > resources[index].Current) return;
        if (await Shell.Current.DisplayAlertAsync("Confirmar gasto", $"¿Gastar {amount} de {resources[index].Name}?", "Gastar", "Cancelar"))
            await _viewModel.SpendAsync(resources[index].Id, amount);
    }
}
