using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Mcp;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.ManageMcpRemoteConnections;

/// <summary>MCP 远端连接只读查询。</summary>
internal sealed class AiMcpRemoteConnectionQueryService(
    IQueryExecutor queryExecutor,
    ICurrentTenant currentTenant)
{
    public async Task<Result<IReadOnlyList<AiMcpRemoteConnectionListItem>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var (scopeKey, tenantId) = AiMcpRemoteScope.Resolve(currentTenant);
        var rows = await queryExecutor.QueryAsync<AiMcpRemoteConnectionRecord>(
            AiMcpRemoteSql.ListConnections,
            AiSqlParameters.Create(("ScopeKey", scopeKey), ("ScopeTenantId", tenantId)),
            cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<AiMcpRemoteConnectionListItem>>.Success(
            rows.Select(AiMcpRemoteConnectionMapper.ToListItem).ToArray());
    }

    public async Task<Result<AiMcpRemoteConnectionResponse>> GetAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(connectionId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result<AiMcpRemoteConnectionResponse>.Failure(new Error(
                AiErrorCodes.McpRemoteConnectionNotFound,
                "The MCP remote connection was not found.",
                ErrorType.NotFound));
        }

        return Result<AiMcpRemoteConnectionResponse>.Success(AiMcpRemoteConnectionMapper.ToResponse(row));
    }

    public async Task<Result<IReadOnlyList<AiMcpRemoteToolApprovalItem>>> ListApprovalsAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        if (await FindAsync(connectionId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result<IReadOnlyList<AiMcpRemoteToolApprovalItem>>.Failure(new Error(
                AiErrorCodes.McpRemoteConnectionNotFound,
                "The MCP remote connection was not found.",
                ErrorType.NotFound));
        }

        var rows = await queryExecutor.QueryAsync<AiMcpRemoteToolApprovalRecord>(
            AiMcpRemoteSql.ListApprovalsByConnection,
            AiSqlParameters.Create(("ConnectionId", connectionId)),
            cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<AiMcpRemoteToolApprovalItem>>.Success(
            rows.Select(AiMcpRemoteConnectionMapper.ToApprovalItem).ToArray());
    }

    internal async Task<AiMcpRemoteConnectionRecord?> FindAsync(
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var (scopeKey, tenantId) = AiMcpRemoteScope.Resolve(currentTenant);
        return await queryExecutor.QuerySingleOrDefaultAsync<AiMcpRemoteConnectionRecord>(
            AiMcpRemoteSql.FindConnectionById,
            AiSqlParameters.Create(
                ("ConnectionId", connectionId),
                ("ScopeKey", scopeKey),
                ("ScopeTenantId", tenantId)),
            cancellationToken).ConfigureAwait(false);
    }
}
