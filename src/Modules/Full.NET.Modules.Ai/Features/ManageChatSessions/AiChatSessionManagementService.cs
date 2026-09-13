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
/// <param name="queryExecutor">当前请求查询执行器。</param>
/// <param name="commandExecutor">当前请求命令执行器。</param>
/// <param name="transaction">会话状态变更短事务。</param>
/// <param name="queries">会话归属查询服务。</param>
/// <param name="generationRegistry">仅加速当前代次取消的本地注册表。</param>
/// <param name="currentTenant">可信租户上下文。</param>
/// <param name="clock">生成租约有效性判断时钟。</param>
/// <param name="idGenerator">会话标识生成器。</param>
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
    /// <summary>在所属范围创建会话并绑定可用模型。</summary>
    /// <param name="ownerUserId">当前会话所有者。</param>
    /// <param name="request">已经过入口绑定的请求。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    public Task<Result<AiChatSessionResponse>> CreateAsync(
        Guid ownerUserId,
        CreateAiChatSessionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(ownerUserId, request, token),
            cancellationToken);

    /// <summary>以版本约束更新会话标题。</summary>
    /// <param name="sessionId">已授权的会话标识。</param>
    /// <param name="ownerUserId">当前会话所有者。</param>
    /// <param name="request">已经过入口绑定的请求。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    public Task<Result<AiChatSessionResponse>> RenameAsync(
        Guid sessionId,
        Guid ownerUserId,
        UpdateAiChatSessionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RenameCoreAsync(sessionId, ownerUserId, request, token),
            cancellationToken);

    /// <summary>由外层事务包裹的写工具重命名；不得再嵌套开启事务。</summary>
    internal Task<Result<AiChatSessionResponse>> RenameInCurrentTransactionAsync(
        Guid sessionId,
        Guid ownerUserId,
        UpdateAiChatSessionRequest request,
        CancellationToken cancellationToken = default) =>
        RenameCoreAsync(sessionId, ownerUserId, request, cancellationToken);

    /// <summary>删除已授权会话与本模块消息。</summary>
    /// <param name="sessionId">已授权的会话标识。</param>
    /// <param name="ownerUserId">当前会话所有者。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    public Task<Result<bool>> DeleteAsync(
        Guid sessionId,
        Guid ownerUserId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DeleteCoreAsync(sessionId, ownerUserId, token),
            cancellationToken);

    /// <summary>持久化指定代次的取消请求，并尝试本地加速取消。</summary>
    /// <param name="sessionId">已授权的会话标识。</param>
    /// <param name="ownerUserId">当前会话所有者。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
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

        if (!session.IsGenerating || session.GenerationId is null || session.GenerationExpiresAtUtc <= clock.UtcNow)
        {
            return Result<bool>.Failure(new Error(
                AiErrorCodes.ChatGenerationNotActive,
                "No active AI chat generation exists for this session.",
                ErrorType.Validation));
        }

        var affected = await commandExecutor.ExecuteAsync(AiChatGenerationSql.RequestCancellation,
            AiChatSessionQueryService.BuildScopeParameters(scope, ownerUserId, ("SessionId", sessionId),
                ("GenerationId", session.GenerationId)), cancellationToken).ConfigureAwait(false);
        if (affected != 1)
            return Result<bool>.Failure(new Error(AiErrorCodes.ChatGenerationNotActive,
                "The generation has already finished or changed.", ErrorType.Conflict));
        if (session.GenerationId is { } generationId) generationRegistry.TryCancel(sessionId, generationId);
        return Result<bool>.Success(true);
    }
    /// <summary>仅从本租户或共享模型创建会话。</summary>
    /// <param name="ownerUserId">当前会话所有者。</param>
    /// <param name="request">已经过入口绑定的请求。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
    private async Task<Result<AiChatSessionResponse>> CreateCoreAsync(
        Guid ownerUserId,
        CreateAiChatSessionRequest request,
        CancellationToken cancellationToken)
    {
        var scope = AiChatScope.Resolve(currentTenant);
        var model = await queryExecutor.QuerySingleOrDefaultAsync<AiModelConfigRecord>(
                scope.TenantId.HasValue
                    ? AiModelConfigSql.FindAvailableForTenantChat
                    : AiModelConfigSql.FindAvailableForHostChat,
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
    /// <summary>验证标题并防止覆盖并发修改。</summary>
    /// <param name="sessionId">已授权的会话标识。</param>
    /// <param name="ownerUserId">当前会话所有者。</param>
    /// <param name="request">已经过入口绑定的请求。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
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
    /// <summary>先验证所有权，再取消及删除该会话。</summary>
    /// <param name="sessionId">已授权的会话标识。</param>
    /// <param name="ownerUserId">当前会话所有者。</param>
    /// <param name="cancellationToken">取消当前操作的令牌。</param>
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

        if (session.GenerationId is { } generationId) generationRegistry.TryCancel(sessionId, generationId);
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
    /// <summary>生成会话输入校验错误。</summary>
    /// <param name="message">可对外返回的安全错误摘要。</param>
    private static Result<T> ValidationFailure<T>(string message) =>
        Result<T>.Failure(new Error(
            AiErrorCodes.ChatSessionInvalid,
            message,
            ErrorType.Validation));
}
