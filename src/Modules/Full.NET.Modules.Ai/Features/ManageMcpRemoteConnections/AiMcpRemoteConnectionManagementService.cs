using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.AgenticWeb.Mcp.Client;
using Full.NET.Agents.Mcp;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Mcp;
using Full.NET.Modules.Ai.Persistence;
using System.Text.Json;

namespace Full.NET.Modules.Ai.Features.ManageMcpRemoteConnections;

/// <summary>MCP 远端连接写入、发现与工具批准。</summary>
internal sealed class AiMcpRemoteConnectionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    AiMcpRemoteConnectionQueryService queries,
    ICurrentTenant currentTenant,
    AiMcpRemoteTokenProtector tokenProtector,
    McpClientConnectionManager connectionManager,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<AiMcpRemoteConnectionResponse>> CreateAsync(
        CreateAiMcpRemoteConnectionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => CreateCoreAsync(request, token), cancellationToken);

    public Task<Result<AiMcpRemoteConnectionResponse>> UpdateAsync(
        Guid connectionId,
        UpdateAiMcpRemoteConnectionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => UpdateCoreAsync(connectionId, request, token), cancellationToken);

    public Task<Result<IReadOnlyList<AiMcpRemoteDiscoveredToolItem>>> DiscoverToolsAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default) =>
        DiscoverCoreAsync(connectionId, cancellationToken);

    public Task<Result<AiMcpRemoteToolApprovalItem>> ApproveToolAsync(
        Guid connectionId,
        ApproveAiMcpRemoteToolRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(token => ApproveCoreAsync(connectionId, request, token), cancellationToken);

    private async Task<Result<AiMcpRemoteConnectionResponse>> CreateCoreAsync(
        CreateAiMcpRemoteConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = AiMcpRemoteConnectionValidator.ValidateCreate(
            request.ConnectionKey,
            request.DisplayName,
            request.EndpointUrl,
            request.ServiceToken);
        if (validation is not null)
        {
            return ValidationFailure<AiMcpRemoteConnectionResponse>(validation);
        }

        var (scopeKey, tenantId) = AiMcpRemoteScope.Resolve(currentTenant);
        var now = clock.UtcNow;
        var id = idGenerator.NewId();
        try
        {
            await commandExecutor.ExecuteAsync(
                AiMcpRemoteSql.InsertConnection,
                AiSqlParameters.Create(
                    ("Id", id),
                    ("ScopeKey", scopeKey),
                    ("ScopeTenantId", tenantId),
                    ("ConnectionKey", request.ConnectionKey.Trim()),
                    ("DisplayName", request.DisplayName.Trim()),
                    ("EndpointUrl", request.EndpointUrl.Trim()),
                    ("ServiceTokenProtected", tokenProtector.Protect(request.ServiceToken.Trim())),
                    ("OAuthScopesJson", request.OAuthScopesJson),
                    ("IsEnabled", true),
                    ("Version", 1L),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            return Result<AiMcpRemoteConnectionResponse>.Failure(new Error(
                AiErrorCodes.McpRemoteConnectionConflict,
                "A MCP remote connection with the same key already exists.",
                ErrorType.Conflict));
        }

        return await queries.GetAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<AiMcpRemoteConnectionResponse>> UpdateCoreAsync(
        Guid connectionId,
        UpdateAiMcpRemoteConnectionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = AiMcpRemoteConnectionValidator.ValidateUpdate(request.DisplayName, request.EndpointUrl);
        if (validation is not null)
        {
            return ValidationFailure<AiMcpRemoteConnectionResponse>(validation);
        }

        var row = await queries.FindAsync(connectionId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return NotFound<AiMcpRemoteConnectionResponse>();
        }

        var protectedToken = row.ServiceTokenProtected;
        if (request.ClearServiceToken)
        {
            return ValidationFailure<AiMcpRemoteConnectionResponse>("Service token cannot be cleared.");
        }

        if (!string.IsNullOrWhiteSpace(request.ServiceToken))
        {
            protectedToken = tokenProtector.Protect(request.ServiceToken.Trim());
        }

        var (scopeKey, tenantId) = AiMcpRemoteScope.Resolve(currentTenant);
        var now = clock.UtcNow;
        var affected = await commandExecutor.ExecuteAsync(
            AiMcpRemoteSql.UpdateConnection,
            AiSqlParameters.Create(
                ("ConnectionId", connectionId),
                ("ScopeKey", scopeKey),
                ("ScopeTenantId", tenantId),
                ("DisplayName", request.DisplayName.Trim()),
                ("EndpointUrl", request.EndpointUrl.Trim()),
                ("ServiceTokenProtected", protectedToken),
                ("OAuthScopesJson", request.OAuthScopesJson),
                ("IsEnabled", request.IsEnabled),
                ("UpdatedAtUtc", now),
                ("Version", request.Version)),
            cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            return Result<AiMcpRemoteConnectionResponse>.Failure(new Error(
                AiErrorCodes.McpRemoteConnectionConflict,
                "The MCP remote connection was updated by another request.",
                ErrorType.Conflict));
        }

        return await queries.GetAsync(connectionId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<IReadOnlyList<AiMcpRemoteDiscoveredToolItem>>> DiscoverCoreAsync(
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        var row = await queries.FindAsync(connectionId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result<IReadOnlyList<AiMcpRemoteDiscoveredToolItem>>.Failure(new Error(
                AiErrorCodes.McpRemoteConnectionNotFound,
                "The MCP remote connection was not found.",
                ErrorType.NotFound));
        }

        if (!row.IsEnabled)
        {
            return Result<IReadOnlyList<AiMcpRemoteDiscoveredToolItem>>.Failure(new Error(
                AiErrorCodes.McpRemoteConnectionUnavailable,
                "The MCP remote connection is disabled.",
                ErrorType.BusinessRule));
        }

        var approvals = await queryExecutor.QueryAsync<AiMcpRemoteToolApprovalRecord>(
            AiMcpRemoteSql.ListApprovalsByConnection,
            AiSqlParameters.Create(("ConnectionId", connectionId)),
            cancellationToken).ConfigureAwait(false);
        var approvalMap = approvals.ToDictionary(item => item.RemoteToolName, StringComparer.Ordinal);

        var endpoint = new Uri(row.EndpointUrl, UriKind.Absolute);
        var token = tokenProtector.Unprotect(row.ServiceTokenProtected);
        var client = await connectionManager.GetClientAsync(connectionId, endpoint, token, cancellationToken)
            .ConfigureAwait(false);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var items = new List<AiMcpRemoteDiscoveredToolItem>(tools.Count);
        foreach (var tool in tools)
        {
            var schema = tool.JsonSchema.ValueKind == JsonValueKind.Undefined
                ? "{}"
                : tool.JsonSchema.GetRawText();
            approvalMap.TryGetValue(tool.Name, out var approval);
            var approved = approval is not null
                && string.Equals(approval.ApprovalStatusKey, McpRemoteCapabilityPolicy.ApprovedStatusKey, StringComparison.Ordinal);
            items.Add(new(
                tool.Name,
                schema,
                approved,
                approval?.ApprovalStatusKey));
        }

        return Result<IReadOnlyList<AiMcpRemoteDiscoveredToolItem>>.Success(items);
    }

    private async Task<Result<AiMcpRemoteToolApprovalItem>> ApproveCoreAsync(
        Guid connectionId,
        ApproveAiMcpRemoteToolRequest request,
        CancellationToken cancellationToken)
    {
        var row = await queries.FindAsync(connectionId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Result<AiMcpRemoteToolApprovalItem>.Failure(new Error(
                AiErrorCodes.McpRemoteConnectionNotFound,
                "The MCP remote connection was not found.",
                ErrorType.NotFound));
        }

        if (string.IsNullOrWhiteSpace(request.RemoteToolName))
        {
            return ValidationFailure<AiMcpRemoteToolApprovalItem>("Remote tool name is required.");
        }

        var permissionCode = string.IsNullOrWhiteSpace(request.PermissionCode)
            ? AiMcpPermissions.RemoteInvoke
            : request.PermissionCode.Trim();
        var sideEffectKey = request.SideEffectKey.Trim();
        var endpoint = new Uri(row.EndpointUrl, UriKind.Absolute);
        var token = tokenProtector.Unprotect(row.ServiceTokenProtected);
        var client = await connectionManager.GetClientAsync(connectionId, endpoint, token, cancellationToken)
            .ConfigureAwait(false);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var live = tools.FirstOrDefault(item => string.Equals(item.Name, request.RemoteToolName.Trim(), StringComparison.Ordinal));
        if (live is null)
        {
            return Result<AiMcpRemoteToolApprovalItem>.Failure(new Error(
                AiErrorCodes.McpRemoteToolNotFound,
                "The remote tool was not found on the MCP server.",
                ErrorType.NotFound));
        }

        var schemaJson = live.JsonSchema.ValueKind == JsonValueKind.Undefined
            ? "{}"
            : live.JsonSchema.GetRawText();
        if (!McpRemoteCapabilityPolicy.CanApproveForExposure(sideEffectKey, schemaJson, out var rejection))
        {
            return Result<AiMcpRemoteToolApprovalItem>.Failure(new Error(
                rejection ?? AiErrorCodes.McpRemoteToolInvalid,
                "The remote tool cannot be approved.",
                ErrorType.BusinessRule));
        }

        var localToolName = McpRemoteCapabilityPolicy.BuildLocalToolName(row.ConnectionKey, live.Name);
        var hash = McpRemoteCapabilityPolicy.ComputeSchemaHash(schemaJson);
        var now = clock.UtcNow;
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<AiMcpRemoteToolApprovalRecord>(
            AiMcpRemoteSql.FindApprovalByLocalName,
            AiSqlParameters.Create(("ConnectionId", connectionId), ("LocalToolName", localToolName)),
            cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            var approvalId = idGenerator.NewId();
            await commandExecutor.ExecuteAsync(
                AiMcpRemoteSql.InsertApproval,
                AiSqlParameters.Create(
                    ("Id", approvalId),
                    ("ConnectionId", connectionId),
                    ("LocalToolName", localToolName),
                    ("RemoteToolName", live.Name),
                    ("ToolVersion", 1),
                    ("InputSchemaJson", schemaJson),
                    ("InputSchemaHash", hash),
                    ("SideEffectKey", sideEffectKey),
                    ("PermissionCode", permissionCode),
                    ("ApprovalStatusKey", McpRemoteCapabilityPolicy.ApprovedStatusKey),
                    ("Version", 1L),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken).ConfigureAwait(false);
            existing = await queryExecutor.QuerySingleOrDefaultAsync<AiMcpRemoteToolApprovalRecord>(
                AiMcpRemoteSql.FindApprovalByLocalName,
                AiSqlParameters.Create(("ConnectionId", connectionId), ("LocalToolName", localToolName)),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await commandExecutor.ExecuteAsync(
                AiMcpRemoteSql.UpdateApproval,
                AiSqlParameters.Create(
                    ("ApprovalId", existing.Id),
                    ("ConnectionId", connectionId),
                    ("RemoteToolName", live.Name),
                    ("ToolVersion", existing.ToolVersion + 1),
                    ("InputSchemaJson", schemaJson),
                    ("InputSchemaHash", hash),
                    ("SideEffectKey", sideEffectKey),
                    ("PermissionCode", permissionCode),
                    ("ApprovalStatusKey", McpRemoteCapabilityPolicy.ApprovedStatusKey),
                    ("UpdatedAtUtc", now)),
                cancellationToken).ConfigureAwait(false);
            existing = await queryExecutor.QuerySingleOrDefaultAsync<AiMcpRemoteToolApprovalRecord>(
                AiMcpRemoteSql.FindApprovalByLocalName,
                AiSqlParameters.Create(("ConnectionId", connectionId), ("LocalToolName", localToolName)),
                cancellationToken).ConfigureAwait(false);
        }

        return Result<AiMcpRemoteToolApprovalItem>.Success(
            AiMcpRemoteConnectionMapper.ToApprovalItem(existing!));
    }

    private static Result<T> ValidationFailure<T>(string message) =>
        Result<T>.Failure(new Error(AiErrorCodes.McpRemoteConnectionInvalid, message, ErrorType.Validation));

    private static Result<T> NotFound<T>() =>
        Result<T>.Failure(new Error(
            AiErrorCodes.McpRemoteConnectionNotFound,
            "The MCP remote connection was not found.",
            ErrorType.NotFound));

    private static bool IsUniqueViolation(Exception ex) =>
        ex.Message.Contains("UX_fn_ai_mcp_remote_connection_Key", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase);
}
