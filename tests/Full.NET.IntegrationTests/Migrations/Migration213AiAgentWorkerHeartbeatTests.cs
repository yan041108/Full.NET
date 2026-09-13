using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>冻结 213，重复执行保持空表与索引可用。</summary>
[TestClass]
public sealed class Migration213AiAgentWorkerHeartbeatTests
{
    [TestMethod]
    public Task SqlServer_recovers_worker_heartbeat_schema() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_recovers_worker_heartbeat_schema() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var mysql = provider == DatabaseProvider.MySql;
        var cs = mysql ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync() : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using DbConnection db = mysql
            ? new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: false))
            : new SqlConnection(cs);
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var name = assembly.GetManifestResourceNames().Single(item => item.EndsWith($"{provider}.213_AiAgentWorkerHeartbeat.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync();
        await db.ExecuteAsync(script);
        await db.ExecuteAsync(script);
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_worker_instance"));
    }
}
