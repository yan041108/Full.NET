using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Features.ManageChatSessions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>公开问答编排：共享会话槽位，短事务启动，事务外收费，独立清理后复核并交付。</summary>
internal sealed class AiKnowledgeAskService(IQueryExecutor query, ICommandExecutor commands, ICommandTransaction transaction, IDataTransactionState transactions,
    AiChatSessionQueryService sessions, AiKnowledgePaidAnswerService paid, AiKnowledgeAnswerStore store,
    AiKnowledgeAnswerHistoryGuard history, AiKnowledgePermissionGuard permissions, AiChatGenerationRegistry registry,
    AiChatGenerationLeaseMonitor leases, AiChatCleanupScope cleanup, ICurrentTenant tenant, IClock clock, IIdGenerator ids)
{
    /// <summary>权限集合与索引读取保持一致，不能只凭 Chat.Send 执行来源推理。</summary>
    internal static readonly IReadOnlyList<string> RequiredPermissions = Array.AsReadOnly(new[] { AiChatPermissions.Send,
        AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead,
        AiKnowledgeDocumentPermissions.IndexRead });

    /// <summary>不逐 Token 输出，不自动重试收费；未知失败只返回固定机器码。</summary>
    internal async Task<Result<AskAiKnowledgeResponse>> AskAsync(Guid sessionId, Guid actor, AskAiKnowledgeRequest request,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (transactions.HasTransaction) throw new InvalidOperationException("公开问答不能加入已有事务。");
        if (actor == Guid.Empty || sessionId == Guid.Empty || request is null || request.EmbeddingModelConfigId == Guid.Empty
            || request.KnowledgeBaseIds is null || request.KnowledgeBaseIds.Count is < 1 or > 8
            || request.KnowledgeBaseIds.Any(id => id == Guid.Empty)
            || request.KnowledgeBaseIds.Distinct().Count() != request.KnowledgeBaseIds.Count
            || request.Question is null || request.Question.Length > 4096 || AiChatContentPolicy.ValidateUserMessage(request.Question) is not null)
            return Fail(AiKnowledgeErrorCodes.InputInvalid, ErrorType.BusinessRule);
        var sourceIds = Array.AsReadOnly(request.KnowledgeBaseIds.ToArray());
        var question = request.Question;
        var scope = AiChatScope.Resolve(tenant);
        var originalTenant = scope.TenantId is { } tenantId ? new TenantContext(tenantId, tenant.Identifier!, tenant.Name!) : null;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromMinutes(3));
        using var monitorStop = new CancellationTokenSource();
        var generation = ids.NewId(); var userMessage = ids.NewId();
        Task? monitor = null; var acquired = false; var attempted = false; var completed = false; var cleaned = true;
        var executionToken = budget.Token;
        var status = AiChatMessageStatusKeys.Failed; var now = clock.UtcNow;
        AiKnowledgeExecutedAnswer? answer = null;
        Result<AskAiKnowledgeResponse>? failure = null;
        try
        {
            if (!await permissions.IsAuthorizedAsync(RequiredPermissions, actor, budget.Token).ConfigureAwait(false) || !SameScope())
                return Fail("authorization.permission_denied", ErrorType.Forbidden);
            var session = await sessions.FindOwnedSessionAsync(scope, sessionId, actor, budget.Token).ConfigureAwait(false);
            if (session is null || !SameScope()) return Fail(AiErrorCodes.ChatSessionNotFound, ErrorType.NotFound);
            var modelSql = scope.TenantId.HasValue ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat;
            var generationModel = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(modelSql,
                AiSqlParameters.Create(("ModelConfigId", session.ModelConfigId)), budget.Token).ConfigureAwait(false);
            if (!SameScope()) return Fail("authorization.permission_denied", ErrorType.Forbidden);
            var embedding = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(modelSql,
                AiSqlParameters.Create(("ModelConfigId", request.EmbeddingModelConfigId)), budget.Token).ConfigureAwait(false);
            if (!SameScope()) return Fail("authorization.permission_denied", ErrorType.Forbidden);
            if (generationModel is not { IsEnabled: true } || embedding is not { IsEnabled: true })
                return Fail(AiKnowledgeErrorCodes.ModelUnavailable, ErrorType.BusinessRule);
            now = clock.UtcNow; attempted = true;
            acquired = await transaction.ExecuteAsync(async work =>
            {
                if (!SameScope()) throw new UnauthorizedAccessException();
                if (await commands.ExecuteAsync(AiChatGenerationSql.Acquire, Parameters(), work).ConfigureAwait(false) != 1) return false;
                await commands.ExecuteAsync(AiChatGenerationSql.FailAbandonedMessages, AiSqlParameters.Create(("SessionId", sessionId)), work).ConfigureAwait(false);
                RequireOne(await commands.ExecuteAsync(AiChatSql.InsertMessage, AiSqlParameters.Create(("Id", userMessage),
                    ("SessionId", sessionId), ("RoleKey", AiChatMessageRoleKeys.User), ("Content", question),
                    ("StatusKey", AiChatMessageStatusKeys.Completed), ("PromptTokens", null), ("CompletionTokens", null), ("CreatedAtUtc", now)), work).ConfigureAwait(false));
                RequireOne(await commands.ExecuteAsync(AiKnowledgeAnswerSql.InsertFor(scope.TenantId.HasValue), Parameters(), work).ConfigureAwait(false));
                var values = Parameters(); values["MessageDelta"] = 2; values["LastMessageAtUtc"] = now;
                values["UpdatedAtUtc"] = now; values["ReplaceTitle"] = session.MessageCount == 0 && session.Title == AiChatContentPolicy.DefaultSessionTitle ? 1 : 0;
                values["Title"] = AiChatContentPolicy.BuildTitleFromMessage(question);
                RequireOne(await commands.ExecuteAsync(AiChatSql.TouchSessionAfterMessage, values, work).ConfigureAwait(false));
                if (!SameScope()) throw new UnauthorizedAccessException();
                return true;
            }, budget.Token).ConfigureAwait(false);
            if (!acquired) return Fail(AiErrorCodes.ChatGenerationInProgress, ErrorType.Conflict);
            var workToken = registry.Register(sessionId, generation, budget.Token);
            executionToken = workToken;
            monitor = leases.WatchAsync(scope, sessionId, actor, generation, now + AiChatGenerationLeaseMonitor.LeaseDuration, budget, monitorStop.Token);
            if (!await leases.RenewOnceAsync(scope, sessionId, actor, generation, workToken).WaitAsync(workToken).ConfigureAwait(false)) throw new OperationCanceledException();
            answer = await paid.ExecuteAsync(sourceIds, actor, embedding, generationModel, question, workToken).ConfigureAwait(false);
            if (!SameScope() || !await store.CompleteAsync(answer, generation, sessionId, actor, workToken).ConfigureAwait(false))
                throw new UnauthorizedAccessException();
            completed = true;
            workToken.ThrowIfCancellationRequested();
        }
        catch (AiBudgetException exception)
        {
            failure = Fail(exception.Code, exception.Code == "ai.budget.operation_conflict" ? ErrorType.Conflict : ErrorType.Forbidden);
        }
        catch (OperationCanceledException)
        {
            status = AiChatMessageStatusKeys.Cancelled; failure = Fail(AiErrorCodes.ChatGenerationNotActive, ErrorType.Conflict);
        }
        catch (Exception) when (executionToken.IsCancellationRequested)
        {
            status = AiChatMessageStatusKeys.Cancelled; failure = Fail(AiErrorCodes.ChatGenerationNotActive, ErrorType.Conflict);
        }
        catch (UnauthorizedAccessException) { failure = Fail("authorization.permission_denied", ErrorType.Forbidden); }
        catch (InvalidDataException) { failure = Fail(AiErrorCodes.KnowledgeAnswerUnavailable, ErrorType.Conflict); }
        catch (Exception) { failure = Fail(AiErrorCodes.ChatGenerationFailed, ErrorType.Unexpected); }
        finally
        {
            await monitorStop.CancelAsync().ConfigureAwait(false);
            if (monitor is not null)
            {
                try { await monitor.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                catch (Exception) { cleaned = false; }
            }
            // 即使提交结果未知也按本代标识收敛；不能覆盖已完成正文或后继代次。
            if (attempted)
            {
                if (!completed)
                    cleaned &= await cleanup.RunAsync(originalTenant, async (services, work) =>
                    {
                        var values = Parameters(); values["StatusKey"] = status;
                        await services.GetRequiredService<ICommandExecutor>().ExecuteAsync(AiKnowledgeAnswerSql.FailFor(scope.TenantId.HasValue), values, work).ConfigureAwait(false);
                    }).ConfigureAwait(false);
                cleaned &= await cleanup.RunAsync(originalTenant, async (services, work) =>
                {
                    var values = Parameters(); values["Now"] = clock.UtcNow;
                    await services.GetRequiredService<ICommandExecutor>().ExecuteAsync(AiChatGenerationSql.Release, values, work).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            if (acquired) registry.Unregister(sessionId, generation);
        }
        token.ThrowIfCancellationRequested();
        if (failure is not null) return failure;
        if (!cleaned || answer is null || !completed) return Fail(AiErrorCodes.ChatGenerationFailed, ErrorType.Unexpected);
        try
        {
            // 清理、最终会话读取都可能等待；完整权限/来源复核必须是交付前最后一轮异步读取。
            if (!SameScope() || await sessions.FindOwnedSessionAsync(scope, sessionId, actor, budget.Token).ConfigureAwait(false) is null)
                return Fail(AiErrorCodes.ChatSessionNotFound, ErrorType.NotFound);
            var json = AiKnowledgeAnswerLineageCodec.Encode(AiKnowledgeAnswerLineageCodec.Create(answer, generation, sessionId));
            var verified = await history.AuthorizeAsync(json, generation, sessionId, actor, true, budget.Token).ConfigureAwait(false);
            if (verified is null || !SameScope()) return Fail(AiErrorCodes.KnowledgeAnswerUnavailable, ErrorType.Conflict);
            budget.Token.ThrowIfCancellationRequested();
            var message = AiChatMapper.MapMessage(new AiChatMessageRecord { Id = generation, SessionId = sessionId,
                RoleKey = AiChatMessageRoleKeys.Assistant, Content = answer.Answer.Text, StatusKey = AiChatMessageStatusKeys.Completed,
                CreatedAtUtc = now, IsKnowledgeAnswer = true, KnowledgeLineage = json }, verified);
            return Result<AskAiKnowledgeResponse>.Success(new(message, answer.Answer.Refused));
        }
        catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); return Fail(AiErrorCodes.ChatGenerationNotActive, ErrorType.Conflict); }
        catch (Exception) { return Fail(AiErrorCodes.KnowledgeAnswerUnavailable, ErrorType.Conflict); }

        bool SameScope() => tenant.Id == scope.TenantId && tenant.IsHost == !scope.TenantId.HasValue && (tenant.IsHost || tenant.IsAvailable);
        Dictionary<string, object?> Parameters() => AiChatSessionQueryService.BuildScopeParameters(scope, actor,
            ("SessionId", sessionId), ("GenerationId", generation), ("MessageId", generation), ("Now", now),
            ("ExpiresAtUtc", now + AiChatGenerationLeaseMonitor.LeaseDuration)).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static void RequireOne(int affected) { if (affected != 1) throw new InvalidOperationException("知识问答启动写入未确认。"); }
    private static Result<AskAiKnowledgeResponse> Fail(string code, ErrorType type) =>
        Result<AskAiKnowledgeResponse>.Failure(new Error(code, "The knowledge answer request could not be completed.", type));
}
