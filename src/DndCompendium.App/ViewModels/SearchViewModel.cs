using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.App.ViewModels;

public partial class SearchViewModel(IUniversalSearchService searchService) : ObservableObject
{
    private static readonly IReadOnlyDictionary<string, SearchContentKind?> CategoryMap =
        new Dictionary<string, SearchContentKind?>(StringComparer.Ordinal)
        {
            ["Todos"] = null,
            ["Documentos"] = SearchContentKind.Document,
            ["Clases"] = SearchContentKind.Class,
            ["Subclases"] = SearchContentKind.Subclass,
            ["Features de clase"] = SearchContentKind.ClassFeature,
            ["Features de subclase"] = SearchContentKind.SubclassFeature,
            ["Razas"] = SearchContentKind.Race,
            ["Features de raza"] = SearchContentKind.RaceFeature,
            ["Backgrounds"] = SearchContentKind.Background,
            ["Feats"] = SearchContentKind.Feat,
            ["Equipo"] = SearchContentKind.Equipment,
            ["Spells"] = SearchContentKind.Spell,
            ["Exploits"] = SearchContentKind.Exploit,
            ["Otras secciones"] = SearchContentKind.Other
        };

    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _filterCancellation;
    private bool _initialized;

    public ObservableCollection<SearchResultGroup> Groups { get; } = [];
    public IReadOnlyList<string> CategoryOptions { get; } = CategoryMap.Keys.ToArray();
    public IReadOnlyList<string> OriginOptions { get; private set; } = ["Todas las clases/orígenes"];
    public IReadOnlyList<string> LevelOptions { get; private set; } = ["Todos los niveles"];
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);
    public bool HasResults => Groups.Count > 0;
    public string ResultSummary => !HasQuery
        ? $"{IndexEntryCount:N0} entradas offline indexadas"
        : ResultCount == 1 ? "1 resultado" : $"{ResultCount} resultados";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuery))]
    private string query = string.Empty;

    [ObservableProperty]
    private string selectedCategory = "Todos";

    [ObservableProperty]
    private string selectedOrigin = "Todas las clases/orígenes";

    [ObservableProperty]
    private string selectedLevel = "Todos los niveles";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    private int resultCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSummary))]
    private int indexEntryCount;

    partial void OnQueryChanged(string value) => ScheduleSearch();
    partial void OnSelectedCategoryChanged(string value)
    {
        _filterCancellation?.Cancel();
        _filterCancellation?.Dispose();
        var cancellation = _filterCancellation = new CancellationTokenSource();
        _ = ChangeCategoryAsync(value, cancellation.Token);
    }

    private async Task ChangeCategoryAsync(string value, CancellationToken token)
    {
        try
        {
            await RefreshFilterOptionsAsync(value, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        ScheduleSearch(immediate: true);
    }
    partial void OnSelectedOriginChanged(string value) => ScheduleSearch(immediate: true);
    partial void OnSelectedLevelChanged(string value) => ScheduleSearch(immediate: true);

    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await searchService.RefreshIndexAsync();
            IndexEntryCount = await searchService.GetEntryCountAsync();
            await RefreshFilterOptionsAsync(SelectedCategory);
            _initialized = true;
            await SearchNowAsync(Query, SelectedCategory, CancellationToken.None);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo preparar el índice offline: {exception.Message}";
        }
        finally { IsBusy = false; OnPropertyChanged(nameof(ResultSummary)); }
    }

    [RelayCommand]
    private static Task OpenAsync(SearchResultItemViewModel? item)
    {
        if (item is null) return Task.CompletedTask;
        var entry = item.Result.Entry;
        if (entry.Kind == SearchContentKind.Spell && entry.ReferenceId is not null)
            return Shell.Current.GoToAsync($"{AppRoutes.SpellDetail}?spellId={Uri.EscapeDataString(entry.ReferenceId)}");
        if (entry.Kind == SearchContentKind.Exploit && entry.ReferenceId is not null)
            return Shell.Current.GoToAsync($"{AppRoutes.ExploitDetail}?exploitId={Uri.EscapeDataString(entry.ReferenceId)}");
        if (entry.DocumentId is null) return Task.CompletedTask;
        var target = string.IsNullOrWhiteSpace(entry.Target) ? string.Empty : $"&target={Uri.EscapeDataString(entry.Target)}";
        return Shell.Current.GoToAsync($"{AppRoutes.Reader}?documentId={Uri.EscapeDataString(entry.DocumentId)}{target}");
    }

    private void ScheduleSearch(bool immediate = false)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = _searchCancellation = new CancellationTokenSource();
        _ = RunScheduledSearchAsync(Query, SelectedCategory, immediate, cancellation.Token);
    }

    private async Task RunScheduledSearchAsync(string requestedQuery, string requestedCategory, bool immediate, CancellationToken token)
    {
        try
        {
            if (!immediate) await Task.Delay(180, token);
            if (!_initialized) return;
            await SearchNowAsync(requestedQuery, requestedCategory, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) { ErrorMessage = $"No se pudo completar la búsqueda: {exception.Message}"; }
    }

    private async Task SearchNowAsync(string requestedQuery, string requestedCategory, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(requestedQuery))
        {
            Groups.Clear();
            ResultCount = 0;
            OnPropertyChanged(nameof(HasResults));
            return;
        }
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var kind = CategoryMap.GetValueOrDefault(requestedCategory);
            var results = await searchService.SearchAsync(new SearchRequest
            {
                Query = requestedQuery,
                Kind = kind,
                Origin = SelectedOrigin == "Todas las clases/orígenes" ? null : SelectedOrigin,
                Level = SelectedLevel == "Todos los niveles" ? null : SelectedLevel,
                Limit = 120
            }, token);
            token.ThrowIfCancellationRequested();
            Groups.Clear();
            foreach (var category in results.GroupBy(result => result.Entry.Kind)
                         .OrderBy(group => CategoryOrder(group.Key)))
            {
                Groups.Add(new SearchResultGroup(CategoryLabel(category.Key), category.Select(result => new SearchResultItemViewModel(result))));
            }
            ResultCount = results.Count;
            OnPropertyChanged(nameof(HasResults));
        }
        finally { IsBusy = false; }
    }

    private async Task RefreshFilterOptionsAsync(
        string requestedCategory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = await searchService.GetFilterOptionsAsync(
                CategoryMap.GetValueOrDefault(requestedCategory), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            OriginOptions = ["Todas las clases/orígenes", .. options.Origins];
            LevelOptions = ["Todos los niveles", .. options.Levels];
            if (!OriginOptions.Contains(SelectedOrigin, StringComparer.OrdinalIgnoreCase)) SelectedOrigin = OriginOptions[0];
            if (!LevelOptions.Contains(SelectedLevel, StringComparer.OrdinalIgnoreCase)) SelectedLevel = LevelOptions[0];
            OnPropertyChanged(nameof(OriginOptions));
            OnPropertyChanged(nameof(LevelOptions));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { ErrorMessage = $"No se pudieron actualizar los filtros: {exception.Message}"; }
    }

    public static string CategoryLabel(SearchContentKind kind) => kind switch
    {
        SearchContentKind.Document => "Documentos",
        SearchContentKind.Class => "Clases",
        SearchContentKind.Subclass => "Subclases",
        SearchContentKind.ClassFeature => "Features de clase",
        SearchContentKind.SubclassFeature => "Features de subclase",
        SearchContentKind.Race => "Razas",
        SearchContentKind.RaceFeature => "Features de raza",
        SearchContentKind.Background => "Backgrounds",
        SearchContentKind.Feat => "Feats",
        SearchContentKind.Equipment => "Equipo",
        SearchContentKind.Spell => "Spells",
        SearchContentKind.Exploit => "Exploits",
        _ => "Otras secciones"
    };

    private static int CategoryOrder(SearchContentKind kind) => kind switch
    {
        SearchContentKind.Document => 0,
        SearchContentKind.Class => 1,
        SearchContentKind.Subclass => 2,
        SearchContentKind.ClassFeature => 3,
        SearchContentKind.SubclassFeature => 4,
        SearchContentKind.Race => 5,
        SearchContentKind.RaceFeature => 6,
        SearchContentKind.Background => 7,
        SearchContentKind.Feat => 8,
        SearchContentKind.Equipment => 9,
        SearchContentKind.Spell => 10,
        SearchContentKind.Exploit => 11,
        _ => 12
    };
}

public sealed class SearchResultGroup : ObservableCollection<SearchResultItemViewModel>
{
    public SearchResultGroup(string name, IEnumerable<SearchResultItemViewModel> items) : base(items) => Name = name;
    public string Name { get; }
    public string Header => $"{Name} · {Count}";
}
