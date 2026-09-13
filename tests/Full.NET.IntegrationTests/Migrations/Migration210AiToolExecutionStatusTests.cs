using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>冻结单一 210 脚本，直接模拟尚未记账且 CHECK 已删除后的恢复。</summary>
[TestClass]
public sealed class Migration210AiToolExecutionStatusTests
{
    [TestMethod]
    public Task SqlServer_recovers_interrupted_check_expansion() => VerifyAsync(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_recovers_interrupted_check_expansion() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var sqlServer = provider == DatabaseProvider.SqlServer;
        var cs = sqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using DbConnection connection = sqlServer ? new SqlConnection(cs)
            : new MySqlConnection(new MySqlConnectionStringBuilder(cs) { AllowUserVariables = true }.ConnectionString);
        await connection.ExecuteAsync("""
            CREATE TABLE fn_ai_agent_tool_call (StatusKey varchar(16) NOT NULL,
                CONSTRAINT CK_fn_ai_agent_tool_call_StatusKey CHECK (StatusKey IN ('succeeded', 'failed', 'denied')))
            """);
        await connection.ExecuteAsync("INSERT INTO fn_ai_agent_tool_call (StatusKey) VALUES ('succeeded')");
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var name = assembly.GetManifestResourceNames().Single(item => item.EndsWith($"{provider}.210_AiToolExecutionStatus.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync();
        await connection.ExecuteAsync(script);
        await connection.ExecuteAsync("INSERT INTO fn_ai_agent_tool_call (StatusKey) VALUES ('started'), ('cancelled')");
        await connection.ExecuteAsync(sqlServer
            ? "ALTER TABLE fn_ai_agent_tool_call DROP CONSTRAINT CK_fn_ai_agent_tool_call_StatusKey"
            : "ALTER TABLE fn_ai_agent_tool_call DROP CHECK CK_fn_ai_agent_tool_call_StatusKey");
        await connection.ExecuteAsync(script);
        await connection.ExecuteAsync(script);
        Assert.AreEqual(3, await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_agent_tool_call"));
        await Assert.ThrowsAsync<DbException>(() => connection.ExecuteAsync("INSERT INTO fn_ai_agent_tool_call (StatusKey) VALUES ('unknown')"));
    }
}
