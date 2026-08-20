using System.Text.RegularExpressions;
using Full.NET.Modules.Jobs.Contracts;

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>HTTP 任务 Args 校验；管理端写入与 Worker 执行共用规则。</summary>
internal static partial class HttpJobArgsValidator
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
        "HEAD",
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
    };

    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "Api-Key",
    };

    private static readonly int[] DefaultSuccessStatusCodes = [200, 201, 202, 204];

    public static bool TryValidate(
        HttpJobArgs args,
        bool rejectSensitivePlainHeaders,
        out string? errorMessage)
    {
        errorMessage = null;
        if (args is null)
        {
            errorMessage = "HTTP job args are required.";
            return false;
        }

        var url = args.Url?.Trim() ?? string.Empty;
        if (url.Length is < 1 or > 2048
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            errorMessage = "HTTP job URL is invalid.";
            return false;
        }

        var method = args.Method?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!AllowedMethods.Contains(method))
        {
            errorMessage = "HTTP job method is not allowed.";
            return false;
        }

        if (args.Headers is not null)
        {
            if (args.Headers.Count > 32)
            {
                errorMessage = "HTTP job headers exceed the maximum count.";
                return false;
            }

            foreach (var (name, value) in args.Headers)
            {
                if (!IsValidHeaderToken(name))
                {
                    errorMessage = "HTTP job header name is invalid.";
                    return false;
                }

                if (value is null || value.Length > 1024)
                {
                    errorMessage = "HTTP job header value is invalid.";
                    return false;
                }

                if (rejectSensitivePlainHeaders && SensitiveHeaderNames.Contains(name))
                {
                    errorMessage = "Sensitive headers must use secretHeaders.";
                    return false;
                }
            }
        }

        if (args.SecretHeaders is not null)
        {
            if (args.SecretHeaders.Count > 16)
            {
                errorMessage = "HTTP job secret headers exceed the maximum count.";
                return false;
            }

            foreach (var (name, reference) in args.SecretHeaders)
            {
                if (!IsValidHeaderToken(name))
                {
                    errorMessage = "HTTP job secret header name is invalid.";
                    return false;
                }

                var configKey = reference?.ConfigKey?.Trim().ToLowerInvariant() ?? string.Empty;
                if (!ConfigKeyPattern().IsMatch(configKey))
                {
                    errorMessage = "HTTP job secret header configKey is invalid.";
                    return false;
                }
            }
        }

        if (args.TimeoutSeconds is < 1 or > 120)
        {
            errorMessage = "HTTP job timeoutSeconds is out of range.";
            return false;
        }

        var successCodes = args.SuccessStatusCodes ?? DefaultSuccessStatusCodes;
        if (successCodes.Count is < 1 or > 16
            || successCodes.Any(code => code is < 100 or > 599))
        {
            errorMessage = "HTTP job successStatusCodes are invalid.";
            return false;
        }

        return true;
    }

    public static bool IsSensitiveHeaderName(string headerName) =>
        SensitiveHeaderNames.Contains(headerName);

    private static bool IsValidHeaderToken(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && HeaderTokenPattern().IsMatch(name);

    [GeneratedRegex(@"^[a-z][a-z0-9._-]{1,126}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex ConfigKeyPattern();

    [GeneratedRegex(@"^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderTokenPattern();
}
