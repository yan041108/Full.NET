using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.WorkflowOutcomes;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

[TestClass]
public sealed class EnterpriseRequestWorkflowOutcomeServiceTests
{
    [TestMethod]
    public async Task Ignores_non_enterprise_business_type()
    {
        var f = new Fixture();
        await f.Deliver("other.business");
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Approved_workflow_updates_submitted_request()
    {
        var f = new Fixture();
        await f.Deliver();
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestWorkflowSql.ApplyTerminalStatus,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestApprovalSql.MarkFinal,
            Arg.Is<object?>(p => ((Dictionary<string, object?>)p!)["LastMessageId"]!.Equals(f.Message)), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Coordinator.CommitCount);
        f.Tenant.Received(1).SetHost();
    }

    [TestMethod]
    public async Task Does_not_update_when_request_is_not_submitted()
    {
        var f = new Fixture(); f.Row = f.Row with { Status = "Draft" };
        await f.Deliver();
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("instance")]
    [DataRow("tenant")]
    [DataRow("version")]
    [DataRow("identity")]
    [DataRow("deleted")]
    [DataRow("missing")]
    public async Task Foreign_or_stale_outcome_has_no_side_effects(string kind)
    {
        var f = new Fixture();
        if (kind == "instance") f.Submission = f.Submission! with { WorkflowInstanceId = Guid.NewGuid() };
        if (kind == "tenant") f.Submission = f.Submission! with { TenantId = Guid.NewGuid() };
        if (kind == "version") f.Row = f.Row with { Version = f.Row.Version + 1 };
        if (kind == "identity") f.Row = f.Row with { Id = Guid.NewGuid() };
        if (kind == "deleted") f.Row = f.Row with { IsDeleted = true };
        if (kind == "missing") f.Submission = null;
        if (kind is "missing" or "version") await Assert.ThrowsAsync<InvalidOperationException>(() => f.Deliver());
        else await f.Deliver();
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Coordinator.CommitCount);
    }

    [TestMethod]
    [DataRow("Approved")]
    [DataRow("Rejected")]
    public async Task Sealed_submission_ignores_duplicate_or_conflicting_outcome(string sealedStatus)
    {
        var f = new Fixture(); f.Submission = f.Submission! with { FinalStatus = sealedStatus, LastMessageId = f.Message };
        await f.Deliver();
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Receipt_conflict_rolls_back_status_write()
    {
        var f = new Fixture();
        f.Commands.ExecuteAsync(EnterpriseRequestApprovalSql.MarkFinal, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Deliver());
        Assert.AreEqual(1, f.Coordinator.RollbackCount); Assert.AreEqual(0, f.Coordinator.CommitCount);
        f.Tenant.Received(1).SetHost();
    }

    private sealed class Fixture
    {
        internal readonly Guid Instance = Guid.CreateVersion7(), Message = Guid.CreateVersion7();
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly ICurrentTenantContextWriter Tenant = Substitute.For<ICurrentTenantContextWriter>();
        internal readonly RecordingDbTransactionCoordinator Coordinator = new();
        internal EnterpriseRequestRecord Row;
        internal EnterpriseRequestApprovalSubmission? Submission;
        internal readonly EnterpriseRequestWorkflowOutcomeService Service;
        internal Fixture()
        {
            Row = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "REQ", "Title", "Submitted", 1m,
                Guid.CreateVersion7(), 2, DateTimeOffset.UtcNow, Guid.CreateVersion7(), null, null, false, null, null);
            Submission = new(Guid.CreateVersion7(), Row.TenantId, Row.Id, Row.Version, Guid.CreateVersion7(), Instance,
                Row.ApplicantUserId, Row.OrganizationUnitId, Row.Title, DateTimeOffset.UtcNow, null, null, null, null);
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(EnterpriseRequestSql.FindByIdStatement, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Row);
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(EnterpriseRequestApprovalSql.FindByRequest, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Submission);
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Tenant.IsHost.Returns(true);
            Service = new(Queries, Commands, Substitute.For<IClock>(), new DapperCommandTransaction(Coordinator), Tenant);
        }
        internal Task Deliver(string type = EnterpriseRequestWorkflowConstants.BusinessType) =>
            Service.HandleTerminalWorkflowAsync(type, Row.Id.ToString("D"), "Approved", Instance, Row.TenantId, Message);
    }
}
