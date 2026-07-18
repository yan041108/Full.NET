using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Seeding.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Seeding;

/// <summary>
/// 通过 Identity 领域服务协调受保护宿主管理员，不在 Seed 层复制账号或授权规则。
/// </summary>
internal sealed class HostAdministratorSeedContributor(
    IIdentityBootstrapService bootstrapService,
    IOptions<IdentityOptions> options) : IDataSeedContributor
{
    public string Name => "identity.host_administrator";

    public int Version => 1;

    public IReadOnlySet<SeedProfile> Profiles { get; } =
        new HashSet<SeedProfile> { SeedProfile.Baseline };

    public IReadOnlyCollection<string> Dependencies { get; } = [];

    public async Task<SeedContributionResult> SeedAsync(
        SeedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var bootstrap = options.Value.Bootstrap;
        if (string.IsNullOrWhiteSpace(bootstrap.Username)
            || string.IsNullOrWhiteSpace(bootstrap.Password))
        {
            throw new SeedContributionException(
                SeedContributionErrorCodes.BootstrapSecretMissing);
        }

        var result = await bootstrapService
            .BootstrapHostAdminAsync(
                new BootstrapHostAdminRequest(
                    bootstrap.Username,
                    bootstrap.Password,
                    string.IsNullOrWhiteSpace(bootstrap.DisplayName)
                        ? "系统管理员"
                        : bootstrap.DisplayName),
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            throw new SeedContributionException(
                result.Error?.Code ?? SeedContributionErrorCodes.DataConflict);
        }

        if (result.Value!.Created)
        {
            return new SeedContributionResult(1, 0, 0, "seeding.data.created");
        }

        return result.Value.AuthorizationChanged
            ? new SeedContributionResult(0, 1, 0, "seeding.data.updated")
            : new SeedContributionResult(0, 0, 1, "seeding.data.skipped");
    }
}
