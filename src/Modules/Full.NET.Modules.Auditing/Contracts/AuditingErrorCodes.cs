namespace Full.NET.Modules.Auditing.Contracts;

/// <summary>
/// Auditing 模块对外返回的稳定错误码。
/// </summary>
public static class AuditingErrorCodes
{
    /// <summary>Auditing 错误码前缀。</summary>
    public const string Prefix = "auditing.";

    /// <summary>目标访问日志不存在。</summary>
    public const string AccessLogNotFound = "auditing.access_log.not_found";

    /// <summary>访问日志游标无效、版本未知或与当前筛选不匹配。</summary>
    public const string AccessLogCursorInvalid = "auditing.access_log.cursor_invalid";

    /// <summary>目标操作日志不存在。</summary>
    public const string OperationLogNotFound = "auditing.operation_log.not_found";

    /// <summary>目标异常日志不存在。</summary>
    public const string ExceptionLogNotFound = "auditing.exception_log.not_found";

    /// <summary>目标出站调用审计不存在。</summary>
    public const string OutboundCallLogNotFound = "auditing.outbound_call.not_found";

    /// <summary>Contains 查询缺少完整的 UTC 时间范围。</summary>
    public const string ContainsTimeRangeRequired =
        "auditing.query.contains_time_range_required";

    /// <summary>审计查询的 UTC 时间范围顺序无效。</summary>
    public const string TimeRangeInvalid =
        "auditing.query.time_range_invalid";

    /// <summary>Contains 查询的 UTC 时间范围超过服务端上限。</summary>
    public const string ContainsTimeRangeExceeded =
        "auditing.query.contains_time_range_exceeded";

    /// <summary>趋势查询缺少完整的 UTC 时间范围。</summary>
    public const string TrendTimeRangeRequired =
        "auditing.query.trend_time_range_required";

    /// <summary>趋势查询的 UTC 时间范围超过服务端上限。</summary>
    public const string TrendTimeRangeExceeded =
        "auditing.query.trend_time_range_exceeded";

    /// <summary>趋势查询桶宽参数无效。</summary>
    public const string TrendBucketSizeInvalid =
        "auditing.query.trend_bucket_size_invalid";

    /// <summary>趋势查询时间桶数量超过服务端上限。</summary>
    public const string TrendBucketLimitExceeded =
        "auditing.query.trend_bucket_limit_exceeded";

    /// <summary>域审计变更差异查询缺少 TraceId。</summary>
    public const string DomainChangeDiffTraceIdRequired =
        "auditing.change_diff.trace_id_required";

    /// <summary>域审计变更差异查询的 TraceId 无效。</summary>
    public const string DomainChangeDiffTraceIdInvalid =
        "auditing.change_diff.trace_id_invalid";

    /// <summary>
    /// 获取当前目录中的全部稳定错误码。
    /// </summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        AccessLogNotFound,
        AccessLogCursorInvalid,
        OperationLogNotFound,
        ExceptionLogNotFound,
        OutboundCallLogNotFound,
        ContainsTimeRangeRequired,
        TimeRangeInvalid,
        ContainsTimeRangeExceeded,
        TrendTimeRangeRequired,
        TrendTimeRangeExceeded,
        TrendBucketSizeInvalid,
        TrendBucketLimitExceeded,
        DomainChangeDiffTraceIdRequired,
        DomainChangeDiffTraceIdInvalid,
    ]);
}
