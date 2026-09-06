namespace Full.NET.Modules.Reporting.Contracts;

/// <summary>报表参数数据类型键。</summary>
public static class ReportingParameterDataTypeKeys
{
    /// <summary>字符串参数。</summary>
    public const string String = "string";

    /// <summary>整数参数。</summary>
    public const string Integer = "integer";

    /// <summary>布尔参数。</summary>
    public const string Boolean = "boolean";

    /// <summary>日期参数（ISO 8601 日期）。</summary>
    public const string Date = "date";
}

/// <summary>静态 Query Port 参数定义；SQL 仅存在于服务端审查过的实现中。</summary>
/// <param name="ParameterKey">稳定参数键。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="DataTypeKey">数据类型键。</param>
/// <param name="IsRequired">是否必填。</param>
/// <param name="DefaultValue">默认值文本。</param>
/// <param name="Minimum">整数最小值；非整数参数为 <see langword="null"/>。</param>
/// <param name="Maximum">整数最大值；非整数参数为 <see langword="null"/>。</param>
public sealed record ReportingQueryPortParameterDefinition(
    string ParameterKey,
    string DisplayName,
    string DataTypeKey,
    bool IsRequired,
    string? DefaultValue,
    int? Minimum,
    int? Maximum);

/// <summary>静态 Query Port 目录项；禁止客户端提交任意 SQL。</summary>
/// <param name="QueryPortKey">稳定 Query Port 键。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="Description">用途说明。</param>
/// <param name="SupportedProviderKeys">支持的数据源提供程序键。</param>
/// <param name="Parameters">受控参数定义。</param>
public sealed record ReportingQueryPortDefinition(
    string QueryPortKey,
    string DisplayName,
    string Description,
    IReadOnlyList<string> SupportedProviderKeys,
    IReadOnlyList<ReportingQueryPortParameterDefinition> Parameters);

/// <summary>报表定义参数 Schema 项；覆盖 Query Port 参数的展示与默认值。</summary>
/// <param name="ParameterKey">参数键，必须属于所选 Query Port。</param>
/// <param name="DisplayName">显示名称。</param>
/// <param name="DataTypeKey">数据类型键。</param>
/// <param name="IsRequired">是否必填。</param>
/// <param name="DefaultValue">默认值文本。</param>
public sealed record ReportingParameterSchemaEntry(
    string ParameterKey,
    string DisplayName,
    string DataTypeKey,
    bool IsRequired,
    string? DefaultValue);
