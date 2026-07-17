namespace Full.NET.Abstractions.Results;

/// <summary>
/// 定义字段验证使用的稳定错误码。
/// </summary>
public static class ValidationErrorCodes
{
    /// <summary>
    /// 验证错误码前缀。
    /// </summary>
    public const string Prefix = "validation.";

    /// <summary>
    /// 一个或多个字段验证失败。
    /// </summary>
    public const string Failed = "validation.failed";

    /// <summary>
    /// 必填值为空。
    /// </summary>
    public const string Required = "validation.required";

    /// <summary>
    /// 文本超过最大长度。
    /// </summary>
    public const string MaximumLength = "validation.maximum_length";

    /// <summary>
    /// 值不符合约定格式。
    /// </summary>
    public const string InvalidFormat = "validation.invalid_format";

    /// <summary>
    /// 获取当前目录中的全部稳定错误码。
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly([Failed, Required, MaximumLength, InvalidFormat]);
}
