using System.Data.Common;
using System.Text.Json;
using Dapper;
using DotNet.Testcontainers.Builders;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>使用已暂停的独占 Connector 真实坐标触发源窗口告警；旧发布者始终保持暂停。</summary>
internal static class MessagingCdcSourceAlertVerification
{
    internal static async Task VerifyAsync(DatabaseOptions database, DbConnection connection, CdcDebeziumPipelineEnvironment pipeline,
        string source, string connector, string topic, string group, KafkaMessagingOptions kafka,
        string eventType = "fullnet.organization.unit.changed")
    {
        var directory = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR")!;
        var root = Api.FullNetApiFactory.FindRepositoryRoot();
        using var connect = pipeline.CreateConnectAdminClient();
        Assert.IsTrue(await connect.IsConnectorPausedAsync(connector));
        await using var services = MessagingCdcHealthCollectionTests.Services(database);
        await using var exporter = await MessagingMetricsTestExporter.StartAsync();
        using var originalWorker = Worker(connector);
        await originalWorker.SampleAsync(default);
        var config = Path.Combine(directory, $"prometheus-source-{database.Provider}.yml");
        await File.WriteAllTextAsync(config, $"global:\n  scrape_interval: 2s\n  evaluation_interval: 2s\nrule_files:\n  - /etc/prometheus/fullnet-rules.yaml\nscrape_configs:\n  - job_name: fullnet-messaging-cdc\n    static_configs:\n      - targets: ['host.docker.internal:{exporter.Port}']\n");
        await using var prometheus = new ContainerBuilder("prom/prometheus:v3.15.0@sha256:efd719c99d83b060d9daefdcf00360461adf279f45ef5391f8d111892118753e")
            .WithExtraHost("host.docker.internal", "host-gateway").WithPortBinding(9090, true)
            .WithBindMount(config, "/etc/prometheus/prometheus.yml")
            .WithBindMount(Path.Combine(root, "deploy/observability/prometheus-rules.yaml"), "/etc/prometheus/fullnet-rules.yaml")
            .WithCreateParameterModifier(p => { var limits = p.HostConfig ??= new(); limits.Memory = 512L << 20; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000; })
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(9090).ForPath("/-/ready"))).Build();
        await prometheus.StartAsync();
        using var client = new HttpClient { BaseAddress = new($"http://127.0.0.1:{prometheus.GetMappedPublicPort(9090)}"), Timeout = TimeSpan.FromSeconds(5) };
        await WaitAsync(async () => { await originalWorker.SampleAsync(default); return await QueryAsync("fullnet_messaging_connector_offset_unrecoverable == 0"); }, "真实原 Connector 源坐标可读");
        var oldPosition = (await connect.TryReadConnectorPositionAsync(connector))!;
        if (database.Provider == DatabaseProvider.MySql)
        {
            await connection.ExecuteAsync("FLUSH BINARY LOGS");
            var currentFile = await connection.ExecuteScalarAsync<string>("SHOW MASTER STATUS");
            Assert.IsNotNull(currentFile);
            Assert.IsTrue(currentFile.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-'));
            await connection.ExecuteAsync($"PURGE BINARY LOGS TO '{currentFile}'");
        }
        else
        {
            var saved = Convert.FromHexString(oldPosition.Lsn!.CommitLsn.Replace(":", "", StringComparison.Ordinal));
            // 清理使用真实捕获行的后续提交 LSN；只有全库高水位推进但没有新 Outbox 行，不能保证该 capture instance 窗口移动。
            await WaitAsync(async () => await connection.ExecuteScalarAsync<int>("SELECT CASE WHEN MAX(__$start_lsn)>@Position THEN 1 ELSE 0 END FROM cdc.fullnet_fn_messaging_outbox_event_CT", new { Position = saved }) == 1,
                "清理前等待后续实际捕获行超过旧坐标");
            var current = await connection.ExecuteScalarAsync<byte[]>("SELECT MAX(__$start_lsn) FROM cdc.fullnet_fn_messaging_outbox_event_CT");
            await connection.ExecuteAsync("sys.sp_cdc_cleanup_change_table", new
            {
                capture_instance = "fullnet_fn_messaging_outbox_event", low_water_mark = current, threshold = 5000,
            }, commandType: System.Data.CommandType.StoredProcedure);
        }
        await originalWorker.StartAsync(default);
        await WaitAsync(() => QueryAsync("ALERTS{alertname=\"FullNetMessagingConnectorOffsetUnrecoverable\",alertstate=\"firing\"} == 1"),
            "真实旧源窗口丢失告警 firing", TimeSpan.FromMinutes(3));
        await SaveAsync("unrecoverable");
        await originalWorker.StopAsync(default);

        // 不 resume 旧 Connector；另建隔离 no_data 基线只验证观测恢复，不把它当成旧业务或回退操作的恢复。
        var replacement = connector + "-health-" + Guid.NewGuid().ToString("N")[..8];
        var replacementConfig = new Dictionary<string, string>((await connect.TryReadConnectorConfigurationAsync(connector))!)
        {
            // GET /config 含服务端补入的原名称；新基线必须显式使用新名称，不能把旧 name 提交给新 PUT 路径。
            ["name"] = replacement,
            ["database.password"] = SharedDatabaseFixture.MySqlRootPassword,
            ["schema.history.internal.kafka.topic"] = "fullnet.dev.shadow.internal.history." + replacement,
        };
        try
        {
            await connect.RegisterConnectorAsync(replacement, CdcDebeziumConnectorTestSupport.CreateIsolatedConnectorConfig(replacement, replacementConfig));
            Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(replacement, TimeSpan.FromSeconds(120)));
            using var recovered = Worker(replacement);
            await recovered.StartAsync(default);
            await WaitAsync(async () => await QueryAsync("fullnet_messaging_connector_offset_unrecoverable == 0")
                && !await QueryAsync("ALERTS{alertname=\"FullNetMessagingConnectorOffsetUnrecoverable\",alertstate=\"firing\"} == 1"),
                "新基线真实源坐标采集使告警恢复");
            Assert.IsTrue(await connect.IsConnectorPausedAsync(connector), "告警恢复不能偷偷恢复已退役的旧发布者。");
            await SaveAsync("recovered");
            await recovered.StopAsync(default);
        }
        finally { await connect.DeleteConnectorAsync(replacement); }

        KafkaCdcMonitoringWorker Worker(string name) => new(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new KafkaCdcMonitoringOptions { Enabled = true, PollSeconds = 10, SampleTimeoutSeconds = 30 }),
            Options.Create(new KafkaConnectRollbackOptions { Enabled = true, Streams = [new() { ConnectorName = name, SourceDatabaseName = source,
                EventType = eventType, SchemaVersion = 1, TopicName = topic, ConsumerGroupId = group }] }),
            Options.Create(kafka), connect, new KafkaConsumerLagObserver());
        async Task<bool> QueryAsync(string query)
        {
            using var response = await client.GetAsync("/api/v1/query?query=" + Uri.EscapeDataString(query));
            response.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return body.RootElement.GetProperty("data").GetProperty("result").GetArrayLength() > 0;
        }
        async Task SaveAsync(string phase)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, $"source-alert-{database.Provider}-{phase}.json"), await client.GetStringAsync("/api/v1/alerts"));
            await File.WriteAllTextAsync(Path.Combine(directory, $"source-metric-{database.Provider}-{phase}.json"),
                await client.GetStringAsync("/api/v1/query?query=fullnet_messaging_connector_offset_unrecoverable"));
        }
    }

    private static async Task WaitAsync(Func<Task<bool>> action, string message, TimeSpan? timeout = null)
    {
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(2));
        try { while (!await action()) await Task.Delay(1000, deadline.Token); }
        catch (OperationCanceledException) { Assert.Fail("等待失败：" + message); }
    }
}
