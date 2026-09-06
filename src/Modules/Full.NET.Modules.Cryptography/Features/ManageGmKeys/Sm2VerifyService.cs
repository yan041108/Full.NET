using System.Text;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Cryptography.Configuration;
using Full.NET.Modules.Cryptography.Contracts;
using Full.NET.Modules.Cryptography.Infrastructure;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Cryptography.Features.ManageGmKeys;

/// <summary>受控 SM2 验签服务。</summary>
internal sealed class Sm2VerifyService(
    CryptographyKeyQueryService keyQueryService,
    IOptions<CryptographyOptions> options)
{
    /// <summary>验证 SM2 签名。</summary>
    public async Task<Result<Sm2VerifyResponse>> VerifyAsync(
        Sm2VerifyRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message)
            || string.IsNullOrWhiteSpace(request.SignatureHex))
        {
            return ValidationFailed();
        }

        var keyResult = await keyQueryService.FindByKeyKeyAsync(request.KeyKey, cancellationToken)
            .ConfigureAwait(false);
        if (!keyResult.IsSuccess)
        {
            return Result<Sm2VerifyResponse>.Failure(keyResult.Error!);
        }

        var key = keyResult.Value!;
        var userId = ResolveUserId(request.UserId);
        var messageBytes = Encoding.UTF8.GetBytes(request.Message);
        var isValid = GmSm2SignatureEngine.VerifyHex(
            messageBytes,
            userId,
            request.SignatureHex.Trim(),
            key.PublicKeyHex);
        if (!isValid)
        {
            return Result<Sm2VerifyResponse>.Failure(new Error(
                CryptographyErrorCodes.SignatureInvalid,
                "The SM2 signature is invalid.",
                ErrorType.Validation));
        }

        return Result<Sm2VerifyResponse>.Success(
            new Sm2VerifyResponse(
                key.Id,
                key.KeyKey,
                true,
                key.PublicKeyFingerprint));
    }

    private byte[] ResolveUserId(string? userId)
    {
        var value = string.IsNullOrWhiteSpace(userId)
            ? options.Value.DefaultUserId
            : userId.Trim();
        return Encoding.UTF8.GetBytes(value);
    }

    private static Result<Sm2VerifyResponse> ValidationFailed() =>
        Result<Sm2VerifyResponse>.Failure(new Error(
            CryptographyErrorCodes.VerifyValidationFailed,
            "The SM2 verify request failed validation.",
            ErrorType.Validation));
}
