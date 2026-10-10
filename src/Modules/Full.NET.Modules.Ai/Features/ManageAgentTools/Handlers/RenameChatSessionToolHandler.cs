using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageAgentApprovals;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Serialization;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;

/// <summary>首个写工具：审批消费与会话重命名在同一本地事务提交。</summary>
internal sealed class RenameChatSessionToolHandler(
    AiChatSessionManagementService sessions,
    AiChatSessionQueryService queries,
    AiAgentApprovalConsumption approvals,
    ICommandTransaction transaction,
    ICurrentTenant tenant) : IAgentToolHandler
{
    internal const string ToolName = "ai.chat.sessions.rename";
    internal const int ToolVersion = 1;

    public bool ValidateArguments(JsonElement arguments) => RenameChatSessionArgumentParser.Parse(arguments) is not null;

    public async ValueTask<JsonElement> ExecuteAsync(ToolInvocation invocation, ToolActor actor, CancellationToken cancellationToken)
    {
        var parsed = RenameChatSessionArgumentParser.Parse(invocation.Arguments)
            ?? throw new InvalidOperationException("Invalid tool arguments.");
        if (actor.UserId == Guid.Empty || actor.TenantId != tenant.Id || (actor.TenantId is null && !tenant.IsHost))
        {
            throw new InvalidOperationException("Tool scope mismatch.");
        }

        var scope = AiChatScope.Resolve(tenant);
        var session = await queries.FindOwnedSessionAsync(scope, parsed.SessionId, actor.UserId, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (session is null)
        {
            throw new InvalidOperationException("Tool session not found.");
        }

        // 目标查询期间权限或原会话可能失效；Identity 授权必须在 AI 本地事务外完成。
        var admission = await approvals.ValidateForToolExecutionAsync(invocation, actor, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (admission != AgentApprovalExecutionStatus.Approved)
            throw new InvalidOperationException("The agent approval is no longer executable.");

        var result = await transaction.ExecuteResultAsync(async token =>
        {
            if (!await approvals.TryConsumeForToolExecutionAsync(invocation, actor, token).ConfigureAwait(false))
            {
                return Result<JsonElement>.Failure(new Error(
                    AiErrorCodes.AgentApprovalNotDecidable,
                    "The agent approval could not be consumed.",
                    ErrorType.BusinessRule));
            }

            var renamed = await sessions.RenameInCurrentTransactionAsync(
                parsed.SessionId,
                actor.UserId,
                new UpdateAiChatSessionRequest(parsed.Title, session.Version),
                token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!renamed.IsSuccess)
            {
                return Result<JsonElement>.Failure(renamed.Error!);
            }

            return Result<JsonElement>.Success(JsonSerializer.SerializeToElement(
                new RenameChatSessionResult(parsed.SessionId, parsed.Title),
                AiToolJsonSerializerContext.Default.RenameChatSessionResult));
        }, cancellationToken).ConfigureAwait(false);

        // 提交等待期间取消时不得交付成功；已提交写入仍需按工具未知结果语义核对。
        cancellationToken.ThrowIfCancellationRequested();

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Error?.Code ?? "rename_failed");
        }

        return result.Value;
    }
}
