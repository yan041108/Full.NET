namespace Full.NET.Abstractions.OpenApi;

/// <summary>
/// 将字符串属性允许的稳定机器码写入运行时 OpenAPI enum，避免客户端另建枚举真相。
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FullNetOpenApiStringEnumAttribute : Attribute
{
    /// <summary>
    /// 创建稳定字符串枚举声明；值必须非空且不得重复。
    /// </summary>
    public FullNetOpenApiStringEnumAttribute(params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0
            || values.Any(string.IsNullOrWhiteSpace)
            || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new ArgumentException("OpenAPI 字符串枚举必须包含非空且不重复的稳定机器码。", nameof(values));
        }

        Values = Array.AsReadOnly((string[])values.Clone());
    }

    /// <summary>按声明顺序公开稳定机器码。</summary>
    public IReadOnlyList<string> Values { get; }
}
