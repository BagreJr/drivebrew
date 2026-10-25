using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;

namespace DndCompendium.Infrastructure.Persistence;

public sealed class PdfSharpCharacterSheetPdfService : ICharacterSheetPdfService
{
    private static readonly object FontGate = new();

    public async Task WriteAsync(
        Stream template,
        Stream output,
        CharacterSheetData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(data);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFontResolver();

        await using var source = new MemoryStream();
        await template.CopyToAsync(source, cancellationToken).ConfigureAwait(false);
        source.Position = 0;

        using var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify);
        var fields = document.AcroForm.Fields;
        var missing = new List<string>();
        foreach (var (name, value) in data.TextFields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fields[name] is PdfTextField textField)
            {
                textField.ReadOnly = false;
                textField.Text = value ?? string.Empty;
            }
            else if (name.StartsWith("Digital.", StringComparison.Ordinal))
            {
                // Digital-only fields are rendered on supplemental pages below.
            }
            else
            {
                missing.Add(name);
            }
        }

        foreach (var name in data.CheckedFields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fields[name] is PdfCheckBoxField checkBox)
            {
                checkBox.ReadOnly = false;
                checkBox.Checked = true;
            }
            else
            {
                missing.Add(name);
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidDataException(
                $"La plantilla no contiene estos campos: {string.Join(", ", missing.Distinct(StringComparer.Ordinal))}");
        }

        DrawCheckedMarkers(document, data.CheckedFields);
        AppendSupplementalPages(document, data);
        document.AcroForm.Elements.SetBoolean("/NeedAppearances", false);
        document.Save(output, closeStream: false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CharacterSheetData> ReadAsync(
        Stream input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureFontResolver();

        await using var source = new MemoryStream();
        await input.CopyToAsync(source, cancellationToken).ConfigureAwait(false);
        source.Position = 0;
        using var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify);
        var result = new CharacterSheetData();
        var fields = document.AcroForm.Fields;
        foreach (var name in fields.DescendantNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (fields[name])
            {
                case PdfTextField textField when !string.IsNullOrEmpty(textField.Text):
                    result.TextFields[name] = textField.Text;
                    break;
                case PdfCheckBoxField { Checked: true }:
                    result.CheckedFields.Add(name);
                    break;
            }
        }
        result.UpdatedAt = DateTimeOffset.UtcNow;
        return result;
    }

    private static void DrawCheckedMarkers(PdfSharp.Pdf.PdfDocument document, IReadOnlySet<string> checkedFields)
    {
        if (checkedFields.Count == 0) return;
        foreach (var page in document.Pages)
        {
            XGraphics? graphics = null;
            try
            {
                for (var index = 0; index < page.Annotations.Count; index++)
                {
                    var annotation = page.Annotations[index];
                    var name = annotation.Elements.GetString("/T");
                    if (!checkedFields.Contains(name)) continue;
                    graphics ??= XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                    var rectangle = annotation.Rectangle;
                    var size = Math.Max(2.8, Math.Min(rectangle.Width, rectangle.Height) * 0.52);
                    var x = rectangle.X1 + (rectangle.Width - size) / 2;
                    var y = page.Height.Point - rectangle.Y2 + (rectangle.Height - size) / 2;
                    graphics.DrawEllipse(XBrushes.Black, x, y, size, size);
                }
            }
            finally
            {
                graphics?.Dispose();
            }
        }
    }

    private static void AppendSupplementalPages(PdfDocument document, CharacterSheetData data)
    {
        var sections = BuildSupplementalSections(data);
        if (sections.Count == 0) return;

        const double margin = 42;
        const double lineHeight = 13;
        var titleFont = new XFont("OpenSans", 18, XFontStyleEx.Bold);
        var headingFont = new XFont("OpenSans", 12, XFontStyleEx.Bold);
        var bodyFont = new XFont("OpenSans", 9.5, XFontStyleEx.Regular);
        PdfPage? page = null;
        XGraphics? graphics = null;
        var y = 0d;

        void NewPage()
        {
            graphics?.Dispose();
            page = document.AddPage();
            page.Width = XUnit.FromPoint(612);
            page.Height = XUnit.FromPoint(792);
            graphics = XGraphics.FromPdfPage(page);
            y = margin;
            graphics.DrawString("Character Sheet — Información adicional", titleFont, XBrushes.Black,
                new XRect(margin, y, page.Width.Point - (margin * 2), 28), XStringFormats.TopLeft);
            y += 34;
        }

        NewPage();
        try
        {
            foreach (var (heading, content) in sections)
            {
                var lines = WrapText(graphics!, content, bodyFont, page!.Width.Point - (margin * 2));
                if (y + 25 + Math.Min(lines.Count, 3) * lineHeight > page.Height.Point - margin) NewPage();
                graphics!.DrawString(heading, headingFont, XBrushes.Black,
                    new XRect(margin, y, page!.Width.Point - (margin * 2), 20), XStringFormats.TopLeft);
                y += 20;
                foreach (var line in lines)
                {
                    if (y + lineHeight > page.Height.Point - margin) NewPage();
                    graphics!.DrawString(line, bodyFont, XBrushes.Black,
                        new XRect(margin, y, page!.Width.Point - (margin * 2), lineHeight), XStringFormats.TopLeft);
                    y += lineHeight;
                }
                y += 10;
            }
        }
        finally
        {
            graphics?.Dispose();
        }
    }

    private static List<(string Heading, string Content)> BuildSupplementalSections(CharacterSheetData data)
    {
        var sections = new List<(string Heading, string Content)>();
        AddDigital("Digital.Subclass", "Subclase");
        AddDigital("Digital.ClassFeatures", "Class Features");
        AddDigital("Digital.SubclassFeatures", "Subclass Features");
        AddDigital("Digital.RacialTraits", "Racial Traits");
        AddDigital("Digital.Feats", "Feats");
        AddDigital("Digital.Notes", "Notas");
        AddOverflow("Features and Traits", "Features & Traits (continuación)", 900);
        AddOverflow("Feat+Traits", "Features adicionales (continuación)", 900);
        AddOverflow("Equipment", "Equipo (continuación)", 1100);
        AddOverflow("Treasure", "Tesoro (continuación)", 900);
        AddOverflow("Backstory", "Backstory (continuación)", 1300);
        if (data.Features.Count > 0)
        {
            sections.Add(("Referencias del Compendium", string.Join('\n', data.Features.Select(feature =>
                $"• {feature.Name} — {feature.Kind}{(string.IsNullOrWhiteSpace(feature.Level) ? string.Empty : $" · Nivel {feature.Level}")}"))));
        }
        if (data.Modifiers.Count > 0)
        {
            sections.Add(("Modificadores estructurados", string.Join('\n', data.Modifiers.Select(modifier =>
                $"• {modifier.SourceName}: {modifier.Target} {(modifier.Value >= 0 ? "+" : string.Empty)}{modifier.Value}"
                + $" ({modifier.BonusType}, {(modifier.IsActive ? "activo" : "inactivo")})"))));
        }
        return sections;

        void AddDigital(string key, string heading)
        {
            if (data.TextFields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                sections.Add((heading, value));
        }

        void AddOverflow(string key, string heading, int threshold)
        {
            if (data.TextFields.TryGetValue(key, out var value) && value.Length > threshold)
                sections.Add((heading, value));
        }
    }

    private static IReadOnlyList<string> WrapText(XGraphics graphics, string text, XFont font, double width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(paragraph))
            {
                lines.Add(string.Empty);
                continue;
            }
            var current = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
                if (graphics.MeasureString(candidate, font).Width <= width || string.IsNullOrEmpty(current))
                {
                    current = candidate;
                    continue;
                }
                lines.Add(current);
                current = word;
            }
            if (!string.IsNullOrEmpty(current)) lines.Add(current);
        }
        return lines;
    }

    private static void EnsureFontResolver()
    {
        if (GlobalFontSettings.FontResolver is EmbeddedOpenSansFontResolver) return;
        lock (FontGate)
        {
            if (GlobalFontSettings.FontResolver is null)
            {
                GlobalFontSettings.FontResolver = EmbeddedOpenSansFontResolver.Instance;
            }
        }
    }

    private sealed class EmbeddedOpenSansFontResolver : IFontResolver
    {
        private const string RegularFace = "OpenSans-Regular";
        private const string SemiboldFace = "OpenSans-Semibold";
        private static readonly Lazy<byte[]> Regular = new(() => ReadFont("OpenSans-Regular.ttf"));
        private static readonly Lazy<byte[]> Semibold = new(() => ReadFont("OpenSans-Semibold.ttf"));

        public static EmbeddedOpenSansFontResolver Instance { get; } = new();

        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
            => new(bold ? SemiboldFace : RegularFace, mustSimulateBold: false, mustSimulateItalic: italic);

        public byte[] GetFont(string faceName)
            => faceName == SemiboldFace ? Semibold.Value : Regular.Value;

        private static byte[] ReadFont(string fileName)
        {
            var assembly = typeof(PdfSharpCharacterSheetPdfService).Assembly;
            using var input = assembly.GetManifestResourceStream($"DndCompendium.Infrastructure.Fonts.{fileName}")
                ?? throw new InvalidOperationException($"No se encontró la fuente {fileName}.");
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }
    }
}
