using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.App.Services;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.App.ViewModels;

public partial class CharacterSheetViewModel(
    ICharacterProfileStore profiles,
    ICharacterSheetService characterSheets) : ObservableObject
{
    private CharacterProfile? _character;
    private CharacterSheetData _data = new();

    public bool HasCharacter => _character is not null;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharacter))]
    private string characterName = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private string statusText = "Hoja editable offline";

    [ObservableProperty]
    private bool hasUnsavedChanges;

    public async Task LoadAsync(string characterId)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(characterId)) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            _character = (await profiles.GetAllAsync()).SingleOrDefault(item => item.Id == characterId)
                ?? throw new KeyNotFoundException("El personaje ya no existe.");
            CharacterName = _character.Name;
            _data = await characterSheets.LoadDataAsync(_character);
            HasUnsavedChanges = false;
            StatusText = _data.UpdatedAt == default
                ? "Hoja lista para completar"
                : $"Guardado: {_data.UpdatedAt.ToLocalTime():g}";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo abrir la hoja: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string CreateEditorPayloadJson()
        => JsonSerializer.Serialize(new
        {
            type = "load",
            payload = new
            {
                characterName = CharacterName,
                textFields = _data.TextFields,
                checkedFields = _data.CheckedFields
            }
        });

    public void ApplyEditorChange(string name, string kind, string? value, bool isChecked)
    {
        if (string.IsNullOrWhiteSpace(name) || _character is null) return;
        if (string.Equals(kind, "checkbox", StringComparison.OrdinalIgnoreCase))
        {
            if (isChecked) _data.CheckedFields.Add(name);
            else _data.CheckedFields.Remove(name);
        }
        else if (string.IsNullOrEmpty(value))
        {
            _data.TextFields.Remove(name);
        }
        else
        {
            _data.TextFields[name] = value;
        }

        HasUnsavedChanges = true;
        StatusText = "Cambios sin guardar";
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SaveAsync()
    {
        if (_character is null || IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await SaveCoreAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo guardar la hoja: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenPdfAsync()
    {
        if (_character is null || IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await SaveCoreAsync();
            await characterSheets.OpenAsync(_character);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo abrir el PDF: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ShareAsync()
    {
        if (_character is null || IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await SaveCoreAsync();
            await characterSheets.ShareAsync(_character);
        }
        catch (Exception exception)
        {
            ErrorMessage = $"No se pudo guardar la copia: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveIfNeededAsync()
    {
        if (!HasUnsavedChanges || _character is null || IsBusy) return;
        await SaveAsync();
    }

    public void ReportEditorError(string message)
        => ErrorMessage = $"No se pudo mostrar la hoja: {message}";

    private async Task SaveCoreAsync()
    {
        if (_character is null) return;
        _data.UpdatedAt = DateTimeOffset.UtcNow;
        await characterSheets.SaveDataAsync(_character, _data);
        HasUnsavedChanges = false;
        StatusText = $"Guardado: {_data.UpdatedAt.ToLocalTime():g}";
    }
}
