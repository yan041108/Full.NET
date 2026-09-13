using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Tools;
using Microsoft.Extensions.AI;

namespace Full.NET.Agents.Runtime;

/// <summary>受控工具循环：模型请求经统一执行器派发，未注册工具不会执行 Handler。</summary>
public sealed class AgentToolLoop
{
    public async Task<AgentToolLoopResult> RunAsync(AgentToolLoopRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var definition = AgentDefinitionRegistry.Resolve(request.DefinitionKey, request.DefinitionVersion)
            ?? throw new InvalidOperationException("Unknown agent definition.");
        if (definition.MaxToolIterations <= 0)
        {
            throw new InvalidOperationException("Definition does not support tool loop execution.");
        }

        var messages = new List<ChatMessage> { new(ChatRole.User, request.Prompt) };
        var allowed = definition.AllowedToolNames.ToHashSet(StringComparer.Ordinal);
        if (request.AdditionalAllowedToolNames is not null)
        {
            foreach (var name in request.AdditionalAllowedToolNames)
            {
                allowed.Add(name);
            }
        }
        var toolDescriptions = BuildToolDescriptions(request.Registry, allowed);
        var options = new ChatOptions { Tools = [.. toolDescriptions] };
        long? inputTokens = null;
        long? outputTokens = null;
        var toolSteps = 0;
        var approvalRequired = false;
        var reconciliationRequired = false;

        for (var iteration = 0; iteration < definition.MaxToolIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await request.Client.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            inputTokens = AddUsage(inputTokens, response.Usage?.InputTokenCount);
            outputTokens = AddUsage(outputTokens, response.Usage?.OutputTokenCount);
            var calls = response.Messages.SelectMany(message => message.Contents.OfType<FunctionCallContent>()).ToArray();
            if (calls.Length == 0)
            {
                var text = response.Messages.LastOrDefault()?.Text ?? string.Empty;
                return new(text, SerializeSession(messages, text), inputTokens, outputTokens, toolSteps, approvalRequired, reconciliationRequired);
            }

            messages.AddRange(response.Messages);
            foreach (var call in calls)
            {
                toolSteps++;
                if (!allowed.Contains(call.Name))
                {
                    messages.Add(new ChatMessage(ChatRole.Tool,
                    [
                        new FunctionResultContent(call.CallId,
                            JsonSerializer.Serialize(new { statusKey = "denied", errorCode = "ai.tool.unavailable" }))
                    ]));
                    continue;
                }

                var tool = request.Registry.Find(call.Name);
                if (tool is null || !tool.IsEnabled)
                {
                    messages.Add(new ChatMessage(ChatRole.Tool,
                    [
                        new FunctionResultContent(call.CallId,
                            JsonSerializer.Serialize(new { statusKey = "denied", errorCode = "ai.tool.unavailable" }))
                    ]));
                    continue;
                }

                using var arguments = ToJsonElement(call.Arguments);
                var operationId = request.ResolveOperationId(call.CallId);
                var invocation = new ToolInvocation(operationId, request.RunId, call.Name, tool.Version, arguments.RootElement.Clone());
                var result = await request.Executor.ExecuteAsync(invocation, cancellationToken).ConfigureAwait(false);
                if (string.Equals(result.ErrorCode, "ai.tool.approval_required", StringComparison.Ordinal))
                {
                    approvalRequired = true;
                }

                if (string.Equals(result.ErrorCode, "ai.tool.reconciliation_required", StringComparison.Ordinal))
                {
                    reconciliationRequired = true;
                }

                var payload = JsonSerializer.Serialize(new
                {
                    statusKey = result.StatusKey,
                    errorCode = result.ErrorCode,
                    value = result.Value,
                    isUntrusted = result.IsUntrusted,
                });
                messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, payload)]));
            }
        }

        throw new AgentToolLoopBudgetExceededException();
    }

    private static IReadOnlyList<AITool> BuildToolDescriptions(AgentToolRegistry registry, HashSet<string> allowed)
    {
        var schema = JsonSerializer.SerializeToElement(new { type = "object", additionalProperties = false });
        var tools = new List<AITool>();
        foreach (var name in allowed)
        {
            var tool = registry.Find(name);
            if (tool is null || !tool.IsEnabled)
            {
                continue;
            }

            tools.Add(AIFunctionFactory.CreateDeclaration(
                name,
                $"Read-only tool {name} version {tool.Version}.",
                schema));
        }

        return tools;
    }

    private static JsonElement SerializeSession(IReadOnlyList<ChatMessage> messages, string finalText)
    {
        var payload = new List<object>();
        foreach (var message in messages)
        {
            payload.Add(new { role = message.Role.Value, text = message.Text, contents = message.Contents.Count });
        }

        payload.Add(new { role = "assistant", text = finalText, contents = 0 });
        return JsonSerializer.SerializeToElement(payload);
    }

    private static JsonDocument ToJsonElement(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(JsonSerializer.Serialize(arguments));
    }

    private static long? AddUsage(long? current, long? delta) => current is null && delta is null ? null : (current ?? 0) + (delta ?? 0);
}

/// <summary>工具循环执行请求；RunId 与 OperationId 解析由调用方控制。</summary>
public sealed record AgentToolLoopRequest(
    Guid RunId,
    string DefinitionKey,
    int DefinitionVersion,
    string Prompt,
    IChatClient Client,
    IAgentToolExecutor Executor,
    AgentToolRegistry Registry,
    Func<string, Guid> ResolveOperationId,
    IReadOnlySet<string>? AdditionalAllowedToolNames = null);

/// <summary>单次运行结果；会话快照供持久化，不含凭据。</summary>
public sealed record AgentToolLoopResult(
    string Text,
    JsonElement Session,
    long? InputTokens,
    long? OutputTokens,
    int ToolSteps,
    bool ApprovalRequired = false,
    bool ReconciliationRequired = false);

/// <summary>达到定义允许的工具迭代上限。</summary>
public sealed class AgentToolLoopBudgetExceededException : InvalidOperationException
{
    public AgentToolLoopBudgetExceededException() : base("Agent tool loop exceeded the configured iteration budget.") { }
}
