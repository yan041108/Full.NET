// 本地/开发租户种子贡献者；仅非 Production Overlay，不写入生产 Baseline。
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;
using Full.NET.Seeding.Abstractions;

namespace Full.NET.Modules.Tenancy.Seeding;

/// <summary>本地/开发租户种子贡献者；仅非 Production Overlay，不写入生产 Baseline。</summary>
internal sealed class LocalTenantSeedContributor(
    IQueryExecutor queryExecutor,
    ITenantProvisioningService provisioningService) : IDataSeedContributor
{
    private const string Identifier = "local";
    private const string TenantName = "Full.NET Local";
    private const string Domain = "localhost";

    public string Name => "tenancy.local_tenant";

    public int Version => 1;

    public IReadOnlySet<SeedProfile> Profiles { get; } =
        new HashSet<SeedProfile> { SeedProfile.Development };

    public IReadOnlyCollection<string> Dependencies { get; } = [];

    public async Task<SeedContributionResult> SeedAsync(
        SeedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var existing = await queryExecutor
            .QuerySingleOrDefaultAsync<LocalTenantSeedSummary>(
                TenantSql.FindSummaryByIdentifier,
                TenancySqlParameters.Create(("Identifier", Identifier)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (!MatchesExpectedState(existing))
            {
                throw new SeedContributionException(
                    SeedContributionErrorCodes.DataConflict);
            }

            return new SeedContributionResult(0, 0, 1, "seeding.data.skipped");
        }

        var provisioned = await provisioningService
            .ProvisionAsync(
                new ProvisionTenantRequest(Identifier, TenantName, Domain),
                cancellationToken)
            .ConfigureAwait(false);
        if (!provisioned.IsSuccess)
        {
            throw new SeedContributionException(
                provisioned.Error?.Code ?? SeedContributionErrorCodes.DataConflict);
        }

        return new SeedContributionResult(1, 0, 0, "seeding.data.created");
    }

    private static bool MatchesExpectedState(LocalTenantSeedSummary existing) =>
        string.Equals(existing.Identifier, Identifier, StringComparison.Ordinal) &&
        string.Equals(existing.Name, TenantName, StringComparison.Ordinal) &&
        string.Equals(existing.Domain, Domain, StringComparison.Ordinal);
}

internal sealed record LocalTenantSeedSummary(
    Guid Id,
    string Identifier,
    string Name,
    string Domain,
    bool IsActive,
    int Version);
