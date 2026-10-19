using Full.NET.Abstractions.Observability;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Diagnostics;

/// <summary>只接收已通过现有会话授权的元数据；按任务作用域保留到外层失败日志完成。</summary>
internal sealed class AiBackgroundLogCorrelation
{
    private LogCorrelationSnapshot snapshot = new();

    /// <summary>外层 Worker 持有此作用域，使内层异步调用验证后的快照也能用于失败记录。</summary>
    internal IDisposable BeginCapture() => LogCorrelationContext.BeginScope(() => snapshot);

    /// <summary>调用方必须先验证绑定；不复制安全戳，也不从任务所有者推断请求或网络身份。</summary>
    internal IDisposable BeginValidatedScope(SessionBindingSnapshot binding)
    {
        snapshot = new LogCorrelationSnapshot
        {
            UserId = binding.UserId,
            TenantId = binding.TenantId,
            SessionId = binding.SessionKind == SessionBindingKinds.Refresh ? binding.SessionId : null,
            ApplicationSessionId = binding.SessionKind == SessionBindingKinds.OidcApplication ? binding.SessionId : null,
        };
        var frozen = snapshot;
        return LogCorrelationContext.BeginScope(() => frozen);
    }
}
