using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Features.CrossModulePorts;

/// <summary>按定义键向其他模块暴露最新已发布工作流版本摘要。</summary>
internal sealed class WorkflowPublishedDefinitionDirectoryAdapter(
    IQueryExecutor queryExecutor,
    ICurrentTenant currentTenant) : IWorkflowPublishedDefinitionDirectory
{
    /// <inheritdoc />
    public async Task<WorkflowPublishedDefinitionVersion?> FindLatestPublishedAsync(
        string definitionKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(definitionKey))
        {
            return null;
        }

        var scope = WorkflowManagementScope.Resolve(currentTenant);
        var definition = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowDefinitionRecord>(
            WorkflowSql.FindDefinitionByKey,
            WorkflowSqlParameters.Create(
                ("TenantScopeKey", scope.TenantScopeKey),
                ("DefinitionKey", definitionKey.Trim())),
            cancellationToken).ConfigureAwait(false);
        if (definition?.LatestPublishedVersionId is not { } versionId)
        {
            return null;
        }

        var version = await queryExecutor.QuerySingleOrDefaultAsync<WorkflowDefinitionVersionRecord>(
            WorkflowSql.FindDefinitionVersionById,
            WorkflowSqlParameters.Create(
                ("VersionId", versionId),
                ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken).ConfigureAwait(false);
        if (version is null)
        {
            return null;
        }

        return new WorkflowPublishedDefinitionVersion(
            version.Id,
            version.FormVersionId,
            definition.DefinitionKey);
    }
}
