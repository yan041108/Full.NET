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
    /// <param name="context">本次 Seed Run 的非敏感运行元数据，包含 RunId、Profile、环境、默认语言与关联标识。</param>
    /// <param name="cancellationToken">用于取消数据库或下游业务调用的令牌。</param>
    /// <returns>包含创建/更新/跳过计数与稳定结果码的执行结果，禁止承载 Secret 或个人敏感数据。</returns>
    /// <remarks>
    /// 实现必须在自有边界内完成幂等协调：使用稳定自然键检查真实状态后再决定新建/更新/跳过，
    /// 不得删除已有用户修改、重置密码或覆盖审计历史。Contributor 在独立事务内写入自身数据，
    /// 不应跨 Contributor 共享本地事务；并发执行由编排器通过租约与依赖图保证，
    /// 本方法不得依赖其他 Contributor 已提交的事务结果作为正确性前提。
    /// </remarks>
    Task<SeedContributionResult> SeedAsync(
        SeedContext context,
        CancellationToken cancellationToken = default);
}
