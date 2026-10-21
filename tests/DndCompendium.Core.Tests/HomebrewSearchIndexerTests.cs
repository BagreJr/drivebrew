using DndCompendium.Core.Models;
using DndCompendium.Core.Services;

namespace DndCompendium.Core.Tests;

public sealed class HomebrewSearchIndexerTests
{
    [Fact]
    public void IndexDocument_IgnoresTocAndTracksPageAndContext()
    {
        var document = CreateDocument(DocumentCategory.Classes, "Psion");
        const string source = """
            ```metadata
            title: Psion
            ```
            {{toc
            # [Psion](#p1)
            ## [Teleport Step](#p2)
            }}
            # Psion
            A master of psionic power.
            \page
            ## Teleport Step
            At 3rd level, you can use your reaction to teleport 30 feet.
            """;
        var content = new LocalDocumentContent("psion", "Psion", source, "documents/psion.txt", "abc");

        var entries = HomebrewSearchIndexer.IndexDocument(document, content);

        Assert.Equal(2, entries.Count);
        var feature = Assert.Single(entries, entry => entry.Name == "Teleport Step");
        Assert.Equal(SearchContentKind.ClassFeature, feature.Kind);
        Assert.Equal("teleport-step|p2", feature.Target);
        Assert.Equal("3", feature.Level);
        Assert.Contains("reaction", feature.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Concentración", "concentracion")]
    [InlineData("  Blood-Rites! ", "blood rites")]
    [InlineData("PSIÓN", "psion")]
    public void NormalizeForSearch_IsAccentAndPunctuationInsensitive(string value, string expected)
        => Assert.Equal(expected, HomebrewSearchIndexer.NormalizeForSearch(value));

    [Fact]
    public void IndexDocument_ClassifiesRaceFeaturesWithoutInventingMetadata()
    {
        var document = CreateDocument(DocumentCategory.Races, "Razas");
        const string source = "# Elf\nAncient folk.\n### Fey Ancestry\nYou have advantage against being charmed.";
        var content = new LocalDocumentContent("races", "Razas", source, "documents/races.txt", "def");

        var entries = HomebrewSearchIndexer.IndexDocument(document, content);

        Assert.Contains(entries, entry => entry.Name == "Elf" && entry.Kind == SearchContentKind.Race);
        var feature = Assert.Single(entries, entry => entry.Name == "Fey Ancestry");
        Assert.Equal(SearchContentKind.RaceFeature, feature.Kind);
        Assert.Null(feature.Level);
        Assert.Null(feature.Requirements);
    }

    [Fact]
    public void IndexDocument_SeparatesSubclassesAndTheirFeaturesWhenStructureIsExplicit()
    {
        var document = CreateDocument(DocumentCategory.Classes, "Psion");
        const string source = "# Psion\n## Subclasses\n### Mind Weaver\nA telepathic discipline.\n#### Mental Ward\nYou shield an ally with your reaction.";
        var content = new LocalDocumentContent("psion", "Psion", source, "documents/psion.txt", "ghi");

        var entries = HomebrewSearchIndexer.IndexDocument(document, content);

        Assert.Contains(entries, entry => entry.Name == "Mind Weaver" && entry.Kind == SearchContentKind.Subclass);
        Assert.Contains(entries, entry => entry.Name == "Mental Ward" && entry.Kind == SearchContentKind.SubclassFeature);
    }

    [Fact]
    public void IndexDocument_RecognizesRealFieldsOfStudyHierarchyAndExactFallbackTarget()
    {
        var document = CreateDocument(DocumentCategory.Classes, "Alchemist");
        const string source = """
            # Alchemist
            ### Bomb Formula
            At 2nd level, you learn to make bombs.
            \page
            ## Fields Of Study
            ### Amorist
            A specialist in mind-affecting alchemy.
            #### Magnetic Personality
            Starting at 6th level, you add Intelligence to Charisma checks.
            """;
        var content = new LocalDocumentContent("alchemist", "Alchemist", source, "documents/alchemist.txt", "real");

        var entries = HomebrewSearchIndexer.IndexDocument(document, content);

        Assert.Contains(entries, entry => entry.Name == "Bomb Formula" && entry.Kind == SearchContentKind.ClassFeature);
        Assert.Contains(entries, entry => entry.Name == "Amorist" && entry.Kind == SearchContentKind.Subclass);
        var feature = Assert.Single(entries, entry => entry.Name == "Magnetic Personality");
        Assert.Equal(SearchContentKind.SubclassFeature, feature.Kind);
        Assert.Equal("Amorist", feature.Parent);
        Assert.Equal("Alchemist", feature.ParentClass);
        Assert.Equal("Amorist", feature.ParentSubclass);
        Assert.Equal("6", feature.Level);
        Assert.Equal("magnetic-personality|p2", feature.Target);
    }

    [Fact]
    public void IndexDocument_UsesRealMagusHierarchyWithoutTurningNarrativeSubheadingsIntoFeatures()
    {
        var source = File.ReadAllText(FindRepositoryFile("test-data", "homebrewery-real", "Magus.txt"));
        var document = CreateDocument(DocumentCategory.Classes, "Magus");
        var content = new LocalDocumentContent("magus", "Magus", source, "documents/magus.txt", "real-magus");

        var entries = HomebrewSearchIndexer.IndexDocument(document, content);

        var subclass = Assert.Single(entries, entry => entry.Name == "Order of Gateways");
        Assert.Equal(SearchContentKind.Subclass, subclass.Kind);
        Assert.Equal("Magus", subclass.ParentClass);
        var feature = Assert.Single(entries, entry => entry.Name == "Gateway Assault");
        Assert.Equal(SearchContentKind.SubclassFeature, feature.Kind);
        Assert.Equal("Magus", feature.ParentClass);
        Assert.Equal("Order of Gateways", feature.ParentSubclass);
        Assert.Contains(entries, entry => entry.Name == "Quick Build" && entry.Kind == SearchContentKind.Other);
    }

    [Fact]
    public void IndexDocument_ClassifiesRealMixedCompendiumByStructuralSection()
    {
        var source = File.ReadAllText(FindRepositoryFile("test-data", "homebrewery-real", "Backgrounds, Feats & Equipment.txt"));
        var document = CreateDocument(DocumentCategory.Equipment, "Backgrounds, Feats & Equipment");
        var content = new LocalDocumentContent("mixed", document.Title, source, "documents/mixed.txt", "real-mixed");

        var entries = HomebrewSearchIndexer.IndexDocument(document, content);

        Assert.Contains(entries, entry => entry.Name == "Acolyte" && entry.Kind == SearchContentKind.Background);
        Assert.Contains(entries, entry => entry.Name == "Arcane Preparation" && entry.Kind == SearchContentKind.Feat);
        Assert.Contains(entries, entry => entry.Name == "Light Armor" && entry.Kind == SearchContentKind.Equipment);
        Assert.Contains(entries, entry => entry.Name == "Suggested Characteristics" && entry.Kind == SearchContentKind.Other);
    }

    [Fact]
    public void IndexDocument_MakesMarkdownTableCellsSearchable()
    {
        var document = CreateDocument(DocumentCategory.Equipment, "Equipment");
        const string source = "# Equipment\n### Weapons\n| Name | Property |\n|:---|:---|\n| Moonblade | Finesse weapon |";
        var content = new LocalDocumentContent("equipment", "Equipment", source, "documents/equipment.txt", "table");

        var entry = Assert.Single(HomebrewSearchIndexer.IndexDocument(document, content), item => item.Name == "Weapons");

        Assert.Contains("Moonblade", entry.Description);
        Assert.DoesNotContain(":---", entry.Description);
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DndCompendium.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }

    [Fact]
    public void IndexDocument_PreservesSameNamedFeaturesFromDifferentParents()
    {
        var document = CreateDocument(DocumentCategory.Classes, "Warden");
        const string source = """
            # Warden
            ## Subclasses
            ### Sentinel
            #### Defensive Stance
            At 5th level, guard an ally.
            ### Green Knight
            #### Defensive Stance
            At 7th level, root yourself in place.
            """;
        var content = new LocalDocumentContent("warden", "Warden", source, "documents/warden.txt", "duplicates");

        var matches = HomebrewSearchIndexer.IndexDocument(document, content)
            .Where(entry => entry.Name == "Defensive Stance").ToArray();

        Assert.Equal(2, matches.Length);
        Assert.Equal(2, matches.Select(entry => entry.Id).Distinct().Count());
        Assert.Contains(matches, entry => entry.Parent == "Sentinel" && entry.Level == "5");
        Assert.Contains(matches, entry => entry.Parent == "Green Knight" && entry.Level == "7");
    }

    [Fact]
    public void IndexDocument_PreservesSubclassContextWhenContainerAndSubclassesAreSiblingHeadings()
    {
        var document = CreateDocument(DocumentCategory.Classes, "Wizard");
        const string source = """
            # Wizard
            ### Class Features
            ### Arcane Recovery
            Once per day, recover a spell slot.
            ### Arcane Traditions
            Choose a magical tradition.
            ### Arcane Commander
            A tactical wizard.
            #### Tactical Reposition
            At 6th level, move an ally with your reaction.
            ### Beguiler
            A deceptive wizard.
            #### Sneak Spell
            At 2nd level, conceal a spell.
            """;
        var content = new LocalDocumentContent("wizard", "Wizard", source, "documents/wizard.txt", "siblings");

        var entries = HomebrewSearchIndexer.IndexDocument(document, content);

        Assert.Contains(entries, entry => entry.Name == "Arcane Recovery" && entry.Kind == SearchContentKind.ClassFeature);
        Assert.Contains(entries, entry => entry.Name == "Arcane Commander" && entry.Kind == SearchContentKind.Subclass);
        Assert.Contains(entries, entry => entry.Name == "Tactical Reposition"
            && entry.Kind == SearchContentKind.SubclassFeature && entry.ParentSubclass == "Arcane Commander");
        Assert.Contains(entries, entry => entry.Name == "Sneak Spell"
            && entry.Kind == SearchContentKind.SubclassFeature && entry.ParentSubclass == "Beguiler");
    }

    [Theory]
    [InlineData("Gadgeteer", "Tech Specialties", "Futurist", "Recursive Deterrence")]
    [InlineData("Ranger", "Ranger Archetype", "Nomad", "Expunging Strikes")]
    [InlineData("Savant", "Academic Disciplines", "Philosopher", "Power Word: Confound")]
    public void IndexDocument_RecognizesNamedSubclassSectionsAndDeepFeatures(
        string className,
        string sectionName,
        string subclassName,
        string featureName)
    {
        var document = CreateDocument(DocumentCategory.Classes, className);
        var source = $"# {className}\n## {sectionName}\n### {subclassName}\n#### Words of Power\n##### {featureName}\nThe target makes an Intelligence saving throw.";
        var content = new LocalDocumentContent(document.Id, className, source, $"documents/{document.Id}.txt", "named-section");

        var entries = HomebrewSearchIndexer.IndexDocument(document, content);

        Assert.Contains(entries, entry => entry.Name == subclassName && entry.Kind == SearchContentKind.Subclass);
        var feature = Assert.Single(entries, entry => entry.Name == featureName);
        Assert.Equal(SearchContentKind.SubclassFeature, feature.Kind);
        Assert.Equal(subclassName, feature.ParentSubclass);
    }

    [Fact]
    public void IndexDocument_StopsSubclassContextAtASeparateFeatureSystem()
    {
        var document = CreateDocument(DocumentCategory.Classes, "Psion");
        const string source = "# Psion\n## Psionic Archetypes\n### Awakened Mind\n#### Mind Reader\nIntelligence saving throw.\n## Psionic Disciplines\n### Mesmerism Discipline\n#### Illusory Image\nIntelligence saving throw.";
        var content = new LocalDocumentContent("psion", "Psion", source, "documents/psion.txt", "boundary");

        var entries = HomebrewSearchIndexer.IndexDocument(document, content);

        Assert.Contains(entries, entry => entry.Name == "Mind Reader" && entry.Kind == SearchContentKind.SubclassFeature);
        Assert.Contains(entries, entry => entry.Name == "Illusory Image" && entry.Kind != SearchContentKind.SubclassFeature);
    }

    [Fact]
    public void IndexDocument_KeepsMonsterBlockTextInsideItsSubclassFeature()
    {
        var document = CreateDocument(DocumentCategory.Classes, "Vessel");
        const string source = """
            # Vessel
            ## Sealed Spirit
            ### The Trickster
            #### Trickster Archon
            You assume the following form:
            {{monster,frame
            ## Trickster Archon
            ***Twisted Logic.*** A creature must make an Intelligence saving throw.
            }}
            #### Master of Mischief
            At 6th level, your tricks improve.
            """;
        var content = new LocalDocumentContent("vessel", "Vessel", source, "documents/vessel.txt", "monster");

        var entries = HomebrewSearchIndexer.IndexDocument(document, content);

        var archon = Assert.Single(entries, entry => entry.Name == "Trickster Archon");
        Assert.Equal(SearchContentKind.SubclassFeature, archon.Kind);
        Assert.Equal("The Trickster", archon.ParentSubclass);
        Assert.Contains("Intelligence saving throw", archon.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IndexDocument_DoesNotTreatNestedDisciplineSpecializationsAsClassSubclasses()
    {
        var document = CreateDocument(DocumentCategory.Classes, "Psion");
        const string source = "# Psion\n## Psionic Disciplines\n### Psychokinesis Discipline\n### Specializations\n#### Cryokinetic\nCold damage option.";
        var content = new LocalDocumentContent("psion", "Psion", source, "documents/psion.txt", "nested-choice");

        var entries = HomebrewSearchIndexer.IndexDocument(document, content);

        Assert.Contains(entries, entry => entry.Name == "Specializations" && entry.Kind != SearchContentKind.Subclass);
        Assert.Contains(entries, entry => entry.Name == "Cryokinetic" && entry.Kind == SearchContentKind.Other);
    }

    private static LibraryDocument CreateDocument(DocumentCategory category, string title) => new()
    {
        Id = title.ToLowerInvariant(),
        Title = title,
        FileName = $"{title}.txt",
        Category = category,
        Description = "Documento de prueba",
        SourceSizeBytes = 100,
        SourceModifiedAt = DateTimeOffset.UnixEpoch,
        SourceChecksum = string.Empty,
        DownloadState = DocumentDownloadState.Downloaded,
        DownloadedAt = DateTimeOffset.UnixEpoch,
        RelativePath = $"documents/{title}.txt"
    };
}
