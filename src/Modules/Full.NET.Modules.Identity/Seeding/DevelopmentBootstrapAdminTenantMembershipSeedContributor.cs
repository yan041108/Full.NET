using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Seeding.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Identity.Seeding;

/// <summary>本地租户创建后补齐开发管理员成员；不改变生产基线与其他 Overlay 的执行图。</summary>
internal sealed class DevelopmentBootstrapAdminTenantMembershipSeedContributor(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICurrentTenantContextWriter currentTenant,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<IdentityOptions> options) : IDataSeedContributor
{
    private readonly BootstrapAdminTenantMembershipSeedContributor membership =
        new(queryExecutor, commandExecutor, currentTenant, clock, idGenerator, options);

    public string Name => "identity.development_admin_tenant_membership";
    public int Version => 1;
    public IReadOnlySet<SeedProfile> Profiles { get; } = new HashSet<SeedProfile> { SeedProfile.Development };
    public IReadOnlyCollection<string> Dependencies { get; } =
        ["identity.bootstrap_admin_tenant_membership", "tenancy.local_tenant"];

    public Task<SeedContributionResult> SeedAsync(SeedContext context, CancellationToken cancellationToken = default) =>
        membership.SeedLocalTenantAsync(context, cancellationToken);
}
