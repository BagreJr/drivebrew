using Microsoft.Maui.ApplicationModel;

namespace DndCompendium.App.Views;

public partial class MorePage : ContentPage, IQueryAttributable
{
    private readonly CharacterProfilesPage _characters;
    private readonly DownloadsPage _downloads;
    private readonly ResourceManagerPage _resources;
    private readonly RulesAssistantPage _rules;
    private readonly FavoritesPage _favorites;
    private readonly PreparedSpellsPage _prepared;
    private readonly SettingsPage _settings;
    private Func<Task> _loadSelected;

    public MorePage(
        CharacterProfilesPage characters,
        ResourceManagerPage resources,
        RulesAssistantPage rules,
        DownloadsPage downloads,
        FavoritesPage favorites,
        PreparedSpellsPage prepared,
        SettingsPage settings)
    {
        InitializeComponent();
        _characters = characters;
        _resources = resources;
        _rules = rules;
        _downloads = downloads;
        _favorites = favorites;
        _prepared = prepared;
        _settings = settings;
        _loadSelected = characters.LoadAsync;
        SelectSection(characters, characters.LoadAsync, CharactersTab);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _loadSelected();
    }

    private async void OnCharactersClicked(object? sender, EventArgs e)
        => await SelectSectionAsync(_characters, _characters.LoadAsync, CharactersTab);

    private async void OnDownloadsClicked(object? sender, EventArgs e)
        => await SelectSectionAsync(_downloads, _downloads.LoadAsync, DownloadsTab);

    private async void OnResourcesClicked(object? sender, EventArgs e)
        => await SelectSectionAsync(_resources, _resources.LoadAsync, ResourcesTab);

    private async void OnRulesClicked(object? sender, EventArgs e)
        => await SelectSectionAsync(_rules, RulesAssistantPage.LoadAsync, RulesTab);

    private async void OnFavoritesClicked(object? sender, EventArgs e)
        => await SelectSectionAsync(_favorites, _favorites.LoadAsync, FavoritesTab);

    private async void OnPreparedClicked(object? sender, EventArgs e)
        => await SelectSectionAsync(_prepared, _prepared.LoadAsync, PreparedTab);

    private async void OnSettingsClicked(object? sender, EventArgs e)
        => await SelectSectionAsync(_settings, _settings.LoadAsync, SettingsTab);

    private async Task SelectSectionAsync(View content, Func<Task> load, Button selected)
    {
        SelectSection(content, load, selected);
        await load();
    }

    private void SelectSection(View content, Func<Task> load, Button selected)
    {
        SectionHost.Content = content;
        _loadSelected = load;
        foreach (var button in new[] { CharactersTab, ResourcesTab, RulesTab, PreparedTab, FavoritesTab, DownloadsTab, SettingsTab })
        {
            button.Style = (Style)Application.Current!.Resources[
                ReferenceEquals(button, selected)
                    ? "SmallAccentButtonStyle"
                    : "SmallSecondaryButtonStyle"];
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("section", out var section)
            || !string.Equals(Convert.ToString(section), "settings", StringComparison.OrdinalIgnoreCase)) return;
        var update = query.TryGetValue("update", out var rawUpdate)
            && bool.TryParse(Convert.ToString(rawUpdate), out var parsed) && parsed;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await SelectSectionAsync(_settings, _settings.LoadAsync, SettingsTab);
            if (update) await _settings.BeginUpdateAsync();
        });
    }
}
