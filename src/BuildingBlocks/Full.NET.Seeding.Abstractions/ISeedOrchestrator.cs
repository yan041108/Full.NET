using Full.NET.Abstractions.Results;

namespace Full.NET.Seeding.Abstractions;

/// <summary>
/// 协调 Profile 门禁、Contributor 顺序、运行锁和执行审计。
/// </summary>
public interface ISeedOrchestrator
{
    /// <summary>
    /// 显式执行目标 Profile；调用方必须在数据库迁移成功后调用。
    /// </summary>
    Task<Result<SeedRunResult>> RunAsync(
        SeedProfile profile,
        CancellationToken cancellationToken = default);
}
