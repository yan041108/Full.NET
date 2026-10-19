using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Abstractions.Observability;

/// <summary>事件产生时冻结的关联元数据；可空字段表示未获取，禁止放入令牌、Cookie 或原始 IP。</summary>
public sealed record LogCorrelationSnapshot
{
    /// <summary>W3C 链路标识，不能用请求标识替代。</summary>
    public string? TraceId { get; init; }
    /// <summary>事件产生时的当前跨度。</summary>
    public string? SpanId { get; init; }
    /// <summary>已知的父跨度；根跨度留空。</summary>
    public string? ParentSpanId { get; init; }
    /// <summary>宿主请求标识，与跨进程链路标识独立。</summary>
    public string? RequestId { get; init; }
    /// <summary>已验证请求主体；后台无主体时为空。</summary>
    public Guid? UserId { get; init; }
    /// <summary>已解析的租户，Host 上下文为空。</summary>
    public Guid? TenantId { get; init; }
    /// <summary>已验证刷新会话标识，不是刷新令牌。</summary>
    public Guid? SessionId { get; init; }
    /// <summary>已验证 OIDC 中心会话标识。</summary>
    public Guid? CenterSessionId { get; init; }
    /// <summary>已验证 OIDC 应用会话标识。</summary>
    public Guid? ApplicationSessionId { get; init; }
    /// <summary>可信代理规范化地址的 SHA-256 指纹；仍属于假名化数据。</summary>
    public string? ClientIpFingerprint { get; init; }

    /// <summary>用源生成契约保存关联快照，供所有持久化提供程序使用。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, LogCorrelationJsonContext.Default.LogCorrelationSnapshot);

    /// <summary>读取有界历史快照；无数据或无效内容返回空，不推断缺失字段。</summary>
    /// <param name="json">数据库中至多 2048 字符的关联 JSON。</param>
    /// <returns>已记录的快照，或空。</returns>
    public static LogCorrelationSnapshot? FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 2048) return null;
        try { return JsonSerializer.Deserialize(json, LogCorrelationJsonContext.Default.LogCorrelationSnapshot); }
        catch (JsonException) { return null; }
    }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LogCorrelationSnapshot))]
internal partial class LogCorrelationJsonContext : JsonSerializerContext;
