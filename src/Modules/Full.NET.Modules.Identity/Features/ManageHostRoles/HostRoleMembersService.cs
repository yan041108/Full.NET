using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageHostRoles;

/// <summary>Host 角色成员分页查询与整量替换。</summary>
internal sealed class HostRoleMembersService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<HostRoleMembersPageResponse>> ListAsync(
        Guid roleId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var role = await FindHostRoleAsync(roleId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return NotFoundPage();
        }

        if (!CanManageMembers(role))
        {
            return SystemLockedPage();
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountHostRoleMembers,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => IdentitySql.ListHostRoleMembersSqlServer,
            DatabaseProvider.MySql => IdentitySql.ListHostRoleMembersMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<HostRoleMemberRow>(
                statement,
                IdentitySqlParameters.Create(
                    ("RoleId", roleId),
                    ("Offset", offset),
                    ("PageSize", pageSize)),
                cancellationToken)
            .ConfigureAwait(false);
        var items = rows
            .Select(row => new HostRoleMemberResponse(
                row.UserId,
                row.Username,
                row.DisplayName,
                row.IsActive))
            .ToArray();
        return Result<HostRoleMembersPageResponse>.Success(
            new HostRoleMembersPageResponse(roleId, items, page, pageSize, total, role.Version));
    }

    public Task<Result<HostRoleMembersAssignmentResponse>> ReplaceAsync(
        Guid roleId,
        ReplaceHostRoleMembersRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ReplaceCoreAsync(roleId, request, token),
            cancellationToken);

    private async Task<Result<HostRoleMembersAssignmentResponse>> ReplaceCoreAsync(
        Guid roleId,
        ReplaceHostRoleMembersRequest request,
        CancellationToken cancellationToken)
    {
        var role = await FindHostRoleAsync(roleId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return NotFoundAssignment();
        }

        if (!CanManageMembers(role))
        {
            return SystemLockedAssignment();
        }

        if (!role.IsActive)
        {
            return SystemLockedAssignment();
        }

        var userIds = (request.UserIds ?? [])
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        foreach (var userId in userIds)
        {
            var user = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                    IdentitySql.FindHostUserById,
                    IdentitySqlParameters.Create(("UserId", userId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (user is null || !user.IsActive)
            {
                return Result<HostRoleMembersAssignmentResponse>.Failure(new Error(
                    IdentityErrorCodes.UserNotFound,
                    "One or more host users were not found.",
                    ErrorType.NotFound));
            }
        }

        var previousUserIds = (await queryExecutor.QueryAsync<Guid>(
                    IdentitySql.ListHostRoleMemberUserIds,
                    IdentitySqlParameters.Create(("RoleId", roleId)),
                    cancellationToken)
                .ConfigureAwait(false)).ToHashSet();
        var now = clock.UtcNow;
        var versionRows = await commandExecutor.ExecuteAsync(
                IdentitySql.UpdateHostRoleVersion,
                IdentitySqlParameters.Create(
                    ("RoleId", roleId),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (versionRows != 1)
        {
            return VersionConflict();
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.DeleteHostRoleMemberAssignments,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var userId in userIds)
        {
            await commandExecutor.ExecuteAsync(
                    IdentitySql.EnsureUserRole,
                    new IdentityUserRole(userId, roleId),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var affectedUserIds = previousUserIds
            .Union(userIds)
            .ToArray();
        foreach (var userId in affectedUserIds)
        {
            await commandExecutor.ExecuteAsync(
                    IdentitySql.RotateSecurityStamp,
                    IdentitySqlParameters.Create(
                        ("UserId", userId),
                        ("SecurityStamp", idGenerator.NewId().ToString("N")),
                        ("UpdatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(
                    IdentitySql.RevokeAllUserSessions,
                    IdentitySqlParameters.Create(("UserId", userId), ("RevokedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var updatedRole = await FindHostRoleAsync(roleId, cancellationToken).ConfigureAwait(false);
        return Result<HostRoleMembersAssignmentResponse>.Success(
            new HostRoleMembersAssignmentResponse(roleId, userIds, updatedRole!.Version));
    }

    private Task<IdentityRoleRecord?> FindHostRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
            IdentitySql.FindHostRoleById,
            IdentitySqlParameters.Create(("RoleId", roleId)),
            cancellationToken);

    private static bool CanManageMembers(IdentityRoleRecord role) =>
        !role.IsSystem && !role.IsSuperAdministrator;

    private static Result<HostRoleMembersPageResponse> NotFoundPage() =>
        Result<HostRoleMembersPageResponse>.Failure(new Error(
            IdentityErrorCodes.RoleNotFound,
            "The host role was not found.",
            ErrorType.NotFound));

    private static Result<HostRoleMembersPageResponse> SystemLockedPage() =>
        Result<HostRoleMembersPageResponse>.Failure(new Error(
            IdentityErrorCodes.RoleSystemLocked,
            "System roles cannot change members.",
            ErrorType.BusinessRule));

    private static Result<HostRoleMembersAssignmentResponse> NotFoundAssignment() =>
        Result<HostRoleMembersAssignmentResponse>.Failure(new Error(
            IdentityErrorCodes.RoleNotFound,
            "The host role was not found.",
            ErrorType.NotFound));

    private static Result<HostRoleMembersAssignmentResponse> SystemLockedAssignment() =>
        Result<HostRoleMembersAssignmentResponse>.Failure(new Error(
            IdentityErrorCodes.RoleSystemLocked,
            "System roles cannot change members.",
            ErrorType.BusinessRule));

    private static Result<HostRoleMembersAssignmentResponse> VersionConflict() =>
        Result<HostRoleMembersAssignmentResponse>.Failure(new Error(
            IdentityErrorCodes.ProfileVersionConflict,
            "The host role changed concurrently.",
            ErrorType.Conflict));
}
