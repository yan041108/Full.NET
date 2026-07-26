namespace Full.NET.Abstractions.Results;

/// <summary>
/// 定义不归属于具体业务模块的稳定错误码。
/// </summary>
public static class CommonErrorCodes
{
    /// <summary>
    /// 通用错误码前缀。
    /// </summary>
    public const string Prefix = "common.";

    /// <summary>
    /// 授权错误码前缀。
    /// </summary>
    public const string AuthorizationPrefix = "authorization.";

    /// <summary>
    /// 未预期的服务端错误。
    /// </summary>
    public const string Unexpected = "common.unexpected";

    /// <summary>
    /// 当前身份缺少所需权限。
    /// </summary>
    public const string PermissionDenied = "authorization.permission_denied";

    /// <summary>
    /// Host API 全局限流触发。
    /// </summary>
    public const string RateLimited = "hosting.rate_limit.exceeded";

    /// <summary>
    /// 获取当前目录中的全部稳定错误码。
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly([Unexpected, PermissionDenied, RateLimited]);
}
