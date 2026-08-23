using Full.NET.Abstractions.Results;

namespace Full.NET.Seeding.Abstractions;

/// <summary>
/// 协调 Profile 门禁、Contributor 顺序、运行锁和执行审计。
/// </summary>
/// <remarks>
/// <para>编排器是 Seed 流程的唯一入口：调用方传入目标 Profile 后，编排器按
/// <see cref="SeedProfileNames.EffectiveLayers"/> 展开确定性继承链（Production 仅 Baseline，
/// Development/Demo/Test 必须先执行 Baseline 层再执行对应 Overlay 层），
/// 依据 <see cref="IDataSeedContributor.Dependencies"/> 拓扑排序 Contributor，
/// 并通过运行租约保证同一数据库同一时刻只有一个 Seed Run 在执行。</para>
/// <para>每个 Contributor 的执行结果与稳定错误码写入执行审计；失败时不进行自动回滚或重试，
/// 由调用方依据审计结果决定后续处置。生产部署禁止 API/Worker 启动时调用本接口，
/// 只能由 Migrator 在迁移成功后显式触发。</para>
/// </remarks>
public interface ISeedOrchestrator
{
    /// <summary>
    /// 显式执行目标 Profile；调用方必须在数据库迁移成功后调用。
    /// </summary>
    /// <param name="profile">目标 Profile，由编排器展开为其完整继承层（Baseline + 必要 Overlay）。</param>
    /// <param name="cancellationToken">用于取消本次 Seed Run 的令牌；取消后已写入数据不会被自动回滚。</param>
    /// <returns>
    /// 成功时返回聚合 <see cref="SeedRunResult"/>；失败时返回承载稳定错误码的失败结果，
    /// 不包含 Secret 或动态输入文本。
    /// </returns>
    /// <remarks>
    /// 本方法在执行前会校验目标 Profile 与环境允许组合（Production 仅允许 Baseline），
    /// 未授权组合直接失败关闭；并发请求由运行租约串行化，重复请求返回租约占用错误而非并行执行。
    /// </remarks>
    Task<Result<SeedRunResult>> RunAsync(
        SeedProfile profile,
        CancellationToken cancellationToken = default);
}
