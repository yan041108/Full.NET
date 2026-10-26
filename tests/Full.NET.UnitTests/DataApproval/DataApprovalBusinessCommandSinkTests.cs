using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.DataApproval.Contracts;
using Full.NET.Modules.DataApproval.Features.ProjectWorkflowOutcomes;
using Full.NET.Modules.DataApproval.Persistence;
using Full.NET.Modules.Workflow.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.DataApproval;

/// <summary>实际节点消费前独立验证作用域和固定业务绑定，跨模块端口不得被本地事务包裹。</summary>
[TestClass]
public sealed class DataApprovalBusinessCommandSinkTests
{
    [TestMethod]
    public async Task Owner_and_proof_run_outside_local_transaction_and_receipt_is_written_inside()
    {
        var f = new Fixture(); var wroteInside = false;
        f.Commands.ExecuteAsync(DataApprovalCommandSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { wroteInside = f.Transaction.Inside; return 1; });
        await f.Sink.HandleAsync(f.Context, f.Value, default);
        Assert.IsTrue(wroteInside);
        await f.Owner.Received(1).ApplyAsync(Arg.Any<DataApprovalScenarioCommand>(), Arg.Any<CancellationToken>());
        await f.Owner.DidNotReceive().CompensateAsync(Arg.Any<DataApprovalScenarioCommand>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Cancelled_root_uses_compensation_even_when_original_execute_arrives_late()
    {
        var f = new Fixture(); f.Proof = f.Proof with { RootStatusKey = "cancelled" };
        await f.Sink.HandleAsync(f.Context, f.Value, default);
        await f.Owner.Received(1).CompensateAsync(Arg.Any<DataApprovalScenarioCommand>(), Arg.Any<CancellationToken>());
        await f.Owner.DidNotReceive().ApplyAsync(Arg.Any<DataApprovalScenarioCommand>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Stale_generation_and_wrong_tenant_never_call_owner()
    {
        var f = new Fixture(); f.Proof = f.Proof with { Generation = 2, CompensationRequested = true };
        await f.Sink.HandleAsync(f.Context, f.Value, default);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Sink.HandleAsync(f.Context with { TenantId = Guid.CreateVersion7() }, f.Value, default));
        await f.Owner.DidNotReceive().ApplyAsync(Arg.Any<DataApprovalScenarioCommand>(), Arg.Any<CancellationToken>());
        await f.Owner.DidNotReceive().CompensateAsync(Arg.Any<DataApprovalScenarioCommand>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("actor")]
    [DataRow("definition")]
    public async Task Request_binding_mismatch_is_rejected_before_owner(string mismatch)
    {
        var f = new Fixture(); var row = mismatch switch { "root" => f.Row with { WorkflowInstanceId = Guid.CreateVersion7() },
            "actor" => f.Row with { SubmittedByUserId = Guid.CreateVersion7() }, _ => f.Row with { WorkflowDefinitionVersionId = Guid.CreateVersion7() } };
        f.Query.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(DataApprovalSql.FindRequestById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Sink.HandleAsync(f.Context, f.Value, default));
        await f.Owner.DidNotReceive().ApplyAsync(Arg.Any<DataApprovalScenarioCommand>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Missing_owner_link_is_retryable_and_unrequested_compensation_is_denied()
    {
        var f = new Fixture();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Sink.HandleAsync(f.Context, f.Value with { PhaseKey = "compensate" }, default));
        f.Query.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(DataApprovalSql.FindRequestById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(f.Row with { WorkflowInstanceId = null });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Sink.HandleAsync(f.Context, f.Value, default));
        await f.Owner.DidNotReceive().ApplyAsync(Arg.Any<DataApprovalScenarioCommand>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("serial_numbers.rule.version_conflict", "declined")]
    [DataRow("serial_numbers.approval_command.input_conflict", "unknown")]
    [DataRow("serial_numbers.approval_command.receipt_invalid", "unknown")]
    public async Task Failure_distinguishes_proven_no_effect_from_unresolved_owner_proof(string code, string expected)
    {
        var f = new Fixture(); f.Owner.ApplyAsync(Arg.Any<DataApprovalScenarioCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<DataApprovalCommandEffect>.Failure(new Error(code, "owner failure", ErrorType.Conflict)));
        await f.Sink.HandleAsync(f.Context, f.Value, default);
        Assert.AreEqual(expected, f.Published.Single().OutcomeKey);
        Assert.IsNull(f.Published.Single().AppliedVersion);
    }

    private sealed class Fixture
    {
        public IQueryExecutor Query { get; } = Substitute.For<IQueryExecutor>();
        public ICommandExecutor Commands { get; } = Substitute.For<ICommandExecutor>();
        public IDataApprovalScenarioCommandHandler Owner { get; } = Substitute.For<IDataApprovalScenarioCommandHandler>();
        public TrackedTransaction Transaction { get; } = new();
        public List<WorkflowBusinessCommandResultIntegrationEvent> Published { get; } = [];
        public WorkflowBusinessCommandProof Proof { get; set; }
        public DataApprovalRequestRecord Row { get; }
        public WorkflowBusinessCommandRequestedIntegrationEvent Value { get; }
        public IntegrationEventContext Context { get; } = new(Guid.CreateVersion7(), WorkflowBusinessCommandIntegrationEventTypes.Requested, 1, null, null, DateTimeOffset.UtcNow);
        public DataApprovalBusinessCommandSink Sink { get; }
        public Fixture()
        {
            var now = DateTimeOffset.UtcNow; var request = Guid.CreateVersion7(); var instance = Guid.CreateVersion7(); var definition = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
            Row = new(request, null, "host", "host", DataApprovalScenarioKeys.SerialRuleHostUpdate, Guid.CreateVersion7(), DataApprovalStatusKeys.InReview,
                null, "{}", instance, 1, definition, actor, now, null, "request", "none", null, null, null, 0, "none", null, null, null, 0, now, now, 2);
            var binding = new WorkflowBusinessCommandBinding(Guid.CreateVersion7(), instance, definition, "data_approval", "data_approval.apply_serial_rule_update", 1,
                new('a',64), DataApprovalWorkflowBusinessTypes.SerialRuleUpdate, request.ToString("D"), actor);
            Value = new(binding, WorkflowBusinessCommandHash.Compute(binding), "execute", 1, now);
            Proof = new(binding, Value.InputHash, "active", "requested", false, 1);
            var proofs = Substitute.For<IWorkflowBusinessCommandProofReader>();
            proofs.FindAsync(binding.CallId, instance, Value.InputHash, Arg.Any<CancellationToken>()).Returns(_ => { Assert.IsFalse(Transaction.Inside); return Proof; });
            Query.QuerySingleOrDefaultAsync<DataApprovalRequestRecord>(DataApprovalSql.FindRequestById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Row);
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Owner.Supports(Row.ScenarioKey).Returns(true);
            Owner.ApplyAsync(Arg.Any<DataApprovalScenarioCommand>(), Arg.Any<CancellationToken>()).Returns(_ => { Assert.IsFalse(Transaction.Inside); return Result<DataApprovalCommandEffect>.Success(new(binding.CallId, new('b',64), "applied", 2, null)); });
            Owner.CompensateAsync(Arg.Any<DataApprovalScenarioCommand>(), Arg.Any<CancellationToken>()).Returns(_ => { Assert.IsFalse(Transaction.Inside); return Result<DataApprovalCommandEffect>.Success(new(binding.CallId, new('b',64), "cancelled", null, null)); });
            var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true);
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
            var outbox = Substitute.For<IOutboxWriter>();
            outbox.AddAsync(WorkflowBusinessCommandIntegrationEventTypes.Result, 1, Arg.Any<WorkflowBusinessCommandResultIntegrationEvent>(),
                Arg.Any<IntegrationEventMetadata>(), Arg.Any<CancellationToken>()).Returns(call => { Assert.IsTrue(Transaction.Inside);
                    Published.Add(call.ArgAt<WorkflowBusinessCommandResultIntegrationEvent>(2)); return Task.CompletedTask; });
            Sink = new(Query, Commands, Transaction, tenant, clock, Substitute.For<IIdGenerator>(), outbox, proofs, [Owner]);
        }
    }
    private sealed class TrackedTransaction : ICommandTransaction
    {
        public bool Inside { get; private set; }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
        { Inside = true; try { return await action(token); } finally { Inside = false; } }
    }
}
