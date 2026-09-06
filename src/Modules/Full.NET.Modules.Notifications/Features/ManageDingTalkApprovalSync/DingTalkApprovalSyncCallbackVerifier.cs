using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Notifications.Contracts;
using Microsoft.Extensions.Configuration;

namespace Full.NET.Modules.Notifications.Features.ManageDingTalkApprovalSync;

/// <summary>验签钉钉审批镜像回调；只更新本地镜像记录，不回写 Workflow 决策。</summary>
internal sealed class DingTalkApprovalSyncCallbackVerifier(IConfiguration configuration)
{
    public const string SignatureHeaderName = "X-DingTalk-Approval-Sync-Signature";
    private const string EnvScheme = "env://";
    private const string CallbackSecretReferenceKey =
        "Notifications:Providers:DingTalk:Workflow:CallbackSecretReference";

    public Result<VerifiedDingTalkApprovalSyncCallback> Verify(
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue(SignatureHeaderName, out var signature)
            || string.IsNullOrWhiteSpace(signature))
        {
            return Invalid();
        }

        var secret = ResolveSecret(configuration[CallbackSecretReferenceKey]);
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

            var processInstanceId = document.RootElement.TryGetProperty("processInstanceId", out var idElement)
                ? idElement.GetString()
                : null;
            var status = document.RootElement.TryGetProperty("status", out var statusElement)
                ? statusElement.GetString()
                : null;
            var result = document.RootElement.TryGetProperty("result", out var resultElement)
                ? resultElement.GetString()
                : null;
            var eventTime = document.RootElement.TryGetProperty("eventTime", out var eventTimeElement)
                ? eventTimeElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(processInstanceId)
                || string.IsNullOrWhiteSpace(status)
                || string.IsNullOrWhiteSpace(eventTime))
            {
                return Invalid();
            }

            return Result<VerifiedDingTalkApprovalSyncCallback>.Success(
                new VerifiedDingTalkApprovalSyncCallback(
                    processInstanceId,
                    status,
                    result,
                    eventTime,
                    Convert.ToHexString(SHA256.HashData(body.Span)).ToLowerInvariant()));
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

    private static string? ResolveSecret(string? secretReference)
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

    private static Result<VerifiedDingTalkApprovalSyncCallback> Invalid() =>
        Result<VerifiedDingTalkApprovalSyncCallback>.Failure(new Error(
            NotificationsErrorCodes.ReceiptInvalid,
            "The approval sync callback signature or payload is invalid.",
            ErrorType.Validation));
}

/// <summary>验签后的钉钉审批镜像回调载荷。</summary>
internal sealed record VerifiedDingTalkApprovalSyncCallback(
    string ProcessInstanceId,
    string ExternalStatusKey,
    string? ExternalResultKey,
    string EventTime,
    string PayloadDigest);
