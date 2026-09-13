using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Agents.Tools;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageAgentDelegations;

/// <summary>创建、列出与撤销持久委托；不存储访问令牌。</summary>
internal sealed class AiAgentDelegationService(
    IQueryExecutor queries,
    ICommandExecutor commands,
    IAgentToolRegistrySource toolRegistrySource,
    ICurrentTenant tenant,
    IClock clock,
    IIdGenerator ids,
    IOptions<AiAgentRuntimeOptions> runtimeOptions)
{
    public async Task<Result<CreateAiAgentDelegationResponse>> CreateAsync(
        CreateAiAgentDelegationRequest request,
        Guid grantorUserId,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidateRequestAsync(request, grantorUserId, cancellationToken).ConfigureAwait(false);
        if (validation is not null)
        {
            return Invalid<CreateAiAgentDelegationResponse>(validation);
        }

        var delegationId = ids.NewId();
        var now = clock.UtcNow;
        var affected = await commands.ExecuteAsync(
            AiAgentDelegationSql.Insert,
            AiSqlParameters.Create(
                ("Id", delegationId),
                ("ScopeKey", ResolveScope()),
                ("TenantId", tenant.Id),
                ("GrantorUserId", grantorUserId),
                ("GranteeUserId", request.GranteeUserId),
                ("ToolName", NormalizeOptional(request.ToolName)),
                ("PermissionCode", NormalizeOptional(request.PermissionCode)),
                ("ExpiresAtUtc", request.ExpiresAtUtc),
                ("Now", now)),
            cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new InvalidOperationException("Agent delegation was not created.");
        }

        return Result<CreateAiAgentDelegationResponse>.Success(new(delegationId));
    }

    public async Task<Result<IReadOnlyList<AiAgentDelegationResponse>>> ListOwnedAsync(
        Guid grantorUserId,
        CancellationToken cancellationToken = default)
    {
        var rows = await queries.QueryAsync<AiAgentDelegationRecord>(
            AiAgentDelegationSql.ListOwned,
            AiSqlParameters.Create(("ScopeKey", ResolveScope()), ("GrantorUserId", grantorUserId)),
            cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<AiAgentDelegationResponse>>.Success(rows.Select(Map).ToArray());
    }

    public async Task<Result<AiAgentDelegationResponse>> RevokeAsync(
        Guid delegationId,
        Guid grantorUserId,
        RevokeAiAgentDelegationRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await queries.QuerySingleOrDefaultAsync<AiAgentDelegationRecord>(
            AiAgentDelegationSql.FindOwnedById,
            AiSqlParameters.Create(
                ("Id", delegationId),
                ("ScopeKey", ResolveScope()),
                ("GrantorUserId", grantorUserId)),
            cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound<AiAgentDelegationResponse>();
        }

        if (existing.RevokedAtUtc is not null)
        {
            return Result<AiAgentDelegationResponse>.Success(Map(existing));
        }

        var now = clock.UtcNow;
        var affected = await commands.ExecuteAsync(
            AiAgentDelegationSql.RevokeOwned,
            AiSqlParameters.Create(
                ("Id", delegationId),
                ("ScopeKey", ResolveScope()),
                ("GrantorUserId", grantorUserId),
                ("ExpectedVersion", request.ExpectedVersion),
                ("Now", now)),
            cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            return Result<AiAgentDelegationResponse>.Failure(new Error(
                AiErrorCodes.AgentDelegationConcurrencyConflict,
                "The agent delegation was changed by another request.",
                ErrorType.Conflict));
        }

        existing.RevokedAtUtc = now;
        existing.Version = request.ExpectedVersion + 1;
        existing.UpdatedAtUtc = now;
        return Result<AiAgentDelegationResponse>.Success(Map(existing));
    }

    public async ValueTask<bool> HasActiveAsync(
        Guid grantorUserId,
        Guid granteeUserId,
        string toolName,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        var count = await queries.QuerySingleOrDefaultAsync<int>(
            AiAgentDelegationSql.CountActive,
            AiSqlParameters.Create(
                ("ScopeKey", ResolveScope()),
                ("GrantorUserId", grantorUserId),
                ("GranteeUserId", granteeUserId),
                ("ToolName", toolName),
                ("PermissionCode", permissionCode),
                ("Now", clock.UtcNow)),
            cancellationToken).ConfigureAwait(false);
        return count > 0;
    }

    private async Task<string?> ValidateRequestAsync(
        CreateAiAgentDelegationRequest request,
        Guid grantorUserId,
        CancellationToken cancellationToken)
    {
        if (request.GranteeUserId == Guid.Empty || request.GranteeUserId == grantorUserId)
        {
            return "The grantee must be a different user.";
        }

        var toolName = NormalizeOptional(request.ToolName);
        var permissionCode = NormalizeOptional(request.PermissionCode);
        if (toolName is null && permissionCode is null)
        {
            return "At least one delegation scope is required.";
        }

        if (toolName is not null)
        {
            var tools = await toolRegistrySource.GetRegistryAsync(cancellationToken).ConfigureAwait(false);
            if (tools.Find(toolName) is null)
            {
                return "The delegated tool is not registered.";
            }
        }

        if (permissionCode is not null
            && permissionCode is not AiAgentApprovalPermissions.Decide)
        {
            return "The delegated permission is not supported.";
        }

        var now = clock.UtcNow;
        var maxSeconds = Math.Clamp(runtimeOptions.Value.MaxRunDurationSeconds, 60, 86_400);
        if (request.ExpiresAtUtc <= now || request.ExpiresAtUtc > now.AddSeconds(maxSeconds))
        {
            return "The delegation expiry is out of range.";
        }

        return null;
    }

    private static AiAgentDelegationResponse Map(AiAgentDelegationRecord record) => new(
        record.Id,
        record.GrantorUserId,
        record.GranteeUserId,
        record.ToolName,
        record.PermissionCode,
        record.ExpiresAtUtc,
        record.RevokedAtUtc,
        record.Version,
        record.CreatedAtUtc);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string ResolveScope() => tenant.Id is { } id ? id.ToString("N") : tenant.IsHost ? "host"
        : throw new InvalidOperationException("Tenant scope is required for agent delegations.");

    private static Result<T> Invalid<T>(string message) => Result<T>.Failure(new Error(
        AiErrorCodes.AgentDelegationInvalid,
        message,
        ErrorType.Validation));

    private static Result<T> NotFound<T>() => Result<T>.Failure(new Error(
        AiErrorCodes.AgentDelegationNotFound,
        "The agent delegation was not found.",
        ErrorType.NotFound));
}
