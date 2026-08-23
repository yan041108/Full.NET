namespace Full.NET.Realtime.SignalR.Features.RealtimeProbe;

/// <summary>
/// Testing 环境自探针 HTTP 响应；保持与客户端稳定机器码契约一致。
/// </summary>
internal sealed record RealtimeProbeResponse(string Code);
