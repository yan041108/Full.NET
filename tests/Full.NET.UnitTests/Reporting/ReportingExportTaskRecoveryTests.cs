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
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Features.PublishedDefinitions;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Reporting;

/// <summary>报表导出必须按租约恢复，上传成功后崩溃要绑定已有文件，取消不得写成失败。</summary>
[TestClass]
public sealed class ReportingExportTaskRecoveryTests
{
    /// <summary>旧队列没有当前会话委托时，不能重用文件或凭旧权限快照生成。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Missing_session_binding_blocks_generation_and_file_attachment(bool existingOutput)
    {
        var fixture = CreateFixture();
        var task = SeedProcessing(fixture, leaseExpired: true);
        task.ActorPermissionCodesJson = "[\"reporting.executions.run\"]";
        if (existingOutput)
            fixture.Files.ReadyFiles.Add(new TenantResourceFileReadyItem(Guid.NewGuid(), "report.xlsx", fixture.Clock.UtcNow));
        await fixture.Runner.ProcessPendingAsync(CancellationToken.None);
        Assert.AreEqual(0, fixture.GenerateCount);
        Assert.AreEqual(ReportingExportTaskStatusKeys.Failed, fixture.Store.Tasks[task.Id].StatusKey);
        Assert.AreEqual("authorization.permission_denied", fixture.Store.Tasks[task.Id].ErrorCode);
    }

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

    /// <summary>会话撤销、版本撤销与绑定主体不符都不能复用已上传文件或生成新文件。</summary>
    [TestMethod]
    [DataRow("session", false)]
    [DataRow("session", true)]
    [DataRow("grant", false)]
    [DataRow("grant", true)]
    [DataRow("actor", false)]
    [DataRow("actor", true)]
    public async Task Current_authorization_blocks_recovery(string reason, bool existingOutput)
    {
        var fixture = CreateFixture(); var task = SeedProcessing(fixture, true);
        if (reason == "session") fixture.Identity.AuthorizeAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AuthorizedSessionActor?>(null));
        if (reason == "grant") fixture.Store.Granted = false;
        if (reason == "actor") task.RequestedByUserId = Guid.NewGuid();
        if (existingOutput) fixture.Files.ReadyFiles.Add(new(Guid.NewGuid(), "report.xlsx", fixture.Clock.UtcNow));
        Assert.AreEqual(1, await fixture.Runner.ProcessPendingAsync(CancellationToken.None));
        Assert.AreEqual(0, fixture.GenerateCount);
        Assert.AreEqual(ReportingExportTaskStatusKeys.Failed, task.StatusKey);
        Assert.AreEqual(CommonErrorCodes.PermissionDenied, task.ErrorCode);
    }

    /// <summary>生成期间撤销授权必须阻止上传，不能仅靠下载入口事后拦截。</summary>
    [TestMethod]
    public async Task Revocation_during_generation_blocks_upload()
    {
        var fixture = CreateFixture(); var task = SeedQueued(fixture);
        fixture.Workbook.GenerateAsync(Arg.Any<ReportingExportTaskRecord>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(_ => { fixture.Store.Granted = false;
                return Result<ReportingExportGeneratedFile>.Success(new(1, "report.xlsx", [1])); });
        await fixture.Runner.ProcessPendingAsync(CancellationToken.None);
        Assert.AreEqual(ReportingExportTaskStatusKeys.Failed, task.StatusKey);
        Assert.AreEqual(CommonErrorCodes.PermissionDenied, task.ErrorCode);
        Assert.IsNull(task.OutputFileId);
        Assert.AreEqual(0, fixture.Files.UploadCount);
    }

    /// <summary>超级管理员令牌省略逐项权限，但原导出列快照仍需冻结当前有效权限。</summary>
    [TestMethod]
    public async Task Super_administrator_freezes_effective_column_permissions()
    {
        var fixture = CreateFixture(); fixture.Context.SetTenant(new TenantContext(fixture.TenantId, "acme", "Acme"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(FullNetIdentityClaimTypes.SuperAdministrator, "true"),
            new Claim(FullNetIdentityClaimTypes.Scope, $"tenant:{fixture.TenantId:N}")], "test"));
        var binding = new SessionBindingSnapshot(fixture.ActorId, fixture.TenantId, Guid.NewGuid(), "stamp", "host", $"tenant:{fixture.TenantId:N}");
        var result = await fixture.Management.CreateAsync(new(Guid.NewGuid(), "excel", 1, []), fixture.ActorId, principal, binding);
        Assert.IsTrue(result.IsSuccess);
        var snapshot = ReportingExportTaskMapper.DeserializeAuthorization(fixture.Store.Tasks.Values.Single().ActorPermissionCodesJson)!;
        CollectionAssert.Contains(snapshot.PermissionCodes, ReportingExecutionPermissions.ColumnSchemaName);
        CollectionAssert.DoesNotContain(snapshot.PermissionCodes, ReportingDefinitionPermissions.GrantTenants);
        var executionPrincipal = (ClaimsPrincipal)fixture.Workbook.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IReportingExportWorkbookSource.GenerateAsync)).GetArguments()[1]!;
        Assert.AreEqual($"tenant:{fixture.TenantId:N}", executionPrincipal.FindFirst(FullNetIdentityClaimTypes.Scope)!.Value);
        Assert.IsTrue(executionPrincipal.HasClaim(FullNetIdentityClaimTypes.Permission, ReportingExecutionPermissions.ColumnSchemaName));
        Assert.IsFalse(executionPrincipal.HasClaim(FullNetIdentityClaimTypes.SuperAdministrator, "true"));
    }

    /// <summary>普通租户令牌的原列快照只冻结目录内当前有效权限，拒绝 Host 专属或未知声明。</summary>
    [TestMethod]
    public async Task Tenant_snapshot_excludes_host_only_and_unknown_permissions()
    {
        var fixture = CreateFixture(); fixture.Context.SetTenant(new TenantContext(fixture.TenantId, "acme", "Acme"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(FullNetIdentityClaimTypes.Scope, $"tenant:{fixture.TenantId:N}"),
            new Claim(FullNetIdentityClaimTypes.Permission, ReportingExecutionPermissions.ColumnSchemaName),
            new Claim(FullNetIdentityClaimTypes.Permission, ReportingDefinitionPermissions.GrantTenants),
            new Claim(FullNetIdentityClaimTypes.Permission, "unknown.column")], "test"));
        var binding = new SessionBindingSnapshot(fixture.ActorId, fixture.TenantId, Guid.NewGuid(), "stamp", "host", $"tenant:{fixture.TenantId:N}");
        var result = await fixture.Management.CreateAsync(new(Guid.NewGuid(), "excel", 1, []), fixture.ActorId, principal, binding);
        Assert.IsTrue(result.IsSuccess);
        var snapshot = ReportingExportTaskMapper.DeserializeAuthorization(fixture.Store.Tasks.Values.Single().ActorPermissionCodesJson)!;
        CollectionAssert.AreEquivalent(new[] { ReportingExecutionPermissions.ColumnSchemaName }, snapshot.PermissionCodes);
    }

    /// <summary>创建过程中的底层授权撤销必须保留为 Forbidden，不能持久化后降成输入错误。</summary>
    [TestMethod]
    public async Task Persisted_permission_denial_retains_forbidden_contract()
    {
        var fixture = CreateFixture(); fixture.Context.SetTenant(new TenantContext(fixture.TenantId, "acme", "Acme"));
        fixture.Workbook.GenerateAsync(Arg.Any<ReportingExportTaskRecord>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(Result<ReportingExportGeneratedFile>.Failure(new(CommonErrorCodes.PermissionDenied, "Revoked", ErrorType.Forbidden)));
        var binding = new SessionBindingSnapshot(fixture.ActorId, fixture.TenantId, Guid.NewGuid(), "stamp", "host", $"tenant:{fixture.TenantId:N}");
        var result = await fixture.Management.CreateAsync(new(Guid.NewGuid(), "excel", 1, []), fixture.ActorId,
            new ClaimsPrincipal(new ClaimsIdentity("test")), binding);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(CommonErrorCodes.PermissionDenied, result.Error!.Code);
        Assert.AreEqual(ErrorType.Forbidden, result.Error.Type);
        Assert.AreEqual(ReportingExportTaskStatusKeys.Failed, fixture.Store.Tasks.Values.Single().StatusKey);
    }

    /// <summary>生成返回成功或失败时若租约已到期，都应停止上传和终态写入。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Expired_lease_during_generation_preserves_processing(bool failure)
    {
        var fixture = CreateFixture(); var task = SeedQueued(fixture);
        fixture.Workbook.GenerateAsync(Arg.Any<ReportingExportTaskRecord>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(_ => { fixture.Clock.UtcNow = task.LeaseExpiresAtUtc!.Value;
                return failure ? Result<ReportingExportGeneratedFile>.Failure(new("probe.failure", "probe", ErrorType.Validation))
                    : Result<ReportingExportGeneratedFile>.Success(new(1, "report.xlsx", [1])); });
        await RunOwnedAsync(fixture, task);
        AssertProcessing(task); Assert.AreEqual(0, fixture.Files.UploadCount);
    }

    /// <summary>忽略取消令牌的文件列表返回后，已到期持有者不能绑定文件。</summary>
    [TestMethod]
    public async Task Expired_lease_during_existing_file_lookup_preserves_processing()
    {
        var fixture = CreateFixture(); var task = SeedQueued(fixture);
        fixture.Files.ReadyFiles.Add(new(Guid.NewGuid(), "report.xlsx", fixture.Clock.UtcNow));
        fixture.Files.OnList = () => fixture.Clock.UtcNow = task.LeaseExpiresAtUtc!.Value;
        await RunOwnedAsync(fixture, task);
        AssertProcessing(task); Assert.AreEqual(0, fixture.GenerateCount);
    }

    /// <summary>上传期间到期无论存储返回成功还是失败，都不得覆盖可恢复的任务。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Expired_lease_during_upload_preserves_processing(bool failure)
    {
        var fixture = CreateFixture(); var task = SeedQueued(fixture);
        fixture.Files.FailUpload = failure;
        fixture.Files.OnUpload = () => fixture.Clock.UtcNow = task.LeaseExpiresAtUtc!.Value;
        await RunOwnedAsync(fixture, task);
        AssertProcessing(task); Assert.AreEqual(1, fixture.Files.UploadCount);
        Assert.IsFalse(fixture.Files.UploadStream!.CanRead);
    }

    /// <summary>完成前授权复核跨过租约期限，允许或拒绝都不能写入终态。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Expired_lease_during_completion_authorization_preserves_processing(bool denied)
    {
        var fixture = CreateFixture(); var task = SeedQueued(fixture);
        fixture.Identity.AuthorizeAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => { var binding = call.Arg<SessionBindingSnapshot>()!;
                if (fixture.Files.UploadCount > 0) { fixture.Clock.UtcNow = task.LeaseExpiresAtUtc!.Value;
                    if (denied) return null; }
                return new AuthorizedSessionActor(binding.UserId, binding.TenantId, binding.SessionId); });
        await RunOwnedAsync(fixture, task);
        AssertProcessing(task); Assert.AreEqual(1, fixture.Files.UploadCount);
    }

    /// <summary>领取返回时期限已到，不得继续授权、查文件或生成。</summary>
    [TestMethod]
    public async Task Expired_lease_at_claim_return_stops_execution()
    {
        var fixture = CreateFixture(); var task = SeedQueued(fixture);
        fixture.Store.OnClaim = () => fixture.Clock.UtcNow = task.LeaseExpiresAtUtc!.Value;
        await RunOwnedAsync(fixture, task);
        AssertProcessing(task); Assert.AreEqual(0, fixture.GenerateCount);
        Assert.AreEqual(0, fixture.Files.ListCount);
    }

    /// <summary>租约的剩余期限应主动取消工作簿生成，不等待调用方取消。</summary>
    [TestMethod]
    public async Task Remaining_lease_deadline_cancels_generation()
    {
        var fixture = CreateFixture(); var task = SeedQueued(fixture); var cancelled = false;
        fixture.Store.OnClaim = () => fixture.Clock.UtcNow = task.LeaseExpiresAtUtc!.Value.AddMilliseconds(-100);
        fixture.Workbook.GenerateAsync(Arg.Any<ReportingExportTaskRecord>(), Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(async call => { try { await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>()).WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (OperationCanceledException) { cancelled = true; throw; }
                return Result<ReportingExportGeneratedFile>.Success(new(1, "report.xlsx", [1])); });
        await RunOwnedAsync(fixture, task);
        Assert.IsTrue(cancelled, "生成必须收到租约期限取消。"); AssertProcessing(task);
        Assert.AreEqual(0, fixture.Files.UploadCount);
    }

    private static Task RunOwnedAsync(Fixture fixture, ReportingExportTaskRecord task)
    {
        fixture.Context.SetTenant(new TenantContext(fixture.TenantId, "acme", "Acme"));
        return fixture.Runner.RunOwnedAsync(task.Id, CancellationToken.None);
    }

    private static void AssertProcessing(ReportingExportTaskRecord task)
    {
        Assert.AreEqual(ReportingExportTaskStatusKeys.Processing, task.StatusKey);
        Assert.IsNotNull(task.LeaseId); Assert.IsNull(task.CompletedAtUtc);
        Assert.IsNull(task.OutputFileId); Assert.IsNull(task.ErrorCode);
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
        var identity = Substitute.For<IBackgroundSessionAuthorization>();
        identity.AuthorizeAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => { var binding = call.Arg<SessionBindingSnapshot>()!;
                return new AuthorizedSessionActor(binding.UserId, binding.TenantId, binding.SessionId); });
        var database = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
        var definitions = new ReportingPublishedDefinitionResolver(store, new ReportingDefinitionQueryService(store, database), currentTenant, database);
        var authorization = new ReportingExportAuthorization(currentTenant, identity, definitions);
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
            new OptionsMonitorStub<ReportingExportOptions>(options), authorization);
        var management = new ReportingExportTaskManagementService(definitions, files, store, store, runner, currentTenant,
            clock, ids, database, authorization,
            new Full.NET.Modules.Identity.Authorization.PermissionClaimEvaluator(
                Full.NET.Modules.Identity.Authorization.AuthorizationCatalog.Create([new Full.NET.Modules.Reporting.ReportingAuthorizationContributor()])));
        return new Fixture(store, runner, workbook, files, clock, tenantId, identity, currentTenant, management, () => generateCount);
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
            RequestedByUserId = fixture.ActorId,
            CreatedAtUtc = fixture.Clock.UtcNow.AddMinutes(-10),
            LeaseId = leaseId,
            LeaseExpiresAtUtc = leaseExpiresAtUtc,
            ActorPermissionCodesJson = ReportingExportTaskMapper.SerializeAuthorization([ReportingExecutionPermissions.Run],
                new SessionBindingSnapshot(fixture.ActorId, fixture.TenantId, Guid.NewGuid(), "stamp", "host", $"tenant:{fixture.TenantId:N}")),
            Version = 1,
        };

    private sealed class Fixture(
        ExportTaskStore store,
        ReportingExportTaskRunner runner,
        IReportingExportWorkbookSource workbook,
        FakeFileStore files,
        MutableClock clock,
        Guid tenantId,
        IBackgroundSessionAuthorization identity,
        CurrentTenantAccessor context,
        ReportingExportTaskManagementService management,
        Func<int> generateCount)
    {
        public ExportTaskStore Store { get; } = store;
        public IBackgroundSessionAuthorization Identity { get; } = identity;
        public CurrentTenantAccessor Context { get; } = context;
        public ReportingExportTaskManagementService Management { get; } = management;
        public ReportingExportTaskRunner Runner { get; } = runner;
        public IReportingExportWorkbookSource Workbook { get; } = workbook;
        public FakeFileStore Files { get; } = files;
        public MutableClock Clock { get; } = clock;
        public Guid TenantId { get; } = tenantId;
        public Guid ActorId { get; } = Guid.NewGuid();
        public int GenerateCount => generateCount();
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
        public List<TenantResourceFileReadyItem> ReadyFiles { get; } = [];
        public int UploadCount { get; private set; }
        public int ListCount { get; private set; }
        public Action? OnList { get; set; }
        public Action? OnUpload { get; set; }
        public bool FailUpload { get; set; }
        public Stream? UploadStream { get; private set; }

        public Task<Result<TenantResourceFileReference>> UploadAsync(
            string ownerModuleKey, Guid resourceId, Guid actorUserId, string originalFileName,
            string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default)
        {
            UploadCount++;
            UploadStream = content;
            OnUpload?.Invoke();
            if (FailUpload) return Task.FromResult(Result<TenantResourceFileReference>.Failure(new("probe.upload", "probe", ErrorType.Validation)));
            return Task.FromResult(Result<TenantResourceFileReference>.Success(new(Guid.NewGuid(), contentLength, "hash")));
        }

        public Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(
            string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<TenantResourceFileContent>.Failure(
                new Error("files.not_found", "not found", ErrorType.NotFound)));

        public Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(
            string ownerModuleKey, Guid resourceId, CancellationToken cancellationToken = default)
        {
            ListCount++; OnList?.Invoke();
            return Task.FromResult<IReadOnlyList<TenantResourceFileReadyItem>>(ReadyFiles);
        }

        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ExportTaskStore(ICurrentTenant tenant) : IQueryExecutor, ICommandExecutor
    {
        public Dictionary<Guid, ReportingExportTaskRecord> Tasks { get; } = [];
        public bool Granted { get; set; } = true;
        public Action? OnClaim { get; set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            SqlScopeGuard.Validate(statement, tenant);
            var values = Params(parameters);
            if (statement.Name == "reporting.resolve_granted_version" && Granted)
                return Task.FromResult((T?)(object)new ReportingDefinitionVersionRecord
                    { DefinitionId = (Guid)values["DefinitionId"]!, VersionNumber = 1 });
            if (statement.Name == ReportingTenantGrantSql.FindDefinition.Name)
                return Task.FromResult((T?)(object)new ReportingDefinitionRecord
                    { Id = (Guid)values["DefinitionId"]!, IsEnabled = true });
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
            if (statement.Name == ReportingExportTaskSql.InsertFor(DatabaseProvider.SqlServer).Name
                && parameters is ReportingExportTaskRecord created)
            {
                Tasks[created.Id] = created;
                return Task.FromResult(1);
            }
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
            OnClaim?.Invoke();
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
