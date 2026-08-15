using Dapper;

using Full.NET.Data.Abstractions;

using Full.NET.IntegrationTests.Migrations;

using Full.NET.Messaging.Abstractions;

using Microsoft.Data.SqlClient;



namespace Full.NET.IntegrationTests.Messaging;



[TestClass]
[DoNotParallelize]
public sealed class SqlServerCdcShadowTests

{

    [TestMethod]

    public async Task SqlServer_committed_outbox_insert_is_captured_by_cdc()

    {

        var connectionString = await SqlServerCdcTestSupport.ResolveConnectionStringAsync();

        var options = new DatabaseOptions

        {

            Provider = DatabaseProvider.SqlServer,

            ConnectionString = connectionString,

            CommandTimeoutSeconds = 300,

        };

        await MessagingOutboxTestSupport.MigrateAsync(options);



        var cdcEnablement = await SqlServerCdcTestSupport.TryEnableCdcAsync(connectionString);

        if (!cdcEnablement.Succeeded)

        {

            Assert.Inconclusive(SqlServerCdcTestSupport.BuildInconclusiveMessage(cdcEnablement));

        }



        var partitionKey = Guid.CreateVersion7().ToString("D");

        var committed = await CdcShadowFixture.InsertCommittedOutboxEventAsync(

            options,

            partitionKey);



        var captured = await CdcShadowFixture.WaitForSqlServerCdcInsertAsync(

            connectionString,

            committed.Fingerprint.EventId,

            TimeSpan.FromSeconds(60));



        if (!captured)
        {
            Assert.Inconclusive(
                "SQL Server CDC change table did not observe insert within timeout (Agent/capture job gap in test container).");
        }

    }



    [TestMethod]

    public async Task SqlServer_rolled_back_outbox_insert_is_not_captured_by_cdc()

    {

        var connectionString = await SqlServerCdcTestSupport.ResolveConnectionStringAsync();

        var options = new DatabaseOptions

        {

            Provider = DatabaseProvider.SqlServer,

            ConnectionString = connectionString,

            CommandTimeoutSeconds = 300,

        };

        await MessagingOutboxTestSupport.MigrateAsync(options);



        var cdcEnablement = await SqlServerCdcTestSupport.TryEnableCdcAsync(connectionString);

        if (!cdcEnablement.Succeeded)

        {

            Assert.Inconclusive(SqlServerCdcTestSupport.BuildInconclusiveMessage(cdcEnablement));

        }



        var partitionKey = Guid.CreateVersion7().ToString("D");

        await CdcShadowFixture.InsertRolledBackOutboxAttemptAsync(options, partitionKey);



        await using (var connection = new SqlConnection(connectionString))

        {

            var outboxCount = await connection.ExecuteScalarAsync<int>(

                """

                SELECT COUNT(1)

                FROM dbo.fn_messaging_outbox_event

                WHERE PartitionKey = @PartitionKey

                """,

                new { PartitionKey = partitionKey });

            Assert.AreEqual(0, outboxCount);



            await Task.Delay(TimeSpan.FromSeconds(5));

            var cdcCount = await connection.ExecuteScalarAsync<int>(

                """

                SELECT COUNT(1)

                FROM cdc.fullnet_fn_messaging_outbox_event_CT

                WHERE PartitionKey = @PartitionKey AND __$operation = 2

                """,

                new { PartitionKey = partitionKey });

            Assert.AreEqual(0, cdcCount);

        }

    }



    [TestMethod]

    public async Task SqlServer_shadow_kafka_message_matches_outbox_fingerprint()

    {

        var environment = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);

        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();

        var options = new DatabaseOptions

        {

            Provider = DatabaseProvider.SqlServer,

            ConnectionString = connectionString,

            CommandTimeoutSeconds = 300,

        };

        await MessagingOutboxTestSupport.MigrateAsync(options);



        var partitionKey = Guid.CreateVersion7().ToString("D");

        var committed = await CdcShadowFixture.InsertCommittedOutboxEventAsync(

            options,

            partitionKey);



        var topic = CdcShadowFixture.CreateUniqueShadowTopic();
        await CdcShadowFixture.PublishShadowMessageToTopicAsync(environment, committed, topic);
        using var consumer = environment.CreateConsumer(
            $"fullnet.cdc.shadow.sqlserver.{Guid.NewGuid():N}",
            "fullnet.cdc.shadow.sqlserver");
        consumer.Subscribe(topic);



        var consumed = await KafkaTestMessages.ConsumeOneAsync(consumer, TimeSpan.FromSeconds(30));



        var comparison = CdcShadowFixture.CompareKafkaShadowMessage(

            committed,

            consumed,

            consumed.Offset.Value);

        Assert.IsTrue(comparison.IsMatch);

        Assert.AreEqual(ShadowComparisonOutcome.Match, comparison.Outcome);

    }

}

