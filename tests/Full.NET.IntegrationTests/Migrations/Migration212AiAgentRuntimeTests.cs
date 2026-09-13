using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>冻结 212，模拟只建完运行表后重跑，幂等索引与 CHECK 保持可用。</summary>
[TestClass]
public sealed class Migration212AiAgentRuntimeTests
{
    [TestMethod]
    public Task SqlServer_recovers_partial_agent_runtime_schema() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_recovers_partial_agent_runtime_schema() => VerifyAsync(DatabaseProvider.MySql);

    [TestMethod]
    public Task SqlServer_recovers_partial_checkpoint_schema() => VerifyCheckpointPartialAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_recovers_partial_checkpoint_schema() => VerifyCheckpointPartialAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var mysql = provider == DatabaseProvider.MySql;
        var cs = mysql ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync() : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using DbConnection db = mysql
            ? new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: false))
            : new SqlConnection(cs);
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var name = assembly.GetManifestResourceNames().Single(item => item.EndsWith($"{provider}.212_AiAgentRuntime.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync();
        var marker = mysql ? "CREATE TABLE IF NOT EXISTS fn_ai_agent_step" : "IF OBJECT_ID(N'dbo.fn_ai_agent_step', N'U') IS NULL";
        var offset = script.IndexOf(marker, StringComparison.Ordinal);
        Assert.IsGreaterThan(0, offset);
        await db.ExecuteAsync(script[..offset]);
        await db.ExecuteAsync(script);
        await db.ExecuteAsync(script);
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_run"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_step"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_checkpoint"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_event"));
    }

    private static async Task VerifyCheckpointPartialAsync(DatabaseProvider provider)
    {
        var mysql = provider == DatabaseProvider.MySql;
        var cs = mysql ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync() : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using DbConnection db = mysql
            ? new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: false))
            : new SqlConnection(cs);
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var name = assembly.GetManifestResourceNames().Single(item => item.EndsWith($"{provider}.212_AiAgentRuntime.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync();
        var marker = mysql
            ? "CREATE TABLE IF NOT EXISTS fn_ai_agent_checkpoint"
            : "IF OBJECT_ID(N'dbo.fn_ai_agent_checkpoint', N'U') IS NULL";
        var offset = script.IndexOf(marker, StringComparison.Ordinal);
        Assert.IsGreaterThan(0, offset);
        await db.ExecuteAsync(script[..offset]);
        await db.ExecuteAsync(script);
        await db.ExecuteAsync(script);
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_checkpoint"));
    }
}
