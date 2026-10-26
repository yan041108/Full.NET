using System.Text.Json;
using Full.NET.Messaging.Kafka;

namespace Full.NET.UnitTests.Messaging;

/// <summary>真实部署模板必须能满足生产资源校验，而不只是通过测试手写配置。</summary>
[TestClass]
public sealed class KafkaConnectorDeploymentContractTests
{
    [TestMethod]
    [DataRow("mysql", "shadow")]
    [DataRow("mysql", "capacity")]
    [DataRow("sqlserver", "shadow")]
    [DataRow("sqlserver", "capacity")]
    public void Deployed_connector_uses_raw_binary_transport_and_the_authoritative_route(string provider, string profile)
    {
        var (config, binding) = Read(provider, profile);
        Assert.AreEqual("io.debezium.converters.BinaryDataConverter", config.GetValueOrDefault("value.converter"),
            "MemoryPack 原始字节不能依赖 Connect Worker 的 JSON 默认值。");
        Assert.AreEqual("org.apache.kafka.connect.json.JsonConverter", config.GetValueOrDefault("value.converter.delegate.converter.type"));
        Assert.AreEqual("false", config.GetValueOrDefault("value.converter.delegate.converter.type.schemas.enable"));
        Assert.IsTrue(profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding)
            : KafkaConnectResourceIdentity.IsTargetRoute(config, binding));
        // 非二进制元数据也必须有受控编码，不能直到真实 Connector 启动后才失败。
        var requiredKeys = new List<string> { "value.converter", "value.converter.delegate.converter.type", "value.converter.delegate.converter.type.schemas.enable", "topic.heartbeat.prefix" };
        if (provider == "mysql")
        {
            requiredKeys.Add("heartbeat.interval.ms");
            if (profile == "shadow") requiredKeys.Add("transforms.outbox.table.field.event.timestamp");
        }
        foreach (var key in requiredKeys)
        {
            var original = config[key];
            var fingerprint = KafkaConnectResourceIdentity.Compute(config);
            config.Remove(key);
            Assert.IsFalse(profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding)
                : KafkaConnectResourceIdentity.IsTargetRoute(config, binding), key);
            Assert.AreNotEqual(fingerprint, KafkaConnectResourceIdentity.Compute(config), key);
            config[key] = original;
        }
        if (provider == "mysql")
        {
            config["heartbeat.interval.ms"] = "0";
            Assert.IsFalse(profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding)
                : KafkaConnectResourceIdentity.IsTargetRoute(config, binding));
            config["heartbeat.interval.ms"] = "1000";
        }
        else
        {
            config["transforms.outbox.table.field.event.timestamp"] = "OccurredAtUtc";
            Assert.IsFalse(profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding)
                : KafkaConnectResourceIdentity.IsTargetRoute(config, binding), "SQL Server 正式及影子路由都不能把 ZonedTimestamp 当作整数时间。");
            config.Remove("transforms.outbox.table.field.event.timestamp");
        }
        config["value.converter"] = "org.apache.kafka.connect.converters.ByteArrayConverter";
        Assert.IsFalse(profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding)
            : KafkaConnectResourceIdentity.IsTargetRoute(config, binding));
        config["value.converter"] = "io.debezium.converters.BinaryDataConverter";
        config["heartbeat.topics.prefix"] = config["topic.heartbeat.prefix"];
        Assert.IsFalse(profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding)
            : KafkaConnectResourceIdentity.IsTargetRoute(config, binding));
        config.Remove("heartbeat.topics.prefix");
        config["heartbeat.action.query"] = "SELECT 1";
        Assert.IsFalse(profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding)
            : KafkaConnectResourceIdentity.IsTargetRoute(config, binding));
    }

    [TestMethod]
    [DataRow("mysql", false)]
    [DataRow("mysql", true)]
    [DataRow("sqlserver", false)]
    [DataRow("sqlserver", true)]
    public void Shadow_timestamp_validation_matches_the_provider_wire_type(string provider, bool wrongTimestamp)
    {
        var (config, binding) = Read(provider, "shadow");
        // 独立隔离时标校验，避免转换器缺失掩盖 SQL Server 的 ZonedTimestamp 拒绝错误。
        config["value.converter"] = "io.debezium.converters.BinaryDataConverter";
        config["value.converter.delegate.converter.type"] = "org.apache.kafka.connect.json.JsonConverter";
        config["value.converter.delegate.converter.type.schemas.enable"] = "false";
        if (wrongTimestamp)
        {
            if (provider == "sqlserver") config["transforms.outbox.table.field.event.timestamp"] = "OccurredAtUtc";
            else config.Remove("transforms.outbox.table.field.event.timestamp");
        }
        Assert.AreEqual(!wrongTimestamp, KafkaConnectResourceIdentity.IsShadowRoute(config, binding));
    }

    private static (Dictionary<string, string> Config, KafkaConnectRollbackStreamBinding Binding) Read(string provider, string profile)
    {
        var origin = Environment.GetEnvironmentVariable("FULLNET_TEST_REPOSITORY_ROOT") ?? Directory.GetCurrentDirectory();
        var directory = new DirectoryInfo(origin);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx"))) directory = directory.Parent;
        Assert.IsNotNull(directory);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName,
            $"deploy/messaging/connectors/{provider}-outbox-{profile}.json")));
        var config = document.RootElement.GetProperty("config").EnumerateObject()
            .ToDictionary(item => item.Name, item => item.Value.GetString()!, StringComparer.Ordinal);
        var binding = new KafkaConnectRollbackStreamBinding
        {
            SourceDatabaseName = "test_source", TopicName = "messaging.orders.v1", ShadowTopicName = "fullnet.shadow.orders.v1",
        };
        config[provider == "sqlserver" ? "database.names" : "database.include.list"] = binding.SourceDatabaseName;
        if (provider == "mysql") config["table.include.list"] = config["table.include.list"].Replace("${FULLNET_MYSQL_DATABASE}", binding.SourceDatabaseName, StringComparison.Ordinal);
        config["transforms.outbox.route.topic.replacement"] = profile == "shadow" ? binding.ShadowTopicName : binding.TopicName;
        return (config, binding);
    }
}
