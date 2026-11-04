using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Printing.Features.ManageTemplates;
using Full.NET.Modules.Printing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Printing.Features.PublishedTemplates;

/// <summary>Host 对活动租户授予精确发布版本；撤销无需租户仍处于活动状态。</summary>
internal sealed class PrintingTenantGrantManagementService(
    ICurrentTenant tenant, IActiveTenantContextResolver tenants, PrintingTemplateQueryService templates,
    ICommandExecutor commands, IIdGenerator ids, IClock clock, IOptions<DatabaseOptions> database,
    IQueryExecutor queries)
{
    /// <summary>分页保留所有存量授权；停用租户仍可被 Host 查看并显式撤销。</summary>
    public async Task<Result<PagedResult<Guid>>> ListAsync(Guid templateId, int versionNumber,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!tenant.IsHost || templateId == Guid.Empty || versionNumber <= 0)
            return Result<PagedResult<Guid>>.Failure(new(CommonErrorCodes.PermissionDenied,
                "Host version grant permission is required.", ErrorType.Forbidden));
        if (page < 1 || pageSize is < 1 or > 200)
            return Result<PagedResult<Guid>>.Failure(new(Contracts.PrintingErrorCodes.TemplateInvalid,
                "Page must be positive and page size must be between 1 and 200.", ErrorType.Validation));
        var version = await templates.GetVersionAsync(templateId, versionNumber, cancellationToken).ConfigureAwait(false);
        if (!version.IsSuccess) return Result<PagedResult<Guid>>.Failure(version.Error!);
        var parameters = PrintingSqlParameters.Create(("TemplateId", templateId), ("VersionNumber", versionNumber),
            ("Offset", ((long)page - 1) * pageSize), ("PageSize", pageSize));
        var total = await queries.QuerySingleOrDefaultAsync<long>(PrintingTenantGrantSql.CountGrants, parameters, cancellationToken).ConfigureAwait(false);
        var rows = await queries.QueryAsync<Guid>(PrintingTenantGrantSql.ListGrants(database.Value.Provider), parameters, cancellationToken).ConfigureAwait(false);
        return Result<PagedResult<Guid>>.Success(new(rows, page, pageSize, total));
    }

    /// <summary>Host 幂等授予精确版本；撤销允许目标租户或模板已停用。</summary>
    /// <remarks>活动租户 Port 在本模块写入前完成，不进入跨模块本地事务。</remarks>
    public async Task<Result<bool>> SetAsync(Guid templateId, int versionNumber, Guid tenantId, Guid actorId,
        bool grant, CancellationToken cancellationToken)
    {
        if (!tenant.IsHost || tenantId == Guid.Empty || actorId == Guid.Empty || versionNumber <= 0)
            return Result<bool>.Failure(new(CommonErrorCodes.PermissionDenied, "Host version grant permission is required.", ErrorType.Forbidden));
        if (grant)
        {
            if (await tenants.ResolveActiveByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is null)
                return Result<bool>.Failure(new(CommonErrorCodes.PermissionDenied, "The target tenant is unavailable.", ErrorType.Forbidden));
            var template = await templates.GetByIdAsync(templateId, cancellationToken).ConfigureAwait(false);
            var version = await templates.GetVersionAsync(templateId, versionNumber, cancellationToken).ConfigureAwait(false);
            if (!template.IsSuccess || template.Value?.IsEnabled != true || !version.IsSuccess)
                return Result<bool>.Failure(new(CommonErrorCodes.PermissionDenied, "The published version is unavailable.", ErrorType.Forbidden));
        }
        await commands.ExecuteAsync(grant ? PrintingTenantGrantSql.Grant(database.Value.Provider) : PrintingTenantGrantSql.Revoke,
            PrintingSqlParameters.Create(("Id", ids.NewId()), ("TargetTenantId", tenantId), ("TemplateId", templateId),
                ("VersionNumber", versionNumber), ("GrantedByUserId", actorId), ("CreatedAtUtc", clock.UtcNow)), cancellationToken).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }
}
