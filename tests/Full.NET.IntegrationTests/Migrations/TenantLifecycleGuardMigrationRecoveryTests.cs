using System.Data.Common;
using System.Text.RegularExpressions;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>建表已提交而固定行缺失时补齐；重复部署保留已有锁修订。</summary>
[TestClass]
public sealed class TenantLifecycleGuardMigrationRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Lifecycle_guard_migration_recovers_missing_seed_and_preserves_revision(DatabaseProvider provider)
    {
        var server = provider == DatabaseProvider.SqlServer;
        var connectionString = server ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using DbConnection connection = server ? new SqlConnection(connectionString) : ReviewFixMigrationRecoverySupport.MySqlConnection(connectionString);
        await connection.OpenAsync();
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        using var reader = new StreamReader(assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith(
            $"{(server ? "SqlServer" : "MySql")}.362_TenancyLifecycleGuard.sql", StringComparison.Ordinal)))!);
        var script = await reader.ReadToEndAsync();
        var batches = server ? Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase) : [script];
        await connection.ExecuteAsync(server ? batches[0] : script[..script.IndexOf("INSERT INTO", StringComparison.Ordinal)]);
        await RunAsync();
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM fn_tenancy_lifecycle_guard WHERE Version=1"));
        await connection.ExecuteAsync("UPDATE fn_tenancy_lifecycle_guard SET Version=9");
        await RunAsync(); await RunAsync();
        Assert.AreEqual(9L, await connection.ExecuteScalarAsync<long>("SELECT Version FROM fn_tenancy_lifecycle_guard"));
        await connection.ExecuteAsync("DELETE FROM fn_tenancy_lifecycle_guard");
        await RunAsync();
        Assert.AreEqual(1L, await connection.ExecuteScalarAsync<long>("SELECT Version FROM fn_tenancy_lifecycle_guard"));
        async Task RunAsync() { foreach (var batch in batches.Where(value => !string.IsNullOrWhiteSpace(value))) await connection.ExecuteAsync(batch); }
    }
}
