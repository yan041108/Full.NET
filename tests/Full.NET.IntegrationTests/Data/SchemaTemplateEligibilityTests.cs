using Dapper;
using Full.NET.Data.Abstractions;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using System.Data.Common;

namespace Full.NET.IntegrationTests.Data;

[TestClass]
public sealed class SchemaTemplateEligibilityTests
{
    [TestMethod]
    [DataRow("AliasType")]
    [DataRow("Schema")]
    [DataRow("User")]
    [DataRow("XmlSchema")]
    public async Task SqlServer_template_must_preserve_independent_metadata_without_journal(string kind)
    {
        var cs = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using var connection = new SqlConnection(cs);
        var (create, exists) = kind switch
        {
            "AliasType" => ("CREATE TYPE dbo.LegacyCode FROM nvarchar(20) NULL;", "SELECT COUNT(*) FROM sys.types WHERE name = 'LegacyCode';"),
            "Schema" => ("CREATE SCHEMA LegacyScope;", "SELECT COUNT(*) FROM sys.schemas WHERE name = 'LegacyScope';"),
            "User" => ("CREATE USER LegacyUser WITHOUT LOGIN;", "SELECT COUNT(*) FROM sys.database_principals WHERE name = 'LegacyUser';"),
            "XmlSchema" => ("CREATE XML SCHEMA COLLECTION dbo.LegacyXml AS N'<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"><xs:element name=\"Value\" type=\"xs:string\"/></xs:schema>';", "SELECT COUNT(*) FROM sys.xml_schema_collections WHERE name = 'LegacyXml';"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        await connection.ExecuteAsync(create);
        var hydrated = await ApiSchemaTemplate.TryHydrateEmptyDatabaseAsync(DatabaseProvider.SqlServer, cs,
            (_, _) => throw new InvalidOperationException("独立历史元数据不得被模板覆盖。"), CancellationToken.None);
        Assert.IsFalse(hydrated);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(exists));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MySql_template_must_preserve_routine_or_event_without_tables_or_journal(bool isEvent)
    {
        var cs = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using var connection = new MySqlConnection(cs);
        await connection.ExecuteAsync(isEvent
            ? "CREATE EVENT LegacyEvent ON SCHEDULE AT CURRENT_TIMESTAMP + INTERVAL 1 DAY DO SELECT 7;"
            : "CREATE PROCEDURE LegacyRoutine() SELECT 7;");
        var hydrated = await ApiSchemaTemplate.TryHydrateEmptyDatabaseAsync(DatabaseProvider.MySql, cs,
            (_, _) => throw new InvalidOperationException("独立历史元数据不得被模板覆盖。"), CancellationToken.None);
        Assert.IsFalse(hydrated);
        var exists = isEvent
            ? "SELECT COUNT(*) FROM information_schema.EVENTS WHERE EVENT_SCHEMA = DATABASE() AND EVENT_NAME = 'LegacyEvent';"
            : "SELECT COUNT(*) FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA = DATABASE() AND ROUTINE_NAME = 'LegacyRoutine';";
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(exists));
    }

    [TestMethod]
    public async Task SqlServer_template_must_preserve_database_trigger_without_tables_or_journal()
    {
        var cs = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using var connection = new SqlConnection(cs);
        await connection.ExecuteAsync("CREATE TRIGGER SchemaGuard ON DATABASE FOR CREATE_TABLE AS RETURN;");
        Assert.AreEqual(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped = 0;"));
        var hydrated = await ApiSchemaTemplate.TryHydrateEmptyDatabaseAsync(DatabaseProvider.SqlServer, cs,
            (_, _) => throw new InvalidOperationException("数据库级触发器不得被模板覆盖。"), CancellationToken.None);
        Assert.IsFalse(hydrated);
        Assert.AreEqual(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.triggers WHERE parent_class = 0 AND is_ms_shipped = 0 AND name = 'SchemaGuard';"));
    }

    [TestMethod]
    public Task SqlServer_template_must_preserve_legacy_schema_without_journal() => VerifyAsync(DatabaseProvider.SqlServer);

    [TestMethod]
    public Task MySql_template_must_preserve_legacy_schema_without_journal() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        var cs = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await using DbConnection connection = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(cs) : new MySqlConnection(cs);
        await connection.ExecuteAsync("CREATE TABLE LegacyProbe (Value int); INSERT INTO LegacyProbe (Value) VALUES (7);");
        var hydrated = await ApiSchemaTemplate.TryHydrateEmptyDatabaseAsync(provider, cs,
            (_, _) => throw new InvalidOperationException("非空历史库不得初始化或覆盖模板。"), CancellationToken.None);
        Assert.IsFalse(hydrated);
        Assert.AreEqual(7, await connection.ExecuteScalarAsync<int>("SELECT Value FROM LegacyProbe;"));
    }
}
