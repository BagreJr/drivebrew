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
    private ResourceItemViewModel? _pannedResource;
    private int _resourceOriginIndex = -1;
    private int _resourceTargetIndex = -1;
    private bool _resourceMovePending;
    private PreparedResourceItemViewModel? _pannedPrepared;
    private int _preparedOriginIndex = -1;
    private int _preparedTargetIndex = -1;
    private bool _preparedMovePending;
#if ANDROID
    private readonly Dictionary<Android.Views.View, EventHandler<Android.Views.View.TouchEventArgs>> _androidTouchHandlers = [];
    private object? _androidPannedItem;
    private int _androidOriginIndex = -1;
    private int _androidTargetIndex = -1;
    private float _androidStartY;
    private bool _androidMovePending;
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
                args.Handled = true;
                break;
            case Android.Views.MotionEventActions.Move:
                native.Parent?.RequestDisallowInterceptTouchEvent(true);
                if (_androidPannedItem is not null && _androidOriginIndex >= 0 && !_androidMovePending)
                {
                    var density = DeviceDisplay.MainDisplayInfo.Density;
                    var delta = (motion.RawY - _androidStartY) / density;
                    var count = _androidPannedItem is ResourceItemViewModel resource
                        ? ResourceCollectionFor(resource).Count
                        : _viewModel.PreparedItems.Count;
                    var targetIndex = Math.Clamp(
                        _androidOriginIndex + (int)Math.Round(delta / ReorderStep, MidpointRounding.AwayFromZero),
                        0,
                        count - 1);
                    if (targetIndex != _androidTargetIndex)
                    {
                        _androidTargetIndex = targetIndex;
                        _androidMovePending = true;
                        _ = MoveAndroidItemAsync(_androidPannedItem, targetIndex);
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

    private async Task MoveAndroidItemAsync(object item, int targetIndex)
    {
        try
        {
            if (item is ResourceItemViewModel resource)
                await _viewModel.MoveResourceAsync(resource, ResourceCollectionFor(resource)[targetIndex]);
            else if (item is PreparedResourceItemViewModel prepared)
                await _viewModel.MovePreparedAsync(prepared, _viewModel.PreparedItems[targetIndex]);
        }
        finally { _androidMovePending = false; }
    }
#endif

    private async void OnResourcePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType == GestureStatus.Started)
        {
            _pannedResource = (sender as PanGestureRecognizer)?.BindingContext as ResourceItemViewModel;
            var collection = ResourceCollectionFor(_pannedResource);
            _resourceOriginIndex = _pannedResource is null ? -1 : collection.IndexOf(_pannedResource);
            _resourceTargetIndex = _resourceOriginIndex;
            return;
        }
        if (e.StatusType == GestureStatus.Running && _pannedResource is not null && _resourceOriginIndex >= 0 && !_resourceMovePending)
        {
            var collection = ResourceCollectionFor(_pannedResource);
            var targetIndex = Math.Clamp(
                _resourceOriginIndex + (int)Math.Round(e.TotalY / ReorderStep, MidpointRounding.AwayFromZero),
                0,
                collection.Count - 1);
            if (targetIndex == _resourceTargetIndex) return;
            _resourceTargetIndex = targetIndex;
            _resourceMovePending = true;
            try { await _viewModel.MoveResourceAsync(_pannedResource, collection[targetIndex]); }
            finally { _resourceMovePending = false; }
            return;
        }
        if (e.StatusType is GestureStatus.Completed or GestureStatus.Canceled)
        {
            _pannedResource = null;
            _resourceOriginIndex = -1;
            _resourceTargetIndex = -1;
        }
    }

    private async void OnPreparedResourcePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType == GestureStatus.Started)
        {
            _pannedPrepared = (sender as PanGestureRecognizer)?.BindingContext as PreparedResourceItemViewModel;
            _preparedOriginIndex = _pannedPrepared is null ? -1 : _viewModel.PreparedItems.IndexOf(_pannedPrepared);
            _preparedTargetIndex = _preparedOriginIndex;
            return;
        }
        if (e.StatusType == GestureStatus.Running && _pannedPrepared is not null && _preparedOriginIndex >= 0 && !_preparedMovePending)
        {
            var targetIndex = Math.Clamp(
                _preparedOriginIndex + (int)Math.Round(e.TotalY / ReorderStep, MidpointRounding.AwayFromZero),
                0,
                _viewModel.PreparedItems.Count - 1);
            if (targetIndex == _preparedTargetIndex) return;
            _preparedTargetIndex = targetIndex;
            _preparedMovePending = true;
            try { await _viewModel.MovePreparedAsync(_pannedPrepared, _viewModel.PreparedItems[targetIndex]); }
            finally { _preparedMovePending = false; }
            return;
        }
        if (e.StatusType is GestureStatus.Completed or GestureStatus.Canceled)
        {
            _pannedPrepared = null;
            _preparedOriginIndex = -1;
            _preparedTargetIndex = -1;
        }
    }

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
