using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Streaming;

namespace Full.NET.Modules.Ai.Features.ManageChatSessions;

/// <summary>聊天会话创建、重命名、删除与取消生成。</summary>
internal sealed class AiChatSessionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    AiChatSessionQueryService queries,
    AiChatGenerationRegistry generationRegistry,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>创建聊天会话。</summary>
    public Task<Result<AiChatSessionResponse>> CreateAsync(
        Guid ownerUserId,
        CreateAiChatSessionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(ownerUserId, request, token),
            cancellationToken);

    /// <summary>重命名聊天会话。</summary>
    public Task<Result<AiChatSessionResponse>> RenameAsync(
        Guid sessionId,
        Guid ownerUserId,
        UpdateAiChatSessionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RenameCoreAsync(sessionId, ownerUserId, request, token),
            cancellationToken);

    /// <summary>删除聊天会话及其消息。</summary>
    public Task<Result<bool>> DeleteAsync(
        Guid sessionId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DeleteCoreAsync(sessionId, ownerUserId, token),
            cancellationToken);

    /// <summary>取消进行中的流式生成。</summary>
    public async Task<Result<bool>> CancelGenerationAsync(
        Guid sessionId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var scope = AiChatScope.Resolve(currentTenant);
        var session = await queries.FindOwnedSessionAsync(scope, sessionId, ownerUserId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            return Result<bool>.Failure(new Error(
                AiErrorCodes.ChatSessionNotFound,
                "The AI chat session was not found.",
                ErrorType.NotFound));
        }

        if (!session.IsGenerating)
        {
            return Result<bool>.Failure(new Error(
                AiErrorCodes.ChatGenerationNotActive,
                "No active AI chat generation exists for this session.",
                ErrorType.Validation));
        }

        generationRegistry.TryCancel(sessionId);
        return Result<bool>.Success(true);
    }

    private async Task<Result<AiChatSessionResponse>> CreateCoreAsync(
        Guid ownerUserId,
        CreateAiChatSessionRequest request,
        CancellationToken cancellationToken)
    {
        var scope = AiChatScope.Resolve(currentTenant);
        var model = await queryExecutor.QuerySingleOrDefaultAsync<AiModelConfigRecord>(
                AiModelConfigSql.FindById,
                AiSqlParameters.Create(("ModelConfigId", request.ModelConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (model is null || !model.IsEnabled)
        {
            return Result<AiChatSessionResponse>.Failure(new Error(
                AiErrorCodes.ModelConfigUnavailable,
                "The selected AI model configuration is not available.",
                ErrorType.Validation));
        }

        var title = string.IsNullOrWhiteSpace(request.Title)
            ? AiChatContentPolicy.DefaultSessionTitle
            : request.Title.Trim();
        if (title.Length > 256)
        {
            return ValidationFailure<AiChatSessionResponse>("Title must not exceed 256 characters.");
        }

        var sessionId = idGenerator.NewId();
        var now = clock.UtcNow;
        await commandExecutor.ExecuteAsync(
                AiChatSql.InsertSession,
                AiSqlParameters.Create(
                    ("Id", sessionId),
                    ("TenantId", scope.TenantId),
                    ("OwnerUserId", ownerUserId),
                    ("ModelConfigId", model.Id),
                    ("ModelName", model.Name),
                    ("Title", title),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        return await queries.GetByIdAsync(sessionId, ownerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<AiChatSessionResponse>> RenameCoreAsync(
        Guid sessionId,
        Guid ownerUserId,
        UpdateAiChatSessionRequest request,
        CancellationToken cancellationToken)
    {
        var scope = AiChatScope.Resolve(currentTenant);
        var title = request.Title.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 256)
        {
            return ValidationFailure<AiChatSessionResponse>("Title is required and must not exceed 256 characters.");
        }

        var affected = await commandExecutor.ExecuteAsync(
                AiChatSql.UpdateSessionTitle,
                AiChatSessionQueryService.BuildScopeParameters(
                    scope,
                    ownerUserId,
                    ("SessionId", sessionId),
                    ("Title", title),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return Result<AiChatSessionResponse>.Failure(new Error(
                AiErrorCodes.ChatSessionConcurrencyConflict,
                "The AI chat session was modified by another request.",
                ErrorType.Conflict));
        }

        return await queries.GetByIdAsync(sessionId, ownerUserId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid sessionId,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        var scope = AiChatScope.Resolve(currentTenant);
        var session = await queries.FindOwnedSessionAsync(scope, sessionId, ownerUserId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            return Result<bool>.Failure(new Error(
                AiErrorCodes.ChatSessionNotFound,
                "The AI chat session was not found.",
                ErrorType.NotFound));
        }

        generationRegistry.TryCancel(sessionId);
        await commandExecutor.ExecuteAsync(
                AiChatSql.DeleteMessagesBySession,
                AiSqlParameters.Create(("SessionId", sessionId)),
                cancellationToken)
            .ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
                AiChatSql.DeleteSession,
                AiChatSessionQueryService.BuildScopeParameters(
                    scope,
                    ownerUserId,
                    ("SessionId", sessionId)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<bool>.Success(true);
    }

    private static Result<T> ValidationFailure<T>(string message) =>
        Result<T>.Failure(new Error(
            AiErrorCodes.ChatSessionInvalid,
            message,
            ErrorType.Validation));
}
