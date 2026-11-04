using DndCompendium.Core.Models;
using DndCompendium.App.Services;
using DndCompendium.Infrastructure.Persistence;

namespace DndCompendium.Infrastructure.Tests;

public sealed class PdfSharpCharacterSheetPdfServiceTests
{
    [Fact]
    public async Task DigitalFieldsAndReferences_AreExportedOnSupplementalPages()
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "character_sheet_template.pdf");
        var data = new CharacterSheetData
        {
            TextFields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CharacterName"] = "Nyx",
                ["Digital.Subclass"] = "Chronurgy",
                ["Digital.ClassFeatures"] = "Arcane Recovery\nChronal Shift"
            },
            Features =
            [
                new CharacterFeatureReference
                {
                    Id = "feature:chronal-shift",
                    Name = "Chronal Shift",
                    Kind = "SubclassFeature",
                    Level = "2"
                }
            ]
        };
        var service = new PdfSharpCharacterSheetPdfService();

        await using var template = File.OpenRead(templatePath);
        await using var output = new MemoryStream();
        await service.WriteAsync(template, output, data);

        output.Position = 0;
        using var exported = PdfSharp.Pdf.IO.PdfReader.Open(output, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.True(exported.PageCount >= 4);
    }

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
    public async Task ManualPlayValues_RoundTripThroughOriginalAcroForm()
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "character_sheet_template.pdf");
        var data = new CharacterSheetData
        {
            TextFields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CharacterName"] = "Nyx",
                ["HPCurrent"] = "17",
                ["HPMax"] = "31",
                ["HPTemp"] = "6",
                ["HD"] = "3d8",
                ["HDTotal"] = "5d8",
                ["Inspiration"] = "1"
            }
        };
        var service = new PdfSharpCharacterSheetPdfService();

        await using var template = File.OpenRead(templatePath);
        await using var output = new MemoryStream();
        await service.WriteAsync(template, output, data);
        output.Position = 0;
        var restored = await service.ReadAsync(output);

        Assert.Equal("17", restored.TextFields["HPCurrent"]);
        Assert.Equal("31", restored.TextFields["HPMax"]);
        Assert.Equal("6", restored.TextFields["HPTemp"]);
        Assert.Equal("3d8", restored.TextFields["HD"]);
        Assert.Equal("5d8", restored.TextFields["HDTotal"]);
        Assert.Equal("1", restored.TextFields["Inspiration"]);
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

    [Fact]
    public async Task StructuredSelectionsAndSpellSnapshots_RoundTripInsidePdfMetadata()
    {
        var templatePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "character_sheet_template.pdf");
        var data = new CharacterSheetData
        {
            TextFields = new Dictionary<string, string> { ["CharacterName"] = "Nyx" },
            Selections =
            [
                new CharacterCompendiumSelection
                {
                    Id = "selection:Race:elf", ReferenceId = "race:elf", Kind = "Race", Name = "Elf",
                    SourceKey = "document:elf", DocumentId = "elf", Target = "elf"
                }
            ],
            Effects =
            [
                new CharacterSheetEffect
                {
                    Id = "elf-dex", SourceSelectionId = "selection:Race:elf", SourceName = "Elf",
                    EffectKind = "AbilityBonus", Target = "DEX", Value = 2
                }
            ],
            Spells =
            [
                new CharacterSpellReference { Id = "magic-missile", Name = "Magic Missile", Level = 1, IsKnown = true }
            ]
        };
        var service = new PdfSharpCharacterSheetPdfService();

        await using var template = File.OpenRead(templatePath);
        await using var output = new MemoryStream();
        await service.WriteAsync(template, output, data);
        output.Position = 0;
        var restored = await service.ReadAsync(output);

        Assert.Equal("Nyx", restored.TextFields["CharacterName"]);
        Assert.Equal("Elf", Assert.Single(restored.Selections).Name);
        Assert.Equal("DEX", Assert.Single(restored.Effects).Target);
        Assert.Equal("Magic Missile", Assert.Single(restored.Spells).Name);
    }
}
