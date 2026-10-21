using System.Security.Cryptography;
using DndCompendium.Core.Abstractions;
using DndCompendium.Core.Models;
using DndCompendium.Core.Services;
using DndCompendium.Infrastructure.Persistence;

namespace DndCompendium.Infrastructure.Tests;

public sealed class DownloadManagerTests
{
    [Fact]
    public async Task Download_EmitsProgressAndInstallsReadableCopy()
    {
        using var directory = new TemporaryDirectory();
        var bytes = CreateBytes(180_000);
        var source = new ControlledSource(bytes);
        await using var repository = new LocalDocumentRepository(directory.Path);
        using var manager = new DownloadManager(source, repository);
        var states = new List<DocumentDownloadState>();
        manager.ProgressChanged += (_, job) => states.Add(job.State);

        await manager.InitializeLibraryAsync();
        await manager.DownloadAsync(source.Document.Id);

        Assert.Contains(DocumentDownloadState.Queued, states);
        Assert.Contains(DocumentDownloadState.Downloading, states);
        Assert.Equal(DocumentDownloadState.Downloaded, states[^1]);
        var local = Assert.IsType<LocalDocumentContent>(await repository.ReadAsync(source.Document.Id));
        Assert.Equal(source.Document.Checksum, local.Sha256);
        Assert.Equal(1, source.OpenCount);
    }

    [Fact]
    public async Task PauseThenResume_RestartsAndCompletesTransfer()
    {
        using var directory = new TemporaryDirectory();
        var source = new ControlledSource(CreateBytes(600_000), delayPerRead: TimeSpan.FromMilliseconds(8));
        await using var repository = new LocalDocumentRepository(directory.Path);
        using var manager = new DownloadManager(source, repository);
        var transferStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ProgressChanged += (_, job) =>
        {
            if (job.State == DocumentDownloadState.Downloading && job.Progress > 0)
            {
                transferStarted.TrySetResult();
            }
        };

        await manager.InitializeLibraryAsync();
        var download = manager.DownloadAsync(source.Document.Id);
        await transferStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        manager.Pause(source.Document.Id);
        await download;

        var paused = Assert.IsType<LibraryDocument>(await repository.GetDocumentAsync(source.Document.Id));
        Assert.Equal(DocumentDownloadState.Paused, paused.DownloadState);
        Assert.False(paused.HasLocalCopy);

        await manager.ResumeAsync(source.Document.Id);

        var completed = Assert.IsType<LibraryDocument>(await repository.GetDocumentAsync(source.Document.Id));
        Assert.Equal(DocumentDownloadState.Downloaded, completed.DownloadState);
        Assert.True(completed.HasLocalCopy);
        Assert.Equal(2, source.OpenCount);
    }

    [Fact]
    public async Task Cancel_LeavesNoPartialDocument()
    {
        using var directory = new TemporaryDirectory();
        var source = new ControlledSource(CreateBytes(600_000), delayPerRead: TimeSpan.FromMilliseconds(8));
        await using var repository = new LocalDocumentRepository(directory.Path);
        using var manager = new DownloadManager(source, repository);
        var transferStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.ProgressChanged += (_, job) =>
        {
            if (job.State == DocumentDownloadState.Downloading && job.Progress > 0)
            {
                transferStarted.TrySetResult();
            }
        };

        await manager.InitializeLibraryAsync();
        var download = manager.DownloadAsync(source.Document.Id);
        await transferStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        manager.Cancel(source.Document.Id);
        await download;

        var cancelled = Assert.IsType<LibraryDocument>(await repository.GetDocumentAsync(source.Document.Id));
        Assert.Equal(DocumentDownloadState.NotDownloaded, cancelled.DownloadState);
        Assert.False(cancelled.HasLocalCopy);
        Assert.Null(await repository.ReadAsync(source.Document.Id));
        Assert.Empty(Directory.EnumerateFiles(System.IO.Path.Combine(directory.Path, ".staging")));
    }

    [Fact]
    public async Task FailedIntegrityCheck_CanBeRetriedSuccessfully()
    {
        using var directory = new TemporaryDirectory();
        var correct = CreateBytes(90_000);
        var source = new ControlledSource(correct, corruptFirstOpen: true);
        await using var repository = new LocalDocumentRepository(directory.Path);
        using var manager = new DownloadManager(source, repository);
        var jobs = new List<DownloadJob>();
        manager.ProgressChanged += (_, job) => jobs.Add(job);
        await manager.InitializeLibraryAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => manager.DownloadAsync(source.Document.Id));
        var failed = Assert.IsType<LibraryDocument>(await repository.GetDocumentAsync(source.Document.Id));
        Assert.Equal(DocumentDownloadState.Error, failed.DownloadState);
        Assert.NotNull(failed.ErrorMessage);

        await manager.RetryAsync(source.Document.Id);

        var completed = Assert.IsType<LibraryDocument>(await repository.GetDocumentAsync(source.Document.Id));
        Assert.Equal(DocumentDownloadState.Downloaded, completed.DownloadState);
        Assert.True(completed.HasLocalCopy);
        Assert.Equal(2, source.OpenCount);
        Assert.Contains(jobs, job => job.State == DocumentDownloadState.Error);
        Assert.Equal(DocumentDownloadState.Downloaded, jobs[^1].State);
    }

    [Fact]
    public async Task DownloadAll_ContinuesAfterOneDocumentFails()
    {
        using var directory = new TemporaryDirectory();
        var source = new BatchSource(failingDocumentId: "document-2");
        await using var repository = new LocalDocumentRepository(directory.Path);
        var resources = new RecordingResourceManager(repository, failingDocumentId: "document-1");
        using var manager = new DownloadManager(source, repository, resources);

        await manager.InitializeLibraryAsync();
        await manager.DownloadAllAsync();

        Assert.Equal(["document-1", "document-2", "document-3"], source.OpenedDocumentIds);

        var documents = await repository.GetDocumentsAsync();
        Assert.Equal(DocumentDownloadState.Downloaded, documents.Single(item => item.Id == "document-1").DownloadState);
        Assert.Equal(DocumentDownloadState.Error, documents.Single(item => item.Id == "document-2").DownloadState);
        Assert.Equal(DocumentDownloadState.Downloaded, documents.Single(item => item.Id == "document-3").DownloadState);
        Assert.Equal(["document-1", "document-3"], resources.SynchronizedDocumentIds.Order());
        Assert.True(resources.AllReadableDocumentsWereInstalledBeforeSynchronization);
    }

    [Fact]
    public async Task StartupUpdate_RefreshesOnlyChangedInstalledTxtAndReportsItsFileName()
    {
        using var directory = new TemporaryDirectory();
        var source = new VersionedSource("primera versión");
        await using var repository = new LocalDocumentRepository(directory.Path);
        using var manager = new DownloadManager(source, repository);

        await manager.InitializeLibraryAsync();
        await manager.DownloadAsync(source.Document.Id);
        source.SetContent("segunda versión");

        var result = await manager.UpdateInstalledDocumentsAsync();

        Assert.Equal(["Versioned.txt"], result.UpdatedFileNames);
        Assert.Empty(result.FailedFileNames);
        Assert.Equal("segunda versión", (await repository.ReadAsync(source.Document.Id))?.SourceText);
        Assert.Empty((await manager.UpdateInstalledDocumentsAsync()).UpdatedFileNames);
    }

    private static byte[] CreateBytes(int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)('a' + (index % 26));
        }

        return bytes;
    }

    private sealed class ControlledSource : IDocumentSource
    {
        private readonly byte[] _correctContent;
        private readonly TimeSpan _delayPerRead;
        private readonly bool _corruptFirstOpen;
        private int _openCount;

        public ControlledSource(
            byte[] correctContent,
            TimeSpan delayPerRead = default,
            bool corruptFirstOpen = false)
        {
            _correctContent = correctContent;
            _delayPerRead = delayPerRead;
            _corruptFirstOpen = corruptFirstOpen;
            Document = new SourceDocument
            {
                Id = "controlled-fixture",
                Title = "Controlled fixture",
                FileName = "Controlled.txt",
                Category = DocumentCategory.Unclassified,
                Description = "Controllable download fixture",
                SizeBytes = correctContent.LongLength,
                ModifiedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Checksum = Convert.ToHexString(SHA256.HashData(correctContent)).ToLowerInvariant()
            };
        }

        public SourceDocument Document { get; }

        public int OpenCount => Volatile.Read(ref _openCount);

        public Task<IReadOnlyList<SourceDocument>> GetCatalogAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<SourceDocument> catalog = [Document];
            return Task.FromResult(catalog);
        }

        public Task<Stream> OpenReadAsync(string documentId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(Document.Id, documentId);
            cancellationToken.ThrowIfCancellationRequested();
            var attempt = Interlocked.Increment(ref _openCount);
            var bytes = _corruptFirstOpen && attempt == 1
                ? _correctContent[..^1]
                : _correctContent;
            Stream stream = new SlowReadStream(bytes, _delayPerRead);
            return Task.FromResult(stream);
        }
    }

    private sealed class BatchSource : IDocumentSource
    {
        private readonly string _failingDocumentId;
        private readonly byte[] _content = "contenido de prueba"u8.ToArray();
        private readonly IReadOnlyList<SourceDocument> _documents;

        public BatchSource(string failingDocumentId)
        {
            _failingDocumentId = failingDocumentId;
            var checksum = Convert.ToHexString(SHA256.HashData(_content)).ToLowerInvariant();
            _documents = Enumerable.Range(1, 3)
                .Select(index => new SourceDocument
                {
                    Id = $"document-{index}",
                    Title = $"Document {index}",
                    FileName = $"Document-{index}.txt",
                    Category = DocumentCategory.Unclassified,
                    Description = "Batch download fixture",
                    SizeBytes = _content.LongLength,
                    ModifiedAt = new DateTimeOffset(2026, 1, index, 0, 0, 0, TimeSpan.Zero),
                    Checksum = checksum
                })
                .ToArray();
        }

        public List<string> OpenedDocumentIds { get; } = [];

        public Task<IReadOnlyList<SourceDocument>> GetCatalogAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_documents);
        }

        public Task<Stream> OpenReadAsync(string documentId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenedDocumentIds.Add(documentId);
            if (documentId == _failingDocumentId)
            {
                throw new HttpRequestException("Remote document unavailable.");
            }

            Stream stream = new MemoryStream(_content, writable: false);
            return Task.FromResult(stream);
        }
    }

    private sealed class VersionedSource : IDocumentSource
    {
        private byte[] _content;
        private int _revision = 1;

        public VersionedSource(string content)
        {
            _content = System.Text.Encoding.UTF8.GetBytes(content);
        }

        public SourceDocument Document => new()
        {
            Id = "versioned-fixture",
            Title = "Versioned fixture",
            FileName = "Versioned.txt",
            Category = DocumentCategory.Unclassified,
            Description = "Versioned download fixture",
            SizeBytes = _content.LongLength,
            ModifiedAt = new DateTimeOffset(2026, 1, _revision, 0, 0, 0, TimeSpan.Zero),
            Checksum = Convert.ToHexString(SHA256.HashData(_content)).ToLowerInvariant(),
            Version = $"version-{_revision}"
        };

        public void SetContent(string content)
        {
            _content = System.Text.Encoding.UTF8.GetBytes(content);
            _revision++;
        }

        public Task<IReadOnlyList<SourceDocument>> GetCatalogAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<SourceDocument> documents = [Document];
            return Task.FromResult(documents);
        }

        public Task<Stream> OpenReadAsync(
            string documentId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(Document.Id, documentId);
            cancellationToken.ThrowIfCancellationRequested();
            Stream stream = new MemoryStream(_content, writable: false);
            return Task.FromResult(stream);
        }
    }

    private sealed class RecordingResourceManager(
        ILocalDocumentRepository repository,
        string failingDocumentId) : IDocumentResourceManager
    {
        private readonly object _sync = new();
        private readonly List<string> _synchronizedDocumentIds = [];

        public IReadOnlyList<string> SynchronizedDocumentIds
        {
            get
            {
                lock (_sync)
                {
                    return _synchronizedDocumentIds.ToArray();
                }
            }
        }

        public bool AllReadableDocumentsWereInstalledBeforeSynchronization { get; private set; } = true;

        public async Task<DocumentResourceManifest> SynchronizeAsync(
            string documentId,
            string sourceText,
            CancellationToken cancellationToken = default)
        {
            var documents = await repository.GetDocumentsAsync(cancellationToken);
            lock (_sync)
            {
                AllReadableDocumentsWereInstalledBeforeSynchronization &= documents
                    .Where(document => document.Id is "document-1" or "document-3")
                    .All(document => document.HasLocalCopy);
                _synchronizedDocumentIds.Add(documentId);
            }

            if (documentId == failingDocumentId)
            {
                throw new HttpRequestException("Resource host unavailable.");
            }

            return EmptyManifest(documentId);
        }

        public Task<DocumentResourceManifest> GetManifestAsync(
            string documentId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(EmptyManifest(documentId));

        public Task<IReadOnlyList<LocalDocumentResource>> ReadAvailableAsync(
            string documentId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LocalDocumentResource>>([]);

        public Task DeleteAsync(string documentId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        private static DocumentResourceManifest EmptyManifest(string documentId)
            => new()
            {
                DocumentId = documentId,
                Resources = []
            };
    }

    private sealed class SlowReadStream(byte[] content, TimeSpan delayPerRead)
        : MemoryStream(content, writable: false)
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (delayPerRead > TimeSpan.Zero)
            {
                await Task.Delay(delayPerRead, cancellationToken);
            }

            var limitedBuffer = buffer[..Math.Min(buffer.Length, 4096)];
            return await base.ReadAsync(limitedBuffer, cancellationToken);
        }
    }
}
