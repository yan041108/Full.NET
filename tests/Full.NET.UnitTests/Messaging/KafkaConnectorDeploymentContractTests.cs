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
        var requiredKeys = new List<string> { "value.converter", "value.converter.delegate.converter.type", "value.converter.delegate.converter.type.schemas.enable", "topic.heartbeat.prefix", "heartbeat.interval.ms" };
        if (provider == "sqlserver") requiredKeys.Add("heartbeat.action.query");
        if (provider == "mysql")
        {
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
        config["heartbeat.interval.ms"] = "0";
        Assert.IsFalse(profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding)
            : KafkaConnectResourceIdentity.IsTargetRoute(config, binding));
        config["heartbeat.interval.ms"] = "1000";
        if (provider == "sqlserver")
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
        var withoutTolerance = KafkaConnectResourceIdentity.Compute(config);
        config["errors.tolerance"] = "all";
        Assert.AreNotEqual(withoutTolerance, KafkaConnectResourceIdentity.Compute(config));
        Assert.IsFalse(profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding)
            : KafkaConnectResourceIdentity.IsTargetRoute(config, binding), "不能把被 Connect 容错丢弃的记录当作已投递。");
        config.Remove("errors.tolerance");
        var withoutSkippedOperations = KafkaConnectResourceIdentity.Compute(config);
        config["skipped.operations"] = "c";
        Assert.AreNotEqual(withoutSkippedOperations, KafkaConnectResourceIdentity.Compute(config));
        Assert.IsFalse(profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding)
            : KafkaConnectResourceIdentity.IsTargetRoute(config, binding), "不能跳过正式 Outbox 插入。");
        config.Remove("skipped.operations");
        var withoutFailureMode = KafkaConnectResourceIdentity.Compute(config);
        config["event.processing.failure.handling.mode"] = "warn";
        Assert.AreNotEqual(withoutFailureMode, KafkaConnectResourceIdentity.Compute(config));
        Assert.IsFalse(profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding)
            : KafkaConnectResourceIdentity.IsTargetRoute(config, binding), "不能跳过 Debezium 处理失败的事件。");
        config.Remove("event.processing.failure.handling.mode");
        config["heartbeat.action.query"] = "SELECT 1";
        Assert.IsFalse(profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding)
            : KafkaConnectResourceIdentity.IsTargetRoute(config, binding));
    }

    [TestMethod]
    [DataRow("sqlserver", "shadow")]
    [DataRow("sqlserver", "capacity")]
    public void Sql_server_dedicated_route_requires_exact_autonomous_heartbeat(string provider, string profile)
    {
        var (config, binding) = Read(provider, profile);
        bool Valid() => profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding) : KafkaConnectResourceIdentity.IsTargetRoute(config, binding);
        Assert.IsTrue(Valid());
        var fingerprint = KafkaConnectResourceIdentity.Compute(config);
        config.Remove("heartbeat.action.query");
        Assert.IsFalse(Valid(), "缺少受控心跳不能保证空闲位点推进。");
        Assert.AreNotEqual(fingerprint, KafkaConnectResourceIdentity.Compute(config));
        config["heartbeat.action.query"] = "UPDATE dbo.fn_messaging_cdc_heartbeat SET UpdatedAtUtc = SYSUTCDATETIME()";
        Assert.IsFalse(Valid(), "不能接受其它 SQL 或更新范围。");
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

    [TestMethod]
    [DataRow("mysql", "shadow")]
    [DataRow("mysql", "capacity")]
    [DataRow("sqlserver", "shadow")]
    [DataRow("sqlserver", "capacity")]
    public void Dedicated_stream_filter_retains_its_heartbeat_and_rejects_route_or_identity_drift(string provider, string profile)
    {
        var (config, binding) = Read(provider, profile);
        binding.EventType = "fullnet.messaging.orders.changed"; binding.SchemaVersion = 1;
        binding.TopicName = "messaging.orders.changed.v1";
        binding.ShadowTopicName = "fullnet.dev.shadow." + binding.TopicName;
        config["topic.prefix"] = "source.connector";
        var shadow = profile == "shadow";
        var topic = shadow ? binding.ShadowTopicName : binding.TopicName;
        var heartbeat = config["topic.heartbeat.prefix"] + "." + config["topic.prefix"];
        config["transforms"] = "fullnet_filter,outbox,select";
        config["transforms.fullnet_filter.event.type"] = binding.EventType;
        config["transforms.fullnet_filter.source.topic"] = "source.connector.test_source" + (provider == "sqlserver" ? ".dbo." : ".")
            + (shadow ? "fn_messaging_shadow_event" : "fn_messaging_outbox_event");
        config["transforms.outbox.route.topic.regex"] = @"^fullnet\.(.*)$";
        config["transforms.outbox.route.topic.replacement"] = (shadow ? "fullnet.dev.shadow." : "") + "$1.v1";
        config["predicates"] = "selected";
        config["predicates.selected.type"] = "org.apache.kafka.connect.transforms.predicates.TopicNameMatches";
        config["predicates.selected.pattern"] = "^(?:" + System.Text.RegularExpressions.Regex.Escape(topic) + "|" + System.Text.RegularExpressions.Regex.Escape(heartbeat) + ")$";
        config["transforms.select.type"] = "org.apache.kafka.connect.transforms.Filter";
        config["transforms.select.predicate"] = "selected";
        config["transforms.select.negate"] = "true";
        bool Valid() => shadow ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding) : KafkaConnectResourceIdentity.IsTargetRoute(config, binding);
        Assert.IsTrue(Valid());
        config["transforms"] = "outbox,select";
        Assert.IsFalse(Valid(), "按 Topic 名过滤无法隔离同类型的其它版本。");
        config["transforms"] = "outbox";
        Assert.IsFalse(Valid(), "固定 Topic 不能把共享表的其它事件导入目标流。");
        config["transforms"] = "fullnet_filter,outbox,select";
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(topic, config["predicates.selected.pattern"]));
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(heartbeat, config["predicates.selected.pattern"]));
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(topic + ".other", config["predicates.selected.pattern"]));
        var fingerprint = KafkaConnectResourceIdentity.Compute(config);
        foreach (var (key, changed) in new Dictionary<string, string>
        {
            ["transforms"] = "select,outbox", ["transforms.outbox.route.topic.regex"] = ".*",
            ["transforms.outbox.route.topic.replacement"] = "unrelated.v1",
            ["predicates"] = "other", ["predicates.selected.type"] = "other",
            ["predicates.selected.pattern"] = "^" + System.Text.RegularExpressions.Regex.Escape(topic) + "$",
            ["transforms.select.type"] = "other", ["transforms.select.predicate"] = "other",
            ["transforms.select.negate"] = "false", ["transforms.outbox.predicate"] = "selected",
            ["transforms.fullnet_filter.event.type"] = "fullnet.other.changed",
            ["transforms.fullnet_filter.schema.version"] = "2", ["transforms.fullnet_filter.source.topic"] = "unrelated.source",
            ["transforms.fullnet_filter.predicate"] = "selected",
        })
        {
            var present = config.TryGetValue(key, out var original);
            config[key] = changed;
            Assert.IsFalse(Valid(), key);
            Assert.AreNotEqual(fingerprint, KafkaConnectResourceIdentity.Compute(config), key);
            if (present) config[key] = original!; else config.Remove(key);
        }
        binding.EventType = "fullnet.messaging.other.changed"; Assert.IsFalse(Valid());
        binding.EventType = "fullnet.messaging.orders.changed"; binding.SchemaVersion = 2; Assert.IsFalse(Valid());
    }

    [TestMethod]
    [DataRow("mysql", "shadow")]
    [DataRow("mysql", "capacity")]
    [DataRow("sqlserver", "shadow")]
    [DataRow("sqlserver", "capacity")]
    public void Fixed_topic_route_rejects_regex_that_can_skip_the_replacement(string provider, string profile)
    {
        var (config, binding) = Read(provider, profile);
        bool Valid() => profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding) : KafkaConnectResourceIdentity.IsTargetRoute(config, binding);
        Assert.IsTrue(Valid(), "未配置正则时使用 EventRouter 默认匹配。");
        foreach (var regex in new[] { "(?<routedByValue>.*)", ".*" })
        {
            config["transforms.outbox.route.topic.regex"] = regex;
            Assert.IsTrue(Valid(), regex);
        }
        foreach (var regex in new[] { "^unrelated$", "", "(?s).*" })
        {
            config["transforms.outbox.route.topic.regex"] = regex;
            Assert.IsFalse(Valid(), regex);
        }
    }

    [TestMethod]
    [DataRow("mysql", "shadow")]
    [DataRow("mysql", "capacity")]
    [DataRow("sqlserver", "shadow")]
    [DataRow("sqlserver", "capacity")]
    public void Dedicated_route_rejects_duplicate_headers_and_payload_wrapping(string provider, string profile)
    {
        var (config, binding) = Read(provider, profile);
        bool Valid() => profile == "shadow" ? KafkaConnectResourceIdentity.IsShadowRoute(config, binding) : KafkaConnectResourceIdentity.IsTargetRoute(config, binding);
        Assert.IsTrue(Valid());
        var key = "transforms.outbox.table.fields.additional.placement";
        var original = config[key];
        foreach (var extra in new[] { "CausationId:header:event_id", "Id:header:event_id", "MessageType:envelope:extra" })
        {
            config[key] = extra + "," + original;
            Assert.IsFalse(Valid(), extra);
        }
        config[key] = string.Join(',', original.Split(',').Reverse());
        Assert.IsTrue(Valid(), "精确映射不依赖配置顺序。");
    }

    [TestMethod]
    public void Active_stream_bindings_allow_shared_group_but_reject_overlapping_or_unproven_resources()
    {
        static Full.NET.Messaging.Abstractions.CdcKafkaTopicBinding Topic(string name, string id) => new(name, id, [new(0, 0, 1)]);
        var left = new Full.NET.Messaging.Abstractions.CdcKafkaDeliveryBinding("cluster", "left", "hash", "left.v1", ["shared"], ["5s"], [Topic("left.v1", "left-id"), Topic("left.v1.retry.5s", "retry-id")])
            { ActivationId = Guid.CreateVersion7(), ConnectBaseUri = "http://connect:8083/", EnvironmentName = "Testing", ShadowConnectorName = "left-shadow", ShadowTopicName = "left.shadow.v1" };
        var right = left with { ActivationId = Guid.CreateVersion7(), ConnectorName = "right", TopicName = "right.v1", Topics = [Topic("right.v1", "right-id")], ShadowConnectorName = "right-shadow", ShadowTopicName = "right.shadow.v1" };
        Assert.IsTrue(left.IsIsolatedFrom(right));
        Assert.IsTrue(right.IsIsolatedFrom(left));
        foreach (var collision in new[] { right with { ConnectorName = left.ConnectorName },
            right with { Topics = [Topic("left.v1", "new-id")] }, right with { Topics = [Topic("right.v1", "retry-id")] },
            right with { ConnectorName = "left-shadow" }, right with { ShadowConnectorName = "left" },
            right with { ShadowTopicName = "left.v1.retry.5s" }, right with { Topics = [Topic("left.shadow.v1", "new-id")] },
            right with { ShadowConnectorName = null }, right with { ShadowTopicName = null },
            right with { ActivationId = Guid.Empty }, right with { ConnectBaseUri = null },
            right with { EnvironmentName = "Production" }, right with { KafkaClusterId = "other" } })
            Assert.IsFalse(left.IsIsolatedFrom(collision));
    }

    [TestMethod]
    public void Stream_configuration_rejects_connector_and_topic_aliases_across_formal_and_shadow_resources()
    {
        var first = new KafkaConnectRollbackStreamBinding { EventType = "fullnet.contract.first.changed", SchemaVersion = 1, ConnectorName = "first", ShadowConnectorName = "first-shadow", TopicName = "first.v1", ShadowTopicName = "first.shadow.v1", ConsumerGroupId = "shared" };
        var second = new KafkaConnectRollbackStreamBinding { EventType = "fullnet.contract.second.changed", SchemaVersion = 1, ConnectorName = "second", ShadowConnectorName = "second-shadow", TopicName = "second.v1", ShadowTopicName = "second.shadow.v1", ConsumerGroupId = "shared" };
        var options = new KafkaConnectRollbackOptions { Enabled = true, ConnectBaseUri = "http://connect:8083", Streams = [first, second] };
        var validator = new KafkaConnectRollbackOptionsValidator();
        Assert.IsTrue(validator.Validate(null, options).Succeeded);
        second.ConnectorName = first.ShadowConnectorName; Assert.IsTrue(validator.Validate(null, options).Failed);
        second.ConnectorName = "second"; second.ShadowTopicName = first.TopicName; Assert.IsTrue(validator.Validate(null, options).Failed);
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
            EventType = "fullnet.messaging.orders", SchemaVersion = 1,
            SourceDatabaseName = "test_source", TopicName = "messaging.orders.v1", ShadowTopicName = "fullnet.shadow.orders.v1",
        };
        config[provider == "sqlserver" ? "database.names" : "database.include.list"] = binding.SourceDatabaseName;
        if (provider == "mysql") config["table.include.list"] = config["table.include.list"].Replace("${FULLNET_MYSQL_DATABASE}", binding.SourceDatabaseName, StringComparison.Ordinal);
        config["transforms"] = "fullnet_filter,outbox";
        config["transforms.fullnet_filter.type"] = "net.full.connect.FullNetOutboxStreamFilter";
        config["transforms.fullnet_filter.event.type"] = binding.EventType;
        config["transforms.fullnet_filter.schema.version"] = "1";
        config["transforms.fullnet_filter.source.topic"] = config["topic.prefix"] + ".test_source" + (provider == "sqlserver" ? ".dbo." : ".")
            + (profile == "shadow" ? "fn_messaging_shadow_event" : "fn_messaging_outbox_event");
        config["transforms.outbox.route.topic.replacement"] = profile == "shadow" ? binding.ShadowTopicName : binding.TopicName;
        return (config, binding);
    }
}
