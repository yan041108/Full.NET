using Full.NET.Agents.Runtime;

namespace Full.NET.Agents.Workflows;

/// <summary>模型调用并行，状态和检查点提交串行；工具及数据库 Session 不参与并发。</summary>
public sealed partial class AgentWorkflowRunner
{
    private static AgentWorkflowModelBudget? ModelBudget(AgentWorkflowRunRequest request, AgentWorkflowDefinition definition, int slots)
    {
        if (slots is < 1 or > 2 || request.InputTokenLimit is not > 0 || request.OutputTokenLimit is not > 0
            || request.InputTokenLimit > Full.NET.AI.Abstractions.Budgets.AiExecutionBudget.MaximumTokens
            || request.OutputTokenLimit > Full.NET.AI.Abstractions.Budgets.AiExecutionBudget.MaximumTokens) return null;
        var state = request.State;
        var hadModel = definition.Nodes.Take(state.NextNodeIndex).Any(n => n.Kind == AgentWorkflowNodeKind.ModelText)
            || state.NodeReceipts.Any(r => r.ToolVersion is null);
        var input = hadModel ? state.InputTokens : 0;
        var output = hadModel ? state.OutputTokens : 0;
        if (input is null or < 0 || output is null or < 0 || input >= request.InputTokenLimit || output >= request.OutputTokenLimit) return null;
        var inShare = (request.InputTokenLimit.Value - input.Value) / slots;
        var outShare = (request.OutputTokenLimit.Value - output.Value) / slots;
        return inShare > 0 && outShare > 0 ? new(inShare, outShare) : null;
    }

    private static async Task<AgentWorkflowRunResult?> RunParallelStageAsync(AgentWorkflowRunRequest request,
        AgentWorkflowDefinition definition, int index, CancellationToken token)
    {
        var state = request.State;
        if (request.PersistNodeAsync is null || request.ModelClientFactory is null)
            return Failed("ai.agent_run.parallel_runtime_unavailable");
        var end = definition.GroupEndIndex(index);
        var prefix = AgentWorkflowState.Create(state.SessionId);
        prefix.TargetChatSessionId = state.TargetChatSessionId;
        foreach (var prior in definition.Nodes.Take(definition.InputPrefixIndex(index)))
            if (state.Outputs.TryGetValue(prior.Key, out var output)) prefix.Outputs.Add(prior.Key, output);
            else return Failed("ai.agent_run.checkpoint_incompatible");

        while (state.NextNodeIndex < end)
        {
            token.ThrowIfCancellationRequested();
            var wave = Enumerable.Range(state.NextNodeIndex, end - state.NextNodeIndex)
                .Where(i => !state.NodeReceipts.Any(r => r.NodeIndex == i)).Take(2).ToArray();
            if (wave.Length == 0 || state.ExecutionSteps is null || state.ExecutionSteps > MaximumExecutionSteps - wave.Length)
                return Failed("ai.agent_run.tool_budget_exceeded");
            var allocation = ModelBudget(request, definition, wave.Length);
            if (allocation is null) return Failed("ai.budget.limit_exceeded");
            var intents = new List<(AgentWorkflowNodeReceipt Receipt, string Prompt)>();
            foreach (var nodeIndex in wave)
            {
                var node = definition.Nodes[nodeIndex];
                var prompt = AgentWorkflowNodeReceipt.BuildInput(node, prefix);
                var intent = new AgentWorkflowNodeReceipt(nodeIndex, node.Key, request.ResolveOperationId(node.Key),
                    AgentWorkflowNodeReceipt.Digest(prompt), null, null, null, null, allocation.InputTokenLimit, allocation.OutputTokenLimit);
                state.ExecutionSteps++;
                state.ParallelPendingNodes.Add(intent);
                if (!await request.PersistNodeAsync(intent, state, token).ConfigureAwait(false)) return Unknown();
                intents.Add((intent, prompt));
            }
            token.ThrowIfCancellationRequested();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            // 只有模型网络 I/O 在后台等待；所有共享状态由此方法的单一循环推进。
            var tasks = intents.Select(ExecuteAsync).ToList();
            Exception? failure = null;
            try
            {
                while (tasks.Count != 0)
                {
                    var completed = await Task.WhenAny(tasks).ConfigureAwait(false);
                    tasks.Remove(completed);
                    (AgentWorkflowNodeReceipt Receipt, AgentModelResult Result) value;
                    try { value = await completed.ConfigureAwait(false); }
                    catch (Exception exception)
                    {
                        failure ??= exception;
                        await cancellation.CancelAsync().ConfigureAwait(false);
                        continue;
                    }
                    token.ThrowIfCancellationRequested();
                    var receipt = value.Receipt;
                    var result = value.Result;
                    if (string.IsNullOrWhiteSpace(result.Text) || result.Text.Length > 65536 || result.InputTokens is < 0 || result.OutputTokens is < 0
                        || result.InputTokens > receipt.InputTokenLimit || result.OutputTokens > receipt.OutputTokenLimit)
                    {
                        failure ??= new InvalidDataException("Invalid branch result or usage.");
                        await cancellation.CancelAsync().ConfigureAwait(false);
                        continue;
                    }
                    var first = !state.NodeReceipts.Any(r => r.ToolVersion is null)
                        && !definition.Nodes.Take(definition.InputPrefixIndex(index)).Any(n => n.Kind == AgentWorkflowNodeKind.ModelText);
                    var input = AgentTokenUsage.Add(state.InputTokens, result.InputTokens, first);
                    var output = AgentTokenUsage.Add(state.OutputTokens, result.OutputTokens, first);
                    state.InputTokens = input; state.OutputTokens = output;
                    state.Outputs[receipt.NodeKey] = result.Text;
                    var committed = receipt with { OutputDigest = AgentWorkflowNodeReceipt.Digest(result.Text),
                        InputTokens = result.InputTokens, OutputTokens = result.OutputTokens };
                    state.NodeReceipts.Add(committed);
                    state.NodeReceipts.Sort((left, right) => left.NodeIndex.CompareTo(right.NodeIndex));
                    state.ParallelPendingNodes.RemoveAll(pending => pending.OperationId == receipt.OperationId);
                    while (state.NextNodeIndex < end && state.NodeReceipts.Any(r => r.NodeIndex == state.NextNodeIndex)) state.NextNodeIndex++;
                    if (!await request.PersistNodeAsync(committed, state, token).ConfigureAwait(false))
                    {
                        await cancellation.CancelAsync().ConfigureAwait(false);
                        failure ??= new IOException("Branch receipt commit is unknown.");
                    }
                }
            }
            finally
            {
                // 先结束所有分支，再释放根客户端与作用域，禁止遗留任务继续使用已关闭资源。
                if (tasks.Count != 0)
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch (Exception) { }
                }
            }
            token.ThrowIfCancellationRequested();
            if (failure is not null || state.ParallelPendingNodes.Count != 0) return Unknown();

            async Task<(AgentWorkflowNodeReceipt, AgentModelResult)> ExecuteAsync((AgentWorkflowNodeReceipt Receipt, string Prompt) intent)
            {
                using var client = request.ModelClientFactory(allocation);
                var result = await request.ModelRunner.RunAsync(client, intent.Prompt, cancellation.Token).ConfigureAwait(false);
                return (intent.Receipt, result);
            }
        }
        return null;
        AgentWorkflowRunResult Failed(string code) => new(AgentWorkflowRunStatus.Failed, state, null, code);
        AgentWorkflowRunResult Unknown() => new(AgentWorkflowRunStatus.ReconciliationRequired, state, null, "ai.tool.reconciliation_required");
    }
}
