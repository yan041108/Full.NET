using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Features.FormAttachments;

/// <summary>Files 对账只通过本端口确认当前租户、本模块历史证据仍持有本人资源文件。</summary>
internal sealed class WorkflowTenantFileOwner(IQueryExecutor query, ICurrentTenant tenant) : ITenantResourceFileOwner
{
    public string OwnerModuleKey => WorkflowSignatureCoordinator.SignatureOwnerModuleKey;

    public async Task<bool> IsReferencedAsync(Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
    {
        var scope = WorkflowManagementScope.Resolve(tenant);
        if (scope.TenantId is null) return false;
        return await query.QuerySingleOrDefaultAsync<int>(WorkflowSignatureSql.ExistsForTenantOwner,
            WorkflowSqlParameters.Create(("ActorUserId", resourceId), ("FileId", fileId), ("TenantScopeKey", scope.TenantScopeKey)),
            cancellationToken).ConfigureAwait(false) == 1;
    }
}
