using DndCompendium.App.ViewModels;

namespace DndCompendium.App.Views;

public partial class SpellListView : ContentView
{
    private const int DoubleTapWindowMilliseconds = 550;
    private string? _lastTappedSpellId;
    private long _lastTapTimestamp;

    public SpellListView() => InitializeComponent();

    private async void OnSpellCardTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not SpellItemViewModel spell || BindingContext is not SpellListViewModel viewModel)
        {
            return;
        }

        var timestamp = Environment.TickCount64;
        var isDoubleTap = string.Equals(_lastTappedSpellId, spell.Id, StringComparison.Ordinal)
            && timestamp - _lastTapTimestamp <= DoubleTapWindowMilliseconds;

        _lastTappedSpellId = spell.Id;
        _lastTapTimestamp = timestamp;
        if (!isDoubleTap)
        {
            return;
        }

        _lastTappedSpellId = null;
        _lastTapTimestamp = 0;
        await viewModel.OpenDetailCommand.ExecuteAsync(spell);
    }
}
