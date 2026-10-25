using System.Buffers.Binary;
using System.Text.Json;

namespace DndCompendium.Infrastructure.Tests;

public sealed class CharacterSheetVisualLayoutTests
{
    [Fact]
    public void Layout_CoversEveryEditablePdfWidgetAcrossThreePages()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sheet", "layout.js"));
        var start = script.IndexOf('{');
        var end = script.LastIndexOf(';');
        Assert.True(start >= 0 && end > start);
        using var document = JsonDocument.Parse(script.Substring(start, end - start));
        var root = document.RootElement;
        var pages = root.GetProperty("pages").EnumerateArray().ToArray();
        var fields = root.GetProperty("fields").EnumerateArray().ToArray();

        Assert.Equal(3, pages.Length);
        Assert.Equal(332, fields.Length);
        Assert.Equal(210, fields.Count(field => field.GetProperty("kind").GetString() is "text" or "textarea"));
        Assert.Equal(122, fields.Count(field => field.GetProperty("kind").GetString() == "checkbox"));
        Assert.Equal(fields.Length, fields.Select(field => field.GetProperty("name").GetString()).Distinct().Count());

        foreach (var field in fields)
        {
            Assert.InRange(field.GetProperty("page").GetInt32(), 1, 3);
            Assert.InRange(field.GetProperty("x").GetDouble(), 0, 100);
            Assert.InRange(field.GetProperty("y").GetDouble(), 0, 100);
            Assert.InRange(field.GetProperty("w").GetDouble(), 0.01, 100);
            Assert.InRange(field.GetProperty("h").GetDouble(), 0.01, 100);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PageImage_MatchesOriginalPdfAspectAndResolution(int page)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sheet", $"character-sheet-{page}.png");
        var header = File.ReadAllBytes(path).AsSpan(0, 24);

        Assert.Equal(1224, BinaryPrimitives.ReadInt32BigEndian(header[16..20]));
        Assert.Equal(1584, BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
    }
}
