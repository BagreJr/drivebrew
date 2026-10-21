using DndCompendium.Core.Models;
using System.Globalization;

namespace DndCompendium.App.ViewModels;

public sealed class SearchResultItemViewModel
{
    public SearchResultItemViewModel(SearchResult result)
    {
        Result = result;
        HighlightedSnippet = CreateHighlightedText(result.Snippet, result.MatchedTerms);
    }
    public SearchResult Result { get; }
    public string Name => Result.Entry.Name;
    public string Snippet => Result.Snippet;
    public string CategoryText => SearchViewModel.CategoryLabel(Result.Entry.Kind);
    public string OriginText => Result.Entry.Kind switch
    {
        SearchContentKind.SubclassFeature when !string.IsNullOrWhiteSpace(Result.Entry.ParentSubclass)
            => $"{Result.Entry.ParentClass ?? Result.Entry.Origin} → {Result.Entry.ParentSubclass}",
        SearchContentKind.ClassFeature when !string.IsNullOrWhiteSpace(Result.Entry.ParentClass)
            => Result.Entry.ParentClass!,
        SearchContentKind.Subclass when !string.IsNullOrWhiteSpace(Result.Entry.ParentClass)
            => Result.Entry.ParentClass!,
        _ when !string.IsNullOrWhiteSpace(Result.Entry.Parent) => $"{Result.Entry.Origin} · {Result.Entry.Parent}",
        _ => Result.Entry.Origin
    };
    public string MetadataText => string.Join(" · ", new[]
    {
        string.IsNullOrWhiteSpace(Result.Entry.Level) ? null : $"Nivel {Result.Entry.Level}",
        string.IsNullOrWhiteSpace(Result.Entry.Requirements) ? null : Result.Entry.Requirements
    }.Where(value => value is not null));
    public string LocationText => Result.Entry.LineNumber > 0
        ? $"{PageLabel(Result.Entry.Target)} · línea {Result.Entry.LineNumber}"
        : string.Empty;
    public bool IsFavorite => Result.IsFavorite;
    public string FavoriteText => IsFavorite ? "★ FAVORITO" : string.Empty;
    public FormattedString HighlightedSnippet { get; }

    private static string PageLabel(string? target)
    {
        var page = target?.Split('|', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return page?.StartsWith("p", StringComparison.OrdinalIgnoreCase) == true
            ? $"Página {page[1..]}"
            : "Documento";
    }

    private static FormattedString CreateHighlightedText(string text, IReadOnlyList<string> terms)
    {
        var formatted = new FormattedString();
        var ranges = new List<(int Start, int Length)>();
        foreach (var term in terms.Where(value => value.Length >= 3).OrderByDescending(value => value.Length))
        {
            var start = 0;
            while (start < text.Length)
            {
                var index = CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                    text, term, start, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
                if (index < 0) break;
                if (!ranges.Any(range => index < range.Start + range.Length && range.Start < index + term.Length))
                    ranges.Add((index, term.Length));
                start = index + Math.Max(1, term.Length);
            }
        }
        var cursor = 0;
        foreach (var range in ranges.OrderBy(value => value.Start))
        {
            if (range.Start > cursor) formatted.Spans.Add(new Span { Text = text[cursor..range.Start] });
            formatted.Spans.Add(new Span
            {
                Text = text.Substring(range.Start, range.Length),
                FontAttributes = FontAttributes.Bold,
                BackgroundColor = Color.FromArgb("#5B4315"),
                TextColor = Color.FromArgb("#F4C95D")
            });
            cursor = range.Start + range.Length;
        }
        if (cursor < text.Length) formatted.Spans.Add(new Span { Text = text[cursor..] });
        if (formatted.Spans.Count == 0) formatted.Spans.Add(new Span { Text = text });
        return formatted;
    }
}
