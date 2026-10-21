using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.App.ViewModels;

public partial class CharacterProfilesViewModel(ICharacterProfileStore store) : ObservableObject
{
    public ObservableCollection<CharacterProfileItemViewModel> Characters { get; } = [];
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string ActiveSummary => string.IsNullOrWhiteSpace(ActiveCharacterName)
        ? "Cargando personaje…"
        : $"Personaje activo: {ActiveCharacterName}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveSummary))]
    private string activeCharacterName = string.Empty;

    [ObservableProperty]
    private string newCharacterName = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudieron cargar los personajes: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCharacterName))
        {
            ErrorMessage = "Escribí un nombre para el personaje.";
            return;
        }

        try
        {
            ErrorMessage = null;
            await store.CreateAsync(NewCharacterName);
            NewCharacterName = string.Empty;
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SelectAsync(CharacterProfileItemViewModel? character)
    {
        if (character is null || character.IsActive) return;
        try
        {
            ErrorMessage = null;
            await store.SetActiveAsync(character.Id);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    public async Task RenameAsync(CharacterProfileItemViewModel character, string name)
    {
        try
        {
            ErrorMessage = null;
            await store.RenameAsync(character.Id, name);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    public async Task DeleteAsync(CharacterProfileItemViewModel character)
    {
        try
        {
            ErrorMessage = null;
            await store.DeleteAsync(character.Id);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task ReloadAsync()
    {
        var characters = await store.GetAllAsync();
        var active = await store.GetActiveAsync();
        ActiveCharacterName = active.Name;
        Characters.Clear();
        foreach (var character in characters)
        {
            Characters.Add(new CharacterProfileItemViewModel(
                character,
                character.Id == active.Id,
                characters.Count > 1));
        }
    }
}

public sealed class CharacterProfileItemViewModel(
    CharacterProfile profile,
    bool isActive,
    bool canDelete)
{
    public string Id { get; } = profile.Id;
    public string Name { get; } = profile.Name;
    public bool IsActive { get; } = isActive;
    public bool IsNotActive => !IsActive;
    public bool CanDelete { get; } = canDelete;
    public string StatusText => IsActive ? "ACTIVO" : string.Empty;
}
