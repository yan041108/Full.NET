using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Tools;
using Microsoft.Extensions.AI;

namespace Full.NET.Agents.Runtime;

/// <summary>受控工具循环：模型请求经统一执行器派发，未注册工具不会执行 Handler。</summary>
public sealed class AgentToolLoop
{
    // 迭代上限不能约束单轮批量工具；模型与每个工具请求合计最多 64 步，拒绝项同样占用额度。
    private const int MaximumExecutionSteps = 64;
    private static readonly JsonElement EmptyObjectSchema = JsonDocument.Parse(
        """{"type":"object","additionalProperties":false}""").RootElement.Clone();

    /// <summary>
    /// 执行受控工具循环：按定义允许的最大迭代次数反复调用模型，模型与工具请求合计最多 64 步。
    /// </summary>
    /// <remarks>
    /// 未在定义允许清单中的工具不会进入执行器，直接返回 ai.tool.unavailable 错误载荷；单次循环内累计的 Token 用量与工具步数会写回结果，供调用方做预算核算。
    /// 模型返回的整批参数及调用身份先冻结；本次循环所有轮次的 callId 必须唯一，超限或无效批次不派发工具前缀。
    /// 审批或对账要求立即暂停执行，返回空文本与已发生用量，不派发同轮剩余工具或下一轮模型。
    /// </remarks>
    /// <param name="request">循环运行请求；包含定义键、提示词、客户端、执行器与工具注册表。</param>
    /// <param name="cancellationToken">用于取消模型调用与工具执行的令牌。</param>
    /// <returns>模型最终文本、会话快照、累计 Token 用量、工具步数以及是否触发审批/对账标志。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> 为 <see langword="null"/>。</exception>
    /// <exception cref="InvalidOperationException">定义不存在、不支持工具循环，或模型消息/工具参数不符合封闭准入协议。</exception>
    /// <exception cref="AgentToolLoopBudgetExceededException">达到迭代或累计步数上限，禁止继续派发模型或工具。</exception>
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
        var executionSteps = 0;
        var approvalRequired = false;
        var reconciliationRequired = false;
        var callIds = new HashSet<string>(StringComparer.Ordinal);

        for (var iteration = 0; iteration < definition.MaxToolIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (executionSteps >= MaximumExecutionSteps) throw new AgentToolLoopBudgetExceededException();
            executionSteps++;
            var response = await request.Client.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            // 供应商可以忽略令牌并返回；等待后必须复核，禁止取消后继续派发工具或交付完成结果。
            cancellationToken.ThrowIfCancellationRequested();
            inputTokens = AgentTokenUsage.Add(inputTokens, response.Usage?.InputTokenCount, iteration == 0);
            outputTokens = AgentTokenUsage.Add(outputTokens, response.Usage?.OutputTokenCount, iteration == 0);
            // 整批身份、参数、规模及剩余步数先冻结；不把可变模型对象跨工具 await 保存或部分派发。
            var admitted = AgentToolCallAdmission.Freeze(response, MaximumExecutionSteps - executionSteps, callIds, cancellationToken);
            var calls = admitted.Calls;
            if (calls.Count == 0)
            {
                var text = admitted.Messages.LastOrDefault()?.Text ?? string.Empty;
                return new(text, SerializeSession(messages, text), inputTokens, outputTokens, toolSteps, approvalRequired, reconciliationRequired);
            }

            messages.AddRange(admitted.Messages);
            foreach (var call in calls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (executionSteps >= MaximumExecutionSteps) throw new AgentToolLoopBudgetExceededException();
                executionSteps++;
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

                var operationId = request.ResolveOperationId(call.CallId);
                var invocation = new ToolInvocation(operationId, request.RunId, call.Name, tool.Version, call.Arguments);
                cancellationToken.ThrowIfCancellationRequested();
                var result = await request.Executor.ExecuteAsync(invocation, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
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
                // 暂停结果是执行边界，不能让模型或同轮后续调用继续消耗预算、产生副作用。
                if (approvalRequired || reconciliationRequired)
                {
                    return new(string.Empty, SerializeSession(messages, string.Empty), inputTokens, outputTokens,
                        toolSteps, approvalRequired, reconciliationRequired);
                }
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
/// <param name="ToolSteps">本次循环已处理的工具请求步数，包含本地拒绝项。</param>
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

/// <summary>达到工具迭代或模型／工具累计步数上限。</summary>
public sealed class AgentToolLoopBudgetExceededException : InvalidOperationException
{
    public AgentToolLoopBudgetExceededException() : base("Agent tool loop exceeded the execution budget.") { }
}
