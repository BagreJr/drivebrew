using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.Core.Abstractions;

namespace DndCompendium.App.ViewModels;

public abstract partial class SavedItemsViewModel : ObservableObject
{
    private readonly ISpellRepository _spellRepository;
    private readonly IExploitRepository _exploitRepository;
    private readonly ICharacterProfileStore _profileStore;
    private readonly bool _preparedOnly;

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
    }

    public ObservableCollection<SavedItemViewModel> Items { get; } = [];
    public string PageTitle { get; }
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

            var entries = spells
                .Where(spell => spellIds.Contains(spell.Id))
                .Select(spell => new SavedItemViewModel(
                    spell.Id,
                    "SPELL",
                    spell.Name,
                    spell.Level == 0 ? "Truco" : $"Nivel {spell.Level}",
                    SpellDisplay.School(spell.School),
                    true))
                .Concat(exploits
                    .Where(exploit => exploitIds.Contains(exploit.Id))
                    .Select(exploit => new SavedItemViewModel(
                        exploit.Id,
                        "EXPLOIT",
                        exploit.Name,
                        $"Nivel {exploit.Level}",
                        exploit.Type,
                        false)))
                .OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            Items.Clear();
            foreach (var entry in entries) Items.Add(entry);
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

            Items.Remove(item);
            NotifyListState();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo quitar el elemento: {exception.Message}";
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

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed record SavedItemViewModel(
    string Id,
    string Kind,
    string Name,
    string LevelText,
    string DetailText,
    bool IsSpell);

public sealed class PreparedSpellsViewModel(
    ISpellRepository spellRepository,
    IExploitRepository exploitRepository,
    ICharacterProfileStore profileStore)
    : SavedItemsViewModel(spellRepository, exploitRepository, profileStore, preparedOnly: true);
