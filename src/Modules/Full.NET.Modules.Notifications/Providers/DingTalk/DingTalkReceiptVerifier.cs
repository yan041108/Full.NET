using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Extensions.Configuration;

namespace Full.NET.Modules.Notifications.Providers.DingTalk;

/// <summary>验签钉钉互动卡片失败/送达回执 HTTP 回调；只接受闭合 JSON 对象，正文不入库。</summary>
internal sealed class DingTalkReceiptVerifier(IConfiguration configuration) : INotificationReceiptVerifier
{
    public const string SignatureHeaderName = "X-DingTalk-Receipt-Signature";
    private const string EnvScheme = "env://";
    private const string ReceiptSecretReferenceKey =
        "Notifications:Providers:DingTalk:ReceiptSecretReference";

    public string ProviderTypeKey => DingTalkNotificationProviderAdapter.ProviderTypeKeyValue;

    public Result<VerifiedNotificationReceipt> Verify(
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue(SignatureHeaderName, out var signature)
            || string.IsNullOrWhiteSpace(signature))
        {
            return Invalid();
        }

        var secret = ResolveReceiptSecret(configuration[ReceiptSecretReferenceKey]);
        if (string.IsNullOrEmpty(secret))
        {
            return Invalid();
        }

        try
        {
            var expected = Sign(body.Span, secret);
            var provided = Convert.FromHexString(signature.Trim());
            var expectedBytes = Convert.FromHexString(expected);
            if (provided.Length != expectedBytes.Length
                || !CryptographicOperations.FixedTimeEquals(provided, expectedBytes))
            {
                return Invalid();
            }
        }
        catch (FormatException)
        {
            return Invalid();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Invalid();
            }

            var providerMessageId = document.RootElement.TryGetProperty("outTrackId", out var outTrackElement)
                ? outTrackElement.GetString()
                : null;
            var userId = document.RootElement.TryGetProperty("userId", out var userIdElement)
                ? userIdElement.GetString()
                : null;
            var eventTime = document.RootElement.TryGetProperty("eventTime", out var eventTimeElement)
                ? eventTimeElement.GetString()
                : null;
            var status = document.RootElement.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : null;
            var externalStatusKey = document.RootElement.TryGetProperty("errorCode", out var errorCodeElement)
                ? errorCodeElement.GetString()
                : status;
            if (string.IsNullOrWhiteSpace(providerMessageId)
                || string.IsNullOrWhiteSpace(userId)
                || string.IsNullOrWhiteSpace(eventTime)
                || string.IsNullOrWhiteSpace(status)
                || string.IsNullOrWhiteSpace(externalStatusKey))
            {
                return Invalid();
            }

            var mappedStatusKey = string.Equals(status, "delivered", StringComparison.Ordinal)
                ? "delivered"
                : string.Equals(status, "failed", StringComparison.Ordinal)
                    ? "failed"
                    : null;
            if (mappedStatusKey is null)
            {
                return Invalid();
            }

            var idempotency = $"{providerMessageId}:{userId}:{eventTime}";
            var digest = Convert.ToHexString(SHA256.HashData(body.Span)).ToLowerInvariant();
            return Result<VerifiedNotificationReceipt>.Success(
                new VerifiedNotificationReceipt(
                    idempotency,
                    providerMessageId,
                    externalStatusKey,
                    mappedStatusKey,
                    digest));
        }
        catch (Exception)
        {
            return Invalid();
        }
    }

    internal static string Sign(ReadOnlySpan<byte> body, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        return Convert.ToHexString(HMACSHA256.HashData(key, body)).ToLowerInvariant();
    }

    private static string? ResolveReceiptSecret(string? secretReference)
    {
        if (secretReference is null
            || !secretReference.StartsWith(EnvScheme, StringComparison.Ordinal))
        {
            return null;
        }

        var variableName = secretReference[EnvScheme.Length..];
        if (variableName.Length is < 1 or > 128
            || !(char.IsAsciiLetter(variableName[0]) || variableName[0] == '_')
            || !variableName.All(character =>
                char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            return null;
        }

        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static Result<VerifiedNotificationReceipt> Invalid() =>
        Result<VerifiedNotificationReceipt>.Failure(new Error(
            NotificationsErrorCodes.ReceiptInvalid,
            "The receipt signature or payload is invalid.",
            ErrorType.Validation));
}
