namespace Full.NET.Agents.Runtime;

/// <summary>持久运行存储 Port；实现由 Ai 模块提供，Agents 不访问 fn_ai_*。</summary>
public interface IAgentRunStore
{
    /// <summary>
    /// 创建新的运行记录或按草稿返回已存在记录的标识。
    /// </summary>
    /// <param name="draft">运行草稿，包含幂等去重所需字段。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>运行记录标识；若草稿匹配已有记录则返回已有 Id，否则返回新建 Id。</returns>
    ValueTask<Guid> CreateOrGetAsync(AgentRunDraft draft, CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试以 CAS 方式获取运行租约；已被其他 worker 持有时失败。
    /// </summary>
    /// <param name="runId">目标运行标识。</param>
    /// <param name="workerId">申请租约的 worker 标识。</param>
    /// <param name="now">申请时刻（UTC）。</param>
    /// <param name="leaseDuration">租约有效期。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>获取成功的租约；租约已被占用或 runId 不存在时为 null。</returns>
    ValueTask<AgentRunLease?> TryAcquireAsync(
        Guid runId,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 续租已持有的运行租约，防止其他 worker 抢占。
    /// </summary>
    /// <param name="lease">当前持有的租约。</param>
    /// <param name="now">续租时刻（UTC）。</param>
    /// <param name="leaseDuration">续租后的有效期。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>true 表示续租成功；false 表示租约已过期、被抢占或 lease 无效。</returns>
    ValueTask<bool> RenewAsync(
        AgentRunLease lease,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 提交运行进度，持久化当前检查点与状态。
    /// </summary>
    /// <param name="commit">包含 runId、租约与进度的提交载荷。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>true 表示进度已提交；false 表示租约失效或 runId 不存在。</returns>
    ValueTask<bool> CommitProgressAsync(AgentRunProgressCommit commit, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询指定运行最近一次提交的检查点。
    /// </summary>
    /// <param name="runId">目标运行标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>最新检查点记录；runId 不存在或尚无检查点时为 null。</returns>
    ValueTask<AgentCheckpointRecord?> FindLatestCheckpointAsync(Guid runId, CancellationToken cancellationToken = default);
}
