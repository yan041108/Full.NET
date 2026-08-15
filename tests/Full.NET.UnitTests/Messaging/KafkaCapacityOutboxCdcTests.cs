using Full.NET.Benchmarks.Kafka;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class KafkaCapacityOutboxCdcTests
{
    [TestMethod]
    public void Scope_code_transaction_outbox_cdc_is_valid()
    {
        KafkaCapacityScopeCodes.Validate(KafkaCapacityScopeCodes.TransactionOutboxCdc);
    }

    [TestMethod]
    public void Event_id_factory_is_deterministic_for_same_inputs()
    {
        const uint runHash = 0x12345678;
        const uint sampleHash = 0xABCDEF01;
        const long sequence = 42;
        var first = KafkaCapacityEventIdFactory.Create(runHash, sampleHash, sequence);
        var second = KafkaCapacityEventIdFactory.Create(runHash, sampleHash, sequence);
        Assert.AreEqual(first, second);
        Assert.AreNotEqual(
            KafkaCapacityEventIdFactory.Create(runHash, sampleHash, sequence + 1),
            first);
    }

    [TestMethod]
    public void Connector_template_resolves_capacity_cdc_topic_name()
    {
        var topic = KafkaCapacityConnectorTemplateFactory.ResolveCdcTopicName(
            KafkaCapacityWorkerContracts.EventType);
        Assert.AreEqual(
            "fullnet.capacity.cdc.fullnet.capacity.worker.message",
            topic);
    }

    [TestMethod]
    public async Task Worker_parity_mode_skips_fast_only_overrides_for_scope_C()
    {
        var configuration = new KafkaCapacityConfiguration
        {
            HostParityMode = KafkaCapacityHostParityMode.WorkerParity,
            Database = new KafkaCapacityDatabaseConfiguration
            {
                Provider = DatabaseProvider.MySql,
                ConnectionString = "Server=127.0.0.1;Database=capacity;User=root;Password=x;",
                ExpectedDatabaseName = "capacity",
            },
            Kafka = new KafkaMessagingOptions { Enabled = true, BootstrapServers = "127.0.0.1:9092" },
        };
        var provider = KafkaCapacityServiceFactory.BuildOutboxCdcServices(
            configuration,
            new KafkaCapacityWorkerObserver(1));
        await using var scope = provider.CreateAsyncScope();
        Assert.IsFalse(
            scope.ServiceProvider.GetServices<IEventStreamOwnershipGate>()
                .Any(gate => gate.GetType().Name.Contains("Permissive", StringComparison.Ordinal)));
        await provider.DisposeAsync();
    }

    [TestMethod]
    public void Configuration_loads_connect_overrides_without_disclosing_endpoint()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["KafkaCapacity__Connect__BaseUri"] = "http://connect:8083/",
            ["KafkaCapacity__Connect__ConnectorNamePrefix"] = "fullnet-capacity-it",
            ["KafkaCapacity__Connect__InternalKafkaBootstrapServers"] = "kafka:9092",
        };

        var configuration = KafkaCapacityConfiguration.Load(
            KafkaCapacityOptions.Parse([
                "--scope", KafkaCapacityScopeCodes.TransactionOutboxCdc,
            ]),
            environment.GetValueOrDefault);

        Assert.AreEqual("http://connect:8083/", configuration.Connect.BaseUri);
        Assert.AreEqual("fullnet-capacity-it", configuration.Connect.ConnectorNamePrefix);
        Assert.AreEqual("kafka:9092", configuration.Connect.InternalKafkaBootstrapServers);
        Assert.IsFalse(configuration.ToString().Contains("connect:8083", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Configuration_loads_host_parity_mode_from_environment()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["KafkaCapacity__HostParityMode"] = "WorkerParity",
        };

        var configuration = KafkaCapacityConfiguration.Load(
            KafkaCapacityOptions.Parse(["--scope", KafkaCapacityScopeCodes.WorkerInboxHandler]),
            environment.GetValueOrDefault);

        Assert.AreEqual(
            KafkaCapacityHostParityMode.WorkerParity,
            configuration.HostParityMode);
    }

    [TestMethod]
    public async Task Database_preflight_requires_outbox_table_for_scope_C()
    {
        var preflight = new KafkaCapacityDatabasePreflight(
            new KafkaCapacityDatabaseConfiguration
            {
                Provider = DatabaseProvider.SqlServer,
                ConnectionString = "Server=.;Database=missing;",
                ExpectedDatabaseName = "missing",
                CommandTimeoutSeconds = 1,
            },
            requireOutboxTable: true);

        var failure = await Assert.ThrowsExactlyAsync<KafkaCapacityControlPlaneException>(() =>
            preflight.ValidateAsync(CancellationToken.None));
        Assert.AreEqual("database_preflight_failed", failure.ReasonCode);
    }

    [TestMethod]
    public async Task Connect_preflight_rejects_invalid_configuration()
    {
        var preflight = new KafkaCapacityConnectPreflight(new KafkaCapacityConnectConfiguration());
        var failure = await Assert.ThrowsExactlyAsync<KafkaCapacityControlPlaneException>(() =>
            preflight.ValidateAsync(CancellationToken.None));
        Assert.AreEqual("connect_configuration_invalid", failure.ReasonCode);
    }

    [TestMethod]
    public void Driver_registry_registers_scope_C_factory()
    {
        var registry = KafkaCapacityDriverRegistry.CreateDefault();
        var factory = registry.GetRequired(KafkaCapacityScopeCodes.TransactionOutboxCdc);
        Assert.AreEqual(KafkaCapacityScopeCodes.TransactionOutboxCdc, factory.ScopeCode);
        Assert.IsFalse(KafkaCapacityDriverRegistry.UsesRunnerOwnedTopic(factory.ScopeCode));
    }

    [TestMethod]
    public void Raw_outbox_serializer_uses_messagepack_content_type_for_envelope_contract()
    {
        var serializer = new KafkaCapacityRawIntegrationEventSerializer();
        Assert.AreEqual(MessagingNames.ContentTypeMessagePack, serializer.ContentType);
    }

    [TestMethod]
    public void Envelope_payload_decoder_reads_connect_json_base64_wrapper()
    {
        const uint runHash = 0xA1B2C3D4;
        const uint sampleHash = 0x11223344;
        var payload = KafkaCapacityEnvelopeCodec.Encode(
            128,
            runHash,
            sampleHash,
            42,
            7,
            1000,
            2000);
        var wrapped = JsonSerializer.SerializeToUtf8Bytes(payload);
        Assert.IsTrue(
            KafkaCapacityEnvelopePayloadDecoder.TryDecode(wrapped, out var envelope));
        Assert.AreEqual(42, envelope.GlobalSequence);
        Assert.AreEqual(runHash, envelope.RunHash);
    }

    [TestMethod]
    public async Task Connector_template_loads_mysql_capacity_config()
    {
        var config = await KafkaCapacityConnectorTemplateFactory.CreateConfigAsync(
            DatabaseProvider.MySql,
            "Server=127.0.0.1;Port=3306;User ID=root;Password=secret;Database=fullnet_capacity",
            new KafkaCapacityConnectConfiguration
            {
                DatabaseHostGateway = "host.docker.internal",
                MySqlConnectorUser = "root",
                MySqlConnectorPassword = "secret",
            },
            "kafka:9092");

        Assert.AreEqual(
            "fullnet.capacity.cdc.${routedByValue}",
            config["transforms.outbox.route.topic.replacement"]);
        StringAssert.Contains(config["table.include.list"], "fn_messaging_outbox_event");
    }
}
