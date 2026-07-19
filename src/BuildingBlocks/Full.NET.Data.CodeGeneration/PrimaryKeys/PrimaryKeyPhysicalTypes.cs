namespace Full.NET.Data.CodeGeneration.PrimaryKeys;

/// <summary>
/// 描述同一主键配置档在 C#、双库物理列与 JSON Schema 中的类型映射。
/// </summary>
public sealed class PrimaryKeyPhysicalTypes
{
    /// <summary>获取 C# 属性类型名称。</summary>
    public required string CSharpType { get; init; }

    /// <summary>获取 SQL Server 列类型声明。</summary>
    public required string SqlServerColumnType { get; init; }

    /// <summary>获取 MySQL 列类型声明。</summary>
    public required string MySqlColumnType { get; init; }

    /// <summary>获取 OpenAPI/JSON Schema 类型。</summary>
    public required string JsonSchemaType { get; init; }

    /// <summary>获取 OpenAPI/JSON Schema format；Snowflake 等无 format 时为 <see langword="null"/>。</summary>
    public string? JsonSchemaFormat { get; init; }
}
