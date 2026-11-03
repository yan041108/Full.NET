using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Features.ManageRecoveryTasks;
using Full.NET.Modules.Workflow.HostDashboard;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>恢复列表和工作台计数也必须绑定当前操作人及同一授权证明。</summary>
[TestClass]
public sealed class WorkflowCollaborationReadModelTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public async Task Dashboard_counts_use_current_actor_proofs_in_both_providers(DatabaseProvider provider)
    {
        var query = Substitute.For<IQueryExecutor>(); var tenant = new CurrentTenantAccessor(); tenant.SetHost(); var actor = Guid.CreateVersion7();
        var pending = provider == DatabaseProvider.SqlServer ? WorkflowSql.CountPendingTodosFilteredSqlServer : WorkflowSql.CountPendingTodosFilteredMySql;
        var instances = provider == DatabaseProvider.SqlServer ? WorkflowSql.CountInstancesFilteredSqlServer : WorkflowSql.CountInstancesFilteredMySql;
        query.QuerySingleOrDefaultAsync<long>(pending, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(3);
        query.QuerySingleOrDefaultAsync<long>(instances, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(2);
        var reader = new HostDashboardWorkflowEntryReader(query, tenant, WorkflowTodoManagementTestDependencies.CreateListAuthorization(query, tenant, provider: provider),
            Options.Create(new DatabaseOptions { Provider = provider }));
        var result = await reader.ReadAsync(actor); Assert.AreEqual(3L, result.PendingTodoCount); Assert.AreEqual(2L, result.MyActiveInstanceCount);
        foreach (var statement in new[] { pending, instances })
            await query.Received(1).QuerySingleOrDefaultAsync<long>(statement, Arg.Is<Dictionary<string, object?>>(p => p != null &&
                (Guid)p["ActorUserId"]! == actor && (string)p["CollaborationProofs"]! == "[]"), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)][DataRow(DatabaseProvider.MySql)]
    public async Task Recovery_count_and_page_use_the_same_proof_without_a_write_transaction(DatabaseProvider provider)
    {
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>(); var transaction = Substitute.For<ICommandTransaction>();
        var tenant = new CurrentTenantAccessor(); tenant.SetHost(); var actor = Guid.CreateVersion7(); var options = Options.Create(new DatabaseOptions { Provider = provider });
        var count = provider == DatabaseProvider.SqlServer ? WorkflowRecoverySql.CountTasksForScopeSqlServer : WorkflowRecoverySql.CountTasksForScopeMySql;
        query.QuerySingleOrDefaultAsync<long>(count, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(3);
        var service = new WorkflowRecoveryTaskService(query, command, transaction, tenant, Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), options,
            WorkflowTodoManagementTestDependencies.CreateListAuthorization(query, tenant, provider: provider));
        var result = await service.ListAsync(actor, 2, 1, default); Assert.IsTrue(result.IsSuccess); Assert.AreEqual(3L, result.Value!.Total);
        var page = provider == DatabaseProvider.SqlServer ? WorkflowRecoverySql.ListTasksForScopeSqlServer : WorkflowRecoverySql.ListTasksForScopeMySql;
        await query.Received(1).QueryAsync<WorkflowRecoveryTaskRecord>(page, Arg.Is<Dictionary<string, object?>>(p => p != null &&
            (Guid)p["ActorUserId"]! == actor && (string)p["CollaborationProofs"]! == "[]" && (int)p["Offset"]! == 1 && (int)p["PageSize"]! == 1), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, command.ReceivedCalls().Count()); Assert.AreEqual(0, transaction.ReceivedCalls().Count());
    }
}
