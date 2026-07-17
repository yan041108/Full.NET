using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Identity.Authorization;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Features.Bootstrap;

internal sealed class IdentityBootstrapService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser> passwordHasher,
    IClock clock,
    IIdGenerator idGenerator,
    AuthorizationCatalog authorizationCatalog) : IIdentityBootstrapService
{
    private const string HostScope = "host";
    private const string HostAdministratorRoleCode = "host-administrator";
    private const string HostAdministratorRoleName = "宿主管理员";

    public Task<Result<BootstrapHostAdminResult>> BootstrapHostAdminAsync(
        BootstrapHostAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var passwordViolations = IdentityPasswordPolicy.Validate(request.Password);
        if (passwordViolations.Count > 0)
        {
            return Task.FromResult(Result<BootstrapHostAdminResult>.Failure(new Error(
                Code: IdentityErrorCodes.BootstrapInvalidPassword,
                DefaultMessage: "The bootstrap password does not satisfy the password policy.",
                Type: ErrorType.Validation,
                ValidationErrors: new Dictionary<string, string[]>
                {
                    [nameof(request.Password)] = passwordViolations.ToArray(),
                },
                ValidationViolations:
                [
                    new ValidationViolation(
                        nameof(request.Password),
                        IdentityErrorCodes.BootstrapInvalidPassword,
                        new Dictionary<string, object?>()),
                ])));
        }

        var username = request.Username?.Trim() ?? string.Empty;
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (username.Length is < 3 or > 128 || displayName.Length is < 1 or > 128)
        {
            return Task.FromResult(Result<BootstrapHostAdminResult>.Failure(new Error(
                Code: IdentityErrorCodes.BootstrapInvalidProfile,
                DefaultMessage: "Bootstrap username or display name is invalid.",
                Type: ErrorType.Validation)));
        }

        return transaction.ExecuteAsync(
            token => BootstrapCoreAsync(
                username,
                displayName,
                request.Password,
                token),
            cancellationToken);
    }

    private async Task<Result<BootstrapHostAdminResult>> BootstrapCoreAsync(
        string username,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = username.ToUpperInvariant();
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                new { ScopeKey = HostScope, NormalizedUsername = normalizedUsername },
                cancellationToken)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
        var created = existing is null;
        var userId = existing?.Id;
        if (existing is null)
        {
            var user = new IdentityUser(
                idGenerator.NewId(),
                null,
                HostScope,
                username,
                normalizedUsername,
                displayName,
                string.Empty,
                true,
                0,
                null,
                idGenerator.NewId().ToString("N"),
                now,
                null,
                1);
            user = user with { PasswordHash = passwordHasher.HashPassword(user, password) };

            await RequireExactlyOneAsync(
                    IdentitySql.InsertUser,
                    user,
                    "user insert",
                    cancellationToken)
                .ConfigureAwait(false);
            userId = user.Id;
        }

        await SynchronizeAuthorizationAsync(
                userId!.Value,
                now,
                cancellationToken)
            .ConfigureAwait(false);

        return Result<BootstrapHostAdminResult>.Success(
            new BootstrapHostAdminResult(userId.Value, created, true));
    }

    private async Task SynchronizeAuthorizationAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var role = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindRoleByScopeAndCode,
                new { ScopeKey = HostScope, Code = HostAdministratorRoleCode },
                cancellationToken)
            .ConfigureAwait(false);
        Guid roleId;
        if (role is null)
        {
            roleId = idGenerator.NewId();
            await RequireExactlyOneAsync(
                    IdentitySql.InsertRole,
                    new InsertIdentityRole(
                        roleId,
                        null,
                        HostScope,
                        HostAdministratorRoleCode,
                        HostAdministratorRoleName,
                        true,
                        true,
                        now,
                        null,
                        1),
                    "role insert",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            roleId = role.Id;
            if (!role.IsSystem
                || !role.IsActive
                || !string.Equals(
                    role.Name,
                    HostAdministratorRoleName,
                    StringComparison.Ordinal))
            {
                await RequireExactlyOneAsync(
                        IdentitySql.UpdateSystemRole,
                        new UpdateIdentitySystemRole(
                            role.Id,
                            HostAdministratorRoleName,
                            now,
                            role.Version),
                        "role update",
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var existingPermissions = (await queryExecutor.QueryAsync<string>(
                IdentitySql.GetRolePermissionCodes,
                new { RoleId = roleId },
                cancellationToken)
            .ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var hostPermissions = authorizationCatalog.Permissions
            .Where(permission =>
                (permission.Scope & AuthorizationScope.Host) != 0)
            .Select(permission => permission.Code);
        foreach (var permissionCode in hostPermissions)
        {
            if (existingPermissions.Contains(permissionCode))
            {
                continue;
            }

            await RequireZeroOrOneAsync(
                    IdentitySql.EnsureRolePermission,
                    new IdentityRolePermission(roleId, permissionCode),
                    "role permission synchronization",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await RequireZeroOrOneAsync(
                IdentitySql.EnsureUserRole,
                new IdentityUserRole(userId, roleId),
                "user role synchronization",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RequireExactlyOneAsync(
        SqlStatement statement,
        object parameters,
        string operation,
        CancellationToken cancellationToken)
    {
        var affectedRows = await commandExecutor.ExecuteAsync(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Identity bootstrap {operation} affected {affectedRows} rows instead of one.");
        }
    }

    private async Task RequireZeroOrOneAsync(
        SqlStatement statement,
        object parameters,
        string operation,
        CancellationToken cancellationToken)
    {
        var affectedRows = await commandExecutor.ExecuteAsync(
                statement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows is < 0 or > 1)
        {
            throw new InvalidOperationException(
                $"Identity bootstrap {operation} affected an invalid number of rows: {affectedRows}.");
        }
    }
}
