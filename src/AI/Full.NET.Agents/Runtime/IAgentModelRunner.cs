using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Full.NET.Agents.Runtime;

/// <summary>以供应商中立客户端执行单次运行；框架会话只作为不透明快照流转。</summary>
public interface IAgentModelRunner
{
    /// <summary>客户端由调用方持有并释放；授权、预算与持久化意图必须由运行协调器先完成。</summary>
    Task<AgentModelResult> RunAsync(IChatClient client, string prompt, CancellationToken cancellationToken);

    /// <summary>仅检查框架可恢复性，不重新调用模型；调用方须先校验快照来源、所属主体、完整性和版本。</summary>
    Task ValidateSessionAsync(IChatClient client, JsonElement session, CancellationToken cancellationToken);
}

/// <summary>仅包含本次输出、完整会话及供应商实际返回的计量，未知计量保持空值。</summary>
public sealed record AgentModelResult(string Text, JsonElement Session, long? InputTokens, long? OutputTokens)
{
    // 模型输出和会话可能含用户隐私，日志不得通过 record 的自动诊断文本复制载荷。
    public override string ToString() => $"AgentModelResult {{ InputTokens = {InputTokens}, OutputTokens = {OutputTokens} }}";
}
