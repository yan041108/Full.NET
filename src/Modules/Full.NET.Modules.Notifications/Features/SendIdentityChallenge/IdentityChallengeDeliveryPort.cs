using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Persistence;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.Smtp;
using Microsoft.Extensions.Options;
using Full.NET.Localization;
using System.Globalization;

/// <summary>模块内端口适配或空实现；供测试或未启用能力时闭合 DI。</summary>
namespace Full.NET.Modules.Notifications.Features.SendIdentityChallenge;

internal sealed class IdentityChallengeDeliveryPort(
    IQueryExecutor queryExecutor,
    IEnumerable<INotificationProviderAdapter> providerAdapters,
    IOptions<DatabaseOptions> databaseOptions) : IIdentityChallengeDeliveryPort
{
    private const string SmtpProviderTypeKey = "email.smtp";

    public async Task<Result<bool>> SendAsync(
        IdentityChallengeDeliveryIntent intent,
        CancellationToken cancellationToken = default)
    {
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

        var english = intent.LocaleTag == LocaleCatalog.English;
        var subject = (intent.Purpose, english) switch
        {
            (IdentityAccountChallengePurpose.PasswordRecovery, true) => "Full.NET password recovery",
            (IdentityAccountChallengePurpose.PasswordRecovery, false) => "Full.NET 找回密码",
            (IdentityAccountChallengePurpose.InvitationEmailVerification, true) => "Full.NET invitation verification",
            (IdentityAccountChallengePurpose.InvitationEmailVerification, false) => "Full.NET 邀请验证",
            (_, true) => "Full.NET registration verification",
            _ => "Full.NET 注册验证",
        };
        // 时刻使用确定的 UTC 表示，展示语言不改变验证码、用途或有效期的业务语义。
        var expires = intent.ExpiresAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
        var body = english
            ? $"Your verification code is {intent.Credential}. Valid until {expires}. If you did not request this, ignore this message."
            : $"您的验证码为 {intent.Credential}，请在 {expires} 前使用。若非本人操作，请忽略此邮件。";
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
        var result = await smtpAdapter.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return result.Accepted
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(DeliveryFailed());
    }

    private static Error DeliveryFailed() => new(
        IdentityErrorCodes.AccountChallengeDeliveryFailed,
        "The identity challenge message could not be delivered.",
        ErrorType.BusinessRule);
}
