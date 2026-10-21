using DndCompendium.Core.Models;

namespace DndCompendium.Core.Tests;

public sealed class LibraryDocumentTests
{
    [Fact]
    public void ExistingLocalCopy_RemainsReadableDuringFailedUpdateState()
    {
        var document = CreateDocument(DocumentDownloadState.Error, "documents/magus/version-Magus.txt");

        Assert.True(document.IsDownloaded);
        Assert.True(document.HasLocalCopy);
    }

    [Fact]
    public void StateAlone_DoesNotClaimMissingFileIsDownloaded()
    {
        var document = CreateDocument(DocumentDownloadState.Downloaded, null);

        Assert.False(document.IsDownloaded);
    }

    private static LibraryDocument CreateDocument(DocumentDownloadState state, string? relativePath)
        => new()
        {
            Id = "magus",
            Title = "Magus",
            FileName = "Magus.txt",
            Category = DocumentCategory.Classes,
            Description = "Fixture",
            SourceSizeBytes = 10,
            SourceModifiedAt = DateTimeOffset.UtcNow,
            SourceChecksum = new string('a', 64),
            DownloadState = state,
            LocalSizeBytes = relativePath is null ? 0 : 10,
            DownloadedAt = relativePath is null ? null : DateTimeOffset.UtcNow,
            RelativePath = relativePath
        };
}
