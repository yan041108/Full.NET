using System.Text.RegularExpressions;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>MCP 远端连接字段校验。</summary>
internal static partial class AiMcpRemoteConnectionValidator
{
    internal const int MaxConnectionKeyLength = 64;
    internal const int MaxDisplayNameLength = 128;
    internal const int MaxEndpointLength = 512;

    public static string? ValidateCreate(
        string connectionKey,
        string displayName,
        string endpointUrl,
        string serviceToken)
    {
        if (!IsValidConnectionKey(connectionKey))
            return "Connection key must start with a letter and use lowercase letters, digits, or underscores (max 64).";

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > MaxDisplayNameLength)
            return "Display name is required and must not exceed 128 characters.";

        if (!IsSafeEndpoint(endpointUrl))
            return "Endpoint URL must be an absolute http or https URL without credentials.";

        if (string.IsNullOrWhiteSpace(serviceToken) || serviceToken.Length > 4096)
            return "Service token is required and must not exceed 4096 characters.";

        return null;
    }

    public static string? ValidateUpdate(string displayName, string endpointUrl)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > MaxDisplayNameLength)
            return "Display name is required and must not exceed 128 characters.";

        if (!IsSafeEndpoint(endpointUrl))
            return "Endpoint URL must be an absolute http or https URL without credentials.";

        return null;
    }

    public static bool IsValidConnectionKey(string? connectionKey) =>
        !string.IsNullOrWhiteSpace(connectionKey)
        && connectionKey.Length <= MaxConnectionKeyLength
        && ConnectionKeyPattern().IsMatch(connectionKey);

    private static bool IsSafeEndpoint(string endpointUrl)
    {
        if (!Uri.TryCreate(endpointUrl.Trim(), UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme is not ("http" or "https"))
            return false;
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return false;
        return uri.Host.Length > 0;
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,63}$")]
    private static partial Regex ConnectionKeyPattern();
}
