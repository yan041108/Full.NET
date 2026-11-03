using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserPositions;

/// <summary>隶属详情与响应组装；Identity 展示目录只允许在命令事务外调用。</summary>
internal sealed class TenantUserPositionQueryService(
    IQueryExecutor queryExecutor,
    IHostUserDisplayDirectory hostUserDirectory)
{
    public async Task<Result<OrganizationUserPositionResponse>> GetByIdAsync(
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
            ? Result<OrganizationUserPositionResponse>.Success(Map(row, user))
            : NotFound();
    }

    /// <summary>仅回读本模块数据，使用事务外取得的展示快照组装响应，不调用 Identity。</summary>
    internal async Task<Result<OrganizationUserPositionResponse>> GetLocalByIdAsync(
        Guid assignmentId, HostUserDirectoryEntry user, CancellationToken cancellationToken)
    {
        var row = await FindRowAsync(assignmentId, cancellationToken).ConfigureAwait(false);
        return row is not null && row.UserId == user.Id
            ? Result<OrganizationUserPositionResponse>.Success(Map(row, user))
            : NotFound();
    }

    private Task<OrganizationUserPositionListRow?> FindRowAsync(
        Guid assignmentId, CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<OrganizationUserPositionListRow>(
            OrganizationSql.FindUserPositionById,
            OrganizationSqlParameters.Create(("AssignmentId", assignmentId)),
            cancellationToken);

    internal static OrganizationUserPositionResponse Map(
        OrganizationUserPositionListRow row,
        HostUserDirectoryEntry user) =>
        new(
            row.Id,
            row.UserId,
            user.Username,
            user.DisplayName,
            row.PositionId,
            row.PositionCode,
            row.PositionName,
            row.IsPrimary,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    private static Result<OrganizationUserPositionResponse> NotFound() =>
        Result<OrganizationUserPositionResponse>.Failure(new Error(
            OrganizationErrorCodes.UserPositionNotFound,
            "The organization user position assignment was not found.",
            ErrorType.NotFound));
}
