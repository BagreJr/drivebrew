using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.App.ViewModels;

public abstract partial class SavedItemsViewModel : ObservableObject
{
    private static readonly string[] ExploitClasses =
        ["Barbarian", "Fighter", "Gunslinger", "Rogue", "Warden", "Warlord"];
    private readonly ISpellRepository _spellRepository;
    private readonly IExploitRepository _exploitRepository;
    private readonly ICharacterProfileStore _profileStore;
    private readonly bool _preparedOnly;
    private readonly List<SavedItemViewModel> _allItems = [];

    protected SavedItemsViewModel(
        ISpellRepository spellRepository,
        IExploitRepository exploitRepository,
        ICharacterProfileStore profileStore,
        bool preparedOnly)
    {
        _spellRepository = spellRepository;
        _exploitRepository = exploitRepository;
        _profileStore = profileStore;
        _preparedOnly = preparedOnly;
        PageTitle = preparedOnly ? "Preparados" : "Favoritos";
        pageSubtitle = preparedOnly
            ? "Tus spells y exploits preparados, disponibles offline."
            : "Tus spells y exploits favoritos, disponibles offline.";
        selectedSort = "Manual";
    }

    public ObservableCollection<SavedItemViewModel> Items { get; } = [];
    public string PageTitle { get; }
    public IReadOnlyList<string> ContentOptions { get; } = ["Todos", "Spells", "Exploits"];
    public IReadOnlyList<string> SpellLevelOptions { get; } =
        ["Todos", "Truco (0)", "Nivel 1", "Nivel 2", "Nivel 3", "Nivel 4", "Nivel 5", "Nivel 6", "Nivel 7", "Nivel 8", "Nivel 9"];
    public IReadOnlyList<string> OriginOptions { get; } = ["Todos", "Arcano", "Divino", "Primal"];
    public IReadOnlyList<string> BooleanOptions { get; } = ["Todos", "Sí", "No"];
    public IReadOnlyList<string> CastingTimeOptions { get; } = ["Todos", "Action", "Bonus Action", "Reaction", "Otro"];
    public IReadOnlyList<string> ComponentOptions { get; } = ["Todos", "Verbal (V)", "Somático (S)", "Material (M)"];
    public IReadOnlyList<string> SortOptions { get; } = ["Manual", "A–Z", "Z–A", "Nivel menor a mayor", "Nivel mayor a menor"];
    public bool ShowSpellFilters => SelectedContent == "Spells";
    public bool ShowExploitFilters => SelectedContent == "Exploits";
    public bool CanUseExploitSubfilters => ShowExploitFilters && SelectedExploitType != "Todos";
    public bool CanChooseExploitClass => ShowExploitFilters && SelectedExploitType == "Exploit";
    public bool CanReorder => SelectedSort == "Manual";

    [ObservableProperty]
    private string pageSubtitle;

    public string ResultSummary => Items.Count == 1 ? "1 elemento" : $"{Items.Count} elementos";
    public string EmptyText => _preparedOnly
        ? "Prepará un spell o exploit y aparecerá acá."
        : "Marcá un spell o exploit como favorito y aparecerá acá.";
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsEmpty => Items.Count == 0 && !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool isBusy;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSpellFilters))]
    [NotifyPropertyChangedFor(nameof(ShowExploitFilters))]
    [NotifyPropertyChangedFor(nameof(CanUseExploitSubfilters))]
    [NotifyPropertyChangedFor(nameof(CanChooseExploitClass))]
    private string selectedContent = "Todos";

    [ObservableProperty]
    private string selectedSpellLevel = "Todos";

    [ObservableProperty]
    private IReadOnlyList<string> spellClassOptions = ["Todas"];

    [ObservableProperty]
    private string selectedSpellClass = "Todas";

    [ObservableProperty]
    private string selectedOrigin = "Todos";

    [ObservableProperty]
    private IReadOnlyList<string> schoolOptions = ["Todas"];

    [ObservableProperty]
    private string selectedSchool = "Todas";

    [ObservableProperty]
    private string selectedConcentration = "Todos";

    [ObservableProperty]
    private string selectedRitual = "Todos";

    [ObservableProperty]
    private string selectedCastingTime = "Todos";

    [ObservableProperty]
    private string selectedComponent = "Todos";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseExploitSubfilters))]
    [NotifyPropertyChangedFor(nameof(CanChooseExploitClass))]
    private string selectedExploitType = "Todos";

    [ObservableProperty]
    private IReadOnlyList<string> exploitTypeOptions = ["Todos"];

    [ObservableProperty]
    private string selectedExploitClass = "Todas";

    [ObservableProperty]
    private IReadOnlyList<string> exploitLevelOptions = ["Todos"];

    [ObservableProperty]
    private string selectedExploitLevel = "Todos";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReorder))]
    private string selectedSort;

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedContentChanged(string value) => ApplyFilter();
    partial void OnSelectedSpellLevelChanged(string value) => ApplyFilter();
    partial void OnSelectedSpellClassChanged(string value) => ApplyFilter();
    partial void OnSelectedOriginChanged(string value) => ApplyFilter();
    partial void OnSelectedSchoolChanged(string value) => ApplyFilter();
    partial void OnSelectedConcentrationChanged(string value) => ApplyFilter();
    partial void OnSelectedRitualChanged(string value) => ApplyFilter();
    partial void OnSelectedCastingTimeChanged(string value) => ApplyFilter();
    partial void OnSelectedComponentChanged(string value) => ApplyFilter();
    partial void OnSelectedExploitTypeChanged(string value)
    {
        OnPropertyChanged(nameof(CanUseExploitSubfilters));
        OnPropertyChanged(nameof(CanChooseExploitClass));
        SelectedExploitClass = FixedClassForType(value) ?? "Todas";
        SelectedExploitLevel = "Todos";
        ApplyFilter();
    }
    partial void OnSelectedExploitClassChanged(string value)
    {
        if (!GetExploitLevelFilterOptions().Any(option => option.Value == SelectedExploitLevel && option.IsAvailable))
            SelectedExploitLevel = "Todos";
        ApplyFilter();
    }
    partial void OnSelectedExploitLevelChanged(string value)
    {
        if (CanChooseExploitClass
            && !GetExploitClassFilterOptions().Any(option => option.Value == SelectedExploitClass && option.IsAvailable))
            SelectedExploitClass = "Todas";
        ApplyFilter();
    }
    partial void OnSelectedSortChanged(string value)
    {
        ApplyFilter();
        OnPropertyChanged(nameof(CanReorder));
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var activeCharacter = await _profileStore.GetActiveAsync();
            PageSubtitle = _preparedOnly
                ? $"{activeCharacter.Name}: Spells y Exploits preparados."
                : $"{activeCharacter.Name}: Spells y Exploits favoritos.";
            var spells = await _spellRepository.GetAllAsync(refreshFromDrive: true);
            var exploits = await _exploitRepository.GetAllAsync(refreshFromDrive: true);
            var spellIds = _preparedOnly
                ? await _spellRepository.GetPreparedIdsAsync()
                : await _spellRepository.GetFavoriteIdsAsync();
            var exploitIds = _preparedOnly
                ? await _exploitRepository.GetPreparedIdsAsync()
                : await _exploitRepository.GetFavoriteIdsAsync();

            var loaded = spells
                .Where(spell => spellIds.Contains(spell.Id))
                .Select(SavedItemViewModel.FromSpell)
                .Concat(exploits
                    .Where(exploit => exploitIds.Contains(exploit.Id))
                    .Select(SavedItemViewModel.FromExploit))
                .ToArray();

            _allItems.Clear();
            var savedOrder = _preparedOnly
                ? await _profileStore.GetPreparedOrderAsync()
                : await _profileStore.GetFavoriteOrderAsync();
            var byKey = loaded.ToDictionary(item => item.ReferenceKey, StringComparer.Ordinal);
            foreach (var reference in savedOrder)
            {
                var key = SavedItemViewModel.ReferenceKeyFor(reference.ContentKind, reference.ItemId);
                if (byKey.Remove(key, out var item)) _allItems.Add(item);
            }
            _allItems.AddRange(byKey.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase));

            SpellClassOptions = ["Todas", .. _allItems
                .Where(item => item.IsSpell)
                .SelectMany(item => item.Classes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)];
            if (!SpellClassOptions.Contains(SelectedSpellClass, StringComparer.OrdinalIgnoreCase)) SelectedSpellClass = "Todas";
            SchoolOptions = ["Todas", .. _allItems
                .Where(item => item.IsSpell)
                .Select(item => item.School)
                .Where(school => !string.IsNullOrWhiteSpace(school))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase)];
            if (!SchoolOptions.Contains(SelectedSchool, StringComparer.CurrentCultureIgnoreCase)) SelectedSchool = "Todas";

            var preferredTypes = new[] { "Knacks", "Techniques", "Blood Rites", "Exploit", "Maneuvers" };
            ExploitTypeOptions = ["Todos", .. preferredTypes.Where(type => _allItems.Any(item =>
                !item.IsSpell && string.Equals(item.ExploitType, type, StringComparison.OrdinalIgnoreCase)))];
            ExploitLevelOptions = ["Todos", .. _allItems
                .Where(item => !item.IsSpell)
                .Select(item => item.Level)
                .Distinct()
                .Order()
                .Select(level => $"Nivel {level}")];
            if (!ExploitTypeOptions.Contains(SelectedExploitType, StringComparer.OrdinalIgnoreCase))
                SelectedExploitType = "Todos";
            ApplyFilter();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo cargar la lista: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
            NotifyListState();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RemoveAsync(SavedItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            if (item.IsSpell)
            {
                if (_preparedOnly) await _spellRepository.SetPreparedAsync(item.Id, false);
                else await _spellRepository.SetFavoriteAsync(item.Id, false);
            }
            else
            {
                if (_preparedOnly) await _exploitRepository.SetPreparedAsync(item.Id, false);
                else await _exploitRepository.SetFavoriteAsync(item.Id, false);
            }

            _allItems.Remove(item);
            Items.Remove(item);
            NotifyListState();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo quitar el elemento: {exception.Message}";
        }
    }

    public async Task MoveToAsync(SavedItemViewModel? item, SavedItemViewModel? target)
    {
        if (!CanReorder || item is null || target is null || ReferenceEquals(item, target)) return;
        var targetVisibleIndex = Items.IndexOf(target);
        if (!MoveToIndex(item, targetVisibleIndex)) return;
        await PersistOrderAsync();
    }

    public bool MoveToIndex(SavedItemViewModel? item, int targetVisibleIndex)
    {
        if (!CanReorder || item is null || Items.Count == 0) return false;
        var visibleIndex = Items.IndexOf(item);
        targetVisibleIndex = Math.Clamp(targetVisibleIndex, 0, Items.Count - 1);
        if (visibleIndex < 0 || visibleIndex == targetVisibleIndex) return false;

        var target = Items[targetVisibleIndex];
        _allItems.Remove(item);
        var targetIndex = _allItems.IndexOf(target);
        _allItems.Insert(visibleIndex < targetVisibleIndex ? targetIndex + 1 : targetIndex, item);
        Items.Move(visibleIndex, targetVisibleIndex);
        return true;
    }

    public async Task PersistOrderAsync()
    {
        if (!CanReorder) return;
        try
        {
            var order = _allItems.Select(entry => entry.Reference).ToArray();
            if (_preparedOnly) await _profileStore.SetPreparedOrderAsync(order);
            else await _profileStore.SetFavoriteOrderAsync(order);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo guardar el orden: {exception.Message}";
        }
    }

    [RelayCommand]
    private static Task OpenDetailAsync(SavedItemViewModel? item)
    {
        if (item is null) return Task.CompletedTask;
        var route = item.IsSpell ? AppRoutes.SpellDetail : AppRoutes.ExploitDetail;
        var parameter = item.IsSpell ? "spellId" : "exploitId";
        return Shell.Current.GoToAsync($"{route}?{parameter}={Uri.EscapeDataString(item.Id)}");
    }

    private void ApplyFilter()
    {
        IEnumerable<SavedItemViewModel> filtered = _allItems;
        var query = SearchQuery.Trim();
        if (query.Length > 0)
        {
            filtered = filtered.Where(item => item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || item.DetailText.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || item.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }
        if (SelectedContent == "Spells")
        {
            filtered = filtered.Where(item => item.IsSpell);
            var level = ParseLevel(SelectedSpellLevel);
            if (SelectedSpellLevel == "Truco (0)") level = 0;
            if (level is not null) filtered = filtered.Where(item => item.Level == level);
            if (SelectedSpellClass != "Todas")
                filtered = filtered.Where(item => item.Classes.Contains(SelectedSpellClass, StringComparer.OrdinalIgnoreCase));
            if (SelectedOrigin != "Todos")
                filtered = filtered.Where(item => item.Origins.Contains(SelectedOrigin, StringComparer.OrdinalIgnoreCase));
            if (SelectedSchool != "Todas")
                filtered = filtered.Where(item => string.Equals(item.School, SelectedSchool, StringComparison.CurrentCultureIgnoreCase));
            if (SelectedConcentration != "Todos")
                filtered = filtered.Where(item => item.IsConcentration == (SelectedConcentration == "Sí"));
            if (SelectedRitual != "Todos")
                filtered = filtered.Where(item => item.IsRitual == (SelectedRitual == "Sí"));
            if (SelectedCastingTime != "Todos")
                filtered = filtered.Where(item => item.CastingTimeCategory == SelectedCastingTime);
            if (SelectedComponent != "Todos")
            {
                filtered = SelectedComponent switch
                {
                    "Verbal (V)" => filtered.Where(item => item.HasVerbalComponent),
                    "Somático (S)" => filtered.Where(item => item.HasSomaticComponent),
                    "Material (M)" => filtered.Where(item => item.HasMaterialComponent),
                    _ => filtered
                };
            }
        }
        else if (SelectedContent == "Exploits")
        {
            filtered = filtered.Where(item => !item.IsSpell);
            if (SelectedExploitType != "Todos")
                filtered = filtered.Where(item => string.Equals(item.ExploitType, SelectedExploitType, StringComparison.OrdinalIgnoreCase));
            if (SelectedExploitClass != "Todas")
                filtered = filtered.Where(item => item.Classes.Contains(SelectedExploitClass, StringComparer.OrdinalIgnoreCase));
            if (ParseLevel(SelectedExploitLevel) is int exploitLevel)
                filtered = filtered.Where(item => item.Level == exploitLevel);
        }

        filtered = SelectedSort switch
        {
            "A–Z" => filtered.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "Z–A" => filtered.OrderByDescending(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "Nivel menor a mayor" => filtered.OrderBy(item => item.Level).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            "Nivel mayor a menor" => filtered.OrderByDescending(item => item.Level).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => filtered
        };

        Items.Clear();
        foreach (var item in filtered) Items.Add(item);
        NotifyListState();
    }

    public IReadOnlyList<SavedFilterOption> GetExploitClassFilterOptions()
    {
        var entries = ExploitsForSelectedType().ToArray();
        var selectedLevel = ParseLevel(SelectedExploitLevel);
        return [new("Todas", CanChooseExploitClass), .. ExploitClasses.Select(option => new SavedFilterOption(
            option,
            CanChooseExploitClass && entries.Any(item =>
                (selectedLevel is null || item.Level == selectedLevel)
                && item.Classes.Contains(option, StringComparer.OrdinalIgnoreCase))))];
    }

    public IReadOnlyList<SavedFilterOption> GetExploitLevelFilterOptions()
    {
        var entries = ExploitsForSelectedType().ToArray();
        return ExploitLevelOptions
            .Select(option => new SavedFilterOption(
                option,
                CanUseExploitSubfilters && (option == "Todos" || ParseLevel(option) is int level && entries.Any(item =>
                    item.Level == level
                    && (SelectedExploitClass == "Todas" || item.Classes.Contains(SelectedExploitClass, StringComparer.OrdinalIgnoreCase))))))
            .ToArray();
    }

    private IEnumerable<SavedItemViewModel> ExploitsForSelectedType()
        => SelectedExploitType == "Todos"
            ? []
            : _allItems.Where(item => !item.IsSpell
                && string.Equals(item.ExploitType, SelectedExploitType, StringComparison.OrdinalIgnoreCase));

    private static string? FixedClassForType(string type) => type switch
    {
        "Knacks" => "Ranger",
        "Techniques" => "Monk",
        "Blood Rites" => "Blood Hunter",
        "Maneuvers" => "Vagabond",
        _ => null
    };

    private static int? ParseLevel(string option)
        => option.StartsWith("Nivel ", StringComparison.Ordinal)
           && int.TryParse(option.AsSpan(6), out var level)
            ? level
            : null;

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed record SavedFilterOption(string Value, bool IsAvailable);

public partial class SavedItemViewModel : ObservableObject
{
    private SavedItemViewModel(
        string id,
        string kind,
        string name,
        int level,
        string levelText,
        string detailText,
        bool isSpell,
        IReadOnlyList<string> classes,
        string searchText,
        IReadOnlyList<string>? origins = null,
        string school = "",
        bool isConcentration = false,
        bool isRitual = false,
        string castingTimeCategory = "",
        bool hasVerbalComponent = false,
        bool hasSomaticComponent = false,
        bool hasMaterialComponent = false,
        string exploitType = "")
    {
        Id = id;
        Kind = kind;
        Name = name;
        Level = level;
        LevelText = levelText;
        DetailText = detailText;
        IsSpell = isSpell;
        Classes = classes;
        SearchText = searchText;
        Origins = origins ?? [];
        School = school;
        IsConcentration = isConcentration;
        IsRitual = isRitual;
        CastingTimeCategory = castingTimeCategory;
        HasVerbalComponent = hasVerbalComponent;
        HasSomaticComponent = hasSomaticComponent;
        HasMaterialComponent = hasMaterialComponent;
        ExploitType = exploitType;
    }

    public string Id { get; }
    public string Kind { get; }
    public string Name { get; }
    public int Level { get; }
    public string LevelText { get; }
    public string DetailText { get; }
    public bool IsSpell { get; }
    public IReadOnlyList<string> Classes { get; }
    public string SearchText { get; }
    public IReadOnlyList<string> Origins { get; }
    public string School { get; }
    public bool IsConcentration { get; }
    public bool IsRitual { get; }
    public string CastingTimeCategory { get; }
    public bool HasVerbalComponent { get; }
    public bool HasSomaticComponent { get; }
    public bool HasMaterialComponent { get; }
    public string ExploitType { get; }
    public SavedContentKind ContentKind => IsSpell ? SavedContentKind.Spell : SavedContentKind.Exploit;
    public SavedItemReference Reference => new(ContentKind, Id);
    public string ReferenceKey => ReferenceKeyFor(ContentKind, Id);

    public static SavedItemViewModel FromSpell(Spell spell) => new(
        spell.Id,
        "SPELL",
        spell.Name,
        spell.Level,
        spell.Level == 0 ? "Truco" : $"Nivel {spell.Level}",
        SpellDisplay.School(spell.School),
        true,
        spell.Classes,
        $"{spell.Effect} {spell.HigherLevels}",
        spell.Origins,
        SpellDisplay.School(spell.School),
        spell.IsConcentration,
        spell.IsRitual,
        SpellDisplay.CastingTimeCategory(spell.CastingTime),
        spell.HasVerbalComponent,
        spell.HasSomaticComponent,
        spell.HasMaterialComponent);

    public static SavedItemViewModel FromExploit(ExploitEntry exploit) => new(
        exploit.Id,
        "EXPLOIT",
        exploit.Name,
        exploit.Level,
        $"Nivel {exploit.Level}",
        exploit.Type,
        false,
        exploit.Classes,
        $"{exploit.Prerequisite} {exploit.Effect}",
        exploitType: exploit.Type);

    public static string ReferenceKeyFor(SavedContentKind contentKind, string id)
        => $"{contentKind}:{id}";
}

public sealed class PreparedSpellsViewModel(
    ISpellRepository spellRepository,
    IExploitRepository exploitRepository,
    ICharacterProfileStore profileStore)
    : SavedItemsViewModel(spellRepository, exploitRepository, profileStore, preparedOnly: true);
