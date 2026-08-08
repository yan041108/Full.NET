using Full.NET.Data.Abstractions;

using Full.NET.IntegrationTests.Migrations;

using Full.NET.Messaging.Abstractions;



namespace Full.NET.IntegrationTests.Messaging;



[TestClass]
[DoNotParallelize]
public sealed class MySqlBinlogShadowTests

{

    [TestMethod]

    public async Task MySql_binlog_prerequisites_are_row_full_when_available()

    {

        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();

        var status = await CdcShadowFixture.ReadMySqlBinlogStatusAsync(connectionString);

        if (!status.IsRowFullEnabled)

        {

            Assert.Inconclusive(

                "MySQL test container does not expose ROW/FULL binlog; record environment gap for CDC shadow.");

        }



        Assert.AreEqual("ON", status.LogBin, StringComparer.OrdinalIgnoreCase);

        Assert.AreEqual("ROW", status.BinlogFormat, StringComparer.OrdinalIgnoreCase);

        Assert.AreEqual("FULL", status.BinlogRowImage, StringComparer.OrdinalIgnoreCase);

    }



    [TestMethod]

    public async Task MySql_shadow_kafka_message_matches_outbox_fingerprint()

    {

        var environment = await KafkaFixture.GetOrStartAsync().ConfigureAwait(false);

        var connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();

        var options = new DatabaseOptions

        {

            Provider = DatabaseProvider.MySql,

            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
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
            $"fullnet.cdc.shadow.mysql.{Guid.NewGuid():N}",
            "fullnet.cdc.shadow.mysql");
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

