using System.Text.Json;
using Full.NET.Agents.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Full.NET.Agents.Framework;

/// <summary>单次文本执行不安装自动工具循环；会话由实际框架完整序列化。</summary>
internal sealed class AgentFrameworkAdapter : IAgentModelRunner
{
    public async Task<AgentModelResult> RunAsync(IChatClient client, string prompt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var agent = CreateAgent(client);
        var session = await agent.CreateSessionAsync(cancellationToken);
        var response = await agent.RunAsync(prompt, session, cancellationToken: cancellationToken);
        var snapshot = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        return new(response.Text, snapshot.Clone(), response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount);
    }

    public async Task ValidateSessionAsync(IChatClient client, JsonElement session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(client);
        var agent = CreateAgent(client);
        _ = await agent.DeserializeSessionAsync(session, cancellationToken: cancellationToken);
    }

    private static ChatClientAgent CreateAgent(IChatClient client) => new(client, new ChatClientAgentOptions
    {
        Id = "fullnet-single-text-v1",
        Name = "FullNetSingleText",
        UseProvidedChatClientAsIs = true
    });
}
