using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.App.ViewModels;

public partial class ResourceEditorViewModel(ICharacterProfileStore store) : ObservableObject
{
    private string _resourceId = string.Empty;
    public IReadOnlyList<string> KindOptions { get; } = ["Recurso", "Spell Slot", "Pact Magic Slot"];
    public IReadOnlyList<string> RecoveryOptions { get; } = ["Manual", "Descanso corto", "Descanso largo", "Descanso corto y largo"];
    public bool IsEditing => !string.IsNullOrWhiteSpace(_resourceId);
    public bool IsSlot => SelectedKind != "Recurso";
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string selectedKind = "Recurso";
    [ObservableProperty] private string levelText = "1";
    [ObservableProperty] private string maximumText = "1";
    [ObservableProperty] private string currentText = "1";
    [ObservableProperty] private string unit = string.Empty;
    [ObservableProperty] private string description = string.Empty;
    [ObservableProperty] private string selectedRecovery = "Manual";
    [ObservableProperty] private string recoveryAmountText = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError))] private string? errorMessage;
    [ObservableProperty] private bool isBusy;

    partial void OnSelectedKindChanged(string value) => OnPropertyChanged(nameof(IsSlot));

    public async Task LoadAsync(string? resourceId, string? presetName, string? presetKind)
    {
        ErrorMessage = null;
        _resourceId = resourceId ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_resourceId))
        {
            var resource = (await store.GetResourcesAsync()).FirstOrDefault(item => item.Id == _resourceId)
                ?? throw new KeyNotFoundException("El recurso ya no existe.");
            Name = resource.Name;
            SelectedKind = KindLabel(resource.Kind);
            LevelText = (resource.Level ?? 1).ToString();
            MaximumText = resource.Maximum.ToString();
            CurrentText = resource.Current.ToString();
            Unit = resource.Unit ?? string.Empty;
            Description = resource.Description ?? string.Empty;
            SelectedRecovery = RecoveryLabel(resource.Recovery);
            RecoveryAmountText = resource.RecoveryAmount?.ToString() ?? string.Empty;
        }
        else
        {
            Name = presetName ?? string.Empty;
            SelectedKind = presetKind switch
            {
                nameof(CharacterResourceKind.SpellSlot) => "Spell Slot",
                nameof(CharacterResourceKind.PactMagicSlot) => "Pact Magic Slot",
                _ => "Recurso"
            };
        }
        OnPropertyChanged(nameof(IsEditing));
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SaveAsync()
    {
        if (!int.TryParse(MaximumText, out var maximum) || maximum < 0
            || !int.TryParse(CurrentText, out var current) || current < 0)
        {
            ErrorMessage = "Máximo y actual deben ser números no negativos.";
            return;
        }
        int? level = null;
        if (IsSlot && (!int.TryParse(LevelText, out var parsedLevel) || parsedLevel is < 1 or > 9))
        {
            ErrorMessage = "El nivel del slot debe estar entre 1 y 9.";
            return;
        }
        else if (IsSlot) level = int.Parse(LevelText);
        int? recoveryAmount = null;
        if (!string.IsNullOrWhiteSpace(RecoveryAmountText)
            && (!int.TryParse(RecoveryAmountText, out var parsedAmount) || parsedAmount < 0))
        {
            ErrorMessage = "La cantidad recuperada debe ser un número no negativo o quedar vacía para recuperación completa.";
            return;
        }
        else if (!string.IsNullOrWhiteSpace(RecoveryAmountText)) recoveryAmount = int.Parse(RecoveryAmountText);

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var kind = SelectedKind switch
            {
                "Spell Slot" => CharacterResourceKind.SpellSlot,
                "Pact Magic Slot" => CharacterResourceKind.PactMagicSlot,
                _ => CharacterResourceKind.Custom
            };
            var normalizedName = IsSlot
                ? kind == CharacterResourceKind.SpellSlot ? $"Spell Slots · Nivel {level}" : $"Pact Magic · Nivel {level}"
                : Name;
            await store.UpsertResourceAsync(new CharacterResource
            {
                Id = _resourceId,
                Name = normalizedName,
                Kind = kind,
                Level = level,
                Maximum = maximum,
                Current = current,
                Unit = Unit,
                Description = Description,
                Recovery = SelectedRecovery switch
                {
                    "Descanso corto" => ResourceRecoveryKind.ShortRest,
                    "Descanso largo" => ResourceRecoveryKind.LongRest,
                    "Descanso corto y largo" => ResourceRecoveryKind.ShortAndLongRest,
                    _ => ResourceRecoveryKind.None
                },
                RecoveryAmount = recoveryAmount
            });
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception exception) { ErrorMessage = exception.Message; }
        finally { IsBusy = false; }
    }

    public async Task DeleteAsync()
    {
        if (!IsEditing) return;
        await store.DeleteResourceAsync(_resourceId);
        await Shell.Current.GoToAsync("..");
    }

    private static string KindLabel(CharacterResourceKind kind) => kind switch
    {
        CharacterResourceKind.SpellSlot => "Spell Slot",
        CharacterResourceKind.PactMagicSlot => "Pact Magic Slot",
        _ => "Recurso"
    };

    private static string RecoveryLabel(ResourceRecoveryKind recovery) => recovery switch
    {
        ResourceRecoveryKind.ShortRest => "Descanso corto",
        ResourceRecoveryKind.LongRest => "Descanso largo",
        ResourceRecoveryKind.ShortAndLongRest => "Descanso corto y largo",
        _ => "Manual"
    };
}
