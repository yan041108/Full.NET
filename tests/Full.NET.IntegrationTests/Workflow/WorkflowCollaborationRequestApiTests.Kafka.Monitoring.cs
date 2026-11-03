using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Full.NET.IntegrationTests.Messaging;
using Full.NET.Messaging.Kafka;
using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    private static async Task WaitNativeConnectorBootstrapAsync(Uri address, KafkaConnectAdminClient connect, string name, string directory)
    {
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(10) };
        var restarted = false;
        await WaitKafkaAsync(async () =>
        {
            var status = await connect.TryGetConnectorStatusAsync(name);
            if (status is null) return false;
            using var document = JsonDocument.Parse(status);
            var root = document.RootElement;
            Assert.AreNotEqual("FAILED", root.GetProperty("connector").GetProperty("state").GetString(), "初始Connector失败。");
            var tasks = root.GetProperty("tasks").EnumerateArray().ToArray();
            var failed = tasks.Where(task => task.GetProperty("state").GetString() == "FAILED").ToArray();
            if (failed.Length > 0)
            {
                // 仅恢复已实际遇到的SchemaHistory关闭竞态；不重试业务、权限、数据库或未知启动错误。
                Assert.IsFalse(restarted, "同一Connector的启动故障重现，不能反复重启掩盖失败。");
                foreach (var task in failed)
                {
                    var trace = task.GetProperty("trace").GetString() ?? "";
                    Assert.IsTrue(trace.Contains("java.util.concurrent.RejectedExecutionException", StringComparison.Ordinal)
                        && trace.Contains("io.debezium.storage.kafka.history.KafkaSchemaHistory", StringComparison.Ordinal)
                        && trace.Contains("[Terminated, pool size = 0", StringComparison.Ordinal), "未知任务启动失败，不允许自动恢复。");
                }
                await File.WriteAllTextAsync(Path.Combine(directory, name + "-bootstrap-failure.json"), status);
                using var response = await client.PostAsync($"/connectors/{Uri.EscapeDataString(name)}/restart?includeTasks=true&onlyFailed=true", null);
                Assert.AreEqual(System.Net.HttpStatusCode.Accepted, response.StatusCode, "真实Connect必须接受一次失败任务重启。");
                restarted = true;
                Console.WriteLine("Native Kafka bootstrap: explicit one-time SchemaHistory recovery " + name);
                return false;
            }
            return tasks.Length > 0 && tasks.All(task => task.GetProperty("state").GetString() == "RUNNING")
                && await connect.TryReadConnectorPositionAsync(name) is not null;
        }, "Native Connector启动健康及真实源位置：" + name);
    }

    /// <summary>只接收真实Native Worker的OTLP；使用现有生产告警规则，不注入或改写指标。</summary>
    private sealed class NativeKafkaMonitor(IContainer host, HttpClient client, string data) : IAsyncDisposable
    {
        private const string Image = "prom/prometheus:v3.15.0@sha256:efd719c99d83b060d9daefdcf00360461adf279f45ef5391f8d111892118753e";

        internal IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
        {
            ["Messaging__CdcMonitoring__Enabled"] = "true", ["Messaging__CdcMonitoring__PollSeconds"] = "10",
            ["Messaging__CdcMonitoring__SampleTimeoutSeconds"] = "30", ["Messaging__CdcMonitoring__MaximumSampleAgeSeconds"] = "120",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = $"http://host.docker.internal:{host.GetMappedPublicPort(9090)}/api/v1/otlp",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf", ["OTEL_METRIC_EXPORT_INTERVAL"] = "1000",
            ["OTEL_TRACES_EXPORTER"] = "none", ["OTEL_LOGS_EXPORTER"] = "none",
        };

        internal static async Task<NativeKafkaMonitor> StartAsync(string directory)
        {
            Directory.CreateDirectory(directory);
            var config = Path.Combine(directory, "prometheus.yml");
            await File.WriteAllTextAsync(config, "global:\n  evaluation_interval: 2s\nrule_files:\n  - /etc/prometheus/fullnet-rules.yaml\n");
            var host = new ContainerBuilder(Image)
                .WithLabel("fullnet.workflow.kafka.monitor", "prometheus")
                .WithPortBinding(9090, true).WithExtraHost("host.docker.internal", "host-gateway")
                .WithBindMount(config, "/etc/prometheus/prometheus.yml")
                .WithBindMount(Path.Combine(Api.FullNetApiFactory.FindRepositoryRoot(), "deploy/observability/prometheus-rules.yaml"), "/etc/prometheus/fullnet-rules.yaml")
                .WithCommand("--config.file=/etc/prometheus/prometheus.yml", "--storage.tsdb.path=/prometheus", "--web.enable-otlp-receiver")
                .WithCreateParameterModifier(parameters =>
                {
                    SharedDatabaseFixture.ApplyBoundedOwnership(parameters.Labels ??= new Dictionary<string, string>());
                    var limits = parameters.HostConfig ??= new(); limits.Memory = 512L << 20; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000;
                })
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(9090).ForPath("/-/ready"))).Build();
            try
            {
                await host.StartAsync();
                return new(host, new HttpClient { BaseAddress = new($"http://127.0.0.1:{host.GetMappedPublicPort(9090)}"), Timeout = TimeSpan.FromSeconds(10) }, directory);
            }
            catch { await host.DisposeAsync(); throw; }
        }

        internal async Task VerifyAsync(KafkaConnectAdminClient connect, string connector, string[] topics, IDatabaseSessionLockLease controlLock)
        {
            // 只选择仍在刷新自身采集时间的Native进程；已停止实例的OTLP缓存不能算当前健康。
            const string current = " and on(job,instance) (time() - fullnet_messaging_observation_timestamp_seconds{component=\"collector\",resource_code=\"cdc\"} < 30)";
            await WaitAsync(async () => await CountAsync("fullnet_messaging_connector_running == 1" + current) == topics.Length, "Native四流连接器真实指标");
            await WaitAsync(async () => await CountAsync("fullnet_messaging_connector_offset_unrecoverable == 0" + current) == topics.Length, "Native四流源位置实际可恢复");
            Assert.AreEqual(1, await CountAsync("fullnet_messaging_kafka_observed_topics_lag{consumer_code=\"fullnet.workflow.collaboration\"} == 0" + current), "共享消费组必须只有映射正式流汇总，不能由最后一条流覆盖。");
            try
            {
                await controlLock.EnsureHeldAsync();
                await connect.PauseConnectorAsync(connector);
                await WaitAsync(() => connect.IsConnectorPausedAsync(connector), "实际暂停一个独占Connector");
                await WaitAsync(async () => await CountAsync("fullnet_messaging_connector_running == 1" + current) == topics.Length - 1, "单流失败不应隐藏其它流健康指标");
                await WaitAsync(async () => await CountAsync("ALERTS{alertname=\"FullNetMessagingConnectorNotRunning\",alertstate=\"firing\",resource_code=\"" + topics[2] + "\"}") == 1,
                    "原生单流故障触发真实生产告警", 210);
                Assert.AreEqual(0, await CountAsync("ALERTS{alertname=\"FullNetMessagingConnectorNotRunning\",alertstate=\"firing\",resource_code!=\"" + topics[2] + "\"}"));
                await controlLock.EnsureHeldAsync();
                await File.WriteAllTextAsync(Path.Combine(data, "alerts-firing.json"), await client.GetStringAsync("/api/v1/alerts"));
            }
            finally
            {
                await controlLock.EnsureHeldAsync();
                await connect.ResumeConnectorAsync(connector);
                Assert.IsTrue(await connect.WaitForConnectorHealthyAsync(connector, TimeSpan.FromSeconds(60)));
            }
            await WaitAsync(async () => await CountAsync("fullnet_messaging_connector_running == 1" + current) == topics.Length
                && await CountAsync("ALERTS{alertname=\"FullNetMessagingConnectorNotRunning\",alertstate=\"firing\"}") == 0, "真实恢复后告警解除", 90);
            await File.WriteAllTextAsync(Path.Combine(data, "alerts-recovered.json"), await client.GetStringAsync("/api/v1/alerts"));
        }

        private async Task<int> CountAsync(string query)
        {
            using var response = await client.GetAsync("/api/v1/query?query=" + Uri.EscapeDataString(query));
            Assert.IsTrue(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return result.RootElement.GetProperty("data").GetProperty("result").GetArrayLength();
        }

        private static async Task WaitAsync(Func<Task<bool>> condition, string message, int seconds = 90)
        {
            var until = DateTimeOffset.UtcNow.AddSeconds(seconds);
            while (!await condition()) { Assert.IsTrue(DateTimeOffset.UtcNow < until, message); await Task.Delay(1000); }
        }

        public async ValueTask DisposeAsync()
        {
            try { var logs = await host.GetLogsAsync(); await File.WriteAllTextAsync(Path.Combine(data, "prometheus.log"), logs.Stdout + logs.Stderr); }
            finally { client.Dispose(); await host.DisposeAsync(); }
        }
    }
}
