namespace Full.NET.Modules.Messaging.Contracts;

/// <summary>请求服务器实际验证影子流；目标地址和预期载荷只能来自服务端配置及数据库。</summary>
public sealed record VerifyCutoverRequest(string EventType, int SchemaVersion, string Reason);

/// <summary>有限期切流证明，不暴露内部资源摘要。</summary>
public sealed record CutoverEvidenceResponse(Guid Id, string EventType, int SchemaVersion, int SampleCount,
    DateTimeOffset VerifiedAtUtc, DateTimeOffset ExpiresAtUtc);
