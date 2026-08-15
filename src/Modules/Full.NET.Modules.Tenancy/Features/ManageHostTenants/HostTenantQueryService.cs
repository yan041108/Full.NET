using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Tenancy.Features.ManageHostTenants;

/// <summary>
/// Host 侧租户只读查询服务。提供分页列表与单条详情，支持 SQL Server/MySQL 两种分页方言。
/// 列表查询不走解析缓存，每次直接查最新持久化，确保管理端禁用/改名后 UI 立即可见。
/// </summary>
internal sealed class HostTenantQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>
    /// 按创建顺序倒序的宿主租户分页列表；pageSize 范围 [1, 100] 自动夹取。
    /// </summary>
    public async Task<Result<PagedResult<TenantSummary>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                TenantSql.CountHostTenants,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => TenantSql.ListHostTenantsSqlServer,
            DatabaseProvider.MySql => TenantSql.ListHostTenantsMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<HostTenantRecord>(
                statement,
                new { Offset = offset, PageSize = pageSize },
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<TenantSummary>>.Success(
            new PagedResult<TenantSummary>(items, page, pageSize, total));
    }

    /// <summary>
    /// 按 ID 查询单个租户详情；找不到返回 TenancyErrorCodes.NotFound。
    /// 写操作成功后通常使用本方法重新读取最新数据作为响应体。
    /// </summary>
    public async Task<Result<TenantSummary>> GetByIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<HostTenantRecord>(
                TenantSql.FindHostTenantById,
                new { TenantId = tenantId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<TenantSummary>.Success(Map(record));
    }

    private static TenantSummary Map(HostTenantRecord record) =>
        new(
            record.Id,
            record.Identifier,
            record.Name,
            record.Domain,
            record.IsActive,
            record.Version,
            record.DefaultLocale,
            record.TenantPackageId,
            record.TenantPackageCode,
            record.TenantPackageName);

    private static Result<TenantSummary> NotFound() =>
        Result<TenantSummary>.Failure(new Error(
            TenancyErrorCodes.NotFound,
            "The tenant was not found.",
            ErrorType.NotFound));
}

internal sealed record HostTenantRecord(
    Guid Id,
    string Identifier,
    string Name,
    string Domain,
    bool IsActive,
    int Version,
    string DefaultLocale,
    Guid? TenantPackageId,
    string? TenantPackageCode,
    string? TenantPackageName);
