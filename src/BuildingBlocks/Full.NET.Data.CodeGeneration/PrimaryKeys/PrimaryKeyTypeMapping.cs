namespace Full.NET.Data.CodeGeneration.PrimaryKeys;

/// <summary>
/// 将主键配置档解析为跨 C#、SQL Server、MySQL 与 JSON 的一致物理类型。
/// </summary>
public static class PrimaryKeyTypeMapping
{
    /// <summary>
    /// 解析指定配置档的物理类型映射。
    /// </summary>
    /// <param name="profile">主键配置档。</param>
    /// <returns>四端一致的类型描述。</returns>
    /// <exception cref="ArgumentOutOfRangeException">传入未知配置档时抛出。</exception>
    public static PrimaryKeyPhysicalTypes Resolve(PrimaryKeyProfile profile) =>
        profile switch
        {
            PrimaryKeyProfile.UuidV7 => new PrimaryKeyPhysicalTypes
            {
                CSharpType = "Guid",
                SqlServerColumnType = "uniqueidentifier",
                MySqlColumnType = "BINARY(16)",
                JsonSchemaType = "string",
                JsonSchemaFormat = "uuid",
            },
            PrimaryKeyProfile.Snowflake => new PrimaryKeyPhysicalTypes
            {
                CSharpType = "long",
                SqlServerColumnType = "bigint",
                MySqlColumnType = "BIGINT",
                JsonSchemaType = "string",
                JsonSchemaFormat = null,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "不支持的主键配置档。"),
        };

    /// <summary>
    /// 判断两个配置档是否可在同一实体或关系图中混用。
    /// </summary>
    /// <param name="left">左侧配置档。</param>
    /// <param name="right">右侧配置档。</param>
    /// <returns>仅当两者相同且非默认冲突时返回 <see langword="true"/>。</returns>
    public static bool AreProfilesCompatible(PrimaryKeyProfile left, PrimaryKeyProfile right) =>
        left == right;
}
