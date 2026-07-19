namespace Full.NET.Data.CodeGeneration.PrimaryKeys;

/// <summary>
/// 定义脚手架与代码生成器可选的主键物理映射配置档。
/// </summary>
public enum PrimaryKeyProfile
{
    /// <summary>Full.NET 官方默认：应用端 UUID v7，逻辑类型为 <see cref="Guid"/>。</summary>
    UuidV7,

    /// <summary>项目级可选：Snowflake <c>long</c>，须由独立 ADR 授权且不得与 UUID 混用。</summary>
    Snowflake,
}
