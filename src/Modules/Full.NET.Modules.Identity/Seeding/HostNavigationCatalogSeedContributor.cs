using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Full.NET.Seeding.Abstractions;

namespace Full.NET.Modules.Identity.Seeding;

/// <summary>
/// Baseline Profile 播种：将代码 AuthorizationCatalog 中声明的导航定义
/// 同步写入宿主侧持久化菜单表，支持新增节点与父子重连；已存在的条目按 Id 幂等跳过。
/// 依赖 HostNavigationCatalogSyncService 做具体同步，避免在 Seed 层复制菜单规则。
/// </summary>
internal sealed class HostNavigationCatalogSeedContributor(
    HostNavigationCatalogSyncService catalogSyncService) : IDataSeedContributor
{
    public string Name => "identity.host_navigation_catalog";

    public int Version => 1;

    public IReadOnlySet<SeedProfile> Profiles { get; } =
        new HashSet<SeedProfile> { SeedProfile.Baseline };

    public IReadOnlyCollection<string> Dependencies { get; } = [];

    /// <summary>
    /// 执行导航目录播种：将缺失的模块代码导航项写入宿主持久化菜单表，
    /// 返回 created/updated/skipped 的计数组合。
    /// </summary>
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