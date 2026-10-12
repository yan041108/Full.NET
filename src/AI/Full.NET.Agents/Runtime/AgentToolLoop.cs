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
        using var observation = AgentExecutionTelemetry.Start(request.RunId, "tool_loop", cancellationToken: cancellationToken);
        try
        {
            var result = await RunCoreAsync(request, cancellationToken).ConfigureAwait(false);
            observation.Outcome = result.ApprovalRequired ? "approval_required"
                : result.ReconciliationRequired ? "reconciliation_required" : "completed";
            return result;
        }
        catch (OperationCanceledException) { observation.Outcome = "cancelled"; throw; }
        catch (AgentToolLoopBudgetExceededException) { observation.Outcome = "budget_rejected"; throw; }
    }

    private async Task<AgentToolLoopResult> RunCoreAsync(AgentToolLoopRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var definition = AgentDefinitionRegistry.Resolve(request.DefinitionKey, request.DefinitionVersion)
            ?? throw new InvalidOperationException("Unknown agent definition.");
        if (definition.MaxToolIterations <= 0)
        {
            throw new InvalidOperationException("Definition does not support tool loop execution.");
        }

        var state = request.ResumeCheckpoint is null
            ? new AgentToolLoopState { RunId = request.RunId, DefinitionKey = request.DefinitionKey,
                DefinitionVersion = request.DefinitionVersion, InputDigest = AgentToolLoopCheckpoint.Digest(request.Prompt) }
            : AgentToolLoopCheckpoint.Restore(request.ResumeCheckpoint, request.RunId, request.DefinitionKey,
                request.DefinitionVersion, AgentToolLoopCheckpoint.Digest(request.Prompt), request.ResolveOperationId);
        var approvalCall = request.RecheckApproval ? AgentToolLoopCheckpoint.ApprovalCall(state) : null;
        if (AgentToolLoopCheckpoint.Summarize(state).ReconciliationRequired && approvalCall is null
            || approvalCall is not null && request.PersistCheckpoint is null) AgentToolLoopCheckpoint.Invalid();
        if (state.Phase == "completed") return AgentToolLoopCheckpoint.CompletedResult(state, request.Prompt);
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
        var callIds = state.Rounds.SelectMany(AgentToolLoopCheckpoint.Calls)
            .Select(c => c.CallId!).ToHashSet(StringComparer.Ordinal);

        async Task SaveAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.PersistCheckpoint is not null
                && !await request.PersistCheckpoint(AgentToolLoopCheckpoint.Serialize(state), cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Agent tool loop checkpoint commit was not confirmed.");
            cancellationToken.ThrowIfCancellationRequested();
        }

        while (true)
        {
            // 先消化持久批次的未完成后缀；原模型请求和已确认工具结果不再执行。
            if (state.Rounds.Count != 0)
            {
                foreach (var call in AgentToolLoopCheckpoint.Calls(state.Rounds[^1]).Where(c => c.ResultPayload is null || c == approvalCall))
                {
                    using var toolObservation = AgentExecutionTelemetry.Start(request.RunId, "tool", call.OperationId,
                        cancellationToken: cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (AgentToolLoopCheckpoint.Summarize(state).ExecutionSteps >= MaximumExecutionSteps) throw new AgentToolLoopBudgetExceededException();
                    if (call == approvalCall)
                    {
                        // 重新核对不授予审批，仍走原执行器；原拒绝和本次重入分别占用步数。
                        state.Version = 2; call.ApprovalRetries++; call.ResultPayload = null; approvalCall = null;
                    }
                    if (call.ToolVersion is null)
                    {
                        call.ResultPayload = JsonSerializer.Serialize(new ToolLoopDeniedPayload("denied", "ai.tool.unavailable"),
                            AgentToolLoopJsonSerializerContext.Default.ToolLoopDeniedPayload);
                    }
                    else
                    {
                        // 审批重入沿用已冻结身份；解析器只为从未派发过的后缀创建新身份。
                        call.OperationId ??= request.ResolveOperationId(call.CallId!);
                        if (call.OperationId == Guid.Empty) AgentToolLoopCheckpoint.Invalid();
                        toolObservation.SetOperationId(call.OperationId.Value);
                        state.Phase = "tool_pending";
                        await SaveAsync().ConfigureAwait(false);
                        var tool = request.Registry.Find(call.Name!);
                        // 恢复不能把原参数绑定到新工具版本或已撤回的允许清单；执行器仍复核当前权限。
                        var result = !allowed.Contains(call.Name!) || tool is null || !tool.IsEnabled || tool.Version != call.ToolVersion
                            ? new ToolExecutionResult("denied", null, "ai.tool.unavailable")
                            : await request.Executor.ExecuteAsync(new(call.OperationId.Value, request.RunId,
                                call.Name!, call.ToolVersion.Value, call.Arguments!.Value), cancellationToken).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        call.ResultPayload = JsonSerializer.Serialize(new ToolLoopExecutionPayload(result.StatusKey, result.ErrorCode,
                            result.Value, result.IsUntrusted), AgentToolLoopJsonSerializerContext.Default.ToolLoopExecutionPayload);
                    }
                    var receipt = AgentToolLoopCheckpoint.ReadResult(call.ResultPayload);
                    toolObservation.Outcome = receipt.ErrorCode == "ai.tool.approval_required" ? "approval_required"
                        : receipt.ErrorCode == "ai.tool.reconciliation_required" ? "reconciliation_required"
                        : receipt.StatusKey is "succeeded" or "denied" or "cancelled" ? receipt.StatusKey : "failed";
                    state.Phase = AgentToolLoopCheckpoint.IsPause(receipt.ErrorCode) ? "blocked" : "tools_ready";
                    await SaveAsync().ConfigureAwait(false);
                    if (state.Phase == "blocked")
                    {
                        var summary = AgentToolLoopCheckpoint.Summarize(state);
                        return new(string.Empty, SerializeSession(AgentToolLoopCheckpoint.History(state, request.Prompt), string.Empty),
                            summary.InputTokens, summary.OutputTokens, summary.ToolSteps,
                            receipt.ErrorCode == "ai.tool.approval_required", receipt.ErrorCode == "ai.tool.reconciliation_required");
                    }
                }
            }
            var info = AgentToolLoopCheckpoint.Summarize(state);
            if (state.Rounds.Count >= definition.MaxToolIterations || info.ExecutionSteps >= MaximumExecutionSteps)
                throw new AgentToolLoopBudgetExceededException();
            // 每轮模型都先保存意图；网络返回前中断只能对账，不能从旧 ready 推断本轮未执行。
            state.Phase = "model_pending";
            await SaveAsync().ConfigureAwait(false);
            ChatResponse response;
            using (var modelObservation = AgentExecutionTelemetry.Start(request.RunId, "model", stepIndex: info.ExecutionSteps,
                cancellationToken: cancellationToken))
            {
                try { response = await request.Client.GetResponseAsync(AgentToolLoopCheckpoint.History(state, request.Prompt), options, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { modelObservation.Outcome = "cancelled"; throw; }
                modelObservation.Outcome = "succeeded";
            }
            cancellationToken.ThrowIfCancellationRequested();
            var admitted = AgentToolCallAdmission.Freeze(response, MaximumExecutionSteps - info.ExecutionSteps - 1, callIds, cancellationToken);
            var round = new AgentToolLoopRound { InputTokens = response.Usage?.InputTokenCount, OutputTokens = response.Usage?.OutputTokenCount };
            foreach (var message in admitted.Messages)
            {
                var saved = new AgentToolLoopMessage { AuthorName = message.AuthorName, MessageId = message.MessageId, CreatedAt = message.CreatedAt };
                foreach (var content in message.Contents)
                {
                    if (content is TextContent text) saved.Contents.Add(new() { Kind = "text", Text = text.Text });
                    else if (content is FunctionCallContent function)
                    {
                        var call = admitted.Calls.Single(c => c.CallId == function.CallId);
                        var tool = allowed.Contains(call.Name) ? request.Registry.Find(call.Name) : null;
                        saved.Contents.Add(new() { Kind = "call", CallId = call.CallId, Name = call.Name, Arguments = call.Arguments,
                            ToolVersion = tool is { IsEnabled: true } ? tool.Version : null });
                    }
                }
                round.Messages.Add(saved);
            }
            state.Rounds.Add(round);
            // 累计用量保持未知传播；无工具的最终模型回执也须在交付结果前持久化。
            _ = AgentToolLoopCheckpoint.Summarize(state);
            state.Phase = admitted.Calls.Count == 0 ? "completed" : "tools_ready";
            await SaveAsync().ConfigureAwait(false);
            if (state.Phase == "completed") return AgentToolLoopCheckpoint.CompletedResult(state, request.Prompt);
        }
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

    internal static JsonElement SerializeSession(IReadOnlyList<ChatMessage> messages, string finalText)
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
/// <param name="ResumeCheckpoint">经可信存储校验的逐步检查点；未知派发不得自动恢复。</param>
/// <param name="PersistCheckpoint">在外部派发之前和结果之后提交不可变 JSON 的回调；false 或异常必须停止，调用方负责租约、事务与加密。</param>
public sealed record AgentToolLoopRequest(
    Guid RunId,
    string DefinitionKey,
    int DefinitionVersion,
    string Prompt,
    IChatClient Client,
    IAgentToolExecutor Executor,
    AgentToolRegistry Registry,
    Func<string, Guid> ResolveOperationId,
    IReadOnlySet<string>? AdditionalAllowedToolNames = null,
    string? ResumeCheckpoint = null,
    Func<string, CancellationToken, Task<bool>>? PersistCheckpoint = null)
{
    /// <summary>允许确定的末尾审批拒绝重新进入原执行器复核；必须提供持久化回调，不授予权限或审批。</summary>
    public bool RecheckApproval { get; init; }
}

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
