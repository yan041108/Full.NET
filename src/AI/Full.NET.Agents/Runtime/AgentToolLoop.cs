using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Tools;
using Microsoft.Extensions.AI;

namespace Full.NET.Agents.Runtime;

/// <summary>受控工具循环：模型请求经统一执行器派发，未注册工具不会执行 Handler。</summary>
public sealed class AgentToolLoop
{
    private static readonly JsonElement EmptyObjectSchema = JsonDocument.Parse(
        """{"type":"object","additionalProperties":false}""").RootElement.Clone();

    /// <summary>
    /// 执行受控工具循环：按定义允许的最大迭代次数反复调用模型，逐轮将工具调用交给执行器处理，直到模型不再请求工具或触达迭代上限。
    /// </summary>
    /// <remarks>
    /// 未在定义允许清单中的工具不会进入执行器，直接返回 ai.tool.unavailable 错误载荷；单次循环内累计的 Token 用量与工具步数会写回结果，供调用方做预算核算。
    /// </remarks>
    /// <param name="request">循环运行请求；包含定义键、提示词、客户端、执行器与工具注册表。</param>
    /// <param name="cancellationToken">用于取消模型调用与工具执行的令牌。</param>
    /// <returns>模型最终文本、会话快照、累计 Token 用量、工具步数以及是否触发审批/对账标志。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvalidOperationException">定义不存在，或定义不支持工具循环（MaxToolIterations 非正数）。</exception>
    /// <exception cref="AgentToolLoopBudgetExceededException">达到定义允许的工具迭代上限仍未得到无工具调用的最终响应。</exception>
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
                            JsonSerializer.Serialize(
                                new ToolLoopDeniedPayload("denied", "ai.tool.unavailable"),
                                AgentToolLoopJsonSerializerContext.Default.ToolLoopDeniedPayload))
                    ]));
                    continue;
                }

                var tool = request.Registry.Find(call.Name);
                if (tool is null || !tool.IsEnabled)
                {
                    messages.Add(new ChatMessage(ChatRole.Tool,
                    [
                        new FunctionResultContent(call.CallId,
                            JsonSerializer.Serialize(
                                new ToolLoopDeniedPayload("denied", "ai.tool.unavailable"),
                                AgentToolLoopJsonSerializerContext.Default.ToolLoopDeniedPayload))
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

                var payload = JsonSerializer.Serialize(
                    new ToolLoopExecutionPayload(
                        result.StatusKey,
                        result.ErrorCode,
                        result.Value,
                        result.IsUntrusted),
                    AgentToolLoopJsonSerializerContext.Default.ToolLoopExecutionPayload);
                messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, payload)]));
            }
        }

        throw new AgentToolLoopBudgetExceededException();
    }

    private static IReadOnlyList<AITool> BuildToolDescriptions(AgentToolRegistry registry, HashSet<string> allowed)
    {
        var schema = EmptyObjectSchema;
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
        var entries = messages
            .Select(message => new AgentSessionMessageEntry(message.Role.Value, message.Text, message.Contents.Count))
            .ToList();
        entries.Add(new AgentSessionMessageEntry("assistant", finalText, 0));
        return JsonSerializer.SerializeToElement(
            new AgentSessionSnapshot(entries),
            AgentToolLoopJsonSerializerContext.Default.AgentSessionSnapshot);
    }

    private static JsonDocument ToJsonElement(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return JsonDocument.Parse("{}");
        }

        var converted = new Dictionary<string, JsonElement>(arguments.Count, StringComparer.Ordinal);
        foreach (var (key, value) in arguments)
        {
            converted[key] = ToJsonElementValue(value);
        }

        return JsonDocument.Parse(JsonSerializer.Serialize(
            converted,
            AgentToolLoopJsonSerializerContext.Default.DictionaryStringJsonElement));
    }

    private static JsonElement ToJsonElementValue(object? value) => value switch
    {
        null => default,
        JsonElement element => element,
        bool boolean => JsonSerializer.SerializeToElement(boolean, AgentToolLoopJsonSerializerContext.Default.Boolean),
        string text => JsonSerializer.SerializeToElement(text, AgentToolLoopJsonSerializerContext.Default.String),
        int number => JsonSerializer.SerializeToElement(number, AgentToolLoopJsonSerializerContext.Default.Int32),
        long number => JsonSerializer.SerializeToElement(number, AgentToolLoopJsonSerializerContext.Default.Int64),
        double number => JsonSerializer.SerializeToElement(number, AgentToolLoopJsonSerializerContext.Default.Double),
        _ => JsonSerializer.SerializeToElement(value.ToString(), AgentToolLoopJsonSerializerContext.Default.String),
    };

    private static long? AddUsage(long? current, long? delta) => current is null && delta is null ? null : (current ?? 0) + (delta ?? 0);
}

/// <summary>工具循环执行请求；RunId 与 OperationId 解析由调用方控制。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="RunId">本次运行的稳定标识；用于关联工具调用与持久化会话。</param>
/// <param name="DefinitionKey">代理定义键；与 DefinitionVersion 共同定位代理行为。</param>
/// <param name="DefinitionVersion">代理定义版本；用于选择允许的工具集与迭代上限。</param>
/// <param name="Prompt">本轮用户提示词；作为首条用户消息注入会话。</param>
/// <param name="Client">用于调用 LLM 的聊天客户端。</param>
/// <param name="Executor">工具调用统一执行器；负责权限校验、审批与对账。</param>
/// <param name="Registry">工具注册表；用于构建工具描述与查找工具元数据。</param>
/// <param name="ResolveOperationId">根据模型生成的 callId 解析业务操作标识的回调。</param>
/// <param name="AdditionalAllowedToolNames">定义允许清单之外临时放行的工具名集合；为 <see langword="null"/> 时不追加。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Text">模型最终无工具调用时输出的文本。</param>
/// <param name="Session">序列化后的会话消息快照；用于审计与重放。</param>
/// <param name="InputTokens">累计输入 Token 数；模型未上报用量时为 <see langword="null"/>。</param>
/// <param name="OutputTokens">累计输出 Token 数；模型未上报用量时为 <see langword="null"/>。</param>
/// <param name="ToolSteps">本次循环实际执行的工具调用步数。</param>
/// <param name="ApprovalRequired">是否有工具触发审批要求；为 <see langword="true"/> 时调用方须介入审批流程。</param>
/// <param name="ReconciliationRequired">是否有工具触发对账要求；为 <see langword="true"/> 时调用方须执行对账。</param>
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
