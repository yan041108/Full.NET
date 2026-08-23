using System.Collections.Immutable;

namespace Full.NET.Seeding.Abstractions;

/// <summary>
/// 标识生产安全基线以及允许叠加的环境种子数据集合。
/// </summary>
/// <remarks>
/// <para>Full.NET 种子数据采用“Baseline + 环境 Overlay”的确定性继承链：</para>
/// <list type="bullet">
/// <item><c>Baseline</c> 是所有继承链的根，承载生产必需且可安全重复协调的数据，
/// Production 环境只允许执行 Baseline。</item>
/// <item><c>Development</c> 与 <c>Demo</c> 是开发/演示环境在 Baseline 之上叠加的 Overlay，
/// 编排器必须先执行 Baseline 层再执行该 Overlay 层，二者互不叠加。</item>
/// <item><c>Test</c> 是自动化测试环境的 Overlay，对应 Contributor 只允许存在于测试/Sample 测试程序集，
/// 不得进入发布物；具体场景数据由隔离 Test Factory 创建，不写入 Test Profile 自身。</item>
/// </list>
/// <para>继承关系由 <see cref="SeedProfileNames.EffectiveLayers"/> 封闭表达，禁止运行时组合未声明的 Profile 集合，
/// 也禁止把开发/演示数据改名后放入 Baseline 绕过生产门禁。Overlay 层的 Contributor 不得删除数据、重置密码或覆盖用户修改。</para>
/// </remarks>
public enum SeedProfile
{
    /// <summary>生产运行所必需且可安全重复协调的基础数据。</summary>
    Baseline = 0,

    /// <summary>本地开发环境在 Baseline 上叠加的数据。</summary>
    Development = 1,

    /// <summary>产品演示环境在 Baseline 上叠加的数据。</summary>
    Demo = 2,

    /// <summary>自动化测试环境在 Baseline 上叠加的共享数据。</summary>
    Test = 3,
}

/// <summary>
/// 提供 CLI、配置和审计共同使用的规范 Profile 名称与继承关系。
/// </summary>
public static class SeedProfileNames
{
    private static readonly IReadOnlySet<SeedProfile> BaselineLayers =
        ImmutableHashSet.Create(SeedProfile.Baseline);
    private static readonly IReadOnlySet<SeedProfile> DevelopmentLayers =
        ImmutableHashSet.Create(SeedProfile.Baseline, SeedProfile.Development);
    private static readonly IReadOnlySet<SeedProfile> DemoLayers =
        ImmutableHashSet.Create(SeedProfile.Baseline, SeedProfile.Demo);
    private static readonly IReadOnlySet<SeedProfile> TestLayers =
        ImmutableHashSet.Create(SeedProfile.Baseline, SeedProfile.Test);

    /// <summary>
    /// 尝试把规范 CLI 名称解析为封闭 Profile，拒绝缩写和任意环境名称。
    /// </summary>
    public static bool TryParse(string? value, out SeedProfile profile)
    {
        if (string.Equals(value, "baseline", StringComparison.OrdinalIgnoreCase))
        {
            profile = SeedProfile.Baseline;
            return true;
        }

        if (string.Equals(value, "development", StringComparison.OrdinalIgnoreCase))
        {
            profile = SeedProfile.Development;
            return true;
        }

        if (string.Equals(value, "demo", StringComparison.OrdinalIgnoreCase))
        {
            profile = SeedProfile.Demo;
            return true;
        }

        if (string.Equals(value, "test", StringComparison.OrdinalIgnoreCase))
        {
            profile = SeedProfile.Test;
            return true;
        }

        profile = default;
        return false;
    }

    /// <summary>取得写入 CLI 与审计表的规范小写名称。</summary>
    public static string ToCanonicalName(this SeedProfile profile) => profile switch
    {
        SeedProfile.Baseline => "baseline",
        SeedProfile.Development => "development",
        SeedProfile.Demo => "demo",
        SeedProfile.Test => "test",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "未知 Seed Profile。"),
    };

    /// <summary>
    /// 取得目标 Profile 的确定性执行层；所有环境 Overlay 都必须先包含 Baseline。
    /// </summary>
    public static IReadOnlySet<SeedProfile> EffectiveLayers(this SeedProfile profile) =>
        profile switch
        {
            SeedProfile.Baseline => BaselineLayers,
            SeedProfile.Development => DevelopmentLayers,
            SeedProfile.Demo => DemoLayers,
            SeedProfile.Test => TestLayers,
            _ => throw new ArgumentOutOfRangeException(
                nameof(profile),
                profile,
                "未知 Seed Profile。"),
        };
}
