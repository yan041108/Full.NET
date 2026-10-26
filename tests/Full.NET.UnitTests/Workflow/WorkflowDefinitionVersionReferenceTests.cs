using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution.Connectors;
using Full.NET.Modules.Workflow.Features.ManageDefinitions;
using Full.NET.Modules.Workflow.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>引用检查在定义锁和同模块事务内裁定；最终外键竞态仍返回稳定冲突并使结果事务失败。</summary>
[TestClass]
public sealed class WorkflowDefinitionVersionReferenceTests
{
    [TestMethod]
    public async Task Version_reference_is_checked_inside_transaction_before_any_dependency_is_removed()
    {
        var f = new Fixture();
        f.Query.QuerySingleOrDefaultAsync<int>(WorkflowVersionReferenceSql.HasReferences, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { Assert.IsTrue(f.Transaction.Active); return 1; });
        var result = await f.Service.DeleteVersionAsync(f.VersionId);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(WorkflowErrorCodes.VersionInUse, result.Error!.Code);
        await f.Commands.Received(1).ExecuteAsync(WorkflowVersionReferenceSql.LockDefinition, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(WorkflowVersionReferenceSql.DeleteOwned, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(WorkflowSql.DeleteDefinitionVersion, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }
    [TestMethod]
    public async Task Foreign_key_race_returns_version_in_use_instead_of_leaking_database_error()
    {
        var f = new Fixture();
        f.Commands.ExecuteAsync(WorkflowSql.DeleteDefinitionVersion, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DataCommandException(DataCommandFailureKind.InvalidValue, new InvalidOperationException("foreign key"))));
        var result = await f.Service.DeleteVersionAsync(f.VersionId);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(WorkflowErrorCodes.VersionInUse, result.Error!.Code);
    }
    [TestMethod]
    public async Task Publish_read_deadlock_returns_revision_conflict_without_driver_dependency()
    {
        var f = new Fixture();
        f.Query.QuerySingleOrDefaultAsync<WorkflowDefinitionRecord>(WorkflowSql.FindDefinitionById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<WorkflowDefinitionRecord?>(new DataCommandException(DataCommandFailureKind.Deadlock, new InvalidOperationException("deadlock"))));
        var result = await f.Service.PublishAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), new(1, Guid.CreateVersion7()));
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error!.Code);
        Assert.IsFalse(f.Transaction.Active);
    }
    [TestMethod]
    public async Task Delete_deadlock_is_reported_only_after_transaction_has_ended()
    {
        var f = new Fixture();
        f.Query.QuerySingleOrDefaultAsync<int>(WorkflowVersionReferenceSql.HasReferences, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new DataCommandException(DataCommandFailureKind.Deadlock, new InvalidOperationException("deadlock"))));
        var result = await f.Service.DeleteVersionAsync(f.VersionId);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(WorkflowErrorCodes.RevisionConflict, result.Error!.Code); Assert.IsFalse(f.Transaction.Active);
    }
    private sealed class Fixture
    {
        public Guid VersionId { get; } = Guid.CreateVersion7();
        public IQueryExecutor Query { get; } = Substitute.For<IQueryExecutor>();
        public ICommandExecutor Commands { get; } = Substitute.For<ICommandExecutor>();
        public TrackingTransaction Transaction { get; } = new();
        public WorkflowDefinitionManagementService Service { get; }
        public Fixture()
        {
            var id = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow; var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true);
            Query.QuerySingleOrDefaultAsync<WorkflowDefinitionVersionRecord>(WorkflowSql.FindDefinitionVersionById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new WorkflowDefinitionVersionRecord(VersionId, id, Guid.CreateVersion7(), 1, 1, "{}", new string('a',64), null, Guid.CreateVersion7(), now));
            Query.QuerySingleOrDefaultAsync<WorkflowDefinitionRecord>(WorkflowSql.FindDefinitionById, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new WorkflowDefinitionRecord(id, null, "host", "host", "child", Guid.CreateVersion7(), VersionId, null, "active", Guid.CreateVersion7(), now, null, 1));
            Query.QueryAsync<WorkflowDefinitionVersionRecord>(WorkflowSql.ListDefinitionVersions, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<WorkflowDefinitionVersionRecord>());
            Service = new(Query, Commands, Transaction, tenant, Substitute.For<IClock>(), Substitute.For<IIdGenerator>(),
                Substitute.For<IHostUserBatchSelectionDirectory>(), Substitute.For<ITenantMemberSelectionDirectory>(),
                WorkflowTodoManagementTestDependencies.CreateAssigneePublishValidator(), Substitute.For<ITenantFeatureEntitlementPort>(),
                new WorkflowConnectorCatalog([]), new WorkflowBusinessActionCatalog([]));
        }
    }
    private sealed class TrackingTransaction : ICommandTransaction
    {
        public bool Active { get; private set; }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token)
        { Active = true; try { return await action(token); } finally { Active = false; } }
    }
}
