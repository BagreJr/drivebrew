using DndCompendium.Core.Models;
using DndCompendium.App.Services;
using DndCompendium.Infrastructure.Persistence;

namespace DndCompendium.Infrastructure.Tests;

public sealed class PdfSharpCharacterSheetPdfServiceTests
{
    [Fact]
    public async Task WritesAndReadsTextMultilineAndCheckboxFields()
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "character_sheet_template.pdf");
        var data = new CharacterSheetData
        {
            TextFields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CharacterName"] = "Nyx",
                ["ClassLevel"] = "Wizard 5",
                ["Features and Traits"] = "Darkvision\nArcane Recovery",
                ["Spells 1014"] = "Mage Hand"
            },
            CheckedFields = new HashSet<string>(StringComparer.Ordinal)
            {
                "Check Box 20",
                "Check Box 314"
            }
        };
        var service = new PdfSharpCharacterSheetPdfService();

        await using var template = File.OpenRead(templatePath);
        await using var output = new MemoryStream();
        await service.WriteAsync(template, output, data);

        Assert.True(output.Length > 100_000);
        output.Position = 0;
        var reloaded = await service.ReadAsync(output);
        Assert.Equal("Nyx", reloaded.TextFields["CharacterName"]);
        Assert.Equal("Wizard 5", reloaded.TextFields["ClassLevel"]);
        Assert.Equal("Darkvision\nArcane Recovery", reloaded.TextFields["Features and Traits"]);
        Assert.Equal("Mage Hand", reloaded.TextFields["Spells 1014"]);
        Assert.Contains("Check Box 20", reloaded.CheckedFields);
        Assert.Contains("Check Box 314", reloaded.CheckedFields);
    }

    [Fact]
    public async Task SchemaCoversAndWritesEveryEditableTemplateField()
    {
        var definitions = CharacterSheetSchema.Sections.SelectMany(section => section.Fields).ToArray();
        var textNames = definitions
            .Where(field => !string.IsNullOrEmpty(field.FieldName))
            .SelectMany(field => new[] { field.FieldName }.Concat(field.MirrorFieldNames ?? []))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var checkNames = definitions
            .Select(field => field.ToggleFieldName)
            .Where(name => name is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(210, textNames.Length);
        Assert.Equal(122, checkNames.Length);

        var data = new CharacterSheetData
        {
            TextFields = textNames.ToDictionary(name => name, _ => "1", StringComparer.Ordinal),
            CheckedFields = checkNames.ToHashSet(StringComparer.Ordinal)
        };
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "character_sheet_template.pdf");
        var outputPath = Path.Combine(AppContext.BaseDirectory, "character-sheet-filled-test.pdf");
        var service = new PdfSharpCharacterSheetPdfService();
        await using (var template = File.OpenRead(templatePath))
        await using (var output = File.Create(outputPath))
            await service.WriteAsync(template, output, data);

        await using var input = File.OpenRead(outputPath);
        var reloaded = await service.ReadAsync(input);
        Assert.All(textNames, name => Assert.Equal("1", reloaded.TextFields[name]));
        Assert.All(checkNames, name => Assert.Contains(name, reloaded.CheckedFields));
    }
}
