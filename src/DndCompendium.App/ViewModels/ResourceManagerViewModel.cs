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
    private readonly List<ResourceItemViewModel> _allResources = [];
    private readonly List<PreparedResourceItemViewModel> _allPreparedItems = [];

    public ObservableCollection<ResourceItemViewModel> SpellSlots { get; } = [];
    public ObservableCollection<ResourceItemViewModel> PactSlots { get; } = [];
    public ObservableCollection<ResourceItemViewModel> OtherResources { get; } = [];
    public ObservableCollection<PreparedResourceItemViewModel> PreparedItems { get; } = [];
    public IReadOnlyList<string> TypeOptions { get; } =
        ["Todos", "Spell Slots", "Pact Magic", "Otros recursos", "Spells preparados", "Exploits preparados"];
    public IReadOnlyList<string> RecoveryOptions { get; } =
        ["Todos", "Manual", "Descanso corto", "Descanso largo"];
    public IReadOnlyList<string> StatusOptions { get; } =
        ["Todos", "Disponibles", "Agotados", "Completos"];
    public IReadOnlyList<string> SortOptions { get; } =
        ["Manual", "A–Z", "Z–A", "Menor a mayor", "Mayor a menor"];
    public bool HasSpellSlots => SpellSlots.Count > 0;
    public bool HasPactSlots => PactSlots.Count > 0;
    public bool HasOtherResources => OtherResources.Count > 0;
    public bool HasResources => HasSpellSlots || HasPactSlots || HasOtherResources;
    public bool HasPrepared => PreparedItems.Count > 0;
    public bool HasVisibleItems => HasResources || HasPrepared;
    public bool HasStoredItems => _allResources.Count > 0 || _allPreparedItems.Count > 0;
    public bool IsEmpty => !HasVisibleItems && !IsBusy;
    public bool CanReorder => SelectedSort == "Manual";
    public bool AreResourceFiltersEnabled => SelectedType is not ("Spells preparados" or "Exploits preparados");
    public string EmptyText => HasStoredItems
        ? "No hay elementos que coincidan con los filtros actuales."
        : "Agregá recursos o prepará un spell o exploit para verlo acá.";
    public string ResultSummary
    {
        get
        {
            var count = SpellSlots.Count + PactSlots.Count + OtherResources.Count + PreparedItems.Count;
            return count == 1 ? "1 elemento" : $"{count} elementos";
        }
    }
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

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private string selectedType = "Todos";

    [ObservableProperty]
    private string selectedRecovery = "Todos";

    [ObservableProperty]
    private string selectedStatus = "Todos";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReorder))]
    private string selectedSort = "Manual";

    partial void OnSearchQueryChanged(string value) => ApplyFilters();
    partial void OnSelectedTypeChanged(string value)
    {
        OnPropertyChanged(nameof(AreResourceFiltersEnabled));
        if (!AreResourceFiltersEnabled)
        {
            SelectedRecovery = "Todos";
            SelectedStatus = "Todos";
        }
        ApplyFilters();
    }
    partial void OnSelectedRecoveryChanged(string value) => ApplyFilters();
    partial void OnSelectedStatusChanged(string value) => ApplyFilters();
    partial void OnSelectedSortChanged(string value)
    {
        ApplyFilters();
        OnPropertyChanged(nameof(CanReorder));
    }

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
        => _allResources
            .Where(slot => slot.Kind is CharacterResourceKind.SpellSlot or CharacterResourceKind.PactMagicSlot)
            .Where(slot => slot.Level >= spellLevel && slot.Current > 0)
            .OrderBy(slot => slot.Level)
            .ThenBy(slot => slot.Kind)
            .ToArray();

    public IReadOnlyList<ResourceItemViewModel> SpendableManualResources()
        => _allResources
            .Where(resource => resource.Kind == CharacterResourceKind.Custom && resource.Current > 0)
            .ToArray();

    public async Task SpendAsync(string resourceId, int amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        await profileStore.AdjustResourceAsync(resourceId, -amount);
        await ReloadResourcesAsync();
    }

    public async Task MoveResourceAsync(ResourceItemViewModel? item, ResourceItemViewModel? target)
    {
        if (!CanReorder || item is null || target is null || ReferenceEquals(item, target) || item.Kind != target.Kind) return;
        var collection = item.Kind switch
        {
            CharacterResourceKind.SpellSlot => SpellSlots,
            CharacterResourceKind.PactMagicSlot => PactSlots,
            _ => OtherResources
        };
        var sourceIndex = collection.IndexOf(item);
        var targetIndex = collection.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0) return;
        _allResources.Remove(item);
        var masterTargetIndex = _allResources.IndexOf(target);
        _allResources.Insert(sourceIndex < targetIndex ? masterTargetIndex + 1 : masterTargetIndex, item);
        ApplyFilters();
        try
        {
            await profileStore.SetResourceOrderAsync(_allResources.Select(resource => resource.Id).ToArray());
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo guardar el orden: {exception.Message}";
        }
    }

    public async Task MovePreparedAsync(PreparedResourceItemViewModel? item, PreparedResourceItemViewModel? target)
    {
        if (!CanReorder || item is null || target is null || ReferenceEquals(item, target)) return;
        var sourceIndex = PreparedItems.IndexOf(item);
        var targetIndex = PreparedItems.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0) return;
        _allPreparedItems.Remove(item);
        var masterTargetIndex = _allPreparedItems.IndexOf(target);
        _allPreparedItems.Insert(sourceIndex < targetIndex ? masterTargetIndex + 1 : masterTargetIndex, item);
        ApplyFilters();
        try
        {
            await profileStore.SetPreparedOrderAsync(_allPreparedItems.Select(entry => entry.Reference).ToArray());
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo guardar el orden: {exception.Message}";
        }
    }

    private async Task ReloadResourcesAsync()
    {
        var resources = await profileStore.GetResourcesAsync();
        _allResources.Clear();
        _allResources.AddRange(resources.Select(resource => new ResourceItemViewModel(resource)));
        ApplyFilters();
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
        _allPreparedItems.Clear();
        foreach (var reference in await profileStore.GetPreparedOrderAsync())
        {
            var key = PreparedResourceItemViewModel.ReferenceKeyFor(reference.ContentKind, reference.ItemId);
            if (byKey.Remove(key, out var item)) _allPreparedItems.Add(item);
        }
        foreach (var item in byKey.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            _allPreparedItems.Add(item);
        ApplyFilters();
    }

    private IEnumerable<ResourceItemViewModel> AllResources() => _allResources;

    private void ApplyFilters()
    {
        IEnumerable<ResourceItemViewModel> resources = _allResources;
        IEnumerable<PreparedResourceItemViewModel> prepared = _allPreparedItems;
        var query = SearchQuery.Trim();
        if (query.Length > 0)
        {
            resources = resources.Where(item => item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || item.DetailText.Contains(query, StringComparison.CurrentCultureIgnoreCase));
            prepared = prepared.Where(item => item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || item.DetailText.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        switch (SelectedType)
        {
            case "Spell Slots":
                resources = resources.Where(item => item.Kind == CharacterResourceKind.SpellSlot);
                prepared = [];
                break;
            case "Pact Magic":
                resources = resources.Where(item => item.Kind == CharacterResourceKind.PactMagicSlot);
                prepared = [];
                break;
            case "Otros recursos":
                resources = resources.Where(item => item.Kind == CharacterResourceKind.Custom);
                prepared = [];
                break;
            case "Spells preparados":
                resources = [];
                prepared = prepared.Where(item => item.IsSpell);
                break;
            case "Exploits preparados":
                resources = [];
                prepared = prepared.Where(item => !item.IsSpell);
                break;
        }

        if (SelectedRecovery != "Todos")
        {
            resources = SelectedRecovery switch
            {
                "Manual" => resources.Where(item => item.Recovery == ResourceRecoveryKind.None),
                "Descanso corto" => resources.Where(item => item.Recovery is ResourceRecoveryKind.ShortRest or ResourceRecoveryKind.ShortAndLongRest),
                "Descanso largo" => resources.Where(item => item.Recovery is ResourceRecoveryKind.LongRest or ResourceRecoveryKind.ShortAndLongRest),
                _ => resources
            };
            prepared = [];
        }

        if (SelectedStatus != "Todos")
        {
            resources = SelectedStatus switch
            {
                "Disponibles" => resources.Where(item => item.Current > 0),
                "Agotados" => resources.Where(item => item.Current == 0),
                "Completos" => resources.Where(item => item.Current == item.Maximum),
                _ => resources
            };
            prepared = [];
        }

        resources = SelectedSort switch
        {
            "A–Z" => resources.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "Z–A" => resources.OrderByDescending(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "Menor a mayor" => resources.OrderBy(item => item.Current).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "Mayor a menor" => resources.OrderByDescending(item => item.Current).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => resources
        };
        prepared = SelectedSort switch
        {
            "A–Z" => prepared.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "Z–A" => prepared.OrderByDescending(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "Menor a mayor" => prepared.OrderBy(item => item.Level).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "Mayor a menor" => prepared.OrderByDescending(item => item.Level).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => prepared
        };

        Replace(SpellSlots, resources.Where(item => item.Kind == CharacterResourceKind.SpellSlot));
        Replace(PactSlots, resources.Where(item => item.Kind == CharacterResourceKind.PactMagicSlot));
        Replace(OtherResources, resources.Where(item => item.Kind == CharacterResourceKind.Custom));
        Replace(PreparedItems, prepared);
        NotifyState();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasSpellSlots));
        OnPropertyChanged(nameof(HasPactSlots));
        OnPropertyChanged(nameof(HasOtherResources));
        OnPropertyChanged(nameof(HasResources));
        OnPropertyChanged(nameof(HasPrepared));
        OnPropertyChanged(nameof(HasVisibleItems));
        OnPropertyChanged(nameof(HasStoredItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(ResultSummary));
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
    public ResourceRecoveryKind Recovery => Resource.Recovery;
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
