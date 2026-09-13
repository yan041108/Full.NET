using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Agents.Runtime;
using Full.NET.Agents.Tools;
using Microsoft.Extensions.AI;

namespace Full.NET.Agents.Workflows;

/// <summary>显式工作流执行器；节点顺序静态，子步骤共用根 RunId 与独立 OperationId。</summary>
public sealed class AgentWorkflowRunner
{
    public async Task<AgentWorkflowRunResult> RunAsync(AgentWorkflowRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var definition = AgentWorkflowRegistry.Resolve(request.WorkflowKey, request.WorkflowVersion)
            ?? throw new InvalidOperationException("Unknown workflow definition.");

        var state = request.State;
        var approvalRequired = false;
        var reconciliationRequired = false;

        for (var index = state.NextNodeIndex; index < definition.Nodes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = definition.Nodes[index];
            switch (node.Kind)
            {
                case AgentWorkflowNodeKind.ToolRead:
                case AgentWorkflowNodeKind.ToolWrite:
                {
                    var toolResult = await ExecuteToolNodeAsync(request, node, state, cancellationToken).ConfigureAwait(false);
                    if (toolResult.ApprovalRequired)
                    {
                        approvalRequired = true;
                        state.NextNodeIndex = index;
                        return new(AgentWorkflowRunStatus.AwaitingApproval, state, null, null);
                    }

                    if (toolResult.ReconciliationRequired)
                    {
                        reconciliationRequired = true;
                        state.NextNodeIndex = index;
                        return new(AgentWorkflowRunStatus.ReconciliationRequired, state, null, null);
                    }

                    if (!toolResult.Succeeded)
                    {
                        state.NextNodeIndex = index;
                        return new(AgentWorkflowRunStatus.Failed, state, null, toolResult.ErrorCode ?? "ai.agent_run.workflow_tool_failed");
                    }

                    state.Outputs[node.Key] = toolResult.Output;
                    break;
                }
                case AgentWorkflowNodeKind.ModelText:
                {
                    var prompt = Interpolate(node.PromptTemplate!, state.Outputs);
                    var modelResult = await request.ModelRunner.RunAsync(request.Client, prompt, cancellationToken).ConfigureAwait(false);
                    state.InputTokens = AddUsage(state.InputTokens, modelResult.InputTokens);
                    state.OutputTokens = AddUsage(state.OutputTokens, modelResult.OutputTokens);
                    state.Outputs[node.Key] = modelResult.Text;
                    if (string.Equals(node.Key, "validate", StringComparison.Ordinal)
                        && !IsValidationPassed(modelResult.Text))
                    {
                        state.NextNodeIndex = index + 1;
                        return new(AgentWorkflowRunStatus.Failed, state, null, "ai.agent_run.workflow_validation_failed");
                    }

                    break;
                }
                default:
                    throw new InvalidOperationException($"Unsupported workflow node kind '{node.Kind}'.");
            }

            state.NextNodeIndex = index + 1;
        }

        if (approvalRequired)
        {
            return new(AgentWorkflowRunStatus.AwaitingApproval, state, null, null);
        }

        if (reconciliationRequired)
        {
            return new(AgentWorkflowRunStatus.ReconciliationRequired, state, null, null);
        }

        var finalText = state.Outputs.TryGetValue("summarize", out var summary) ? summary : string.Empty;
        return new(AgentWorkflowRunStatus.Completed, state, finalText, null);
    }

    private static async Task<ToolNodeResult> ExecuteToolNodeAsync(
        AgentWorkflowRunRequest request,
        AgentWorkflowNode node,
        AgentWorkflowState state,
        CancellationToken cancellationToken)
    {
        var toolName = node.ToolName!;
        var tool = request.Registry.Find(toolName);
        if (tool is null || !tool.IsEnabled)
        {
            return new(false, string.Empty, "ai.tool.unavailable", false, false);
        }

        using var arguments = BuildToolArguments(node, state);
        var operationId = request.ResolveOperationId(node.Key);
        var invocation = new ToolInvocation(operationId, request.RunId, toolName, tool.Version, arguments.RootElement.Clone());
        var result = await request.Executor.ExecuteAsync(invocation, cancellationToken).ConfigureAwait(false);
        if (string.Equals(result.ErrorCode, "ai.tool.approval_required", StringComparison.Ordinal))
        {
            return new(false, string.Empty, result.ErrorCode, true, false);
        }

        if (string.Equals(result.ErrorCode, "ai.tool.reconciliation_required", StringComparison.Ordinal))
        {
            return new(false, string.Empty, result.ErrorCode, false, true);
        }

        if (!string.Equals(result.StatusKey, "succeeded", StringComparison.Ordinal))
        {
            return new(false, string.Empty, result.ErrorCode ?? "ai.tool.failed", false, false);
        }

        var output = result.Value?.GetRawText() ?? "null";
        return new(true, output, null, false, false);
    }

    private static JsonDocument BuildToolArguments(AgentWorkflowNode node, AgentWorkflowState state)
    {
        if (string.Equals(node.ToolName, "ai.chat.sessions.rename", StringComparison.Ordinal))
        {
            var title = state.Outputs.TryGetValue("summarize", out var proposed) ? proposed.Trim() : string.Empty;
            if (title.Length > 256)
            {
                title = title[..256];
            }

            return JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                sessionId = state.SessionId,
                title,
            }));
        }

        return JsonDocument.Parse("{}");
    }

    private static bool IsValidationPassed(string text) =>
        text.Contains("VALID", StringComparison.OrdinalIgnoreCase)
        && !text.Contains("INVALID", StringComparison.OrdinalIgnoreCase);

    private static string Interpolate(string template, IReadOnlyDictionary<string, string> outputs)
    {
        var result = template;
        foreach (var (key, value) in outputs)
        {
            result = result.Replace($"{{{{{key}}}}}", value, StringComparison.Ordinal);
        }

        return result;
    }

    private static long? AddUsage(long? current, long? delta) =>
        current is null && delta is null ? null : (current ?? 0) + (delta ?? 0);

    private sealed record ToolNodeResult(
        bool Succeeded,
        string Output,
        string? ErrorCode,
        bool ApprovalRequired,
        bool ReconciliationRequired);
}

/// <summary>工作流执行请求；OperationId 由调用方按节点键派生。</summary>
public sealed record AgentWorkflowRunRequest(
    Guid RunId,
    string WorkflowKey,
    int WorkflowVersion,
    AgentWorkflowState State,
    IChatClient Client,
    IAgentModelRunner ModelRunner,
    IAgentToolExecutor Executor,
    AgentToolRegistry Registry,
    Func<string, Guid> ResolveOperationId);
