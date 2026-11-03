using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserUnits;

/// <summary>隶属详情与响应组装；Identity 展示目录只允许在命令事务外调用。</summary>
internal sealed class TenantUserUnitQueryService(
    IQueryExecutor queryExecutor,
    IHostUserDisplayDirectory hostUserDirectory)
{
    public async Task<Result<OrganizationUserUnitResponse>> GetByIdAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        var row = await FindRowAsync(assignmentId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return NotFound();
        }

        var users = await hostUserDirectory.FindHostUsersAsync(
                [row.UserId],
                cancellationToken)
            .ConfigureAwait(false);
        return users.TryGetValue(row.UserId, out var user)
            ? Result<OrganizationUserUnitResponse>.Success(Map(row, user))
            : NotFound();
    }

    /// <summary>仅回读本模块数据，使用事务外取得的展示快照组装响应，不调用 Identity。</summary>
    internal async Task<Result<OrganizationUserUnitResponse>> GetLocalByIdAsync(
        Guid assignmentId, HostUserDirectoryEntry user, CancellationToken cancellationToken)
    {
        var row = await FindRowAsync(assignmentId, cancellationToken).ConfigureAwait(false);
        return row is not null && row.UserId == user.Id
            ? Result<OrganizationUserUnitResponse>.Success(Map(row, user))
            : NotFound();
    }

    private Task<OrganizationUserUnitListRow?> FindRowAsync(
        Guid assignmentId, CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<OrganizationUserUnitListRow>(
            OrganizationSql.FindUserUnitById,
            OrganizationSqlParameters.Create(("AssignmentId", assignmentId)),
            cancellationToken);

    internal static OrganizationUserUnitResponse Map(
        OrganizationUserUnitListRow row,
        HostUserDirectoryEntry user) =>
        new(
            row.Id,
            row.UserId,
            user.Username,
            user.DisplayName,
            row.UnitId,
            row.UnitCode,
            row.UnitName,
            row.IsPrimary,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    private static Result<OrganizationUserUnitResponse> NotFound() =>
        Result<OrganizationUserUnitResponse>.Failure(new Error(
            OrganizationErrorCodes.UserUnitNotFound,
            "The organization user assignment was not found.",
            ErrorType.NotFound));
}
