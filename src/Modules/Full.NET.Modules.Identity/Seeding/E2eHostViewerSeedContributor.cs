using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Full.NET.Seeding.Abstractions;
using Microsoft.Extensions.Options;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.Modules.Identity.Seeding;

/// <summary>
/// 为真实栈 E2E 提供权限受限的 Host 查看者，验证 API 级 403 与导航裁剪。
/// </summary>
internal sealed class E2eHostViewerSeedContributor(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser> passwordHasher,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<IdentityOptions> options) : IDataSeedContributor
{
    private const string HostScope = "host";
    private const string RoleCode = "e2e-host-viewer";
    private const string RoleName = "E2E 受限查看者";

    private static readonly string[] ViewerPermissions =
    [
        IdentityAuthorizationContributor.DashboardRead,
        IdentityAuthorizationContributor.NavigationRead,
        "tenancy.tenants.read",
        "tenancy.tenants.switch",
    ];

    public string Name => "identity.e2e_host_viewer";

    public int Version => 1;

    public IReadOnlySet<SeedProfile> Profiles { get; } =
        new HashSet<SeedProfile> { SeedProfile.Development };

    public IReadOnlyCollection<string> Dependencies { get; } =
        ["identity.host_administrator"];

    public async Task<SeedContributionResult> SeedAsync(
        SeedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var viewer = options.Value.E2eViewer;
        if (string.IsNullOrWhiteSpace(viewer.Password))
        {
            return new SeedContributionResult(0, 0, 1, "seeding.data.skipped");
        }

        var username = viewer.Username.Trim();
        var displayName = string.IsNullOrWhiteSpace(viewer.DisplayName)
            ? "E2E 受限查看者"
            : viewer.DisplayName.Trim();
        var passwordViolations = IdentityPasswordPolicy.Validate(viewer.Password);
        if (username.Length is < 3 or > 128
            || displayName.Length is < 1 or > 128
            || passwordViolations.Count > 0)
        {
            throw new SeedContributionException(
                IdentityErrorCodes.BootstrapInvalidProfile);
        }

        return await transaction.ExecuteAsync(
            token => SeedCoreAsync(username, displayName, viewer.Password, token),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<SeedContributionResult> SeedCoreAsync(
        string username,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = username.ToUpperInvariant();
        var now = clock.UtcNow;
        var created = 0;
        var updated = 0;

        var role = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindRoleByScopeAndCode,
                new { ScopeKey = HostScope, Code = RoleCode },
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
                        RoleCode,
                        RoleName,
                        true,
                        true,
                        false,
                        now,
                        null,
                        1),
                    cancellationToken)
                .ConfigureAwait(false);
            created++;
        }
        else
        {
            roleId = role.Id;
        }

        foreach (var permission in ViewerPermissions)
        {
            var affected = await commandExecutor.ExecuteAsync(
                    IdentitySql.EnsureRolePermission,
                    new IdentityRolePermission(roleId, permission),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected == 1)
            {
                updated++;
            }
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                new { ScopeKey = HostScope, NormalizedUsername = normalizedUsername },
                cancellationToken)
            .ConfigureAwait(false);
        Guid userId;
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
                    new IdentityUserRecord(
                        user.Id,
                        user.TenantId,
                        user.ScopeKey,
                        user.Username,
                        user.NormalizedUsername,
                        user.DisplayName,
                        user.PasswordHash,
                        user.IsActive,
                        user.FailedLoginCount,
                        user.LockoutEndUtc,
                        user.SecurityStamp,
                        user.CreatedAtUtc,
                        user.UpdatedAtUtc,
                        user.Version,
                        user.PreferredLocale,
                        user.ProfileVersion),
                    cancellationToken)
                .ConfigureAwait(false);
            userId = user.Id;
            created++;
        }
        else
        {
            userId = existing.Id;
        }

        var userRoleAffected = await commandExecutor.ExecuteAsync(
                IdentitySql.EnsureUserRole,
                new IdentityUserRole(userId, roleId),
                cancellationToken)
            .ConfigureAwait(false);
        if (userRoleAffected == 1)
        {
            updated++;
        }

        if (created > 0)
        {
            return new SeedContributionResult(created, updated, 0, "seeding.data.created");
        }

        return updated > 0
            ? new SeedContributionResult(0, updated, 0, "seeding.data.updated")
            : new SeedContributionResult(0, 0, 1, "seeding.data.skipped");
    }

    private async Task RequireExactlyOneAsync(
        SqlStatement statement,
        object parameters,
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
                $"Expected exactly one affected row for {statement.Name}, got {affectedRows}.");
        }
    }
}
