using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.ManageChatSessions;

/// <summary>处理聊天消息发送与 SSE 流式回复。</summary>
internal sealed class AiChatStreamService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    AiChatSessionQueryService sessionQueries,
    AiChatCompletionStreamer completionStreamer,
    AiChatGenerationRegistry generationRegistry,
    AiChatQuotaGuard quotaGuard,
    AiApiKeySecretProtector secretProtector,
    ICurrentTenant currentTenant,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>写入 SSE 流式回复。</summary>
    public async Task StreamAsync(
        Guid sessionId,
        Guid ownerUserId,
        StreamAiChatMessageRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var validationMessage = AiChatContentPolicy.ValidateUserMessage(request.Content);
        if (validationMessage is not null)
        {
            await WriteErrorAndCompleteAsync(httpContext, validationMessage, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var scope = AiChatScope.Resolve(currentTenant);
        var session = await sessionQueries.FindOwnedSessionAsync(scope, sessionId, ownerUserId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            await WriteErrorAndCompleteAsync(
                    httpContext,
                    "The AI chat session was not found.",
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (session.IsGenerating)
        {
            await WriteErrorAndCompleteAsync(
                    httpContext,
                    "Another generation is already in progress for this session.",
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (scope.TenantId is { } tenantId)
        {
            var quotaResult = await quotaGuard.EnsureCanStartRequestAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            if (!quotaResult.IsSuccess)
            {
                await WriteErrorAndCompleteAsync(httpContext, quotaResult.Error!.Message, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
        }

        var model = await queryExecutor.QuerySingleOrDefaultAsync<AiModelConfigRecord>(
                AiModelConfigSql.FindById,
                AiSqlParameters.Create(("ModelConfigId", session.ModelConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (model is null || !model.IsEnabled)
        {
            await WriteErrorAndCompleteAsync(
                    httpContext,
                    "The bound AI model configuration is not available.",
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        string? apiKey = null;
        if (!string.IsNullOrWhiteSpace(model.ApiKeyProtected))
        {
            apiKey = secretProtector.Unprotect(model.ApiKeyProtected);
        }

        var now = clock.UtcNow;
        var userMessageId = idGenerator.NewId();
        var assistantMessageId = idGenerator.NewId();
        var replaceTitle = session.MessageCount == 0
            && string.Equals(session.Title, AiChatContentPolicy.DefaultSessionTitle, StringComparison.Ordinal);
        var newTitle = replaceTitle
            ? AiChatContentPolicy.BuildTitleFromMessage(request.Content)
            : session.Title;

        await SetGeneratingAsync(scope, sessionId, ownerUserId, true, now, cancellationToken)
            .ConfigureAwait(false);
        await InsertMessageAsync(
                userMessageId,
                sessionId,
                AiChatMessageRoleKeys.User,
                request.Content.Trim(),
                AiChatMessageStatusKeys.Completed,
                now,
                cancellationToken)
            .ConfigureAwait(false);
        await InsertMessageAsync(
                assistantMessageId,
                sessionId,
                AiChatMessageRoleKeys.Assistant,
                string.Empty,
                AiChatMessageStatusKeys.Streaming,
                now,
                cancellationToken)
            .ConfigureAwait(false);
        await TouchSessionAsync(
                sessionId,
                messageDelta: 2,
                replaceTitle,
                newTitle,
                now,
                cancellationToken)
            .ConfigureAwait(false);

        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Connection = "keep-alive";
        httpContext.Response.ContentType = "text/event-stream";
        await httpContext.Response.StartAsync(cancellationToken).ConfigureAwait(false);

        var linkedToken = generationRegistry.Register(sessionId, cancellationToken);
        var promptCharacters = request.Content.Length;
        var completionCharacters = 0;
        string statusKey = AiChatMessageStatusKeys.Completed;
        int? promptTokens = null;
        int? completionTokens = null;
        var assistantContent = string.Empty;

        try
        {
            var history = await LoadRecentMessagesAsync(sessionId, cancellationToken).ConfigureAwait(false);
            var result = await completionStreamer.StreamAsync(
                    model,
                    apiKey,
                    history,
                    async delta =>
                    {
                        completionCharacters += delta.Length;
                        assistantContent += delta;
                        await AiChatSseWriter.WriteDeltaAsync(
                                httpContext.Response.Body,
                                delta,
                                linkedToken)
                            .ConfigureAwait(false);
                    },
                    linkedToken)
                .ConfigureAwait(false);

            assistantContent = result.Content;
            promptTokens = result.PromptTokens;
            completionTokens = result.CompletionTokens;
            promptCharacters = history.Sum(item => item.Content.Length);
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
                    cancellationToken)
                .ConfigureAwait(false);

            if (scope.TenantId is { } usageTenantId && statusKey == AiChatMessageStatusKeys.Completed)
            {
                await quotaGuard.RecordUsageAsync(
                        usageTenantId,
                        promptTokens,
                        completionTokens,
                        promptCharacters,
                        completionCharacters,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await AiChatSseWriter.WriteDoneAsync(
                    httpContext.Response.Body,
                    assistantMessageId,
                    promptTokens,
                    completionTokens,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            statusKey = AiChatMessageStatusKeys.Cancelled;
            await UpdateMessageAsync(
                    assistantMessageId,
                    sessionId,
                    assistantContent,
                    statusKey,
                    promptTokens,
                    completionTokens,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            statusKey = AiChatMessageStatusKeys.Failed;
            await UpdateMessageAsync(
                    assistantMessageId,
                    sessionId,
                    assistantContent,
                    statusKey,
                    promptTokens,
                    completionTokens,
                    CancellationToken.None)
                .ConfigureAwait(false);
            await AiChatSseWriter.WriteErrorAsync(
                    httpContext.Response.Body,
                    AiChatContentPolicy.SanitizeExternalError(ex.Message),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            generationRegistry.Unregister(sessionId);
            await SetGeneratingAsync(
                    scope,
                    sessionId,
                    ownerUserId,
                    false,
                    clock.UtcNow,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

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

        return rows
            .OrderBy(item => item.CreatedAtUtc)
            .Select(item => (item.RoleKey, item.Content))
            .ToArray();
    }

    private async Task SetGeneratingAsync(
        AiChatScope scope,
        Guid sessionId,
        Guid ownerUserId,
        bool isGenerating,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
                AiChatSql.SetSessionGenerating,
                AiChatSessionQueryService.BuildScopeParameters(
                    scope,
                    ownerUserId,
                    ("SessionId", sessionId),
                    ("IsGenerating", isGenerating),
                    ("UpdatedAtUtc", updatedAtUtc)),
                cancellationToken)
            .ConfigureAwait(false);

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

    private async Task UpdateMessageAsync(
        Guid messageId,
        Guid sessionId,
        string content,
        string statusKey,
        int? promptTokens,
        int? completionTokens,
        CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
                AiChatSql.UpdateMessage,
                AiSqlParameters.Create(
                    ("MessageId", messageId),
                    ("SessionId", sessionId),
                    ("Content", content),
                    ("StatusKey", statusKey),
                    ("PromptTokens", promptTokens),
                    ("CompletionTokens", completionTokens)),
                cancellationToken)
            .ConfigureAwait(false);

    private async Task TouchSessionAsync(
        Guid sessionId,
        int messageDelta,
        bool replaceTitle,
        string title,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
                AiChatSql.TouchSessionAfterMessage,
                AiSqlParameters.Create(
                    ("SessionId", sessionId),
                    ("MessageDelta", messageDelta),
                    ("LastMessageAtUtc", updatedAtUtc),
                    ("ReplaceTitle", replaceTitle ? 1 : 0),
                    ("Title", title),
                    ("UpdatedAtUtc", updatedAtUtc)),
                cancellationToken)
            .ConfigureAwait(false);

    private static async Task WriteErrorAndCompleteAsync(
        HttpContext httpContext,
        string message,
        CancellationToken cancellationToken)
    {
        httpContext.Response.ContentType = "text/event-stream";
        await httpContext.Response.StartAsync(cancellationToken).ConfigureAwait(false);
        await AiChatSseWriter.WriteErrorAsync(httpContext.Response.Body, message, cancellationToken)
            .ConfigureAwait(false);
    }
}
