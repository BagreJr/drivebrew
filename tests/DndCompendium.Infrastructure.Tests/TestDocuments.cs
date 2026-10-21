using System.Security.Cryptography;
using System.Text;
using DndCompendium.Core.Models;

namespace DndCompendium.Infrastructure.Tests;

internal static class TestDocuments
{
    public static SourceDocument Create(
        string id,
        string title,
        string fileName,
        string content,
        DateTimeOffset? modifiedAt = null,
        DocumentCategory category = DocumentCategory.Classes)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new SourceDocument
        {
            Id = id,
            Title = title,
            FileName = fileName,
            Category = category,
            Description = "Automated offline fixture",
            SizeBytes = bytes.LongLength,
            ModifiedAt = modifiedAt ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
        };
    }

    public static MemoryStream Stream(string content)
        => new(Encoding.UTF8.GetBytes(content), writable: false);
}
