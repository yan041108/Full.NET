using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution.BusinessCommands;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>取消或拒绝后的真实补偿仍须接纳，但不能再次完成或推进根实例。</summary>
[TestClass]
public sealed class WorkflowBusinessCommandResultServiceTests
{
    [TestMethod]
    [DataRow("cancelled")]
    [DataRow("rejected")]
    public async Task Terminal_root_accepts_compensation_after_lock_without_resuming(string status)
    {
        var now = DateTimeOffset.UtcNow;
        var root = new WorkflowInstanceRecord(Guid.CreateVersion7(), null, "host", "host", Guid.CreateVersion7(), Guid.CreateVersion7(),
            "owner.rule", "request", null, status, 3, Guid.CreateVersion7(), now, null, null, null, null, null, null);
        var catalog = new WorkflowBusinessActionCatalog([new("owner", "owner.apply", 1, "host", "owner.rule", "root_finalizer")]);
        var action = catalog.ForScope("host").Single(); var callId = Guid.CreateVersion7();
        var binding = new WorkflowBusinessCommandBinding(callId, root.Id, root.DefinitionVersionId, "owner", "owner.apply", 1, action.Hash, root.BusinessType, root.BusinessId, root.StartedById);
        var hash = WorkflowBusinessCommandHash.Compute(binding);
        var row = new WorkflowBusinessCommandRecord(callId, root.Id, "host", "apply", "end", "owner.apply", 1, action.Hash,
            JsonSerializer.Serialize(binding, WorkflowJsonSerializerContext.Default.WorkflowBusinessCommandBinding), hash, "compensating", true, 2,
            new('b',64), 2, null, null, null, 2, now, now);
        var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>(); var locked = false;
        commands.ExecuteAsync(WorkflowSql.LockInstanceForTodoAction, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => { locked = true; return 1; });
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { Assert.IsTrue(locked, "终态必须在持有根锁后读取"); return root; });
        query.QuerySingleOrDefaultAsync<WorkflowBusinessCommandRecord>(WorkflowBusinessCommandSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        commands.ExecuteAsync(WorkflowBusinessCommandSql.AcceptResult, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true); var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var service = new WorkflowBusinessCommandResultService(query, commands, new ImmediateTransaction(), tenant, clock, Substitute.For<IIdGenerator>(), catalog);
        await service.AcceptAsync(null, new(callId, root.Id, hash, "compensate", 2, "compensated", new('b',64), 2, 3, null, now), default);
        await commands.Received(1).ExecuteAsync(WorkflowBusinessCommandSql.AcceptResult, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        Assert.IsFalse(commands.ReceivedCalls().Any(call => call.GetArguments()[0] is SqlStatement statement &&
            (statement == WorkflowSql.AdvanceInstanceWithRevision || statement == WorkflowSql.CompleteStepWithRevision || statement == WorkflowSql.LockInstanceForMultiApproval)));
    }
    private sealed class ImmediateTransaction : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token) => action(token);
    }
}
