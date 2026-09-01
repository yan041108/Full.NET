namespace Full.NET.Hosting.RateLimiting;

/// <summary>
/// 维护端点级限流策略与稳定错误码的映射，供统一 429 响应使用。
/// </summary>
public sealed class RateLimitPolicyErrorCodes
{
    private readonly Dictionary<string, string> _policyCodes = new(StringComparer.Ordinal);

    /// <summary>
    /// 将限流策略名称映射到稳定错误码；同一策略不能映射到不同错误码。
    /// </summary>
    /// <param name="policyName">RateLimiter 策略名称，需与 <c>RequireRateLimiting(policyName)</c> 一致。</param>
    /// <param name="errorCode">429 响应对外暴露的稳定错误码。</param>
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

    /// <summary>
    /// 解析策略名称对应的稳定错误码；未命中时返回传入的默认值。
    /// </summary>
    /// <param name="policyName">触发限流的策略名称；可为空。</param>
    /// <param name="fallbackErrorCode">未注册时使用的兜底稳定错误码。</param>
    /// <returns>注册的策略错误码或兜底错误码。</returns>
    public string Resolve(string? policyName, string fallbackErrorCode)
    {
        return policyName is not null
            && _policyCodes.TryGetValue(policyName, out var errorCode)
            ? errorCode
            : fallbackErrorCode;
    }
}
