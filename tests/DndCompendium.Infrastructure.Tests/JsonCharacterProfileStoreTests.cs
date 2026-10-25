using DndCompendium.Core.Models;
using DndCompendium.Infrastructure.Persistence;

namespace DndCompendium.Infrastructure.Tests;

public sealed class JsonCharacterProfileStoreTests
{
    [Fact]
    public async Task MigratesLegacySelectionsIntoInitialCharacter()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "spells"));
        Directory.CreateDirectory(Path.Combine(directory.Path, "exploits"));
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "spells", "favorite-spells.json"), "[\"spell-a\"]");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "spells", "prepared-spells.json"), "[\"spell-b\"]");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "exploits", "favorite-exploits.json"), "[\"exploit-a\"]");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "exploits", "prepared-exploits.json"), "[\"exploit-b\"]");

        var store = new JsonCharacterProfileStore(directory.Path);

        Assert.Equal("Mi personaje", (await store.GetActiveAsync()).Name);
        Assert.Contains("spell-a", await store.GetSavedIdsAsync(SavedContentKind.Spell, SavedStateKind.Favorite));
        Assert.Contains("spell-b", await store.GetSavedIdsAsync(SavedContentKind.Spell, SavedStateKind.Prepared));
        Assert.Contains("exploit-a", await store.GetSavedIdsAsync(SavedContentKind.Exploit, SavedStateKind.Favorite));
        Assert.Contains("exploit-b", await store.GetSavedIdsAsync(SavedContentKind.Exploit, SavedStateKind.Prepared));
    }

    [Fact]
    public async Task KeepsFavoritesAndPreparedItemsIsolatedPerCharacter()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonCharacterProfileStore(directory.Path);
        var first = await store.GetActiveAsync();
        await store.SetSavedAsync(SavedContentKind.Spell, SavedStateKind.Favorite, "spell-first", true);

        var second = await store.CreateAsync("Nyx");
        await store.SetSavedAsync(SavedContentKind.Exploit, SavedStateKind.Prepared, "exploit-second", true);
        Assert.Empty(await store.GetSavedIdsAsync(SavedContentKind.Spell, SavedStateKind.Favorite));
        Assert.Contains("exploit-second", await store.GetSavedIdsAsync(SavedContentKind.Exploit, SavedStateKind.Prepared));

        await store.RenameAsync(second.Id, "Nyx Shadow");
        Assert.Equal("Nyx Shadow", (await store.GetActiveAsync()).Name);
        await store.SetActiveAsync(first.Id);
        Assert.Contains("spell-first", await store.GetSavedIdsAsync(SavedContentKind.Spell, SavedStateKind.Favorite));
        Assert.Empty(await store.GetSavedIdsAsync(SavedContentKind.Exploit, SavedStateKind.Prepared));

        await store.DeleteAsync(second.Id);
        Assert.Single(await store.GetAllAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync(first.Id));
    }

    [Fact]
    public async Task PreparedOrderPersistsAndRemainsIsolatedPerCharacter()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonCharacterProfileStore(directory.Path);
        var first = await store.GetActiveAsync();
        await store.SetSavedAsync(SavedContentKind.Spell, SavedStateKind.Prepared, "spell-a", true);
        await store.SetSavedAsync(SavedContentKind.Exploit, SavedStateKind.Prepared, "exploit-b", true);
        await store.SetPreparedOrderAsync([
            new(SavedContentKind.Exploit, "exploit-b"),
            new(SavedContentKind.Spell, "spell-a")
        ]);

        var reloaded = new JsonCharacterProfileStore(directory.Path);
        Assert.Equal([
            new SavedItemReference(SavedContentKind.Exploit, "exploit-b"),
            new SavedItemReference(SavedContentKind.Spell, "spell-a")
        ], await reloaded.GetPreparedOrderAsync());

        await reloaded.SetSavedAsync(SavedContentKind.Exploit, SavedStateKind.Prepared, "exploit-b", false);
        Assert.Equal(
            new SavedItemReference(SavedContentKind.Spell, "spell-a"),
            Assert.Single(await reloaded.GetPreparedOrderAsync()));

        await reloaded.CreateAsync("Nyx");
        Assert.Empty(await reloaded.GetPreparedOrderAsync());
        await reloaded.SetActiveAsync(first.Id);
        Assert.Equal("spell-a", Assert.Single(await reloaded.GetPreparedOrderAsync()).ItemId);
    }

    [Fact]
    public async Task FavoriteOrderPersistsAndRemainsIsolatedPerCharacter()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonCharacterProfileStore(directory.Path);
        var first = await store.GetActiveAsync();
        await store.SetSavedAsync(SavedContentKind.Spell, SavedStateKind.Favorite, "spell-a", true);
        await store.SetSavedAsync(SavedContentKind.Exploit, SavedStateKind.Favorite, "exploit-b", true);
        await store.SetFavoriteOrderAsync([
            new(SavedContentKind.Exploit, "exploit-b"),
            new(SavedContentKind.Spell, "spell-a")
        ]);

        var reloaded = new JsonCharacterProfileStore(directory.Path);
        Assert.Equal([
            new SavedItemReference(SavedContentKind.Exploit, "exploit-b"),
            new SavedItemReference(SavedContentKind.Spell, "spell-a")
        ], await reloaded.GetFavoriteOrderAsync());

        await reloaded.SetSavedAsync(SavedContentKind.Exploit, SavedStateKind.Favorite, "exploit-b", false);
        Assert.Equal(
            new SavedItemReference(SavedContentKind.Spell, "spell-a"),
            Assert.Single(await reloaded.GetFavoriteOrderAsync()));

        await reloaded.CreateAsync("Nyx");
        Assert.Empty(await reloaded.GetFavoriteOrderAsync());
        await reloaded.SetActiveAsync(first.Id);
        Assert.Equal("spell-a", Assert.Single(await reloaded.GetFavoriteOrderAsync()).ItemId);
    }

    [Fact]
    public async Task MigratesPreparedOrderFromVersionTwoProfiles()
    {
        using var directory = new TemporaryDirectory();
        var profilesDirectory = Path.Combine(directory.Path, "profiles");
        Directory.CreateDirectory(profilesDirectory);
        const string state = """
            {
              "Version": 2,
              "ActiveCharacterId": "hero",
              "Characters": [{ "Id": "hero", "Name": "Nyx", "CreatedAt": "2026-10-20T00:00:00Z" }],
              "Saved": [{
                "CharacterId": "hero",
                "PreparedSpellIds": ["spell-b"],
                "PreparedExploitIds": ["exploit-a"]
              }]
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(profilesDirectory, "characters.json"), state);

        var store = new JsonCharacterProfileStore(directory.Path);
        var order = await store.GetPreparedOrderAsync();

        Assert.Equal(2, order.Count);
        Assert.Contains(new SavedItemReference(SavedContentKind.Spell, "spell-b"), order);
        Assert.Contains(new SavedItemReference(SavedContentKind.Exploit, "exploit-a"), order);
    }

    [Fact]
    public async Task MigratesFavoriteOrderFromVersionThreeProfiles()
    {
        using var directory = new TemporaryDirectory();
        var profilesDirectory = Path.Combine(directory.Path, "profiles");
        Directory.CreateDirectory(profilesDirectory);
        const string state = """
            {
              "Version": 3,
              "ActiveCharacterId": "hero",
              "Characters": [{ "Id": "hero", "Name": "Nyx", "CreatedAt": "2026-10-20T00:00:00Z" }],
              "Saved": [{
                "CharacterId": "hero",
                "FavoriteSpellIds": ["spell-b"],
                "FavoriteExploitIds": ["exploit-a"]
              }]
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(profilesDirectory, "characters.json"), state);

        var store = new JsonCharacterProfileStore(directory.Path);
        var order = await store.GetFavoriteOrderAsync();

        Assert.Equal(2, order.Count);
        Assert.Contains(new SavedItemReference(SavedContentKind.Spell, "spell-b"), order);
        Assert.Contains(new SavedItemReference(SavedContentKind.Exploit, "exploit-a"), order);
    }

    [Fact]
    public async Task PersistsResourcesAndKeepsThemIsolatedPerCharacter()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonCharacterProfileStore(directory.Path);
        var first = await store.GetActiveAsync();
        var dice = await store.UpsertResourceAsync(new CharacterResource
        {
            Id = string.Empty,
            Name = "Exploit Dice",
            Maximum = 5,
            Current = 3,
            Recovery = ResourceRecoveryKind.ShortAndLongRest,
            RecoveryAmount = 2
        });
        await store.AdjustResourceAsync(dice.Id, -1);

        var second = await store.CreateAsync("Nyx");
        Assert.Empty(await store.GetResourcesAsync());
        await store.UpsertResourceAsync(new CharacterResource
        {
            Id = string.Empty,
            Name = "Ki Points",
            Maximum = 6,
            Current = 1,
            Recovery = ResourceRecoveryKind.ShortRest
        });

        await store.SetActiveAsync(first.Id);
        var reloadedStore = new JsonCharacterProfileStore(directory.Path);
        var persisted = Assert.Single(await reloadedStore.GetResourcesAsync());
        Assert.Equal("Exploit Dice", persisted.Name);
        Assert.Equal(2, persisted.Current);
        Assert.DoesNotContain((await reloadedStore.GetResourcesAsync()), resource => resource.Name == "Ki Points");

        var preview = Assert.Single(await reloadedStore.PreviewRestAsync(RestKind.Short));
        Assert.Equal(4, preview.Current);
        await reloadedStore.ApplyRestAsync(RestKind.Short);
        Assert.Equal(4, Assert.Single(await reloadedStore.GetResourcesAsync()).Current);
        Assert.Equal("Nyx", second.Name);
    }

    [Fact]
    public async Task SpellAndPactSlotsRemainSeparate()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonCharacterProfileStore(directory.Path);
        await store.UpsertResourceAsync(Slot(CharacterResourceKind.SpellSlot, 2, 3));
        await store.UpsertResourceAsync(Slot(CharacterResourceKind.PactMagicSlot, 2, 2));

        var resources = await store.GetResourcesAsync();
        Assert.Equal(2, resources.Count);
        Assert.Contains(resources, resource => resource.Kind == CharacterResourceKind.SpellSlot);
        Assert.Contains(resources, resource => resource.Kind == CharacterResourceKind.PactMagicSlot);
    }

    [Fact]
    public async Task ResourceOrderPersistsAndRemainsIsolatedPerCharacter()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonCharacterProfileStore(directory.Path);
        var first = await store.GetActiveAsync();
        var firstResource = await store.UpsertResourceAsync(new CharacterResource
        {
            Id = string.Empty, Name = "Exploit Dice", Maximum = 4, Current = 4
        });
        var secondResource = await store.UpsertResourceAsync(new CharacterResource
        {
            Id = string.Empty, Name = "Sorcery Points", Maximum = 6, Current = 6
        });

        await store.SetResourceOrderAsync([secondResource.Id, firstResource.Id]);
        var reloaded = new JsonCharacterProfileStore(directory.Path);
        Assert.Equal([secondResource.Id, firstResource.Id],
            (await reloaded.GetResourcesAsync()).Select(resource => resource.Id));

        await reloaded.CreateAsync("Nyx");
        Assert.Empty(await reloaded.GetResourcesAsync());
        await reloaded.SetActiveAsync(first.Id);
        Assert.Equal([secondResource.Id, firstResource.Id],
            (await reloaded.GetResourcesAsync()).Select(resource => resource.Id));
    }

    [Fact]
    public async Task CorruptProfileIsBackedUpAndReplacedWithSafeDefault()
    {
        using var directory = new TemporaryDirectory();
        var profilesDirectory = Path.Combine(directory.Path, "profiles");
        Directory.CreateDirectory(profilesDirectory);
        await File.WriteAllTextAsync(Path.Combine(profilesDirectory, "characters.json"), "{not-valid-json");

        var store = new JsonCharacterProfileStore(directory.Path);

        Assert.Equal("Mi personaje", (await store.GetActiveAsync()).Name);
        Assert.Empty(await store.GetResourcesAsync());
        Assert.Single(Directory.GetFiles(profilesDirectory, "characters.json.corrupt-*"));
    }

    [Fact]
    public async Task InvalidResourceIsIgnoredWithoutLosingExistingSelections()
    {
        using var directory = new TemporaryDirectory();
        var profilesDirectory = Path.Combine(directory.Path, "profiles");
        Directory.CreateDirectory(profilesDirectory);
        const string state = """
            {
              "Version": 2,
              "ActiveCharacterId": "hero",
              "Characters": [{ "Id": "hero", "Name": "Nyx", "CreatedAt": "2026-10-20T00:00:00Z" }],
              "Saved": [{
                "CharacterId": "hero",
                "FavoriteSpellIds": ["fireball|3"],
                "PreparedSpellIds": [],
                "FavoriteExploitIds": [],
                "PreparedExploitIds": [],
                "Resources": [{ "Id": "broken", "Name": "", "Kind": 0, "Maximum": 4, "Current": 2 }]
              }]
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(profilesDirectory, "characters.json"), state);

        var store = new JsonCharacterProfileStore(directory.Path);

        Assert.Empty(await store.GetResourcesAsync());
        Assert.Contains("fireball|3", await store.GetSavedIdsAsync(SavedContentKind.Spell, SavedStateKind.Favorite));
    }

    private static CharacterResource Slot(CharacterResourceKind kind, int level, int maximum) => new()
    {
        Id = string.Empty,
        Name = kind == CharacterResourceKind.SpellSlot ? $"Spell Slots · Nivel {level}" : $"Pact Magic · Nivel {level}",
        Kind = kind,
        Level = level,
        Maximum = maximum,
        Current = maximum,
        Recovery = kind == CharacterResourceKind.PactMagicSlot
            ? ResourceRecoveryKind.ShortAndLongRest
            : ResourceRecoveryKind.LongRest
    };
}
