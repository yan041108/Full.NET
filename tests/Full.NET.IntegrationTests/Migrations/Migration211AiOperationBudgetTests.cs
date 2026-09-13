using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>冻结 211，模拟只建完前两张表且尚未记账，完整重跑保留既有数据。</summary>
[TestClass]
public sealed class Migration211AiOperationBudgetTests
{
    [TestMethod]
    public Task SqlServer_recovers_partial_budget_schema() => VerifyAsync(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_recovers_partial_budget_schema() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var mysql = provider == DatabaseProvider.MySql;
        var cs = mysql ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync() : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using DbConnection db = mysql
            ? new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: false)) : new SqlConnection(cs);
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var name = assembly.GetManifestResourceNames().Single(item => item.EndsWith($"{provider}.211_AiOperationBudget.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync();
        var marker = mysql ? "CREATE TABLE IF NOT EXISTS fn_ai_operation_budget" : "IF OBJECT_ID(N'dbo.fn_ai_operation_budget', N'U') IS NULL";
        var offset = script.IndexOf(marker, StringComparison.Ordinal);
        Assert.IsGreaterThan(0, offset);
        await db.ExecuteAsync(script[..offset]);
        await db.ExecuteAsync("INSERT INTO fn_ai_budget_scope (Id, ScopeKey) VALUES (@Id, 'host')", new { Id = Guid.CreateVersion7() });
        await db.ExecuteAsync(script);
        await db.ExecuteAsync(script);
        Assert.AreEqual(1L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_budget_scope"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_model_price"));
        await Assert.ThrowsAsync<DbException>(() => db.ExecuteAsync("INSERT INTO fn_ai_budget_scope (Id, ScopeKey) VALUES (@Id, 'host')", new { Id = Guid.CreateVersion7() }));
        if (!mysql)
        {
            await db.ExecuteAsync("DROP INDEX IX_fn_ai_operation_budget_Month ON fn_ai_operation_budget");
            await db.ExecuteAsync(script);
            Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('fn_ai_operation_budget') AND name='IX_fn_ai_operation_budget_Month'"));
        }
    }
}
