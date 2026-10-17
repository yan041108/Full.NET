using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Reporting;

/// <summary>创建人取消与 Worker 领取的安全边界；取消不执行报表、不释放已有文件。</summary>
[TestClass]
public sealed class ReportingExportTaskCancellationTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Queued_cancel_uses_owner_and_version_then_returns_terminal(DatabaseProvider provider)
    {
        var f = new Fixture(provider: provider);
        var result = await f.Cancel();
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("cancelled", result.Value!.StatusKey);
        Assert.AreEqual(f.OutputFileId, result.Value.OutputFileId);
        Assert.AreEqual("retained.xlsx", result.Value.OutputFileName);
        var parameters = (Dictionary<string, object?>)f.Command.ReceivedCalls().Single().GetArguments()[1]!;
        Assert.AreEqual(f.Binding.UserId, parameters["RequestedByUserId"]);
        Assert.AreEqual(4L, parameters["ExpectedVersion"]);
        Assert.AreEqual(1, f.Identity.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("processing")]
    [DataRow("succeeded")]
    [DataRow("failed")]
    public async Task Inflight_and_completed_tasks_are_conflicts_without_write(string status)
    {
        var f = new Fixture(status);
        Assert.AreEqual(ErrorType.Conflict, (await f.Cancel()).Error!.Type);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("actor")]
    [DataRow("tenant")]
    [DataRow("emptySession")]
    [DataRow("emptyStamp")]
    [DataRow("permission")]
    [DataRow("returnedActor")]
    [DataRow("returnedSession")]
    [DataRow("returnedTenant")]
    [DataRow("scopeChanged")]
    public async Task Invalid_binding_or_current_authority_cannot_cancel(string reason)
    {
        var f = new Fixture();
        var binding = reason switch
        {
            "actor" => f.Binding with { UserId = Guid.NewGuid() },
            "tenant" => f.Binding with { TenantId = Guid.NewGuid() },
            "emptySession" => f.Binding with { SessionId = Guid.Empty },
            "emptyStamp" => f.Binding with { SecurityStamp = "" },
            _ => f.Binding,
        };
        if (reason == "permission") f.Identity.AuthorizeAsync(binding, ReportingExportTaskPermissions.Cancel, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AuthorizedSessionActor?>(null));
        if (reason.StartsWith("returned", StringComparison.Ordinal))
            f.Identity.AuthorizeAsync(binding, ReportingExportTaskPermissions.Cancel, Arg.Any<CancellationToken>()).Returns(new AuthorizedSessionActor(
                reason == "returnedActor" ? Guid.NewGuid() : binding.UserId,
                reason == "returnedTenant" ? Guid.NewGuid() : binding.TenantId,
                reason == "returnedSession" ? Guid.NewGuid() : binding.SessionId));
        if (reason == "scopeChanged") f.Identity.AuthorizeAsync(binding, ReportingExportTaskPermissions.Cancel, Arg.Any<CancellationToken>())
            .Returns(_ => { f.Tenant.SetTenant(new TenantContext(Guid.NewGuid(), "other", "Other")); return Task.FromResult<AuthorizedSessionActor?>(new(binding.UserId, binding.TenantId, binding.SessionId)); });
        Assert.AreEqual(CommonErrorCodes.PermissionDenied, (await f.Cancel(binding)).Error!.Code);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Already_cancelled_is_idempotent_and_requires_current_cancel_permission()
    {
        var f = new Fixture("cancelled");
        Assert.IsTrue((await f.Cancel()).IsSuccess);
        f.Identity.AuthorizeAsync(f.Binding, ReportingExportTaskPermissions.Cancel, Arg.Any<CancellationToken>()).Returns(Task.FromResult<AuthorizedSessionActor?>(null));
        Assert.AreEqual(CommonErrorCodes.PermissionDenied, (await f.Cancel()).Error!.Code);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("processing", false)]
    [DataRow("queued", false)]
    [DataRow("cancelled", true)]
    public async Task Cas_loss_accepts_only_same_owner_cancelled_terminal(string final, bool success)
    {
        var f = new Fixture(final: final, affected: 0);
        Assert.AreEqual(success, (await f.Cancel()).IsSuccess);
        Assert.AreEqual(1, f.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Missing_task_has_no_write_or_authorization_side_effect()
    {
        var f = new Fixture();
        f.Query.QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(ReportingExportTaskSql.FindByIdSqlServer, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ReportingExportTaskRecord?>(null));
        Assert.AreEqual(ErrorType.NotFound, (await f.Cancel()).Error!.Type);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Identity.ReceivedCalls().Count());
    }

    private sealed class Fixture
    {
        internal IQueryExecutor Query { get; } = Substitute.For<IQueryExecutor>();
        internal ICommandExecutor Command { get; } = Substitute.For<ICommandExecutor>();
        internal IBackgroundSessionAuthorization Identity { get; } = Substitute.For<IBackgroundSessionAuthorization>();
        internal CurrentTenantAccessor Tenant { get; } = new();
        internal SessionBindingSnapshot Binding { get; }
        internal Guid OutputFileId { get; } = Guid.NewGuid();
        private readonly IClock clock = Substitute.For<IClock>();
        private readonly Guid taskId = Guid.NewGuid();
        private readonly IOptions<DatabaseOptions> database;

        internal Fixture(string status = "queued", string final = "cancelled", int affected = 1, DatabaseProvider provider = DatabaseProvider.SqlServer)
        {
            var tenantId = Guid.NewGuid(); var userId = Guid.NewGuid();
            Tenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
            Binding = new(userId, tenantId, Guid.NewGuid(), "stamp", "host", $"tenant:{tenantId:N}");
            database = Options.Create(new DatabaseOptions { Provider = provider });
            clock.UtcNow.Returns(DateTimeOffset.Parse("2026-10-14T00:00:00Z"));
            var attempted = false;
            Query.QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(ReportingExportTaskSql.FindByIdFor(provider), Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<ReportingExportTaskRecord?>(new()
                {
                    Id = taskId, TenantId = tenantId, RequestedByUserId = userId, StatusKey = attempted ? final : status,
                    Version = attempted ? 5 : 4, ParametersJson = "[]", DefinitionKey = "report", DefinitionName = "Report",
                    FormatKey = "excel", OutputFileId = OutputFileId, OutputFileName = "retained.xlsx",
                }));
            Command.ExecuteAsync(ReportingExportTaskSql.Cancel, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(_ => { attempted = true; return Task.FromResult(affected); });
            Identity.AuthorizeAsync(Binding, ReportingExportTaskPermissions.Cancel, Arg.Any<CancellationToken>())
                .Returns(new AuthorizedSessionActor(userId, tenantId, Binding.SessionId));
        }

        internal Task<Result<ReportingExportTaskDetailResponse>> Cancel(SessionBindingSnapshot? binding = null) =>
            new ReportingExportTaskCancellationService(Query, Command, Tenant, Identity, clock, database).CancelAsync(taskId, binding ?? Binding);
    }
}
