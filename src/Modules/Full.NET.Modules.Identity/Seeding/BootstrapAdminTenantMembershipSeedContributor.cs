using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageTenantMembers;
using Full.NET.Modules.Identity.Features.ManageTenantMembers.Persistence;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Seeding.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Seeding;

/// <summary>
/// 将 Bootstrap 宿主管理员登记为各活动租户的活动成员，便于 Baseline 环境完成组织隶属等租户内操作。
/// </summary>
internal sealed class BootstrapAdminTenantMembershipSeedContributor(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICurrentTenantContextWriter currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<IdentityOptions> options) : IDataSeedContributor
{
    private const string HostScope = "host";

    public string Name => "identity.bootstrap_admin_tenant_membership";

    public int Version => 1;

    public IReadOnlySet<SeedProfile> Profiles { get; } =
        new HashSet<SeedProfile> { SeedProfile.Baseline };

    public IReadOnlyCollection<string> Dependencies { get; } =
        ["identity.host_administrator"];

    public async Task<SeedContributionResult> SeedAsync(
        SeedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bootstrapUsername = options.Value.Bootstrap.Username?.Trim();
        if (string.IsNullOrWhiteSpace(bootstrapUsername))
        {
            return new SeedContributionResult(0, 0, 1, "seeding.data.skipped");
        }

        var previousTenant = CaptureTenantSnapshot(currentTenant);
        try
        {
            currentTenant.SetHost();
            var normalizedUsername = bootstrapUsername.ToUpperInvariant();
            var adminUser = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                    IdentitySql.FindUserByScopeAndUsername,
                    IdentitySqlParameters.Create(
                        ("ScopeKey", HostScope),
                        ("NormalizedUsername", normalizedUsername)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (adminUser is null || !adminUser.IsActive)
            {
                return new SeedContributionResult(0, 0, 1, "seeding.data.skipped");
            }

            var tenants = await queryExecutor.QueryAsync<ActiveTenantSeedRow>(
                    IdentityBaselineSeedSql.ListActiveTenants,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var created = 0;
            var updated = 0;
            var skipped = 0;
            foreach (var tenant in tenants)
            {
                currentTenant.SetTenant(new TenantContext(
                    tenant.Id,
                    tenant.Identifier,
                    tenant.Name));

                var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantMemberRecord>(
                        TenantMembershipSql.FindMemberByTenantAndUser,
                        IdentitySqlParameters.Create(("UserId", adminUser.Id)),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (existing is { Status: TenantMemberStatuses.Active })
                {
                    skipped++;
                    continue;
                }

                var now = clock.UtcNow;
                if (existing is null)
                {
                    await commandExecutor.ExecuteAsync(
                            TenantMembershipSql.InsertMember,
                            IdentitySqlParameters.Create(
                                ("Id", idGenerator.NewId()),
                                ("UserId", adminUser.Id),
                                ("MemberRole", TenantMemberRoles.Admin),
                                ("Status", TenantMemberStatuses.Active),
                                ("CreatedAtUtc", now),
                                ("UpdatedAtUtc", now),
                                ("Version", 1)),
                            cancellationToken)
                        .ConfigureAwait(false);
                    created++;
                    continue;
                }

                await commandExecutor.ExecuteAsync(
                        TenantMembershipSql.UpdateMember,
                        IdentitySqlParameters.Create(
                            ("MemberId", existing.Id),
                            ("MemberRole", TenantMemberRoles.Admin),
                            ("Status", TenantMemberStatuses.Active),
                            ("UpdatedAtUtc", now),
                            ("Version", existing.Version)),
                        cancellationToken)
                    .ConfigureAwait(false);
                updated++;
            }

            if (created > 0)
            {
                return new SeedContributionResult(created, updated, skipped, "seeding.data.created");
            }

            return updated > 0
                ? new SeedContributionResult(0, updated, skipped, "seeding.data.updated")
                : new SeedContributionResult(0, 0, skipped, "seeding.data.skipped");
        }
        finally
        {
            RestoreTenantSnapshot(currentTenant, previousTenant);
        }
    }

    private static (bool IsHost, TenantContext? Tenant) CaptureTenantSnapshot(
        ICurrentTenantContextWriter currentTenant) =>
        (currentTenant.IsHost, currentTenant.IsHost || currentTenant.Id is null
            ? null
            : new TenantContext(
                currentTenant.Id!.Value,
                currentTenant.Identifier ?? string.Empty,
                currentTenant.Name ?? string.Empty));

    private static void RestoreTenantSnapshot(
        ICurrentTenantContextWriter currentTenant,
        (bool IsHost, TenantContext? Tenant) snapshot)
    {
        if (snapshot.IsHost)
        {
            currentTenant.SetHost();
            return;
        }

        if (snapshot.Tenant is not null)
        {
            currentTenant.SetTenant(snapshot.Tenant);
            return;
        }

        currentTenant.Clear();
    }

    private sealed record ActiveTenantSeedRow(Guid Id, string Identifier, string Name);
}
