using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.Core.Abstractions;

namespace DndCompendium.App.ViewModels;

public abstract partial class SpellListViewModel : ObservableObject
{
    private readonly ISpellRepository _repository;
    private readonly ICharacterProfileStore _profileStore;
    private readonly bool _preparedOnly;
    private readonly bool _favoriteOnly;
    private readonly List<SpellItemViewModel> _allSpells = [];
    private string _activeCharacterName = "Mi personaje";

    protected SpellListViewModel(
        ISpellRepository repository,
        ICharacterProfileStore profileStore,
        bool preparedOnly,
        bool favoriteOnly = false)
    {
        _repository = repository;
        _profileStore = profileStore;
        _preparedOnly = preparedOnly;
        _favoriteOnly = favoriteOnly;
        PageTitle = favoriteOnly ? "Spells favoritos" : preparedOnly ? "Spells preparados" : "Spells";
    }

    public ObservableCollection<SpellItemViewModel> Spells { get; } = [];
    public string PageTitle { get; }
    public string PageSubtitle => _favoriteOnly
        ? $"{_activeCharacterName}: Spells favoritos."
        : _preparedOnly
            ? $"{_activeCharacterName}: Spells preparados."
            : $"{_allSpells.Count} conjuros del catálogo.";
    public string ActiveCharacterText => $"PERSONAJE ACTIVO: {_activeCharacterName}";
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool IsEmpty => Spells.Count == 0 && !IsBusy;
    public string ResultSummary => Spells.Count == 1 ? "1 spell" : $"{Spells.Count} spells";

    public IReadOnlyList<string> LevelOptions { get; } =
        ["Todos", "Truco (0)", "Nivel 1", "Nivel 2", "Nivel 3", "Nivel 4", "Nivel 5", "Nivel 6", "Nivel 7", "Nivel 8", "Nivel 9"];
    public IReadOnlyList<string> OriginOptions { get; } = ["Todos", "Arcano", "Divino", "Primal"];
    public IReadOnlyList<string> BooleanOptions { get; } = ["Todos", "Sí", "No"];
    public IReadOnlyList<string> CastingTimeOptions { get; } =
        ["Todos", "Action", "Bonus Action", "Reaction", "Otro"];
    public IReadOnlyList<string> ComponentOptions { get; } =
        ["Todos", "Verbal (V)", "Somático (S)", "Material (M)"];
    public IReadOnlyList<string> SortOptions { get; } =
        ["A–Z", "Z–A", "Nivel mayor a menor", "Nivel menor a mayor"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool isBusy;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private IReadOnlyList<string> classOptions = ["Todas"];

    [ObservableProperty]
    private IReadOnlyList<string> schoolOptions = ["Todas"];

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private string selectedLevel = "Todos";

    [ObservableProperty]
    private string selectedClass = "Todas";

    [ObservableProperty]
    private string selectedOrigin = "Todos";

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
    private string selectedSort = "A–Z";

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedLevelChanged(string value) => ApplyFilter();
    partial void OnSelectedClassChanged(string value) => ApplyFilter();
    partial void OnSelectedOriginChanged(string value) => ApplyFilter();
    partial void OnSelectedSchoolChanged(string value) => ApplyFilter();
    partial void OnSelectedConcentrationChanged(string value) => ApplyFilter();
    partial void OnSelectedRitualChanged(string value) => ApplyFilter();
    partial void OnSelectedCastingTimeChanged(string value) => ApplyFilter();
    partial void OnSelectedComponentChanged(string value) => ApplyFilter();
    partial void OnSelectedSortChanged(string value) => ApplyFilter();

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            _activeCharacterName = (await _profileStore.GetActiveAsync()).Name;
            var spells = await _repository.GetAllAsync(refreshFromDrive: true);
            var preparedIds = await _repository.GetPreparedIdsAsync();
            var favoriteIds = await _repository.GetFavoriteIdsAsync();
            _allSpells.Clear();
            _allSpells.AddRange(spells.Select(spell => new SpellItemViewModel(
                spell,
                preparedIds.Contains(spell.Id),
                favoriteIds.Contains(spell.Id))));
            OnPropertyChanged(nameof(PageSubtitle));
            OnPropertyChanged(nameof(ActiveCharacterText));
            ClassOptions = ["Todas", .. spells.SelectMany(spell => spell.Classes).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
            SchoolOptions = ["Todas", .. spells.Select(spell => SpellDisplay.School(spell.School)).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)];
            ApplyFilter();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo cargar spells.json: {exception.Message}";
            ApplyFilter();
        }
        finally
        {
            IsBusy = false;
            IsRefreshing = false;
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        await LoadAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task TogglePreparedAsync(SpellItemViewModel? spell)
    {
        if (spell is null) return;
        ErrorMessage = null;
        try
        {
            var next = !spell.IsPrepared;
            await _repository.SetPreparedAsync(spell.Id, next);
            spell.IsPrepared = next;
            if (_preparedOnly && !next) ApplyFilter();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo actualizar la lista preparada: {exception.Message}";
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ToggleFavoriteAsync(SpellItemViewModel? spell)
    {
        if (spell is null) return;
        ErrorMessage = null;
        try
        {
            var next = !spell.IsFavorite;
            await _repository.SetFavoriteAsync(spell.Id, next);
            spell.IsFavorite = next;
            if (_favoriteOnly && !next) ApplyFilter();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo actualizar favoritos: {exception.Message}";
        }
    }

    [RelayCommand]
    private static Task OpenDetailAsync(SpellItemViewModel? spell)
    {
        if (spell is null) return Task.CompletedTask;
        return Shell.Current.GoToAsync($"{AppRoutes.SpellDetail}?spellId={Uri.EscapeDataString(spell.Id)}");
    }

    private void ApplyFilter()
    {
        IEnumerable<SpellItemViewModel> filtered = _allSpells;
        if (_preparedOnly) filtered = filtered.Where(spell => spell.IsPrepared);
        if (_favoriteOnly) filtered = filtered.Where(spell => spell.IsFavorite);

        var query = SearchQuery.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(spell =>
                spell.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || spell.Effect.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        var level = SelectedLevel switch
        {
            "Truco (0)" => 0,
            var value when value.StartsWith("Nivel ", StringComparison.Ordinal)
                && int.TryParse(value.AsSpan(6), out var parsed) => parsed,
            _ => -1
        };
        if (level >= 0) filtered = filtered.Where(spell => spell.Level == level);
        if (SelectedClass != "Todas") filtered = filtered.Where(spell => spell.Model.Classes.Contains(SelectedClass, StringComparer.OrdinalIgnoreCase));
        if (SelectedOrigin != "Todos") filtered = filtered.Where(spell => spell.Model.Origins.Contains(SelectedOrigin, StringComparer.OrdinalIgnoreCase));
        if (SelectedSchool != "Todas") filtered = filtered.Where(spell => string.Equals(spell.SchoolText, SelectedSchool, StringComparison.CurrentCultureIgnoreCase));
        if (SelectedConcentration != "Todos") filtered = filtered.Where(spell => spell.Model.IsConcentration == (SelectedConcentration == "Sí"));
        if (SelectedRitual != "Todos") filtered = filtered.Where(spell => spell.Model.IsRitual == (SelectedRitual == "Sí"));
        if (SelectedCastingTime != "Todos")
            filtered = filtered.Where(spell => SpellDisplay.CastingTimeCategory(spell.Model.CastingTime) == SelectedCastingTime);
        if (SelectedComponent != "Todos")
        {
            filtered = SelectedComponent switch
            {
                "Verbal (V)" => filtered.Where(spell => spell.Model.HasVerbalComponent),
                "Somático (S)" => filtered.Where(spell => spell.Model.HasSomaticComponent),
                "Material (M)" => filtered.Where(spell => spell.Model.HasMaterialComponent),
                _ => filtered
            };
        }

        filtered = SelectedSort switch
        {
            "Z–A" => filtered.OrderByDescending(spell => spell.Name, StringComparer.CurrentCultureIgnoreCase),
            "Nivel mayor a menor" => filtered.OrderByDescending(spell => spell.Level).ThenBy(spell => spell.Name, StringComparer.CurrentCultureIgnoreCase),
            "Nivel menor a mayor" => filtered.OrderBy(spell => spell.Level).ThenBy(spell => spell.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => filtered.OrderBy(spell => spell.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        Spells.Clear();
        foreach (var spell in filtered) Spells.Add(spell);
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed class SpellsViewModel(
    ISpellRepository repository,
    ICharacterProfileStore profileStore)
    : SpellListViewModel(repository, profileStore, preparedOnly: false);
