namespace DndCompendium.Core.Models;

public sealed record DocumentUpdateResult(
    IReadOnlyList<string> UpdatedFileNames,
    IReadOnlyList<string> FailedFileNames);
