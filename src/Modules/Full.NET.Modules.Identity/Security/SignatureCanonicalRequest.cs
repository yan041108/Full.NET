using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Full.NET.Modules.Identity.Security;

/// <summary>
/// 将 HTTP 请求规范化为签名输入字符串；规则与
/// <c>2026-07-30-request-signature-authentication-design.md</c> 一致。
/// </summary>
internal static class SignatureCanonicalRequest
{
    public static string BuildCanonicalString(
        string method,
        string canonicalPath,
        string canonicalQuery,
        string contentHash,
        string accessKeyId,
        string timestamp,
        string nonce) =>
        string.Join(
            '\n',
            method,
            canonicalPath,
            canonicalQuery,
            contentHash,
            accessKeyId,
            timestamp,
            nonce);

    public static string NormalizeMethod(string method) =>
        method.Trim().ToUpperInvariant();

    public static bool TryParseUnixTimestamp(
        string value,
        out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (!long.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var timestampSeconds)
            || timestampSeconds < DateTimeOffset.MinValue.ToUnixTimeSeconds()
            || timestampSeconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return false;
        }

        timestamp = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);
        return true;
    }

    public static string NormalizePath(PathString pathBase, PathString path)
    {
        var combined = (pathBase.Value ?? string.Empty) + (path.Value ?? string.Empty);
        if (string.IsNullOrEmpty(combined))
        {
            return "/";
        }

        if (combined.Length > 1 && combined.EndsWith("/", StringComparison.Ordinal))
        {
            throw new SignatureCanonicalizationException(
                IdentitySignatureErrorCodes.InvalidEncoding,
                "Trailing slash is not allowed except for the root path.");
        }

        return combined;
    }

    public static string BuildCanonicalQuery(QueryString queryString)
    {
        var raw = queryString.Value;
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var query = raw.StartsWith('?') ? raw[1..] : raw;
        if (query.Length == 0)
        {
            return string.Empty;
        }

        var pairs = new List<(string Name, string Value)>();
        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            string name;
            string value;
            if (separatorIndex < 0)
            {
                name = segment;
                value = string.Empty;
            }
            else
            {
                name = segment[..separatorIndex];
                value = segment[(separatorIndex + 1)..];
            }

            if (!IsCanonicalQueryComponent(name) || !IsCanonicalQueryComponent(value))
            {
                throw new SignatureCanonicalizationException(
                    IdentitySignatureErrorCodes.InvalidEncoding,
                    "Query parameters must use canonical percent encoding.");
            }

            pairs.Add((Uri.UnescapeDataString(name), Uri.UnescapeDataString(value)));
        }

        return string.Join(
            '&',
            pairs
                .Select(pair => (
                    EncodedName: Uri.EscapeDataString(pair.Name),
                    EncodedValue: Uri.EscapeDataString(pair.Value)))
                .OrderBy(pair => pair.EncodedName, StringComparer.Ordinal)
                .ThenBy(pair => pair.EncodedValue, StringComparer.Ordinal)
                .Select(pair => $"{pair.EncodedName}={pair.EncodedValue}"));
    }

    public static string ComputeContentHash(ReadOnlySpan<byte> body) =>
        Convert.ToHexStringLower(SHA256.HashData(body));

    public static async Task<byte[]> ReadBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        using var memory = new MemoryStream();
        await request.Body.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        var bytes = memory.ToArray();
        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        return bytes;
    }

    public static string ComputeSignature(string canonicalString, byte[] signingKeyBytes)
    {
        var canonicalBytes = Encoding.UTF8.GetBytes(canonicalString);
        var hash = HMACSHA256.HashData(signingKeyBytes, canonicalBytes);
        return Convert.ToHexStringLower(hash);
    }

    public static byte[] ParseSigningKeyBytes(string keyHashHex) =>
        Convert.FromHexString(keyHashHex);

    public static bool FixedTimeEqualsSignatures(string expected, string actual)
    {
        if (expected.Length != actual.Length)
        {
            return false;
        }

        try
        {
            var expectedBytes = Convert.FromHexString(expected);
            var actualBytes = Convert.FromHexString(actual);
            return expectedBytes.Length == actualBytes.Length
                && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string ComputeNonceDigest(string nonce) =>
        TokenHash.Compute(nonce);

    private static bool IsCanonicalQueryComponent(string value)
    {
        if (value.Length == 0)
        {
            return true;
        }

        if (value.Contains('+', StringComparison.Ordinal)
            || value.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return false;
        }

        return string.Equals(
            Uri.EscapeDataString(decoded),
            value,
            StringComparison.Ordinal);
    }
}

/// <summary>签名规范化失败时携带稳定错误码。</summary>
internal sealed class SignatureCanonicalizationException(
    string errorCode,
    string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}
