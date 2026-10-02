using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Full.NET.Agents.Runtime;

/// <summary>以供应商中立客户端执行单次运行；框架会话只作为不透明快照流转。</summary>
public interface IAgentModelRunner
{
    /// <summary>客户端由调用方持有并释放；授权、预算与持久化意图必须由运行协调器先完成。</summary>
    /// <param name="client">已授权的聊天客户端；调用方负责生命周期与释放。</param>
    /// <param name="prompt">本次运行的输入提示文本。</param>
    /// <param name="cancellationToken">用于取消模型调用的令牌。</param>
    /// <returns>模型输出文本、不透明会话快照与计量信息。</returns>
    Task<AgentModelResult> RunAsync(IChatClient client, string prompt, CancellationToken cancellationToken);

    /// <summary>仅检查框架可恢复性，不重新调用模型；调用方须先校验快照来源、所属主体、完整性和版本。</summary>
    /// <param name="client">已授权的聊天客户端；仅用于会话验证，不产生模型调用。</param>
    /// <param name="session">待验证的不透明会话快照。</param>
    /// <param name="cancellationToken">用于取消验证操作的令牌。</param>
    Task ValidateSessionAsync(IChatClient client, JsonElement session, CancellationToken cancellationToken);
}

/// <summary>仅包含本次输出、完整会话及供应商实际返回的计量，未知计量保持空值。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Text">模型本次输出的文本内容。</param>
/// <param name="Session">供应商不透明会话快照；用于后续恢复，调用方不得解读内部结构。</param>
/// <param name="InputTokens">本次输入令牌数；供应商未报告时为 <see langword="null"/>。</param>
/// <param name="OutputTokens">本次输出令牌数；供应商未报告时为 <see langword="null"/>。</param>
public sealed record AgentModelResult(string Text, JsonElement Session, long? InputTokens, long? OutputTokens)
{
    // 模型输出和会话可能含用户隐私，日志不得通过 record 的自动诊断文本复制载荷。
    public override string ToString() => $"AgentModelResult {{ InputTokens = {InputTokens}, OutputTokens = {OutputTokens} }}";
}
