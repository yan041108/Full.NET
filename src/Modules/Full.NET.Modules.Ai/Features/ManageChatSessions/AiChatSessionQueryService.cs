using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageChatSessions;

/// <summary>聊天会话与历史只读查询。</summary>
/// <param name="queryExecutor">当前请求查询执行器。</param>
/// <param name="currentTenant">可信租户范围。</param>
/// <param name="databaseOptions">双库分页语法选择。</param>
/// <param name="clock">判断生成租约是否到期的 UTC 时钟。</param>
internal sealed class AiChatSessionQueryService(
    IQueryExecutor queryExecutor,
    ICurrentTenant currentTenant,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock)
{
    /// <summary>仅分页列出当前用户所属范围内的会话。</summary>
    /// <param name="ownerUserId">当前会话所有者。</param>
    /// <param name="page">从一开始的页码。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
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

    /// <summary>读取已授权会话及历史，过期生成不阻塞客户端。</summary>
    /// <param name="sessionId">已授权的会话标识。</param>
    /// <param name="ownerUserId">当前会话所有者。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
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
        return Result<AiChatSessionResponse>.Success(AiChatMapper.MapDetail(session, messages, clock.UtcNow));
    }
    /// <summary>以用户和租户双重条件读取会话。</summary>
    /// <param name="scope">从可信上下文取得的租户或 Host 范围。</param>
    /// <param name="sessionId">已授权的会话标识。</param>
    /// <param name="ownerUserId">当前会话所有者。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
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

    /// <summary>构造可信所有者范围，禁止由客户端任意选择租户。</summary>
    /// <param name="scope">从可信上下文取得的租户或 Host 范围。</param>
    /// <param name="ownerUserId">当前会话所有者。</param>
    /// <param name="values">额外的具名 SQL 参数。</param>
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

    /// <summary>返回统一的会话不存在错误。</summary>
    private static Result<AiChatSessionResponse> NotFound() =>
        Result<AiChatSessionResponse>.Failure(new Error(
            AiErrorCodes.ChatSessionNotFound,
            "The AI chat session was not found.",
            ErrorType.NotFound));
}
