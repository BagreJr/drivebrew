namespace DndCompendium.Core.Models;

public sealed record SearchIndexEntry
{
    public required string Id { get; init; }
    public required string SourceKey { get; init; }
    public required SearchContentKind Kind { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Origin { get; init; }
    public string? Parent { get; init; }
    public string? ParentClass { get; init; }
    public string? ParentSubclass { get; init; }
    public string? Level { get; init; }
    public string? Requirements { get; init; }
    public string? DocumentId { get; init; }
    public string? Target { get; init; }
    public string? ReferenceId { get; init; }
    public int LineNumber { get; init; }
}

public sealed record SearchResult(
    SearchIndexEntry Entry,
    double Score,
    string Snippet,
    bool IsFavorite)
{
    public IReadOnlyList<string> MatchedTerms { get; init; } = [];
}

public sealed record SearchRequest
{
    public required string Query { get; init; }
    public SearchContentKind? Kind { get; init; }
    public string? Origin { get; init; }
    public string? Level { get; init; }
    public int Limit { get; init; } = 100;
}

public sealed record SearchFilterOptions(
    IReadOnlyList<string> Origins,
    IReadOnlyList<string> Levels);
