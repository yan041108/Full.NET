using Dapper;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

[TestClass]
public sealed class MessagingOutboxMySqlTests
{
    [TestMethod]
    public async Task MySql_messaging_outbox_schema_matches_append_only_contract()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        };
        await MessagingOutboxTestSupport.MigrateAsync(options);

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        await MessagingOutboxSchemaAssertions.VerifyMySqlAsync(connection);
    }

    [TestMethod]
    public async Task MySql_append_only_outbox_insert_persists_metadata_columns()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        };
        await MessagingOutboxTestSupport.MigrateAsync(options);

        var configuration = MessagingOutboxTestSupport.CreateConfiguration(options);
        await using var services = MessagingOutboxTestSupport.BuildAppendOnlyServices(configuration);
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();

        var partitionKey = Guid.CreateVersion7().ToString("D");
        var metadata = MessagingOutboxTestSupport.CreateMetadata(partitionKey);
        var payload = new MessagingOutboxTestSupport.MessagingOutboxTestPayload("append-only");
        var commandTransaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
        var outboxWriter = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        await commandTransaction.ExecuteAsync<bool>(
            async cancellationToken =>
            {
                await outboxWriter.AddAsync(
                    MessagingOutboxTestSupport.TestEventType,
                    MessagingOutboxTestSupport.TestSchemaVersion,
                    payload,
                    metadata,
                    cancellationToken);
                return true;
            },
            CancellationToken.None);

        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        var row = await connection.QuerySingleAsync<MySqlOutboxRow>(
            """
            SELECT MessageType, SchemaVersion, PartitionKey, Producer, TenantId
            FROM fn_messaging_outbox_event
            WHERE PartitionKey = @PartitionKey
            """,
            new { PartitionKey = partitionKey });

        Assert.AreEqual(MessagingOutboxTestSupport.TestEventType, row.MessageType);
        Assert.AreEqual(MessagingOutboxTestSupport.TestSchemaVersion, row.SchemaVersion);
        Assert.AreEqual(partitionKey, row.PartitionKey);
        Assert.AreEqual("fullnet.messaging.tests", row.Producer);
        Assert.IsNull(row.TenantId);
    }

    [TestMethod]
    public async Task MySql_business_transaction_rollback_removes_append_only_outbox_row()
    {
        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var options = new DatabaseOptions
        {
            Provider = DatabaseProvider.MySql,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        };
        await MessagingOutboxTestSupport.MigrateAsync(options);

        var configuration = MessagingOutboxTestSupport.CreateConfiguration(options);
        await using var services = MessagingOutboxTestSupport.BuildAppendOnlyServices(configuration);
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();

        var partitionKey = Guid.CreateVersion7().ToString("D");
        var metadata = MessagingOutboxTestSupport.CreateMetadata(partitionKey);
        var payload = new MessagingOutboxTestSupport.MessagingOutboxTestPayload("rollback");
        var commandTransaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
        var outboxWriter = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        var rollbackObserved = false;
        try
        {
            await commandTransaction.ExecuteAsync<bool>(
                async cancellationToken =>
                {
                    await outboxWriter.AddAsync(
                        MessagingOutboxTestSupport.TestEventType,
                        MessagingOutboxTestSupport.TestSchemaVersion,
                        payload,
                        metadata,
                        cancellationToken);
                    throw new InvalidOperationException(
                        "Injected rollback for append-only messaging outbox test.");
                },
                CancellationToken.None);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("Injected rollback", StringComparison.Ordinal))
        {
            rollbackObserved = true;
        }

        Assert.IsTrue(rollbackObserved);
        await using var connection = new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                connectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false));
        Assert.AreEqual(
            0,
            await connection.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM fn_messaging_outbox_event
                WHERE PartitionKey = @PartitionKey
                """,
                new { PartitionKey = partitionKey }));
    }

    private sealed record MySqlOutboxRow(
        string MessageType,
        int SchemaVersion,
        string PartitionKey,
        string Producer,
        Guid? TenantId);
}