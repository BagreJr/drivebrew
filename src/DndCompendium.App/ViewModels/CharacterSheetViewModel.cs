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

    public IReadOnlyList<CharacterSheetSectionViewModel> Sections { get; private set; } = [];
    public IReadOnlyList<CharacterSheetFieldViewModel> VisibleFields => SelectedSection?.Fields ?? [];
    public bool HasCharacter => _character is not null;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool CanGoPrevious => SelectedSection is not null && Sections.IndexOf(SelectedSection) > 0;
    public bool CanGoNext => SelectedSection is not null && Sections.IndexOf(SelectedSection) < Sections.Count - 1;
    public string SectionHelp => SelectedSection?.HelpText ?? string.Empty;
    public string SectionPosition => SelectedSection is null
        ? string.Empty
        : $"{Sections.IndexOf(SelectedSection) + 1} de {Sections.Count}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharacter))]
    private string characterName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleFields))]
    [NotifyPropertyChangedFor(nameof(SectionHelp))]
    [NotifyPropertyChangedFor(nameof(SectionPosition))]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private CharacterSheetSectionViewModel? selectedSection;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private string statusText = "Los cambios se guardan en el perfil del personaje.";

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
            var data = await characterSheets.LoadDataAsync(_character);
            Sections = CharacterSheetSchema.Sections
                .Select(section => new CharacterSheetSectionViewModel(section, data, OnFieldChanged))
                .ToArray();
            OnPropertyChanged(nameof(Sections));
            SelectedSection = Sections.FirstOrDefault();
            HasUnsavedChanges = false;
            StatusText = data.UpdatedAt == default
                ? "Hoja lista para completar."
                : $"Último guardado: {data.UpdatedAt.ToLocalTime():g}";
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

    [RelayCommand]
    private void PreviousSection()
    {
        if (SelectedSection is null) return;
        var index = Sections.IndexOf(SelectedSection);
        if (index > 0) SelectedSection = Sections[index - 1];
    }

    [RelayCommand]
    private void NextSection()
    {
        if (SelectedSection is null) return;
        var index = Sections.IndexOf(SelectedSection);
        if (index >= 0 && index < Sections.Count - 1) SelectedSection = Sections[index + 1];
    }

    public async Task SaveIfNeededAsync()
    {
        if (!HasUnsavedChanges || _character is null || IsBusy) return;
        await SaveAsync();
    }

    private async Task SaveCoreAsync()
    {
        if (_character is null) return;
        var data = new CharacterSheetData();
        foreach (var field in Sections.SelectMany(section => section.Fields))
        {
            if (field.HasTextInput && !string.IsNullOrEmpty(field.Value))
            {
                data.TextFields[field.FieldName] = field.Value;
                foreach (var mirror in field.MirrorFieldNames)
                    data.TextFields[mirror] = field.Value;
            }
            if (field.HasToggle && field.IsChecked && field.ToggleFieldName is not null)
                data.CheckedFields.Add(field.ToggleFieldName);
        }
        data.UpdatedAt = DateTimeOffset.UtcNow;
        await characterSheets.SaveDataAsync(_character, data);
        HasUnsavedChanges = false;
        StatusText = $"Guardado: {data.UpdatedAt.ToLocalTime():g}";
    }

    private void OnFieldChanged()
    {
        HasUnsavedChanges = true;
        StatusText = "Cambios sin guardar";
    }
}

public sealed class CharacterSheetSectionViewModel
{
    public CharacterSheetSectionViewModel(
        CharacterSheetSectionDefinition definition,
        CharacterSheetData data,
        Action changed)
    {
        Name = definition.Name;
        HelpText = definition.HelpText;
        Fields = definition.Fields
            .Select(field => new CharacterSheetFieldViewModel(field, data, changed))
            .ToArray();
    }

    public string Name { get; }
    public string HelpText { get; }
    public IReadOnlyList<CharacterSheetFieldViewModel> Fields { get; }
}

public partial class CharacterSheetFieldViewModel : ObservableObject
{
    private readonly Action _changed;

    public CharacterSheetFieldViewModel(
        CharacterSheetFieldDefinition definition,
        CharacterSheetData data,
        Action changed)
    {
        FieldName = definition.FieldName;
        Label = definition.Label;
        IsMultiline = definition.IsMultiline;
        IsNumeric = definition.IsNumeric;
        ToggleFieldName = definition.ToggleFieldName;
        ToggleLabel = definition.ToggleLabel ?? string.Empty;
        MirrorFieldNames = definition.MirrorFieldNames ?? [];
        _changed = changed;
        value = data.TextFields.TryGetValue(FieldName, out var current)
            ? current
            : MirrorFieldNames.Select(name => data.TextFields.GetValueOrDefault(name)).FirstOrDefault(text => text is not null)
                ?? string.Empty;
        isChecked = ToggleFieldName is not null && data.CheckedFields.Contains(ToggleFieldName);
    }

    public string FieldName { get; }
    public string Label { get; }
    public bool IsMultiline { get; }
    public bool IsNumeric { get; }
    public string? ToggleFieldName { get; }
    public string ToggleLabel { get; }
    public IReadOnlyList<string> MirrorFieldNames { get; }
    public bool HasTextInput => !string.IsNullOrEmpty(FieldName);
    public bool IsSingleLine => HasTextInput && !IsMultiline;
    public bool HasToggle => ToggleFieldName is not null;
    public bool IsToggleOnly => !HasTextInput && HasToggle;
    public Keyboard InputKeyboard => IsNumeric ? Keyboard.Numeric : Keyboard.Default;

    [ObservableProperty]
    private string value = string.Empty;

    [ObservableProperty]
    private bool isChecked;

    partial void OnValueChanged(string value) => _changed();
    partial void OnIsCheckedChanged(bool value) => _changed();
}

internal static class CharacterSheetCollectionExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> source, T item)
    {
        for (var index = 0; index < source.Count; index++)
            if (EqualityComparer<T>.Default.Equals(source[index], item)) return index;
        return -1;
    }
}
