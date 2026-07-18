namespace Full.NET.Seeding.Abstractions;

/// <summary>
/// 定义模块拥有的幂等 Seed 贡献者；实现必须通过真实业务边界协调数据。
/// </summary>
public interface IDataSeedContributor
{
    /// <summary>取得发布后保持稳定的小写点分名称。</summary>
    string Name { get; }

    /// <summary>取得从 1 开始的 Contributor 数据契约版本。</summary>
    int Version { get; }

    /// <summary>取得 Contributor 直接所属的 Profile 层。</summary>
    IReadOnlySet<SeedProfile> Profiles { get; }

    /// <summary>取得必须先成功执行的 Contributor 稳定名称。</summary>
    IReadOnlyCollection<string> Dependencies { get; }

    /// <summary>
    /// 幂等协调模块数据并返回不包含 Secret 的执行计数。
    /// </summary>
    Task<SeedContributionResult> SeedAsync(
        SeedContext context,
        CancellationToken cancellationToken = default);
}
