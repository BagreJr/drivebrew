using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class CharacterSheetPage : ContentPage, IQueryAttributable
{
    private readonly CharacterSheetViewModel _viewModel;
    private string? _characterId;

    public CharacterSheetPage(CharacterSheetViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("characterId", out var value)) return;
        var characterId = Uri.UnescapeDataString(Convert.ToString(value) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(characterId) || characterId == _characterId) return;
        _characterId = characterId;
        MainThread.BeginInvokeOnMainThread(async () => await _viewModel.LoadAsync(characterId));
    }

    protected override async void OnDisappearing()
    {
        await _viewModel.SaveIfNeededAsync();
        base.OnDisappearing();
    }
}
