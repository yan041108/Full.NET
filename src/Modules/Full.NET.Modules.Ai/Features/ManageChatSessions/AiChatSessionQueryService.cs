using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageChatSessions;

/// <summary>聊天会话与历史只读查询。</summary>
internal sealed class AiChatSessionQueryService(
    IQueryExecutor queryExecutor,
    ICurrentTenant currentTenant,
    IOptions<DatabaseOptions> databaseOptions)
{
    /// <summary>分页列出当前用户的聊天会话。</summary>
    public async Task<Result<PagedResult<AiChatSessionListItem>>> ListAsync(
        Guid ownerUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var scope = AiChatScope.Resolve(currentTenant);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var filter = BuildScopeParameters(scope, ownerUserId, ("Offset", offset), ("PageSize", pageSize));
        var (countStatement, listStatement) = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => (AiChatSql.CountSessionsSqlServer, AiChatSql.ListSessionsSqlServer),
            DatabaseProvider.MySql => (AiChatSql.CountSessionsMySql, AiChatSql.ListSessionsMySql),
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                new SqlStatement("ai.count_chat_sessions", countStatement, SqlDataScope.Global),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        var rows = await queryExecutor.QueryAsync<AiChatSessionRecord>(
                new SqlStatement("ai.list_chat_sessions", listStatement, SqlDataScope.Global),
                filter,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<PagedResult<AiChatSessionListItem>>.Success(
            new PagedResult<AiChatSessionListItem>(
                rows.Select(AiChatMapper.MapListItem).ToArray(),
                page,
                pageSize,
                total));
    }

    /// <summary>读取会话详情与完整历史。</summary>
    public async Task<Result<AiChatSessionResponse>> GetByIdAsync(
        Guid sessionId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var scope = AiChatScope.Resolve(currentTenant);
        var session = await FindOwnedSessionAsync(scope, sessionId, ownerUserId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            return NotFound();
        }

        var messages = await queryExecutor.QueryAsync<AiChatMessageRecord>(
                AiChatSql.ListMessagesBySession,
                AiSqlParameters.Create(("SessionId", sessionId)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<AiChatSessionResponse>.Success(AiChatMapper.MapDetail(session, messages));
    }

    internal async Task<AiChatSessionRecord?> FindOwnedSessionAsync(
        AiChatScope scope,
        Guid sessionId,
        Guid ownerUserId,
        CancellationToken cancellationToken) =>
        await queryExecutor.QuerySingleOrDefaultAsync<AiChatSessionRecord>(
                AiChatSql.FindSessionById,
                BuildScopeParameters(scope, ownerUserId, ("SessionId", sessionId)),
                cancellationToken)
            .ConfigureAwait(false);

    internal static IReadOnlyDictionary<string, object?> BuildScopeParameters(
        AiChatScope scope,
        Guid ownerUserId,
        params (string Name, object? Value)[] values) =>
        AiSqlParameters.Create(
        [
            ("ScopeTenantId", scope.TenantId),
            ("OwnerUserId", ownerUserId),
            ..values,
        ]);

    private static Result<AiChatSessionResponse> NotFound() =>
        Result<AiChatSessionResponse>.Failure(new Error(
            AiErrorCodes.ChatSessionNotFound,
            "The AI chat session was not found.",
            ErrorType.NotFound));
}
