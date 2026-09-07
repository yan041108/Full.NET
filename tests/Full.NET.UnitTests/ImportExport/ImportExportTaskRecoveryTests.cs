using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.ImportExport.ImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>导入任务必须按租约领取、按检查点恢复，取消不得写成失败。</summary>
[TestClass]
public sealed class ImportExportTaskRecoveryTests
{
    /// <summary>租约到期后可以从检查点重领，且不得重置已成功行。</summary>
    [TestMethod]
    public async Task Expired_executing_lease_is_reclaimed_from_checkpoint_async()
    {
        var fixture = CreateFixture();
        var task = SeedExecutingTask(fixture, leaseExpired: true, nextLineNumber: 2, succeededRowCount: 2);
        fixture.Handler.ExecuteBatchAsync(
                Arg.Any<Stream>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<StaticImportPreviewContext>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Assert.AreEqual(2, call.ArgAt<int>(2));
                return Result<StaticImportBatchExecutionResult>.Success(
                    new StaticImportBatchExecutionResult(
                    [
                        new StaticImportRowExecutionResult(3, true, Guid.NewGuid(), null, null),
                    ]));
            });

        var processed = await fixture.Runner.ProcessPendingAsync(CancellationToken.None);

        Assert.AreEqual(1, processed);
        await fixture.Handler.Received(1).ExecuteBatchAsync(
            Arg.Any<Stream>(),
            Arg.Any<long>(),
            2,
            Arg.Any<int>(),
            Arg.Any<StaticImportPreviewContext>(),
            Arg.Any<CancellationToken>());
        var stored = fixture.Store.Tasks[task.Id];
        Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionSucceeded, stored.StatusKey);
        Assert.AreEqual(3, stored.SucceededRowCount);
        Assert.IsNull(stored.LeaseId);
    }

    /// <summary>有效租约禁止第二个 Worker 再次调用处理器。</summary>
    [TestMethod]
    public async Task Active_lease_rejects_overlapping_claim_async()
    {
        var fixture = CreateFixture();
        SeedExecutingTask(fixture, leaseExpired: false, nextLineNumber: 0, succeededRowCount: 0);

        var processed = await fixture.Runner.ProcessPendingAsync(CancellationToken.None);

        Assert.AreEqual(0, processed);
        Assert.AreEqual(0, fixture.HandlerInvocations);
        Assert.AreEqual(ImportExportTaskStatusKeys.Executing, fixture.Store.Tasks.Values.Single().StatusKey);
    }

    /// <summary>宿主取消必须保留 executing，不能写成 execution_failed。</summary>
    [TestMethod]
    public async Task Host_cancel_does_not_mark_execution_failed_async()
    {
        var fixture = CreateFixture();
        SeedQueuedTask(fixture);
        using var cts = new CancellationTokenSource();
        fixture.Handler.ExecuteBatchAsync(
                Arg.Any<Stream>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<StaticImportPreviewContext>(),
                Arg.Any<CancellationToken>())
            .Returns<Result<StaticImportBatchExecutionResult>>(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => fixture.Runner.ProcessPendingAsync(cts.Token));

        var stored = fixture.Store.Tasks.Values.Single();
        Assert.AreEqual(ImportExportTaskStatusKeys.Executing, stored.StatusKey);
        Assert.IsNull(stored.ErrorCode);
    }

    /// <summary>终态任务不能被重复完成覆盖。</summary>
    [TestMethod]
    public async Task Terminal_task_is_not_reclaimed_or_rewritten_async()
    {
        var fixture = CreateFixture();
        var taskId = Guid.NewGuid();
        fixture.Store.Tasks[taskId] = CreateTask(
            fixture,
            taskId,
            ImportExportTaskStatusKeys.ExecutionSucceeded,
            nextLineNumber: 2,
            succeededRowCount: 2,
            leaseId: null,
            leaseExpiresAtUtc: null);

        var processed = await fixture.Runner.ProcessPendingAsync(CancellationToken.None);

        Assert.AreEqual(0, processed);
        Assert.AreEqual(0, fixture.HandlerInvocations);
        Assert.AreEqual(2, fixture.Store.Tasks[taskId].SucceededRowCount);
    }

    /// <summary>错误回执已经上传时，恢复必须绑定现有文件而不是再次上传。</summary>
    [TestMethod]
    public async Task Existing_error_receipt_is_attached_without_second_upload_async()
    {
        var fixture = CreateFixture();
        var task = SeedQueuedTask(fixture, validRowCount: 1);
        var receiptId = Guid.NewGuid();
        fixture.Files.ReadyFiles.Add(new TenantResourceFileReadyItem(task.SourceFileId, "source.xlsx", fixture.Clock.UtcNow));
        fixture.Files.ReadyFiles.Add(new TenantResourceFileReadyItem(receiptId, "source-errors.xlsx", fixture.Clock.UtcNow));
        fixture.Handler.ExecuteBatchAsync(
                Arg.Any<Stream>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<StaticImportPreviewContext>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<StaticImportBatchExecutionResult>.Success(
                new StaticImportBatchExecutionResult(
                [
                    new StaticImportRowExecutionResult(1, false, null, ImportExportErrorCodes.ExecutionFailed, "row"),
                ])));

        await fixture.Runner.ProcessPendingAsync(CancellationToken.None);

        Assert.AreEqual(0, fixture.Files.UploadCount);
        Assert.AreEqual(receiptId, fixture.Store.Tasks[task.Id].ErrorReceiptFileId);
        Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionFailed, fixture.Store.Tasks[task.Id].StatusKey);
    }

    /// <summary>组装使用内存存储的生产 Runner。</summary>
    private static Fixture CreateFixture()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new TenantContext(tenantId, "acme", "Acme");
        var currentTenant = new CurrentTenantAccessor();
        var store = new ImportTaskStore(currentTenant);
        var handler = Substitute.For<IStaticImportSchemaHandler>();
        handler.SchemaKey.Returns("organization.tenant_positions");
        var invocations = 0;
        handler.ExecuteBatchAsync(
                Arg.Any<Stream>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<StaticImportPreviewContext>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                invocations++;
                var start = call.ArgAt<int>(2);
                return Result<StaticImportBatchExecutionResult>.Success(
                    new StaticImportBatchExecutionResult(
                    [
                        new StaticImportRowExecutionResult(start + 1, true, Guid.NewGuid(), null, null),
                    ]));
            });
        var resolver = Substitute.For<IActiveTenantContextResolver>();
        resolver.ResolveActiveByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.NewGuid());
        var files = new FakeFileStore();
        var options = Options.Create(new ImportExportOptions
        {
            ExecutionEnabled = true,
            BatchSize = 50,
            LeaseSeconds = 120,
            PollSeconds = 15,
            MaxUploadBytes = 1024 * 1024,
        });
        var runner = new ImportExportTaskRunner(
            store,
            store,
            new DapperCommandTransaction(new RecordingDbTransactionCoordinator()),
            files,
            new StaticImportSchemaRegistry([handler]),
            resolver,
            currentTenant,
            clock,
            ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            new OptionsMonitorStub<ImportExportOptions>(options.Value));
        return new Fixture(store, runner, handler, files, clock, tenantId, () => invocations);
    }

    private static ImportExportTaskRecord SeedQueuedTask(Fixture fixture, int validRowCount = 2) =>
        SeedTask(fixture, ImportExportTaskStatusKeys.Queued, 0, 0, null, null, validRowCount);

    private static ImportExportTaskRecord SeedExecutingTask(
        Fixture fixture,
        bool leaseExpired,
        int nextLineNumber,
        int succeededRowCount)
    {
        var leaseId = Guid.NewGuid();
        var expires = leaseExpired ? fixture.Clock.UtcNow.AddMinutes(-1) : fixture.Clock.UtcNow.AddMinutes(5);
        return SeedTask(
            fixture,
            ImportExportTaskStatusKeys.Executing,
            nextLineNumber,
            succeededRowCount,
            leaseId,
            expires);
    }

    private static ImportExportTaskRecord SeedTask(
        Fixture fixture,
        string statusKey,
        int nextLineNumber,
        int succeededRowCount,
        Guid? leaseId,
        DateTimeOffset? leaseExpiresAtUtc,
        int validRowCount = 2)
    {
        var taskId = Guid.NewGuid();
        var record = CreateTask(
            fixture,
            taskId,
            statusKey,
            nextLineNumber,
            succeededRowCount,
            leaseId,
            leaseExpiresAtUtc,
            validRowCount);
        fixture.Store.Tasks[taskId] = record;
        return record;
    }

    private static ImportExportTaskRecord CreateTask(
        Fixture fixture,
        Guid taskId,
        string statusKey,
        int nextLineNumber,
        int succeededRowCount,
        Guid? leaseId,
        DateTimeOffset? leaseExpiresAtUtc,
        int validRowCount = 2) =>
        new()
        {
            Id = taskId,
            TenantId = fixture.TenantId,
            SchemaKey = "organization.tenant_positions",
            SchemaDisplayName = "positions",
            WorksheetKey = "positions",
            SourceFileId = Guid.NewGuid(),
            SourceFileName = "source.xlsx",
            StatusKey = statusKey,
            TotalRows = validRowCount,
            ValidRowCount = validRowCount,
            RequestedByUserId = Guid.NewGuid(),
            CreatedAtUtc = fixture.Clock.UtcNow.AddMinutes(-10),
            ProcessedRowCount = nextLineNumber,
            SucceededRowCount = succeededRowCount,
            NextLineNumber = nextLineNumber,
            LeaseId = leaseId,
            LeaseExpiresAtUtc = leaseExpiresAtUtc,
            Version = 1,
        };

    private sealed class Fixture(
        ImportTaskStore store,
        ImportExportTaskRunner runner,
        IStaticImportSchemaHandler handler,
        FakeFileStore files,
        MutableClock clock,
        Guid tenantId,
        Func<int> invocations)
    {
        public ImportTaskStore Store { get; } = store;
        public ImportExportTaskRunner Runner { get; } = runner;
        public IStaticImportSchemaHandler Handler { get; } = handler;
        public FakeFileStore Files { get; } = files;
        public MutableClock Clock { get; } = clock;
        public Guid TenantId { get; } = tenantId;
        public int HandlerInvocations => invocations();
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class OptionsMonitorStub<T>(T current) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue { get; } = current;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class FakeFileStore : ITenantResourceFileStore
    {
        public int UploadCount { get; private set; }
        public List<TenantResourceFileReadyItem> ReadyFiles { get; } = [];

        public Task<Result<TenantResourceFileReference>> UploadAsync(
            string ownerModuleKey, Guid resourceId, Guid actorUserId, string originalFileName,
            string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default)
        {
            UploadCount++;
            var fileId = Guid.NewGuid();
            ReadyFiles.Add(new TenantResourceFileReadyItem(fileId, originalFileName, DateTimeOffset.UtcNow));
            return Task.FromResult(Result<TenantResourceFileReference>.Success(new(fileId, contentLength, "hash")));
        }

        public Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(
            string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<TenantResourceFileContent>.Success(
                new TenantResourceFileContent(new MemoryStream([1, 2, 3]), "application/test", "source.xlsx")));

        public Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(
            string ownerModuleKey, Guid resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantResourceFileReadyItem>>(ReadyFiles);

        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ImportTaskStore(ICurrentTenant tenant) : IQueryExecutor, ICommandExecutor
    {
        public Dictionary<Guid, ImportExportTaskRecord> Tasks { get; } = [];

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            SqlScopeGuard.Validate(statement, tenant);
            var values = Params(parameters);
            if (statement.Name == ImportExportTaskSql.FindById.Name
                && Tasks.TryGetValue((Guid)values["Id"]!, out var task))
            {
                return Task.FromResult((T?)(object)task);
            }

            return Task.FromResult(default(T));
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            SqlScopeGuard.Validate(statement, tenant);
            var values = Params(parameters);
            var now = values.TryGetValue("Now", out var nowValue) && nowValue is DateTimeOffset stamp
                ? stamp
                : DateTimeOffset.UtcNow;
            if (statement.Name == ImportExportTaskSql.ListPendingTenantIdsSqlServer.Name)
            {
                var tenants = Tasks.Values.Where(task => IsClaimable(task, now))
                    .Select(task => task.TenantId).Distinct().Cast<T>().ToArray();
                return Task.FromResult<IReadOnlyList<T>>(tenants);
            }

            if (statement.Name == ImportExportTaskSql.ClaimQueuedSqlServer.Name)
            {
                var claimed = ClaimOne(now, (Guid)values["LeaseId"]!, (DateTimeOffset)values["LeaseExpiresAtUtc"]!);
                return Task.FromResult<IReadOnlyList<T>>(claimed is null ? [] : [(T)(object)claimed]);
            }

            return Task.FromResult<IReadOnlyList<T>>([]);
        }

        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            SqlScopeGuard.Validate(statement, tenant);
            var values = Params(parameters);
            if (!Tasks.TryGetValue((Guid)values["Id"]!, out var current))
            {
                return Task.FromResult(0);
            }

            if (statement.Name == ImportExportTaskSql.UpdateExecutionProgress.Name)
            {
                if (!string.Equals(current.StatusKey, ImportExportTaskStatusKeys.Executing, StringComparison.Ordinal)
                    || current.LeaseId != (Guid?)values["LeaseId"])
                {
                    return Task.FromResult(0);
                }

                var status = (string)values["StatusKey"]!;
                Tasks[current.Id] = Clone(current, status, values);
                return Task.FromResult(1);
            }

            if (statement.Name == ImportExportTaskSql.MarkExecutionFailed.Name)
            {
                if (!string.Equals(current.StatusKey, ImportExportTaskStatusKeys.Executing, StringComparison.Ordinal)
                    || current.LeaseId != (Guid?)values["LeaseId"])
                {
                    return Task.FromResult(0);
                }

                Tasks[current.Id] = CloneFailed(current, (string)values["ErrorCode"]!, (DateTimeOffset)values["ExecutionCompletedAtUtc"]!);
                return Task.FromResult(1);
            }

            return Task.FromResult(0);
        }

        private ImportExportTaskRecord? ClaimOne(DateTimeOffset now, Guid leaseId, DateTimeOffset leaseExpiresAtUtc)
        {
            var candidate = Tasks.Values.Where(task => IsClaimable(task, now)).OrderBy(task => task.CreatedAtUtc)
                .ThenBy(task => task.Id).FirstOrDefault();
            if (candidate is null)
            {
                return null;
            }

            var claimed = new ImportExportTaskRecord
            {
                Id = candidate.Id,
                TenantId = candidate.TenantId,
                SchemaKey = candidate.SchemaKey,
                SchemaDisplayName = candidate.SchemaDisplayName,
                WorksheetKey = candidate.WorksheetKey,
                SourceFileId = candidate.SourceFileId,
                SourceFileName = candidate.SourceFileName,
                StatusKey = ImportExportTaskStatusKeys.Executing,
                TotalRows = candidate.TotalRows,
                ValidRowCount = candidate.ValidRowCount,
                InvalidRowCount = candidate.InvalidRowCount,
                PreviewRowsJson = candidate.PreviewRowsJson,
                ErrorCode = candidate.ErrorCode,
                RequestedByUserId = candidate.RequestedByUserId,
                CreatedAtUtc = candidate.CreatedAtUtc,
                PreviewCompletedAtUtc = candidate.PreviewCompletedAtUtc,
                ProcessedRowCount = candidate.ProcessedRowCount,
                SucceededRowCount = candidate.SucceededRowCount,
                ExecutionFailedRowCount = candidate.ExecutionFailedRowCount,
                NextLineNumber = candidate.NextLineNumber,
                ExecutionRowsJson = candidate.ExecutionRowsJson,
                ErrorReceiptFileId = candidate.ErrorReceiptFileId,
                ExecutionStartedAtUtc = candidate.ExecutionStartedAtUtc ?? now,
                ExecutionCompletedAtUtc = candidate.ExecutionCompletedAtUtc,
                LeaseId = leaseId,
                LeaseExpiresAtUtc = leaseExpiresAtUtc,
                Version = candidate.Version + 1,
            };
            Tasks[claimed.Id] = claimed;
            return claimed;
        }

        private static bool IsClaimable(ImportExportTaskRecord task, DateTimeOffset now) =>
            task.StatusKey == ImportExportTaskStatusKeys.Queued
            || (task.StatusKey == ImportExportTaskStatusKeys.Executing
                && (task.LeaseExpiresAtUtc is null || task.LeaseExpiresAtUtc <= now));

        private static ImportExportTaskRecord Clone(
            ImportExportTaskRecord current,
            string statusKey,
            IReadOnlyDictionary<string, object?> values)
        {
            var terminal = statusKey is ImportExportTaskStatusKeys.Queued
                or ImportExportTaskStatusKeys.ExecutionSucceeded
                or ImportExportTaskStatusKeys.ExecutionPartial
                or ImportExportTaskStatusKeys.ExecutionFailed;
            return new ImportExportTaskRecord
            {
                Id = current.Id,
                TenantId = current.TenantId,
                SchemaKey = current.SchemaKey,
                SchemaDisplayName = current.SchemaDisplayName,
                WorksheetKey = current.WorksheetKey,
                SourceFileId = current.SourceFileId,
                SourceFileName = current.SourceFileName,
                StatusKey = statusKey,
                TotalRows = current.TotalRows,
                ValidRowCount = current.ValidRowCount,
                InvalidRowCount = current.InvalidRowCount,
                PreviewRowsJson = current.PreviewRowsJson,
                ErrorCode = values["ErrorCode"] as string,
                RequestedByUserId = current.RequestedByUserId,
                CreatedAtUtc = current.CreatedAtUtc,
                PreviewCompletedAtUtc = current.PreviewCompletedAtUtc,
                ProcessedRowCount = (int)values["ProcessedRowCount"]!,
                SucceededRowCount = (int)values["SucceededRowCount"]!,
                ExecutionFailedRowCount = (int)values["ExecutionFailedRowCount"]!,
                NextLineNumber = (int)values["NextLineNumber"]!,
                ExecutionRowsJson = values["ExecutionRowsJson"] as string,
                ErrorReceiptFileId = values["ErrorReceiptFileId"] as Guid?,
                ExecutionStartedAtUtc = current.ExecutionStartedAtUtc,
                ExecutionCompletedAtUtc = values["ExecutionCompletedAtUtc"] as DateTimeOffset?,
                LeaseId = terminal ? null : current.LeaseId,
                LeaseExpiresAtUtc = terminal ? null : current.LeaseExpiresAtUtc,
                Version = current.Version + 1,
            };
        }

        private static ImportExportTaskRecord CloneFailed(
            ImportExportTaskRecord current,
            string errorCode,
            DateTimeOffset completedAtUtc) =>
            new()
            {
                Id = current.Id,
                TenantId = current.TenantId,
                SchemaKey = current.SchemaKey,
                SchemaDisplayName = current.SchemaDisplayName,
                WorksheetKey = current.WorksheetKey,
                SourceFileId = current.SourceFileId,
                SourceFileName = current.SourceFileName,
                StatusKey = ImportExportTaskStatusKeys.ExecutionFailed,
                TotalRows = current.TotalRows,
                ValidRowCount = current.ValidRowCount,
                InvalidRowCount = current.InvalidRowCount,
                PreviewRowsJson = current.PreviewRowsJson,
                ErrorCode = errorCode,
                RequestedByUserId = current.RequestedByUserId,
                CreatedAtUtc = current.CreatedAtUtc,
                PreviewCompletedAtUtc = current.PreviewCompletedAtUtc,
                ProcessedRowCount = current.ProcessedRowCount,
                SucceededRowCount = current.SucceededRowCount,
                ExecutionFailedRowCount = current.ExecutionFailedRowCount,
                NextLineNumber = current.NextLineNumber,
                ExecutionRowsJson = current.ExecutionRowsJson,
                ErrorReceiptFileId = current.ErrorReceiptFileId,
                ExecutionStartedAtUtc = current.ExecutionStartedAtUtc,
                ExecutionCompletedAtUtc = completedAtUtc,
                Version = current.Version + 1,
            };

        private static IReadOnlyDictionary<string, object?> Params(object? parameters) =>
            parameters as IReadOnlyDictionary<string, object?>
            ?? throw new InvalidOperationException("Named SQL parameters are required.");
    }
}
