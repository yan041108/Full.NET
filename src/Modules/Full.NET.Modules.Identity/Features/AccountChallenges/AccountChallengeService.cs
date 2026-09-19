using System.Security.Cryptography;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Identity.Features.AccountChallenges;

internal sealed class AccountChallengeService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IIdentityChallengeDeliveryPort challengeDeliveryPort,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const int DefaultMaxAttempts = 5;
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);

    public async Task<Result<AccountChallengeAcceptedResponse>> CreateAndDeliverAsync(
        IdentityAccountChallengePurpose purpose,
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null)
        {
            return Result<AccountChallengeAcceptedResponse>.Failure(InvalidChallenge());
        }

        var now = clock.UtcNow;
        var challengeId = idGenerator.NewId();
        var code = GenerateCode();
        var expiresAtUtc = now.Add(DefaultLifetime);
        await transaction.ExecuteAsync(
            async token =>
            {
                await commandExecutor.ExecuteAsync(
                        AccountChallengeSql.InvalidateActive,
                        IdentitySqlParameters.Create(
                            ("Purpose", (byte)purpose),
                            ("NormalizedEmail", normalizedEmail),
                            ("ConsumedAtUtc", now)),
                        token)
                    .ConfigureAwait(false);
                await commandExecutor.ExecuteAsync(
                        AccountChallengeSql.Insert,
                        IdentitySqlParameters.Create(
                            ("ChallengeId", challengeId),
                            ("Purpose", (byte)purpose),
                            ("NormalizedEmail", normalizedEmail),
                            ("CredentialHash", AccountChallengeCredentialHasher.Hash(challengeId, code)),
                            ("ExpiresAtUtc", expiresAtUtc),
                            ("MaxAttempts", DefaultMaxAttempts),
                            ("CreatedAtUtc", now)),
                        token)
                    .ConfigureAwait(false);
                return true;
            },
            cancellationToken)
            .ConfigureAwait(false);

        var intent = new IdentityChallengeDeliveryIntent(
            challengeId,
            purpose,
            normalizedEmail,
            code,
            expiresAtUtc,
            $"identity-challenge:{(byte)purpose}:{challengeId:N}");
        var delivered = await challengeDeliveryPort.SendAsync(intent, cancellationToken).ConfigureAwait(false);
        if (!delivered.IsSuccess)
        {
            await commandExecutor.ExecuteAsync(
                    AccountChallengeSql.InvalidateActive,
                    IdentitySqlParameters.Create(
                        ("Purpose", (byte)purpose),
                        ("NormalizedEmail", normalizedEmail),
                        ("ConsumedAtUtc", clock.UtcNow)),
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<AccountChallengeAcceptedResponse>.Failure(new Error(
                IdentityErrorCodes.AccountChallengeDeliveryFailed,
                "The verification message could not be delivered.",
                ErrorType.BusinessRule));
        }

        return Result<AccountChallengeAcceptedResponse>.Success(
            new AccountChallengeAcceptedResponse(challengeId, expiresAtUtc));
    }

    public async Task<Result<bool>> ConsumeAsync(
        Guid challengeId,
        IdentityAccountChallengePurpose purpose,
        string email,
        string credential,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null)
        {
            return Result<bool>.Failure(InvalidChallenge());
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<AccountChallengeRecord>(
                AccountChallengeSql.FindById,
                IdentitySqlParameters.Create(("ChallengeId", challengeId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null
            || record.Purpose != (byte)purpose
            || !string.Equals(record.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
        {
            return Result<bool>.Failure(InvalidChallenge());
        }

        if (record.ConsumedAtUtc.HasValue || record.ExpiresAtUtc <= clock.UtcNow)
        {
            return Result<bool>.Failure(InvalidChallenge());
        }

        if (record.AttemptCount >= record.MaxAttempts)
        {
            return Result<bool>.Failure(AttemptsExceeded());
        }

        var credentialHash = AccountChallengeCredentialHasher.Hash(challengeId, credential);
        if (!string.Equals(record.CredentialHash, credentialHash, StringComparison.Ordinal))
        {
            await commandExecutor.ExecuteAsync(
                    AccountChallengeSql.IncrementAttempt,
                    IdentitySqlParameters.Create(
                        ("ChallengeId", challengeId),
                        ("Version", record.Version)),
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<bool>.Failure(InvalidChallenge());
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                AccountChallengeSql.Consume,
                IdentitySqlParameters.Create(
                    ("ChallengeId", challengeId),
                    ("ConsumedAtUtc", clock.UtcNow),
                    ("CredentialHash", credentialHash),
                    ("Version", record.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affectedRows == 1
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(InvalidChallenge());
    }

    internal static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalized = email.Trim().ToLowerInvariant();
        return normalized.Contains('@', StringComparison.Ordinal) ? normalized : null;
    }

    private static string GenerateCode()
    {
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Error InvalidChallenge() => new(
        IdentityErrorCodes.AccountChallengeInvalid,
        "The account challenge is invalid or has expired.",
        ErrorType.Validation);

    private static Error AttemptsExceeded() => new(
        IdentityErrorCodes.AccountChallengeAttemptsExceeded,
        "The account challenge attempt limit has been exceeded.",
        ErrorType.Validation);
}
