using DndCompendium.Infrastructure.Persistence;

namespace DndCompendium.Infrastructure.Tests;

public sealed class JsonReadingHistoryStoreTests
{
    [Fact]
    public async Task KeepsOnlyTheThreeMostRecentlyOpenedDocuments()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonReadingHistoryStore(directory.Path);

        await RecordAsync(store, "one", "One");
        await RecordAsync(store, "two", "Two");
        await RecordAsync(store, "three", "Three");
        await RecordAsync(store, "four", "Four");

        var recent = await store.GetRecentAsync();
        Assert.Equal(["four", "three", "two"], recent.Select(entry => entry.DocumentId));
    }

    [Fact]
    public async Task ReopeningADocumentMovesItToTheFrontWithoutDuplicatingIt()
    {
        using var directory = new TemporaryDirectory();
        using (var store = new JsonReadingHistoryStore(directory.Path))
        {
            await RecordAsync(store, "one", "One");
            await RecordAsync(store, "two", "Two");
            await RecordAsync(store, "one", "One renamed");
        }

        using var restarted = new JsonReadingHistoryStore(directory.Path);
        var recent = await restarted.GetRecentAsync();
        Assert.Equal(["one", "two"], recent.Select(entry => entry.DocumentId));
        Assert.Equal("One renamed", recent[0].Title);
    }

    private static async Task RecordAsync(JsonReadingHistoryStore store, string id, string title)
    {
        await store.RecordOpenedAsync(id, title);
        await Task.Delay(2);
    }
}
