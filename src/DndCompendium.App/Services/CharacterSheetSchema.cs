namespace DndCompendium.App.Services;

public sealed record CharacterSheetFieldDefinition(
    string FieldName,
    string Label,
    bool IsMultiline = false,
    bool IsNumeric = false,
    string? ToggleFieldName = null,
    string? ToggleLabel = null,
    IReadOnlyList<string>? MirrorFieldNames = null);

public sealed record CharacterSheetSectionDefinition(
    string Name,
    string HelpText,
    IReadOnlyList<CharacterSheetFieldDefinition> Fields);

public static class CharacterSheetSchema
{
    public static IReadOnlyList<CharacterSheetSectionDefinition> Sections { get; } = BuildSections();

    private static IReadOnlyList<CharacterSheetSectionDefinition> BuildSections()
    {
        var sections = new List<CharacterSheetSectionDefinition>
        {
            new("Identidad", "Información principal del personaje.",
            [
                Field("CharacterName", "Nombre del personaje", mirrors: ["CharacterName 2"]),
                Field("ClassLevel", "Clase y nivel"),
                Field("Background", "Background"),
                Field("PlayerName", "Nombre del jugador"),
                Field("Race ", "Raza"),
                Field("Alignment", "Alineamiento"),
                Field("XP", "Puntos de experiencia", numeric: true),
                Field("Age", "Edad"),
                Field("Height", "Altura"),
                Field("Weight", "Peso"),
                Field("Eyes", "Ojos"),
                Field("Skin", "Piel"),
                Field("Hair", "Cabello")
            ]),
            new("Características", "Puntuaciones, modificadores y bonificador de competencia.",
            [
                Field("STR", "Fuerza", numeric: true), Field("STRmod", "Modificador de Fuerza"),
                Field("DEX", "Destreza", numeric: true), Field("DEXmod ", "Modificador de Destreza"),
                Field("CON", "Constitución", numeric: true), Field("CONmod", "Modificador de Constitución"),
                Field("INT", "Inteligencia", numeric: true), Field("INTmod", "Modificador de Inteligencia"),
                Field("WIS", "Sabiduría", numeric: true), Field("WISmod", "Modificador de Sabiduría"),
                Field("CHA", "Carisma", numeric: true), Field("CHamod", "Modificador de Carisma"),
                Field("ProfBonus", "Bonificador de competencia"),
                Field("Inspiration", "Inspiración")
            ]),
            new("Salvaciones", "Marcá las salvaciones en las que el personaje es competente.",
            [
                Field("ST Strength", "Fuerza", toggle: "Check Box 11"),
                Field("ST Dexterity", "Destreza", toggle: "Check Box 18"),
                Field("ST Constitution", "Constitución", toggle: "Check Box 19"),
                Field("ST Intelligence", "Inteligencia", toggle: "Check Box 20"),
                Field("ST Wisdom", "Sabiduría", toggle: "Check Box 21"),
                Field("ST Charisma", "Carisma", toggle: "Check Box 22")
            ]),
            new("Habilidades", "Ingresá el modificador y marcá la competencia correspondiente.",
            [
                Field("Acrobatics", "Acrobatics (DEX)", toggle: "Check Box 23"),
                Field("Animal", "Animal Handling (WIS)", toggle: "Check Box 24"),
                Field("Arcana", "Arcana (INT)", toggle: "Check Box 25"),
                Field("Athletics", "Athletics (STR)", toggle: "Check Box 26"),
                Field("Deception ", "Deception (CHA)", toggle: "Check Box 27"),
                Field("History ", "History (INT)", toggle: "Check Box 28"),
                Field("Insight", "Insight (WIS)", toggle: "Check Box 29"),
                Field("Intimidation", "Intimidation (CHA)", toggle: "Check Box 30"),
                Field("Investigation ", "Investigation (INT)", toggle: "Check Box 31"),
                Field("Medicine", "Medicine (WIS)", toggle: "Check Box 32"),
                Field("Nature", "Nature (INT)", toggle: "Check Box 33"),
                Field("Perception ", "Perception (WIS)", toggle: "Check Box 34"),
                Field("Performance", "Performance (CHA)", toggle: "Check Box 35"),
                Field("Persuasion", "Persuasion (CHA)", toggle: "Check Box 36"),
                Field("Religion", "Religion (INT)", toggle: "Check Box 37"),
                Field("SleightofHand", "Sleight of Hand (DEX)", toggle: "Check Box 38"),
                Field("Stealth ", "Stealth (DEX)", toggle: "Check Box 39"),
                Field("Survival", "Survival (WIS)", toggle: "Check Box 40"),
                Field("Passive", "Percepción pasiva", numeric: true)
            ]),
            new("Combate", "Defensas, movimiento, puntos de golpe, dados de golpe y salvaciones de muerte.",
            [
                Field("AC", "Armor Class", numeric: true),
                Field("Initiative", "Iniciativa"),
                Field("Speed", "Velocidad"),
                Field("HPMax", "Puntos de golpe máximos", numeric: true),
                Field("HPCurrent", "Puntos de golpe actuales", numeric: true),
                Field("HPTemp", "Puntos de golpe temporales", numeric: true),
                Field("HDTotal", "Dados de golpe totales"),
                Field("HD", "Dados de golpe actuales"),
                Toggle("Check Box 12", "Éxito de muerte 1"),
                Toggle("Check Box 13", "Éxito de muerte 2"),
                Toggle("Check Box 14", "Éxito de muerte 3"),
                Toggle("Check Box 15", "Fallo de muerte 1"),
                Toggle("Check Box 16", "Fallo de muerte 2"),
                Toggle("Check Box 17", "Fallo de muerte 3")
            ]),
            new("Ataques", "Armas, bonificadores, daño y notas de ataques o spellcasting.",
            [
                Field("Wpn Name", "Arma o ataque 1"), Field("Wpn1 AtkBonus", "Bonificador 1"), Field("Wpn1 Damage", "Daño y tipo 1"),
                Field("Wpn Name 2", "Arma o ataque 2"), Field("Wpn2 AtkBonus ", "Bonificador 2"), Field("Wpn2 Damage ", "Daño y tipo 2"),
                Field("Wpn Name 3", "Arma o ataque 3"), Field("Wpn3 AtkBonus  ", "Bonificador 3"), Field("Wpn3 Damage ", "Daño y tipo 3"),
                Field("AttacksSpellcasting", "Ataques y spellcasting", multiline: true)
            ]),
            new("Personalidad", "Rasgos personales, ideales, vínculos y defectos.",
            [
                Field("PersonalityTraits ", "Rasgos de personalidad", multiline: true),
                Field("Ideals", "Ideales", multiline: true),
                Field("Bonds", "Vínculos", multiline: true),
                Field("Flaws", "Defectos", multiline: true)
            ]),
            new("Equipo", "Competencias, idiomas, monedas y equipo.",
            [
                Field("ProficienciesLang", "Otras competencias e idiomas", multiline: true),
                Field("CP", "CP", numeric: true), Field("SP", "SP", numeric: true),
                Field("EP", "EP", numeric: true), Field("GP", "GP", numeric: true), Field("PP", "PP", numeric: true),
                Field("Equipment", "Equipo", multiline: true)
            ]),
            new("Rasgos", "Features, traits y contenido adicional de la hoja.",
            [
                Field("Features and Traits", "Features y traits", multiline: true),
                Field("Feat+Traits", "Features y traits adicionales", multiline: true),
                Field("Allies", "Aliados y organizaciones", multiline: true),
                Field("FactionName", "Nombre de facción u organización"),
                Field("Treasure", "Tesoro", multiline: true)
            ]),
            new("Historia", "Trasfondo narrativo y detalles biográficos.",
            [
                Field("Backstory", "Historia del personaje", multiline: true)
            ]),
            new("Spellcasting", "Datos generales para lanzar spells.",
            [
                Field("Spellcasting Class 2", "Clase de spellcasting"),
                Field("SpellcastingAbility 2", "Característica de spellcasting"),
                Field("SpellSaveDC  2", "Spell Save DC", numeric: true),
                Field("SpellAtkBonus 2", "Bonificador de ataque de spell")
            ])
        };

        sections.Add(SpellSection("Cantrips", "Spells de nivel 0.",
            ["Spells 1014", "Spells 1016", "Spells 1017", "Spells 1018", "Spells 1019", "Spells 1020", "Spells 1021", "Spells 1022"]));
        sections.Add(SpellSection("Spells - Nivel 1", "Slots y spells de nivel 1.",
            ["Spells 1015", "Spells 1023", "Spells 1024", "Spells 1025", "Spells 1026", "Spells 1027", "Spells 1028", "Spells 1029", "Spells 1030", "Spells 1031", "Spells 1032", "Spells 1033"],
            ["Check Box 251", "Check Box 309", "Check Box 3010", "Check Box 3011", "Check Box 3012", "Check Box 3013", "Check Box 3014", "Check Box 3015", "Check Box 3016", "Check Box 3017", "Check Box 3018", "Check Box 3019"], 19));
        sections.Add(SpellSection("Spells - Nivel 2", "Slots y spells de nivel 2.",
            ["Spells 1046", "Spells 1034", "Spells 1035", "Spells 1036", "Spells 1037", "Spells 1038", "Spells 1039", "Spells 1040", "Spells 1041", "Spells 1042", "Spells 1043", "Spells 1044", "Spells 1045"],
            ["Check Box 313", "Check Box 310", "Check Box 3020", "Check Box 3021", "Check Box 3022", "Check Box 3023", "Check Box 3024", "Check Box 3025", "Check Box 3026", "Check Box 3027", "Check Box 3028", "Check Box 3029", "Check Box 3030"], 20));
        sections.Add(SpellSection("Spells - Nivel 3", "Slots y spells de nivel 3.",
            ["Spells 1048", "Spells 1047", "Spells 1049", "Spells 1050", "Spells 1051", "Spells 1052", "Spells 1053", "Spells 1054", "Spells 1055", "Spells 1056", "Spells 1057", "Spells 1058", "Spells 1059"],
            ["Check Box 315", "Check Box 314", "Check Box 3031", "Check Box 3032", "Check Box 3033", "Check Box 3034", "Check Box 3035", "Check Box 3036", "Check Box 3037", "Check Box 3038", "Check Box 3039", "Check Box 3040", "Check Box 3041"], 21));
        sections.Add(SpellSection("Spells - Nivel 4", "Slots y spells de nivel 4.",
            ["Spells 1061", "Spells 1060", "Spells 1062", "Spells 1063", "Spells 1064", "Spells 1065", "Spells 1066", "Spells 1067", "Spells 1068", "Spells 1069", "Spells 1070", "Spells 1071", "Spells 1072"],
            ["Check Box 317", "Check Box 316", "Check Box 3042", "Check Box 3043", "Check Box 3044", "Check Box 3045", "Check Box 3046", "Check Box 3047", "Check Box 3048", "Check Box 3049", "Check Box 3050", "Check Box 3051", "Check Box 3052"], 22));
        sections.Add(SpellSection("Spells - Nivel 5", "Slots y spells de nivel 5.",
            ["Spells 1074", "Spells 1073", "Spells 1075", "Spells 1076", "Spells 1077", "Spells 1078", "Spells 1079", "Spells 1080", "Spells 1081"],
            ["Check Box 319", "Check Box 318", "Check Box 3053", "Check Box 3054", "Check Box 3055", "Check Box 3056", "Check Box 3057", "Check Box 3058", "Check Box 3059"], 23));
        sections.Add(SpellSection("Spells - Nivel 6", "Slots y spells de nivel 6.",
            ["Spells 1083", "Spells 1082", "Spells 1084", "Spells 1085", "Spells 1086", "Spells 1087", "Spells 1088", "Spells 1089", "Spells 1090"],
            ["Check Box 321", "Check Box 320", "Check Box 3060", "Check Box 3061", "Check Box 3062", "Check Box 3063", "Check Box 3064", "Check Box 3065", "Check Box 3066"], 24));
        sections.Add(SpellSection("Spells - Nivel 7", "Slots y spells de nivel 7.",
            ["Spells 1092", "Spells 1091", "Spells 1093", "Spells 1094", "Spells 1095", "Spells 1096", "Spells 1097", "Spells 1098", "Spells 1099"],
            ["Check Box 323", "Check Box 322", "Check Box 3067", "Check Box 3068", "Check Box 3069", "Check Box 3070", "Check Box 3071", "Check Box 3072", "Check Box 3073"], 25));
        sections.Add(SpellSection("Spells - Nivel 8", "Slots y spells de nivel 8.",
            ["Spells 10101", "Spells 10100", "Spells 10102", "Spells 10103", "Spells 10104", "Spells 10105", "Spells 10106"],
            ["Check Box 325", "Check Box 324", "Check Box 3074", "Check Box 3075", "Check Box 3076", "Check Box 3077", "Check Box 3078"], 26));
        sections.Add(SpellSection("Spells - Nivel 9", "Slots y spells de nivel 9.",
            ["Spells 10108", "Spells 10107", "Spells 10109", "Spells 101010", "Spells 101011", "Spells 101012", "Spells 101013"],
            ["Check Box 327", "Check Box 326", "Check Box 3079", "Check Box 3080", "Check Box 3081", "Check Box 3082", "Check Box 3083"], 27));
        return sections;
    }

    private static CharacterSheetSectionDefinition SpellSection(
        string name,
        string help,
        IReadOnlyList<string> spellFields,
        IReadOnlyList<string>? preparedFields = null,
        int? slotSuffix = null)
    {
        var fields = new List<CharacterSheetFieldDefinition>();
        if (slotSuffix is not null)
        {
            fields.Add(Field($"SlotsTotal {slotSuffix}", "Slots totales", numeric: true));
            fields.Add(Field($"SlotsRemaining {slotSuffix}", "Slots disponibles", numeric: true));
        }
        for (var index = 0; index < spellFields.Count; index++)
        {
            fields.Add(Field(
                spellFields[index],
                $"Spell {index + 1}",
                toggle: preparedFields is null ? null : preparedFields[index],
                toggleLabel: "Preparado"));
        }
        return new CharacterSheetSectionDefinition(name, help, fields);
    }

    private static CharacterSheetFieldDefinition Field(
        string name,
        string label,
        bool multiline = false,
        bool numeric = false,
        string? toggle = null,
        string? toggleLabel = "Competente",
        IReadOnlyList<string>? mirrors = null)
        => new(name, label, multiline, numeric, toggle, toggle is null ? null : toggleLabel, mirrors);

    private static CharacterSheetFieldDefinition Toggle(string name, string label)
        => new(string.Empty, label, ToggleFieldName: name, ToggleLabel: "Marcado");
}
