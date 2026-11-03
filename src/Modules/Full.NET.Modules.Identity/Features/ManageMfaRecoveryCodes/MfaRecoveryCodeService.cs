using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageMfaRecoveryCodes.Persistence;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Identity.Domain;

/// <summary>Identity 模块应用服务；编排事务、权限与持久化。</summary>
namespace Full.NET.Modules.Identity.Features.ManageMfaRecoveryCodes;

internal sealed class MfaRecoveryCodeService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock,
    IIdGenerator idGenerator,
    IRandomTokenGenerator randomTokenGenerator,
    AuthenticationSecurityEventWriter audit)
{
    private const int CodeCount = 10;

    public Task<Result<RegenerateMfaRecoveryCodesResponse>> RegenerateAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => RegenerateCoreAsync(userId, token),
            cancellationToken);

    public Task<Result<ConsumeMfaRecoveryCodeResponse>> ConsumeAsync(
        Guid userId,
        string recoveryCode,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ConsumeCoreAsync(userId, recoveryCode, token),
            cancellationToken);

    private async Task<Result<RegenerateMfaRecoveryCodesResponse>> RegenerateCoreAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await commandExecutor.ExecuteAsync(
                MfaRecoveryCodeSql.DeleteByUserId,
                Identity.Persistence.IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;
        var plaintextCodes = new List<string>(CodeCount);
        for (var index = 0; index < CodeCount; index += 1)
        {
            var code = randomTokenGenerator.Generate(16);
            plaintextCodes.Add(code);
            var affected = await commandExecutor.ExecuteAsync(
                    MfaRecoveryCodeSql.Insert,
                    Identity.Persistence.IdentitySqlParameters.Create(
                        ("Id", idGenerator.NewId()),
                        ("UserId", userId),
                        ("CodeHash", HashCode(code)),
                        ("CreatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affected != 1) throw new InvalidOperationException("Recovery code insert did not persist exactly one row.");
        }

        await audit.WriteAsync(userId, userId, AuthenticationEventCatalog.RecoveryCodesRegenerated,
            "identity.mfa_recovery_codes_regenerated", true, "recovery_code", cancellationToken).ConfigureAwait(false);

        return Result<RegenerateMfaRecoveryCodesResponse>.Success(
            new RegenerateMfaRecoveryCodesResponse(plaintextCodes));
    }

    private async Task<Result<ConsumeMfaRecoveryCodeResponse>> ConsumeCoreAsync(
        Guid userId,
        string recoveryCode,
        CancellationToken cancellationToken)
    {
        var normalized = recoveryCode?.Trim() ?? string.Empty;
        if (normalized.Length is < 8 or > 64)
        {
            await WriteConsumptionAsync(userId, false, cancellationToken).ConfigureAwait(false);
            return Result<ConsumeMfaRecoveryCodeResponse>.Failure(new Error(
                IdentityErrorCodes.MfaRecoveryCodeInvalid,
                "Recovery code is invalid.",
                ErrorType.Validation));
        }

        var rows = await queryExecutor.QueryAsync<MfaRecoveryCodeRecord>(
                MfaRecoveryCodeSql.ListActiveByUserId,
                Identity.Persistence.IdentitySqlParameters.Create(("UserId", userId)),
                cancellationToken)
            .ConfigureAwait(false);
        var hash = HashCode(normalized);
        var match = rows.FirstOrDefault(row => CryptographicOperations.FixedTimeEquals(row.CodeHash, hash));
        if (match is null)
        {
            await WriteConsumptionAsync(userId, false, cancellationToken).ConfigureAwait(false);
            return Result<ConsumeMfaRecoveryCodeResponse>.Failure(new Error(
                IdentityErrorCodes.MfaRecoveryCodeInvalid,
                "Recovery code is invalid.",
                ErrorType.Validation));
        }

        var affected = await commandExecutor.ExecuteAsync(
                MfaRecoveryCodeSql.Consume,
                Identity.Persistence.IdentitySqlParameters.Create(
                    ("Id", match.Id),
                    ("ConsumedAtUtc", clock.UtcNow),
                    ("Version", match.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            await WriteConsumptionAsync(userId, false, cancellationToken).ConfigureAwait(false);
            return Result<ConsumeMfaRecoveryCodeResponse>.Failure(new Error(
                IdentityErrorCodes.MfaRecoveryCodeInvalid,
                "Recovery code is invalid.",
                ErrorType.Conflict));
        }

        await WriteConsumptionAsync(userId, true, cancellationToken).ConfigureAwait(false);
        return Result<ConsumeMfaRecoveryCodeResponse>.Success(new ConsumeMfaRecoveryCodeResponse(true));
    }

    private Task WriteConsumptionAsync(Guid userId, bool succeeded, CancellationToken token) =>
        audit.WriteAsync(userId, succeeded ? userId : null, AuthenticationEventCatalog.RecoveryCodeConsumed,
            succeeded ? "identity.mfa_recovery_code_consumed" : IdentityErrorCodes.MfaRecoveryCodeInvalid,
            succeeded, "recovery_code", token);

    private static byte[] HashCode(string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim()));
}

internal sealed record MfaRecoveryCodeRecord(Guid Id, byte[] CodeHash, int Version);
