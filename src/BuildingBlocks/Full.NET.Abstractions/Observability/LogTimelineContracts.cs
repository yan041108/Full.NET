namespace Full.NET.Abstractions.Observability;

/// <summary>有界日志关联查询；同一请求中的非空筛选条件按 AND 组合。</summary>
/// <param name="FromUtc">包含的 UTC 起点。</param>
/// <param name="ToUtc">不包含的 UTC 终点。</param>
/// <param name="TraceId">精确 W3C TraceId，可为空。</param>
/// <param name="SessionId">精确会话标识，可匹配刷新、中心或应用会话。</param>
/// <param name="UserId">精确用户标识。</param>
/// <param name="ClientIpFingerprint">精确 IP 指纹，不接受明文 IP。</param>
/// <param name="Limit">每个来源最多返回的事件数。</param>
public sealed record LogTimelineQuery(DateTimeOffset FromUtc, DateTimeOffset ToUtc,
    string? TraceId, Guid? SessionId, Guid? UserId, string? ClientIpFingerprint, int Limit);

/// <summary>统一时间线中的安全摘要；不返回原始日志正文、业务差异或受限请求内容。</summary>
/// <param name="Id">来源内稳定标识。</param>
/// <param name="Source">固定数据源键。</param>
/// <param name="OccurredAtUtc">事件发生时间；不是入库时间。</param>
/// <param name="Summary">固定动作键或日志类别。</param>
/// <param name="Correlation">已采集的关联信息。</param>
public sealed record LogTimelineEntry(string Id, string Source, DateTimeOffset OccurredAtUtc,
    string Summary, LogCorrelationSnapshot Correlation);

/// <summary>来源查询的覆盖结果；状态不能被解释为该来源完整保存了所有历史事件。</summary>
/// <param name="Entries">安全摘要，按发生时间正序。</param>
/// <param name="Status">available、truncated、unavailable 或 not_configured。</param>
public sealed record LogTimelineSourceResult(IReadOnlyList<LogTimelineEntry> Entries, string Status);

/// <summary>由数据所有者提供日志摘要，聚合模块不能直接读取其他模块的表。</summary>
public interface ILogTimelineReader
{
    /// <summary>稳定来源名称。</summary>
    string Source { get; }
    /// <summary>读取该来源还需具备的已有权限。</summary>
    string RequiredPermission { get; }
    /// <summary>执行有界只读查询；取消必须向调用方传播。</summary>
    /// <param name="query">已验证的时间与关联筛选。</param>
    /// <param name="cancellationToken">查询取消信号。</param>
    /// <returns>摘要和覆盖状态。</returns>
    Task<LogTimelineSourceResult> ReadAsync(LogTimelineQuery query, CancellationToken cancellationToken);
}
