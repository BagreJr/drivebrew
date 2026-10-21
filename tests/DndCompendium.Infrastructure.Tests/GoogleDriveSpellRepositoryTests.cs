using System.Net;
using System.Text;
using DndCompendium.Infrastructure.Sources;

namespace DndCompendium.Infrastructure.Tests;

public sealed class GoogleDriveSpellRepositoryTests
{
    private const string Json = """
        [
          {
            "Nombre": "Fireball",
            "Nivel": "3",
            "D": "2",
            "P": "2",
            "A": "1",
            "Escuela": "evocation",
            "Casting time": "action",
            "Range": "150 feet",
            "duracion": "instantaneous",
            "concentracion": "no",
            "efecto": "A bright streak flashes.",
            "level alto": "Damage increases by 1d6.",
            "verbal": "si",
            "somatico": "si",
            "material": "si",
            "descripcion material": "Sulfur and bat guano.",
            "ritual": "no",
            "sorcerer": "1",
            "wizard": "1"
          },
          {
            "Nombre": "Bless",
            "Nivel": "1",
            "D": "1",
            "P": "2",
            "A": "2",
            "Escuela": "enchantment",
            "Casting time": "action",
            "Range": "30 feet",
            "duracion": "1 minute",
            "concentracion": "si",
            "efecto": "Bless three creatures.",
            "level alto": "One additional target.",
            "verbal": "si",
            "somatico": "si",
            "material": "si",
            "descripcion material": "Holy water.",
            "ritual": "no",
            "clerigo": "1",
            "paladin": "1"
          }
        ]
        """;

    [Fact]
    public async Task DownloadsParsesCachesAndPersistsPreparedSpells()
    {
        using var directory = new TemporaryDirectory();
        var bytes = Encoding.UTF8.GetBytes(Json);
        using (var repository = new GoogleDriveSpellRepository(
                   directory.Path,
                   new HttpClient(new JsonHandler(bytes))))
        {
            var spells = await repository.GetAllAsync();

            Assert.Equal(2, spells.Count);
            var fireball = Assert.Single(spells, spell => spell.Name == "Fireball");
            Assert.Equal(3, fireball.Level);
            Assert.Equal(["Arcano"], fireball.Origins);
            Assert.Equal(["Sorcerer", "Wizard"], fireball.Classes);
            Assert.True(fireball.HasMaterialComponent);

            await repository.SetPreparedAsync(fireball.Id, true);
            Assert.Contains(fireball.Id, await repository.GetPreparedIdsAsync());
            await repository.SetFavoriteAsync(fireball.Id, true);
            Assert.Contains(fireball.Id, await repository.GetFavoriteIdsAsync());
        }

        using var offline = new GoogleDriveSpellRepository(
            directory.Path,
            new HttpClient(new OfflineHandler()));
        var cached = await offline.GetAllAsync();
        Assert.Equal(2, cached.Count);
        Assert.Contains(cached.Single(spell => spell.Name == "Fireball").Id, await offline.GetPreparedIdsAsync());
        Assert.Contains(cached.Single(spell => spell.Name == "Fireball").Id, await offline.GetFavoriteIdsAsync());
    }

    private sealed class JsonHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = request.Method == HttpMethod.Head
                ? new ByteArrayContent([])
                : new ByteArrayContent(bytes);
            content.Headers.ContentLength = bytes.LongLength;
            content.Headers.LastModified = new DateTimeOffset(2026, 10, 19, 4, 36, 42, TimeSpan.Zero);
            content.Headers.ContentType = new("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new HttpRequestException("Offline fixture.");
    }
}
