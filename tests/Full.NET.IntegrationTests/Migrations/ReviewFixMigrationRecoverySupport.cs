using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>审查修复新增迁移的双库恢复测试共用迁移执行器与对象探测。</summary>
internal static class ReviewFixMigrationRecoverySupport
{
    /// <summary>构建使用正式 UUID 与命名策略的迁移执行器。</summary>
    /// <param name="provider">数据库提供程序。</param>
    /// <param name="connectionString">隔离测试库连接字符串。</param>
    public static DbUpMigrationRunner CreateRunner(DatabaseProvider provider, string connectionString) => new(
        Options.Create(new DatabaseOptions
        {
            Provider = provider,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        }),
        NullLoggerFactory.Instance,
        MigrationContractOptionFactory.UuidOptions(),
        MigrationContractOptionFactory.NamingOptions());

    /// <summary>创建使用标准 Binary16 Guid 字节序的 MySQL 查询连接。</summary>
    /// <param name="connectionString">隔离测试库连接字符串。</param>
    public static MySqlConnection MySqlConnection(string connectionString) => new(
        MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));

    /// <summary>删除指定脚本的 DbUp 记账，以便重跑前向迁移。</summary>
    /// <param name="connection">已打开的数据库连接。</param>
    /// <param name="scriptToken">脚本文件名片段。</param>
    /// <param name="sqlServer">是否为 SQL Server。</param>
    public static Task DeleteScriptAsync(DbConnection connection, string scriptToken, bool sqlServer) =>
        connection.ExecuteAsync(
            sqlServer
                ? "DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE @Pattern"
                : "DELETE FROM schemaversions WHERE ScriptName LIKE @Pattern",
            new { Pattern = "%" + scriptToken });

    /// <summary>探测业务表是否存在。</summary>
    /// <param name="connection">已打开的数据库连接。</param>
    /// <param name="tableName">表名。</param>
    /// <param name="sqlServer">是否为 SQL Server。</param>
    public static Task<int> TableExistsAsync(DbConnection connection, string tableName, bool sqlServer) =>
        connection.ExecuteScalarAsync<int>(
            sqlServer
                ? "SELECT COUNT(*) FROM sys.tables WHERE name = @TableName AND schema_id = SCHEMA_ID(N'dbo')"
                : "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @TableName",
            new { TableName = tableName });

    /// <summary>探测列是否存在。</summary>
    /// <param name="connection">已打开的数据库连接。</param>
    /// <param name="tableName">表名。</param>
    /// <param name="columnName">列名。</param>
    /// <param name="sqlServer">是否为 SQL Server。</param>
    public static Task<int> ColumnExistsAsync(
        DbConnection connection,
        string tableName,
        string columnName,
        bool sqlServer) =>
        connection.ExecuteScalarAsync<int>(
            sqlServer
                ? "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.' + @TableName) AND name = @ColumnName"
                : """
                  SELECT COUNT(*) FROM information_schema.columns
                  WHERE table_schema = DATABASE() AND table_name = @TableName AND column_name = @ColumnName
                  """,
            new { TableName = tableName, ColumnName = columnName });

    /// <summary>探测索引是否存在。</summary>
    /// <param name="connection">已打开的数据库连接。</param>
    /// <param name="tableName">表名。</param>
    /// <param name="indexName">索引名。</param>
    /// <param name="sqlServer">是否为 SQL Server。</param>
    public static Task<int> IndexExistsAsync(
        DbConnection connection,
        string tableName,
        string indexName,
        bool sqlServer) =>
        connection.ExecuteScalarAsync<int>(
            sqlServer
                ? "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.' + @TableName) AND name = @IndexName"
                : """
                  SELECT COUNT(DISTINCT index_name) FROM information_schema.statistics
                  WHERE table_schema = DATABASE() AND table_name = @TableName AND index_name = @IndexName
                  """,
            new { TableName = tableName, IndexName = indexName });
}
