using System.Text.RegularExpressions;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;

namespace DndCompendium.Core.Services;

/// <summary>
/// Extracts only render resources. Ordinary Markdown and HTML navigation links are deliberately ignored.
/// </summary>
public sealed partial class HomebrewResourceExtractor : IExternalResourceExtractor
{
    public IReadOnlyList<ExternalResourceReference> Extract(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        var resources = new Dictionary<string, ExternalResourceReference>(StringComparer.Ordinal);

        AddMatches(MarkdownImageRegex(), sourceText, DocumentResourceKind.Image, resources);
        AddMatches(HtmlImageRegex(), sourceText, DocumentResourceKind.Image, resources);
        AddMatches(HtmlSourceRegex(), sourceText, DocumentResourceKind.Image, resources);
        AddStyleSheetLinks(sourceText, resources);
        AddMatches(CssImportRegex(), sourceText, DocumentResourceKind.Style, resources);

        foreach (Match match in CssUrlRegex().Matches(sourceText))
        {
            Add(match.Groups["url"].Value, InferCssKind(match.Groups["url"].Value), resources);
        }

        return resources.Values.ToArray();
    }

    private static void AddMatches(
        Regex regex,
        string source,
        DocumentResourceKind kind,
        IDictionary<string, ExternalResourceReference> resources)
    {
        foreach (Match match in regex.Matches(source))
        {
            Add(match.Groups["url"].Value, kind, resources);
        }
    }

    private static void AddStyleSheetLinks(
        string source,
        IDictionary<string, ExternalResourceReference> resources)
    {
        foreach (Match match in HtmlLinkRegex().Matches(source))
        {
            var attributes = match.Groups["attributes"].Value;
            if (!StyleSheetRelRegex().IsMatch(attributes))
            {
                continue;
            }

            var href = HrefRegex().Match(attributes);
            if (href.Success)
            {
                Add(href.Groups["url"].Value, DocumentResourceKind.Style, resources);
            }
        }
    }

    private static void Add(
        string rawUrl,
        DocumentResourceKind kind,
        IDictionary<string, ExternalResourceReference> resources)
    {
        var candidate = rawUrl.Trim().Trim('<', '>');
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        var normalized = builder.Uri.AbsoluteUri;
        if (!resources.ContainsKey(normalized))
        {
            resources.Add(normalized, new ExternalResourceReference(normalized, kind));
        }
    }

    private static DocumentResourceKind InferCssKind(string rawUrl)
    {
        var path = Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri)
            ? uri.AbsolutePath
            : rawUrl;
        var extension = Path.GetExtension(path);
        return extension.ToLowerInvariant() switch
        {
            ".woff" or ".woff2" or ".ttf" or ".otf" or ".eot" => DocumentResourceKind.Font,
            ".css" => DocumentResourceKind.Style,
            _ => DocumentResourceKind.Image
        };
    }

    [GeneratedRegex("""!\[[^\]]*\]\(\s*<?(?<url>https?://[^\s)>]+)>?(?:\s+['"][^'"]*['"])?\s*\)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex("""<img\b[^>]*?\bsrc\s*=\s*['"](?<url>https?://[^'"]+)['"][^>]*>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlImageRegex();

    [GeneratedRegex("""<source\b[^>]*?\bsrc\s*=\s*['"](?<url>https?://[^'"]+)['"][^>]*>""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlSourceRegex();

    [GeneratedRegex(@"<link\b(?<attributes>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlLinkRegex();

    [GeneratedRegex("""\brel\s*=\s*['"][^'"]*\bstylesheet\b[^'"]*['"]""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StyleSheetRelRegex();

    [GeneratedRegex("""\bhref\s*=\s*['"](?<url>https?://[^'"]+)['"]""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HrefRegex();

    [GeneratedRegex("""@import\s+(?:url\(\s*)?['"]?(?<url>https?://[^'"\s\)]+)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CssImportRegex();

    [GeneratedRegex("""url\(\s*['"]?(?<url>https?://[^'"\s\)]+)['"]?\s*\)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CssUrlRegex();
}
