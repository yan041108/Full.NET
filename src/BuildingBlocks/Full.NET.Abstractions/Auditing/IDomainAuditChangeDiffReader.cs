namespace Full.NET.Abstractions.Auditing;

/// <summary>
/// 模块侧域审计变更差异只读贡献者：由业务模块查询本模块 B0 域审计表并返回脱敏后的差异摘要。
/// </summary>
public interface IDomainAuditChangeDiffReader
{
    /// <summary>贡献者所属模块键，用于聚合响应分组。</summary>
    string ModuleKey { get; }

    /// <summary>
    /// 按分布式追踪标识读取本模块内匹配的域审计差异。
    /// </summary>
    /// <param name="traceId">HTTP 审计或业务链路 TraceId。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配条目；无记录时返回空集合。</returns>
    Task<IReadOnlyList<DomainAuditChangeDiffEntry>> ReadByTraceIdAsync(
        string traceId,
        CancellationToken cancellationToken = default);
}
