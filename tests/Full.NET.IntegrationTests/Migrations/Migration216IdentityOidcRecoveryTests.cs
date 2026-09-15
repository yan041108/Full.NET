using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>冻结 216，重复执行保持 OIDC 协议状态表可用。</summary>
[TestClass]
public sealed class Migration216IdentityOidcRecoveryTests
{
    [TestMethod]
    public Task SqlServer_recovers_oidc_protocol_schema() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_recovers_oidc_protocol_schema() => VerifyAsync(DatabaseProvider.MySql);

    [TestMethod]
    public Task SqlServer_recovers_partial_token_schema() => VerifyTokenPartialAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_recovers_partial_token_schema() => VerifyTokenPartialAsync(DatabaseProvider.MySql);

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
            .Single(item => item.EndsWith($"{provider}.216_IdentityOidcProtocolState.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync();
        await db.ExecuteAsync(script);
        await db.ExecuteAsync(script);
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_identity_oidc_application"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_identity_oidc_authorization"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_identity_oidc_scope"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_identity_oidc_token"));
    }

    private static async Task VerifyTokenPartialAsync(DatabaseProvider provider)
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
            .Single(item => item.EndsWith($"{provider}.216_IdentityOidcProtocolState.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var script = await reader.ReadToEndAsync();
        var marker = mysql
            ? "CREATE TABLE IF NOT EXISTS fn_identity_oidc_token"
            : "IF OBJECT_ID(N'dbo.fn_identity_oidc_token', N'U') IS NULL";
        var offset = script.IndexOf(marker, StringComparison.Ordinal);
        Assert.IsGreaterThan(0, offset);
        await db.ExecuteAsync(script[..offset]);
        await db.ExecuteAsync(script);
        await db.ExecuteAsync(script);
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_identity_oidc_application"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_identity_oidc_authorization"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_identity_oidc_scope"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_identity_oidc_token"));
    }
}