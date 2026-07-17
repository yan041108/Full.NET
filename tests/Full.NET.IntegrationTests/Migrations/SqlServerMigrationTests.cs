using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;

namespace Full.NET.IntegrationTests.Migrations;

[TestClass]
public sealed class SqlServerMigrationTests
{
    private readonly MsSqlContainer _container = new MsSqlBuilder(
            "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
        .WithPassword("FullNet_Test!123")
        .Build();

    [TestInitialize]
    public Task StartAsync() => _container.StartAsync();

    [TestCleanup]
    public async Task CleanupAsync() => await _container.DisposeAsync();

    [TestMethod]
    public async Task SqlServer_migration_is_idempotent_and_creates_binary_outbox_schema()
    {
        var runner = new DbUpMigrationRunner(
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
                ConnectionString = _container.GetConnectionString(),
            }),
            NullLoggerFactory.Instance);

        var first = await runner.MigrateAsync();
        var second = await runner.MigrateAsync();

        Assert.IsTrue(first.Successful);
        Assert.IsTrue(first.ExecutedScriptCount > 0);
        Assert.IsTrue(second.Successful);
        Assert.AreEqual(0, second.ExecutedScriptCount);

        await using var connection = new SqlConnection(_container.GetConnectionString());
        var tables = (await connection.QueryAsync<string>(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo'"))
            .ToArray();

        AssertContainsIgnoreCase(tables, "fn_tenant_tenant");
        AssertContainsIgnoreCase(tables, "fn_outbox_message");
        AssertContainsIgnoreCase(tables, "fn_identity_user");
        AssertContainsIgnoreCase(tables, "fn_identity_refresh_session");
        AssertContainsIgnoreCase(tables, "fn_identity_auth_audit");
        AssertContainsIgnoreCase(tables, "SchemaVersions");

        var indexes = (await connection.QueryAsync<string>(
            """
            SELECT indexObject.name
            FROM sys.indexes AS indexObject
            INNER JOIN sys.tables AS tableObject ON tableObject.object_id = indexObject.object_id
            WHERE tableObject.name LIKE 'fn_identity_%' AND indexObject.name IS NOT NULL
            """))
            .ToArray();

        AssertRequiredIdentityIndexes(indexes);

        var columns = (await connection.QueryAsync<ColumnMetadata>(
            """
            SELECT COLUMN_NAME AS Name,
                   DATA_TYPE AS DataType,
                   CAST(CHARACTER_MAXIMUM_LENGTH AS bigint) AS MaximumLength
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'fn_outbox_message'
            """))
            .ToArray();

        AssertRequiredOutboxColumns(columns);
        var payload = columns.Single(column =>
            string.Equals(column.Name, "Payload", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual("varbinary", payload.DataType, ignoreCase: true);
        Assert.AreEqual(-1L, payload.MaximumLength);
    }

    private static void AssertRequiredOutboxColumns(IEnumerable<ColumnMetadata> columns)
    {
        var names = columns.Select(column => column.Name).ToArray();
        AssertContainsIgnoreCase(names, "SchemaVersion");
        AssertContainsIgnoreCase(names, "ContentType");
        AssertContainsIgnoreCase(names, "TenantId");
        AssertContainsIgnoreCase(names, "TraceId");
        AssertContainsIgnoreCase(names, "Payload");
    }

    private static void AssertRequiredIdentityIndexes(IEnumerable<string> indexes)
    {
        AssertContainsIgnoreCase(indexes, "UX_fn_identity_user_Scope_Username");
        AssertContainsIgnoreCase(indexes, "UX_fn_identity_refresh_session_TokenHash");
        AssertContainsIgnoreCase(indexes, "IX_fn_identity_refresh_session_Family");
        AssertContainsIgnoreCase(indexes, "IX_fn_identity_refresh_session_User");
        AssertContainsIgnoreCase(indexes, "IX_fn_identity_auth_audit_OccurredAt");
        AssertContainsIgnoreCase(indexes, "IX_fn_identity_auth_audit_User");
    }

    private static void AssertContainsIgnoreCase(IEnumerable<string> values, string expected) =>
        Assert.IsTrue(values.Contains(expected, StringComparer.OrdinalIgnoreCase));

    private sealed record ColumnMetadata(string Name, string DataType, long? MaximumLength);
}
