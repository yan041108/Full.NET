using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.Smtp;

namespace Full.NET.Modules.Notifications.Features.VerifyRecipientEndpoints;

/// <summary>通过已注册的 SMTP Adapter 发送收件端点验证码邮件。</summary>
internal sealed class SmtpRecipientEndpointVerificationMailSender(
    IQueryExecutor queryExecutor,
    IEnumerable<INotificationProviderAdapter> providerAdapters) : IRecipientEndpointVerificationMailSender
{
    private const string SmtpProviderTypeKey = "email.smtp";

    /// <inheritdoc />
    public async Task<Result<bool>> SendAsync(
        Guid providerProfileVersionId,
        string recipientEmail,
        string code,
        CancellationToken cancellationToken)
    {
        var profileVersion = await queryExecutor.QuerySingleOrDefaultAsync<NotificationProviderProfileVersionRecord>(
                NotificationPlatformSql.FindProfileVersionById,
                NotificationPlatformSqlParameters.Create(("Id", providerProfileVersionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (profileVersion is null
            || !string.Equals(profileVersion.ProviderTypeKey, SmtpProviderTypeKey, StringComparison.Ordinal))
        {
            return Result<bool>.Failure(SendFailed());
        }

        var adapter = providerAdapters.SingleOrDefault(item =>
            string.Equals(item.Descriptor.ProviderTypeKey, SmtpProviderTypeKey, StringComparison.Ordinal)
            && string.Equals(item.RecipientEndpointKindKey, "email", StringComparison.Ordinal));
        if (adapter is not SmtpNotificationProviderAdapter)
        {
            return Result<bool>.Failure(SendFailed());
        }

        var request = new NotificationProviderRequest(
            providerProfileVersionId,
            "email",
            recipientEmail,
            profileVersion.NonSecretConfigJson,
            profileVersion.SecretReference,
            "Full.NET recipient endpoint verification",
            $"Your verification code is {code}. It expires in 15 minutes.",
            $"recipient-endpoint-verify:{providerProfileVersionId:N}:{recipientEmail}");
        var result = await adapter.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return result.Accepted
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(SendFailed());
    }

    private static Error SendFailed() => new(
        NotificationsErrorCodes.RecipientEndpointVerificationSendFailed,
        "The verification email could not be sent.",
        ErrorType.BusinessRule);
}
