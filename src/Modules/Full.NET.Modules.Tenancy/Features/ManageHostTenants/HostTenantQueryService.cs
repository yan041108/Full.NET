using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Tenancy.Features.ManageHostTenants;

/// <summary>Host 租户分页列表与详情只读查询。</summary>
internal sealed class HostTenantQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
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
