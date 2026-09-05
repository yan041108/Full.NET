using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>验证 117 通知模板语言结构可从索引缺失的部分迁移状态恢复。</summary>
[TestClass]
public sealed class Migration117NotificationsTemplateLocaleRecoveryTests
{
    /// <summary>SQL Server 重跑 117 时必须补回模板语言唯一索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task SqlServer_recovers_missing_template_locale_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateSqlServer(connectionString, 117).Successful);
        await using var connection = new SqlConnection(connectionString);
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.columns WHERE (object_id = OBJECT_ID(N'dbo.fn_notifications_template') AND name IN (N'LocaleTag', N'DefaultLocaleTag')) OR (object_id = OBJECT_ID(N'dbo.fn_notifications_template_version') AND name = N'LocaleTag')"));

        await connection.ExecuteAsync(
            """
            DROP INDEX UX_fn_notifications_template_Scope_Key_Locale ON dbo.fn_notifications_template;
            DELETE FROM dbo.SchemaVersions WHERE ScriptName LIKE '%117_NotificationsTemplateLocale.sql';
            """);

        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateSqlServer(connectionString, 117).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_notifications_template') AND name = N'UX_fn_notifications_template_Scope_Key_Locale'"));
    }

    /// <summary>MySQL 重跑 117 时必须补回模板语言唯一索引。</summary>
    /// <returns>表示异步验证过程的任务。</returns>
    [TestMethod]
    public async Task MySql_recovers_missing_template_locale_index()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateMySql(connectionString, 117).Successful);
        await using var connection = new MySqlConnection(
            Migration114To118RecoveryTestSupport.MySqlQueryConnectionString(connectionString));
        Assert.AreEqual(3, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND ((TABLE_NAME = 'fn_notifications_template' AND COLUMN_NAME IN ('LocaleTag', 'DefaultLocaleTag')) OR (TABLE_NAME = 'fn_notifications_template_version' AND COLUMN_NAME = 'LocaleTag'))"));

        await connection.ExecuteAsync(
            """
            ALTER TABLE fn_notifications_template DROP INDEX UX_fn_notifications_template_Scope_Key_Locale;
            DELETE FROM schemaversions WHERE ScriptName LIKE '%117_NotificationsTemplateLocale.sql';
            """);

        Assert.IsTrue(Migration114To118RecoveryTestSupport.MigrateMySql(connectionString, 117).Successful);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'fn_notifications_template' AND INDEX_NAME = 'UX_fn_notifications_template_Scope_Key_Locale'"));
    }
}
