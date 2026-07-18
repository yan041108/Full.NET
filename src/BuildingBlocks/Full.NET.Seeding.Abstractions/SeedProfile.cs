using System.Collections.Immutable;

namespace Full.NET.Seeding.Abstractions;

/// <summary>
/// 标识生产安全基线以及允许叠加的环境种子数据集合。
/// </summary>
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
