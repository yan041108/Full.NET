using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Reporting.Configuration;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Reporting;

/// <summary>报表导出必须按租约恢复，上传成功后崩溃要绑定已有文件，取消不得写成失败。</summary>
[TestClass]
public sealed class ReportingExportTaskRecoveryTests
{
    /// <summary>上传已成功但任务仍 processing 时，恢复必须完成且不再生成工作簿。</summary>
    [TestMethod]
    public async Task Crash_after_upload_completes_from_existing_file_async()
    {
        var fixture = CreateFixture();
        var task = SeedProcessing(fixture, leaseExpired: true);
        var fileId = Guid.NewGuid();
        fixture.Files.ReadyFiles.Add(new TenantResourceFileReadyItem(fileId, "report.xlsx", fixture.Clock.UtcNow));

        var processed = await fixture.Runner.ProcessPendingAsync(CancellationToken.None);

        Assert.AreEqual(1, processed);
        Assert.AreEqual(0, fixture.GenerateCount);
        var stored = fixture.Store.Tasks[task.Id];
        Assert.AreEqual(ReportingExportTaskStatusKeys.Succeeded, stored.StatusKey);
        Assert.AreEqual(fileId, stored.OutputFileId);
        Assert.IsNull(stored.LeaseId);
    }

    /// <summary>有效租约禁止第二个 Worker 领取。</summary>
    [TestMethod]
    public async Task Active_lease_rejects_overlapping_claim_async()
    {
        var fixture = CreateFixture();
        SeedProcessing(fixture, leaseExpired: false);

        var processed = await fixture.Runner.ProcessPendingAsync(CancellationToken.None);

        Assert.AreEqual(0, processed);
        Assert.AreEqual(0, fixture.GenerateCount);
        Assert.AreEqual(ReportingExportTaskStatusKeys.Processing, fixture.Store.Tasks.Values.Single().StatusKey);
    }

    /// <summary>请求取消必须保留 processing。</summary>
    [TestMethod]
    public async Task Cancel_does_not_mark_failed_async()
    {
        var fixture = CreateFixture();
        SeedQueued(fixture);
        using var cts = new CancellationTokenSource();
        fixture.Workbook.GenerateAsync(
                Arg.Any<ReportingExportTaskRecord>(),
                Arg.Any<ClaimsPrincipal>(),
                Arg.Any<CancellationToken>())
            .Returns<Result<ReportingExportGeneratedFile>>(_ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => fixture.Runner.ProcessPendingAsync(cts.Token));

        var stored = fixture.Store.Tasks.Values.Single();
        Assert.AreEqual(ReportingExportTaskStatusKeys.Processing, stored.StatusKey);
        Assert.IsNull(stored.ErrorCode);
    }

    /// <summary>已经成功的任务不能被过期租约重领后再次生成。</summary>
    [TestMethod]
    public async Task Succeeded_task_is_not_regenerated_async()
    {
        var fixture = CreateFixture();
        var taskId = Guid.NewGuid();
        fixture.Store.Tasks[taskId] = CreateTask(
            fixture,
            taskId,
            ReportingExportTaskStatusKeys.Succeeded,
            outputFileId: Guid.NewGuid(),
            leaseId: null,
            leaseExpiresAtUtc: null);

        var processed = await fixture.Runner.ProcessPendingAsync(CancellationToken.None);

        Assert.AreEqual(0, processed);
        Assert.AreEqual(0, fixture.GenerateCount);
    }

    private static Fixture CreateFixture()
    {
        var tenantId = Guid.NewGuid();
        var tenant = new TenantContext(tenantId, "acme", "Acme");
        var currentTenant = new CurrentTenantAccessor();
        var store = new ExportTaskStore(currentTenant);
        var workbook = Substitute.For<IReportingExportWorkbookSource>();
        var generateCount = 0;
        workbook.GenerateAsync(Arg.Any<ReportingExportTaskRecord>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                generateCount++;
                return Result<ReportingExportGeneratedFile>.Success(
                    new ReportingExportGeneratedFile(1, "report.xlsx", [1, 2, 3]));
            });
        var resolver = Substitute.For<IActiveTenantContextResolver>();
        resolver.ResolveActiveByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        var clock = new MutableClock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
        var ids = Substitute.For<IIdGenerator>();
        ids.NewId().Returns(_ => Guid.NewGuid());
        var files = new FakeFileStore();
        var options = new ReportingExportOptions { ExecutionEnabled = true, BatchSize = 20, LeaseSeconds = 300, PollSeconds = 15 };
        var runner = new ReportingExportTaskRunner(
            store,
            store,
            new DapperCommandTransaction(new RecordingDbTransactionCoordinator()),
            files,
            workbook,
            resolver,
            currentTenant,
            clock,
            ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            new OptionsMonitorStub<ReportingExportOptions>(options));
        return new Fixture(store, runner, workbook, files, clock, tenantId, () => generateCount);
    }

    private static ReportingExportTaskRecord SeedQueued(Fixture fixture) =>
        Seed(fixture, ReportingExportTaskStatusKeys.Queued, leaseExpired: true);

    private static ReportingExportTaskRecord SeedProcessing(Fixture fixture, bool leaseExpired) =>
        Seed(fixture, ReportingExportTaskStatusKeys.Processing, leaseExpired);

    private static ReportingExportTaskRecord Seed(Fixture fixture, string statusKey, bool leaseExpired)
    {
        var taskId = Guid.NewGuid();
        var record = CreateTask(
            fixture,
            taskId,
            statusKey,
            outputFileId: null,
            leaseId: statusKey == ReportingExportTaskStatusKeys.Queued ? null : Guid.NewGuid(),
            leaseExpiresAtUtc: statusKey == ReportingExportTaskStatusKeys.Queued
                ? null
                : (leaseExpired ? fixture.Clock.UtcNow.AddMinutes(-1) : fixture.Clock.UtcNow.AddMinutes(5)));
        fixture.Store.Tasks[taskId] = record;
        return record;
    }

    private static ReportingExportTaskRecord CreateTask(
        Fixture fixture,
        Guid taskId,
        string statusKey,
        Guid? outputFileId,
        Guid? leaseId,
        DateTimeOffset? leaseExpiresAtUtc) =>
        new()
        {
            Id = taskId,
            TenantId = fixture.TenantId,
            DefinitionId = Guid.NewGuid(),
            VersionNumber = 1,
            DefinitionKey = "demo",
            DefinitionName = "Demo",
            FormatKey = ReportingExportFormatKeys.Excel,
            ParametersJson = "[]",
            StatusKey = statusKey,
            OutputFileId = outputFileId,
            OutputFileName = outputFileId is null ? null : "report.xlsx",
            RequestedByUserId = Guid.NewGuid(),
            CreatedAtUtc = fixture.Clock.UtcNow.AddMinutes(-10),
            LeaseId = leaseId,
            LeaseExpiresAtUtc = leaseExpiresAtUtc,
            ActorPermissionCodesJson = """["reporting.executions.run"]""",
            Version = 1,
        };

    private sealed class Fixture(
        ExportTaskStore store,
        ReportingExportTaskRunner runner,
        IReportingExportWorkbookSource workbook,
        FakeFileStore files,
        MutableClock clock,
        Guid tenantId,
        Func<int> generateCount)
    {
        public ExportTaskStore Store { get; } = store;
        public ReportingExportTaskRunner Runner { get; } = runner;
        public IReportingExportWorkbookSource Workbook { get; } = workbook;
        public FakeFileStore Files { get; } = files;
        public MutableClock Clock { get; } = clock;
        public Guid TenantId { get; } = tenantId;
        public int GenerateCount => generateCount();
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
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
        public List<TenantResourceFileReadyItem> ReadyFiles { get; } = [];

        public Task<Result<TenantResourceFileReference>> UploadAsync(
            string ownerModuleKey, Guid resourceId, Guid actorUserId, string originalFileName,
            string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<TenantResourceFileReference>.Success(new(Guid.NewGuid(), contentLength, "hash")));

        public Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(
            string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<TenantResourceFileContent>.Failure(
                new Error("files.not_found", "not found", ErrorType.NotFound)));

        public Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(
            string ownerModuleKey, Guid resourceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantResourceFileReadyItem>>(ReadyFiles);

        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ExportTaskStore(ICurrentTenant tenant) : IQueryExecutor, ICommandExecutor
    {
        public Dictionary<Guid, ReportingExportTaskRecord> Tasks { get; } = [];

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            SqlScopeGuard.Validate(statement, tenant);
            var values = Params(parameters);
            if ((statement.Name == ReportingExportTaskSql.FindById.Name
                    || statement.Name == ReportingExportTaskSql.FindByIdSqlServer.Name)
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
            if (statement.Name == ReportingExportTaskSql.ListPendingTenantIdsSqlServer.Name)
            {
                var tenants = Tasks.Values.Where(task => IsClaimable(task, now))
                    .Select(task => task.TenantId).Distinct().Cast<T>().ToArray();
                return Task.FromResult<IReadOnlyList<T>>(tenants);
            }

            if (statement.Name is "reporting.export_task.claim.sqlserver"
                or "reporting.export_task.claim_by_id.sqlserver")
            {
                Guid? requiredId = values.TryGetValue("Id", out var idValue) && idValue is Guid guid ? guid : null;
                var claimed = ClaimOne(now, (Guid)values["LeaseId"]!, (DateTimeOffset)values["LeaseExpiresAtUtc"]!, requiredId);
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

            if (current.LeaseId != (Guid?)values["LeaseId"]
                || !string.Equals(current.StatusKey, ReportingExportTaskStatusKeys.Processing, StringComparison.Ordinal))
            {
                return Task.FromResult(0);
            }

            if (statement.Name == ReportingExportTaskSql.CompleteSucceeded.Name
                || statement.Name == ReportingExportTaskSql.CompleteSucceededSqlServer.Name)
            {
                current.StatusKey = ReportingExportTaskStatusKeys.Succeeded;
                current.OutputFileId = (Guid)values["OutputFileId"]!;
                current.OutputFileName = values["OutputFileName"] as string;
                current.RowCount = (int)values["RowCount"]!;
                current.CompletedAtUtc = values["CompletedAtUtc"] as DateTimeOffset?;
                current.LeaseId = null;
                current.LeaseExpiresAtUtc = null;
                current.Version++;
                return Task.FromResult(1);
            }

            if (statement.Name == ReportingExportTaskSql.CompleteFailed.Name
                || statement.Name == ReportingExportTaskSql.CompleteFailedSqlServer.Name)
            {
                current.StatusKey = ReportingExportTaskStatusKeys.Failed;
                current.ErrorCode = values["ErrorCode"] as string;
                current.ErrorMessage = values["ErrorMessage"] as string;
                current.CompletedAtUtc = values["CompletedAtUtc"] as DateTimeOffset?;
                current.LeaseId = null;
                current.LeaseExpiresAtUtc = null;
                current.Version++;
                return Task.FromResult(1);
            }

            return Task.FromResult(0);
        }

        private ReportingExportTaskRecord? ClaimOne(
            DateTimeOffset now,
            Guid leaseId,
            DateTimeOffset leaseExpiresAtUtc,
            Guid? requiredId)
        {
            var candidate = Tasks.Values
                .Where(task => (requiredId is null || task.Id == requiredId) && IsClaimable(task, now))
                .OrderBy(task => task.CreatedAtUtc)
                .ThenBy(task => task.Id)
                .FirstOrDefault();
            if (candidate is null)
            {
                return null;
            }

            candidate.StatusKey = ReportingExportTaskStatusKeys.Processing;
            candidate.LeaseId = leaseId;
            candidate.LeaseExpiresAtUtc = leaseExpiresAtUtc;
            candidate.Version++;
            return candidate;
        }

        private static bool IsClaimable(ReportingExportTaskRecord task, DateTimeOffset now) =>
            task.StatusKey == ReportingExportTaskStatusKeys.Queued
            || (task.StatusKey == ReportingExportTaskStatusKeys.Processing
                && (task.LeaseExpiresAtUtc is null || task.LeaseExpiresAtUtc <= now));

        private static IReadOnlyDictionary<string, object?> Params(object? parameters) =>
            parameters as IReadOnlyDictionary<string, object?>
            ?? throw new InvalidOperationException("Named SQL parameters are required.");
    }
}
