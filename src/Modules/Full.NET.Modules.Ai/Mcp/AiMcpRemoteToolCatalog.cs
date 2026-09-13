using Full.NET.Agents.Mcp;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Mcp;

/// <summary>从持久化批准记录构建可执行远端工具目录。</summary>
internal sealed class AiMcpRemoteToolCatalog(
    IQueryExecutor queryExecutor,
    ICurrentTenant currentTenant,
    IOptions<DatabaseOptions> databaseOptions,
    IToolAuthorizationPort authorization,
    AiMcpRemoteTokenProtector tokenProtector) : IMcpRemoteToolCatalog
{
    public async ValueTask<IReadOnlyList<McpRemoteApprovedTool>> ListExecutableAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await QueryRowsAsync(cancellationToken).ConfigureAwait(false);
        var tools = new List<McpRemoteApprovedTool>();
        foreach (var row in rows)
        {
            if (await authorization.AuthorizeAsync(row.PermissionCode, cancellationToken).ConfigureAwait(false) is null)
            {
                continue;
            }

            tools.Add(Map(row));
        }

        return tools;
    }

    public async ValueTask<McpRemoteApprovedTool?> FindExecutableAsync(
        string localToolName,
        CancellationToken cancellationToken = default)
    {
        foreach (var tool in await ListExecutableAsync(cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(tool.LocalToolName, localToolName, StringComparison.Ordinal))
            {
                return tool;
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<AiMcpRemoteToolRecord>> QueryRowsAsync(CancellationToken cancellationToken)
    {
        var scopeKey = currentTenant.Id is { } id ? id.ToString("N") : currentTenant.IsHost ? "host"
            : throw new InvalidOperationException("Tenant scope is required for MCP remote tools.");
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => AiMcpRemoteSql.ListExecutableToolsSqlServer,
            DatabaseProvider.MySql => AiMcpRemoteSql.ListExecutableToolsMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<AiMcpRemoteToolRecord>(
            new SqlStatement("ai.list_mcp_remote_tools", statement, SqlDataScope.Global),
            AiSqlParameters.Create(("ScopeKey", scopeKey), ("ScopeTenantId", currentTenant.Id)),
            cancellationToken).ConfigureAwait(false);
        return rows.ToArray();
    }

    private McpRemoteApprovedTool Map(AiMcpRemoteToolRecord row) =>
        new(
            row.ConnectionId,
            row.ConnectionKey,
            new Uri(row.EndpointUrl, UriKind.Absolute),
            row.LocalToolName,
            row.RemoteToolName,
            row.ToolVersion,
            row.InputSchemaJson,
            row.InputSchemaHash,
            row.SideEffectKey,
            row.PermissionCode,
            row.ApprovalStatusKey,
            tokenProtector.Unprotect(row.ServiceTokenProtected));
}
