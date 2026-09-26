using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageTenantMembers;

internal sealed class TenantMembershipQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<PagedResult<TenantMemberResponse>>> ListMembersAsync(
        int page,
        int pageSize,
        string? status,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var parameters = Identity.Persistence.IdentitySqlParameters.Create(
            ("Status", NormalizeFilter(status)),
            ("Offset", offset),
            ("PageSize", pageSize));
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                TenantMembershipSql.CountMembers,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => TenantMembershipSql.ListMembersSqlServer,
            DatabaseProvider.MySql => TenantMembershipSql.ListMembersMySql,
            _ => throw new InvalidOperationException("Unsupported database provider."),
        };
        var rows = await queryExecutor.QueryAsync<TenantMemberListRow>(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(MapMember).ToArray();
        return Result<PagedResult<TenantMemberResponse>>.Success(
            new PagedResult<TenantMemberResponse>(items, page, pageSize, total));
    }

    public async Task<Result<TenantMemberResponse>> GetMemberByIdAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<TenantMemberListRow>(
                TenantMembershipSql.FindMemberById,
                Identity.Persistence.IdentitySqlParameters.Create(("MemberId", memberId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return MemberNotFound();
        }

        return Result<TenantMemberResponse>.Success(MapMember(row));
    }

    public async Task<Result<TenantMemberResponse>> GetCurrentMemberAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var row = await queryExecutor.QuerySingleOrDefaultAsync<TenantMemberListRow>(
                TenantMembershipSql.FindMemberListRowByTenantAndUser,
                Identity.Persistence.IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return MemberNotFound();
        }

        return Result<TenantMemberResponse>.Success(MapMember(row));
    }

    public async Task<Result<PagedResult<TenantInvitationResponse>>> ListInvitationsAsync(
        int page,
        int pageSize,
        string? status,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var parameters = Identity.Persistence.IdentitySqlParameters.Create(
            ("Status", NormalizeFilter(status)),
            ("Offset", offset),
            ("PageSize", pageSize));
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                TenantMembershipSql.CountInvitations,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => TenantMembershipSql.ListInvitationsSqlServer,
            DatabaseProvider.MySql => TenantMembershipSql.ListInvitationsMySql,
            _ => throw new InvalidOperationException("Unsupported database provider."),
        };
        var rows = await queryExecutor.QueryAsync<TenantInvitationRecord>(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows.Select(MapInvitation).ToArray();
        return Result<PagedResult<TenantInvitationResponse>>.Success(
            new PagedResult<TenantInvitationResponse>(items, page, pageSize, total));
    }

    private static TenantMemberResponse MapMember(TenantMemberListRow row) =>
        new(row.Id, row.TenantId, row.UserId, row.Username, row.DisplayName,
            row.MemberRole, row.Status, row.CreatedAtUtc, row.UpdatedAtUtc, row.Version);

    private static TenantInvitationResponse MapInvitation(TenantInvitationRecord row) =>
        new(row.Id, row.TenantId, row.TargetEmail, row.TargetUserId, row.InvitedByUserId,
            row.MemberRole, row.Status, row.ExpiresAtUtc, row.CreatedAtUtc, row.UpdatedAtUtc, row.Version);

    private static string? NormalizeFilter(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static Result<TenantMemberResponse> MemberNotFound() =>
        Result<TenantMemberResponse>.Failure(new Error(
            IdentityErrorCodes.TenantMemberNotFound,
            "The tenant member was not found.",
            ErrorType.NotFound));
}
