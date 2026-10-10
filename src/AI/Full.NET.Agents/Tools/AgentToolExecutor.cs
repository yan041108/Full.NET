using System.Diagnostics;
using System.Text;
using Full.NET.AI.Abstractions.Tools;

namespace Full.NET.Agents.Tools;

/// <summary>请求作用域工具执行器；写工具在审批消费通过后才派发 Handler。</summary>
public sealed class AgentToolExecutor(
    IAgentToolRegistrySource registrySource,
    IToolAuthorizationPort authorization,
    IToolAuditPort audit,
    IAgentApprovalPort? approval = null) : IAgentToolExecutor
{
    private readonly HashSet<Guid> operations = [];
    private readonly object gate = new();
    private int attempts;
    private bool running;

    /// <inheritdoc />
    public async ValueTask<ToolExecutionResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (cancellationToken.IsCancellationRequested) return new("cancelled", null, "ai.tool.cancelled");
        var registry = await registrySource.GetRegistryAsync(cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested) return new("cancelled", null, "ai.tool.cancelled");
        var tool = registry.Find(invocation.ToolName ?? string.Empty);
        string? rejection;
        lock (gate)
        {
            rejection = running ? "ai.tool.busy" : ++attempts > 8 ? "ai.tool.budget_exceeded"
                : !operations.Add(invocation.OperationId) ? "ai.tool.duplicate_operation" : null;
            if (rejection is null) running = true;
        }
        if (rejection is not null) return await DenyAsync(invocation, tool, rejection).ConfigureAwait(false);
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(TimeSpan.FromSeconds(10));
            var token = budget.Token;
            var startedAt = Stopwatch.GetTimestamp();
            if (invocation.OperationId == Guid.Empty)
                rejection = "ai.tool.invalid_invocation";
            else if (tool is null || !tool.IsEnabled || tool.Handler is null)
                rejection = "ai.tool.unavailable";
            else if (tool.Version != invocation.ToolVersion)
                rejection = "ai.tool.version_mismatch";
            else if (invocation.Arguments.ValueKind != System.Text.Json.JsonValueKind.Object
                || Encoding.UTF8.GetByteCount(invocation.Arguments.GetRawText()) > 16384
                || !tool.Handler.ValidateArguments(invocation.Arguments))
                rejection = "ai.tool.invalid_arguments";
            if (rejection is not null) return await DenyAsync(invocation, tool, rejection).ConfigureAwait(false);

            var actor = await authorization.AuthorizeAsync(tool!.PermissionCode, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (actor is null) return await DenyAsync(invocation, tool, "ai.tool.authorization_required").ConfigureAwait(false);
            if (tool.SideEffectKey is not ("none" or "read"))
            {
                var approvalStatus = approval is null
                    ? AgentApprovalExecutionStatus.Required
                    : await approval.ValidateForExecutionAsync(invocation, tool.SideEffectKey, actor, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                rejection = approvalStatus switch
                {
                    AgentApprovalExecutionStatus.Approved => null,
                    AgentApprovalExecutionStatus.NotRequired => "ai.tool.approval_required",
                    AgentApprovalExecutionStatus.Denied => "ai.tool.approval_denied",
                    _ => "ai.tool.approval_required",
                };
                if (rejection is not null) return await DenyAsync(invocation, tool, rejection).ConfigureAwait(false);
            }
            // 复制参数，调用方释放 JSON 文档或并行准备下一次调用不能改变已批准输入。
            invocation = invocation with { Arguments = invocation.Arguments.Clone() };
            await audit.BeginAsync(invocation, tool.PermissionCode, "started", null, token).ConfigureAwait(false);
            ToolExecutionResult result;
            var bytes = 0;
            var dispatchedWrite = false;
            try
            {
                token.ThrowIfCancellationRequested();
                // 意图持久化期间目录可能撤回或升级；只允许原版本、权限和副作用策略继续派发。
                var currentTool = (await registrySource.GetRegistryAsync(token).ConfigureAwait(false)).Find(tool.Name);
                token.ThrowIfCancellationRequested();
                if (currentTool is null || !currentTool.IsEnabled || currentTool.Handler is null
                    || currentTool.Version != tool.Version || currentTool.PermissionCode != tool.PermissionCode
                    || currentTool.SideEffectKey != tool.SideEffectKey)
                    result = new("denied", null, "ai.tool.unavailable");
                else if (!currentTool.Handler.ValidateArguments(invocation.Arguments))
                    result = new("denied", null, "ai.tool.invalid_arguments");
                else
                {
                    // 意图写入存在 await 边界，派发前重新确认身份仍是原主体和原会话。
                    var current = await authorization.AuthorizeAsync(tool.PermissionCode, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (current != actor)
                        result = new("denied", null, "ai.tool.authorization_required");
                    else
                    {
                        token.ThrowIfCancellationRequested();
                        // 进入写 Handler 后，异常或取消不能证明副作用尚未发生，必须保守进入对账。
                        dispatchedWrite = tool.SideEffectKey is not ("none" or "read");
                        var value = await currentTool.Handler.ExecuteAsync(invocation, actor, token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        bytes = Encoding.UTF8.GetByteCount(value.GetRawText());
                        result = bytes > 65536 ? new("failed", null, dispatchedWrite ? "ai.tool.reconciliation_required" : "ai.tool.output_limit")
                            : new("succeeded", value.Clone(), null);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result = dispatchedWrite ? new("failed", null, "ai.tool.reconciliation_required")
                    : new("cancelled", null, "ai.tool.cancelled");
            }
            catch (Exception)
            {
                result = new("failed", null, dispatchedWrite ? "ai.tool.reconciliation_required" : "ai.tool.execution_failed");
            }
            // 回执使用独立取消预算；已派发写动作的结果无法保存时，不能伪装未执行。
            using var receipt = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await audit.CompleteAsync(invocation.OperationId, result.StatusKey, result.ErrorCode, bytes,
                    (int)Math.Min(int.MaxValue, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds), receipt.Token).ConfigureAwait(false);
            }
            catch (Exception) when (result.StatusKey == "succeeded" || dispatchedWrite)
            {
                return new("failed", null, "ai.tool.reconciliation_required");
            }

            // 回执可能忽略调用方取消；已保存的事实保持原样，但取消后不再交付成功输出。
            if (token.IsCancellationRequested)
                return dispatchedWrite ? new("failed", null, "ai.tool.reconciliation_required")
                    : new("cancelled", null, "ai.tool.cancelled");

            return result;
        }
        catch (OperationCanceledException) { return new("cancelled", null, "ai.tool.cancelled"); }
        catch (Exception) { return new("failed", null, "ai.tool.audit_or_authorization_failed"); }
        finally { lock (gate) running = false; }
    }

    private async ValueTask<ToolExecutionResult> DenyAsync(ToolInvocation invocation, AgentToolDefinition? tool, string code)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await audit.BeginAsync(invocation, tool?.PermissionCode ?? string.Empty, "denied", code, timeout.Token).ConfigureAwait(false);
            return new("denied", null, code);
        }
        catch (Exception) { return new("failed", null, "ai.tool.audit_failed"); }
    }
}
