namespace Full.NET.Modules.Document.Features.DocumentAccessLogs;

/// <summary>成功读取文档内容后需要落库的访问观测上下文。</summary>
/// <param name="ActorUserId">执行访问的用户；匿名分享场景为 null。</param>
/// <param name="ClientIpFingerprint">客户端 IP 指纹；禁止写入明文 IP。</param>
/// <param name="AccessTypeKey">访问类型稳定键。</param>
/// <param name="SourceKey">访问来源稳定键。</param>
internal sealed record DocumentAccessObservation(
    Guid? ActorUserId,
    string? ClientIpFingerprint,
    string AccessTypeKey,
    string SourceKey);
