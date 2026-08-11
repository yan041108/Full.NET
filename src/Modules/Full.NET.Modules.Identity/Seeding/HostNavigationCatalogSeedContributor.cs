using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Full.NET.Seeding.Abstractions;

namespace Full.NET.Modules.Identity.Seeding;

internal sealed class HostNavigationCatalogSeedContributor(
    HostNavigationCatalogSyncService catalogSyncService) : IDataSeedContributor
{
    public string Name => "identity.host_navigation_catalog";

    public int Version => 1;

    public IReadOnlySet<SeedProfile> Profiles { get; } =
        new HashSet<SeedProfile> { SeedProfile.Baseline };

    public IReadOnlyCollection<string> Dependencies { get; } = [];

    public async Task<SeedContributionResult> SeedAsync(
        SeedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var (created, skipped, reparented) = await catalogSyncService
            .SyncMissingCatalogEntriesAsync(cancellationToken)
            .ConfigureAwait(false);
        if (created > 0 || reparented > 0)
        {
            return new SeedContributionResult(created + reparented, 0, skipped, "seeding.data.created");
        }

        return new SeedContributionResult(0, 0, skipped, "seeding.data.skipped");
    }
}