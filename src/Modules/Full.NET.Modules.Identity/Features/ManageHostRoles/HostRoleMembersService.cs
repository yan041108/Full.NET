using System.Text.Json;
using Full.NET.Modules.Identity.Serialization;
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
        CancellationToken cancellationToken = default,
        IReadOnlyList<Guid>? userIds = null)
    {
        if (userIds is { Count: > 100 }) return Result<HostRoleMembersPageResponse>.Failure(new Error(
            ValidationErrorCodes.Failed, "At most 100 candidate users may be queried.", ErrorType.Validation));
        // 客户端以空 UUID 表示空候选；先保留原始数量预算，再解码，避免将标记送入 SQL。
        if (userIds is not null) userIds = userIds.Where(id => id != Guid.Empty).ToArray();
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
        var offset = ((long)page - 1) * pageSize;
        if (userIds is { Count: 0 }) return Result<HostRoleMembersPageResponse>.Success(new(roleId, [], page, pageSize, 0, role.Version));
        var parameters = IdentitySqlParameters.Create(("RoleId", roleId), ("Offset", offset), ("PageSize", pageSize), ("UserIds", userIds));
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                userIds is null ? IdentitySql.CountHostRoleMembers : FilteredCount,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => userIds is null ? IdentitySql.ListHostRoleMembersSqlServer : FilteredListSqlServer,
            DatabaseProvider.MySql => userIds is null ? IdentitySql.ListHostRoleMembersMySql : FilteredListMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<HostRoleMemberRow>(
                statement,
                parameters,
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
        transaction.ExecuteResultAsync(
            token => ReplaceCoreAsync(roleId, request, token),
            cancellationToken);

    private static string WithCandidatesText(SqlStatement statement) => statement.Text.Replace(
        "WHERE userRole.RoleId = @RoleId", "WHERE userRole.RoleId = @RoleId AND identityUser.Id IN @UserIds", StringComparison.Ordinal);
    private static readonly SqlStatement FilteredCount = new("identity.count_candidate_role_members",
        WithCandidatesText(IdentitySql.CountHostRoleMembers), SqlDataScope.HostOnly);
    private static readonly SqlStatement FilteredListSqlServer = new("identity.list_candidate_role_members.sql_server",
        WithCandidatesText(IdentitySql.ListHostRoleMembersSqlServer), SqlDataScope.HostOnly);
    private static readonly SqlStatement FilteredListMySql = new("identity.list_candidate_role_members.mysql",
        WithCandidatesText(IdentitySql.ListHostRoleMembersMySql), SqlDataScope.HostOnly);

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

        if (request.UserIds is null || request.UserIds.Count > 10_000 || request.UserIds.Any(id => id == Guid.Empty))
            return Result<HostRoleMembersAssignmentResponse>.Failure(new Error(ValidationErrorCodes.Failed,
                "Role members must contain at most 10000 non-empty user identifiers.", ErrorType.Validation));
        var requestedUserIds = request.UserIds.Distinct().OrderBy(id => id).ToArray();
        if (request.CandidateUserIds is { } candidates && (candidates.Count > 10_000
            || candidates.Any(id => id == Guid.Empty) || requestedUserIds.Except(candidates).Any()))
            return Result<HostRoleMembersAssignmentResponse>.Failure(new Error(ValidationErrorCodes.Failed,
                "The candidate member selection is invalid.", ErrorType.Validation));
        var userIds = requestedUserIds;
        var now = clock.UtcNow;
        // 先以角色版本取得本地写锁，串行化同一角色的成员替换；后续任何失败 Result 必须回滚该版本。
        var versionRows = await commandExecutor.ExecuteAsync(HostRoleMembersSql.UpdateRoleVersion,
            IdentitySqlParameters.Create(("RoleId", roleId), ("UpdatedAtUtc", now), ("Version", request.Version)),
            cancellationToken).ConfigureAwait(false);
        if (versionRows != 1) return VersionConflict();
        var mysql = databaseOptions.Value.Provider == DatabaseProvider.MySql;
        var previousUserIds = await queryExecutor.QueryAsync<Guid>(IdentitySql.ListHostRoleMemberUserIds,
            IdentitySqlParameters.Create(("RoleId", roleId)), cancellationToken).ConfigureAwait(false);
        if (request.CandidateUserIds is { } candidateIds)
            userIds = previousUserIds.Except(candidateIds).Union(requestedUserIds).OrderBy(id => id).ToArray();
        if (userIds.Length > 10_000) return Result<HostRoleMembersAssignmentResponse>.Failure(new Error(
            ValidationErrorCodes.Failed, "Role members exceed the 10000 member budget.", ErrorType.Validation));
        var parameters = IdentitySqlParameters.Create(("RoleId", roleId),
            ("UserIdsJson", JsonSerializer.Serialize(userIds, IdentityJsonSerializerContext.Default.GuidArray)));
        var affectedIds = previousUserIds.Union(userIds).ToArray();
        // 不同角色也可能共享用户：先按数据库一致顺序锁定完整新旧并集，禁止只锁新成员后反向补锁旧成员。
        var locks = IdentitySqlParameters.Create(("UserIdsJson", JsonSerializer.Serialize(affectedIds,
            IdentityJsonSerializerContext.Default.GuidArray)));
        var users = await queryExecutor.QueryAsync<HostRoleMemberUserState>(mysql ? HostRoleMembersSql.ValidateUsersMySql : HostRoleMembersSql.ValidateUsersSqlServer,
            locks, cancellationToken).ConfigureAwait(false);
        var activeIds = users.Where(user => user.IsActive).Select(user => user.Id).ToHashSet();
        if (requestedUserIds.Any(id => !activeIds.Contains(id)))
            return Result<HostRoleMembersAssignmentResponse>.Failure(new Error(IdentityErrorCodes.UserNotFound,
                "One or more host users were not found.", ErrorType.NotFound));
        await commandExecutor.ExecuteAsync(IdentitySql.DeleteHostRoleMemberAssignments, parameters, cancellationToken).ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(mysql ? HostRoleMembersSql.InsertMembersMySql : HostRoleMembersSql.InsertMembersSqlServer,
            parameters, cancellationToken).ConfigureAwait(false);
        // 新旧并集全部撤销；即使成员没有变化，也不省略既有安全戳轮换或会话撤销语义。
        // 查询已按提供程序原生主键顺序锁定用户；沿用该顺序，不在业务模块引用 ADO.NET 排序类型。
        // 缺失的旧用户仍保留在撤销集合中，不能因验证投影未返回而缩小既有撤销范围。
        var lockedIds = users.Select(user => user.Id).ToArray();
        foreach (var chunk in lockedIds.Concat(affectedIds.Except(lockedIds)).Chunk(1000))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stamps = chunk.Select(id => new HostRoleMemberStamp(id, idGenerator.NewId().ToString("N"))).ToArray();
            var revoke = IdentitySqlParameters.Create(("Now", now),
                ("UserStampsJson", JsonSerializer.Serialize(stamps, IdentityJsonSerializerContext.Default.HostRoleMemberStampArray)));
            await commandExecutor.ExecuteAsync(mysql ? HostRoleMembersSql.RotateStampsMySql : HostRoleMembersSql.RotateStampsSqlServer,
                revoke, cancellationToken).ConfigureAwait(false);
            await commandExecutor.ExecuteAsync(mysql ? HostRoleMembersSql.RevokeSessionsMySql : HostRoleMembersSql.RevokeSessionsSqlServer,
                revoke, cancellationToken).ConfigureAwait(false);
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
