using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class CharacterProfilesPage : ContentView
{
    private readonly CharacterProfilesViewModel _viewModel;

    public CharacterProfilesPage(CharacterProfilesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public Task LoadAsync() => _viewModel.LoadCommand.ExecuteAsync(null);

    private async void OnRenameClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: CharacterProfileItemViewModel character }) return;
        var name = await Shell.Current.DisplayPromptAsync(
            "Renombrar personaje",
            "Nuevo nombre:",
            "Guardar",
            "Cancelar",
            placeholder: "Nombre",
            maxLength: 60,
            initialValue: character.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            await _viewModel.RenameAsync(character, name);
        }
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: CharacterProfileItemViewModel character }) return;
        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Eliminar personaje",
            $"¿Eliminar a {character.Name} y todos sus favoritos y preparados?",
            "Eliminar",
            "Cancelar");
        if (confirmed)
        {
            await _viewModel.DeleteAsync(character);
        }
    }
}
