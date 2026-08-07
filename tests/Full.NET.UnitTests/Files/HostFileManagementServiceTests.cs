using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.ManageHostFiles;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class HostFileManagementServiceTests
{
    [TestMethod]
    public async Task Delete_checks_open_claims_inside_files_transaction()
    {
        var transaction = new ObservableTransaction();
        var fileId = Guid.CreateVersion7();
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<Guid>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(fileId);
        var claimService = Substitute.For<IHostFileReferenceClaimService>();
        claimService.HasOpenClaimsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => transaction.IsExecuting);
        var storage = new RecordingBlobStorage();
        var service = new HostFileManagementService(
            new OneAffectedCommandExecutor(),
            transaction,
            new HostFileQueryService(
                queryExecutor,
                Options.Create(new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                })),
            claimService,
            CreateRegistry(storage),
            Substitute.For<IClock>(),
            Substitute.For<IIdGenerator>(),
            Options.Create(new LocalFileStorageOptions
            {
                RootPath = "unused",
                MaxUploadBytes = 1024,
            }));

        var result = await service.DeleteAsync(fileId, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.FileReferenced, result.Error!.Code);
        Assert.AreEqual(1, transaction.InvocationCount);
    }

    [TestMethod]
    public async Task Upload_does_not_save_blob_when_pending_commit_result_is_uncertain()
    {
        var storage = new RecordingBlobStorage();
        var service = CreateUploadService(
            storage,
            new ThrowAfterActionTransaction(throwOnInvocation: 1));
        await using var content = new MemoryStream([1, 2, 3, 4]);

        _ = await Assert.ThrowsAsync<IOException>(
            () => service.UploadAsync(
                Guid.CreateVersion7(),
                "pending-commit.bin",
                "application/octet-stream",
                content,
                content.Length,
                CancellationToken.None));

        Assert.AreEqual(0, storage.SaveCount);
        Assert.AreEqual(0, storage.DeleteCount);
    }

    [TestMethod]
    public async Task Upload_preserves_blob_when_ready_commit_result_is_uncertain()
    {
        var storage = new RecordingBlobStorage();
        var transaction = new ThrowAfterActionTransaction(throwOnInvocation: 3);
        var service = CreateUploadService(storage, transaction);
        await using var content = new MemoryStream([1, 2, 3, 4]);

        _ = await Assert.ThrowsAsync<IOException>(
            () => service.UploadAsync(
                Guid.CreateVersion7(),
                "ready-commit.bin",
                "application/octet-stream",
                content,
                content.Length,
                CancellationToken.None));

        Assert.AreEqual(3, transaction.InvocationCount);
        Assert.AreEqual(1, storage.SaveCount);
        Assert.AreEqual(0, storage.DeleteCount);
    }

    [TestMethod]
    public async Task Upload_claims_blob_publication_before_saving_content()
    {
        var events = new List<string>();
        var commandExecutor = new UploadSequenceCommandExecutor(events);
        var storage = new UploadSequenceStorage(events);
        var fileId = Guid.CreateVersion7();
        var createdAtUtc = new DateTimeOffset(
            2026,
            8,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new HostFileDetailRecord(
                fileId,
                "publication.bin",
                "application/octet-stream",
                4,
                storage.ProviderKey,
                $"host/2026/08/{fileId:N}",
                null,
                createdAtUtc,
                Guid.CreateVersion7()));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(createdAtUtc);
        var idGenerator = Substitute.For<IIdGenerator>();
        idGenerator.NewId().Returns(fileId);
        var service = new HostFileManagementService(
            commandExecutor,
            new ImmediateTransaction(),
            new HostFileQueryService(
                queryExecutor,
                Options.Create(new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                })),
            CreateClaimService(),
            CreateRegistry(storage),
            clock,
            idGenerator,
            Options.Create(new LocalFileStorageOptions
            {
                RootPath = "unused",
                MaxUploadBytes = 1024,
            }));
        await using var content = new MemoryStream([1, 2, 3, 4]);

        var result = await service.UploadAsync(
            Guid.CreateVersion7(),
            "publication.bin",
            "application/octet-stream",
            content,
            content.Length,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[] { "insert", "claim-publication", "save", "ready" },
            events);
    }

    [TestMethod]
    public async Task Upload_accepts_ready_state_completed_concurrently_by_reconciliation()
    {
        var storage = new RecordingBlobStorage();
        var service = CreateUploadService(
            storage,
            new ImmediateTransaction(),
            new ZeroOnThirdCommandExecutor());
        await using var content = new MemoryStream([1, 2, 3, 4]);

        var result = await service.UploadAsync(
            Guid.CreateVersion7(),
            "concurrent-ready.bin",
            "application/octet-stream",
            content,
            content.Length,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, storage.SaveCount);
        Assert.AreEqual(0, storage.DeleteCount);
    }

    [TestMethod]
    public async Task Upload_removes_blob_when_insert_does_not_affect_exactly_one_row()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-files-upload-insert-invariant-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileId = Guid.CreateVersion7();
            var createdByUserId = Guid.CreateVersion7();
            var now = new DateTimeOffset(
                2026,
                7,
                30,
                0,
                0,
                0,
                TimeSpan.Zero);
            var storageOptions = Options.Create(new LocalFileStorageOptions
            {
                RootPath = rootPath,
                MaxUploadBytes = 1024,
            });
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(now);
            var idGenerator = Substitute.For<IIdGenerator>();
            idGenerator.NewId().Returns(fileId);
            var service = new HostFileManagementService(
                new ZeroAffectedCommandExecutor(),
                new ImmediateTransaction(),
                new HostFileQueryService(
                    Substitute.For<IQueryExecutor>(),
                    Options.Create(new DatabaseOptions
                    {
                        Provider = DatabaseProvider.SqlServer,
                    })),
                CreateClaimService(),
                CreateRegistry(new LocalHostFileBlobStorage(storageOptions)),
                clock,
                idGenerator,
                storageOptions);
            await using var content = new MemoryStream([1, 2, 3, 4]);

            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.UploadAsync(
                    createdByUserId,
                    "insert-invariant.txt",
                    "text/plain",
                    content,
                    content.Length,
                    CancellationToken.None));

            Assert.AreEqual(
                0,
                Directory.GetFiles(
                    rootPath,
                    "*",
                    SearchOption.AllDirectories).Length);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [TestMethod]
    public async Task Delete_preserves_blob_when_soft_delete_affects_multiple_rows()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-files-delete-invariant-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileId = Guid.CreateVersion7();
            var createdByUserId = Guid.CreateVersion7();
            var now = new DateTimeOffset(
                2026,
                7,
                31,
                0,
                0,
                0,
                TimeSpan.Zero);
            var storageKey = $"host/{fileId:N}.bin";
            var storageOptions = Options.Create(new LocalFileStorageOptions
            {
                RootPath = rootPath,
                MaxUploadBytes = 1024,
            });
            var blobStorage = new LocalHostFileBlobStorage(storageOptions);
            await using (var content = new MemoryStream([1, 2, 3, 4]))
            {
                await blobStorage.SaveAsync(storageKey, content, CancellationToken.None);
            }

            var queryExecutor = Substitute.For<IQueryExecutor>();
            queryExecutor.QuerySingleOrDefaultAsync<Guid>(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(fileId);
            queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                    Arg.Any<SqlStatement>(),
                    Arg.Any<object?>(),
                    Arg.Any<CancellationToken>())
                .Returns(new HostFileDetailRecord(
                    fileId,
                    "delete-invariant.bin",
                    "application/octet-stream",
                    4,
                    LocalHostFileBlobStorage.Key,
                    storageKey,
                    null,
                    now,
                    createdByUserId));
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(now);
            var service = new HostFileManagementService(
                new TwoAffectedCommandExecutor(),
                new ImmediateTransaction(),
                new HostFileQueryService(
                    queryExecutor,
                    Options.Create(new DatabaseOptions
                    {
                        Provider = DatabaseProvider.SqlServer,
                    })),
                CreateClaimService(),
                CreateRegistry(blobStorage),
                clock,
                Substitute.For<IIdGenerator>(),
                storageOptions);

            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.DeleteAsync(fileId, CancellationToken.None));

            Assert.AreEqual(
                1,
                Directory.GetFiles(
                    rootPath,
                    "*",
                    SearchOption.AllDirectories).Length);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [TestMethod]
    public async Task Upload_preserves_blob_when_ready_file_cannot_be_read_back()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-files-upload-readback-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(rootPath);
        try
        {
            var fileId = Guid.CreateVersion7();
            var createdByUserId = Guid.CreateVersion7();
            var now = new DateTimeOffset(
                2026,
                7,
                31,
                0,
                0,
                0,
                TimeSpan.Zero);
            var storageOptions = Options.Create(new LocalFileStorageOptions
            {
                RootPath = rootPath,
                MaxUploadBytes = 1024,
            });
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(now);
            var idGenerator = Substitute.For<IIdGenerator>();
            idGenerator.NewId().Returns(fileId);
            var service = new HostFileManagementService(
                new RecordingCommandExecutor(),
                new ImmediateTransaction(),
                new HostFileQueryService(
                    Substitute.For<IQueryExecutor>(),
                    Options.Create(new DatabaseOptions
                    {
                        Provider = DatabaseProvider.SqlServer,
                    })),
                CreateClaimService(),
                CreateRegistry(new LocalHostFileBlobStorage(storageOptions)),
                clock,
                idGenerator,
                storageOptions);
            await using var content = new MemoryStream([1, 2, 3, 4]);

            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.UploadAsync(
                    createdByUserId,
                    "readback.txt",
                    "text/plain",
                    content,
                    content.Length,
                    CancellationToken.None));

            Assert.AreEqual(
                1,
                Directory.GetFiles(
                    rootPath,
                    "*",
                    SearchOption.AllDirectories).Length);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [TestMethod]
    public async Task Upload_rejects_empty_actual_content_when_declared_length_is_positive()
    {
        var fileId = Guid.CreateVersion7();
        var createdByUserId = Guid.CreateVersion7();
        var now = new DateTimeOffset(
            2026,
            7,
            30,
            0,
            0,
            0,
            TimeSpan.Zero);
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new HostFileDetailRecord(
                fileId,
                "empty.txt",
                "text/plain",
                0,
                LocalHostFileBlobStorage.Key,
                "unused",
                null,
                now,
                createdByUserId));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var idGenerator = Substitute.For<IIdGenerator>();
        idGenerator.NewId().Returns(fileId);
        var service = new HostFileManagementService(
            new RecordingCommandExecutor(),
            new ImmediateTransaction(),
            new HostFileQueryService(
                queryExecutor,
                Options.Create(new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                })),
            CreateClaimService(),
            CreateRegistry(new AcceptingBlobStorage()),
            clock,
            idGenerator,
            Options.Create(new LocalFileStorageOptions
            {
                RootPath = "unused",
                MaxUploadBytes = 4,
            }));
        await using var content = new MemoryStream();

        var result = await service.UploadAsync(
            createdByUserId,
            "empty.txt",
            "text/plain",
            content,
            contentLength: 1,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.InvalidUpload, result.Error!.Code);
    }

    [TestMethod]
    public async Task Upload_persists_actual_content_length_when_declared_length_is_lower()
    {
        var actualContent = new byte[] { 1, 2, 3 };
        var fileId = Guid.CreateVersion7();
        var createdByUserId = Guid.CreateVersion7();
        var now = new DateTimeOffset(
            2026,
            7,
            30,
            0,
            0,
            0,
            TimeSpan.Zero);
        var commandExecutor = new RecordingCommandExecutor();
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new HostFileDetailRecord(
                fileId,
                "actual-size.txt",
                "text/plain",
                actualContent.Length,
                LocalHostFileBlobStorage.Key,
                "unused",
                null,
                now,
                createdByUserId));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var idGenerator = Substitute.For<IIdGenerator>();
        idGenerator.NewId().Returns(fileId);
        var service = new HostFileManagementService(
            commandExecutor,
            new ImmediateTransaction(),
            new HostFileQueryService(
                queryExecutor,
                Options.Create(new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                })),
            CreateClaimService(),
            CreateRegistry(new AcceptingBlobStorage()),
            clock,
            idGenerator,
            Options.Create(new LocalFileStorageOptions
            {
                RootPath = "unused",
                MaxUploadBytes = 4,
            }));
        await using var content = new MemoryStream(actualContent);

        var result = await service.UploadAsync(
            createdByUserId,
            "actual-size.txt",
            "text/plain",
            content,
            contentLength: 1,
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual((long)actualContent.Length, commandExecutor.InsertSizeBytes);
        Assert.AreEqual(LocalHostFileBlobStorage.Key, commandExecutor.InsertProviderKey);
    }

    [TestMethod]
    public async Task Upload_rejects_actual_content_that_exceeds_configured_limit()
    {
        var storageOptions = Options.Create(new LocalFileStorageOptions
        {
            RootPath = "unused",
            MaxUploadBytes = 4,
        });
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(
            2026,
            7,
            30,
            0,
            0,
            0,
            TimeSpan.Zero));
        var idGenerator = Substitute.For<IIdGenerator>();
        idGenerator.NewId().Returns(Guid.CreateVersion7());
        var service = new HostFileManagementService(
            Substitute.For<ICommandExecutor>(),
            Substitute.For<ICommandTransaction>(),
            new HostFileQueryService(
                Substitute.For<IQueryExecutor>(),
                Options.Create(new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                })),
            CreateClaimService(),
            CreateRegistry(new FailingSaveBlobStorage()),
            clock,
            idGenerator,
            storageOptions);
        await using var content = new MemoryStream([1, 2, 3, 4, 5]);

        var result = await service.UploadAsync(
            Guid.CreateVersion7(),
            "oversized.txt",
            "text/plain",
            content,
            contentLength: 1,
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(FilesErrorCodes.FileTooLarge, result.Error!.Code);
    }

    [TestMethod]
    public async Task Upload_propagates_request_cancellation_from_blob_save()
    {
        using var requestCancellation = new CancellationTokenSource();
        var storageOptions = Options.Create(new LocalFileStorageOptions
        {
            RootPath = "unused",
            MaxUploadBytes = 1024,
        });
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(
            2026,
            7,
            30,
            0,
            0,
            0,
            TimeSpan.Zero));
        var idGenerator = Substitute.For<IIdGenerator>();
        idGenerator.NewId().Returns(Guid.CreateVersion7());
        var service = new HostFileManagementService(
            Substitute.For<ICommandExecutor>(),
            Substitute.For<ICommandTransaction>(),
            new HostFileQueryService(
                Substitute.For<IQueryExecutor>(),
                Options.Create(new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                })),
            CreateClaimService(),
            CreateRegistry(new CancelingSaveBlobStorage(requestCancellation)),
            clock,
            idGenerator,
            storageOptions);
        await using var content = new MemoryStream([1, 2, 3, 4]);

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.UploadAsync(
                Guid.CreateVersion7(),
                "canceled.txt",
                "text/plain",
                content,
                content.Length,
                requestCancellation.Token));

        Assert.IsTrue(requestCancellation.IsCancellationRequested);
    }

    [TestMethod]
    public async Task Upload_cancellation_during_ready_commit_preserves_published_blob()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-files-upload-compensation-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(rootPath);
        try
        {
            using var requestCancellation = new CancellationTokenSource();
            var storageOptions = Options.Create(new LocalFileStorageOptions
            {
                RootPath = rootPath,
                MaxUploadBytes = 1024,
            });
            var queryExecutor = Substitute.For<IQueryExecutor>();
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(new DateTimeOffset(
                2026,
                7,
                30,
                0,
                0,
                0,
                TimeSpan.Zero));
            var idGenerator = Substitute.For<IIdGenerator>();
            idGenerator.NewId().Returns(Guid.CreateVersion7());
            var service = new HostFileManagementService(
                new OneAffectedCommandExecutor(),
                new CancelingTransaction(requestCancellation, cancelOnInvocation: 3),
                new HostFileQueryService(
                    queryExecutor,
                    Options.Create(new DatabaseOptions
                    {
                        Provider = DatabaseProvider.SqlServer,
                    })),
                CreateClaimService(),
                CreateRegistry(new LocalHostFileBlobStorage(storageOptions)),
                clock,
                idGenerator,
                storageOptions);
            await using var content = new MemoryStream([1, 2, 3, 4]);

            _ = await Assert.ThrowsAsync<OperationCanceledException>(
                () => service.UploadAsync(
                    Guid.CreateVersion7(),
                    "canceled.txt",
                    "text/plain",
                    content,
                    content.Length,
                    requestCancellation.Token));

            Assert.IsTrue(requestCancellation.IsCancellationRequested);
            Assert.AreEqual(
                1,
                Directory.GetFiles(
                    rootPath,
                    "*",
                    SearchOption.AllDirectories).Length);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [TestMethod]
    public async Task Delete_uses_the_provider_recorded_with_the_file()
    {
        var fileId = Guid.CreateVersion7();
        var createdByUserId = Guid.CreateVersion7();
        var now = new DateTimeOffset(
            2026,
            8,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        var storageKey = "host/2026/08/provider-boundary";
        var queryExecutor = Substitute.For<IQueryExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<Guid>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(fileId);
        queryExecutor.QuerySingleOrDefaultAsync<HostFileDetailRecord>(
                Arg.Any<SqlStatement>(),
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(new HostFileDetailRecord(
                fileId,
                "provider-boundary.bin",
                "application/octet-stream",
                4,
                "archive",
                storageKey,
                null,
                now,
                createdByUserId));
        var local = new RecordingDeleteStorage(LocalHostFileBlobStorage.Key);
        var archive = new RecordingDeleteStorage("archive");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(now);
        var service = new HostFileManagementService(
            new OneAffectedCommandExecutor(),
            new ImmediateTransaction(),
            new HostFileQueryService(
                queryExecutor,
                Options.Create(new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                })),
            CreateClaimService(),
            CreateRegistry(local, archive),
            clock,
            Substitute.For<IIdGenerator>(),
            Options.Create(new LocalFileStorageOptions
            {
                RootPath = "unused",
                MaxUploadBytes = 1024,
            }));

        var result = await service.DeleteAsync(fileId, CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(0, local.DeletedStorageKeys);
        CollectionAssert.AreEqual(
            new[] { storageKey },
            archive.DeletedStorageKeys);
    }

    private static FileStorageProviderRegistry CreateRegistry(
        params IFileStorageProvider[] providers) =>
        new(
            providers,
            Options.Create(new FileStorageOptions
            {
                DefaultProviderKey = providers[0].ProviderKey,
            }));

    private static IHostFileReferenceClaimService CreateClaimService(bool hasOpenClaims = false)
    {
        var claimService = Substitute.For<IHostFileReferenceClaimService>();
        claimService.HasOpenClaimsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(hasOpenClaims);
        return claimService;
    }

    private static HostFileManagementService CreateUploadService(
        IFileStorageProvider storage,
        ICommandTransaction transaction,
        ICommandExecutor? commandExecutor = null)
    {
        var fileId = Guid.CreateVersion7();
        var createdAtUtc = new DateTimeOffset(
            2026,
            8,
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        var queryExecutor = new FixedHostFileQueryExecutor(
            new HostFileDetailRecord(
                fileId,
                "commit-boundary.bin",
                "application/octet-stream",
                4,
                storage.ProviderKey,
                $"host/2026/08/{fileId:N}",
                null,
                createdAtUtc,
                Guid.CreateVersion7()));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(createdAtUtc);
        var idGenerator = Substitute.For<IIdGenerator>();
        idGenerator.NewId().Returns(fileId);

        return new HostFileManagementService(
            commandExecutor ?? new OneAffectedCommandExecutor(),
            transaction,
            new HostFileQueryService(
                queryExecutor,
                Options.Create(new DatabaseOptions
                {
                    Provider = DatabaseProvider.SqlServer,
                })),
            CreateClaimService(),
            CreateRegistry(storage),
            clock,
            idGenerator,
            Options.Create(new LocalFileStorageOptions
            {
                RootPath = "unused",
                MaxUploadBytes = 1024,
            }));
    }

    private sealed class ThrowAfterActionTransaction(int throwOnInvocation)
        : ICommandTransaction
    {
        public int InvocationCount { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            var result = await action(cancellationToken);
            if (InvocationCount == throwOnInvocation)
            {
                throw new IOException("The commit result was not observed.");
            }

            return result;
        }
    }

    private sealed class ObservableTransaction : ICommandTransaction
    {
        public bool IsExecuting { get; private set; }

        public int InvocationCount { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            IsExecuting = true;
            try
            {
                return await action(cancellationToken);
            }
            finally
            {
                IsExecuting = false;
            }
        }
    }

    private sealed class FixedHostFileQueryExecutor(HostFileDetailRecord record)
        : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((T?)(object)record);

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("List query was not expected.");
    }

    private sealed class CancelingTransaction(
        CancellationTokenSource requestCancellation,
        int cancelOnInvocation = 1) : ICommandTransaction
    {
        private int _invocationCount;

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            _invocationCount++;
            if (_invocationCount == cancelOnInvocation)
            {
                requestCancellation.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }

            return await action(cancellationToken);
        }
    }

    private sealed class ImmediateTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            action(cancellationToken);
    }

    private sealed class RecordingCommandExecutor : ICommandExecutor
    {
        public long? InsertSizeBytes { get; private set; }

        public string? InsertProviderKey { get; private set; }

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (statement != HostFileSql.Insert)
            {
                return Task.FromResult(1);
            }

            var sizeProperty = parameters?.GetType().GetProperty("SizeBytes")
                ?? throw new InvalidOperationException(
                    "Insert parameters did not contain SizeBytes.");
            InsertSizeBytes = (long?)sizeProperty.GetValue(parameters);
            var providerProperty = parameters?.GetType().GetProperty("ProviderKey")
                ?? throw new InvalidOperationException(
                    "Insert parameters did not contain ProviderKey.");
            InsertProviderKey = (string?)providerProperty.GetValue(parameters);
            return Task.FromResult(1);
        }
    }

    private sealed class UploadSequenceCommandExecutor(List<string> events)
        : ICommandExecutor
    {
        private int _invocationCount;

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            _invocationCount++;
            events.Add(_invocationCount switch
            {
                1 => "insert",
                2 => "claim-publication",
                _ => "ready",
            });
            return Task.FromResult(1);
        }
    }

    private sealed class ZeroAffectedCommandExecutor : ICommandExecutor
    {
        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class OneAffectedCommandExecutor : ICommandExecutor
    {
        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
    }

    private sealed class TwoAffectedCommandExecutor : ICommandExecutor
    {
        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(2);
    }

    private sealed class ZeroOnThirdCommandExecutor : ICommandExecutor
    {
        private int _invocationCount;

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            _invocationCount++;
            return Task.FromResult(_invocationCount == 3 ? 0 : 1);
        }
    }

    private sealed class AcceptingBlobStorage : IFileStorageProvider
    {
        public string ProviderKey => LocalHostFileBlobStorage.Key;

        public Task SaveAsync(
            string storageKey,
            Stream content,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Blob read was not expected.");

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Blob deletion was not expected.");
    }

    private sealed class RecordingBlobStorage : IFileStorageProvider
    {
        public string ProviderKey => LocalHostFileBlobStorage.Key;

        public int SaveCount { get; private set; }

        public int DeleteCount { get; private set; }

        public Task SaveAsync(
            string storageKey,
            Stream content,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Blob read was not expected.");

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken)
        {
            DeleteCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class UploadSequenceStorage(List<string> events)
        : IFileStorageProvider
    {
        public string ProviderKey => LocalHostFileBlobStorage.Key;

        public Task SaveAsync(
            string storageKey,
            Stream content,
            CancellationToken cancellationToken)
        {
            events.Add("save");
            return Task.CompletedTask;
        }

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Blob read was not expected.");

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Blob deletion was not expected.");
    }

    private sealed class CancelingSaveBlobStorage(
        CancellationTokenSource requestCancellation) : IFileStorageProvider
    {
        public string ProviderKey => LocalHostFileBlobStorage.Key;

        public Task SaveAsync(
            string storageKey,
            Stream content,
            CancellationToken cancellationToken)
        {
            requestCancellation.Cancel();
            return Task.FromCanceled(cancellationToken);
        }

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Blob read was not expected.");

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Blob deletion was not expected.");
    }

    private sealed class FailingSaveBlobStorage : IFileStorageProvider
    {
        public string ProviderKey => LocalHostFileBlobStorage.Key;

        public Task SaveAsync(
            string storageKey,
            Stream content,
            CancellationToken cancellationToken) =>
            Task.FromException(new IOException("Blob save should not be reached."));

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Blob read was not expected.");

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Blob deletion was not expected.");
    }

    private sealed class RecordingDeleteStorage(string providerKey)
        : IFileStorageProvider
    {
        public string ProviderKey => providerKey;

        public List<string> DeletedStorageKeys { get; } = [];

        public Task SaveAsync(
            string storageKey,
            Stream content,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Blob save was not expected.");

        public Task<Stream> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Blob read was not expected.");

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken)
        {
            DeletedStorageKeys.Add(storageKey);
            return Task.CompletedTask;
        }
    }
}
