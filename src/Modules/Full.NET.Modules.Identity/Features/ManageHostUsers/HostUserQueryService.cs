using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

/// <summary>Host 用户分页列表与详情只读查询。</summary>
internal sealed class HostUserQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<HostUserResponse>>> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountHostUsers,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => IdentitySql.ListHostUsersSqlServer,
            DatabaseProvider.MySql => IdentitySql.ListHostUsersMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<HostUserListRow>(
                statement,
                new { Offset = offset, PageSize = pageSize },
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(Map).ToArray();
        return Result<PagedResult<HostUserResponse>>.Success(
            new PagedResult<HostUserResponse>(items, page, pageSize, total));
    }

    public async Task<Result<HostUserResponse>> GetByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return Result<HostUserResponse>.Success(Map(record));
    }

    private static HostUserResponse Map(HostUserListRow row) =>
        new(
            row.Id,
            row.Username,
            row.DisplayName,
            row.IsActive,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    private static HostUserResponse Map(IdentityUserRecord record) =>
        new(
            record.Id,
            record.Username,
            record.DisplayName,
            record.IsActive,
            record.CreatedAtUtc,
            record.UpdatedAtUtc,
            record.Version);

    private static Result<HostUserResponse> NotFound() =>
        Result<HostUserResponse>.Failure(new Error(
            IdentityErrorCodes.UserNotFound,
            "The host user was not found.",
            ErrorType.NotFound));
}
