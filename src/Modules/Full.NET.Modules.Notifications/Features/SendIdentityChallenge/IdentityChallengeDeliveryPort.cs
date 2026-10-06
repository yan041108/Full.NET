using System.Net.Mail;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.Smtp;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.SendIdentityChallenge;

internal sealed class IdentityChallengeDeliveryPort(
    IQueryExecutor queryExecutor,
    IEnumerable<INotificationProviderAdapter> providerAdapters,
    IOptions<DatabaseOptions> databaseOptions,
    IClock clock) : IIdentityChallengeDeliveryPort
{
    private const string SmtpProviderTypeKey = "email.smtp";

    public async Task<Result<bool>> SendAsync(
        IdentityChallengeDeliveryIntent intent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (intent is null)
        {
            return Result<bool>.Failure(DeliveryFailed());
        }

        // 用途属于封闭契约；未知数值不能退化为注册邮件，也不能触发配置读取或外发。
        var subject = intent.Purpose switch
        {
            IdentityAccountChallengePurpose.RegistrationEmailVerification => "Full.NET registration verification",
            IdentityAccountChallengePurpose.PasswordRecovery => "Full.NET password recovery",
            IdentityAccountChallengePurpose.InvitationEmailVerification => "Full.NET invitation verification",
            _ => null,
        };
        if (subject is null || !IsValidIntent(intent) || intent.ExpiresAtUtc <= clock.UtcNow)
        {
            return Result<bool>.Failure(DeliveryFailed());
        }

        var statement = databaseOptions.Value.Provider == DatabaseProvider.MySql
            ? IdentityChallengeDeliverySql.FindFirstHostSmtpProfileVersionMySql
            : IdentityChallengeDeliverySql.FindFirstHostSmtpProfileVersionSqlServer;
        var profileVersion = await queryExecutor.QuerySingleOrDefaultAsync<NotificationProviderProfileVersionRecord>(
                statement,
                NotificationPlatformSqlParameters.Create(),
                cancellationToken)
            .ConfigureAwait(false);
        if (profileVersion is null)
        {
            return Result<bool>.Failure(DeliveryFailed());
        }

        var adapter = providerAdapters.SingleOrDefault(item =>
            string.Equals(item.Descriptor.ProviderTypeKey, SmtpProviderTypeKey, StringComparison.Ordinal)
            && string.Equals(item.RecipientEndpointKindKey, "email", StringComparison.Ordinal));
        if (adapter is not SmtpNotificationProviderAdapter smtpAdapter)
        {
            return Result<bool>.Failure(DeliveryFailed());
        }

        var body = $"Your verification code is {intent.Credential}. It expires at {intent.ExpiresAtUtc:u}.";
        var request = new NotificationProviderRequest(
            profileVersion.Id,
            "email",
            intent.NormalizedEmail,
            profileVersion.NonSecretConfigJson,
            profileVersion.SecretReference,
            subject,
            body,
            intent.IdempotencyKey,
            []);
        // 配置查询可能跨过有效期；进入提供程序前再次检查，已经开始的 SMTP 调用不因过期强行中断。
        cancellationToken.ThrowIfCancellationRequested();
        if (intent.ExpiresAtUtc <= clock.UtcNow)
        {
            return Result<bool>.Failure(DeliveryFailed());
        }

        var result = await smtpAdapter.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return result.Accepted
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(DeliveryFailed());
    }

    private static bool IsValidIntent(IdentityChallengeDeliveryIntent intent) =>
        intent.ChallengeId != Guid.Empty
        && IsSafeRequiredText(intent.Credential)
        && IsSafeRequiredText(intent.IdempotencyKey)
        && IsSafeRequiredText(intent.NormalizedEmail)
        // 受信调用方的规范化不能替代投递边界约束；拒绝显示名、注释、批量地址和首尾空白。
        && MailAddress.TryCreate(intent.NormalizedEmail, out var address)
        && string.Equals(address.Address, intent.NormalizedEmail, StringComparison.Ordinal);

    private static bool IsSafeRequiredText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);

    private static Error DeliveryFailed() => new(
        IdentityErrorCodes.AccountChallengeDeliveryFailed,
        "The identity challenge message could not be delivered.",
        ErrorType.BusinessRule);
}
