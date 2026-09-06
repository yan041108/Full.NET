using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Payments.Contracts;
using Full.NET.Modules.Payments.Domain;
using Full.NET.Modules.Payments.Persistence;
using Full.NET.Modules.Payments.Security;

namespace Full.NET.Modules.Payments.Features.ManageMerchantConfigs;

/// <summary>支付商户配置创建、更新与禁用。</summary>
internal sealed class PaymentMerchantConfigManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    PaymentMerchantConfigQueryService queries,
    IIdentityActiveTenantDirectory activeTenants,
    PaymentSecretProtector secretProtector,
    IClock clock,
    IIdGenerator idGenerator)
{
    /// <summary>创建商户配置。</summary>
    /// <param name="request">创建请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建结果或稳定业务错误。</returns>
    public Task<Result<PaymentMerchantConfigResponse>> CreateAsync(
        CreatePaymentMerchantConfigRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    /// <summary>更新商户配置。</summary>
    /// <param name="merchantConfigId">配置标识。</param>
    /// <param name="request">更新请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的配置或稳定业务错误。</returns>
    public Task<Result<PaymentMerchantConfigResponse>> UpdateAsync(
        Guid merchantConfigId,
        UpdatePaymentMerchantConfigRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(merchantConfigId, request, token),
            cancellationToken);

    /// <summary>禁用商户配置。</summary>
    /// <param name="merchantConfigId">配置标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>禁用后的配置或稳定业务错误。</returns>
    public Task<Result<PaymentMerchantConfigResponse>> DisableAsync(
        Guid merchantConfigId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(merchantConfigId, token),
            cancellationToken);

    private async Task<Result<PaymentMerchantConfigResponse>> CreateCoreAsync(
        CreatePaymentMerchantConfigRequest request,
        CancellationToken cancellationToken)
    {
        var channelKey = request.ChannelKey.Trim();
        var merchantId = NormalizeMerchantId(channelKey, request.MerchantId);
        var certificateSerialNo = NormalizeCertificateSerialNo(channelKey, request.CertificateSerialNo);
        var returnUrl = PaymentMerchantFieldValidator.NormalizeReturnUrl(request.ReturnUrl);

        var validationMessage = PaymentMerchantFieldValidator.ValidateMetadata(
            request.Name,
            channelKey,
            request.AppId,
            merchantId,
            certificateSerialNo,
            request.NotifyUrl,
            request.ReturnUrl);
        if (validationMessage is not null)
        {
            return ValidationFailure<PaymentMerchantConfigResponse>(validationMessage);
        }

        var secretValidation = ValidateCreateSecrets(channelKey, request.ApiV3Key, request.PrivateKeyPem);
        if (secretValidation is not null)
        {
            return ValidationFailure<PaymentMerchantConfigResponse>(secretValidation);
        }

        var tenantValidation = await ValidateTenantScopeAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);
        if (!tenantValidation.IsSuccess)
        {
            return Result<PaymentMerchantConfigResponse>.Failure(tenantValidation.Error!);
        }

        var now = clock.UtcNow;
        var merchantConfigId = idGenerator.NewId();
        var protectedApiV3Key = string.IsNullOrWhiteSpace(request.ApiV3Key)
            ? null
            : secretProtector.ProtectApiV3Key(request.ApiV3Key.Trim());
        var protectedPrivateKey = secretProtector.ProtectPrivateKey(request.PrivateKeyPem!.Trim());

        if (request.IsDefault)
        {
            await ClearDefaultForScopeAsync(
                    request.TenantId,
                    channelKey,
                    merchantConfigId,
                    now,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await commandExecutor.ExecuteAsync(
                PaymentMerchantConfigSql.Insert,
                PaymentSqlParameters.Create(
                    ("Id", merchantConfigId),
                    ("TenantId", request.TenantId),
                    ("Name", request.Name.Trim()),
                    ("ChannelKey", channelKey),
                    ("AppId", request.AppId.Trim()),
                    ("MerchantId", merchantId),
                    ("CertificateSerialNo", certificateSerialNo),
                    ("NotifyUrl", request.NotifyUrl.Trim()),
                    ("ReturnUrl", returnUrl),
                    ("ApiV3KeyProtected", protectedApiV3Key),
                    ("PrivateKeyProtected", protectedPrivateKey),
                    ("IsDefault", request.IsDefault),
                    ("IsEnabled", request.IsEnabled),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", null),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);

        return await queries.GetByIdAsync(merchantConfigId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<PaymentMerchantConfigResponse>> UpdateCoreAsync(
        Guid merchantConfigId,
        UpdatePaymentMerchantConfigRequest request,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(
                PaymentMerchantConfigSql.FindById,
                PaymentSqlParameters.Create(("MerchantConfigId", merchantConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFoundDetail();
        }

        var channelKey = request.ChannelKey.Trim();
        var merchantId = NormalizeMerchantId(channelKey, request.MerchantId);
        var certificateSerialNo = NormalizeCertificateSerialNo(channelKey, request.CertificateSerialNo);
        var returnUrl = PaymentMerchantFieldValidator.NormalizeReturnUrl(request.ReturnUrl);

        var validationMessage = PaymentMerchantFieldValidator.ValidateMetadata(
            request.Name,
            channelKey,
            request.AppId,
            merchantId,
            certificateSerialNo,
            request.NotifyUrl,
            request.ReturnUrl);
        if (validationMessage is not null)
        {
            return ValidationFailure<PaymentMerchantConfigResponse>(validationMessage);
        }

        var protectedApiV3Key = ResolveProtectedApiV3Key(current, request);
        var protectedPrivateKey = ResolveProtectedPrivateKey(current, request);
        var secretValidation = ValidateUpdateSecrets(
            channelKey,
            protectedApiV3Key,
            protectedPrivateKey,
            request.PrivateKeyPem);
        if (secretValidation is not null)
        {
            return ValidationFailure<PaymentMerchantConfigResponse>(secretValidation);
        }

        var now = clock.UtcNow;
        if (request.IsDefault)
        {
            await ClearDefaultForScopeAsync(
                    current.TenantId,
                    channelKey,
                    merchantConfigId,
                    now,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var affected = await commandExecutor.ExecuteAsync(
                PaymentMerchantConfigSql.Update,
                PaymentSqlParameters.Create(
                    ("MerchantConfigId", merchantConfigId),
                    ("Name", request.Name.Trim()),
                    ("ChannelKey", channelKey),
                    ("AppId", request.AppId.Trim()),
                    ("MerchantId", merchantId),
                    ("CertificateSerialNo", certificateSerialNo),
                    ("NotifyUrl", request.NotifyUrl.Trim()),
                    ("ReturnUrl", returnUrl),
                    ("ApiV3KeyProtected", protectedApiV3Key),
                    ("PrivateKeyProtected", protectedPrivateKey),
                    ("IsDefault", request.IsDefault),
                    ("IsEnabled", request.IsEnabled),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyFailure<PaymentMerchantConfigResponse>();
        }

        return await queries.GetByIdAsync(merchantConfigId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<PaymentMerchantConfigResponse>> DisableCoreAsync(
        Guid merchantConfigId,
        CancellationToken cancellationToken)
    {
        var current = await queryExecutor.QuerySingleOrDefaultAsync<PaymentMerchantConfigRecord>(
                PaymentMerchantConfigSql.FindById,
                PaymentSqlParameters.Create(("MerchantConfigId", merchantConfigId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return NotFoundDetail();
        }

        var affected = await commandExecutor.ExecuteAsync(
                PaymentMerchantConfigSql.Disable,
                PaymentSqlParameters.Create(
                    ("MerchantConfigId", merchantConfigId),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", current.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected == 0)
        {
            return ConcurrencyFailure<PaymentMerchantConfigResponse>();
        }

        return await queries.GetByIdAsync(merchantConfigId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ClearDefaultForScopeAsync(
        Guid? tenantId,
        string channelKey,
        Guid excludeMerchantConfigId,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
                PaymentMerchantConfigSql.ClearDefaultForScope,
                PaymentSqlParameters.Create(
                    ("TenantId", tenantId),
                    ("ChannelKey", channelKey),
                    ("ExcludeMerchantConfigId", excludeMerchantConfigId),
                    ("UpdatedAtUtc", updatedAtUtc)),
                cancellationToken)
            .ConfigureAwait(false);

    private string? ResolveProtectedApiV3Key(
        PaymentMerchantConfigRecord current,
        UpdatePaymentMerchantConfigRequest request)
    {
        if (request.ClearApiV3Key)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.ApiV3Key))
        {
            return secretProtector.ProtectApiV3Key(request.ApiV3Key.Trim());
        }

        return current.ApiV3KeyProtected;
    }

    private string? ResolveProtectedPrivateKey(
        PaymentMerchantConfigRecord current,
        UpdatePaymentMerchantConfigRequest request)
    {
        if (request.ClearPrivateKey)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.PrivateKeyPem))
        {
            return secretProtector.ProtectPrivateKey(request.PrivateKeyPem.Trim());
        }

        return current.PrivateKeyProtected;
    }

    private static string? ValidateCreateSecrets(
        string channelKey,
        string? apiV3Key,
        string? privateKeyPem)
    {
        if (string.Equals(channelKey, PaymentChannelKeys.WeChatNative, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(apiV3Key))
        {
            return "API v3 key is required when creating a WeChat merchant configuration.";
        }

        return PaymentMerchantFieldValidator.ValidatePrivateKeyPem(privateKeyPem ?? string.Empty);
    }

    private static string? ValidateUpdateSecrets(
        string channelKey,
        string? protectedApiV3Key,
        string? protectedPrivateKey,
        string? privateKeyPem)
    {
        if (string.IsNullOrWhiteSpace(protectedPrivateKey))
        {
            return "Private key must remain configured.";
        }

        if (!string.IsNullOrWhiteSpace(privateKeyPem))
        {
            var privateKeyValidation = PaymentMerchantFieldValidator.ValidatePrivateKeyPem(privateKeyPem);
            if (privateKeyValidation is not null)
            {
                return privateKeyValidation;
            }
        }

        if (string.Equals(channelKey, PaymentChannelKeys.WeChatNative, StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(protectedApiV3Key))
        {
            return "API v3 key must remain configured for WeChat merchant configurations.";
        }

        return null;
    }

    private static string NormalizeMerchantId(string channelKey, string merchantId)
    {
        if (string.Equals(channelKey, PaymentChannelKeys.AlipayPage, StringComparison.Ordinal))
        {
            return PaymentMerchantFieldValidator.NormalizeAlipayOptionalField(merchantId);
        }

        return merchantId.Trim();
    }

    private static string NormalizeCertificateSerialNo(string channelKey, string certificateSerialNo)
    {
        if (string.Equals(channelKey, PaymentChannelKeys.AlipayPage, StringComparison.Ordinal))
        {
            return PaymentMerchantFieldValidator.NormalizeAlipayOptionalField(certificateSerialNo);
        }

        return certificateSerialNo.Trim();
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
                PaymentErrorCodes.TenantNotFound,
                "The specified tenant does not exist or is not active.",
                ErrorType.Validation));
        }

        return Result<bool>.Success(true);
    }

    private static Result<T> ValidationFailure<T>(string message) =>
        Result<T>.Failure(new Error(
            PaymentErrorCodes.MerchantConfigInvalid,
            message,
            ErrorType.Validation));

    private static Result<T> ConcurrencyFailure<T>() =>
        Result<T>.Failure(new Error(
            PaymentErrorCodes.MerchantConfigConcurrencyConflict,
            "The payment merchant configuration was modified by another request.",
            ErrorType.Conflict));

    private static Result<PaymentMerchantConfigResponse> NotFoundDetail() =>
        Result<PaymentMerchantConfigResponse>.Failure(new Error(
            PaymentErrorCodes.MerchantConfigNotFound,
            "The payment merchant configuration was not found.",
            ErrorType.NotFound));
}
