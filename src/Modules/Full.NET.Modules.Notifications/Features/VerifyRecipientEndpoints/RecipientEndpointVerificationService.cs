using System.Security.Cryptography;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Features.ManageRecipientEndpoints;
using Full.NET.Modules.Notifications.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Notifications.Features.VerifyRecipientEndpoints;

/// <summary>
/// 管理收件端点邮件验证码的生成、发送、校验与 pending → verified 升级；
/// 验证码只以哈希形式落库，HTTP 永不返回原值。
/// </summary>
internal sealed class RecipientEndpointVerificationService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    ICurrentTenant currentTenant,
    NotificationRecipientEndpointProtector protector,
    IRecipientEndpointVerificationMailSender mailSender,
    IClock clock,
    IIdGenerator idGenerator,
    IOptions<DatabaseOptions> databaseOptions)
{
    private const int CodeLength = 6;
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SendCooldown = TimeSpan.FromMinutes(1);
    private const int DefaultMaxAttempts = 5;

    /// <summary>向待验证端点发送新的邮件验证码，并使旧挑战失效。</summary>
    /// <param name="userId">当前认证用户标识。</param>
    /// <param name="endpointId">待验证端点标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<Result<SendRecipientEndpointVerificationResponse>> SendCodeAsync(
        Guid userId,
        Guid endpointId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => SendCodeCoreAsync(userId, endpointId, token),
            cancellationToken);

    /// <summary>校验验证码并在成功时自动升级端点为 verified。</summary>
    /// <param name="userId">当前认证用户标识。</param>
    /// <param name="endpointId">待验证端点标识。</param>
    /// <param name="request">用户提交的验证码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<Result<RecipientEndpointResponse>> VerifyCodeAsync(
        Guid userId,
        Guid endpointId,
        VerifyRecipientEndpointCodeRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => VerifyCodeCoreAsync(userId, endpointId, request, token),
            cancellationToken);

    private async Task<Result<SendRecipientEndpointVerificationResponse>> SendCodeCoreAsync(
        Guid userId,
        Guid endpointId,
        CancellationToken cancellationToken)
    {
        var endpoint = await FindOwnedPendingEndpointAsync(userId, endpointId, cancellationToken)
            .ConfigureAwait(false);
        if (endpoint is null)
        {
            return Result<SendRecipientEndpointVerificationResponse>.Failure(EndpointNotFound());
        }

        if (!string.Equals(endpoint.EndpointKindKey, "email", StringComparison.Ordinal))
        {
            return Result<SendRecipientEndpointVerificationResponse>.Failure(ValidationFailed());
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var latestCreated = await queryExecutor.QuerySingleOrDefaultAsync<DateTimeOffset?>(
                SelectLatestCreatedStatement(),
                NotificationPlatformSqlParameters.Create(
                    ("RecipientEndpointId", endpointId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
        if (latestCreated is DateTimeOffset createdAt
            && now - createdAt < SendCooldown)
        {
            return Result<SendRecipientEndpointVerificationResponse>.Failure(new Error(
                NotificationsErrorCodes.RecipientEndpointVerificationSendCooldown,
                "A verification code was sent recently.",
                ErrorType.Conflict));
        }

        var code = GenerateCode();
        var challengeId = idGenerator.NewId();
        var expiresAtUtc = now.Add(CodeLifetime);
        await commandExecutor.ExecuteAsync(
                NotificationRecipientEndpointChallengeSql.InvalidateActiveByEndpoint,
                NotificationPlatformSqlParameters.Create(
                    ("RecipientEndpointId", endpointId),
                    ("ConsumedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
                NotificationRecipientEndpointChallengeSql.Insert,
                NotificationPlatformSqlParameters.Create(
                    ("Id", challengeId),
                    ("RecipientEndpointId", endpointId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId),
                    ("CodeHash", RecipientEndpointVerificationCodeHasher.Hash(challengeId, code)),
                    ("MaxAttempts", DefaultMaxAttempts),
                    ("ExpiresAtUtc", expiresAtUtc),
                    ("CreatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

        var email = protector.Unprotect(endpoint.ProtectedValue);
        var sendResult = await mailSender.SendAsync(
                endpoint.ProviderProfileVersionId,
                email,
                code,
                cancellationToken)
            .ConfigureAwait(false);
        if (!sendResult.IsSuccess)
        {
            await commandExecutor.ExecuteAsync(
                    NotificationRecipientEndpointChallengeSql.MarkConsumed,
                    NotificationPlatformSqlParameters.Create(
                        ("Id", challengeId),
                        ("TenantScopeKey", scope.TenantScopeKey),
                        ("UserId", userId),
                        ("ConsumedAtUtc", clock.UtcNow)),
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<SendRecipientEndpointVerificationResponse>.Failure(sendResult.Error!);
        }

        return Result<SendRecipientEndpointVerificationResponse>.Success(
            new SendRecipientEndpointVerificationResponse(
                expiresAtUtc,
                now.Add(SendCooldown)));
    }

    private async Task<Result<RecipientEndpointResponse>> VerifyCodeCoreAsync(
        Guid userId,
        Guid endpointId,
        VerifyRecipientEndpointCodeRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.Code?.Trim() ?? string.Empty;
        if (code.Length != CodeLength || !code.All(char.IsDigit))
        {
            return Result<RecipientEndpointResponse>.Failure(CodeInvalid());
        }

        var endpoint = await FindOwnedPendingEndpointAsync(userId, endpointId, cancellationToken)
            .ConfigureAwait(false);
        if (endpoint is null)
        {
            return Result<RecipientEndpointResponse>.Failure(EndpointNotFound());
        }

        var scope = NotificationInboxScope.Resolve(currentTenant);
        var now = clock.UtcNow;
        var challenge = await queryExecutor.QuerySingleOrDefaultAsync<NotificationRecipientEndpointChallengeRecord>(
                SelectActiveChallengeStatement(),
                NotificationPlatformSqlParameters.Create(
                    ("RecipientEndpointId", endpointId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId),
                    ("NowUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (challenge is null)
        {
            return Result<RecipientEndpointResponse>.Failure(ChallengeMissing());
        }

        if (challenge.AttemptCount >= challenge.MaxAttempts)
        {
            await MarkEndpointFailedAsync(endpointId, scope.TenantScopeKey, userId, now, cancellationToken)
                .ConfigureAwait(false);
            return Result<RecipientEndpointResponse>.Failure(AttemptsExhausted());
        }

        var expectedHash = RecipientEndpointVerificationCodeHasher.Hash(challenge.Id, code);
        if (!string.Equals(expectedHash, challenge.CodeHash, StringComparison.Ordinal))
        {
            await commandExecutor.ExecuteAsync(
                    NotificationRecipientEndpointChallengeSql.IncrementAttempt,
                    NotificationPlatformSqlParameters.Create(
                        ("Id", challenge.Id),
                        ("TenantScopeKey", scope.TenantScopeKey),
                        ("UserId", userId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (challenge.AttemptCount + 1 >= challenge.MaxAttempts)
            {
                await commandExecutor.ExecuteAsync(
                        NotificationRecipientEndpointChallengeSql.MarkConsumed,
                        NotificationPlatformSqlParameters.Create(
                            ("Id", challenge.Id),
                            ("TenantScopeKey", scope.TenantScopeKey),
                            ("UserId", userId),
                            ("ConsumedAtUtc", now)),
                        cancellationToken)
                    .ConfigureAwait(false);
                await MarkEndpointFailedAsync(endpointId, scope.TenantScopeKey, userId, now, cancellationToken)
                    .ConfigureAwait(false);
                return Result<RecipientEndpointResponse>.Failure(AttemptsExhausted());
            }

            return Result<RecipientEndpointResponse>.Failure(CodeInvalid());
        }

        await commandExecutor.ExecuteAsync(
                NotificationRecipientEndpointChallengeSql.MarkConsumed,
                NotificationPlatformSqlParameters.Create(
                    ("Id", challenge.Id),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId),
                    ("ConsumedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        var updated = await commandExecutor.ExecuteAsync(
                NotificationRecipientEndpointSql.MarkVerified,
                NotificationPlatformSqlParameters.Create(
                    ("Id", endpointId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (updated == 0)
        {
            return Result<RecipientEndpointResponse>.Failure(EndpointNotFound());
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<NotificationRecipientEndpointRecord>(
                NotificationRecipientEndpointSql.FindMaskedById,
                NotificationPlatformSqlParameters.Create(
                    ("Id", endpointId),
                    ("TenantScopeKey", scope.TenantScopeKey)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null
            ? Result<RecipientEndpointResponse>.Failure(EndpointNotFound())
            : Result<RecipientEndpointResponse>.Success(Map(record));
    }

    private async Task<NotificationRecipientEndpointProtectedRecord?> FindOwnedPendingEndpointAsync(
        Guid userId,
        Guid endpointId,
        CancellationToken cancellationToken)
    {
        var scope = NotificationInboxScope.Resolve(currentTenant);
        return await queryExecutor.QuerySingleOrDefaultAsync<NotificationRecipientEndpointProtectedRecord>(
                NotificationRecipientEndpointSql.FindOwnedPendingProtected,
                NotificationPlatformSqlParameters.Create(
                    ("Id", endpointId),
                    ("TenantScopeKey", scope.TenantScopeKey),
                    ("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task MarkEndpointFailedAsync(
        Guid endpointId,
        string tenantScopeKey,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await commandExecutor.ExecuteAsync(
                NotificationRecipientEndpointSql.MarkFailed,
                NotificationPlatformSqlParameters.Create(
                    ("Id", endpointId),
                    ("TenantScopeKey", tenantScopeKey),
                    ("UserId", userId),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);

    private SqlStatement SelectActiveChallengeStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => NotificationRecipientEndpointChallengeSql.FindActiveByEndpoint,
            DatabaseProvider.MySql => NotificationRecipientEndpointChallengeSql.FindActiveByEndpointMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

    private SqlStatement SelectLatestCreatedStatement() =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer =>
                NotificationRecipientEndpointChallengeSql.FindLatestCreatedAtByEndpoint,
            DatabaseProvider.MySql =>
                NotificationRecipientEndpointChallengeSql.FindLatestCreatedAtByEndpointMySql,
            _ => throw new InvalidOperationException("The configured database provider is not supported."),
        };

    private static string GenerateCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    private static RecipientEndpointResponse Map(NotificationRecipientEndpointRecord record) =>
        new(
            record.Id,
            record.UserId,
            record.ProviderProfileVersionId,
            record.EndpointKindKey,
            record.MaskedValue,
            record.VerificationStatusKey,
            record.CreatedAtUtc);

    private static Error EndpointNotFound() => new(
        NotificationsErrorCodes.RecipientEndpointNotFound,
        "The recipient endpoint was not found in the current scope.",
        ErrorType.NotFound);

    private static Error ValidationFailed() => new(
        NotificationsErrorCodes.RecipientEndpointValidationFailed,
        "The recipient endpoint value or kind is invalid.",
        ErrorType.Validation);

    private static Error ChallengeMissing() => new(
        NotificationsErrorCodes.RecipientEndpointVerificationChallengeMissing,
        "No active verification challenge was found.",
        ErrorType.Validation);

    private static Error CodeInvalid() => new(
        NotificationsErrorCodes.RecipientEndpointVerificationCodeInvalid,
        "The verification code is invalid.",
        ErrorType.Validation);

    private static Error AttemptsExhausted() => new(
        NotificationsErrorCodes.RecipientEndpointVerificationAttemptsExhausted,
        "The verification attempts were exhausted.",
        ErrorType.Conflict);
}
