using System.Text;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Cryptography.Configuration;
using Full.NET.Modules.Cryptography.Contracts;
using Full.NET.Modules.Cryptography.Infrastructure;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Cryptography.Features.ManageGmKeys;

/// <summary>受控 SM2 签名服务。</summary>
internal sealed class Sm2SignatureService(
    CryptographyKeyQueryService keyQueryService,
    IOptions<CryptographyOptions> options,
    IClock clock)
{
    /// <summary>对消息执行 SM2 签名。</summary>
    public async Task<Result<Sm2SignResponse>> SignAsync(
        Sm2SignRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return ValidationFailed();
        }

        var keyResult = await keyQueryService.FindByKeyKeyAsync(request.KeyKey, cancellationToken)
            .ConfigureAwait(false);
        if (!keyResult.IsSuccess)
        {
            return Result<Sm2SignResponse>.Failure(keyResult.Error!);
        }

        var key = keyResult.Value!;
        if (!string.Equals(key.Status, CryptographyKeyStatuses.Active, StringComparison.Ordinal))
        {
            return Result<Sm2SignResponse>.Failure(new Error(
                CryptographyErrorCodes.KeyRetired,
                "The cryptography key is retired.",
                ErrorType.Conflict));
        }

        if (!options.Value.Sm2PrivateKeys.TryGetValue(key.KeyKey, out var privateKeyHex)
            || string.IsNullOrWhiteSpace(privateKeyHex))
        {
            return Result<Sm2SignResponse>.Failure(new Error(
                CryptographyErrorCodes.PrivateKeyNotConfigured,
                "The cryptography key private material is not configured.",
                ErrorType.Conflict));
        }

        var userId = ResolveUserId(request.UserId);
        var messageBytes = Encoding.UTF8.GetBytes(request.Message);
        var signatureHex = GmSm2SignatureEngine.SignHex(messageBytes, userId, privateKeyHex.Trim());
        return Result<Sm2SignResponse>.Success(
            new Sm2SignResponse(
                key.Id,
                key.KeyKey,
                key.Algorithm,
                signatureHex,
                key.PublicKeyFingerprint,
                clock.UtcNow));
    }

    private byte[] ResolveUserId(string? userId)
    {
        var value = string.IsNullOrWhiteSpace(userId)
            ? options.Value.DefaultUserId
            : userId.Trim();
        return Encoding.UTF8.GetBytes(value);
    }

    private static Result<Sm2SignResponse> ValidationFailed() =>
        Result<Sm2SignResponse>.Failure(new Error(
            CryptographyErrorCodes.SignValidationFailed,
            "The SM2 sign request failed validation.",
            ErrorType.Validation));
}
