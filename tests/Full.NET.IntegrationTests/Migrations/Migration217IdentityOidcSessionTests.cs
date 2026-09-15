using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Migrations;

/// <summary>冻结 217，重复执行保持 OIDC 会话表可用。</summary>
[TestClass]
public sealed class Migration217IdentityOidcSessionTests
{
    [TestMethod]
    public Task SqlServer_recovers_oidc_session_schema() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_recovers_oidc_session_schema() => VerifyAsync(DatabaseProvider.MySql);

    [TestMethod]
    public Task SqlServer_recovers_partial_application_session_schema() =>
        VerifyApplicationSessionPartialAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_recovers_partial_application_session_schema() =>
        VerifyApplicationSessionPartialAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var mysql = provider == DatabaseProvider.MySql;
        var cs = mysql
            ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using DbConnection db = mysql
            ? new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: false))
            : new SqlConnection(cs);
        await ExecuteProtocolAndSessionMigrationsAsync(db, provider);
        await ExecuteProtocolAndSessionMigrationsAsync(db, provider);
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_identity_oidc_center_session"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_identity_oidc_application_session"));
    }

    private static async Task VerifyApplicationSessionPartialAsync(DatabaseProvider provider)
    {
        var mysql = provider == DatabaseProvider.MySql;
        var cs = mysql
            ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()
            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using DbConnection db = mysql
            ? new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: false))
            : new SqlConnection(cs);
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        var protocolName = assembly.GetManifestResourceNames()
            .Single(item => item.EndsWith($"{provider}.216_IdentityOidcProtocolState.sql", StringComparison.Ordinal));
        using (var protocolStream = assembly.GetManifestResourceStream(protocolName)!)
        using (var protocolReader = new StreamReader(protocolStream))
        {
            await db.ExecuteAsync(await protocolReader.ReadToEndAsync());
        }

        var sessionName = assembly.GetManifestResourceNames()
            .Single(item => item.EndsWith($"{provider}.217_IdentityOidcSession.sql", StringComparison.Ordinal));
        using var sessionStream = assembly.GetManifestResourceStream(sessionName)!;
        using var sessionReader = new StreamReader(sessionStream);
        var script = await sessionReader.ReadToEndAsync();
        var marker = mysql
            ? "CREATE TABLE IF NOT EXISTS fn_identity_oidc_application_session"
            : "IF OBJECT_ID(N'dbo.fn_identity_oidc_application_session', N'U') IS NULL";
        var offset = script.IndexOf(marker, StringComparison.Ordinal);
        Assert.IsGreaterThan(0, offset);
        await db.ExecuteAsync(script[..offset]);
        await db.ExecuteAsync(script);
        await db.ExecuteAsync(script);
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_identity_oidc_center_session"));
        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_identity_oidc_application_session"));
    }

    private static async Task ExecuteProtocolAndSessionMigrationsAsync(DbConnection db, DatabaseProvider provider)
    {
        var assembly = typeof(DbUpMigrationRunner).Assembly;
        foreach (var migration in new[] { "216_IdentityOidcProtocolState.sql", "217_IdentityOidcSession.sql" })
        {
            var name = assembly.GetManifestResourceNames()
                .Single(item => item.EndsWith($"{provider}.{migration}", StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            await db.ExecuteAsync(await reader.ReadToEndAsync());
        }
    }
}