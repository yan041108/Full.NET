using System.Text.Json;
using Microsoft.Extensions.Options;
using Full.NET.Modules.Identity.Serialization;
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
    IIdGenerator idGenerator, IOptions<DatabaseOptions>? databaseOptions = null)
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
        transaction.ExecuteResultAsync(
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

        if (request.RoleIds is { Count: > 1000 } || request.RoleIds?.Any(id => id == Guid.Empty) == true)
            return RoleNotAssignable();
        var roleIds = (request.RoleIds ?? []).Distinct().ToArray();
        var previousRoleIds = await LoadAssignableRoleIdsAsync(userId, cancellationToken).ConfigureAwait(false);
        var affectedRoleIds = previousRoleIds.Union(roleIds).ToArray();
        if (affectedRoleIds.Length > 10000) return RoleNotAssignable();
        var roleParameters = IdentitySqlParameters.Create(("RoleIdsJson", JsonSerializer.Serialize(affectedRoleIds,
            IdentityJsonSerializerContext.Default.GuidArray)), ("UpdatedAtUtc", clock.UtcNow));
        if (affectedRoleIds.Length > 0)
        {
            // 与角色成员入口统一先锁角色、再锁用户，原生主键顺序避免多个角色反向争锁。
            var roles = await queryExecutor.QueryAsync<IdentityRoleRecord>(databaseOptions?.Value.Provider == DatabaseProvider.MySql
                ? LockRolesMySql : LockRolesSqlServer, roleParameters, cancellationToken).ConfigureAwait(false);
            var roleMap = roles.ToDictionary(role => role.Id);
            foreach (var roleId in roleIds)
            {
                if (!roleMap.TryGetValue(roleId, out var role)) return RoleNotFound();
                if (!role.IsActive || role.IsSystem || role.IsSuperAdministrator) return RoleNotAssignable();
            }
            // 用户侧赋权也推进角色版本，使候选页保存能够检测其他入口的成员变化。
            await commandExecutor.ExecuteAsync(databaseOptions?.Value.Provider == DatabaseProvider.MySql
                ? TouchRolesMySql : TouchRolesSqlServer, roleParameters, cancellationToken).ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.UpdateHostUserRoleAssignments,
                IdentitySqlParameters.Create(
                    ("UserId", userId),
                    ("SecurityStamp", idGenerator.NewId().ToString("N")),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows == 0)
        {
            return VersionConflict();
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.DeleteUserAssignableRoles,
                IdentitySqlParameters.Create(("UserId", userId)),
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
                IdentitySqlParameters.Create(("UserId", userId), ("RevokedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        return await GetAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    private const string RoleColumns = "Id,TenantId,ScopeKey,Code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,UpdatedAtUtc,Version";
    private const string SqlServerRolePredicate = "Id IN (SELECT Id FROM OPENJSON(@RoleIdsJson) WITH (Id uniqueidentifier '$')) AND ScopeKey='host' AND TenantId IS NULL";
    private const string MySqlRolePredicate = "Id IN (SELECT UUID_TO_BIN(j.Id,0) FROM JSON_TABLE(@RoleIdsJson,'$[*]' COLUMNS(Id CHAR(36) PATH '$')) j) AND ScopeKey='host' AND TenantId IS NULL";
    internal static readonly SqlStatement LockRolesSqlServer = new("identity.user_roles.lock_roles.sql_server",
        "SELECT " + RoleColumns + " FROM fn_identity_role WITH (UPDLOCK) WHERE " + SqlServerRolePredicate + " ORDER BY Id", SqlDataScope.HostOnly);
    internal static readonly SqlStatement LockRolesMySql = new("identity.user_roles.lock_roles.mysql",
        "SELECT " + RoleColumns + " FROM fn_identity_role WHERE " + MySqlRolePredicate + " ORDER BY Id FOR UPDATE", SqlDataScope.HostOnly);
    internal static readonly SqlStatement TouchRolesSqlServer = new("identity.user_roles.touch_roles.sql_server",
        "UPDATE fn_identity_role SET Version=Version+1,UpdatedAtUtc=@UpdatedAtUtc WHERE " + SqlServerRolePredicate + " AND IsSystem=0 AND IsSuperAdministrator=0", SqlDataScope.HostOnly);
    internal static readonly SqlStatement TouchRolesMySql = new("identity.user_roles.touch_roles.mysql",
        "UPDATE fn_identity_role SET Version=Version+1,UpdatedAtUtc=@UpdatedAtUtc WHERE " + MySqlRolePredicate + " AND IsSystem=0 AND IsSuperAdministrator=0", SqlDataScope.HostOnly);

    private async Task<IdentityUserRecord?> LoadHostUserAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindHostUserById,
                IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<IReadOnlyList<Guid>> LoadAssignableRoleIdsAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        (await queryExecutor.QueryAsync<Guid>(
                IdentitySql.GetUserAssignableRoleIds,
                IdentitySqlParameters.Create(("UserId", userId)),
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
