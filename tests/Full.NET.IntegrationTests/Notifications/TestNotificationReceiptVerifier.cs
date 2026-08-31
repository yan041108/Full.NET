using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Notifications.Contracts;
using Full.NET.Modules.Notifications.Providers;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>
/// 测试回执 HMAC 验签器；密钥只用于测试程序集，不得写入日志或错误正文。
/// </summary>
internal sealed class TestNotificationReceiptVerifier : INotificationReceiptVerifier
{
    public const string SignatureHeaderName = "X-FullNet-Test-Receipt-Signature";
    public const string HmacKey = "test-receipt-hmac-key";

    public string ProviderTypeKey => TestNotificationProvider.ProviderTypeKeyValue;

    public Result<VerifiedNotificationReceipt> Verify(
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue(SignatureHeaderName, out var signature)
            || string.IsNullOrWhiteSpace(signature))
        {
            return Invalid();
        }

        try
        {
            var expected = Sign(body.Span);
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
            var root = document.RootElement;
            var idempotency = root.GetProperty("receiptIdempotencyKey").GetString();
            var mapped = root.GetProperty("mappedStatusKey").GetString();
            var external = root.GetProperty("externalStatusKey").GetString();
            if (string.IsNullOrWhiteSpace(idempotency)
                || string.IsNullOrWhiteSpace(mapped)
                || string.IsNullOrWhiteSpace(external))
            {
                return Invalid();
            }

            var providerMessageId = root.TryGetProperty("providerMessageId", out var message)
                ? message.GetString()
                : null;
            var digest = Convert.ToHexString(SHA256.HashData(body.Span)).ToLowerInvariant();
            return Result<VerifiedNotificationReceipt>.Success(
                new VerifiedNotificationReceipt(
                    idempotency,
                    providerMessageId,
                    external,
                    mapped,
                    digest));
        }
        catch (Exception)
        {
            return Invalid();
        }
    }

    public static string Sign(ReadOnlySpan<byte> body)
    {
        var key = Encoding.UTF8.GetBytes(HmacKey);
        return Convert.ToHexString(HMACSHA256.HashData(key, body)).ToLowerInvariant();
    }

    private static Result<VerifiedNotificationReceipt> Invalid() =>
        Result<VerifiedNotificationReceipt>.Failure(new Error(
            NotificationsErrorCodes.ReceiptInvalid,
            "The receipt signature or payload is invalid.",
            ErrorType.Validation));
}

/// <summary>第二个测试 Provider 的回执验签器，用于证明外部消息号只能在 Provider 内匹配。</summary>
internal sealed class AlternateTestNotificationReceiptVerifier : INotificationReceiptVerifier
{
    public const string ProviderTypeKeyValue = "test.notification.alternate";

    private readonly TestNotificationReceiptVerifier _inner = new();

    public string ProviderTypeKey => ProviderTypeKeyValue;

    public Result<VerifiedNotificationReceipt> Verify(
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, string> headers) =>
        _inner.Verify(body, headers);
}
