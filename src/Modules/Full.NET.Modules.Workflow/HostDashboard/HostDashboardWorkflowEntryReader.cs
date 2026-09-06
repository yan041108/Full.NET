using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.HostDashboard;

/// <summary>批量读取当前用户待办与活动实例计数，供 Host 工作台业务入口聚合。</summary>
internal sealed class HostDashboardWorkflowEntryReader(
    IQueryExecutor queryExecutor,
    ICurrentTenant currentTenant)
    : IHostDashboardWorkflowEntryReader
{
    /// <inheritdoc />
    public async Task<HostDashboardWorkflowEntryMetrics> ReadAsync(
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(actorUserId, Guid.Empty);

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var pendingTodoCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                WorkflowSql.CountPendingTodosFiltered,
                WorkflowSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("AssigneeUserId", actorUserId),
                    ("DefinitionKey", null),
                    ("BusinessType", null),
                    ("ResultActionKey", null),
                    ("ArrivedFromUtc", null),
                    ("ArrivedToUtc", null),
                    ("Offset", 0),
                    ("PageSize", 1)),
                cancellationToken)
            .ConfigureAwait(false);
        var myActiveInstanceCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                WorkflowSql.CountInstancesFiltered,
                WorkflowSqlParameters.Create(
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("StartedById", actorUserId),
                    ("StatusKey", "active"),
                    ("DefinitionKey", null),
                    ("DefinitionVersionId", null),
                    ("StartedFromUtc", null),
                    ("StartedToUtc", null)),
                cancellationToken)
            .ConfigureAwait(false);

        return new HostDashboardWorkflowEntryMetrics(
            pendingTodoCount,
            myActiveInstanceCount);
    }
}
