using Full.NET.Abstractions.Results;
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

        var subject = intent.Purpose switch
        {
            IdentityAccountChallengePurpose.PasswordRecovery => "Full.NET password recovery",
            IdentityAccountChallengePurpose.InvitationEmailVerification => "Full.NET invitation verification",
            _ => "Full.NET registration verification",
        };
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
