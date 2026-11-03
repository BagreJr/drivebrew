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

    private async void OnMoreClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: CharacterProfileItemViewModel character }) return;
        var action = await Shell.Current.DisplayActionSheetAsync(
            character.Name,
            "Cancelar",
            character.CanDelete ? "Eliminar" : null,
            "Duplicar",
            "Editar nombre",
            "Importar PDF",
            "Exportar PDF");
        switch (action)
        {
            case "Duplicar":
                var copyName = await Shell.Current.DisplayPromptAsync(
                    "Duplicar personaje", "Nombre de la copia:", "Duplicar", "Cancelar",
                    initialValue: $"{character.Name} copia", maxLength: 60);
                if (!string.IsNullOrWhiteSpace(copyName)) await _viewModel.DuplicateAsync(character, copyName);
                break;
            case "Editar nombre":
                await RenameCharacterAsync(character);
                break;
            case "Importar PDF":
                await ImportCharacterAsync(character);
                break;
            case "Exportar PDF":
                _viewModel.ShareSheetCommand.Execute(character);
                break;
            case "Eliminar":
                await DeleteCharacterAsync(character);
                break;
        }
    }

    private Task RenameCharacterAsync(CharacterProfileItemViewModel character)
        => RenameCharacterCoreAsync(character);

    private async Task RenameCharacterCoreAsync(CharacterProfileItemViewModel character)
    {
        var name = await Shell.Current.DisplayPromptAsync(
            "Editar identidad", "Nombre del personaje:", "Guardar", "Cancelar",
            placeholder: "Nombre", maxLength: 60, initialValue: character.Name);
        if (!string.IsNullOrWhiteSpace(name)) await _viewModel.RenameAsync(character, name);
    }

    private async Task DeleteCharacterAsync(CharacterProfileItemViewModel character)
    {
        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Eliminar personaje",
            $"¿Eliminar a {character.Name}, su hoja, favoritos, preparados y recursos?",
            "Eliminar",
            "Cancelar");
        if (confirmed) await _viewModel.DeleteAsync(character);
    }

    private async Task ImportCharacterAsync(CharacterProfileItemViewModel character)
    {
        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Importar personaje",
            $"La hoja de {character.Name} será reemplazada por el PDF elegido. Antes de migrar se conserva una copia de respaldo.",
            "Elegir PDF",
            "Cancelar");
        if (!confirmed) return;
        if (await _viewModel.ImportSheetAsync(character))
            await Shell.Current.DisplayAlertAsync("Personaje importado", "La hoja quedó guardada sin conexión.", "Aceptar");
    }
}
