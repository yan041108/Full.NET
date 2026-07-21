using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageHostUsers;

/// <summary>Host 用户可分配角色读取与替换；超级管理员与系统角色由专用边界管理。</summary>
internal sealed class HostUserRolesService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<Result<HostUserRolesResponse>> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await LoadHostUserAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return NotFound();
        }

        var roleIds = await LoadAssignableRoleIdsAsync(userId, cancellationToken)
            .ConfigureAwait(false);
        return Result<HostUserRolesResponse>.Success(Map(user, roleIds));
    }

    public Task<Result<HostUserRolesResponse>> ReplaceAsync(
        Guid userId,
        ReplaceHostUserRolesRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ReplaceCoreAsync(userId, request, token),
            cancellationToken);

    private async Task<Result<HostUserRolesResponse>> ReplaceCoreAsync(
        Guid userId,
        ReplaceHostUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        var user = await LoadHostUserAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return NotFound();
        }

        var roleIds = (request.RoleIds ?? [])
            .Distinct()
            .ToArray();
        foreach (var roleId in roleIds)
        {
            var role = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                    IdentitySql.FindHostRoleById,
                    new { RoleId = roleId },
                    cancellationToken)
                .ConfigureAwait(false);
            if (role is null)
            {
                return RoleNotFound();
            }

            if (!role.IsActive || role.IsSystem || role.IsSuperAdministrator)
            {
                return RoleNotAssignable();
            }
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.UpdateHostUserRoleAssignments,
                new
                {
                    UserId = userId,
                    SecurityStamp = idGenerator.NewId().ToString("N"),
                    UpdatedAtUtc = now,
                    request.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows == 0)
        {
            return VersionConflict();
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.DeleteUserAssignableRoles,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var roleId in roleIds)
        {
            await commandExecutor.ExecuteAsync(
                    IdentitySql.EnsureUserRole,
                    new IdentityUserRole(userId, roleId),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.RevokeAllUserSessions,
                new { UserId = userId, RevokedAtUtc = now },
                cancellationToken)
            .ConfigureAwait(false);

        return await GetAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IdentityUserRecord?> LoadHostUserAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<Guid>> LoadAssignableRoleIdsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        (await queryExecutor.QueryAsync<Guid>(
                IdentitySql.GetUserAssignableRoleIds,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false)).ToArray();

    private static HostUserRolesResponse Map(
        IdentityUserRecord user,
        IReadOnlyList<Guid> roleIds) =>
        new(user.Id, roleIds, user.Version);

    private static Result<HostUserRolesResponse> NotFound() =>
        Result<HostUserRolesResponse>.Failure(new Error(
            IdentityErrorCodes.UserNotFound,
            "The host user was not found.",
            ErrorType.NotFound));

    private static Result<HostUserRolesResponse> RoleNotFound() =>
        Result<HostUserRolesResponse>.Failure(new Error(
            IdentityErrorCodes.UserRolesRoleNotFound,
            "One or more host roles were not found.",
            ErrorType.NotFound));

    private static Result<HostUserRolesResponse> RoleNotAssignable() =>
        Result<HostUserRolesResponse>.Failure(new Error(
            IdentityErrorCodes.UserRolesRoleNotAssignable,
            "One or more host roles cannot be assigned through user role management.",
            ErrorType.Validation));

    private static Result<HostUserRolesResponse> VersionConflict() =>
        Result<HostUserRolesResponse>.Failure(new Error(
            IdentityErrorCodes.ProfileVersionConflict,
            "The host user changed concurrently.",
            ErrorType.Conflict));
}
