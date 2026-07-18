namespace Full.NET.Seeding.Dapper;

/// <summary>
/// 定义 Seed Orchestrator 的非敏感运行选项；账号密码等 Secret 不得放入本节。
/// </summary>
public sealed class SeedOptions
{
    /// <summary>配置节名称。</summary>
    public const string SectionName = "Seeding";

    /// <summary>获取或设置 Baseline 数据使用的规范 BCP 47 默认语言标签。</summary>
    public string DefaultLocale { get; set; } = "zh-CN";

    /// <summary>获取或设置等待数据库级 Seed 独占锁的秒数。</summary>
    public int LockTimeoutSeconds { get; set; } = 30;
}
