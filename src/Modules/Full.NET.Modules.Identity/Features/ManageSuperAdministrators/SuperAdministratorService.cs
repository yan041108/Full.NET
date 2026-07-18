using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Features.ManageSuperAdministrators;

/// <summary>
/// 通过锁定唯一系统角色串行化授予和撤销，保证双数据库下最后一名保护可线性化。
/// </summary>
internal sealed class SuperAdministratorService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock,
    IIdGenerator idGenerator) : ISuperAdministratorService
{
    public Task<Result<SuperAdministratorChangeResponse>> GrantAsync(
        Guid operatorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => GrantCoreAsync(operatorUserId, targetUserId, token),
            cancellationToken);

    public Task<Result<SuperAdministratorChangeResponse>> RevokeAsync(
        Guid operatorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RevokeCoreAsync(operatorUserId, targetUserId, token),
            cancellationToken);

    private async Task<Result<SuperAdministratorChangeResponse>> GrantCoreAsync(
        Guid operatorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        var role = await LockRoleAsync(cancellationToken).ConfigureAwait(false);
        if (role is null || !role.IsSuperAdministrator || !role.IsActive)
        {
            throw new InvalidOperationException(
                "The protected super-administrator role is unavailable.");
        }

        if (!await IsActiveSuperAdministratorAsync(operatorUserId, cancellationToken)
                .ConfigureAwait(false))
        {
            return Failure(
                IdentityErrorCodes.SuperAdministratorOperatorRequired,
                "The operator is not an active super administrator.");
        }

        var targetExists = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountActiveHostUser,
                new { UserId = targetUserId },
                cancellationToken)
            .ConfigureAwait(false) > 0;
        if (!targetExists)
        {
            return Failure(
                IdentityErrorCodes.SuperAdministratorTargetNotFound,
                "The target is not an active Host account.");
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.EnsureUserRole,
                new { UserId = targetUserId, RoleId = role.Id },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows > 1)
        {
            throw new InvalidOperationException(
                "Granting the super-administrator role affected more than one row.");
        }

        if (affectedRows == 1)
        {
            await InvalidateTargetAsync(
                operatorUserId,
                targetUserId,
                "identity.super_administrator.granted",
                cancellationToken).ConfigureAwait(false);
        }

        return Result<SuperAdministratorChangeResponse>.Success(
            new SuperAdministratorChangeResponse(targetUserId, affectedRows == 1));
    }

    private async Task<Result<SuperAdministratorChangeResponse>> RevokeCoreAsync(
        Guid operatorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        var role = await LockRoleAsync(cancellationToken).ConfigureAwait(false);
        if (role is null || !role.IsSuperAdministrator || !role.IsActive)
        {
            throw new InvalidOperationException(
                "The protected super-administrator role is unavailable.");
        }

        if (!await IsActiveSuperAdministratorAsync(operatorUserId, cancellationToken)
                .ConfigureAwait(false))
        {
            return Failure(
                IdentityErrorCodes.SuperAdministratorOperatorRequired,
                "The operator is not an active super administrator.");
        }

        if (!await IsActiveSuperAdministratorAsync(targetUserId, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result<SuperAdministratorChangeResponse>.Success(
                new SuperAdministratorChangeResponse(targetUserId, false));
        }

        var activeCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountActiveSuperAdministrators,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (activeCount <= 1)
        {
            return Failure(
                IdentityErrorCodes.SuperAdministratorLastRemaining,
                "The last active super administrator cannot be revoked.");
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.DeleteSuperAdministratorAssignment,
                new { UserId = targetUserId, RoleId = role.Id },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "Revoking the super-administrator role did not affect exactly one row.");
        }

        await InvalidateTargetAsync(
            operatorUserId,
            targetUserId,
            "identity.super_administrator.revoked",
            cancellationToken).ConfigureAwait(false);
        return Result<SuperAdministratorChangeResponse>.Success(
            new SuperAdministratorChangeResponse(targetUserId, true));
    }

    private Task<IdentityRoleRecord?> LockRoleAsync(CancellationToken cancellationToken)
    {
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => IdentitySql.LockSuperAdministratorRoleSqlServer,
            DatabaseProvider.MySql => IdentitySql.LockSuperAdministratorRoleMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        return queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
            statement,
            cancellationToken: cancellationToken);
    }

    private async Task<bool> IsActiveSuperAdministratorAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await queryExecutor.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountActiveSuperAdministratorAssignment,
                new { UserId = userId },
                cancellationToken)
            .ConfigureAwait(false) > 0;

    private async Task InvalidateTargetAsync(
        Guid operatorUserId,
        Guid targetUserId,
        string eventType,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var userRows = await commandExecutor.ExecuteAsync(
                IdentitySql.RotateSecurityStamp,
                new
                {
                    UserId = targetUserId,
                    SecurityStamp = idGenerator.NewId().ToString("N"),
                    UpdatedAtUtc = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (userRows != 1)
        {
            throw new InvalidOperationException(
                "Rotating the target security stamp did not affect exactly one row.");
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.RevokeAllUserSessions,
                new { UserId = targetUserId, RevokedAtUtc = now },
                cancellationToken)
            .ConfigureAwait(false);
        var auditRows = await commandExecutor.ExecuteAsync(
                IdentitySql.InsertSuperAdministratorAudit,
                new
                {
                    Id = idGenerator.NewId(),
                    UserId = targetUserId,
                    SessionId = (Guid?)null,
                    UsernameFingerprint = TokenHash.Compute(targetUserId.ToString("N")),
                    EventType = eventType,
                    ResultCode = eventType,
                    Succeeded = true,
                    IpAddress = (string?)null,
                    UserAgent = (string?)null,
                    ContextTenantId = (Guid?)null,
                    OccurredAtUtc = now,
                    ActorUserId = operatorUserId,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (auditRows != 1)
        {
            throw new InvalidOperationException(
                "Writing the super-administrator audit did not affect exactly one row.");
        }
    }

    private static Result<SuperAdministratorChangeResponse> Failure(
        string code,
        string message) =>
        Result<SuperAdministratorChangeResponse>.Failure(new Error(
            Code: code,
            Message: message,
            Type: ErrorType.Forbidden));
}
