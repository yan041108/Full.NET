using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;
using Full.NET.Modules.Tenancy.Persistence;
using Full.NET.Seeding.Abstractions;

namespace Full.NET.Modules.Tenancy.Seeding;

/// <summary>
/// Baseline Profile 播种：登记 Host 侧功能权益目录机器码，供 Enforced 阶段跨模块门禁与回填引用。
/// </summary>
internal sealed class TenancyEntitlementCatalogBaselineSeedContributor(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IIdGenerator idGenerator) : IDataSeedContributor
{
    public string Name => "tenancy.entitlement_catalog_baseline";

    public int Version => 1;

    public IReadOnlySet<SeedProfile> Profiles { get; } =
        new HashSet<SeedProfile> { SeedProfile.Baseline };

    public IReadOnlyCollection<string> Dependencies { get; } = [];

    public async Task<SeedContributionResult> SeedAsync(
        SeedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var created = 0;
        var skipped = 0;
        foreach (var entry in BaselineCatalogEntries)
        {
            var inserted = await EnsureCatalogEntryAsync(entry, cancellationToken).ConfigureAwait(false);
            if (inserted)
            {
                created++;
            }
            else
            {
                skipped++;
            }
        }

        return created > 0
            ? new SeedContributionResult(created, 0, skipped, "seeding.data.created")
            : new SeedContributionResult(0, 0, skipped, "seeding.data.skipped");
    }

    private async Task<bool> EnsureCatalogEntryAsync(
        BaselineCatalogEntry entry,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantEntitlementCatalogRecord>(
                TenantEntitlementSql.FindCatalogByCode,
                TenancySqlParameters.Create(("Code", entry.Code)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return false;
        }

        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                TenantEntitlementSql.InsertCatalog,
                TenancySqlParameters.Create(
                    ("Id", idGenerator.NewId()),
                    ("Code", entry.Code),
                    ("Name", entry.Name),
                    ("Description", entry.Description),
                    ("EntitlementType", entry.EntitlementType),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private static readonly BaselineCatalogEntry[] BaselineCatalogEntries =
    [
        new(
            TenantEntitlementCatalogCodes.CompatibilityBaseline,
            "Compatibility Baseline",
            "Default compatibility-phase entitlement binding target.",
            TenantEntitlementTypes.Feature),
        new(
            TenantEntitlementCatalogCodes.Workflow,
            "Workflow",
            "Tenant-scoped workflow instance start and runtime features.",
            TenantEntitlementTypes.Feature),
    ];

    private sealed record BaselineCatalogEntry(
        string Code,
        string Name,
        string Description,
        string EntitlementType);
}
