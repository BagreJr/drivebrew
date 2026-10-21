using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using DndCompendium.Infrastructure.Persistence;
using DndCompendium.Infrastructure.Sources;

namespace DndCompendium.Infrastructure.Tests;

public sealed class UniversalSearchServiceTests
{
    [Fact]
    public async Task Search_IsCaseAccentAliasAndDescriptionAwareWithExpectedRanking()
    {
        using var directory = new TemporaryDirectory();
        await using var documents = new LocalDocumentRepository(directory.Path);
        var spells = new MemorySpellRepository([AcidSplash]);
        var exploits = new MemoryExploitRepository([]);
        using var search = new UniversalSearchService(directory.Path, documents, spells, exploits, new JsonCharacterProfileStore(directory.Path));
        await search.RefreshIndexAsync();

        var phrase = await search.SearchAsync("DEXTERITY SAVING THROW");
        var alias = await search.SearchAsync("dex save");
        var typo = await search.SearchAsync("Acid Splach");

        Assert.Equal("Acid Splash", Assert.Single(phrase).Entry.Name);
        Assert.Equal("Acid Splash", Assert.Single(alias).Entry.Name);
        Assert.Equal("Acid Splash", Assert.Single(typo).Entry.Name);
        Assert.Contains("Dexterity", phrase[0].Snippet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_CaseVariantsReturnTheSameFullTextResultSet()
    {
        using var directory = new TemporaryDirectory();
        await using var documents = new LocalDocumentRepository(directory.Path);
        using var search = new UniversalSearchService(directory.Path, documents,
            new MemorySpellRepository([AcidSplash]), new MemoryExploitRepository([]), new JsonCharacterProfileStore(directory.Path));
        await search.RefreshIndexAsync();

        var upper = await search.SearchAsync("DEXTERITY SAVING THROW");
        var lower = await search.SearchAsync("dexterity saving throw");
        var mixed = await search.SearchAsync("DexTeRiTy SaViNg ThRoW");

        Assert.Equal(lower.Select(result => result.Entry.Id), upper.Select(result => result.Entry.Id));
        Assert.Equal(lower.Select(result => result.Entry.Id), mixed.Select(result => result.Entry.Id));
        Assert.Equal("Acid Splash", Assert.Single(lower).Entry.Name);
    }

    [Fact]
    public async Task Search_RequiresEveryWordOfAMultiwordQueryInsideTheSameEntry()
    {
        using var directory = new TemporaryDirectory();
        await using var documents = new LocalDocumentRepository(directory.Path);
        var spells = new MemorySpellRepository([
            AcidSplash with { Id = "fire-reaction", Name = "Flame Guard", Effect = "Use a reaction to deal fire damage." },
            AcidSplash with { Id = "fire-only", Name = "Flame Bolt", Effect = "Deal fire damage." },
            AcidSplash with { Id = "reaction-only", Name = "Quick Guard", Effect = "Use your reaction." }
        ]);
        using var search = new UniversalSearchService(directory.Path, documents, spells,
            new MemoryExploitRepository([]), new JsonCharacterProfileStore(directory.Path));
        await search.RefreshIndexAsync();

        var results = await search.SearchAsync("fire damage reaction");

        Assert.Equal("Flame Guard", Assert.Single(results).Entry.Name);
    }

    [Fact]
    public async Task Search_IndexesAllRelevantSpellMetadataAndDeduplicatesRepeatedReferences()
    {
        using var directory = new TemporaryDirectory();
        await using var documents = new LocalDocumentRepository(directory.Path);
        using var search = new UniversalSearchService(directory.Path, documents,
            new MemorySpellRepository([AcidSplash, AcidSplash]), new MemoryExploitRepository([]), new JsonCharacterProfileStore(directory.Path));
        await search.RefreshIndexAsync();

        Assert.Equal("Acid Splash", Assert.Single(await search.SearchAsync("60 feet instantaneous")).Entry.Name);
        Assert.Equal("Acid Splash", Assert.Single(await search.SearchAsync("conjuration verbal somatic")).Entry.Name);
        Assert.Single(await search.SearchAsync("Acid Splash"));
    }

    [Fact]
    public async Task Search_RanksLiteralDescriptionPhraseAheadOfSeparatedWordsAndFuzzyNames()
    {
        using var directory = new TemporaryDirectory();
        await using var documents = new LocalDocumentRepository(directory.Path);
        var literal = AcidSplash;
        var separated = AcidSplash with
        {
            Id = "evasive-acid", Name = "Evasive Acid", Effect = "Make a Dexterity check. On failure, resolve a saving effect and throw acid."
        };
        using var search = new UniversalSearchService(directory.Path, documents,
            new MemorySpellRepository([separated, literal]), new MemoryExploitRepository([]), new JsonCharacterProfileStore(directory.Path));
        await search.RefreshIndexAsync();

        var results = await search.SearchAsync("Dexterity Saving Throw");

        Assert.Equal("Acid Splash", results[0].Entry.Name);
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public async Task Search_ClassifiesDocumentFeaturesWithTheirFullHierarchy()
    {
        using var directory = new TemporaryDirectory();
        const string text = "# Alchemist\n## Fields of Study\n### Amorist\n#### Magnetic Personality\nAt 6th level, your charming formula forces a Wisdom saving throw.";
        var source = TestDocuments.Create("alchemist", "Alchemist", "Alchemist.txt", text);
        await using var documents = new LocalDocumentRepository(directory.Path);
        await documents.UpsertCatalogAsync([source]);
        await using (var stream = TestDocuments.Stream(text)) await documents.InstallAsync(source, stream);
        using var search = new UniversalSearchService(directory.Path, documents,
            new MemorySpellRepository([]), new MemoryExploitRepository([]), new JsonCharacterProfileStore(directory.Path));
        await search.RefreshIndexAsync();

        var feature = Assert.Single(await search.SearchAsync("Magnetic Personality"));

        Assert.Equal(SearchContentKind.SubclassFeature, feature.Entry.Kind);
        Assert.Equal("Alchemist", feature.Entry.ParentClass);
        Assert.Equal("Amorist", feature.Entry.ParentSubclass);
    }

    [Fact]
    public async Task Search_SubclassFilterIncludesSubclassFeaturesForIntelligenceSavingThrow()
    {
        using var directory = new TemporaryDirectory();
        const string text = "# Ranger\n## Ranger Archetype\n### Nomad\n#### Expunging Strikes\nThe target must make an Intelligence saving throw.";
        var source = TestDocuments.Create("ranger", "Ranger", "Ranger.txt", text);
        await using var documents = new LocalDocumentRepository(directory.Path);
        await documents.UpsertCatalogAsync([source]);
        await using (var stream = TestDocuments.Stream(text)) await documents.InstallAsync(source, stream);
        using var search = new UniversalSearchService(directory.Path, documents,
            new MemorySpellRepository([]), new MemoryExploitRepository([]), new JsonCharacterProfileStore(directory.Path));
        await search.RefreshIndexAsync();

        var results = await search.SearchAsync(new SearchRequest
        {
            Query = "Intelligence Saving Throw",
            Kind = SearchContentKind.Subclass,
            Limit = 20
        });

        var feature = Assert.Single(results);
        Assert.Equal("Expunging Strikes", feature.Entry.Name);
        Assert.Equal(SearchContentKind.SubclassFeature, feature.Entry.Kind);
        Assert.Equal("Nomad", feature.Entry.ParentSubclass);
    }

    [Fact]
    public async Task Search_UsesTheRealOfflineSpellAndExploitCatalogs()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "spells"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "exploits"));
        File.Copy(FindRepositoryFile("artifacts", "spells.json"), Path.Combine(directory.Path, "spells", "spells.json"));
        File.Copy(FindRepositoryFile("artifacts", "exploit.json"), Path.Combine(directory.Path, "exploits", "exploit.json"));
        var profiles = new JsonCharacterProfileStore(directory.Path);
        using var spells = new GoogleDriveSpellRepository(directory.Path, new HttpClient(), profiles);
        using var exploits = new GoogleDriveExploitRepository(directory.Path, new HttpClient(), profiles);
        await using var documents = new LocalDocumentRepository(directory.Path);
        using var search = new UniversalSearchService(directory.Path, documents, spells, exploits, profiles);

        await search.RefreshIndexAsync();
        var savingThrow = await search.SearchAsync("DEXTERITY SAVING THROW");
        var defensiveStance = await search.SearchAsync("Defensive Stance");

        Assert.Contains(savingThrow, result => result.Entry.Name == "Acid Splash" && result.Entry.Kind == SearchContentKind.Spell);
        Assert.Contains(defensiveStance, result => result.Entry.Name == "Defensive Stance" && result.Entry.Kind == SearchContentKind.Exploit);
    }

    [Fact]
    public async Task Search_RestrainedWithSpellFilterReturnsSafeRealCatalogSnippets()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "spells"));
        File.Copy(FindRepositoryFile("artifacts", "spells.json"), Path.Combine(directory.Path, "spells", "spells.json"));
        var profiles = new JsonCharacterProfileStore(directory.Path);
        using var spells = new GoogleDriveSpellRepository(directory.Path, new HttpClient(), profiles);
        await using var documents = new LocalDocumentRepository(directory.Path);
        using var search = new UniversalSearchService(directory.Path, documents, spells,
            new MemoryExploitRepository([]), profiles);

        await search.RefreshIndexAsync();
        var results = await search.SearchAsync(new SearchRequest
        {
            Query = "Restrained",
            Kind = SearchContentKind.Spell,
            Limit = 120
        });

        Assert.NotEmpty(results);
        Assert.All(results, result =>
        {
            Assert.Equal(SearchContentKind.Spell, result.Entry.Kind);
            Assert.False(string.IsNullOrWhiteSpace(result.Snippet));
            Assert.InRange(result.Snippet.Length, 1, 222);
        });
    }

    [Fact]
    public async Task Search_AppliesCategoryClassAndLevelFiltersWithoutRemovingLegitimateDuplicates()
    {
        using var directory = new TemporaryDirectory();
        const string text = "# Warden\n### Defensive Stance\nAt 7th level, use your reaction to guard an ally.";
        var source = TestDocuments.Create("warden", "Warden", "Warden.txt", text);
        await using var documents = new LocalDocumentRepository(directory.Path);
        await documents.UpsertCatalogAsync([source]);
        await using (var stream = TestDocuments.Stream(text)) await documents.InstallAsync(source, stream);
        var exploits = new MemoryExploitRepository([DefensiveStance]);
        using var search = new UniversalSearchService(directory.Path, documents, new MemorySpellRepository([]), exploits, new JsonCharacterProfileStore(directory.Path));
        await search.RefreshIndexAsync();

        var all = await search.SearchAsync("Defensive Stance");
        var filtered = await search.SearchAsync(new SearchRequest
        {
            Query = "Defensive Stance",
            Kind = SearchContentKind.Exploit,
            Origin = "Fighter",
            Level = "5",
            Limit = 20
        });

        Assert.True(all.Count >= 2);
        Assert.Single(filtered);
        Assert.Equal(SearchContentKind.Exploit, filtered[0].Entry.Kind);
    }

    [Fact]
    public async Task Refresh_IsIncrementalAndKeepsPreviousIndexWhenCatalogUpdateFails()
    {
        using var directory = new TemporaryDirectory();
        await using var documents = new LocalDocumentRepository(directory.Path);
        var spells = new MemorySpellRepository([AcidSplash]);
        using var search = new UniversalSearchService(directory.Path, documents, spells, new MemoryExploitRepository([]), new JsonCharacterProfileStore(directory.Path));
        await search.RefreshIndexAsync();
        var originalCount = await search.GetEntryCountAsync();

        spells.Items = [AcidSplash, AcidSplash with { Id = "misty-step", Name = "Misty Step", Effect = "Teleport 30 feet." }];
        await search.RefreshIndexAsync();
        Assert.Equal("Misty Step", Assert.Single(await search.SearchAsync("teleport 30 feet")).Entry.Name);

        spells.ThrowOnRead = true;
        await search.RefreshIndexAsync();
        Assert.NotEmpty(await search.SearchAsync("Misty Step"));
        Assert.True(await search.GetEntryCountAsync() > originalCount);
    }

    [Fact]
    public async Task Refresh_AddsAndRemovesCatalogItemsWithoutChangingProfilesOrFavorites()
    {
        using var directory = new TemporaryDirectory();
        await using var documents = new LocalDocumentRepository(directory.Path);
        var profiles = new JsonCharacterProfileStore(directory.Path);
        var character = await profiles.CreateAsync("Mira");
        await profiles.SetActiveAsync(character.Id);
        await profiles.SetSavedAsync(SavedContentKind.Spell, SavedStateKind.Favorite, AcidSplash.Id, true);
        var spells = new MemorySpellRepository([AcidSplash]);
        using var search = new UniversalSearchService(directory.Path, documents, spells,
            new MemoryExploitRepository([]), profiles);
        await search.RefreshIndexAsync();
        Assert.True(Assert.Single(await search.SearchAsync("Acid Splash")).IsFavorite);

        spells.Items = [AcidSplash with { Id = "misty-step", Name = "Misty Step", Effect = "Teleport up to 30 feet." }];
        await search.RefreshIndexAsync();

        Assert.Empty(await search.SearchAsync("Acid Splash"));
        Assert.Equal("Misty Step", Assert.Single(await search.SearchAsync("Teleport up to 30 feet")).Entry.Name);
        Assert.Equal(character.Id, (await profiles.GetActiveAsync()).Id);
        Assert.Contains(AcidSplash.Id, await profiles.GetSavedIdsAsync(SavedContentKind.Spell, SavedStateKind.Favorite));
    }

    private static readonly Spell AcidSplash = new()
    {
        Id = "acid-splash", Name = "Acid Splash", Level = 0, School = "Conjuration",
        CastingTime = "1 action", Range = "60 feet", Duration = "Instantaneous",
        IsConcentration = false, IsRitual = false, HasVerbalComponent = true,
        HasSomaticComponent = true, HasMaterialComponent = false, MaterialDescription = "—",
        Effect = "A target must succeed on a Dexterity saving throw or take acid damage.",
        HigherLevels = "—", Classes = ["Sorcerer", "Wizard"], Origins = ["Arcane"]
    };

    private static readonly ExploitEntry DefensiveStance = new()
    {
        Id = "defensive-stance", Name = "Defensive Stance", Level = 5, LevelLabel = "5",
        Type = "Exploit", Prerequisite = "—", Effect = "As a bonus action, enter a defensive stance.",
        Classes = ["Fighter", "Warden"]
    };

    private static string FindRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DndCompendium.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }

    private sealed class MemorySpellRepository(IReadOnlyList<Spell> items) : ISpellRepository
    {
        public IReadOnlyList<Spell> Items { get; set; } = items;
        public bool ThrowOnRead { get; set; }
        public Task<IReadOnlyList<Spell>> GetAllAsync(bool refreshFromDrive = true, CancellationToken cancellationToken = default)
            => ThrowOnRead ? Task.FromException<IReadOnlyList<Spell>>(new IOException("offline")) : Task.FromResult(Items);
        public Task<IReadOnlyList<Spell>> UpdateFromDriveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Items);
        public Task<IReadOnlySet<string>> GetPreparedIdsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task SetPreparedAsync(string spellId, bool isPrepared, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlySet<string>> GetFavoriteIdsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task SetFavoriteAsync(string spellId, bool isFavorite, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MemoryExploitRepository(IReadOnlyList<ExploitEntry> items) : IExploitRepository
    {
        public Task<IReadOnlyList<ExploitEntry>> GetAllAsync(bool refreshFromDrive = true, CancellationToken cancellationToken = default) => Task.FromResult(items);
        public Task<IReadOnlyList<ExploitEntry>> UpdateFromDriveAsync(CancellationToken cancellationToken = default) => Task.FromResult(items);
        public Task<IReadOnlySet<string>> GetPreparedIdsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task SetPreparedAsync(string exploitId, bool isPrepared, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlySet<string>> GetFavoriteIdsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
        public Task SetFavoriteAsync(string exploitId, bool isFavorite, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
