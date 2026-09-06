using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Ai.Features.ManageModelConfigs;

/// <summary>AI 模型配置创建、更新与禁用。</summary>
internal sealed class AiModelConfigManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    AiModelConfigQueryService queries,
    IIdentityActiveTenantDirectory activeTenants,
    AiApiKeySecretProtector secretProtector,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>创建模型配置。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建结果或稳定业务错误。</returns>
    public Task<Result<AiModelConfigResponse>> CreateAsync(
        CreateAiModelConfigRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    /// <summary>更新模型配置。</summary>
    /// <param name="modelConfigId">配置标识。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的配置或稳定业务错误。</returns>
    public Task<Result<AiModelConfigResponse>> UpdateAsync(
        Guid modelConfigId,
        UpdateAiModelConfigRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(modelConfigId, request, token),
            cancellationToken);

    /// <summary>禁用模型配置。</summary>
    /// <param name="modelConfigId">配置标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>禁用后的配置或稳定业务错误。</returns>
    public Task<Result<AiModelConfigResponse>> DisableAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(modelConfigId, token),
            cancellationToken);

    private async Task<Result<AiModelConfigResponse>> CreateCoreAsync(
        CreateAiModelConfigRequest request,
        CancellationToken cancellationToken)
    {
        var validationMessage = AiModelConfigFieldValidator.ValidateMetadata(
            request.Name,
            request.ProviderKey,
            request.EndpointBaseUrl,
            request.ModelId,
            request.OrganizationId);
        if (validationMessage is not null)
        {
            return ValidationFailure<AiModelConfigResponse>(validationMessage);
        }

        if (AiModelConfigFieldValidator.RequiresApiKey(request.ProviderKey)
            && string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return Result<AiModelConfigResponse>.Failure(new Error(
                AiErrorCodes.ModelConfigApiKeyRequired,
                "API key is required when creating an openai_compatible model configuration.",
                ErrorType.Validation));
        }

        var tenantValidation = await ValidateTenantScopeAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!tenantValidation.IsSuccess)
        {
            return Result<AiModelConfigResponse>.Failure(tenantValidation.Error!);
        }

        var now = clock.UtcNow;
        var modelConfigId = idGenerator.NewId();
        string? protectedApiKey = string.IsNullOrWhiteSpace(request.ApiKey)
            ? null
            : secretProtector.Protect(request.ApiKey.Trim());

        if (request.IsDefault)
        {
            await ClearDefaultForScopeAsync(request.TenantId, modelConfigId, now, cancellationToken)
                .ConfigureAwait(false);
        }

        await commandExecutor.ExecuteAsync(
                AiModelConfigSql.Insert,
                AiSqlParameters.Create(
                    ("Id", modelConfigId),
                    ("TenantId", request.TenantId),
                    ("Name", request.Name.Trim()),
                    ("ProviderKey", request.ProviderKey.Trim()),
                    ("EndpointBaseUrl", request.EndpointBaseUrl.Trim()),
                    ("ModelId", request.ModelId.Trim()),
                    ("ApiKeyProtected", protectedApiKey),
                    ("OrganizationId", NormalizeOptional(request.OrganizationId)),
                    ("IsDefault", request.IsDefault),
                    ("IsEnabled", request.IsEnabled),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", null),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(modelConfigId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<AiModelConfigResponse>> UpdateCoreAsync(
        Guid modelConfigId,
        UpdateAiModelConfigRequest request,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<AiModelConfigRecord>(
                AiModelConfigSql.FindById,
                AiSqlParameters.Create(("ModelConfigId", modelConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFoundDetail();
        }

        var validationMessage = AiModelConfigFieldValidator.ValidateMetadata(
            request.Name,
            request.ProviderKey,
            request.EndpointBaseUrl,
            request.ModelId,
            request.OrganizationId);
        if (validationMessage is not null)
        {
            return ValidationFailure<AiModelConfigResponse>(validationMessage);
        }

        var protectedApiKey = ResolveProtectedApiKey(current, request);
        if (AiModelConfigFieldValidator.RequiresApiKey(request.ProviderKey)
            && string.IsNullOrWhiteSpace(protectedApiKey))
        {
            return Result<AiModelConfigResponse>.Failure(new Error(
                AiErrorCodes.ModelConfigApiKeyRequired,
                "API key must remain configured for openai_compatible provider.",
                ErrorType.Validation));
        }

        var now = clock.UtcNow;
        if (request.IsDefault)
        {
            await ClearDefaultForScopeAsync(current.TenantId, modelConfigId, now, cancellationToken)
                .ConfigureAwait(false);
        }

        var affected = await commandExecutor.ExecuteAsync(
                AiModelConfigSql.Update,
                AiSqlParameters.Create(
                    ("ModelConfigId", modelConfigId),
                    ("Name", request.Name.Trim()),
                    ("ProviderKey", request.ProviderKey.Trim()),
                    ("EndpointBaseUrl", request.EndpointBaseUrl.Trim()),
                    ("ModelId", request.ModelId.Trim()),
                    ("ApiKeyProtected", protectedApiKey),
                    ("OrganizationId", NormalizeOptional(request.OrganizationId)),
                    ("IsDefault", request.IsDefault),
                    ("IsEnabled", request.IsEnabled),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyFailure<AiModelConfigResponse>();
        }

        return await queries.GetByIdAsync(modelConfigId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<AiModelConfigResponse>> DisableCoreAsync(
        Guid modelConfigId,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<AiModelConfigRecord>(
                AiModelConfigSql.FindById,
                AiSqlParameters.Create(("ModelConfigId", modelConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFoundDetail();
        }

        var affected = await commandExecutor.ExecuteAsync(
                AiModelConfigSql.Disable,
                AiSqlParameters.Create(
                    ("ModelConfigId", modelConfigId),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", current.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyFailure<AiModelConfigResponse>();
        }

        return await queries.GetByIdAsync(modelConfigId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ClearDefaultForScopeAsync(
        Guid? tenantId,
        Guid excludeModelConfigId,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
                AiModelConfigSql.ClearDefaultForScope,
                AiSqlParameters.Create(
                    ("TenantId", tenantId),
                    ("ExcludeModelConfigId", excludeModelConfigId),
                    ("UpdatedAtUtc", updatedAtUtc)),
                cancellationToken)
            .ConfigureAwait(false);

    private string? ResolveProtectedApiKey(
        AiModelConfigRecord current,
        UpdateAiModelConfigRequest request)
    {
        if (request.ClearApiKey)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return secretProtector.Protect(request.ApiKey.Trim());
        }

        return current.ApiKeyProtected;
    }

    private async Task<Result<bool>> ValidateTenantScopeAsync(
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId is null)
        {
            return Result<bool>.Success(true);
        }

        var exists = await activeTenants.IsActiveTenantAsync(tenantId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
        {
            return Result<bool>.Failure(new Error(
                AiErrorCodes.TenantNotFound,
                "The specified tenant does not exist or is not active.",
                ErrorType.Validation));
        }

        return Result<bool>.Success(true);
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static Result<T> ValidationFailure<T>(string message) =>
        Result<T>.Failure(new Error(
            AiErrorCodes.ModelConfigInvalid,
            message,
            ErrorType.Validation));

    private static Result<T> ConcurrencyFailure<T>() =>
        Result<T>.Failure(new Error(
            AiErrorCodes.ModelConfigConcurrencyConflict,
            "The AI model configuration was modified by another request.",
            ErrorType.Conflict));

    private static Result<AiModelConfigResponse> NotFoundDetail() =>
        Result<AiModelConfigResponse>.Failure(new Error(
            AiErrorCodes.ModelConfigNotFound,
            "The AI model configuration was not found.",
            ErrorType.NotFound));
}
