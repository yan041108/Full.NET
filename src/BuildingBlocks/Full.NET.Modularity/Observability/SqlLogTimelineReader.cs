using Full.NET.Abstractions.Observability;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modularity.Observability;

/// <summary>执行数据所有者声明的固定 SQL；不构造跨模块查询，也不读取日志正文。</summary>
public sealed class SqlLogTimelineReader(
    IQueryExecutor queries, IOptions<DatabaseOptions> options,
    string source, string requiredPermission, SqlStatement sqlServer, SqlStatement mySql) : ILogTimelineReader
{
    /// <inheritdoc />
    public string Source => source;
    /// <inheritdoc />
    public string RequiredPermission => requiredPermission;

    /// <inheritdoc />
    public async Task<LogTimelineSourceResult> ReadAsync(LogTimelineQuery query, CancellationToken cancellationToken)
    {
        var statement = options.Value.Provider switch
        {
            DatabaseProvider.SqlServer => sqlServer,
            DatabaseProvider.MySql => mySql,
            _ => throw new InvalidOperationException("Unsupported database provider."),
        };
        var rows = await queries.QueryAsync<LogTimelineRow>(statement, new Dictionary<string, object?>
        {
            ["FromUtc"] = query.FromUtc, ["ToUtc"] = query.ToUtc, ["TraceId"] = query.TraceId,
            ["SessionId"] = query.SessionId?.ToString("D"), ["UserId"] = query.UserId?.ToString("D"),
            ["UserGuid"] = query.UserId, ["SessionGuid"] = query.SessionId,
            ["ClientIpFingerprint"] = query.ClientIpFingerprint, ["FetchSize"] = query.Limit + 1,
        }, cancellationToken).ConfigureAwait(false);
        return new(rows.Take(query.Limit).Select(row =>
        {
            var captured = LogCorrelationSnapshot.FromJson(row.CorrelationJson) ?? new();
            return new LogTimelineEntry(row.Id.ToString("D"), Source, row.OccurredAtUtc, row.Summary,
                captured with
                {
                    TraceId = captured.TraceId ?? row.TraceId,
                    UserId = captured.UserId ?? row.UserId,
                    TenantId = captured.TenantId ?? row.TenantId,
                    SessionId = captured.SessionId ?? row.SessionId,
                    CenterSessionId = captured.CenterSessionId ?? row.CenterSessionId,
                    ApplicationSessionId = captured.ApplicationSessionId ?? row.ApplicationSessionId,
                    ClientIpFingerprint = captured.ClientIpFingerprint ?? row.ClientIpFingerprint,
                });
        }).ToArray(), rows.Count > query.Limit ? "truncated" : "available");
    }
}

/// <summary>固定 SQL 投影；可空关联列兼容迁移前的记录。</summary>
public sealed class LogTimelineRow
{
    /// <summary>来源内行主键。</summary>
    public Guid Id { get; init; }
    /// <summary>UTC 发生时间。</summary>
    public DateTimeOffset OccurredAtUtc { get; init; }
    /// <summary>固定动作键。</summary>
    public string Summary { get; init; } = string.Empty;
    /// <summary>历史追踪列。</summary>
    public string? TraceId { get; init; }
    /// <summary>历史用户列。</summary>
    public Guid? UserId { get; init; }
    /// <summary>历史租户列。</summary>
    public Guid? TenantId { get; init; }
    /// <summary>历史刷新会话。</summary>
    public Guid? SessionId { get; init; }
    /// <summary>历史中心会话。</summary>
    public Guid? CenterSessionId { get; init; }
    /// <summary>历史应用会话。</summary>
    public Guid? ApplicationSessionId { get; init; }
    /// <summary>历史指纹列。</summary>
    public string? ClientIpFingerprint { get; init; }
    /// <summary>事件产生时冻结的快照。</summary>
    public string? CorrelationJson { get; init; }
}
