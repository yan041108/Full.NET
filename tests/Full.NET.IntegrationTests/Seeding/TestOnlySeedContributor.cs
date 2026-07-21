using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Full.NET.Seeding.Abstractions;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.IntegrationTests.Seeding;

/// <summary>
/// 仅供 Integration 契约验证：在 Test Profile 写入可查询标记，证明与 Development Overlay 隔离。
/// </summary>
internal sealed class TestOnlySeedContributor(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser> passwordHasher,
    IClock clock,
    IIdGenerator idGenerator) : IDataSeedContributor
{
    internal const string MarkerUsername = "seed-profile-test-marker";

    private const string HostScope = "host";
    private const string MarkerPassword = "Seed-Profile-Test!99";

    public string Name => "testing.profile_contract_marker";

    public int Version => 1;

    public IReadOnlySet<SeedProfile> Profiles { get; } =
        new HashSet<SeedProfile> { SeedProfile.Test };

    public IReadOnlyCollection<string> Dependencies { get; } = [];

    public async Task<SeedContributionResult> SeedAsync(
        SeedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var normalizedUsername = MarkerUsername.ToUpperInvariant();
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                new { ScopeKey = HostScope, NormalizedUsername = normalizedUsername },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new SeedContributionResult(0, 0, 1, "seeding.data.skipped");
        }

        var now = clock.UtcNow;
        var user = new IdentityUser(
            idGenerator.NewId(),
            null,
            HostScope,
            MarkerUsername,
            normalizedUsername,
            "Seed Test Profile Marker",
            string.Empty,
            true,
            0,
            null,
            idGenerator.NewId().ToString("N"),
            now,
            null,
            1);
        user = user with { PasswordHash = passwordHasher.HashPassword(user, MarkerPassword) };
        var affectedRows = await commandExecutor.ExecuteAsync(
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
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Test profile marker insert affected {affectedRows} rows instead of one.");
        }

        return new SeedContributionResult(1, 0, 0, "seeding.data.created");
    }
}
