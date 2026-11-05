using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>取消只推进本人未领取工作，不能凭旧版本覆盖执行中状态或重置任务结果。</summary>
[TestClass]
public sealed class ImportTaskCancellationTests
{
    [TestMethod]
    [DataRow(ImportExportTaskStatusKeys.PreviewSucceeded)]
    [DataRow(ImportExportTaskStatusKeys.Queued)]
    [DataRow(ImportExportTaskStatusKeys.ExecutionPartial)]
    public async Task Pending_owner_task_can_stop_without_source_write_permission(string status)
    {
        var fixture = new Fixture(status); var result = await fixture.Service.CancelAsync(fixture.Id, 5, default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(ImportExportTaskStatusKeys.Canceled, result.Value!.StatusKey);
        Assert.AreEqual(6, result.Value.Version); Assert.AreEqual(4, result.Value.ProcessedRowCount); Assert.AreEqual(3, result.Value.SucceededRowCount);
        Assert.IsTrue(result.Value.HasErrorReceipt);
        await fixture.Sessions.Received(1).AuthorizeAsync(ImportExportPermissions.ImportTasksCancel, default);
        Assert.AreEqual(1, fixture.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(ImportExportTaskStatusKeys.Executing)]
    [DataRow(ImportExportTaskStatusKeys.ExecutionSucceeded)]
    [DataRow(ImportExportTaskStatusKeys.ExecutionFailed)]
    [DataRow(ImportExportTaskStatusKeys.PreviewFailed)]
    [DataRow(ImportExportTaskStatusKeys.Uploaded)]
    public async Task Executing_or_other_terminal_states_cannot_be_overwritten(string status)
    {
        var fixture = new Fixture(status); var result = await fixture.Service.CancelAsync(fixture.Id, 5, default);
        Assert.AreEqual(ImportTaskCancellationErrorCodes.Conflict, result.Error!.Code);
        Assert.AreEqual(status, fixture.Status); Assert.IsFalse(fixture.Commands.ReceivedCalls().Any());
    }

    [TestMethod]
    public async Task Stale_version_and_lost_claim_race_do_not_return_a_successful_detail()
    {
        var fixture = new Fixture(ImportExportTaskStatusKeys.Queued);
        Assert.AreEqual(ErrorType.Conflict, (await fixture.Service.CancelAsync(fixture.Id, 4, default)).Error!.Type);
        Assert.IsFalse(fixture.Commands.ReceivedCalls().Any()); fixture.CommandResult = 0;
        Assert.AreEqual(ErrorType.Conflict, (await fixture.Service.CancelAsync(fixture.Id, 5, default)).Error!.Type);
        await fixture.Queries.DidNotReceive().QuerySingleOrDefaultAsync<ImportExportTaskRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Already_canceled_owner_intent_is_idempotent_even_with_pre_cancel_version()
    {
        var fixture = new Fixture(ImportExportTaskStatusKeys.PreviewSucceeded);
        var first = await fixture.Service.CancelAsync(fixture.Id, 5, default); var again = await fixture.Service.CancelAsync(fixture.Id, 5, default);
        Assert.IsTrue(again.IsSuccess); Assert.AreEqual(first.Value!.Version, again.Value!.Version);
        Assert.AreEqual(1, fixture.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Revoked_or_other_tenant_session_is_rejected_before_task_lookup()
    {
        var fixture = new Fixture(ImportExportTaskStatusKeys.Queued);
        fixture.Sessions.AuthorizeAsync(ImportExportPermissions.ImportTasksCancel, Arg.Any<CancellationToken>()).Returns((AuthorizedSessionActor?)null);
        Assert.AreEqual(ErrorType.Forbidden, (await fixture.Service.CancelAsync(fixture.Id, 5, default)).Error!.Type);
        fixture.Sessions.AuthorizeAsync(ImportExportPermissions.ImportTasksCancel, Arg.Any<CancellationToken>())
            .Returns(new AuthorizedSessionActor(fixture.UserId, Guid.CreateVersion7(), Guid.CreateVersion7()));
        Assert.AreEqual(ErrorType.Forbidden, (await fixture.Service.CancelAsync(fixture.Id, 5, default)).Error!.Type);
        Assert.IsFalse(fixture.Queries.ReceivedCalls().Any()); Assert.IsFalse(fixture.Commands.ReceivedCalls().Any());
    }

    [TestMethod]
    public async Task Missing_owner_projection_never_reads_private_task_data()
    {
        var fixture = new Fixture(ImportExportTaskStatusKeys.Queued) { Visible = false };
        Assert.AreEqual(ErrorType.NotFound, (await fixture.Service.CancelAsync(fixture.Id, 5, default)).Error!.Type);
        await fixture.Queries.DidNotReceive().QuerySingleOrDefaultAsync<ImportExportTaskRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.IsFalse(fixture.Commands.ReceivedCalls().Any());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task Invalid_versions_never_reach_authority_or_storage(int version)
    {
        var fixture = new Fixture(ImportExportTaskStatusKeys.Queued);
        Assert.AreEqual(ErrorType.Validation, (await fixture.Service.CancelAsync(fixture.Id, version, default)).Error!.Type);
        Assert.IsFalse(fixture.Sessions.ReceivedCalls().Any()); Assert.IsFalse(fixture.Queries.ReceivedCalls().Any());
    }

    /// <summary>提供程序忽略取消时，调用边界仍停止后续读写；提交后的取消也不能伪装为成功交付。</summary>
    [TestMethod]
    [DataRow("entry")]
    [DataRow("authorize")]
    [DataRow("lookup")]
    [DataRow("write")]
    [DataRow("reload")]
    public async Task Cancellation_at_each_await_boundary_stops_result_delivery(string stage)
    {
        var fixture = new Fixture(ImportExportTaskStatusKeys.Queued); using var cancellation = new CancellationTokenSource();
        if (stage == "entry") cancellation.Cancel();
        if (stage == "authorize") fixture.Sessions.When(value => value.AuthorizeAsync(ImportExportPermissions.ImportTasksCancel, Arg.Any<CancellationToken>())).Do(_ => cancellation.Cancel());
        if (stage == "lookup") fixture.Queries.When(value => value.QuerySingleOrDefaultAsync<ImportTaskCancellationState>(ImportExportTaskSql.FindOwnedCancellationState, Arg.Any<object?>(), Arg.Any<CancellationToken>())).Do(_ => cancellation.Cancel());
        if (stage == "write") fixture.Commands.When(value => value.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())).Do(_ => cancellation.Cancel());
        if (stage == "reload") fixture.Queries.When(value => value.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())).Do(_ => cancellation.Cancel());
        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.CancelAsync(fixture.Id, 5, cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        if (stage is "entry" or "authorize" or "lookup") Assert.IsFalse(fixture.Commands.ReceivedCalls().Any());
        if (stage == "write") await fixture.Queries.DidNotReceive().QuerySingleOrDefaultAsync<ImportExportTaskRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        internal Guid Id { get; } = Guid.CreateVersion7();
        internal Guid UserId { get; } = Guid.CreateVersion7();
        internal IQueryExecutor Queries { get; } = Substitute.For<IQueryExecutor>();
        internal ICommandExecutor Commands { get; } = Substitute.For<ICommandExecutor>();
        internal ICurrentSessionAuthorization Sessions { get; } = Substitute.For<ICurrentSessionAuthorization>();
        internal string Status { get; private set; }
        internal long Version { get; private set; } = 5;
        internal bool Visible { get; set; } = true;
        internal int CommandResult { get; set; } = 1;
        internal ImportExportTaskCancellationService Service { get; }
        internal Fixture(string status)
        {
            Status = status; var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new(Guid.CreateVersion7(), "test", "Test"));
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            Sessions.AuthorizeAsync(ImportExportPermissions.ImportTasksCancel, Arg.Any<CancellationToken>()).Returns(new AuthorizedSessionActor(UserId, tenant.Id, Guid.CreateVersion7()));
            Queries.QuerySingleOrDefaultAsync<ImportTaskCancellationState>(ImportExportTaskSql.FindOwnedCancellationState, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(call => { var parameters = (Dictionary<string, object?>)call[1]!; Assert.AreEqual(UserId, parameters["UserId"]); return Visible ? new ImportTaskCancellationState { Id = Id, StatusKey = Status, Version = Version } : null; });
            Queries.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => new ImportExportTaskRecord {
                SourceExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7), Id = Id, TenantId = tenant.Id!.Value, RequestedByUserId = UserId, StatusKey = Status,
                    Version = Version, ProcessedRowCount = 4, SucceededRowCount = 3, ExecutionFailedRowCount = 1, ErrorReceiptFileId = Guid.CreateVersion7() });
            Commands.ExecuteAsync(ImportExportTaskSql.CancelOwnedPending, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => { if (CommandResult == 1) { Status = ImportExportTaskStatusKeys.Canceled; Version++; } return CommandResult; });
            Service = new(Queries, Commands, tenant, Sessions, clock);
        }
    }
}
