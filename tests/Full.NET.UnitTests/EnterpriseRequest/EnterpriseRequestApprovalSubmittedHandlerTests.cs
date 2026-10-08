using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.SubmitForApproval;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Serialization.MemoryPack;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

[TestClass]
public sealed class EnterpriseRequestApprovalSubmittedHandlerTests
{
    [TestMethod]
    public async Task Starts_fixed_identity_and_records_receipt_after_workflow_commit()
    {
        var f = new Fixture();
        await f.Deliver();
        await f.Starter.Received(1).StartAsync(f.Submission.SubmittedById,
            Arg.Is<StartWorkflowInstanceCommand>(v => v != null && v.RequestedInstanceId == f.Submission.WorkflowInstanceId &&
                v.DefinitionVersionId == f.Submission.WorkflowDefinitionVersionId && v.BusinessTitle == f.Submission.BusinessTitle), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestApprovalSql.MarkStarted, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        f.Tenant.Received(1).SetHost();
    }

    [TestMethod]
    public async Task Recorded_start_skips_cross_module_writes()
    {
        var f = new Fixture(); f.Submission = f.Submission with { StartedAtUtc = DateTimeOffset.UtcNow };
        await f.Deliver();
        Assert.AreEqual(0, f.Starter.ReceivedCalls().Count()); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Lost_receipt_retries_same_pinned_instance_even_after_terminal_outcome()
    {
        var f = new Fixture();
        f.Commands.ExecuteAsync(EnterpriseRequestApprovalSql.MarkStarted, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0, 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Deliver());
        f.Row = f.Row with { Status = "Approved", Version = 3 };
        f.Submission = f.Submission with { FinalStatus = "Approved" };
        await f.Deliver();
        await f.Starter.Received(2).StartAsync(f.Submission.SubmittedById,
            Arg.Is<StartWorkflowInstanceCommand>(v => v != null && v.RequestedInstanceId == f.Submission.WorkflowInstanceId), Arg.Any<CancellationToken>());
        f.Tenant.Received(2).SetHost();
    }

    [TestMethod]
    [DataRow("transaction")]
    [DataRow("inactive")]
    [DataRow("tenant")]
    [DataRow("submission")]
    [DataRow("version")]
    [DataRow("denied")]
    public async Task Invalid_delivery_does_not_start_workflow(string kind)
    {
        var f = new Fixture();
        if (kind == "transaction") f.State.HasTransaction.Returns(true);
        if (kind == "inactive") f.Resolver.ResolveActiveByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((TenantContext?)null);
        if (kind == "tenant") f.Submission = f.Submission with { TenantId = Guid.NewGuid() };
        if (kind == "submission") f.Submission = f.Submission with { Id = Guid.NewGuid() };
        if (kind == "version") f.Row = f.Row with { Version = 4 };
        if (kind == "denied") f.Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Deliver());
        Assert.AreEqual(0, f.Starter.ReceivedCalls().Count()); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Wrong_workflow_identity_cannot_be_recorded_as_started()
    {
        var f = new Fixture();
        f.Starter.StartAsync(Arg.Any<Guid>(), Arg.Any<StartWorkflowInstanceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<WorkflowInstanceLifecycleResult>.Success(new(Guid.NewGuid(), "active", 1)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Deliver());
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count()); f.Tenant.Received(1).SetHost();
    }

    private sealed class Fixture
    {
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly IDataTransactionState State = Substitute.For<IDataTransactionState>();
        internal readonly ICurrentTenantContextWriter Tenant = Substitute.For<ICurrentTenantContextWriter>();
        internal readonly IActiveTenantContextResolver Resolver = Substitute.For<IActiveTenantContextResolver>();
        internal readonly IOrganizationOwnedEntityWriteAuthorizer Authorizer = Substitute.For<IOrganizationOwnedEntityWriteAuthorizer>();
        internal readonly IWorkflowInstanceStarter Starter = Substitute.For<IWorkflowInstanceStarter>();
        internal readonly MemoryPackIntegrationEventSerializer Serializer = new();
        internal EnterpriseRequestRecord Row;
        internal EnterpriseRequestApprovalSubmission Submission;
        internal readonly IntegrationEventContext Context;
        internal readonly byte[] Payload;
        internal readonly EnterpriseRequestApprovalSubmittedHandler Handler;
        internal Fixture()
        {
            Row = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "REQ", "Title", "Submitted", 1m,
                Guid.CreateVersion7(), 2, DateTimeOffset.UtcNow, Guid.CreateVersion7(), null, null, false, null, null);
            Submission = new(Guid.CreateVersion7(), Row.TenantId, Row.Id, 2, Guid.CreateVersion7(), Guid.CreateVersion7(),
                Row.ApplicantUserId, Row.OrganizationUnitId, Row.Title, DateTimeOffset.UtcNow, null, null, null, null);
            Context = new(Guid.CreateVersion7(), EnterpriseRequestApprovalSubmittedIntegrationEvent.EventType, 1, Row.TenantId, null, DateTimeOffset.UtcNow);
            Payload = Serializer.Serialize(new EnterpriseRequestApprovalSubmittedIntegrationEvent(Submission.Id, Row.Id));
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(EnterpriseRequestSql.FindByIdStatement, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Row);
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(EnterpriseRequestApprovalSql.FindByRequest, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Submission);
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Resolver.ResolveActiveByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(new TenantContext(Row.TenantId, "demo", "Demo"));
            Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
            Starter.StartAsync(Arg.Any<Guid>(), Arg.Any<StartWorkflowInstanceCommand>(), Arg.Any<CancellationToken>()).Returns(_ => Result<WorkflowInstanceLifecycleResult>.Success(new(Submission.WorkflowInstanceId, "active", 1)));
            Tenant.IsHost.Returns(true);
            Handler = new(Serializer, Queries, Commands, State, Tenant, Resolver, Authorizer, Starter, Substitute.For<IClock>());
        }
        internal Task Deliver() => Handler.HandleAsync(Context, Payload, CancellationToken.None);
    }
}
