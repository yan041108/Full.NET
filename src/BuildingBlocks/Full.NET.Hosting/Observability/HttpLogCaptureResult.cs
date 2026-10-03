using Microsoft.AspNetCore.Http;

namespace Full.NET.Hosting.Observability;

/// <summary>HTTP 投影的安全机器状态；不包含异常消息或原始请求值。</summary>
public enum HttpLogCaptureState
{
    /// <summary>已完成受控投影。</summary>
    Captured,
    /// <summary>功能已关闭。</summary>
    NotEnabled,
    /// <summary>路由、投影或目的地不在许可范围。</summary>
    NotAllowed,
    /// <summary>当前部署没有对应目的地资格。</summary>
    NotApplicable,
    /// <summary>敏感输入被移除。</summary>
    Redacted,
    /// <summary>输入受限后仍无法保留全部内容。</summary>
    Truncated,
    /// <summary>投影失败或请求已取消。</summary>
    Failed,
    /// <summary>事件或字节预算已耗尽。</summary>
    BudgetExceeded,
}

/// <summary>受控 HTTP 投影结果；JSON 仅供 Hosting 内部按目的地读取。</summary>
public sealed class HttpLogCaptureResult
{
    internal HttpLogCaptureResult(
        HttpLogCaptureState state,
        string? payloadJson,
        HttpContext? owner = null,
        HttpLogCaptureTarget? target = null)
    {
        State = state;
        PayloadJson = payloadJson;
        Owner = owner;
        Target = target;
    }

    /// <summary>本次捕获的安全机器状态。</summary>
    public HttpLogCaptureState State { get; }

    internal string? PayloadJson { get; }

    internal HttpContext? Owner { get; }

    internal HttpLogCaptureTarget? Target { get; }
}
