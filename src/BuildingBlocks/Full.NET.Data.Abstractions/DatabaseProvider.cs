namespace Full.NET.Data.Abstractions;

/// <summary>
/// 标识当前运行环境使用的关系型数据库 Provider 类型，用于在 Dapper 之上切换
/// SQL 方言（如 TOP/LIMIT、UNIQUE 索引语法、GETDATE()/NOW() 差异）。
/// </summary>
/// <remarks>
/// <para>
/// 注意：当前代码库中不包含 None 成员。DatabaseOptions 在 Startup 阶段必须显式指定
/// 有效 Provider，使用默认值（0 = SqlServer）被视为有意选择而非未配置。如果需要
/// 增加 None 哨兵值，应在同一提交中同步调整 DatabaseOptions.Provider 的默认值与
/// Startup 校验逻辑，避免未配置场景静默落到 SqlServer。
/// </para>
/// <para>
/// 新增 Provider 时需同步更新：SQL 方言映射、迁移脚本生成器、连接字符串校验器。
/// </para>
/// </remarks>
public enum DatabaseProvider
{
    /// <summary>
    /// Microsoft SQL Server（2016+，含 Azure SQL Database）。
    /// </summary>
    /// <remarks>
    /// 使用 Microsoft.Data.SqlClient 驱动。推荐兼容性级别 130 以上以启用
    /// STRING_SPLIT、JSON 支持等现代特性。
    /// </remarks>
    SqlServer,

    /// <summary>
    /// Oracle MySQL（8.0+，含兼容分支 Percona Server / MariaDB 10.5+）。
    /// </summary>
    /// <remarks>
    /// 使用 MySqlConnector 驱动。必须配合 <see cref="MySqlGuidStorageMode.Binary16"/>
    /// 生产模式以保证 RFC 9562 UUID 的索引效率与字节序正确性。
    /// </remarks>
    MySql,
}
