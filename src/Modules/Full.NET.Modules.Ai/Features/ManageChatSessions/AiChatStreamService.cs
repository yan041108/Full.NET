using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Modules.Ai.Budgets;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using Full.NET.Abstractions.Results;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Ai.Features.ManageChatSessions;

/// <summary>处理聊天预检、模型执行和有界收尾，传输通过输出接口适配。</summary>
/// <param name="queryExecutor">当前请求查询执行器。</param>
/// <param name="commandExecutor">当前请求命令执行器。</param>
/// <param name="transaction">原子启动消息和租约的短事务。</param>
/// <param name="sessionQueries">会话所有权查询服务。</param>
/// <param name="completionStreamer">有界模型流读取器。</param>
/// <param name="generationRegistry">按生成代次管理的本地取消入口。</param>
/// <param name="leaseMonitor">独立数据库作用域内的租约监视器。</param>
/// <param name="quotaGuard">租户配额预留与幂等结算服务。</param>
/// <param name="cleanupScope">收尾阶段使用的独立有界数据作用域。</param>
/// <param name="currentTenant">可信当前租户范围。</param>
/// <param name="databaseOptions">数据库提供程序选择。</param>
/// <param name="clock">统一 UTC 时钟。</param>
/// <param name="idGenerator">消息及生成标识生成器。</param>
internal sealed class AiChatStreamService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    AiChatSessionQueryService sessionQueries,
    AiChatCompletionStreamer completionStreamer,
    AiChatGenerationRegistry generationRegistry,
    AiChatGenerationLeaseMonitor leaseMonitor,
    IAiOperationBudgetStore quotaGuard,
    AiChatCleanupScope cleanupScope,
    ICurrentTenant currentTenant,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>原子获取生成槽位后发送有界 SSE，所有异常路径均收敛自身租约。</summary>
    /// <param name="sessionId">已授权的会话标识。</param>
    /// <param name="ownerUserId">当前会话所有者。</param>
    /// <param name="request">已经过入口绑定的请求。</param>
    /// <param name="output">承载语义事件的传输适配器。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    public async Task<Result<bool>> StreamAsync(
        Guid sessionId,
        Guid ownerUserId,
        StreamAiChatMessageRequest request,
        IAiChatOutput output,
        CancellationToken cancellationToken = default)
    {
        var validationMessage = AiChatContentPolicy.ValidateUserMessage(request.Content);
        if (validationMessage is not null)
        {
            return Result<bool>.Failure(new Error(AiErrorCodes.ChatMessageInvalid, validationMessage, ErrorType.BusinessRule));
        }

        var scope = AiChatScope.Resolve(currentTenant);
        var cleanupTenant = scope.TenantId is { } tenantId
            ? new TenantContext(tenantId, currentTenant.Identifier!, currentTenant.Name!) : null;
        var session = await sessionQueries.FindOwnedSessionAsync(scope, sessionId, ownerUserId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
            return Result<bool>.Failure(new Error(AiErrorCodes.ChatSessionNotFound,
                "The AI chat session was not found.", ErrorType.NotFound));

        var model = await queryExecutor.QuerySingleOrDefaultAsync<AiModelConfigRecord>(
                scope.TenantId.HasValue ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
                AiSqlParameters.Create(("ModelConfigId", session.ModelConfigId)), cancellationToken).ConfigureAwait(false);
        if (model is null || !model.IsEnabled)
            return Result<bool>.Failure(new Error(AiErrorCodes.ModelConfigUnavailable,
                "The bound AI model configuration is not available.", ErrorType.BusinessRule));

        var now = clock.UtcNow;
        var userMessageId = idGenerator.NewId();
        var assistantMessageId = idGenerator.NewId();
        var replaceTitle = session.MessageCount == 0
            && string.Equals(session.Title, AiChatContentPolicy.DefaultSessionTitle, StringComparison.Ordinal);
        var newTitle = replaceTitle
            ? AiChatContentPolicy.BuildTitleFromMessage(request.Content)
            : session.Title;

        using var requestBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestBudget.CancelAfter(TimeSpan.FromMinutes(3));
        using var monitorStop = new CancellationTokenSource();
        Task? monitor = null;
        var acquired = false;
        AiOperationReservation? reservation = null;
        string statusKey = AiChatMessageStatusKeys.Completed;
        int? promptTokens = null;
        int? completionTokens = null;
        var assistantBuffer = new StringBuilder();
        Error? failure = null;

        try
        {
            // 槽位获取与两条消息写入在同一短事务；任何中途失败都回滚，外部流不占用事务。
            acquired = await transaction.ExecuteAsync(async token =>
            {
                var started = await commandExecutor.ExecuteAsync(AiChatGenerationSql.Acquire,
                    AiChatSessionQueryService.BuildScopeParameters(scope, ownerUserId,
                        ("SessionId", sessionId), ("GenerationId", assistantMessageId), ("Now", now),
                        ("ExpiresAtUtc", now + AiChatGenerationLeaseMonitor.LeaseDuration)), token).ConfigureAwait(false);
                if (started != 1) return false;
                await commandExecutor.ExecuteAsync(AiChatGenerationSql.FailAbandonedMessages,
                    AiSqlParameters.Create(("SessionId", sessionId)), token).ConfigureAwait(false);
                await InsertMessageAsync(
                        userMessageId,
                        sessionId,
                        AiChatMessageRoleKeys.User,
                        request.Content.Trim(),
                        AiChatMessageStatusKeys.Completed,
                        now,
                        token)
                    .ConfigureAwait(false);
                await InsertMessageAsync(
                        assistantMessageId,
                        sessionId,
                        AiChatMessageRoleKeys.Assistant,
                        string.Empty,
                        AiChatMessageStatusKeys.Streaming,
                        now,
                        token)
                    .ConfigureAwait(false);
                await TouchSessionAsync(
                        scope, ownerUserId, assistantMessageId, sessionId,
                        messageDelta: 2,
                        replaceTitle,
                        newTitle,
                        now,
                        token)
                    .ConfigureAwait(false);

                return true;
            }, requestBudget.Token).ConfigureAwait(false);
            if (!acquired)
            {
                return Result<bool>.Failure(new Error(AiErrorCodes.ChatGenerationInProgress,
                    "Another generation is already in progress for this session.", ErrorType.Conflict));
            }
            var linkedToken = generationRegistry.Register(sessionId, assistantMessageId, requestBudget.Token);
            monitor = leaseMonitor.WatchAsync(scope, sessionId, ownerUserId, assistantMessageId, now + AiChatGenerationLeaseMonitor.LeaseDuration, requestBudget, monitorStop.Token);
            var history = await LoadRecentMessagesAsync(sessionId, linkedToken).ConfigureAwait(false);
            try
            {
                reservation = await quotaGuard.ReserveAsync(
                    AiChatBudgetRequest.Create(assistantMessageId, model, history), linkedToken).ConfigureAwait(false);
                if (!reservation.IsNew)
                    throw new AiBudgetException("ai.budget.operation_conflict");
            }
            catch (AiBudgetException error)
            {
                failure = new Error(error.Code, "The AI operation budget cannot cover this request.",
                    error.Code == "ai.budget.operation_conflict" ? ErrorType.Conflict : ErrorType.Forbidden);
                throw;
            }
            linkedToken.ThrowIfCancellationRequested();
            if (!await leaseMonitor.RenewOnceAsync(scope, sessionId, ownerUserId, assistantMessageId, linkedToken)
                .WaitAsync(linkedToken).ConfigureAwait(false))
                throw new OperationCanceledException("AI generation ownership was lost before dispatch.", linkedToken);
            await output.StartAsync(linkedToken).ConfigureAwait(false);
            var result = await completionStreamer.StreamAsync(
                    model,
                    history,
                    delta => output.WriteDeltaAsync(delta, linkedToken),
                    linkedToken,
                    assistantBuffer)
                .ConfigureAwait(false);

            linkedToken.ThrowIfCancellationRequested();
            if (!await leaseMonitor.RenewOnceAsync(scope, sessionId, ownerUserId, assistantMessageId, linkedToken).WaitAsync(linkedToken).ConfigureAwait(false))
                throw new OperationCanceledException("AI generation ownership was lost.", linkedToken);
            var assistantContent = result.Content;
            promptTokens = result.PromptTokens;
            completionTokens = result.CompletionTokens;
            statusKey = result.Cancelled
                ? AiChatMessageStatusKeys.Cancelled
                : AiChatMessageStatusKeys.Completed;

            await UpdateMessageAsync(
                    assistantMessageId,
                    sessionId,
                    assistantContent,
                    statusKey,
                    promptTokens,
                    completionTokens,
                    requestBudget.Token)
                .ConfigureAwait(false);

        }
        catch (OperationCanceledException)
        {
            statusKey = AiChatMessageStatusKeys.Cancelled;
            failure = new Error(AiErrorCodes.ChatGenerationNotActive,
                "The AI generation was cancelled or lost ownership.", ErrorType.Conflict);
        }
        catch (Exception)
        {
            statusKey = AiChatMessageStatusKeys.Failed;
            failure ??= ExecutionFailure();
        }
        finally
        {
            // 各阶段独立截止；某一清理失败不能跳过注销或释放，所有 SQL 仍限定本代标识。
            await monitorStop.CancelAsync().ConfigureAwait(false);
            if (monitor is not null && !await TryCleanupAsync(token => monitor.WaitAsync(token)).ConfigureAwait(false))
                failure ??= ExecutionFailure();
            if (acquired && statusKey != AiChatMessageStatusKeys.Completed
                && !await cleanupScope.RunAsync(cleanupTenant, (services, token) => UpdateMessageAsync(assistantMessageId, sessionId,
                    assistantBuffer.ToString(), statusKey, promptTokens, completionTokens, token, services.GetRequiredService<ICommandExecutor>())).ConfigureAwait(false))
                failure ??= ExecutionFailure();
            if (reservation is not null && !await cleanupScope.RunAsync(cleanupTenant, (services, token) =>
                    services.GetRequiredService<IAiOperationBudgetStore>().SettleAsync(reservation.OperationId, new(promptTokens, completionTokens),
                        statusKey == AiChatMessageStatusKeys.Completed && failure is null ? "succeeded"
                            : statusKey == AiChatMessageStatusKeys.Cancelled ? "cancelled" : "failed", token)).ConfigureAwait(false))
                failure ??= ExecutionFailure();
            generationRegistry.Unregister(sessionId, assistantMessageId);
            if (acquired && !await cleanupScope.RunAsync(cleanupTenant, (services, token) =>
                    ReleaseGenerationAsync(scope, sessionId, ownerUserId, assistantMessageId, token, services.GetRequiredService<ICommandExecutor>())).ConfigureAwait(false))
                failure ??= ExecutionFailure();
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!output.HasStarted)
            return Result<bool>.Failure(failure ?? ExecutionFailure());
        // 终态在收尾后发送，避免持久化或结算失败却向客户端报告成功。
        using var notification = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        notification.CancelAfter(TimeSpan.FromSeconds(5));
        if (failure is not null)
            await output.WriteErrorAsync(failure.Message, notification.Token).ConfigureAwait(false);
        else
            await output.WriteDoneAsync(assistantMessageId, promptTokens, completionTokens, notification.Token).ConfigureAwait(false);
        return Result<bool>.Success(true);
    }

    /// <summary>独立清理最多等待五秒；失败由调用方汇总为安全错误并继续其他清理。</summary>
    private static async Task<bool> TryCleanupAsync(Func<CancellationToken, Task> cleanup)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await cleanup(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>基础设施错误不携带驱动、模型或凭据的原始信息。</summary>
    private static Error ExecutionFailure() => new(AiErrorCodes.ChatGenerationFailed,
        "The AI generation could not be completed.", ErrorType.Unexpected);

    /// <summary>读取有限历史并按提示预算保留最新完整消息。</summary>
    /// <param name="sessionId">已授权的会话标识。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    private async Task<IReadOnlyList<(string RoleKey, string Content)>> LoadRecentMessagesAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AiChatMessageRecord> rows;
        if (databaseOptions.Value.Provider == DatabaseProvider.SqlServer)
        {
            rows = await queryExecutor.QueryAsync<AiChatMessageRecord>(
                    AiChatSql.ListRecentMessagesForModel,
                    AiSqlParameters.Create(
                        ("SessionId", sessionId),
                        ("Take", AiChatContentPolicy.MaxHistoryMessages)),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            rows = await queryExecutor.QueryAsync<AiChatMessageRecord>(
                    new SqlStatement(
                        "ai.list_recent_chat_messages_for_model.mysql",
                        AiChatSql.ListRecentMessagesForModelMySql,
                        SqlDataScope.Global),
                    AiSqlParameters.Create(
                        ("SessionId", sessionId),
                        ("Take", AiChatContentPolicy.MaxHistoryMessages)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // 从最新消息向前收集完整消息，超出提示预算时不再带入更旧内容。
        var history = new List<(string RoleKey, string Content)>();
        var characters = 0;
        foreach (var row in rows.OrderByDescending(item => item.CreatedAtUtc).ThenByDescending(item => item.Id))
        {
            if (characters + (long)row.Content.Length > AiChatContentPolicy.MaxHistoryCharacters) break;
            history.Add((row.RoleKey, row.Content));
            characters += row.Content.Length;
        }
        history.Reverse();
        return history;
    }

    /// <summary>只释放本代持久化租约；迟到清理不能改变后继任务。</summary>
    /// <param name="scope">可信租户范围。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="ownerUserId">已授权用户。</param>
    /// <param name="generationId">本代生成标识。</param>
    /// <param name="cancellationToken">独立清理取消令牌。</param>
    /// <param name="cleanupExecutor">独立作用域中的命令执行器。</param>
    private async Task ReleaseGenerationAsync(AiChatScope scope, Guid sessionId, Guid ownerUserId,
        Guid generationId, CancellationToken cancellationToken, ICommandExecutor cleanupExecutor) =>
        await cleanupExecutor.ExecuteAsync(AiChatGenerationSql.Release,
            AiChatSessionQueryService.BuildScopeParameters(scope, ownerUserId, ("SessionId", sessionId),
                ("GenerationId", generationId), ("Now", clock.UtcNow)), cancellationToken).ConfigureAwait(false);

    /// <summary>在启动短事务中保存消息初始状态。</summary>
    /// <param name="messageId">当前消息唯一标识。</param>
    /// <param name="sessionId">已授权的会话标识。</param>
    /// <param name="roleKey">消息角色稳定键。</param>
    /// <param name="content">受长度约束的消息正文。</param>
    /// <param name="statusKey">消息终态或流式状态键。</param>
    /// <param name="createdAtUtc">消息创建的 UTC 时间。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    private async Task InsertMessageAsync(
        Guid messageId,
        Guid sessionId,
        string roleKey,
        string content,
        string statusKey,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
                AiChatSql.InsertMessage,
                AiSqlParameters.Create(
                    ("Id", messageId),
                    ("SessionId", sessionId),
                    ("RoleKey", roleKey),
                    ("Content", content),
                    ("StatusKey", statusKey),
                    ("PromptTokens", null),
                    ("CompletionTokens", null),
                    ("CreatedAtUtc", createdAtUtc)),
                cancellationToken)
            .ConfigureAwait(false);

    /// <summary>仅在仍持有生成代次时保存本条回复，防止旧进程覆盖恢复状态。</summary>
    /// <param name="messageId">当前消息唯一标识。</param>
    /// <param name="sessionId">已授权的会话标识。</param>
    /// <param name="content">受长度约束的消息正文。</param>
    /// <param name="statusKey">消息终态或流式状态键。</param>
    /// <param name="promptTokens">提供程序确认的提示用量。</param>
    /// <param name="completionTokens">提供程序确认的生成用量。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    /// <param name="cleanupExecutor">失败收尾时提供独立作用域的执行器。</param>
    private async Task UpdateMessageAsync(
        Guid messageId,
        Guid sessionId,
        string content,
        string statusKey,
        int? promptTokens,
        int? completionTokens,
        CancellationToken cancellationToken, ICommandExecutor? cleanupExecutor = null)
    {
        var affected = await (cleanupExecutor ?? commandExecutor).ExecuteAsync(
                AiChatSql.UpdateMessage,
                AiSqlParameters.Create(
                    ("MessageId", messageId),
                    ("GenerationId", messageId),
                    ("SessionId", sessionId),
                    ("Content", content),
                    ("StatusKey", statusKey),
                    ("PromptTokens", promptTokens),
                    ("CompletionTokens", completionTokens)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1) throw new InvalidOperationException("AI generation ownership was lost during message persistence.");
    }

    /// <summary>由当前生成持有者维护消息计数和首条消息标题。</summary>
    /// <param name="scope">从可信上下文取得的租户或 Host 范围。</param>
    /// <param name="ownerUserId">当前会话所有者。</param>
    /// <param name="generationId">本代生成所有权标识。</param>
    /// <param name="sessionId">已授权的会话标识。</param>
    /// <param name="messageDelta">本次新增消息条数。</param>
    /// <param name="replaceTitle">是否以首条消息替换默认标题。</param>
    /// <param name="title">已验证的会话标题。</param>
    /// <param name="updatedAtUtc">状态变更的 UTC 时间。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    private async Task TouchSessionAsync(
        AiChatScope scope,
        Guid ownerUserId,
        Guid generationId,
        Guid sessionId,
        int messageDelta,
        bool replaceTitle,
        string title,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
                AiChatSql.TouchSessionAfterMessage,
                AiChatSessionQueryService.BuildScopeParameters(scope, ownerUserId,
                    ("GenerationId", generationId),
                    ("SessionId", sessionId),
                    ("MessageDelta", messageDelta),
                    ("LastMessageAtUtc", updatedAtUtc),
                    ("ReplaceTitle", replaceTitle ? 1 : 0),
                    ("Title", title),
                    ("UpdatedAtUtc", updatedAtUtc)),
                cancellationToken)
            .ConfigureAwait(false);

}
