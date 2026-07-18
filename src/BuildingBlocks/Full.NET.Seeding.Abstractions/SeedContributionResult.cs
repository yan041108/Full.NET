namespace Full.NET.Seeding.Abstractions;

/// <summary>
/// 描述单个 Contributor 的非敏感执行计数和稳定结果码。
/// </summary>
/// <param name="CreatedCount">本次新建的业务记录数量。</param>
/// <param name="UpdatedCount">本次协调的系统管理记录数量。</param>
/// <param name="SkippedCount">真实状态已满足要求而跳过的记录数量。</param>
/// <param name="Code">用于审计和日志的稳定机器码。</param>
public sealed record SeedContributionResult(
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount,
    string Code);

/// <summary>
/// 描述完整 Seed Run 的聚合结果，不包含 Contributor 输入或 Secret。
/// </summary>
/// <param name="RunId">本次执行标识。</param>
/// <param name="Profile">调用方请求的目标 Profile。</param>
/// <param name="ContributorCount">实际执行的 Contributor 数量。</param>
/// <param name="CreatedCount">全部 Contributor 的新建数量。</param>
/// <param name="UpdatedCount">全部 Contributor 的更新数量。</param>
/// <param name="SkippedCount">全部 Contributor 的跳过数量。</param>
public sealed record SeedRunResult(
    Guid RunId,
    SeedProfile Profile,
    int ContributorCount,
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount);
