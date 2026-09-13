using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>Agent Tool 调用审计只读查询。</summary>
internal sealed class AiAgentToolCallQueryService(
    IQueryExecutor queryExecutor,
    ICurrentTenant currentTenant,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>分页查询调用审计。</summary>
    public async Task<Result<PagedResult<AiAgentToolCallListItem>>> ListAsync(
        AiAgentToolCallListQuery query,
        CancellationToken cancellationToken = default)
    {
        var scope = AiChatScope.Resolve(currentTenant);
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = AiSqlParameters.Create(
        [
            ("ScopeTenantId", scope.TenantId),
            ("FilterTenantId", query.TenantId),
            ("ToolName", string.IsNullOrWhiteSpace(query.ToolName) ? null : query.ToolName.Trim()),
            ("StatusKey", string.IsNullOrWhiteSpace(query.StatusKey) ? null : query.StatusKey.Trim()),
            ("Offset", offset),
            ("PageSize", pageSize),
        ]);
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (
                AiAgentToolCallSql.CountCallsSqlServer,
                AiAgentToolCallSql.ListCallsSqlServer),
            DatabaseProvider.MySql => (
                AiAgentToolCallSql.CountCallsMySql,
                AiAgentToolCallSql.ListCallsMySql),
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                new SqlStatement("ai.count_agent_tool_calls", countStatement, SqlDataScope.Global),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<AiAgentToolCallRecord>(
                new SqlStatement("ai.list_agent_tool_calls", listStatement, SqlDataScope.Global),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<AiAgentToolCallListItem>>.Success(
            new PagedResult<AiAgentToolCallListItem>(
                rows.Select(Map).ToArray(),
                page,
                pageSize,
                total));
    }

    private static AiAgentToolCallListItem Map(AiAgentToolCallRecord row) =>
        new(
            row.Id,
            row.TenantId,
            row.ActorUserId,
            row.ToolName,
            row.PermissionCode,
            row.StatusKey,
            row.DurationMs,
            row.InputSummary,
            row.OutputSummary,
            row.ErrorCode,
            row.TraceId,
            row.RunId,
            row.ArgumentsHash,
            row.ApprovalId,
            row.CreatedAtUtc);
}
