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

    [TestMethod]
    public async Task Pre_cancelled_delivery_does_not_resolve_start_or_record_receipt()
    {
        var f = new Fixture(); using var stop = new CancellationTokenSource(); stop.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => f.Deliver(stop.Token));
        Assert.AreEqual(stop.Token, error.CancellationToken);
        Assert.AreEqual(0, f.Resolver.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Starter.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Tenant.ReceivedCalls().Count(call => call.GetMethodInfo().Name is "SetTenant" or "SetHost" or "Clear"));
    }

    /// <summary>各失败点保持消息可重试；重放不得更换原实例、提交身份或业务版本。</summary>
    [TestMethod]
    [DataRow("resolver", false)] [DataRow("resolver", true)]
    [DataRow("submission", false)] [DataRow("submission", true)]
    [DataRow("request", false)] [DataRow("request", true)]
    [DataRow("authorization", false)] [DataRow("authorization", true)]
    [DataRow("starter", false)] [DataRow("starter", true)]
    [DataRow("receipt", false)] [DataRow("receipt", true)]
    [DataRow("receipt-read", false)] [DataRow("receipt-read", true)]
    public async Task Failure_or_cancellation_retries_original_pinned_start(string stage, bool cancelled)
    {
        var f = new Fixture(); using var stop = new CancellationTokenSource(); var token = stop.Token;
        Exception fault = cancelled ? new OperationCanceledException("test.start_cancelled", token)
            : new InvalidOperationException("test.start_fault");
        var armed = true;
        Task<T> Next<T>(T value)
        {
            if (!armed) return Task.FromResult(value);
            armed = false;
            if (cancelled) stop.Cancel();
            return Task.FromException<T>(fault);
        }
        if (stage == "resolver") f.Resolver.ResolveActiveByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Next<TenantContext?>(new(f.Row.TenantId, "demo", "Demo")));
        if (stage == "submission") f.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(
            EnterpriseRequestApprovalSql.FindByRequest, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Next<EnterpriseRequestApprovalSubmission?>(f.Submission));
        if (stage == "request") f.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
            EnterpriseRequestSql.FindByIdStatement, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Next<EnterpriseRequestRecord?>(f.Row));
        if (stage == "authorization") f.Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Next(Result<bool>.Success(true)));
        if (stage == "starter") f.Starter.StartAsync(Arg.Any<Guid>(), Arg.Any<StartWorkflowInstanceCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => Next(Result<WorkflowInstanceLifecycleResult>.Success(new(f.Submission.WorkflowInstanceId, "active", 1))));
        if (stage == "receipt") f.Commands.ExecuteAsync(EnterpriseRequestApprovalSql.MarkStarted, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Next(1));
        if (stage == "receipt-read")
        {
            f.Commands.ExecuteAsync(EnterpriseRequestApprovalSql.MarkStarted, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0, 1);
            var reads = 0;
            f.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(
                EnterpriseRequestApprovalSql.FindByRequest, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(_ => ++reads == 2 ? Next<EnterpriseRequestApprovalSubmission?>(f.Submission)
                    : Task.FromResult<EnterpriseRequestApprovalSubmission?>(f.Submission));
        }
        if (cancelled)
        {
            var observed = await Assert.ThrowsAsync<OperationCanceledException>(() => f.Deliver(token));
            Assert.AreSame(fault, observed); Assert.AreEqual(token, observed.CancellationToken);
        }
        else Assert.AreSame(fault, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Deliver(token)));
        Assert.IsFalse(armed); Assert.IsNull(f.Submission.StartedAtUtc);
        Assert.AreEqual("Submitted", f.Row.Status); Assert.AreEqual(2L, f.Row.Version);
        var reachedStart = stage is "starter" or "receipt" or "receipt-read";
        var reachedReceipt = stage is "receipt" or "receipt-read";
        Assert.AreEqual(reachedStart ? 1 : 0, f.Starter.ReceivedCalls().Count());
        Assert.AreEqual(reachedReceipt ? 1 : 0, f.Commands.ReceivedCalls().Count());
        f.Tenant.Received(stage == "resolver" ? 0 : 1).SetHost();

        // 使用同一可靠消息和未取消的新投递令牌重放，不能把首次异常折算为成功回执。
        await f.Deliver();
        await f.Starter.Received(reachedStart ? 2 : 1).StartAsync(f.Submission.SubmittedById,
            Arg.Is<StartWorkflowInstanceCommand>(v => v != null && v.RequestedInstanceId == f.Submission.WorkflowInstanceId &&
                v.DefinitionVersionId == f.Submission.WorkflowDefinitionVersionId &&
                v.BusinessType == EnterpriseRequestWorkflowConstants.BusinessType && v.BusinessId == f.Row.Id.ToString("D") &&
                v.IdempotencyKey == $"submit:{f.Row.Id:D}:1" && v.BusinessTitle == f.Submission.BusinessTitle), Arg.Any<CancellationToken>());
        await f.Commands.Received(reachedReceipt ? 2 : 1).ExecuteAsync(EnterpriseRequestApprovalSql.MarkStarted,
            Arg.Is<object?>(v => v is Dictionary<string, object?> &&
                Equals(((Dictionary<string, object?>)v)["Id"], f.Submission.Id) &&
                Equals(((Dictionary<string, object?>)v)["WorkflowInstanceId"], f.Submission.WorkflowInstanceId)), Arg.Any<CancellationToken>());
        await f.Resolver.Received(1).ResolveActiveByIdAsync(f.Row.TenantId, token);
        if (reachedStart) await f.Starter.Received(1).StartAsync(f.Submission.SubmittedById, Arg.Any<StartWorkflowInstanceCommand>(), token);
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestApprovalSql.MarkStarted, Arg.Any<object?>(), CancellationToken.None);
        f.Tenant.Received(stage == "resolver" ? 1 : 2).SetHost(); Assert.IsFalse(f.State.HasTransaction);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Failed_or_empty_workflow_result_remains_retryable(bool empty)
    {
        var f = new Fixture();
        var failed = empty ? Result<WorkflowInstanceLifecycleResult>.Success(null!)
            : Result<WorkflowInstanceLifecycleResult>.Failure(new Error("test.definition_unavailable", "Test failure", ErrorType.Conflict));
        f.Starter.StartAsync(Arg.Any<Guid>(), Arg.Any<StartWorkflowInstanceCommand>(), Arg.Any<CancellationToken>())
            .Returns(failed, Result<WorkflowInstanceLifecycleResult>.Success(new(f.Submission.WorkflowInstanceId, "active", 1)));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Deliver());
        Assert.AreEqual(EnterpriseRequestWorkflowErrorCodes.WorkflowStartFailed, error.Message);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count()); f.Tenant.Received(1).SetHost();
        await f.Deliver();
        await f.Starter.Received(2).StartAsync(f.Submission.SubmittedById,
            Arg.Is<StartWorkflowInstanceCommand>(v => v != null && v.RequestedInstanceId == f.Submission.WorkflowInstanceId), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(EnterpriseRequestApprovalSql.MarkStarted, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("same")] [DataRow("submission")] [DataRow("instance")] [DataRow("pending")]
    public async Task Receipt_compare_exchange_accepts_only_same_completed_start(string kind)
    {
        var f = new Fixture(); var original = f.Submission;
        var latest = original with { StartedAtUtc = DateTimeOffset.UtcNow };
        if (kind == "submission") latest = latest with { Id = Guid.CreateVersion7() };
        if (kind == "instance") latest = latest with { WorkflowInstanceId = Guid.CreateVersion7() };
        if (kind == "pending") latest = latest with { StartedAtUtc = null };
        var reads = 0;
        f.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(EnterpriseRequestApprovalSql.FindByRequest,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => ++reads == 1 ? original : latest);
        f.Commands.ExecuteAsync(EnterpriseRequestApprovalSql.MarkStarted, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        if (kind == "same") await f.Deliver();
        else Assert.AreEqual("enterprise_request.start_receipt_conflict", (await Assert.ThrowsAsync<InvalidOperationException>(() => f.Deliver())).Message);
        Assert.AreEqual(2, reads); f.Tenant.Received(1).SetHost();
        await f.Starter.Received(1).StartAsync(original.SubmittedById, Arg.Any<StartWorkflowInstanceCommand>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("host", false)] [DataRow("host", true)]
    [DataRow("tenant", false)] [DataRow("tenant", true)]
    [DataRow("clear", false)] [DataRow("clear", true)]
    public async Task Actual_parent_context_is_restored_after_start_or_failure(string parent, bool failed)
    {
        var f = new Fixture(); var accessor = new CurrentTenantAccessor(); var writer = (ICurrentTenantContextWriter)accessor;
        var previous = new TenantContext(Guid.CreateVersion7(), "previous", "原始租户");
        if (parent == "host") writer.SetHost(); else if (parent == "tenant") writer.SetTenant(previous);
        var fault = new InvalidOperationException("test.workflow_unavailable");
        f.Starter.StartAsync(Arg.Any<Guid>(), Arg.Any<StartWorkflowInstanceCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Assert.AreEqual(f.Row.TenantId, accessor.Id); Assert.IsFalse(accessor.IsHost);
            return failed ? Task.FromException<Result<WorkflowInstanceLifecycleResult>>(fault)
                : Task.FromResult(Result<WorkflowInstanceLifecycleResult>.Success(new(f.Submission.WorkflowInstanceId, "active", 1)));
        });
        var handler = new EnterpriseRequestApprovalSubmittedHandler(f.Serializer, f.Queries, f.Commands, f.State, writer,
            f.Resolver, f.Authorizer, f.Starter, Substitute.For<IClock>());
        if (failed) Assert.AreSame(fault, await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(f.Context, f.Payload, CancellationToken.None)));
        else await handler.HandleAsync(f.Context, f.Payload, CancellationToken.None);
        Assert.AreEqual(parent == "host", accessor.IsHost); Assert.AreEqual(parent != "clear", accessor.IsAvailable);
        Assert.AreEqual(parent == "tenant" ? previous.Id : (Guid?)null, accessor.Id);
        Assert.AreEqual(parent == "tenant" ? previous.Identifier : null, accessor.Identifier);
        Assert.AreEqual(parent == "tenant" ? previous.Name : null, accessor.Name);
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
        internal Task Deliver(CancellationToken token = default) => Handler.HandleAsync(Context, Payload, token);
    }
}
