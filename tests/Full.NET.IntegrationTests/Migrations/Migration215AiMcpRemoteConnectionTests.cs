using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>冻结 215，重复执行保持 MCP 远端连接与工具批准表可用。</summary>
[TestClass]
public sealed class Migration215AiMcpRemoteConnectionTests
{
    [TestMethod]
    public Task SqlServer_recovers_mcp_remote_schema() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_recovers_mcp_remote_schema() => VerifyAsync(DatabaseProvider.MySql);

    [TestMethod]
    public Task SqlServer_recovers_partial_tool_approval_schema() => VerifyToolApprovalPartialAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_recovers_partial_tool_approval_schema() => VerifyToolApprovalPartialAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var mysql = provider == DatabaseProvider.MySql;
        var cs = mysql
            ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using DbConnection db = mysql
            ? new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: false))
            : new SqlConnection(cs);
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(item => item.EndsWith($"{provider}.215_AiMcpRemoteConnection.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync();
        await db.ExecuteAsync(script);
        await db.ExecuteAsync(script);
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_mcp_remote_connection"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_mcp_remote_tool_approval"));
    }

    private static async Task VerifyToolApprovalPartialAsync(DatabaseProvider provider)
    {
        var mysql = provider == DatabaseProvider.MySql;
        var cs = mysql
            ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using DbConnection db = mysql
            ? new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: false))
            : new SqlConnection(cs);
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(item => item.EndsWith($"{provider}.215_AiMcpRemoteConnection.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync();
        var marker = mysql
            ? "CREATE TABLE IF NOT EXISTS fn_ai_mcp_remote_tool_approval"
            : "IF OBJECT_ID(N'dbo.fn_ai_mcp_remote_tool_approval', N'U') IS NULL";
        var offset = script.IndexOf(marker, StringComparison.Ordinal);
        Assert.IsGreaterThan(0, offset);
        await db.ExecuteAsync(script[..offset]);
        await db.ExecuteAsync(script);
        await db.ExecuteAsync(script);
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_mcp_remote_connection"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_mcp_remote_tool_approval"));
    }
}
