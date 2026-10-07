using System.Net.Mail;
using System.Security.Cryptography;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Identity.Features.AccountChallenges;

internal sealed partial class AccountChallengeService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IIdentityChallengeDeliveryPort challengeDeliveryPort,
    IClock clock,
    IIdGenerator idGenerator,
    ILogger<AccountChallengeService>? logger = null)
{
    private const int DefaultMaxAttempts = 5;
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan CompensationTimeout = TimeSpan.FromSeconds(5);

    public async Task<Result<AccountChallengeAcceptedResponse>> CreateAndDeliverAsync(
        IdentityAccountChallengePurpose purpose,
        string email,
        CancellationToken cancellationToken = default,
        Guid? recoveryUserId = null,
        string? recoverySecurityStamp = null)
    {
        // 恢复目标必须来自 Identity 的权威账号读取；缺失绑定时不创建任何真实凭据。
        if (purpose == IdentityAccountChallengePurpose.PasswordRecovery
            && (recoveryUserId.GetValueOrDefault() == Guid.Empty || string.IsNullOrWhiteSpace(recoverySecurityStamp)))
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
                                ? AccountChallengeCredentialHasher.HashPasswordRecovery(challengeId, recoveryUserId!.Value, recoverySecurityStamp!, code)
                                : AccountChallengeCredentialHasher.Hash(challengeId, code)),
                            ("ExpiresAtUtc", expiresAtUtc),
                            ("MaxAttempts", DefaultMaxAttempts),
                            ("DeliveryStateKey", "unknown"),
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
        Result<bool> delivered;
        try
        {
            var outcome = IdentityChallengeDeliveryOutcome.Unknown;
            if (challengeDeliveryPort is IIdentityChallengeDeliveryOutcomePort outcomePort)
            {
                outcome = await outcomePort.SendWithOutcomeAsync(intent, cancellationToken).ConfigureAwait(false);
                delivered = outcome == IdentityChallengeDeliveryOutcome.Accepted
                    ? Result<bool>.Success(true) : Result<bool>.Failure(DeliveryFailed());
            }
            else
            {
                // 旧 Port 未保留失败阶段，非受理结果只能保守记录为 unknown，不能推断确定未发送。
                delivered = await challengeDeliveryPort.SendAsync(intent, cancellationToken).ConfigureAwait(false);
                if (delivered.IsSuccess && delivered.Value) outcome = IdentityChallengeDeliveryOutcome.Accepted;
            }

            // 日记与挑战同一行原子创建，外发不进入事务；仅确切受理并持久化后才返回真实挑战。
            if (!delivered.IsSuccess || !delivered.Value) cancellationToken.ThrowIfCancellationRequested();
            // 外部明确受理后即使请求已取消，也独立限时保存事实；写入失败仍不得返回真实挑战受理成功。
            using var completionTimeout = new CancellationTokenSource(CompensationTimeout);
            var completed = await commandExecutor.ExecuteAsync(AccountChallengeSql.CompleteDelivery,
                IdentitySqlParameters.Create(("ChallengeId", challengeId), ("CompletedAtUtc", clock.UtcNow),
                    ("DeliveryStateKey", outcome switch
                    {
                        IdentityChallengeDeliveryOutcome.Accepted => "accepted",
                        IdentityChallengeDeliveryOutcome.Rejected => "rejected",
                        _ => "unknown",
                    })), completionTimeout.Token).ConfigureAwait(false);
            if (completed != 1) delivered = Result<bool>.Failure(DeliveryFailed());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 挑战已经提交，即使请求取消也须尝试撤销；补偿失败不能覆盖原始调用方取消。
            try { await InvalidateUndeliveredAsync(challengeId, purpose).ConfigureAwait(false); }
            catch (Exception) { /* 补偿方法已记录安全诊断，保持原取消异常和令牌。 */ }
            throw;
        }
        catch (Exception)
        {
            // 投递异常不证明未发送，不自动重试；仅撤销本次凭据，匿名恢复仍返回占位受理。
            // 适配器异常可能携带明文凭据，日志只记录安全标识；调用方取消保持向上传播。
            if (logger is not null) LogDeliveryException(logger, challengeId, (byte)purpose);
            delivered = Result<bool>.Failure(DeliveryFailed());
        }
        // Port 契约只有成功且明确 true 才代表受理，false 不能留下可消费的真实挑战。
        if (!delivered.IsSuccess || !delivered.Value)
        {
            try { await InvalidateUndeliveredAsync(challengeId, purpose).ConfigureAwait(false); }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // 传输返回失败时也可能已经取消；保留调用方令牌，不把数据库失败转换成受理成功。
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Result<AccountChallengeAcceptedResponse>.Failure(DeliveryFailed());
        }

        return Result<AccountChallengeAcceptedResponse>.Success(
            new AccountChallengeAcceptedResponse(challengeId, expiresAtUtc));
    }

    /// <summary>提交后以独立五秒取消期限撤销本次未确认受理的挑战，不重试外部投递。</summary>
    /// <remarks>迟到补偿只限定当前标识；数据库必须遵守令牌，补偿失败继续传播并留下安全诊断。</remarks>
    private async Task InvalidateUndeliveredAsync(Guid challengeId, IdentityAccountChallengePurpose purpose)
    {
        using var timeout = new CancellationTokenSource(CompensationTimeout);
        try
        {
            await commandExecutor.ExecuteAsync(
                    AccountChallengeSql.InvalidateById,
                    IdentitySqlParameters.Create(("ChallengeId", challengeId), ("ConsumedAtUtc", clock.UtcNow)),
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 原始投递及数据库异常均可能含敏感数据；日志只能记录安全定位字段，不能宣称撤销成功。
            if (logger is not null) LogCompensationFailure(logger, challengeId, (byte)purpose);
            throw;
        }
    }

    /// <summary>生成匿名恢复的占位受理结果，保持正常挑战的标识与窗口形态，但不生成凭据或访问持久化。</summary>
    /// <remarks>受理结果不能证明账号存在或邮件已送达；调用方仍须通过真实挑战完成后续验证。</remarks>
    internal Result<AccountChallengeAcceptedResponse> CreateAcceptedPlaceholder()
    {
        var now = clock.UtcNow;
        return Result<AccountChallengeAcceptedResponse>.Success(
            new AccountChallengeAcceptedResponse(idGenerator.NewId(), now.Add(DefaultLifetime)));
    }

    /// <summary>对账已完成或已到期的未知/拒收挑战，原子撤销当前标识；不重发邮件，不访问其他模块表。</summary>
    /// <remarks>有效且仍在发送的挑战不变，明确受理和旧版空状态记录不变；重复对账为无操作。</remarks>
    public async Task<bool> ReconcileUnconfirmedAsync(Guid challengeId, CancellationToken cancellationToken = default) =>
        await commandExecutor.ExecuteAsync(AccountChallengeSql.ReconcileDelivery,
            IdentitySqlParameters.Create(("ChallengeId", challengeId), ("Now", clock.UtcNow)), cancellationToken)
            .ConfigureAwait(false) == 1;

    public async Task<Result<bool>> ConsumeAsync(
        Guid challengeId,
        IdentityAccountChallengePurpose purpose,
        string email,
        string credential,
        CancellationToken cancellationToken = default,
        Guid? recoveryUserId = null,
        string? recoverySecurityStamp = null)
    {
        var validated = await ValidateCredentialAsync(challengeId, purpose, email, credential, cancellationToken, recoveryUserId, recoverySecurityStamp)
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
    /// <param name="recoverySecurityStamp">恢复用途提供同次权威读取的安全戳，不能信任请求正文。</param>
    /// <returns>校验结果；成功不代表挑战已消费。</returns>
    public async Task<Result<bool>> ValidateAsync(
        Guid challengeId,
        IdentityAccountChallengePurpose purpose,
        string email,
        string credential,
        CancellationToken cancellationToken = default,
        Guid? recoveryUserId = null,
        string? recoverySecurityStamp = null)
    {
        var validated = await ValidateCredentialAsync(challengeId, purpose, email, credential, cancellationToken, recoveryUserId, recoverySecurityStamp)
            .ConfigureAwait(false);
        return validated.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(validated.Error!);
    }

    private async Task<Result<AccountChallengeRecord>> ValidateCredentialAsync(
        Guid challengeId,
        IdentityAccountChallengePurpose purpose,
        string email,
        string credential,
        CancellationToken cancellationToken,
        Guid? recoveryUserId,
        string? recoverySecurityStamp)
    {
        if (purpose == IdentityAccountChallengePurpose.PasswordRecovery
            && (recoveryUserId.GetValueOrDefault() == Guid.Empty || string.IsNullOrWhiteSpace(recoverySecurityStamp)))
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

        // 新挑战必须有持久化受理证据。旧版 null 状态只按原过期窗口兼容，不续期、不提升为 accepted。
        // 即使补偿数据库失败，未知挑战也不能消费；SQL 再次核对状态竞态，且不撤销有效在途请求。
        if (record.DeliveryStateKey is not (null or "accepted"))
        {
            await ReconcileUnconfirmedAsync(challengeId, cancellationToken).ConfigureAwait(false);
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

        // 不回退旧恢复摘要：旧格式缺少账号或安全戳绑定，必须重新申请；注册摘要保持兼容。
        var credentialHash = purpose == IdentityAccountChallengePurpose.PasswordRecovery
            ? AccountChallengeCredentialHasher.HashPasswordRecovery(challengeId, recoveryUserId!.Value, recoverySecurityStamp!, credential)
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
        if (string.IsNullOrWhiteSpace(email) || email.Any(char.IsControl))
        {
            return null;
        }

        var normalized = email.Trim().ToLowerInvariant();
        // 保持已有大小写规范化，只接受裸的单个地址；拒绝显示名、注释及批量收件人。
        return MailAddress.TryCreate(normalized, out var address)
            && string.Equals(address.Address, normalized, StringComparison.Ordinal)
            ? normalized : null;
    }

    private static string GenerateCode()
    {
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    [LoggerMessage(EventId = 4531, Level = LogLevel.Warning,
        Message = "Account challenge delivery threw; ChallengeId {ChallengeId}, Purpose {Purpose}.")]
    private static partial void LogDeliveryException(ILogger logger, Guid challengeId, byte purpose);

    [LoggerMessage(EventId = 4532, Level = LogLevel.Warning,
        Message = "Account challenge compensation failed; ChallengeId {ChallengeId}, Purpose {Purpose}.")]
    private static partial void LogCompensationFailure(ILogger logger, Guid challengeId, byte purpose);

    private static Error DeliveryFailed() => new(
        IdentityErrorCodes.AccountChallengeDeliveryFailed,
        "The verification message could not be delivered.",
        ErrorType.BusinessRule);

    private static Error InvalidChallenge() => new(
        IdentityErrorCodes.AccountChallengeInvalid,
        "The account challenge is invalid or has expired.",
        ErrorType.Validation);

    private static Error AttemptsExceeded() => new(
        IdentityErrorCodes.AccountChallengeAttemptsExceeded,
        "The account challenge attempt limit has been exceeded.",
        ErrorType.Validation);
}
