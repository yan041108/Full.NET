using System.Text.Json.Nodes;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.AliyunSms;

namespace Full.NET.Modules.Notifications.Features.VerifyRecipientEndpoints;

/// <summary>通过已注册的阿里云短信 Adapter 发送收件端点验证码。</summary>
internal sealed class AliyunSmsRecipientEndpointVerificationSender(
    IQueryExecutor queryExecutor,
    IEnumerable<INotificationProviderAdapter> providerAdapters) : IRecipientEndpointVerificationSmsSender
{
    /// <inheritdoc />
    public async Task<Result<bool>> SendAsync(
        Guid providerProfileVersionId,
        string recipientPhone,
        string code,
        CancellationToken cancellationToken)
    {
        var profileVersion = await queryExecutor.QuerySingleOrDefaultAsync<NotificationProviderProfileVersionRecord>(
                NotificationPlatformSql.FindProfileVersionById,
                NotificationPlatformSqlParameters.Create(("Id", providerProfileVersionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (profileVersion is null
            || !string.Equals(
                profileVersion.ProviderTypeKey,
                AliyunSmsNotificationProviderAdapter.ProviderTypeKeyValue,
                StringComparison.Ordinal)
            || !AliyunSmsNotificationProviderAdapter.TryParseConfig(
                profileVersion.NonSecretConfigJson,
                out var config)
            || string.IsNullOrWhiteSpace(config!.VerificationTemplateCode))
        {
            return Result<bool>.Failure(SendFailed());
        }

        var adapter = providerAdapters.SingleOrDefault(item =>
            string.Equals(
                item.Descriptor.ProviderTypeKey,
                AliyunSmsNotificationProviderAdapter.ProviderTypeKeyValue,
                StringComparison.Ordinal)
            && string.Equals(item.RecipientEndpointKindKey, "sms", StringComparison.Ordinal));
        if (adapter is not AliyunSmsNotificationProviderAdapter)
        {
            return Result<bool>.Failure(SendFailed());
        }

        var templateParam = new JsonObject { ["code"] = code }.ToJsonString();
        var request = new NotificationProviderRequest(
            providerProfileVersionId,
            "sms",
            recipientPhone,
            profileVersion.NonSecretConfigJson,
            profileVersion.SecretReference,
            config.VerificationTemplateCode!,
            templateParam,
            $"recipient-endpoint-verify:{providerProfileVersionId:N}:{recipientPhone}",
            []);
        var result = await adapter.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return result.Accepted
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(SendFailed());
    }

    private static Error SendFailed() => new(
        NotificationsErrorCodes.RecipientEndpointVerificationSendFailed,
        "The verification SMS could not be sent.",
        ErrorType.BusinessRule);
}
