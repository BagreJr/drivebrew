using CommunityToolkit.Mvvm.ComponentModel;
using DndCompendium.Core.Models;
using DndCompendium.Core.Services;

namespace DndCompendium.App.ViewModels;

public partial class SpellItemViewModel : ObservableObject
{
    public SpellItemViewModel(Spell model, bool prepared, bool favorite)
    {
        Model = model;
        isPrepared = prepared;
        isFavorite = favorite;
    }

    public Spell Model { get; }
    public string Id => Model.Id;
    public string Name => Model.Name;
    public int Level => Model.Level;
    public string LevelText => Level == 0 ? "Truco" : $"Nivel {Level}";
    public string SchoolText => SpellDisplay.School(Model.School);
    public string CastingTime => Model.CastingTime;
    public string Range => Model.Range;
    public string Duration => Model.Duration;
    public string ConcentrationText => Model.IsConcentration ? "Sí" : "No";
    public string RitualText => Model.IsRitual ? "Sí" : "No";
    public string ClassesText => Model.Classes.Count == 0 ? "—" : string.Join(", ", Model.Classes);
    public string OriginsText => Model.Origins.Count == 0 ? "—" : string.Join(", ", Model.Origins);
    public string Effect => Model.Effect;
    public string HigherLevels => Model.HigherLevels;
    public bool HasHigherLevels => Model.HigherLevels != "—";
    public string MaterialDescription => Model.MaterialDescription;
    public bool HasMaterialDescription => Model.MaterialDescription != "—";
    public string ComponentsText
    {
        get
        {
            var components = new List<string>(3);
            if (Model.HasVerbalComponent) components.Add("V");
            if (Model.HasSomaticComponent) components.Add("S");
            if (Model.HasMaterialComponent) components.Add("M");
            return components.Count == 0 ? "—" : string.Join(", ", components);
        }
    }

    public string PreparedActionText => IsPrepared ? "Quitar" : "Preparar";
    public string PreparedStatusText => IsPrepared ? "PREPARADO" : string.Empty;
    public string FavoriteActionText => IsFavorite ? "★ Favorito" : "☆ Favorito";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreparedActionText))]
    [NotifyPropertyChangedFor(nameof(PreparedStatusText))]
    private bool isPrepared;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FavoriteActionText))]
    private bool isFavorite;
}

internal static class SpellDisplay
{
    public static string CastingTimeCategory(string castingTime)
        => SpellCastingTimeClassifier.Classify(castingTime) switch
        {
            SpellCastingTimeKind.Action => "Action",
            SpellCastingTimeKind.BonusAction => "Bonus Action",
            SpellCastingTimeKind.Reaction => "Reaction",
            _ => "Otro"
        };

    public static string School(string school) => school.Trim().ToLowerInvariant() switch
    {
        "abjuration" => "Abjuración",
        "conjuration" => "Conjuración",
        "divination" => "Adivinación",
        "enchantment" => "Encantamiento",
        "evocation" => "Evocación",
        "illusion" => "Ilusión",
        "necromancy" => "Nigromancia",
        "transmutation" => "Transmutación",
        _ => string.IsNullOrWhiteSpace(school) ? "—" : school
    };
}
