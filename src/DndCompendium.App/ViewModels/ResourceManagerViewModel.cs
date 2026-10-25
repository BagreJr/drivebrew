using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.App.ViewModels;

public partial class ResourceManagerViewModel(
    ICharacterProfileStore profileStore,
    ISpellRepository spellRepository,
    IExploitRepository exploitRepository) : ObservableObject
{
    public ObservableCollection<ResourceItemViewModel> SpellSlots { get; } = [];
    public ObservableCollection<ResourceItemViewModel> PactSlots { get; } = [];
    public ObservableCollection<ResourceItemViewModel> OtherResources { get; } = [];
    public ObservableCollection<PreparedResourceItemViewModel> PreparedItems { get; } = [];
    public bool HasSpellSlots => SpellSlots.Count > 0;
    public bool HasPactSlots => PactSlots.Count > 0;
    public bool HasOtherResources => OtherResources.Count > 0;
    public bool HasResources => HasSpellSlots || HasPactSlots || HasOtherResources;
    public bool IsEmpty => !HasResources && !IsBusy;
    public bool HasPrepared => PreparedItems.Count > 0;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string ActiveSummary => string.IsNullOrWhiteSpace(ActiveCharacterName)
        ? "Cargando personaje…"
        : $"Recursos de {ActiveCharacterName}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveSummary))]
    private string activeCharacterName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var active = await profileStore.GetActiveAsync();
            ActiveCharacterName = active.Name;
            await ReloadResourcesAsync();
            await ReloadPreparedAsync();
        }
        catch (Exception exception) { ErrorMessage = $"No se pudieron cargar los recursos: {exception.Message}"; }
        finally { IsBusy = false; NotifyState(); }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task AdjustAsync(ResourceAdjustment? adjustment)
    {
        if (adjustment is null) return;
        try
        {
            ErrorMessage = null;
            await profileStore.AdjustResourceAsync(adjustment.Resource.Id, adjustment.Delta);
            await ReloadResourcesAsync();
        }
        catch (Exception exception) { ErrorMessage = exception.Message; }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ToggleSlotAsync(ResourceSlotDotViewModel? dot)
    {
        if (dot is null) return;
        try
        {
            var current = AllResources().FirstOrDefault(item => item.Id == dot.ResourceId)?.Current ?? 0;
            var target = dot.IsAvailable ? dot.Position - 1 : dot.Position;
            await profileStore.AdjustResourceAsync(dot.ResourceId, target - current);
            await ReloadResourcesAsync();
        }
        catch (Exception exception) { ErrorMessage = exception.Message; }
    }

    [RelayCommand]
    private static Task OpenPreparedAsync(PreparedResourceItemViewModel? item)
    {
        if (item is null) return Task.CompletedTask;
        var route = item.IsSpell ? AppRoutes.SpellDetail : AppRoutes.ExploitDetail;
        var parameter = item.IsSpell ? "spellId" : "exploitId";
        return Shell.Current.GoToAsync($"{route}?{parameter}={Uri.EscapeDataString(item.Id)}");
    }

    public Task<IReadOnlyList<ResourceRestChange>> PreviewRestAsync(RestKind kind)
        => profileStore.PreviewRestAsync(kind);

    public async Task ApplyRestAsync(RestKind kind)
    {
        await profileStore.ApplyRestAsync(kind);
        await ReloadResourcesAsync();
    }

    public IReadOnlyList<ResourceItemViewModel> AvailableSlotsFor(int spellLevel)
        => SpellSlots.Concat(PactSlots)
            .Where(slot => slot.Level >= spellLevel && slot.Current > 0)
            .OrderBy(slot => slot.Level)
            .ThenBy(slot => slot.Kind)
            .ToArray();

    public IReadOnlyList<ResourceItemViewModel> SpendableManualResources()
        => OtherResources.Where(resource => resource.Current > 0).ToArray();

    public async Task SpendAsync(string resourceId, int amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        await profileStore.AdjustResourceAsync(resourceId, -amount);
        await ReloadResourcesAsync();
    }

    public async Task MoveResourceAsync(ResourceItemViewModel? item, ResourceItemViewModel? target)
    {
        if (item is null || target is null || ReferenceEquals(item, target) || item.Kind != target.Kind) return;
        var collection = item.Kind switch
        {
            CharacterResourceKind.SpellSlot => SpellSlots,
            CharacterResourceKind.PactMagicSlot => PactSlots,
            _ => OtherResources
        };
        var sourceIndex = collection.IndexOf(item);
        var targetIndex = collection.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0) return;
        collection.Move(sourceIndex, targetIndex);
        try
        {
            await profileStore.SetResourceOrderAsync(AllResources().Select(resource => resource.Id).ToArray());
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo guardar el orden: {exception.Message}";
        }
    }

    public async Task MovePreparedAsync(PreparedResourceItemViewModel? item, PreparedResourceItemViewModel? target)
    {
        if (item is null || target is null || ReferenceEquals(item, target)) return;
        var sourceIndex = PreparedItems.IndexOf(item);
        var targetIndex = PreparedItems.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0) return;
        PreparedItems.Move(sourceIndex, targetIndex);
        try
        {
            await profileStore.SetPreparedOrderAsync(PreparedItems.Select(entry => entry.Reference).ToArray());
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo guardar el orden: {exception.Message}";
        }
    }

    private async Task ReloadResourcesAsync()
    {
        var resources = await profileStore.GetResourcesAsync();
        SpellSlots.Clear();
        PactSlots.Clear();
        OtherResources.Clear();
        foreach (var resource in resources)
        {
            var item = new ResourceItemViewModel(resource);
            switch (resource.Kind)
            {
                case CharacterResourceKind.SpellSlot: SpellSlots.Add(item); break;
                case CharacterResourceKind.PactMagicSlot: PactSlots.Add(item); break;
                default: OtherResources.Add(item); break;
            }
        }
        NotifyState();
    }

    private async Task ReloadPreparedAsync()
    {
        var spellIds = await spellRepository.GetPreparedIdsAsync();
        var exploitIds = await exploitRepository.GetPreparedIdsAsync();
        var spells = await spellRepository.GetAllAsync(false);
        var exploits = await exploitRepository.GetAllAsync(false);
        var loaded = spells.Where(item => spellIds.Contains(item.Id))
            .Select(spell => new PreparedResourceItemViewModel(spell.Id, spell.Name, true, spell.Level, spell.Level == 0 ? "Truco" : $"Spell · Nivel {spell.Level}"))
            .Concat(exploits.Where(item => exploitIds.Contains(item.Id))
                .Select(exploit => new PreparedResourceItemViewModel(exploit.Id, exploit.Name, false, exploit.Level, $"{exploit.Type} · Nivel {exploit.Level}")))
            .ToArray();
        var byKey = loaded.ToDictionary(item => item.ReferenceKey, StringComparer.Ordinal);
        PreparedItems.Clear();
        foreach (var reference in await profileStore.GetPreparedOrderAsync())
        {
            var key = PreparedResourceItemViewModel.ReferenceKeyFor(reference.ContentKind, reference.ItemId);
            if (byKey.Remove(key, out var item)) PreparedItems.Add(item);
        }
        foreach (var item in byKey.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            PreparedItems.Add(item);
        OnPropertyChanged(nameof(HasPrepared));
    }

    private IEnumerable<ResourceItemViewModel> AllResources() => SpellSlots.Concat(PactSlots).Concat(OtherResources);

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasSpellSlots));
        OnPropertyChanged(nameof(HasPactSlots));
        OnPropertyChanged(nameof(HasOtherResources));
        OnPropertyChanged(nameof(HasResources));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed class ResourceItemViewModel
{
    public ResourceItemViewModel(CharacterResource resource)
    {
        Resource = resource;
        Dots = Enumerable.Range(1, resource.Maximum)
            .Select(position => new ResourceSlotDotViewModel(resource.Id, position, position <= resource.Current))
            .ToArray();
    }
    public CharacterResource Resource { get; }
    public string Id => Resource.Id;
    public string Name => Resource.Name;
    public CharacterResourceKind Kind => Resource.Kind;
    public int? Level => Resource.Level;
    public int Current => Resource.Current;
    public int Maximum => Resource.Maximum;
    public string CountText => $"{Current} / {Maximum}{(string.IsNullOrWhiteSpace(Resource.Unit) ? string.Empty : $" {Resource.Unit}")}";
    public string DetailText => string.Join(" · ", new[] { RecoveryText, Resource.Description }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string RecoveryText => Resource.Recovery switch
    {
        ResourceRecoveryKind.ShortRest => RecoveryAmount("Descanso corto"),
        ResourceRecoveryKind.LongRest => RecoveryAmount("Descanso largo"),
        ResourceRecoveryKind.ShortAndLongRest => RecoveryAmount("Descanso corto y largo"),
        _ => "Recuperación manual"
    };
    public IReadOnlyList<ResourceSlotDotViewModel> Dots { get; }
    public ResourceAdjustment Spend => new(this, -1);
    public ResourceAdjustment Recover => new(this, 1);
    private string RecoveryAmount(string label) => Resource.RecoveryAmount is int amount ? $"{label}: +{amount}" : $"{label}: completo";
}

public sealed record ResourceSlotDotViewModel(string ResourceId, int Position, bool IsAvailable)
{
    public string Glyph => IsAvailable ? "●" : "○";
    public string AccessibilityText => IsAvailable ? $"Slot {Position} disponible" : $"Slot {Position} gastado";
}

public sealed record ResourceAdjustment(ResourceItemViewModel Resource, int Delta);
public sealed record PreparedResourceItemViewModel(string Id, string Name, bool IsSpell, int Level, string DetailText)
{
    public string KindText => IsSpell ? "SPELL" : "EXPLOIT";
    public string UseText => IsSpell ? (Level == 0 ? "Usar truco" : "Gastar slot…") : "Gastar recurso…";
    public SavedItemReference Reference => new(IsSpell ? SavedContentKind.Spell : SavedContentKind.Exploit, Id);
    public string ReferenceKey => ReferenceKeyFor(Reference.ContentKind, Id);
    public static string ReferenceKeyFor(SavedContentKind contentKind, string id) => $"{contentKind}:{id}";
}
