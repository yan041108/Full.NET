using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Full.NET.Hosting.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;

namespace Full.NET.IntegrationTests.Observability;

/// <summary>从当前平台 Helm 清单取配置，验证仅指标 Host 的真实导出、双通道和原始告警窗口。</summary>
[TestClass]
[DoNotParallelize]
public sealed class HostLoggingMetricsPipelineTests
{
    private const string CollectorImage = "ghcr.io/open-telemetry/opentelemetry-collector-releases/opentelemetry-collector-contrib:0.163.1@sha256:1175b03003053962012f5a231f5567f66a3e8fdb960c7835c0e48f7aa1555806";
    private const string PrometheusImage = "prom/prometheus:v3.15.0@sha256:efd719c99d83b060d9daefdcf00360461adf279f45ef5391f8d111892118753e";

    [TestMethod]
    public async Task Host_queue_metrics_reach_prometheus_and_priority_drop_alert_fires_then_resolves()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var token = deadline.Token;
        var root = FindRoot();
        var platform = await RenderPlatformAsync(root, token);
        var items = platform.GetProperty("items").EnumerateArray().ToArray();
        foreach (var pair in new[] { ("telemetry-collector", CollectorImage), ("prometheus", PrometheusImage) })
            Assert.AreEqual(pair.Item2, items.Single(item => item.GetProperty("kind").GetString() == "Deployment"
                && item.GetProperty("metadata").GetProperty("name").GetString() == pair.Item1)
                .GetProperty("spec").GetProperty("template").GetProperty("spec")
                .GetProperty("containers")[0].GetProperty("image").GetString(), "固定镜像必须与原始清单一致。");
        var data = items.Single(item => item.GetProperty("kind").GetString() == "ConfigMap").GetProperty("data");
        var collectorConfig = JsonNode.Parse(data.GetProperty("collector.json").GetString()!)!;
        var pipelines = collectorConfig["service"]!["pipelines"]!.AsObject();
        CollectionAssert.AreEqual(new[] { "metrics" }, pipelines.Select(pair => pair.Key).ToArray());
        Assert.AreEqual("5s", collectorConfig["exporters"]!["prometheus_remote_write"]!["wal"]!["truncate_frequency"]!.GetValue<string>());
        // 仅启动两后端，调整目的地/规则路径并移除其他抓取和通知；保留原处理器、WAL、评估周期与规则窗口。
        collectorConfig["exporters"]!["prometheus_remote_write"]!["http"]!["endpoint"] = "http://127.0.0.1:9090/api/v1/write";
        var prometheusConfig = JsonNode.Parse(data.GetProperty("prometheus.json").GetString()!)!;
        prometheusConfig["rule_files"] = new JsonArray("/work/rules.json");
        prometheusConfig["scrape_configs"] = new JsonArray();
        prometheusConfig["alerting"]!["alertmanagers"] = new JsonArray();
        var observationWindow = TimeSpan.FromSeconds(45);
        await using var prometheus = Budget(new ContainerBuilder(PrometheusImage))
            .WithPortBinding(9090, true)
            .WithPortBinding(4318, true)
            .WithResourceMapping(Encoding.UTF8.GetBytes(prometheusConfig.ToJsonString()), "/work/prometheus.json")
            .WithResourceMapping(Encoding.UTF8.GetBytes(data.GetProperty("rules.json").GetString()!), "/work/rules.json")
            .WithCommand("--config.file=/work/prometheus.json", "--storage.tsdb.path=/tmp/prometheus",
                "--web.enable-remote-write-receiver")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(9090))
            .Build();
        await prometheus.StartAsync(token);
        await using var collector = Budget(new ContainerBuilder(CollectorImage))
            .WithCreateParameterModifier(parameters =>
            {
                parameters.User = "10001:10001";
                var limits = parameters.HostConfig ??= new();
                limits.NetworkMode = "container:" + prometheus.Id;
                limits.Tmpfs = new Dictionary<string, string>
                {
                    ["/var/otel-collector/storage"] = "rw,uid=10001,gid=10001,mode=0700,size=67108864,nosuid,nodev,noexec",
                };
            })
            .WithResourceMapping(Encoding.UTF8.GetBytes(collectorConfig.ToJsonString()), "/work/collector.json")
            .WithCommand("--config=/work/collector.json")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Everything is ready"))
            .Build();
        await collector.StartAsync(token);
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            BaseAddress = new UriBuilder("http", prometheus.Hostname, prometheus.GetMappedPublicPort(9090)).Uri,
            Timeout = TimeSpan.FromSeconds(5),
        };
        using var writer = new BlockedWriter();
        var original = Console.Out;
        IHost? host = null;
        try
        {
            Console.SetOut(writer);
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            // 隔离运行机的 OTLP 目的地、认证头和资源覆盖，测试数据只发送到自有容器。
            foreach (var key in builder.Configuration.AsEnumerable().Select(pair => pair.Key)
                .Where(key => key.StartsWith("OTEL_", StringComparison.Ordinal)).ToArray())
                builder.Configuration[key] = null;
            builder.Configuration["FullNet:Logging:DeliveryMode"] = "Collector";
            builder.Configuration["FullNet:Logging:ExpectedDeliveryMode"] = "Collector";
            builder.Configuration["FullNet:Logging:AsyncBufferSize"] = "2";
            builder.Configuration["FullNet:Logging:HighPriorityAsyncBufferSize"] = "2";
            builder.Configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] =
                new UriBuilder("http", prometheus.Hostname, prometheus.GetMappedPublicPort(4318), "/v1/metrics").Uri.ToString();
            builder.Configuration["OTEL_EXPORTER_OTLP_METRICS_PROTOCOL"] = "http/protobuf";
            builder.Configuration["OTEL_SERVICE_NAME"] = "fullnet-logging-metrics-test";
            builder.AddFullNetServiceDefaults();
            // 观察传输不能写入正在阻塞的被测队列；仅隔离本夹具的 OTLP 客户端。
            foreach (var name in new[] { "OtlpMetricExporter", "OtlpTraceExporter" })
                builder.Services.AddHttpClient(name)
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false })
                    .RemoveAllLoggers()
                    .ConfigureAdditionalHttpMessageHandlers((handlers, _) => handlers.Clear());
            builder.Services.ConfigureAll<OtlpExporterOptions>(options =>
                options.HttpClientFactory = () => new HttpClient(new SocketsHttpHandler { UseProxy = false }));
            host = builder.Build();
            var meter = host.Services.GetRequiredService<MeterProvider>();
            var monitors = host.Services.GetRequiredService<FullNetLoggingMonitors>();
            var instance = host.Services.GetRequiredService<LoggingResourceMetadata>().Instance;
            var identity = "job=\"fullnet-logging-metrics-test\",instance=\"" + instance + "\"";
            Assert.IsNull(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
            var logger = host.Services.GetRequiredService<ILogger<HostLoggingMetricsPipelineTests>>();
            logger.LogInformation("Metrics pipeline baseline");
            logger.LogError("Metrics pipeline priority baseline");
            async Task Flush()
            {
                Assert.IsTrue(meter.ForceFlush(5_000), "真实 OTLP 导出失败。");
                await Task.CompletedTask;
            }
            await WaitAsync(async () =>
            {
                await Flush();
                return await ValueAsync(http, "fullnet_logging_events_consumed_total{channel=\"high_priority\",outcome=\"accepted\"}", token) >= 1
                    && await ValueAsync(http, "fullnet_logging_events_dropped_total{channel=\"high_priority\"}", token) == 0;
            }, observationWindow, token, "Prometheus 零丢弃基线");
            var baseline = await QueryAsync(http,
                "fullnet_logging_events_dropped_total{channel=\"high_priority\"}", token);
            Assert.AreEqual(1, baseline.GetArrayLength());
            var labels = baseline[0].GetProperty("metric");
            Assert.AreEqual("fullnet-logging-metrics-test", labels.GetProperty("job").GetString());
            Assert.AreEqual(meter.GetResource().Attributes.Single(pair => pair.Key == "service.instance.id").Value as string,
                labels.GetProperty("instance").GetString());
            // 首个零值样本必须先落入 Prometheus，随后才制造丢弃，避免 increase 错过初次增长。
            await Task.Delay(TimeSpan.FromSeconds(2), token);
            await Flush();
            writer.Armed = true;
            logger.LogInformation("Block general output");
            Assert.IsTrue(writer.Entered.Wait(TimeSpan.FromSeconds(5), token));
            logger.LogError("Block priority output");
            await WaitAsync(() => Task.FromResult(monitors.HighPriority.Snapshot.Count == 0),
                TimeSpan.FromSeconds(5), token, "优先消费者进入在途阻塞");
            for (var index = 0; index < 3; index++)
            {
                logger.LogInformation("Fill general queue {Index}", index);
                logger.LogError("Fill priority queue {Index}", index);
            }
            Console.SetOut(original);
            Assert.AreEqual(2, monitors.General.Snapshot.Count);
            Assert.AreEqual(2, monitors.HighPriority.Snapshot.Count);
            Assert.AreEqual(1, monitors.General.Snapshot.DroppedMessagesCount);
            Assert.AreEqual(1, monitors.HighPriority.Snapshot.DroppedMessagesCount);
            await WaitAsync(async () =>
            {
                await Flush();
                Assert.AreEqual(1, monitors.General.Snapshot.DroppedMessagesCount,
                    "观察链路产生了额外普通日志，不能继续使用此饱和夹具。");
                Assert.AreEqual(1, monitors.HighPriority.Snapshot.DroppedMessagesCount);
                foreach (var channel in new[] { "general", "high_priority" })
                {
                    var selector = "{channel=\"" + channel + "\"," + identity + "}";
                    if (await ValueAsync(http, "fullnet_logging_queue_depth" + selector, token) != 2
                        || await ValueAsync(http, "fullnet_logging_queue_capacity" + selector, token) != 2
                        || !(await ValueAsync(http, "fullnet_logging_queue_bytes" + selector, token) > 0)
                        || !(await ValueAsync(http, "fullnet_logging_queue_bytes_capacity" + selector, token) > 0)
                        || !(await ValueAsync(http, "fullnet_logging_queue_oldest_age_seconds" + selector, token) > 0)
                        || await ValueAsync(http, "fullnet_logging_events_dropped_total" + selector, token) != 1)
                        return false;
                }
                return true;
            }, observationWindow, token, "双通道饱和与丢弃指标");
            writer.Release.Set();
            await WaitAsync(async () =>
            {
                await Flush();
                return await ValueAsync(http, "fullnet_logging_queue_bytes{channel=\"general\"}", token) == 0
                    && await ValueAsync(http, "fullnet_logging_queue_bytes{channel=\"high_priority\"}", token) == 0;
            }, observationWindow, token, "双通道在途字节归零");
            await WaitAsync(async () =>
            {
                await Flush();
                return await AlertFiringAsync(http, instance, token);
            }, TimeSpan.FromSeconds(100), token, "正式优先丢弃告警触发");
            // 保留正式五分钟 increase 与一分钟 for，不通过缩短规则窗口伪造恢复验收。
            await WaitAsync(async () =>
            {
                await Flush();
                var samples = await QueryAsync(http,
                    "increase(fullnet_logging_events_dropped_total{channel=\"high_priority\"}[5m])", token);
                return samples.GetArrayLength() == 1
                    && double.Parse(samples[0].GetProperty("value")[1].GetString()!, CultureInfo.InvariantCulture) == 0
                    && !await AlertFiringAsync(http, instance, token);
            }, TimeSpan.FromMinutes(6), token, "正式五分钟窗口滚动后告警恢复");
            var completedLogs = await collector.GetLogsAsync();
            Assert.IsFalse((completedLogs.Stdout + completedLogs.Stderr)
                .Contains("out of order sample", StringComparison.Ordinal),
                "Remote Write 出现乱序拒绝，部分指标可查询不能视为完整链路通过。");
        }
        catch
        {
            Console.SetOut(original);
            var logs = await collector.GetLogsAsync();
            Console.Error.WriteLine(logs.Stdout + logs.Stderr);
            var storageLogs = await prometheus.GetLogsAsync();
            Console.Error.WriteLine(storageLogs.Stdout + storageLogs.Stderr);
            throw;
        }
        finally
        {
            writer.Release.Set();
            Console.SetOut(original);
            host?.Dispose();
        }
    }

    /// <summary>直接渲染当前本地 Chart；超时后终止本轮子进程，不读取集群或复用外部清单。</summary>
    private static async Task<JsonElement> RenderPlatformAsync(string root, CancellationToken token)
    {
        var start = new ProcessStartInfo("helm")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "template", "fullnet-observability", Path.Combine(root, "deploy/helm/fullnet-observability"),
            "--namespace", "observability", "--set",
            "cluster=integration-host-platform,prometheusClaim=prometheus-data,alertmanagerClaim=alertmanager-data",
            "--set-file", "rulesSource=" + Path.Combine(root, "deploy/observability/prometheus-rules.yaml"),
            "--set-file", "collectorSource=" + Path.Combine(root, "deploy/observability/otel-collector-values.yaml"),
        })
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        Assert.IsTrue(process.Start(), "没有启动当前平台渲染器。");
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token);
            var stderr = await error;
            Assert.AreEqual(0, process.ExitCode,
                "平台渲染失败：" + (stderr.Length > 2000 ? stderr[^2000..] : stderr));
            // Helm 来源注释与文档分隔符不属于 JSON，清单数据本身必须原样解析。
            var json = string.Join('\n', (await output).Split('\n').Where(line =>
                !line.StartsWith('#') && line.Trim() != "---"));
            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("List", document.RootElement.GetProperty("kind").GetString());
            return document.RootElement.Clone();
        }
        finally
        {
            if (!process.HasExited)
            {
                // 子进程可能在判断后自然退出，只忽略已经证实退出的竞态。
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static ContainerBuilder Budget(ContainerBuilder builder) =>
        builder.WithImagePullPolicy(PullPolicy.Never).WithCreateParameterModifier(parameters =>
        {
            var configuration = parameters.HostConfig ??= new();
            configuration.Memory = 512L * 1024 * 1024;
            configuration.NanoCPUs = 1_000_000_000;
        });

    private static async Task<double> ValueAsync(HttpClient client, string expression, CancellationToken token)
    {
        var result = await QueryAsync(client, expression, token);
        Assert.IsTrue(result.GetArrayLength() <= 1, "出现意外的重复或高基数时间序列：" + expression);
        return result.GetArrayLength() == 0 ? double.NaN
            : double.Parse(result[0].GetProperty("value")[1].GetString()!, CultureInfo.InvariantCulture);
    }

    private static async Task<JsonElement> QueryAsync(HttpClient client, string expression, CancellationToken token)
    {
        using var response = await client.GetAsync("api/v1/query?query=" + Uri.EscapeDataString(expression), token);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        Assert.AreEqual("success", document.RootElement.GetProperty("status").GetString());
        return document.RootElement.GetProperty("data").GetProperty("result").Clone();
    }

    private static async Task<bool> AlertFiringAsync(HttpClient client, string instance, CancellationToken token)
    {
        using var response = await client.GetAsync("api/v1/alerts", token);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return document.RootElement.GetProperty("data").GetProperty("alerts").EnumerateArray().Any(alert =>
            alert.GetProperty("labels").GetProperty("alertname").GetString() == "FullNetPriorityLogDrop"
            && alert.GetProperty("labels").GetProperty("job").GetString() == "fullnet-logging-metrics-test"
            && alert.GetProperty("labels").GetProperty("instance").GetString() == instance
            && alert.GetProperty("labels").GetProperty("channel").GetString() == "high_priority"
            && alert.GetProperty("labels").GetProperty("severity").GetString() == "critical"
            && alert.GetProperty("state").GetString() == "firing");
    }

    private static async Task WaitAsync(Func<Task<bool>> condition, TimeSpan timeout, CancellationToken token, string stage)
    {
        var started = DateTimeOffset.UtcNow;
        var expires = DateTimeOffset.UtcNow + timeout;
        do
        {
            if (await condition())
            {
                Console.Error.WriteLine($"[logging-metrics] {stage}: {DateTimeOffset.UtcNow:O}, elapsed={DateTimeOffset.UtcNow - started}");
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        } while (DateTimeOffset.UtcNow < expires);
        Assert.Fail($"{stage} 未在 {timeout} 内达到预期，开始时间 {started:O}。");
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("没有找到仓库根目录。");
    }

    /// <summary>阻塞 Console 同步锁，使两个后台出口停滞；释放前不会阻塞调用方。</summary>
    private sealed class BlockedWriter : TextWriter
    {
        public volatile bool Armed;
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(string? value)
        {
            if (!Armed) return;
            Entered.Set();
            Release.Wait();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Entered.Dispose(); Release.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
