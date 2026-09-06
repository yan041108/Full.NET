using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageTenantQuotas;

/// <summary>AI 租户配额分页列表与详情只读查询。</summary>
internal sealed class AiTenantQuotaQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>按租户标识读取配额详情；不存在时返回零用量占位响应。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>详情或稳定业务错误。</returns>
    public async Task<Result<AiTenantQuotaResponse>> GetByTenantIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<AiTenantQuotaRecord>(
                AiTenantQuotaSql.FindByTenantId,
                AiSqlParameters.Create(("TenantId", tenantId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return Result<AiTenantQuotaResponse>.Failure(new Error(
                AiErrorCodes.TenantQuotaNotFound,
                "The AI tenant quota was not found.",
                ErrorType.NotFound));
        }

        return Result<AiTenantQuotaResponse>.Success(AiTenantQuotaMapper.MapDetail(row));
    }

    /// <summary>分页查询租户配额列表。</summary>
    /// <param name="page">页码，从 1 开始。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="tenantId">可选租户筛选。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    public async Task<Result<PagedResult<AiTenantQuotaListItem>>> ListAsync(
        int page,
        int pageSize,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = AiSqlParameters.Create(
            ("TenantId", tenantId),
            ("Offset", offset),
            ("PageSize", pageSize));
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                AiTenantQuotaSql.CountSqlServer,
                AiTenantQuotaSql.ListSqlServer),
            DatabaseProvider.MySql => (
                AiTenantQuotaSql.CountMySql,
                AiTenantQuotaSql.ListMySql),
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                new SqlStatement("ai.count_tenant_quotas", countStatement, SqlDataScope.HostOnly),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<AiTenantQuotaRecord>(
                new SqlStatement("ai.list_tenant_quotas", listStatement, SqlDataScope.HostOnly),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<AiTenantQuotaListItem>>.Success(
            new PagedResult<AiTenantQuotaListItem>(
                rows.Select(AiTenantQuotaMapper.MapListItem).ToArray(),
                page,
                pageSize,
                total));
    }
}
