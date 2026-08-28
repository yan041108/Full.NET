using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageHostMenus;

/// <summary>Host 菜单分页列表、全量列表与详情只读查询。</summary>
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
                IdentitySqlParameters.Create(("Offset", offset), ("PageSize", pageSize)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<HostMenuResponse>>.Success(
            new PagedResult<HostMenuResponse>(items, page, pageSize, total));
    }

    public async Task<Result<IReadOnlyList<HostMenuResponse>>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await queryExecutor.QueryAsync<HostMenuListRow>(
                IdentitySql.ListAllHostMenus,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<HostMenuResponse>>.Success(
            rows.Select(Map).ToArray());
    }

    public async Task<Result<HostMenuResponse>> GetByIdAsync(
        Guid menuId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityNavigationRecord>(
                IdentitySql.FindHostMenuById,
                IdentitySqlParameters.Create(("MenuId", menuId)),
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
            row.Version,
            NormalizeMenuType(row.MenuType),
            row.Redirect,
            row.LinkUrl,
            row.IsHidden,
            row.IsKeepAlive,
            row.IsAffix,
            row.IsEmbedded,
            row.Remark);

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
            record.Version,
            NormalizeMenuType(record.MenuType),
            record.Redirect,
            record.LinkUrl,
            record.IsHidden,
            record.IsKeepAlive,
            record.IsAffix,
            record.IsEmbedded,
            record.Remark);

    private static string NormalizeMenuType(string? menuType)
    {
        if (string.Equals(menuType, IdentityHostMenuTypes.Directory, StringComparison.Ordinal))
        {
            return IdentityHostMenuTypes.Directory;
        }

        if (string.Equals(menuType, IdentityHostMenuTypes.Button, StringComparison.Ordinal))
        {
            return IdentityHostMenuTypes.Button;
        }

        return IdentityHostMenuTypes.Menu;
    }

    private static Result<HostMenuResponse> NotFound() =>
        Result<HostMenuResponse>.Failure(new Error(
            IdentityErrorCodes.MenuNotFound,
            "The host menu was not found.",
            ErrorType.NotFound));
}
