using System.Globalization;
using DndCompendium.Core.Models;

namespace DndCompendium.App.ViewModels;

public sealed class SearchResultItemViewModel
{
    private readonly HighlightedText _highlightedName;
    private readonly HighlightedText _highlightedSnippet;

    public SearchResultItemViewModel(SearchResult result)
    {
        Result = result;
        _highlightedName = Highlight(result.Entry.Name, result.MatchedTerms);
        _highlightedSnippet = Highlight(result.Snippet, result.MatchedTerms);
    }
    public SearchResult Result { get; }
    public string Name => Result.Entry.Name;
    public string Snippet => Result.Snippet;
    public string NameBeforeMatch => _highlightedName.Before;
    public string NameMatch => _highlightedName.Match;
    public string NameAfterMatch => _highlightedName.After;
    public string SnippetBeforeMatch => _highlightedSnippet.Before;
    public string SnippetMatch => _highlightedSnippet.Match;
    public string SnippetAfterMatch => _highlightedSnippet.After;
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

    private static string PageLabel(string? target)
    {
        var page = target?.Split('|', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return page?.StartsWith("p", StringComparison.OrdinalIgnoreCase) == true
            ? $"Página {page[1..]}"
            : "Documento";
    }

    private static HighlightedText Highlight(string text, IReadOnlyList<string> matchedTerms)
    {
        foreach (var term in matchedTerms.Where(term => !string.IsNullOrWhiteSpace(term)))
        {
            var position = CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                text,
                term.Trim(),
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
            if (position < 0) continue;
            var length = Math.Min(term.Trim().Length, text.Length - position);
            return new HighlightedText(text[..position], text.Substring(position, length), text[(position + length)..]);
        }
        return new HighlightedText(text, string.Empty, string.Empty);
    }

    private sealed record HighlightedText(string Before, string Match, string After);

}
