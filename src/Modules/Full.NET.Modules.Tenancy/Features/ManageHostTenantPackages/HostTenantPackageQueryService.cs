using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;

/// <summary>Host 租户套餐分页列表与详情只读查询。</summary>
internal sealed class HostTenantPackageQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<TenantPackageSummary>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                TenantPackageSql.CountHostPackages,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => TenantPackageSql.ListHostPackagesSqlServer,
            DatabaseProvider.MySql => TenantPackageSql.ListHostPackagesMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<TenantPackageRecord>(
                statement,
                new { Offset = offset, PageSize = pageSize },
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<TenantPackageSummary>>.Success(
            new PagedResult<TenantPackageSummary>(items, page, pageSize, total));
    }

    public async Task<Result<TenantPackageSummary>> GetByIdAsync(
        Guid packageId,
        CancellationToken cancellationToken = default)
    {
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => TenantPackageSql.FindByIdSqlServer,
            DatabaseProvider.MySql => TenantPackageSql.FindByIdMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var record = await queryExecutor.QuerySingleOrDefaultAsync<TenantPackageRecord>(
                statement,
                new { PackageId = packageId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<TenantPackageSummary>.Success(Map(record));
    }

    private static TenantPackageSummary Map(TenantPackageRecord record) =>
        new(
            record.Id,
            record.Code,
            record.Name,
            record.Description,
            record.IsActive,
            record.Version,
            (int)record.AssignedTenantCount);

    private static Result<TenantPackageSummary> NotFound() =>
        Result<TenantPackageSummary>.Failure(new Error(
            TenancyErrorCodes.PackageNotFound,
            "The tenant package was not found.",
            ErrorType.NotFound));
}

internal sealed record TenantPackageRecord(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    int Version,
    long AssignedTenantCount = 0);

internal sealed record TenantPackageIdentityRecord(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    int Version);
