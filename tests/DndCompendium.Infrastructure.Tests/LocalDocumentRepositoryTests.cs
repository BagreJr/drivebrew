using System.Security.Cryptography;
using System.Text;
using DndCompendium.Core.Models;
using DndCompendium.Infrastructure.Persistence;
using DndCompendium.Infrastructure.Sources;

namespace DndCompendium.Infrastructure.Tests;

public sealed class LocalDocumentRepositoryTests
{
    [Fact]
    public async Task InstalledDocument_PersistsAcrossRepositoryRestart()
    {
        using var directory = new TemporaryDirectory();
        var source = new EmbeddedDocumentSource();
        var magus = (await source.GetCatalogAsync()).Single(document => document.FileName == "Magus.txt");

        string relativePath;
        await using (var first = new LocalDocumentRepository(directory.Path))
        {
            await first.UpsertCatalogAsync([magus]);
            await using var stream = await source.OpenReadAsync(magus.Id);
            await first.InstallAsync(magus, stream);
            var installed = Assert.IsType<LocalDocumentContent>(await first.ReadAsync(magus.Id));
            relativePath = installed.RelativePath;
            Assert.Contains("# Magus", installed.SourceText);
        }

        await using (var restarted = new LocalDocumentRepository(directory.Path))
        {
            await restarted.InitializeAsync();
            var installed = Assert.IsType<LocalDocumentContent>(await restarted.ReadAsync(magus.Id));
            Assert.Equal(relativePath, installed.RelativePath);
            Assert.Equal(magus.Checksum, installed.Sha256);

            var metadata = Assert.IsType<LibraryDocument>(await restarted.GetDocumentAsync(magus.Id));
            Assert.True(metadata.HasLocalCopy);
            Assert.Equal(DocumentDownloadState.Downloaded, metadata.DownloadState);
        }
    }

    [Fact]
    public async Task ReadInstalledDocument_DoesNotNeedOrInvokeSource()
    {
        using var directory = new TemporaryDirectory();
        var text = "# Magus\nUnique offline passage.";
        var document = TestDocuments.Create("magus", "Magus", "Magus.txt", text);
        await using var repository = new LocalDocumentRepository(directory.Path);
        await repository.UpsertCatalogAsync([document]);
        await using (var content = TestDocuments.Stream(text))
        {
            await repository.InstallAsync(document, content);
        }

        // Reading takes only a document id; no IDocumentSource or network collaborator is present.
        var local = Assert.IsType<LocalDocumentContent>(await repository.ReadAsync(document.Id));

        Assert.Equal(text, local.SourceText);
    }

    [Fact]
    public async Task InvalidUpdate_LeavesPreviousValidatedCopyIntact()
    {
        using var directory = new TemporaryDirectory();
        const string oldText = "# Magus\nStable local version";
        const string expectedNewText = "# Magus\nExpected new version";
        const string corruptDownload = "# Magus\nIncomplete transfer";
        var original = TestDocuments.Create("magus", "Magus", "Magus.txt", oldText);
        var update = TestDocuments.Create(
            "magus",
            "Magus updated",
            "Magus.txt",
            expectedNewText,
            original.ModifiedAt.AddDays(1));
        await using var repository = new LocalDocumentRepository(directory.Path);
        await repository.UpsertCatalogAsync([original]);
        await using (var firstDownload = TestDocuments.Stream(oldText))
        {
            await repository.InstallAsync(original, firstDownload);
        }

        var before = Assert.IsType<LocalDocumentContent>(await repository.ReadAsync(original.Id));
        await repository.UpsertCatalogAsync([update]);
        await using var invalidStream = TestDocuments.Stream(corruptDownload);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => repository.InstallAsync(update, invalidStream));

        Assert.Contains("SHA-256", error.Message);
        var after = Assert.IsType<LocalDocumentContent>(await repository.ReadAsync(original.Id));
        Assert.Equal(before.SourceText, after.SourceText);
        Assert.Equal(before.Sha256, after.Sha256);
        Assert.Equal(before.RelativePath, after.RelativePath);
        var metadata = Assert.IsType<LibraryDocument>(await repository.GetDocumentAsync(original.Id));
        Assert.Equal(DocumentDownloadState.UpdateAvailable, metadata.DownloadState);
        Assert.True(metadata.HasLocalCopy);
        Assert.Empty(Directory.EnumerateFiles(System.IO.Path.Combine(directory.Path, ".staging")));
    }

    [Fact]
    public async Task HostileSourceNames_AreStoredAsSafeRelativePaths()
    {
        using var directory = new TemporaryDirectory();
        const string text = "# Safe content";
        var document = TestDocuments.Create(
            "../outside/id",
            "Traversal fixture",
            "../../outside.txt",
            text);
        await using var repository = new LocalDocumentRepository(directory.Path);
        await repository.UpsertCatalogAsync([document]);
        await using var stream = TestDocuments.Stream(text);

        await repository.InstallAsync(document, stream);

        var local = Assert.IsType<LocalDocumentContent>(await repository.ReadAsync(document.Id));
        Assert.False(System.IO.Path.IsPathRooted(local.RelativePath));
        Assert.DoesNotContain("..", local.RelativePath);
        var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(
            directory.Path,
            local.RelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        var rootPrefix = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(directory.Path))
            + System.IO.Path.DirectorySeparatorChar;
        Assert.StartsWith(rootPrefix, fullPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(fullPath));
        Assert.False(File.Exists(System.IO.Path.Combine(directory.Path, "..", "outside.txt")));
    }

    [Fact]
    public async Task Search_FindsDownloadedTextAndCatalogTitleOffline()
    {
        using var directory = new TemporaryDirectory();
        var source = new EmbeddedDocumentSource();
        var catalog = await source.GetCatalogAsync();
        var psion = catalog.Single(document => document.FileName == "Psion.txt");
        await using var repository = new LocalDocumentRepository(directory.Path);
        await repository.UpsertCatalogAsync(catalog);
        await using (var stream = await source.OpenReadAsync(psion.Id))
        {
            await repository.InstallAsync(psion, stream);
        }

        var byContent = await repository.SearchAsync("Utopian Mind");
        var byTitle = await repository.SearchAsync("Backgrounds");

        Assert.Single(byContent);
        Assert.Equal(psion.Id, byContent[0].Id);
        Assert.Single(byTitle);
        Assert.Equal("Backgrounds, Feats & Equipment.txt", byTitle[0].FileName);
    }

    [Fact]
    public async Task StorageUsage_TracksInstalledAndDeletedDocuments()
    {
        using var directory = new TemporaryDirectory();
        const string text = "# Storage fixture\n123456789";
        var document = TestDocuments.Create("storage", "Storage", "Storage.txt", text);
        await using var repository = new LocalDocumentRepository(directory.Path);
        await repository.UpsertCatalogAsync([document]);
        await using (var stream = TestDocuments.Stream(text))
        {
            await repository.InstallAsync(document, stream);
        }

        Assert.Equal(Encoding.UTF8.GetByteCount(text), await repository.GetStorageUsageAsync());
        await repository.DeleteLocalCopyAsync(document.Id);

        Assert.Equal(0, await repository.GetStorageUsageAsync());
        Assert.Null(await repository.ReadAsync(document.Id));
        Assert.NotNull(await repository.GetDocumentAsync(document.Id));
    }

    [Fact]
    public async Task Install_ReportsProgressAndStoresComputedSha256()
    {
        using var directory = new TemporaryDirectory();
        var text = new string('x', 150_000);
        var document = TestDocuments.Create("large", "Large", "Large.txt", text);
        var reports = new List<double>();
        await using var repository = new LocalDocumentRepository(directory.Path);
        await repository.UpsertCatalogAsync([document]);
        await using var stream = TestDocuments.Stream(text);

        await repository.InstallAsync(document, stream, new InlineProgress(reports.Add));

        Assert.NotEmpty(reports);
        Assert.Equal(0, reports[0]);
        Assert.Equal(1, reports[^1]);
        Assert.All(reports, value => Assert.InRange(value, 0, 1));
        var local = Assert.IsType<LocalDocumentContent>(await repository.ReadAsync(document.Id));
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant(),
            local.Sha256);
    }

    [Fact]
    public async Task DriveVersionChange_MarksValidatedLocalCopyAsUpdateAvailable()
    {
        using var directory = new TemporaryDirectory();
        const string text = "# Drive-backed class";
        var original = TestDocuments.Create("drive-class", "Drive class", "Drive.txt", text) with
        {
            Checksum = string.Empty,
            Version = "drive:file:1000:20"
        };
        var updated = original with
        {
            ModifiedAt = original.ModifiedAt.AddHours(1),
            Version = "drive:file:2000:24"
        };
        await using var repository = new LocalDocumentRepository(directory.Path);
        await repository.UpsertCatalogAsync([original]);
        await using (var stream = TestDocuments.Stream(text))
        {
            await repository.InstallAsync(original, stream);
        }

        await repository.UpsertCatalogAsync([updated]);

        var metadata = Assert.IsType<LibraryDocument>(await repository.GetDocumentAsync(original.Id));
        Assert.Equal(DocumentDownloadState.UpdateAvailable, metadata.DownloadState);
        Assert.True(metadata.HasLocalCopy);
        Assert.NotNull(await repository.ReadAsync(original.Id));
    }

    [Fact]
    public async Task OfflineFallbackMetadata_DoesNotReplaceKnownDriveVersion()
    {
        using var directory = new TemporaryDirectory();
        const string text = "# Current offline copy";
        var current = TestDocuments.Create("drive-offline", "Drive offline", "Drive.txt", text) with
        {
            Checksum = string.Empty,
            Version = "drive:file:3000:22"
        };
        var staleFallback = current with
        {
            SizeBytes = 10,
            ModifiedAt = current.ModifiedAt.AddDays(-10),
            Version = "drive:file:1000:10",
            IsVersionAuthoritative = false
        };
        await using var repository = new LocalDocumentRepository(directory.Path);
        await repository.UpsertCatalogAsync([current]);
        await using (var stream = TestDocuments.Stream(text))
        {
            await repository.InstallAsync(current, stream);
        }

        await repository.UpsertCatalogAsync([staleFallback]);

        var metadata = Assert.IsType<LibraryDocument>(await repository.GetDocumentAsync(current.Id));
        Assert.Equal(DocumentDownloadState.Downloaded, metadata.DownloadState);
        Assert.Equal(current.Version, metadata.SourceVersion);
        Assert.Equal(current.SizeBytes, metadata.SourceSizeBytes);
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
