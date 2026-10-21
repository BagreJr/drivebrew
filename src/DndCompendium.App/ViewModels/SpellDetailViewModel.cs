using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.Core.Abstractions;

namespace DndCompendium.App.ViewModels;

public partial class SpellDetailViewModel(ISpellRepository repository) : ObservableObject
{
    public bool HasSpell => Spell is not null;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSpell))]
    private SpellItemViewModel? spell;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    public async Task LoadAsync(string spellId)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var spells = await repository.GetAllAsync(refreshFromDrive: false);
            var model = spells.FirstOrDefault(spell => spell.Id == spellId)
                ?? throw new KeyNotFoundException("El spell no existe en el catálogo local.");
            var prepared = await repository.GetPreparedIdsAsync();
            var favorites = await repository.GetFavoriteIdsAsync();
            Spell = new SpellItemViewModel(model, prepared.Contains(model.Id), favorites.Contains(model.Id));
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo abrir el spell: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task TogglePreparedAsync()
    {
        if (Spell is null) return;
        var next = !Spell.IsPrepared;
        await repository.SetPreparedAsync(Spell.Id, next);
        Spell.IsPrepared = next;
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ToggleFavoriteAsync()
    {
        if (Spell is null) return;
        var next = !Spell.IsFavorite;
        await repository.SetFavoriteAsync(Spell.Id, next);
        Spell.IsFavorite = next;
    }
}
