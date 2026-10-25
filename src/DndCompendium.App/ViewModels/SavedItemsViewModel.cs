using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.App.ViewModels;

public abstract partial class SavedItemsViewModel : ObservableObject
{
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
        selectedSort = preparedOnly ? "Manual" : "A–Z";
    }

    public ObservableCollection<SavedItemViewModel> Items { get; } = [];
    public string PageTitle { get; }
    public IReadOnlyList<string> ContentOptions { get; } = ["Todos", "Spells", "Exploits"];
    public IReadOnlyList<string> LevelOptions { get; } =
        ["Todos", "Truco (0)", "Nivel 1", "Nivel 2", "Nivel 3", "Nivel 4", "Nivel 5", "Nivel 6", "Nivel 7", "Nivel 8", "Nivel 9"];
    public IReadOnlyList<string> CastingTimeOptions { get; } = ["Todos", "Action", "Bonus Action", "Reaction", "Otro"];
    public IReadOnlyList<string> ComponentOptions { get; } = ["Todos", "Verbal (V)", "Somático (S)", "Material (M)"];
    public IReadOnlyList<string> SortOptions { get; } = ["Manual", "A–Z", "Z–A", "Nivel menor a mayor", "Nivel mayor a menor"];
    public bool ShowPreparedFilters => _preparedOnly;
    public bool CanReorder => _preparedOnly && SelectedSort == "Manual";

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
    private string selectedContent = "Todos";

    [ObservableProperty]
    private string selectedLevel = "Todos";

    [ObservableProperty]
    private IReadOnlyList<string> classOptions = ["Todas"];

    [ObservableProperty]
    private string selectedClass = "Todas";

    [ObservableProperty]
    private string selectedCastingTime = "Todos";

    [ObservableProperty]
    private string selectedComponent = "Todos";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReorder))]
    private string selectedSort;

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedContentChanged(string value) => ApplyFilter();
    partial void OnSelectedLevelChanged(string value) => ApplyFilter();
    partial void OnSelectedClassChanged(string value) => ApplyFilter();
    partial void OnSelectedCastingTimeChanged(string value) => ApplyFilter();
    partial void OnSelectedComponentChanged(string value) => ApplyFilter();
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
            if (_preparedOnly)
            {
                var byKey = loaded.ToDictionary(item => item.ReferenceKey, StringComparer.Ordinal);
                foreach (var reference in await _profileStore.GetPreparedOrderAsync())
                {
                    var key = SavedItemViewModel.ReferenceKeyFor(reference.ContentKind, reference.ItemId);
                    if (byKey.Remove(key, out var item)) _allItems.Add(item);
                }
                _allItems.AddRange(byKey.Values.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase));
            }
            else
            {
                _allItems.AddRange(loaded.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase));
            }

            ClassOptions = ["Todas", .. _allItems
                .SelectMany(item => item.Classes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)];
            if (!ClassOptions.Contains(SelectedClass, StringComparer.OrdinalIgnoreCase)) SelectedClass = "Todas";
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
        if (!_preparedOnly || !CanReorder || item is null || target is null || ReferenceEquals(item, target)) return;
        var visibleIndex = Items.IndexOf(item);
        var targetVisibleIndex = Items.IndexOf(target);
        if (visibleIndex < 0 || targetVisibleIndex < 0) return;

        _allItems.Remove(item);
        var targetIndex = _allItems.IndexOf(target);
        _allItems.Insert(visibleIndex < targetVisibleIndex ? targetIndex + 1 : targetIndex, item);
        Items.Move(visibleIndex, targetVisibleIndex);

        try
        {
            await _profileStore.SetPreparedOrderAsync(_allItems.Select(entry => entry.Reference).ToArray());
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
                || item.DetailText.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }
        if (SelectedContent == "Spells") filtered = filtered.Where(item => item.IsSpell);
        if (SelectedContent == "Exploits") filtered = filtered.Where(item => !item.IsSpell);

        var level = SelectedLevel switch
        {
            "Truco (0)" => 0,
            var value when value.StartsWith("Nivel ", StringComparison.Ordinal)
                && int.TryParse(value.AsSpan(6), out var parsed) => parsed,
            _ => -1
        };
        if (level >= 0) filtered = filtered.Where(item => item.Level == level);
        if (SelectedClass != "Todas")
            filtered = filtered.Where(item => item.Classes.Contains(SelectedClass, StringComparer.OrdinalIgnoreCase));
        if (SelectedCastingTime != "Todos")
            filtered = filtered.Where(item => item.IsSpell && item.CastingTimeCategory == SelectedCastingTime);
        if (SelectedComponent != "Todos")
        {
            filtered = SelectedComponent switch
            {
                "Verbal (V)" => filtered.Where(item => item.IsSpell && item.HasVerbalComponent),
                "Somático (S)" => filtered.Where(item => item.IsSpell && item.HasSomaticComponent),
                "Material (M)" => filtered.Where(item => item.IsSpell && item.HasMaterialComponent),
                _ => filtered
            };
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

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

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
        string castingTimeCategory = "",
        bool hasVerbalComponent = false,
        bool hasSomaticComponent = false,
        bool hasMaterialComponent = false)
    {
        Id = id;
        Kind = kind;
        Name = name;
        Level = level;
        LevelText = levelText;
        DetailText = detailText;
        IsSpell = isSpell;
        Classes = classes;
        CastingTimeCategory = castingTimeCategory;
        HasVerbalComponent = hasVerbalComponent;
        HasSomaticComponent = hasSomaticComponent;
        HasMaterialComponent = hasMaterialComponent;
    }

    public string Id { get; }
    public string Kind { get; }
    public string Name { get; }
    public int Level { get; }
    public string LevelText { get; }
    public string DetailText { get; }
    public bool IsSpell { get; }
    public IReadOnlyList<string> Classes { get; }
    public string CastingTimeCategory { get; }
    public bool HasVerbalComponent { get; }
    public bool HasSomaticComponent { get; }
    public bool HasMaterialComponent { get; }
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
        exploit.Classes);

    public static string ReferenceKeyFor(SavedContentKind contentKind, string id)
        => $"{contentKind}:{id}";
}

public sealed class PreparedSpellsViewModel(
    ISpellRepository spellRepository,
    IExploitRepository exploitRepository,
    ICharacterProfileStore profileStore)
    : SavedItemsViewModel(spellRepository, exploitRepository, profileStore, preparedOnly: true);
