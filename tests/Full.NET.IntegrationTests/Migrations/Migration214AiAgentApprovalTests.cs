using System.Data.Common;

using Dapper;

using Full.NET.Data.Abstractions;

using Full.NET.Data.MySql;

using Full.NET.Migrations.DbUp;

using Microsoft.Data.SqlClient;

using MySqlConnector;



namespace Full.NET.IntegrationTests.Migrations;



/// <summary>冻结 214，重复执行保持审批/委托表与工具审计扩展可用。</summary>

[TestClass]

public sealed class Migration214AiAgentApprovalTests

{

    [TestMethod]

    public Task SqlServer_recovers_agent_approval_schema() => VerifyAsync(DatabaseProvider.SqlServer);



    [TestMethod]

    public Task MySql_recovers_agent_approval_schema() => VerifyAsync(DatabaseProvider.MySql);



    [TestMethod]

    public Task SqlServer_recovers_partial_delegation_schema() => VerifyDelegationPartialAsync(DatabaseProvider.SqlServer);



    [TestMethod]

    public Task MySql_recovers_partial_delegation_schema() => VerifyDelegationPartialAsync(DatabaseProvider.MySql);



    private static async Task VerifyAsync(DatabaseProvider provider)

    {

        await using var db = await OpenAsync(provider);

        var script = await LoadScriptAsync(provider);

        await EnsureToolCallTableAsync(db, provider);

        await db.ExecuteAsync(script);

        await db.ExecuteAsync(script);

        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_approval"));

        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_delegation"));

    }



    private static async Task VerifyDelegationPartialAsync(DatabaseProvider provider)

    {

        await using var db = await OpenAsync(provider);

        var script = await LoadScriptAsync(provider);

        var mysql = provider == DatabaseProvider.MySql;

        var marker = mysql

            ? "CREATE TABLE IF NOT EXISTS fn_ai_agent_delegation"

            : "IF OBJECT_ID(N'dbo.fn_ai_agent_delegation', N'U') IS NULL";

        var offset = script.IndexOf(marker, StringComparison.Ordinal);

        Assert.IsGreaterThan(0, offset);

        await EnsureToolCallTableAsync(db, provider);

        await db.ExecuteAsync(script[..offset]);

        await db.ExecuteAsync(script);

        await db.ExecuteAsync(script);

        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_approval"));

        Assert.AreEqual(0L, await db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_delegation"));

    }



    private static async Task<DbConnection> OpenAsync(DatabaseProvider provider)

    {

        var mysql = provider == DatabaseProvider.MySql;

        var cs = mysql

            ? await SharedDatabaseFixture.CreateMySqlDatabaseAsync()

            : await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();

        DbConnection db = mysql

            ? new MySqlConnection(MySqlConnectionStringPolicy.Create(cs, MySqlGuidStorageMode.Binary16, allowUserVariables: true))

            : new SqlConnection(cs);

        await db.OpenAsync();

        return db;

    }



    private static async Task<string> LoadScriptAsync(DatabaseProvider provider)

    {

        var assembly = typeof(DbUpMigrationRunner).Assembly;

        var name = assembly.GetManifestResourceNames()

            .Single(item => item.EndsWith($"{provider}.214_AiAgentApproval.sql", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;

        using var reader = new StreamReader(stream);

        return await reader.ReadToEndAsync();

    }



    private static async Task EnsureToolCallTableAsync(DbConnection db, DatabaseProvider provider)

    {

        if (provider == DatabaseProvider.MySql)

        {

            await db.ExecuteAsync(

                "CREATE TABLE IF NOT EXISTS fn_ai_agent_tool_call (StatusKey varchar(16) NOT NULL)");

            return;

        }



        await db.ExecuteAsync(

            """

            IF OBJECT_ID(N'dbo.fn_ai_agent_tool_call', N'U') IS NULL

            CREATE TABLE dbo.fn_ai_agent_tool_call (StatusKey varchar(16) NOT NULL);

            """);

    }

}


