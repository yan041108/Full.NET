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
        CancellationToken cancellationToken = default,
        Guid? recoveryUserId = null)
    {
        // 恢复目标必须来自 Identity 的权威账号读取；缺失绑定时不创建任何真实凭据。
        if (purpose == IdentityAccountChallengePurpose.PasswordRecovery
            && recoveryUserId.GetValueOrDefault() == Guid.Empty)
        {
            return Result<AccountChallengeAcceptedResponse>.Failure(InvalidChallenge());
        }

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
                            ("CredentialHash", purpose == IdentityAccountChallengePurpose.PasswordRecovery
                                ? AccountChallengeCredentialHasher.HashPasswordRecovery(challengeId, recoveryUserId!.Value, code)
                                : AccountChallengeCredentialHasher.Hash(challengeId, code)),
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
            // 投递发生在事务提交之后，迟到失败只能撤销本次挑战，不能影响已成功重发的新挑战。
            await commandExecutor.ExecuteAsync(
                    AccountChallengeSql.InvalidateById,
                    IdentitySqlParameters.Create(
                        ("ChallengeId", challengeId),
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

    /// <summary>生成匿名恢复的占位受理结果，保持正常挑战的标识与窗口形态，但不生成凭据或访问持久化。</summary>
    /// <remarks>受理结果不能证明账号存在或邮件已送达；调用方仍须通过真实挑战完成后续验证。</remarks>
    internal Result<AccountChallengeAcceptedResponse> CreateAcceptedPlaceholder()
    {
        var now = clock.UtcNow;
        return Result<AccountChallengeAcceptedResponse>.Success(
            new AccountChallengeAcceptedResponse(idGenerator.NewId(), now.Add(DefaultLifetime)));
    }

    public async Task<Result<bool>> ConsumeAsync(
        Guid challengeId,
        IdentityAccountChallengePurpose purpose,
        string email,
        string credential,
        CancellationToken cancellationToken = default,
        Guid? recoveryUserId = null)
    {
        var validated = await ValidateCredentialAsync(challengeId, purpose, email, credential, cancellationToken, recoveryUserId)
            .ConfigureAwait(false);
        if (!validated.IsSuccess)
        {
            return Result<bool>.Failure(validated.Error!);
        }

        var record = validated.Value!;
        var affectedRows = await commandExecutor.ExecuteAsync(
                AccountChallengeSql.Consume,
                IdentitySqlParameters.Create(
                    ("ChallengeId", challengeId),
                    ("ConsumedAtUtc", clock.UtcNow),
                    ("CredentialHash", record.CredentialHash),
                    ("Version", record.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        return affectedRows == 1
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(InvalidChallenge());
    }

    /// <summary>校验凭据并记录错误尝试，不消费挑战；注册须在业务事务前调用，消费时仍重新校验。</summary>
    /// <param name="challengeId">当前挑战标识。</param>
    /// <param name="purpose">必须与挑战绑定一致的用途。</param>
    /// <param name="email">必须与挑战绑定一致的目标邮箱。</param>
    /// <param name="credential">本次提交的一次性凭据，仅用于摘要比较。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="recoveryUserId">恢复用途必须提供权威账号标识；其他用途沿用原摘要。</param>
    /// <returns>校验结果；成功不代表挑战已消费。</returns>
    public async Task<Result<bool>> ValidateAsync(
        Guid challengeId,
        IdentityAccountChallengePurpose purpose,
        string email,
        string credential,
        CancellationToken cancellationToken = default,
        Guid? recoveryUserId = null)
    {
        var validated = await ValidateCredentialAsync(challengeId, purpose, email, credential, cancellationToken, recoveryUserId)
            .ConfigureAwait(false);
        return validated.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(validated.Error!);
    }

    private async Task<Result<AccountChallengeRecord>> ValidateCredentialAsync(
        Guid challengeId,
        IdentityAccountChallengePurpose purpose,
        string email,
        string credential,
        CancellationToken cancellationToken,
        Guid? recoveryUserId)
    {
        if (purpose == IdentityAccountChallengePurpose.PasswordRecovery
            && recoveryUserId.GetValueOrDefault() == Guid.Empty)
        {
            return Result<AccountChallengeRecord>.Failure(InvalidChallenge());
        }

        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null)
        {
            return Result<AccountChallengeRecord>.Failure(InvalidChallenge());
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
            return Result<AccountChallengeRecord>.Failure(InvalidChallenge());
        }

        if (record.ConsumedAtUtc.HasValue || record.ExpiresAtUtc <= clock.UtcNow)
        {
            return Result<AccountChallengeRecord>.Failure(InvalidChallenge());
        }

        if (record.AttemptCount >= record.MaxAttempts)
        {
            return Result<AccountChallengeRecord>.Failure(AttemptsExceeded());
        }

        // 不回退旧恢复摘要：旧格式无法证明原账号归属，必须重新申请；注册摘要保持兼容。
        var credentialHash = purpose == IdentityAccountChallengePurpose.PasswordRecovery
            ? AccountChallengeCredentialHasher.HashPasswordRecovery(challengeId, recoveryUserId!.Value, credential)
            : AccountChallengeCredentialHasher.Hash(challengeId, credential);
        if (!string.Equals(record.CredentialHash, credentialHash, StringComparison.Ordinal))
        {
            await commandExecutor.ExecuteAsync(
                    AccountChallengeSql.IncrementAttempt,
                    IdentitySqlParameters.Create(("ChallengeId", challengeId)),
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<AccountChallengeRecord>.Failure(InvalidChallenge());
        }

        return Result<AccountChallengeRecord>.Success(record);
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
