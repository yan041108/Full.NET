using System.Security.Claims;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Reporting.Features.PublishedDefinitions;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Reporting;

/// <summary>忽略取消的跨模块读取不得转为业务失败或泄露流，授权后的租户仍须与原委托一致。</summary>
[TestClass]
public sealed class OutputTaskBoundaryTests
{
    [TestMethod]
    [DataRow("reporting-sqlserver", "entry")]
    [DataRow("reporting-mysql", "entry")]
    [DataRow("import", "entry")]
    [DataRow("reporting-sqlserver", "task")]
    [DataRow("reporting-mysql", "task")]
    [DataRow("import", "task")]
    [DataRow("reporting-sqlserver", "task-missing")]
    [DataRow("reporting-mysql", "task-missing")]
    [DataRow("import", "task-missing")]
    [DataRow("reporting-sqlserver", "identity")]
    [DataRow("reporting-mysql", "identity")]
    [DataRow("import", "identity")]
    [DataRow("reporting-sqlserver", "identity-missing")]
    [DataRow("reporting-mysql", "identity-missing")]
    [DataRow("import", "identity-missing")]
    [DataRow("reporting-sqlserver", "capability-missing")]
    [DataRow("reporting-mysql", "capability-missing")]
    [DataRow("import", "capability-missing")]
    [DataRow("reporting-sqlserver", "file-failed")]
    [DataRow("reporting-mysql", "file-failed")]
    [DataRow("import", "file-failed")]
    [DataRow("reporting-sqlserver", "file")]
    [DataRow("reporting-mysql", "file")]
    [DataRow("import", "file")]
    [DataRow("reporting-sqlserver", "final-identity-missing")]
    [DataRow("reporting-mysql", "final-identity-missing")]
    [DataRow("import", "final-identity-missing")]
    [DataRow("reporting-sqlserver", "version-missing")]
    [DataRow("reporting-mysql", "version-missing")]
    [DataRow("reporting-sqlserver", "final-version-missing")]
    [DataRow("reporting-mysql", "final-version-missing")]
    public async Task Download_prioritizes_cancellation_at_every_handoff_and_releases_owned_content(string module, string stage)
    {
        using var fixture = new Fixture(module, stage);
        if (stage == "entry") fixture.Cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Read());
        Assert.AreEqual(fixture.ContentHandedOff ? 1 : 0, fixture.Stream.DisposeCalls);
        Assert.AreEqual(stage is "file" or "file-failed" or "final-identity-missing" or "final-version-missing" ? 1 : 0, fixture.FileCalls);
        if (stage == "entry") Assert.AreEqual(0, fixture.Query.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("reporting-sqlserver", "scope-identity")]
    [DataRow("reporting-mysql", "scope-identity")]
    [DataRow("import", "scope-identity")]
    [DataRow("reporting-sqlserver", "scope-file")]
    [DataRow("reporting-mysql", "scope-file")]
    [DataRow("import", "scope-file")]
    public async Task Changed_scope_cannot_open_or_transfer_content_under_the_original_binding(string module, string stage)
    {
        using var fixture = new Fixture(module, stage);
        var result = await fixture.Read();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CommonErrorCodes.PermissionDenied, result.Error!.Code);
        Assert.AreEqual(stage == "scope-file" ? 1 : 0, fixture.FileCalls);
        Assert.AreEqual(stage == "scope-file" ? 1 : 0, fixture.Stream.DisposeCalls);
        Assert.AreNotEqual(fixture.Binding.TenantId, fixture.Tenant.Id);
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("reporting-sqlserver")]
    [DataRow("reporting-mysql")]
    [DataRow("import")]
    public async Task Allowed_download_transfers_stream_ownership_to_the_caller(string module)
    {
        using var fixture = new Fixture(module, "allowed");
        var result = await fixture.Read();
        Assert.IsTrue(result.IsSuccess);
        Assert.AreSame(fixture.Stream, result.Value!.Content);
        Assert.AreEqual(0, fixture.Stream.DisposeCalls);
        await result.Value.Content.DisposeAsync();
        Assert.AreEqual(1, fixture.Stream.DisposeCalls);
    }

    [TestMethod]
    [DataRow("reporting-sqlserver", "entry")]
    [DataRow("reporting-mysql", "entry")]
    [DataRow("reporting-sqlserver", "version-missing")]
    [DataRow("reporting-mysql", "version-missing")]
    [DataRow("reporting-sqlserver", "identity-missing")]
    [DataRow("reporting-mysql", "identity-missing")]
    [DataRow("reporting-sqlserver", "identity")]
    [DataRow("reporting-mysql", "identity")]
    [DataRow("reporting-sqlserver", "scope-identity")]
    [DataRow("reporting-mysql", "scope-identity")]
    public async Task Export_creation_does_not_write_after_cancellation_or_scope_change(string module, string stage)
    {
        using var fixture = new Fixture(module, stage);
        if (stage == "entry") fixture.Cancellation.Cancel();
        if (stage == "scope-identity")
        {
            var result = await fixture.Create(new(fixture.DefinitionId, "excel", null, []));
            Assert.AreEqual(CommonErrorCodes.PermissionDenied, result.Error!.Code);
        }
        else await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Create(new(fixture.DefinitionId, "excel", null, [])));
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.FileCalls);
    }

    [TestMethod]
    [DataRow("request")]
    [DataRow("parameters")]
    [DataRow("parameter")]
    public async Task Invalid_export_payload_is_rejected_before_queries_or_writes(string kind)
    {
        using var fixture = new Fixture("reporting-sqlserver", "allowed");
        CreateReportingExportTaskRequest? request = kind == "request" ? null
            : new(fixture.DefinitionId, "excel", null, kind == "parameters" ? null! : [null!]);
        var result = await fixture.Create(request!);
        Assert.AreEqual(ReportingErrorCodes.ExportFailed, result.Error!.Code);
        Assert.AreEqual(0, fixture.Query.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.FileCalls);
    }

    [TestMethod]
    [DataRow("execute", "task-missing")]
    [DataRow("resume", "task-missing")]
    [DataRow("retry", "task-missing")]
    [DataRow("execute", "identity-missing")]
    [DataRow("resume", "identity-missing")]
    [DataRow("retry", "identity-missing")]
    [DataRow("execute", "identity")]
    [DataRow("resume", "identity")]
    [DataRow("retry", "identity")]
    [DataRow("execute", "scope-identity")]
    [DataRow("resume", "scope-identity")]
    [DataRow("retry", "scope-identity")]
    public async Task Import_transition_stops_before_write_when_cancellation_or_scope_changes(string action, string stage)
    {
        using var fixture = new Fixture("import", stage, action switch
        {
            "execute" => ImportExportTaskStatusKeys.PreviewSucceeded,
            "resume" => ImportExportTaskStatusKeys.ExecutionPartial,
            _ => ImportExportTaskStatusKeys.ExecutionFailed,
        });
        if (stage == "scope-identity")
            Assert.AreEqual(CommonErrorCodes.PermissionDenied, (await fixture.Queue(action)).Error!.Code);
        else await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Queue(action));
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.FileCalls);
    }

    [TestMethod]
    [DataRow("execute", 0)]
    [DataRow("resume", 0)]
    [DataRow("retry", 0)]
    [DataRow("execute", 1)]
    [DataRow("resume", 1)]
    [DataRow("retry", 1)]
    public async Task Ignored_transition_cancellation_is_propagated_even_when_cas_returns_zero(string action, int affected)
    {
        using var fixture = new Fixture("import", "transition", action switch
        {
            "execute" => ImportExportTaskStatusKeys.PreviewSucceeded,
            "resume" => ImportExportTaskStatusKeys.ExecutionPartial,
            _ => ImportExportTaskStatusKeys.ExecutionFailed,
        }, affected);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Queue(action));
        Assert.AreEqual(1, fixture.Command.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.FileCalls);
        // 写端口可能已经提交；这里只证明取消传播，不把已发生的写入声称为回滚。
    }

    [TestMethod]
    [DataRow("reporting-sqlserver")]
    [DataRow("reporting-mysql")]
    public async Task Corrupt_published_snapshot_does_not_hide_cancellation_in_background_authorization(string module)
    {
        using var fixture = new Fixture(module, "version-corrupt");
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.CanRun());
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.FileCalls);
    }

    private sealed class Fixture : IDisposable
    {
        public IQueryExecutor Query { get; } = Substitute.For<IQueryExecutor>();
        public ICommandExecutor Command { get; } = Substitute.For<ICommandExecutor>();
        public CurrentTenantAccessor Tenant { get; } = new();
        public CancellationTokenSource Cancellation { get; } = new();
        public CountingStream Stream { get; } = new();
        public Guid DefinitionId { get; } = Guid.CreateVersion7();
        public SessionBindingSnapshot Binding { get; }
        public int FileCalls { get; private set; }
        public bool ContentHandedOff { get; private set; }
        private readonly Guid taskId = Guid.CreateVersion7();
        private readonly ReportingExportTaskManagementService reporting;
        private readonly ReportingExportAuthorization reportingAuthorization;
        private readonly ReportingExportTaskRecord reportingTask;
        private readonly ImportExportTaskExecutionService import;
        private readonly bool isImport;

        public Fixture(string module, string stage, string? importStatus = null, int affected = 1)
        {
            isImport = module == "import";
            var tenantId = Guid.CreateVersion7();
            Tenant.SetTenant(new(tenantId, "acme", "Acme"));
            Binding = new(Guid.CreateVersion7(), tenantId, Guid.CreateVersion7(), "stamp", "host", $"tenant:{tenantId:N}");
            var identity = Substitute.For<IBackgroundSessionAuthorization>();
            identity.AuthorizeAsync(Binding, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                if (stage == "scope-identity") Tenant.SetTenant(new(Guid.CreateVersion7(), "other", "Other"));
                if (stage == "capability-missing" && ((string)call[1]! is "protected.column" or "assign"))
                { Cancellation.Cancel(); return null; }
                if (stage is "identity" or "identity-missing" || stage == "final-identity-missing" && ContentHandedOff)
                    Cancellation.Cancel();
                if (stage == "identity-missing" || stage == "final-identity-missing" && ContentHandedOff) return null;
                // 报表同时模拟端口返回切换后的租户，不能只比较可变当前上下文。
                return new AuthorizedSessionActor(Binding.UserId, !isImport && stage == "scope-identity" ? Tenant.Id : tenantId, Binding.SessionId);
            });
            var files = Substitute.For<ITenantResourceFileStore>();
            files.OpenReadyContentAsync(Arg.Any<string>(), taskId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                FileCalls++;
                if (stage is "file" or "file-failed") Cancellation.Cancel();
                if (stage == "scope-file") Tenant.SetTenant(new(Guid.CreateVersion7(), "other", "Other"));
                if (stage == "file-failed") return Result<TenantResourceFileContent>.Failure(new("fixture.file_failure", "fixture", ErrorType.Validation));
                ContentHandedOff = true;
                return Result<TenantResourceFileContent>.Success(new(Stream, "application/test", "result.xlsx"));
            });
            var provider = module == "reporting-mysql" ? DatabaseProvider.MySql : DatabaseProvider.SqlServer;
            if (stage == "transition") Command.ExecuteAsync(ImportExportTaskSql.QueueExecution, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(_ => { Cancellation.Cancel(); return affected; });
            var database = Options.Create(new DatabaseOptions { Provider = provider });
            var definition = new ReportingDefinitionRecord { Id = DefinitionId, IsEnabled = true, DefinitionKey = "engine", Name = "Engine" };
            var version = new ReportingDefinitionVersionRecord { DefinitionId = DefinitionId, VersionNumber = 1, QueryPortKey = "reporting.database_engine_version" };
            if (stage == "capability-missing") version.LayoutConfigJson = "{\"columns\":[{\"key\":\"secret\",\"requiredPermission\":\"protected.column\"}]}";
            Query.QuerySingleOrDefaultAsync<ReportingDefinitionVersionRecord>(ReportingTenantGrantSql.ResolveVersion(provider), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (stage == "version-missing" || stage == "final-version-missing" && ContentHandedOff)
                { Cancellation.Cancel(); return null; }
                if (stage == "version-corrupt") { Cancellation.Cancel(); version.ParameterSchemaJson = "{"; }
                return version;
            });
            Query.QuerySingleOrDefaultAsync<ReportingDefinitionRecord>(ReportingTenantGrantSql.FindDefinition, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(definition);
            reportingTask = new ReportingExportTaskRecord { Id = taskId, TenantId = tenantId, RequestedByUserId = Binding.UserId,
                DefinitionId = DefinitionId, VersionNumber = 1, StatusKey = "succeeded", OutputFileId = Guid.CreateVersion7(),
                ActorPermissionCodesJson = ReportingExportTaskMapper.SerializeAuthorization(stage == "capability-missing" ? ["protected.column"] : [], Binding) };
            Query.QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(ReportingExportTaskSql.FindByIdFor(provider), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (stage is "task" or "task-missing") Cancellation.Cancel();
                return stage == "task-missing" ? null : reportingTask;
            });
            var id = Substitute.For<IIdGenerator>(); id.NewId().Returns(taskId);
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.Parse("2026-10-15T00:00:00Z"));
            var definitions = new ReportingPublishedDefinitionResolver(Query, new(Query, database), Tenant, database);
            reportingAuthorization = new(Tenant, identity, definitions);
            var permissions = Substitute.For<IIdentityPermissionEvaluator>();
            permissions.ResolvePermissions(Arg.Any<ClaimsPrincipal>()).Returns(Array.Empty<string>());
            reporting = new(definitions, files, Query, Command, null!, Tenant, clock, id, database,
                reportingAuthorization, permissions);
            var handler = Substitute.For<IStaticImportSchemaHandler>(); handler.SchemaKey.Returns("positions");
            handler.GetDefinition().Returns(new StaticImportSchemaDefinition("positions", "Positions", "tenant", "positions.import", []));
            handler.ExecutionCapabilityPermissions.Returns(stage == "capability-missing" ? ["assign"] : []);
            var importTask = new ImportExportTaskRecord { Id = taskId, TenantId = tenantId, RequestedByUserId = Binding.UserId,
                SchemaKey = "positions", SourceFileName = "positions.xlsx", ErrorReceiptFileId = Guid.CreateVersion7(), ExecutionFailedRowCount = 1,
                StatusKey = importStatus ?? ImportExportTaskStatusKeys.ExecutionFailed,
                ExecutionRowsJson = ImportExportTaskMapper.SerializeExecutionState(new(new Dictionary<string, bool> { ["assign"] = stage == "capability-missing" }, [])) };
            Query.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                if (stage is "task" or "task-missing") Cancellation.Cancel();
                return stage == "task-missing" ? null : importTask;
            });
            import = new(Query, Command, files, Tenant, new([handler]), new(identity),
                Substitute.For<IServiceScopeFactory>(), Substitute.For<IOptionsMonitor<ImportExportOptions>>());
        }

        public Task<Result<TenantResourceFileContent>> Read() => isImport
            ? import.OpenErrorReceiptAsync(taskId, Binding, Cancellation.Token) : reporting.OpenDownloadAsync(taskId, Binding, Cancellation.Token);
        public Task<Result<ReportingExportTaskDetailResponse>> Create(CreateReportingExportTaskRequest request) =>
            reporting.CreateAsync(request, Binding.UserId, new ClaimsPrincipal(), Binding, Cancellation.Token);
        public Task<bool> CanRun() => reportingAuthorization.CanRunAsync(reportingTask, Cancellation.Token);
        public Task<Result<ImportExportTaskDetailResponse>> Queue(string action)
        {
            var context = new StaticImportPreviewContext(Binding.UserId, new Dictionary<string, bool>());
            return action switch
            {
                "execute" => import.QueueExecuteAsync(taskId, context, Binding, Cancellation.Token),
                "resume" => import.ResumeAsync(taskId, context, Binding, Cancellation.Token),
                _ => import.RetryAsync(taskId, context, Binding, Cancellation.Token),
            };
        }
        public void Dispose() { Stream.Dispose(); Cancellation.Dispose(); }
    }

    private sealed class CountingStream : MemoryStream
    {
        public int DisposeCalls { get; private set; }
        protected override void Dispose(bool disposing) { if (disposing) DisposeCalls++; base.Dispose(disposing); }
    }
}
