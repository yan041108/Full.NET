using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>一整页停用租户不能让后续截止永远被挡住。</summary>
[TestClass]
public sealed class WorkflowSlaScanTests
{
    [TestMethod]
    public async Task Inactive_tenant_page_advances_scan_cursor_without_dispatching()
    {
        var now = DateTimeOffset.UtcNow; var tenantId = Guid.CreateVersion7(); var instanceId = Guid.CreateVersion7();
        var rows = Enumerable.Range(0, 10).Select(index => new WorkflowSlaRecord(Guid.CreateVersion7(), instanceId, $"tenant:{tenantId:N}",
            "suspend", "elapsed", now.AddMinutes(-20 + index), null, null, "waiting", 1, now.AddHours(-1), null,
            "review", "human.approval", 1, null, null, "active")).ToArray();
        var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        query.QueryAsync<WorkflowSlaRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WorkflowSlaRecord>>(rows), Task.FromResult<IReadOnlyList<WorkflowSlaRecord>>([]));
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowInstanceRecord(instanceId, tenantId, "tenant", $"tenant:{tenantId:N}", Guid.CreateVersion7(), Guid.CreateVersion7(),
                "sla.test", "one", null, "active", 1, Guid.CreateVersion7(), now.AddHours(-1), null, null, null, null, null, null));
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var ids = Substitute.For<IIdGenerator>(); var tenants = Substitute.For<IActiveTenantContextResolver>();
        tenants.ResolveActiveByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns((TenantContext?)null);
        var outputs = new WorkflowAutomaticOutputCoordinator(query, commands, clock, new WorkflowBranchStateCoordinator(query, commands),
            WorkflowTodoManagementTestDependencies.CreateAttachmentCoordinator());
        var processor = new WorkflowSlaProcessor(query, commands, Substitute.For<ICommandTransaction>(), clock, ids,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), Options.Create(new WorkflowRecoveryWorkerOptions { BatchSize = 10 }),
            new WorkflowActivityScheduler(commands, ids), outputs, new WorkflowNotificationOutboxPublisher(Substitute.For<IOutboxWriter>()),
            tenants, Substitute.For<ICurrentTenantContextWriter>(), new WorkflowSlaScanCursor(),
            WorkflowTodoManagementTestDependencies.CreateResultCallbacks(query, commands, ids));
        Assert.AreEqual(0, await processor.ProcessPendingAsync(default)); Assert.AreEqual(0, await processor.ProcessPendingAsync(default));
        Assert.AreEqual(0, await processor.ProcessPendingAsync(default));
        var scans = query.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IQueryExecutor.QueryAsync)).ToArray();
        var second = (IReadOnlyDictionary<string, object?>)scans[1].GetArguments()[1]!;
        Assert.IsTrue(second.TryGetValue("HasAfter", out var hasAfter) && Equals(hasAfter, 1), "停用页必须推进游标，不能每轮重读首批。");
        Assert.AreEqual(rows[^1].Id, second["AfterId"]); Assert.AreEqual(rows[^1].DueAtUtc, second["AfterDueAtUtc"]);
        var first = (IReadOnlyDictionary<string, object?>)scans[0].GetArguments()[1]!;
        var wrapped = (IReadOnlyDictionary<string, object?>)scans[2].GetArguments()[1]!;
        Assert.AreEqual(0, first["HasAfter"]); Assert.IsNull(first["AfterId"]);
        Assert.AreEqual(0, wrapped["HasAfter"]); Assert.IsNull(wrapped["AfterId"]);
        Assert.AreEqual(0, commands.ReceivedCalls().Count());
    }
}
