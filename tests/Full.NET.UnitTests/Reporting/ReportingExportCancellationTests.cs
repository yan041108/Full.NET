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

/// <summary>报表取消只竞争本人排队版本，不能扩大到在途生成或其他主体。</summary>
[TestClass]
public sealed class ReportingExportCancellationTests
{
    [TestMethod]
    public async Task Owner_can_stop_queue_and_repeat_without_rechecking_source_or_advancing_again()
    {
        var fixture = new Fixture(); var result = await fixture.Service.CancelAsync(fixture.Id, 5, default);
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(ReportingExportTaskStatusKeys.Canceled, result.Value!.StatusKey); Assert.AreEqual(6, result.Value.Version);
        Assert.AreEqual("20", result.Value.Parameters.Single().Value); Assert.AreEqual(7, result.Value.VersionNumber);
        Assert.AreEqual(6, (await fixture.Service.CancelAsync(fixture.Id, 5, default)).Value!.Version);
        Assert.AreEqual(1, fixture.Commands.ReceivedCalls().Count());
        await fixture.Sessions.Received(2).AuthorizeAsync(ReportingExportTaskPermissions.Cancel, default);
    }

    [TestMethod]
    [DataRow(ReportingExportTaskStatusKeys.Processing)]
    [DataRow(ReportingExportTaskStatusKeys.Succeeded)]
    [DataRow(ReportingExportTaskStatusKeys.Failed)]
    public async Task Processing_and_completed_outcomes_are_not_overwritten(string state)
    {
        var fixture = new Fixture { Status = state };
        Assert.AreEqual(ReportingExportCancellationErrorCodes.Conflict, (await fixture.Service.CancelAsync(fixture.Id, 5, default)).Error!.Code);
        Assert.IsFalse(fixture.Commands.ReceivedCalls().Any());
    }

    [TestMethod]
    public async Task Version_conflict_and_lost_claim_do_not_read_private_parameters()
    {
        var fixture = new Fixture();
        Assert.AreEqual(ErrorType.Conflict, (await fixture.Service.CancelAsync(fixture.Id, 4, default)).Error!.Type);
        Assert.IsFalse(fixture.Commands.ReceivedCalls().Any()); fixture.CommandResult = 0;
        Assert.AreEqual(ErrorType.Conflict, (await fixture.Service.CancelAsync(fixture.Id, 5, default)).Error!.Type);
        await fixture.Queries.DidNotReceive().QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Missing_owner_or_revoked_session_cannot_mutate()
    {
        var fixture = new Fixture { Visible = false };
        Assert.AreEqual(ErrorType.NotFound, (await fixture.Service.CancelAsync(fixture.Id, 5, default)).Error!.Type);
        fixture.Queries.ClearReceivedCalls();
        fixture.Sessions.AuthorizeAsync(ReportingExportTaskPermissions.Cancel, Arg.Any<CancellationToken>()).Returns((AuthorizedSessionActor?)null);
        Assert.AreEqual(ErrorType.Forbidden, (await fixture.Service.CancelAsync(fixture.Id, 5, default)).Error!.Type);
        Assert.IsFalse(fixture.Commands.ReceivedCalls().Any()); Assert.IsFalse(fixture.Queries.ReceivedCalls().Any());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task Invalid_versions_fail_before_authority_or_storage(int version)
    {
        var fixture = new Fixture(); Assert.AreEqual(ErrorType.Validation, (await fixture.Service.CancelAsync(fixture.Id, version, default)).Error!.Type);
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
        var fixture = new Fixture(); using var cancellation = new CancellationTokenSource();
        if (stage == "entry") cancellation.Cancel();
        if (stage == "authorize") fixture.Sessions.When(value => value.AuthorizeAsync(ReportingExportTaskPermissions.Cancel, Arg.Any<CancellationToken>())).Do(_ => cancellation.Cancel());
        if (stage == "lookup") fixture.Queries.When(value => value.QuerySingleOrDefaultAsync<ReportingTaskCancellationState>(ReportingExportTaskSql.FindOwnedCancellationState, Arg.Any<object?>(), Arg.Any<CancellationToken>())).Do(_ => cancellation.Cancel());
        if (stage == "write") fixture.Commands.When(value => value.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())).Do(_ => cancellation.Cancel());
        if (stage == "reload") fixture.Queries.When(value => value.QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())).Do(_ => cancellation.Cancel());
        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.CancelAsync(fixture.Id, 5, cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        if (stage is "entry" or "authorize" or "lookup") Assert.IsFalse(fixture.Commands.ReceivedCalls().Any());
        if (stage == "write") await fixture.Queries.DidNotReceive().QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    private sealed class Fixture
    {
        internal Guid Id { get; } = Guid.CreateVersion7();
        private Guid UserId { get; } = Guid.CreateVersion7();
        internal string Status { get; set; } = ReportingExportTaskStatusKeys.Queued;
        private long Version { get; set; } = 5;
        internal bool Visible { get; set; } = true;
        internal int CommandResult { get; set; } = 1;
        internal IQueryExecutor Queries { get; } = Substitute.For<IQueryExecutor>();
        internal ICommandExecutor Commands { get; } = Substitute.For<ICommandExecutor>();
        internal ICurrentSessionAuthorization Sessions { get; } = Substitute.For<ICurrentSessionAuthorization>();
        internal ReportingExportTaskCancellationService Service { get; }
        internal Fixture()
        {
            var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new(Guid.CreateVersion7(), "test", "Test"));
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            Sessions.AuthorizeAsync(ReportingExportTaskPermissions.Cancel, Arg.Any<CancellationToken>()).Returns(new AuthorizedSessionActor(UserId, tenant.Id, Guid.CreateVersion7()));
            Queries.QuerySingleOrDefaultAsync<ReportingTaskCancellationState>(ReportingExportTaskSql.FindOwnedCancellationState, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(call => { Assert.AreEqual(UserId, ((Dictionary<string, object?>)call[1]!)["UserId"]); return Visible ? new ReportingTaskCancellationState { Id = Id, StatusKey = Status, Version = Version } : null; });
            Queries.QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(ReportingExportTaskSql.FindByIdFor(DatabaseProvider.SqlServer), Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => new ReportingExportTaskRecord { Id = Id, TenantId = tenant.Id!.Value, RequestedByUserId = UserId, StatusKey = Status, Version = Version,
                    VersionNumber = 7, ParametersJson = "[{\"parameterKey\":\"topN\",\"value\":\"20\"}]" });
            Commands.ExecuteAsync(ReportingExportTaskSql.CancelOwnedQueued, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => { if (CommandResult == 1) { Status = ReportingExportTaskStatusKeys.Canceled; Version++; } return CommandResult; });
            Service = new(Queries, Commands, tenant, Sessions, clock, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        }
    }
}
