using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Execution.Connectors;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>后台连接器在新副作用前必须取得活动租户；无效候选退让，不能占据下一批。</summary>
[TestClass]
public sealed class WorkflowConnectorTenantGateTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Inactive_tenant_is_deferred_before_transaction_or_transport(DatabaseProvider provider)
    {
        var tenantId = Guid.NewGuid(); var id = Guid.NewGuid(); var scope = $"tenant:{tenantId:N}";
        var now = DateTimeOffset.UtcNow;
        var call = new WorkflowConnectorCallRecord(Guid.NewGuid(), id, scope, "call", "end", "external", 1, "hash", "sync",
            "{}", "hash", 1, "{}", "hash", null, null, "pending", null, null, 0, null, null, 0, 0, null, now, null, 1, now, null, null);
        var instance = new WorkflowInstanceRecord(id, tenantId, "tenant", scope, Guid.NewGuid(), null, "test", "1", null,
            "active", 1, Guid.NewGuid(), now, null, null, null, null, null, null);
        var queries = Substitute.For<IQueryExecutor>();
        queries.QueryAsync<WorkflowConnectorCallRecord>(provider == DatabaseProvider.SqlServer ? WorkflowConnectorSql.ListDispatchSqlServer
            : WorkflowConnectorSql.ListDispatchMySql, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new[] { call });
        queries.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(instance);
        var commands = Substitute.For<ICommandExecutor>(); var transaction = Substitute.For<ICommandTransaction>();
        var tenants = new CurrentTenantAccessor(); ((ICurrentTenantContextWriter)tenants).SetHost();
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now);
        var active = Substitute.For<IActiveTenantContextResolver>();
        var processor = new WorkflowConnectorProcessor(queries, commands, transaction, clock, Substitute.For<IIdGenerator>(), tenants,
            Options.Create(new DatabaseOptions { Provider = provider }), Options.Create(new WorkflowRecoveryWorkerOptions()),
            null!, null!, null!, null!, active);
        Assert.AreEqual(0, await processor.ProcessPendingAsync(default));
        await commands.Received(1).ExecuteAsync(WorkflowConnectorSql.DeferInactiveTenant,
            Arg.Is<object?>(value => ((IReadOnlyDictionary<string, object?>)value!)["DueAtUtc"]!.Equals(now.AddSeconds(30))), Arg.Any<CancellationToken>());
        Assert.IsFalse(transaction.ReceivedCalls().Any());
        Assert.IsTrue(tenants.IsHost);
        await active.Received(1).ResolveActiveByIdAsync(tenantId, Arg.Any<CancellationToken>());
    }
}
