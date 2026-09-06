using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ManageHostTenants;

/// <summary>通过 Identity Contract Port 为 Host 租户治理页提供成员与管理员只读目录。</summary>
/// <param name="queryExecutor">租户存在性校验查询执行器。</param>
/// <param name="tenantUsers">Identity 权威租户用户目录端口。</param>
internal sealed class HostTenantDirectoryQueryService(
    IQueryExecutor queryExecutor,
    IHostTenantUserSelectionDirectory tenantUsers)
{
    /// <summary>分页读取指定租户的活动成员目录。</summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="page">页码。</param>
    /// <param name="pageSize">页大小。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>成员分页结果；租户不存在时返回 NotFound。</returns>
    public async Task<Result<HostTenantMembersPageResponse>> ListMembersAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (!await TenantExistsAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return NotFoundMembers();
        }

        var directory = await tenantUsers.ListActiveTenantUsersAsync(
                tenantId,
                page,
                pageSize,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<HostTenantMembersPageResponse>.Success(
            new HostTenantMembersPageResponse(
                tenantId,
                directory.Items.Select(Map).ToArray(),
                directory.Page,
                directory.PageSize,
                directory.Total));
    }

    /// <summary>分页读取指定租户的活动系统管理员目录。</summary>
    /// <param name="tenantId">目标租户标识。</param>
    /// <param name="page">页码。</param>
    /// <param name="pageSize">页大小。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>管理员分页结果；租户不存在时返回 NotFound。</returns>
    public async Task<Result<HostTenantAdministratorsPageResponse>> ListAdministratorsAsync(
        Guid tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (!await TenantExistsAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return NotFoundAdministrators();
        }

        var directory = await tenantUsers.ListActiveTenantAdministratorsAsync(
                tenantId,
                page,
                pageSize,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<HostTenantAdministratorsPageResponse>.Success(
            new HostTenantAdministratorsPageResponse(
                tenantId,
                directory.Items.Select(Map).ToArray(),
                directory.Page,
                directory.PageSize,
                directory.Total));
    }

    private async Task<bool> TenantExistsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantResolutionRecord>(
                TenantSql.FindById,
                TenancySqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        return existing is not null;
    }

    private static HostTenantMemberResponse Map(HostTenantUserDirectoryEntry entry) =>
        new(entry.Id, entry.Username, entry.DisplayName, entry.AccountType, entry.IsActive);

    private static Result<HostTenantMembersPageResponse> NotFoundMembers() =>
        Result<HostTenantMembersPageResponse>.Failure(new Error(
            TenancyErrorCodes.NotFound,
            "The tenant was not found.",
            ErrorType.NotFound));

    private static Result<HostTenantAdministratorsPageResponse> NotFoundAdministrators() =>
        Result<HostTenantAdministratorsPageResponse>.Failure(new Error(
            TenancyErrorCodes.NotFound,
            "The tenant was not found.",
            ErrorType.NotFound));
}
