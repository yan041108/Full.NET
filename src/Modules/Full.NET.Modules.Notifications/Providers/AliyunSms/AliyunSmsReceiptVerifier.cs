using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Extensions.Configuration;

namespace Full.NET.Modules.Notifications.Providers.AliyunSms;

/// <summary>
/// 验签阿里云短信状态报告 HTTP 回调；只接受闭合 JSON 数组，正文不入库。
/// </summary>
internal sealed class AliyunSmsReceiptVerifier(IConfiguration configuration) : INotificationReceiptVerifier
{
    public const string SignatureHeaderName = "X-Aliyun-Sms-Receipt-Signature";
    private const string EnvScheme = "env://";
    private const string ReceiptSecretReferenceKey =
        "Notifications:Providers:AliyunSms:ReceiptSecretReference";

    public string ProviderTypeKey => AliyunSmsNotificationProviderAdapter.ProviderTypeKeyValue;

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
            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() != 1)
            {
                return Invalid();
            }

            var report = document.RootElement[0];
            var providerMessageId = report.TryGetProperty("biz_id", out var bizIdElement)
                ? bizIdElement.GetString()
                : null;
            var phone = report.TryGetProperty("phone_number", out var phoneElement)
                ? phoneElement.GetString()
                : null;
            var reportTime = report.TryGetProperty("report_time", out var reportTimeElement)
                ? reportTimeElement.GetString()
                : null;
            var success = report.TryGetProperty("success", out var successElement)
                && successElement.ValueKind == JsonValueKind.True;
            var externalStatusKey = report.TryGetProperty("err_code", out var errCodeElement)
                ? errCodeElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(providerMessageId)
                || string.IsNullOrWhiteSpace(phone)
                || string.IsNullOrWhiteSpace(reportTime)
                || string.IsNullOrWhiteSpace(externalStatusKey))
            {
                return Invalid();
            }

            var mappedStatusKey = success ? "delivered" : "failed";
            var idempotency = $"{providerMessageId}:{phone}:{reportTime}";
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
