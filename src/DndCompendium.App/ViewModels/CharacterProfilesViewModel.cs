using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.App.Services;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.App.ViewModels;

public partial class CharacterProfilesViewModel(
    ICharacterProfileStore store,
    ICharacterSheetService characterSheets) : ObservableObject
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
            var created = await store.CreateAsync(NewCharacterName);
            await characterSheets.EnsureCreatedAsync(created);
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
            await characterSheets.UpdateCharacterNameAsync(character.Profile with { Name = name.Trim() });
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
            await characterSheets.DeleteAsync(character.Id);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenSheetAsync(CharacterProfileItemViewModel? character)
    {
        if (character is null) return;
        try
        {
            ErrorMessage = null;
            await Shell.Current.GoToAsync(
                $"{AppRoutes.CharacterSheet}?characterId={Uri.EscapeDataString(character.Id)}");
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo abrir la hoja: {exception.Message}";
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ShareSheetAsync(CharacterProfileItemViewModel? character)
    {
        if (character is null) return;
        try
        {
            ErrorMessage = null;
            await characterSheets.ShareAsync(character.Profile);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo guardar la copia: {exception.Message}";
        }
    }

    public async Task<bool> ImportSheetAsync(CharacterProfileItemViewModel character)
    {
        try
        {
            ErrorMessage = null;
            return await characterSheets.PickAndImportAsync(character.Profile);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo importar la hoja: {exception.Message}";
            return false;
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
            await characterSheets.EnsureCreatedAsync(character);
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
    public CharacterProfile Profile { get; } = profile;
    public string Id { get; } = profile.Id;
    public string Name { get; } = profile.Name;
    public bool IsActive { get; } = isActive;
    public bool IsNotActive => !IsActive;
    public bool CanDelete { get; } = canDelete;
    public string StatusText => IsActive ? "ACTIVO" : string.Empty;
    public string SheetStatusText => "Hoja PDF rellenable · 3 páginas · guardada en la app";
}
