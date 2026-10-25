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
            $"¿Eliminar a {character.Name}, su hoja PDF, favoritos, preparados y Resources?",
            "Eliminar",
            "Cancelar");
        if (confirmed)
        {
            await _viewModel.DeleteAsync(character);
        }
    }

    private async void OnImportSheetClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: CharacterProfileItemViewModel character }) return;
        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Reemplazar hoja PDF",
            $"La hoja guardada de {character.Name} será reemplazada por el PDF que elijas.",
            "Elegir PDF",
            "Cancelar");
        if (!confirmed) return;
        if (await _viewModel.ImportSheetAsync(character))
        {
            await Shell.Current.DisplayAlertAsync(
                "Hoja guardada",
                $"La hoja PDF de {character.Name} quedó guardada dentro de la aplicación.",
                "Aceptar");
        }
    }
}
