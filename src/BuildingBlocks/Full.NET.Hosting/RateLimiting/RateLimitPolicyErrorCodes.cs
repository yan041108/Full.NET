namespace Full.NET.Hosting.RateLimiting;

/// <summary>
/// 维护端点级限流策略与稳定错误码的映射，供统一 429 响应使用。
/// </summary>
public sealed class RateLimitPolicyErrorCodes
{
    private readonly Dictionary<string, string> _policyCodes = new(StringComparer.Ordinal);

    public void MapPolicy(string policyName, string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        if (_policyCodes.TryGetValue(policyName, out var registeredErrorCode))
        {
            if (!string.Equals(registeredErrorCode, errorCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Rate limit policy '{policyName}' is already mapped to "
                    + $"'{registeredErrorCode}' and cannot be remapped to '{errorCode}'.");
            }

            return;
        }

        _policyCodes.Add(policyName, errorCode);
    }

    public string Resolve(string? policyName, string fallbackErrorCode)
    {
        return policyName is not null
            && _policyCodes.TryGetValue(policyName, out var errorCode)
            ? errorCode
            : fallbackErrorCode;
    }
}
