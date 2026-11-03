using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.EnterpriseRequest.Features.DraftMutations;

/// <summary>在本模块命令事务内锁定主单；与提交审批共用主单锁，防止已提交后继续写明细。</summary>
internal sealed class EnterpriseRequestDraftGuard(IQueryExecutor queries, IOptions<DatabaseOptions> databaseOptions)
{
    internal static Error InvalidStatus() => new(EnterpriseRequestWorkflowErrorCodes.InvalidStatus,
        "Only draft requests may be changed through CRUD.", ErrorType.Conflict);

    internal async Task<Error?> EnsureDraftAsync(Guid id, Guid? organizationUnitId, CancellationToken token)
    {
        var sql = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => "SELECT Status FROM demo_enterprise_request_enterprise_request WITH (UPDLOCK, HOLDLOCK) WHERE Id=@Id AND TenantId=@TenantId AND IsDeleted=0 AND (@OrganizationUnitId IS NULL OR OrganizationUnitId=@OrganizationUnitId)",
            DatabaseProvider.MySql => "SELECT Status FROM demo_enterprise_request_enterprise_request WHERE Id=@Id AND TenantId=@TenantId AND IsDeleted=0 AND (@OrganizationUnitId IS NULL OR OrganizationUnitId=@OrganizationUnitId) FOR UPDATE",
            _ => throw new InvalidOperationException("Unsupported database provider."),
        };
        var status = await queries.QuerySingleOrDefaultAsync<string>(new SqlStatement(
            "enterprise_request.draft_mutation.lock_parent", sql, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId),
            new Dictionary<string, object?> { ["Id"] = id, ["OrganizationUnitId"] = organizationUnitId }, token).ConfigureAwait(false);
        return status is null ? new Error("enterprise_request.enterprise_requests.not_found", "The request was not found.", ErrorType.NotFound)
            : status == EnterpriseRequestStatusKeys.Draft ? null : InvalidStatus();
    }
}
