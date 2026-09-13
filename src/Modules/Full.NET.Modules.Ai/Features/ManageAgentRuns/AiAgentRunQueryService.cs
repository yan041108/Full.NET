using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Runtime;

namespace Full.NET.Modules.Ai.Features.ManageAgentRuns;

/// <summary>只读查询本人 Agent 运行；跨租户与伪造标识均失败关闭。</summary>
internal sealed class AiAgentRunQueryService(AiAgentRunStore store, ICurrentTenant tenant)
{
    public async Task<Result<AiAgentRunResponse>> GetOwnedAsync(
        Guid runId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var scope = ResolveScope();
        var record = await store.FindOwnedAsync(runId, scope, actorUserId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return Result<AiAgentRunResponse>.Failure(new Error(
                AiErrorCodes.AgentRunNotFound,
                "The agent run was not found.",
                ErrorType.NotFound));
        }

        return Result<AiAgentRunResponse>.Success(new(
            record.Id,
            record.StatusKey,
            record.DefinitionKey,
            record.DefinitionVersion,
            record.DeadlineAtUtc,
            record.CreatedAtUtc,
            record.UpdatedAtUtc));
    }

    private string ResolveScope() => tenant.Id is { } id ? id.ToString("N") : tenant.IsHost ? "host"
        : throw new InvalidOperationException("Tenant scope is required for agent runs.");
}
