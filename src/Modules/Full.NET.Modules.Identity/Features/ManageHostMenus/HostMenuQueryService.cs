using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageHostMenus;

/// <summary>Host 菜单分页列表与详情只读查询。</summary>
internal sealed class HostMenuQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostMenuResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountHostMenus,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => IdentitySql.ListHostMenusSqlServer,
            DatabaseProvider.MySql => IdentitySql.ListHostMenusMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<HostMenuListRow>(
                statement,
                new { Offset = offset, PageSize = pageSize },
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<HostMenuResponse>>.Success(
            new PagedResult<HostMenuResponse>(items, page, pageSize, total));
    }

    public async Task<Result<HostMenuResponse>> GetByIdAsync(
        Guid menuId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityNavigationRecord>(
                IdentitySql.FindHostMenuById,
                new { MenuId = menuId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<HostMenuResponse>.Success(Map(record));
    }

    internal static HostMenuResponse Map(HostMenuListRow row) =>
        new(
            row.Id,
            row.ParentId,
            row.RouteName,
            row.Path,
            row.ComponentKey,
            row.Title,
            row.Caption,
            row.Icon,
            row.DisplayOrder,
            row.RequiredPermission,
            row.IsSystem,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    internal static HostMenuResponse Map(IdentityNavigationRecord record) =>
        new(
            record.Id,
            record.ParentId,
            record.RouteName,
            record.Path,
            record.ComponentKey,
            record.Title,
            record.Caption,
            record.Icon,
            record.DisplayOrder,
            record.RequiredPermission,
            record.IsSystem,
            record.IsActive,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<HostMenuResponse> NotFound() =>
        Result<HostMenuResponse>.Failure(new Error(
            IdentityErrorCodes.MenuNotFound,
            "The host menu was not found.",
            ErrorType.NotFound));
}
