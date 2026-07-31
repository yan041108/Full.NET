namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 定义单表元数据导入支持的数据库提供程序。
/// </summary>
public enum DatabaseMetadataProvider
{
    /// <summary>SQL Server 默认 dbo Schema。</summary>
    SqlServer = 1,

    /// <summary>MySQL 当前连接数据库。</summary>
    MySql = 2,
}
