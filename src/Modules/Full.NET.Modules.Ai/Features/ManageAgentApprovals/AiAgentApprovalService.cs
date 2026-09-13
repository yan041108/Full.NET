using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Agents.Approvals;
using Full.NET.Agents.Tools;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageAgentDelegations;
using Full.NET.Modules.Ai.Features.ManageAgentTools;
using Full.NET.Modules.Ai.Features.ManageAgentTools.Handlers;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageAgentApprovals;

/// <summary>创建、查询与决定 Agent 写工具审批。</summary>
internal sealed class AiAgentApprovalService(
    IQueryExecutor queries,
    ICommandExecutor commands,
    AiAgentRunStore runs,
    AiChatSessionQueryService sessions,
    AiAgentDelegationService delegations,
    AgentToolRegistryFactory toolRegistryFactory,
    AiAgentApprovalConsumption approvalConsumption,
    AiAgentRunApprovalGate approvalGate,
    ICurrentSessionAuthorization sessionAuthorization,
    ICurrentTenant tenant,
    IClock clock,
    IIdGenerator ids,
    IOptions<AiAgentRuntimeOptions> runtimeOptions)
{
    public async Task<Result<CreateAiAgentApprovalResponse>> CreateAsync(
        CreateAiAgentApprovalRequest request,
        SessionBindingSnapshot binding,
        CancellationToken cancellationToken = default)
    {
        if (request.RunId == Guid.Empty || request.OperationId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.ToolName) || string.IsNullOrWhiteSpace(request.ArgumentsJson))
        {
            return Invalid<CreateAiAgentApprovalResponse>();
        }

        var tools = await toolRegistryFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        var tool = tools.Find(request.ToolName);
        if (tool is null || !tool.IsEnabled || tool.SideEffectKey is "none" or "read"
            || tool.Version != request.ToolVersion)
        {
            return Invalid<CreateAiAgentApprovalResponse>();
        }

        var scope = ResolveScope();
        var run = await runs.FindOwnedAsync(request.RunId, scope, binding.UserId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return NotFound<CreateAiAgentApprovalResponse>();
        }

        using var arguments = JsonDocument.Parse(request.ArgumentsJson);
        if (!tool.Handler!.ValidateArguments(arguments.RootElement))
        {
            return Invalid<CreateAiAgentApprovalResponse>();
        }

        var presentation = await BuildPresentationAsync(tool.Name, arguments.RootElement, binding.UserId, cancellationToken)
            .ConfigureAwait(false);
        if (presentation is null)
        {
            return Invalid<CreateAiAgentApprovalResponse>();
        }

        var existing = await queries.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(
            AiAgentApprovalSql.FindByOperation,
            AiSqlParameters.Create(("OperationId", request.OperationId)),
            cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<CreateAiAgentApprovalResponse>.Success(new(existing.Id));
        }

        var approvalId = ids.NewId();
        var now = clock.UtcNow;
        var expiresAt = now.AddSeconds(Math.Clamp(runtimeOptions.Value.MaxRunDurationSeconds, 60, 3600));
        var hash = AgentApprovalGate.ComputeArgumentsHash(arguments.RootElement);
        try
        {
            var affected = await commands.ExecuteAsync(
                AiAgentApprovalSql.Insert,
                AiSqlParameters.Create(
                    ("Id", approvalId),
                    ("ScopeKey", scope),
                    ("TenantId", tenant.Id),
                    ("RunId", request.RunId),
                    ("OperationId", request.OperationId),
                    ("SessionId", run.SessionId),
                    ("ToolName", tool.Name),
                    ("ToolVersion", tool.Version),
                    ("ArgumentsHash", hash),
                    ("ArgumentsProtected", request.ArgumentsJson),
                    ("PolicyVersion", AgentApprovalGate.CurrentPolicyVersion),
                    ("PresentationJson", JsonSerializer.Serialize(presentation, AiJsonSerializerContext.Default.AiAgentApprovalPresentation)),
                    ("RequestedBy", binding.UserId),
                    ("DecisionKey", AgentApprovalDecisionKeys.Pending),
                    ("ExpiresAtUtc", expiresAt),
                    ("Now", now)),
                cancellationToken).ConfigureAwait(false);
            if (affected != 1)
            {
                throw new InvalidOperationException("Agent approval was not created.");
            }
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            var raced = await queries.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(
                AiAgentApprovalSql.FindByOperation,
                AiSqlParameters.Create(("OperationId", request.OperationId)),
                cancellationToken).ConfigureAwait(false);
            return raced is null
                ? throw new InvalidOperationException("Agent approval idempotency race lost.", ex)
                : Result<CreateAiAgentApprovalResponse>.Success(new(raced.Id));
        }

        return Result<CreateAiAgentApprovalResponse>.Success(new(approvalId));
    }

    public async Task<Result<AiAgentApprovalResponse>> GetOwnedAsync(
        Guid approvalId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        var record = await queries.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(
            AiAgentApprovalSql.FindOwnedById,
            AiSqlParameters.Create(("Id", approvalId), ("ScopeKey", ResolveScope()), ("ActorUserId", actorUserId)),
            cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            record = await queries.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(
                AiAgentApprovalSql.FindById,
                AiSqlParameters.Create(("Id", approvalId), ("ScopeKey", ResolveScope())),
                cancellationToken).ConfigureAwait(false);
            if (record is null
                || !await CanDecideAsync(actorUserId, record, cancellationToken).ConfigureAwait(false))
            {
                return Result<AiAgentApprovalResponse>.Failure(new Error(
                    AiErrorCodes.AgentApprovalNotFound,
                    "The agent approval was not found.",
                    ErrorType.NotFound));
            }
        }

        return Result<AiAgentApprovalResponse>.Success(Map(record));
    }

    public async Task<Result<AiAgentApprovalResponse>> DecideAsync(
        Guid approvalId,
        Guid approverUserId,
        DecideAiAgentApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        var record = await queries.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(
            AiAgentApprovalSql.FindById,
            AiSqlParameters.Create(("Id", approvalId), ("ScopeKey", ResolveScope())),
            cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return Result<AiAgentApprovalResponse>.Failure(new Error(
                AiErrorCodes.AgentApprovalNotFound,
                "The agent approval was not found.",
                ErrorType.NotFound));
        }

        if (!string.Equals(record.DecisionKey, AgentApprovalDecisionKeys.Pending, StringComparison.Ordinal)
            || record.ConsumedAtUtc is not null
            || record.ExpiresAtUtc <= clock.UtcNow)
        {
            return Result<AiAgentApprovalResponse>.Failure(new Error(
                AiErrorCodes.AgentApprovalNotDecidable,
                "The agent approval cannot be decided in its current state.",
                ErrorType.BusinessRule));
        }

        if (!await CanDecideAsync(approverUserId, record, cancellationToken).ConfigureAwait(false))
        {
            return Result<AiAgentApprovalResponse>.Failure(new Error(
                AiErrorCodes.AgentApprovalNotAuthorized,
                "The current user is not authorized to decide this approval.",
                ErrorType.Forbidden));
        }

        var decision = request.Approve ? AgentApprovalDecisionKeys.Approved : AgentApprovalDecisionKeys.Denied;
        var now = clock.UtcNow;
        var affected = await commands.ExecuteAsync(
            AiAgentApprovalSql.Decide,
            AiSqlParameters.Create(
                ("Id", approvalId),
                ("ScopeKey", ResolveScope()),
                ("DecisionKey", decision),
                ("ApproverId", approverUserId),
                ("ExpectedVersion", request.ExpectedVersion),
                ("Now", now)),
            cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            return Result<AiAgentApprovalResponse>.Failure(new Error(
                AiErrorCodes.AgentApprovalConcurrencyConflict,
                "The agent approval was changed by another request.",
                ErrorType.Conflict));
        }

        record.DecisionKey = decision;
        record.ApproverId = approverUserId;
        record.Version = request.ExpectedVersion + 1;
        record.UpdatedAtUtc = now;
        return Result<AiAgentApprovalResponse>.Success(Map(record));
    }

    internal ValueTask<AgentApprovalBinding?> FindBindingByOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken) =>
        approvalConsumption.FindBindingByOperationAsync(operationId, cancellationToken);

    public ValueTask<bool> HasApprovedUnconsumedForRunAsync(
        Guid runId,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        approvalGate.HasApprovedUnconsumedForRunAsync(runId, actorUserId, cancellationToken);

    private async Task<AiAgentApprovalPresentation?> BuildPresentationAsync(
        string toolName,
        JsonElement arguments,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(toolName, "ai.chat.sessions.rename", StringComparison.Ordinal))
        {
            var parsed = RenameChatSessionArgumentParser.Parse(arguments);
            if (parsed is null)
            {
                return null;
            }

            var scope = AiChatScope.Resolve(tenant);
            var session = await sessions.FindOwnedSessionAsync(scope, parsed.SessionId, ownerUserId, cancellationToken)
                .ConfigureAwait(false);
            if (session is null)
            {
                return null;
            }

            return new(
                "Rename AI chat session",
                session.Title,
                parsed.Title,
                "Owned chat session",
                null,
                null);
        }

        return null;
    }

    private static AiAgentApprovalResponse Map(AiAgentApprovalRecord record)
    {
        var presentation = JsonSerializer.Deserialize(record.PresentationJson, AiJsonSerializerContext.Default.AiAgentApprovalPresentation)
            ?? new AiAgentApprovalPresentation("Unknown", "Unknown", "Unknown", "Unknown", null, null);
        return new(
            record.Id,
            record.RunId,
            record.OperationId,
            record.ToolName,
            record.ToolVersion,
            record.ArgumentsHash,
            record.DecisionKey,
            presentation.Action,
            presentation.Target,
            presentation.Change,
            presentation.Scope,
            presentation.CostCeiling,
            presentation.Currency,
            record.ExpiresAtUtc,
            record.ConsumedAtUtc,
            record.Version,
            record.CreatedAtUtc);
    }

    private async Task<bool> CanDecideAsync(
        Guid approverUserId,
        AiAgentApprovalRecord record,
        CancellationToken cancellationToken)
    {
        if (await sessionAuthorization.AuthorizeAsync(AiAgentApprovalPermissions.Decide, cancellationToken).ConfigureAwait(false)
            is { UserId: var userId } && userId == approverUserId)
        {
            return true;
        }

        return await delegations.HasActiveAsync(
            record.RequestedBy,
            approverUserId,
            record.ToolName,
            AiAgentApprovalPermissions.Decide,
            cancellationToken).ConfigureAwait(false);
    }

    private string ResolveScope() => tenant.Id is { } id ? id.ToString("N") : tenant.IsHost ? "host"
        : throw new InvalidOperationException("Tenant scope is required for agent approvals.");

    private static Result<T> Invalid<T>() => Result<T>.Failure(new Error(
        AiErrorCodes.AgentApprovalInvalid,
        "The agent approval request is invalid.",
        ErrorType.Validation));

    private static Result<T> NotFound<T>() => Result<T>.Failure(new Error(
        AiErrorCodes.AgentApprovalNotFound,
        "The agent approval was not found.",
        ErrorType.NotFound));

    private static bool IsUniqueViolation(Exception ex) =>
        ex.Message.Contains("UX_fn_ai_agent_approval_Operation", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
}
