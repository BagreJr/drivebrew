using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DndCompendium.Core.Models;

namespace DndCompendium.Core.Services;

public static partial class HomebrewSearchIndexer
{
    private const int MaximumDescriptionLength = 8_000;
    private static readonly HashSet<string> IgnoredHeadings = new(StringComparer.OrdinalIgnoreCase)
    {
        "contents", "table of contents", "índice", "indice"
    };
    private static readonly HashSet<string> NarrativeClassHeadings = new(StringComparer.Ordinal)
    {
        "class features", "creating your character", "creating a character", "creating your class",
        "quick build", "hit points", "proficiencies", "equipment", "multiclassing",
        "the class", "actions", "runes", "companions", "companion", "spell list"
    };
    private static readonly HashSet<string> NamedSubclassSections = new(StringComparer.Ordinal)
    {
        "academic disciplines", "academy of war", "artisan s guilds", "champion s call",
        "diabolic contracts", "divine domains", "grave ambitions", "gunslinger s creeds",
        "mortal burdens", "occult order", "sealed spirit", "sorcererous origins",
        "spirituality", "tech specialties", "trades", "wanderer s roads", "warmage house",
        "witch s crafts"
    };
    private static readonly HashSet<string> SubclassSectionBoundaries = new(StringComparer.Ordinal)
    {
        "blood rites", "eldritch invocations", "eldritch melodies", "familiars", "gadgets",
        "hexes", "maneuvers", "masterwork properties", "psionic disciplines", "ranger traps",
        "scholarly pursuits", "survivalist knacks", "techniques", "totem spirits", "warmage tricks",
        "weird arcana"
    };

    public static IReadOnlyList<SearchIndexEntry> IndexDocument(
        LibraryDocument document,
        LocalDocumentContent content)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(content);
        var sourceKey = $"document:{document.Id}";

        var entries = new List<SearchIndexEntry>
        {
            CreateEntry(
                $"document:{document.Id}",
                sourceKey,
                KindForDocument(document),
                document.Title,
                document.Description,
                document.Title,
                null,
                null,
                null,
                document.Id,
                "p1",
                null,
                1)
        };

        var lines = content.SourceText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var headings = new List<Heading>();
        var page = 1;
        var inFence = false;
        var tocDepth = 0;
        var ignoredHomebrewBlockDepth = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;
            if (ignoredHomebrewBlockDepth > 0)
            {
                ignoredHomebrewBlockDepth = Math.Max(0,
                    ignoredHomebrewBlockDepth + CountToken(trimmed, "{{") - CountToken(trimmed, "}}"));
                continue;
            }
            if (trimmed.StartsWith("{{monster", StringComparison.OrdinalIgnoreCase))
            {
                ignoredHomebrewBlockDepth = Math.Max(0, CountToken(trimmed, "{{") - CountToken(trimmed, "}}"));
                continue;
            }
            if (PageBreakRegex().IsMatch(trimmed))
            {
                page++;
                continue;
            }
            if (trimmed.StartsWith("{{toc", StringComparison.OrdinalIgnoreCase)) tocDepth++;
            if (tocDepth > 0)
            {
                if (trimmed == "}}") tocDepth--;
                continue;
            }

            var match = HeadingRegex().Match(lines[index]);
            if (!match.Success) continue;
            var rawTitle = match.Groups[2].Value.Trim();
            if (rawTitle.Contains("](#", StringComparison.OrdinalIgnoreCase)) continue;
            var title = CleanInlineMarkdown(rawTitle);
            if (title.Length < 2 || IgnoredHeadings.Contains(title)) continue;
            headings.Add(new Heading(index, match.Groups[1].Length, page, title));
        }

        var hierarchy = new Dictionary<int, HeadingContext>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int? subclassSectionLevel = null;
        int? subclassRootLevel = null;
        HeadingContext? activeSubclass = null;
        for (var position = 0; position < headings.Count; position++)
        {
            var heading = headings[position];
            var normalizedHeading = NormalizeForSearch(heading.Title);
            if (subclassSectionLevel.HasValue
                && heading.Level <= subclassSectionLevel.Value
                && IsSubclassSectionBoundary(normalizedHeading))
            {
                subclassSectionLevel = null;
                subclassRootLevel = null;
                activeSubclass = null;
            }
            if (subclassSectionLevel.HasValue && heading.Level < subclassSectionLevel.Value)
            {
                subclassSectionLevel = null;
                subclassRootLevel = null;
                activeSubclass = null;
            }
            foreach (var headingLevel in hierarchy.Keys.Where(level => level >= heading.Level).ToArray()) hierarchy.Remove(headingLevel);
            var parentContext = hierarchy.Count == 0
                ? null
                : hierarchy.OrderByDescending(pair => pair.Key).First().Value;
            var parent = parentContext is null
                ? document.Title
                : parentContext.Title;
            var ancestry = hierarchy.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();

            var nextLine = position + 1 < headings.Count ? headings[position + 1].LineIndex : lines.Length;
            var description = ExtractDescription(lines, heading.LineIndex + 1, nextLine);
            var startsSubclassSection = document.Category == DocumentCategory.Classes
                && IsSubclassSection(normalizedHeading) && !IsNestedChoiceSection(ancestry);
            var kind = KindForHeading(document, heading, parentContext, ancestry, startsSubclassSection,
                subclassSectionLevel, subclassRootLevel, activeSubclass);
            hierarchy[heading.Level] = new HeadingContext(heading.Title, kind);
            if (startsSubclassSection)
            {
                subclassSectionLevel = heading.Level;
                subclassRootLevel = null;
                activeSubclass = null;
            }
            else if (kind == SearchContentKind.Subclass)
            {
                subclassRootLevel ??= heading.Level;
                activeSubclass = new HeadingContext(heading.Title, kind);
            }
            if (NormalizeForSearch(heading.Title) == NormalizeForSearch(document.Title)) continue;
            var headingPath = NormalizeForSearch(string.Join('/', ancestry.Select(item => item.Title).Append(heading.Title)));
            var key = $"{document.Id}|{headingPath}|{kind}";
            if (!seen.Add(key)) continue;

            var level = ExtractLevel(heading.Title, description);
            entries.Add(CreateEntry(
                StableId(key),
                sourceKey,
                kind,
                heading.Title,
                description,
                document.Title,
                kind == SearchContentKind.SubclassFeature
                    ? ancestry.LastOrDefault(item => item.Kind == SearchContentKind.Subclass)?.Title ?? activeSubclass?.Title ?? parent
                    : parent,
                level,
                ExtractRequirements(description),
                document.Id,
                $"{HeadingTarget(heading.Title)}|p{heading.Page}",
                null,
                heading.LineIndex + 1) with
            {
                ParentClass = kind is SearchContentKind.ClassFeature or SearchContentKind.Subclass or SearchContentKind.SubclassFeature
                    ? document.Title
                    : null,
                ParentSubclass = kind == SearchContentKind.SubclassFeature
                    ? ancestry.LastOrDefault(item => item.Kind == SearchContentKind.Subclass)?.Title ?? activeSubclass?.Title
                    : null
            });
        }

        return entries;
    }

    private static int CountToken(string value, string token)
    {
        var count = 0;
        for (var index = 0; (index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0; index += token.Length)
            count++;
        return count;
    }

    public static string NormalizeForSearch(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var previousWasSpace = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character))
            {
                result.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                result.Append(' ');
                previousWasSpace = true;
            }
        }
        return result.ToString().Trim();
    }

    private static SearchIndexEntry CreateEntry(
        string id,
        string sourceKey,
        SearchContentKind kind,
        string name,
        string description,
        string origin,
        string? parent,
        string? level,
        string? requirements,
        string? documentId,
        string? target,
        string? referenceId,
        int lineNumber) => new()
        {
            Id = id,
            SourceKey = sourceKey,
            Kind = kind,
            Name = name.Trim(),
            Description = description.Trim(),
            Origin = origin.Trim(),
            Parent = string.IsNullOrWhiteSpace(parent) ? null : parent.Trim(),
            Level = level,
            Requirements = requirements,
            DocumentId = documentId,
            Target = target,
            ReferenceId = referenceId,
            LineNumber = lineNumber
        };

    private static SearchContentKind KindForDocument(LibraryDocument document) => document.Category switch
    {
        DocumentCategory.Classes => SearchContentKind.Class,
        DocumentCategory.Subclasses => SearchContentKind.Subclass,
        DocumentCategory.Races => SearchContentKind.Race,
        DocumentCategory.Backgrounds => SearchContentKind.Background,
        DocumentCategory.Feats => SearchContentKind.Feat,
        DocumentCategory.Spells => SearchContentKind.Spell,
        DocumentCategory.Weapons => SearchContentKind.Equipment,
        DocumentCategory.Equipment => SearchContentKind.Equipment,
        _ => SearchContentKind.Document
    };

    private static SearchContentKind KindForHeading(
        LibraryDocument document,
        Heading heading,
        HeadingContext? parent,
        IReadOnlyList<HeadingContext> ancestry,
        bool startsSubclassSection,
        int? subclassSectionLevel,
        int? subclassRootLevel,
        HeadingContext? activeSubclass)
    {
        var title = NormalizeForSearch(heading.Title);
        var normalizedParent = NormalizeForSearch(parent?.Title ?? document.Title);
        if (document.Category == DocumentCategory.Races)
            return heading.Level <= 2 ? SearchContentKind.Race : SearchContentKind.RaceFeature;
        if (document.Category != DocumentCategory.Classes)
            return KindForNonClassHeading(document, title, ancestry);
        if (document.Category == DocumentCategory.Classes)
        {
            if (startsSubclassSection || IsNarrativeClassHeading(title)) return SearchContentKind.Other;
            if (subclassSectionLevel.HasValue
                && ((!subclassRootLevel.HasValue && heading.Level == subclassSectionLevel.Value)
                    || subclassRootLevel.HasValue && heading.Level == subclassRootLevel.Value))
                return SearchContentKind.Subclass;
            if (subclassSectionLevel.HasValue && activeSubclass is not null
                && heading.Level > (subclassRootLevel ?? subclassSectionLevel).Value)
                return SearchContentKind.SubclassFeature;
            if (subclassSectionLevel.HasValue && activeSubclass is null
                && parent?.Kind == SearchContentKind.Other && heading.Level > subclassSectionLevel.Value
                || title.StartsWith("subclass ", StringComparison.Ordinal)
                || title.StartsWith("archetype ", StringComparison.Ordinal)) return SearchContentKind.Subclass;
            if (subclassSectionLevel.HasValue && heading.Level <= subclassSectionLevel.Value)
                return SearchContentKind.Other;
            if (parent?.Kind == SearchContentKind.Subclass
                || ancestry.Any(item => item.Kind == SearchContentKind.Subclass)
                || normalizedParent.StartsWith("subclass ", StringComparison.Ordinal)
                || normalizedParent.StartsWith("archetype ", StringComparison.Ordinal))
                return parent?.Kind == SearchContentKind.Subclass || heading.Level <= 4
                    ? SearchContentKind.SubclassFeature
                    : SearchContentKind.Other;
            if (ancestry.Any(item => NormalizeForSearch(item.Title) == "class features"))
                return heading.Level <= 3 && parent?.Kind != SearchContentKind.ClassFeature
                    ? SearchContentKind.ClassFeature
                    : SearchContentKind.Other;
            if (heading.Level >= 4 || parent?.Kind == SearchContentKind.ClassFeature) return SearchContentKind.Other;
            return heading.Level <= 1 && NormalizeForSearch(heading.Title) == NormalizeForSearch(document.Title)
                ? SearchContentKind.Class
                : SearchContentKind.ClassFeature;
        }
        return SearchContentKind.Other;
    }

    private static SearchContentKind KindForNonClassHeading(
        LibraryDocument document,
        string title,
        IReadOnlyList<HeadingContext> ancestry)
    {
        var structural = CategorySection(title);
        if (structural.HasValue) return SearchContentKind.Other;
        var inherited = ancestry.Reverse().Select(item => CategorySection(NormalizeForSearch(item.Title)))
            .FirstOrDefault(value => value.HasValue);
        if (inherited.HasValue)
        {
            if (ancestry.LastOrDefault()?.Kind is SearchContentKind.Background or SearchContentKind.Feat)
                return SearchContentKind.Other;
            return inherited.Value;
        }
        return document.Category switch
        {
            DocumentCategory.Backgrounds => SearchContentKind.Background,
            DocumentCategory.Feats => SearchContentKind.Feat,
            DocumentCategory.Weapons or DocumentCategory.Equipment => SearchContentKind.Equipment,
            DocumentCategory.Subclasses => SearchContentKind.Subclass,
            _ => SearchContentKind.Other
        };
    }

    private static SearchContentKind? CategorySection(string value)
    {
        var normalized = NormalizeForSearch(value);
        if (normalized is "background" or "backgrounds" || normalized.StartsWith("chapter background", StringComparison.Ordinal)) return SearchContentKind.Background;
        if (normalized is "feats" or "starter feats" || normalized.Contains("chapter feats", StringComparison.Ordinal)) return SearchContentKind.Feat;
        if (normalized is "armor and shields" or "weapons" or "exotic weapon" or "firearms"
            or "space age weapons" or "adventuring gear" or "equipment packs" or "tools"
            or "expenses" or "space age graft" or "grafts" or "monstrous graft"
            || normalized.EndsWith(" weapons", StringComparison.Ordinal)) return SearchContentKind.Equipment;
        return null;
    }

    private static bool IsNarrativeClassHeading(string value)
    {
        var normalized = NormalizeForSearch(value);
        return NarrativeClassHeadings.Contains(normalized)
            || normalized.StartsWith("creating ", StringComparison.Ordinal)
            || normalized.StartsWith("chapter ", StringComparison.Ordinal)
            || normalized.EndsWith(" table", StringComparison.Ordinal);
    }

    private static bool IsNestedChoiceSection(IReadOnlyList<HeadingContext> ancestry)
        => ancestry.Any(item =>
        {
            var normalized = NormalizeForSearch(item.Title);
            return normalized.Contains("discipline", StringComparison.Ordinal)
                || normalized.Contains("talent", StringComparison.Ordinal)
                || normalized.Contains("rune", StringComparison.Ordinal);
        });

    private static bool IsSubclassSection(string value)
    {
        var normalized = NormalizeForSearch(value);
        return NamedSubclassSections.Contains(normalized)
            || normalized is "subclass" or "subclasses" or "archetype" or "archetypes"
            or "specialization" or "specializations" or "fields of study" or "field of study"
            or "orders" or "esoteric orders" or "arcane traditions" or "primal paths" or "martial archetypes" or "sacred oaths"
            or "druid circles" or "bard colleges" or "roguish archetypes" or "otherworldly patrons"
            || normalized.EndsWith(" subclasses", StringComparison.Ordinal)
            || normalized.EndsWith(" subclass", StringComparison.Ordinal)
            || normalized.EndsWith(" archetypes", StringComparison.Ordinal)
            || normalized.EndsWith(" archetype", StringComparison.Ordinal)
            || normalized.EndsWith(" traditions", StringComparison.Ordinal)
            || normalized.EndsWith(" schools", StringComparison.Ordinal)
            || normalized.EndsWith(" paths", StringComparison.Ordinal)
            || normalized.EndsWith(" orders", StringComparison.Ordinal);
    }

    private static bool IsSubclassSectionBoundary(string value)
    {
        var normalized = NormalizeForSearch(value);
        return SubclassSectionBoundaries.Contains(normalized)
            || normalized.EndsWith(" exploits", StringComparison.Ordinal)
            || normalized.EndsWith(" grafts", StringComparison.Ordinal);
    }

    private static string ExtractDescription(string[] lines, int start, int end)
    {
        var builder = new StringBuilder();
        var inFence = false;
        for (var index = start; index < end && builder.Length < MaximumDescriptionLength; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith("```", StringComparison.Ordinal)) { inFence = !inFence; continue; }
            if (inFence || string.IsNullOrWhiteSpace(line) || PageBreakRegex().IsMatch(line)) continue;
            if (line.StartsWith("{{", StringComparison.Ordinal) || line == "}}") continue;
            if (line.StartsWith('|'))
            {
                if (MarkdownTableSeparatorRegex().IsMatch(line)) continue;
                line = string.Join(" · ", line.Trim('|').Split('|', StringSplitOptions.TrimEntries)
                    .Where(cell => !string.IsNullOrWhiteSpace(cell)));
            }
            var clean = CleanInlineMarkdown(line);
            if (clean.Length == 0) continue;
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(clean);
        }
        var result = builder.ToString();
        return result.Length <= MaximumDescriptionLength ? result : result[..MaximumDescriptionLength].TrimEnd() + "…";
    }

    private static string CleanInlineMarkdown(string value)
    {
        var result = HtmlTagRegex().Replace(value, " ");
        result = MarkdownImageRegex().Replace(result, string.Empty);
        result = MarkdownLinkRegex().Replace(result, "$1");
        result = MarkdownSyntaxRegex().Replace(result, string.Empty);
        result = WhitespaceRegex().Replace(result, " ");
        return result.Trim(' ', '-', '#', ':');
    }

    private static string? ExtractLevel(string title, string description)
    {
        var match = LevelRegex().Match($"{title} {description}");
        if (!match.Success) return null;
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    private static string? ExtractRequirements(string description)
    {
        var match = RequirementRegex().Match(description);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string StableId(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();

    private static string HeadingTarget(string value)
        => NormalizeForSearch(value).Replace(' ', '-');

    private sealed record Heading(int LineIndex, int Level, int Page, string Title);
    private sealed record HeadingContext(string Title, SearchContentKind Kind);

    [GeneratedRegex(@"^(#{1,6})[ \t]+(.+?)[ \t]*#*[ \t]*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingRegex();
    [GeneratedRegex(@"^\\page(?:break)?(?:\s*\{[^\r\n{}]*\})?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PageBreakRegex();
    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();
    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownImageRegex();
    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLinkRegex();
    [GeneratedRegex(@"[*_`~{}]", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownSyntaxRegex();
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
    [GeneratedRegex(@"^\|?\s*:?-{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownTableSeparatorRegex();
    [GeneratedRegex(@"(?:\b(?:level|nivel)\s*(\d{1,2})\b|\b(\d{1,2})(?:st|nd|rd|th)\s+level\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LevelRegex();
    [GeneratedRegex(@"(?:prerequisite|prerrequisito|requirement|requisito)\s*[:—-]\s*([^.;]{2,160})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequirementRegex();
}
