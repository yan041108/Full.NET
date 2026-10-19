using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace Full.NET.IntegrationTests.Observability;

/// <summary>真实 B1 写库及 OTLP 指标链路；保持生产告警五分钟窗口和五分钟持续条件。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AuditB1MetricsPipelineTests
{
    private const string ServiceName = "fullnet-b1-metrics-test";
    private string? _ownedConnectionString;

    [TestMethod]
    public async Task B1_metrics_reach_prometheus_and_formal_alerts_fire_then_resolve()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(18));
        var token = deadline.Token;
        await using var backend = await MetricsBackend.StartAsync(token);
        _ownedConnectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        var workload = new Workload();
        var settings = new Dictionary<string, string?>
        {
            ["FullNet:Logging:DeliveryMode"] = "Collector",
            ["FullNet:Logging:ExpectedDeliveryMode"] = "Collector",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = backend.OtlpEndpoint.ToString(),
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_SERVICE_NAME"] = ServiceName,
            ["Auditing:MicroBatch:Capacity"] = "8",
            ["Auditing:MicroBatch:MaxBatchRows"] = "1",
            ["Auditing:MicroBatch:MaxBatchBytes"] = "4096",
            ["Auditing:MicroBatch:QueueMaxBytes"] = "65536",
            ["Auditing:MicroBatch:MaxQueueAge"] = "00:01:00",
        };
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, _ownedConnectionString,
            settings, configureTestServices: services =>
            {
                services.AddScoped<AuditWriteBatchWriter>(provider =>
                    ActivatorUtilities.CreateInstance<AuditWriteBatchWriter>(provider,
                        new DelayedCommands(provider.GetRequiredService<ICommandExecutor>(), workload)));
                foreach (var name in new[] { "OtlpMetricExporter", "OtlpTraceExporter" })
                    services.AddHttpClient(name)
                        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { UseProxy = false })
                        .RemoveAllLoggers()
                        .ConfigureAdditionalHttpMessageHandlers((handlers, _) => handlers.Clear());
                // 显式覆盖运行机 OTLP 环境值，样本及凭据不会发往测试以外的目的地。
                services.ConfigureAll<OtlpExporterOptions>(options =>
                {
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                    options.Headers = string.Empty;
                    options.HttpClientFactory = () => new HttpClient(new TestBackendOnlyHandler(backend.OtlpEndpoint));
                });
                services.AddOpenTelemetry().ConfigureResource(resource =>
                {
                    // 保留生产注册确定的启动标识，清除运行机环境附加的其他资源属性。
                    var instance = (string)resource.Build().Attributes.Single(pair => pair.Key == "service.instance.id").Value;
                    resource.Clear().AddService(ServiceName, serviceInstanceId: instance);
                });
            });
        using var load = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task[] workers = [];
        Task rejected = Task.CompletedTask;
        try
        {
            await factory.InitializeAsync(token);
            var coordinator = factory.Services.GetRequiredService<AuditMicroBatchCoordinator>();
            var meter = factory.Services.GetRequiredService<MeterProvider>();
            var instance = (string)meter.GetResource().Attributes.Single(pair => pair.Key == "service.instance.id").Value;
            var selector = "{job=\"" + ServiceName + "\",instance=\"" + instance + "\"}";
            string Metric(string name, string labels = "") => "fullnet_auditing_microbatch_" + name
                + (labels.Length == 0 ? selector : selector[..^1] + "," + labels + "}");
            Task Flush()
            {
                Assert.IsTrue(meter.ForceFlush(5_000), "B1 真实 OTLP 导出失败。");
                return Task.CompletedTask;
            }
            var trace = "b1-metrics-" + Guid.NewGuid().ToString("N");
            var healthy = Create(trace);
            var oversize = healthy with { RequestPath = "/" + new string('x', 4096) };
            // 真正提交约束反例以产生失败仪器；不通过手工 Record/Add 写入测试零值。
            await coordinator.FlushImportantAsync(healthy, null, token);
            await coordinator.FlushImportantAsync(healthy with { ActionKey = null! }, null, token);
            await coordinator.FlushImportantAsync(oversize, null, token);
            await backend.WaitAsync(async () =>
            {
                await Flush();
                return await backend.ValueAsync(Metric("accepted_total", "kind=\"operation\""), token) == 2
                    && await backend.ValueAsync(Metric("flushed_total"), token) == 1
                    && await backend.ValueAsync(Metric("failed_total", "outcome=\"invalid_row\""), token) == 1
                    && await backend.ValueAsync(Metric("poisoned_total"), token) == 1
                    && await backend.ValueAsync(Metric("rejected_total", "reason=\"event_oversize\""), token) == 1
                    && await backend.ValueAsync(Metric("queue_depth"), token) == 0;
            }, backend.ObservationWindow, token, "B1 健康/约束失败/拒绝基线");
            var resource = meter.GetResource();
            Assert.AreEqual(ServiceName, resource.Attributes.Single(pair => pair.Key == "service.name").Value);
            Assert.AreEqual(instance, resource.Attributes.Single(pair => pair.Key == "service.instance.id").Value);
            Assert.IsFalse(await backend.AlertFiringAsync("FullNetAuditB1QueuePressure", instance, token));
            Assert.IsFalse(await backend.AlertFiringAsync("FullNetAuditB1WaitOrFailure", instance, token));

            async Task ProduceAsync()
            {
                while (!load.IsCancellationRequested)
                {
                    // 固定九个循环，单次完成后才创建下一项；不累积未等待 Task。
                    try
                    {
                        await coordinator.FlushImportantAsync(Create(trace), null, load.Token);
                        Interlocked.Increment(ref workload.Completed);
                    }
                    catch (OperationCanceledException) when (load.IsCancellationRequested)
                    {
                        // 只吸收尚未接受时的负载停止；已接受调用仍由协调器等待真实尝试结束。
                        break;
                    }
                }
            }
            async Task RejectAsync()
            {
                while (!load.IsCancellationRequested)
                {
                    await coordinator.FlushImportantAsync(oversize, null, load.Token);
                    Interlocked.Increment(ref workload.Rejected);
                    await Task.Delay(TimeSpan.FromSeconds(2), load.Token);
                }
            }
            workload.DelayMilliseconds = 2_000;
            workers = Enumerable.Range(0, 9).Select(_ => ProduceAsync()).ToArray();
            rejected = RejectAsync();
            await backend.WaitAsync(async () =>
            {
                await Flush();
                return await backend.ValueAsync(Metric("queue_depth"), token) >= 7
                    && await backend.ValueAsync(Metric("queue_capacity"), token) == 8
                    && await backend.ValueAsync(Metric("queue_bytes"), token) > 0
                    && await backend.ValueAsync(Metric("queue_oldest_age_seconds"), token) > 0
                    && await backend.ValueAsync(Metric("wait_seconds_count"), token) > 3
                    && await backend.ValueAsync(Metric("batch_rows_count"), token) > 1
                    && await backend.ValueAsync(Metric("batch_bytes_sum"), token) > 0;
            }, backend.ObservationWindow, token, "B1 队列、在途字节与直方图真实导出");
            await backend.WaitAsync(async () =>
            {
                await Flush();
                return await backend.AlertFiringAsync("FullNetAuditB1QueuePressure", instance, token)
                    && await backend.AlertFiringAsync("FullNetAuditB1WaitOrFailure", instance, token);
            }, TimeSpan.FromMinutes(7), token, "B1 两条正式五分钟持续告警触发");

            workload.DelayMilliseconds = 0;
            await load.CancelAsync();
            await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(20), token);
            await IgnoreLoadCancellationAsync(rejected, load.Token);
            await backend.WaitAsync(() => Task.FromResult(coordinator.QueueBytesInUse == 0),
                TimeSpan.FromSeconds(5), token, "已接受调用完成后预约实际归还");
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
                var count = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>()
                    .QuerySingleOrDefaultAsync<long>(new SqlStatement("test.auditing.count_metrics_rows",
                        "SELECT COUNT(*) FROM fn_auditing_operation_log WHERE TraceId = @TraceId", SqlDataScope.Global),
                        new { TraceId = trace }, token);
                Assert.AreEqual(1L + workload.Completed, count, "每个已完成健康调用必须确实落库，失败反例不能迟到写入。");
            }
            await backend.WaitAsync(async () =>
            {
                await Flush();
                return await backend.ValueAsync(Metric("queue_depth"), token) == 0
                    && await backend.ValueAsync(Metric("queue_bytes"), token) == 0
                    && await backend.ValueAsync(Metric("queue_oldest_age_seconds"), token) == 0
                    && await backend.ValueAsync(Metric("accepted_total", "kind=\"operation\""), token) == 2 + workload.Completed
                    && await backend.ValueAsync(Metric("flushed_total"), token) == 1 + workload.Completed
                    && await backend.ValueAsync(Metric("failed_total", "outcome=\"invalid_row\""), token) == 1
                    && await backend.ValueAsync(Metric("poisoned_total"), token) == 1
                    && await backend.ValueAsync(Metric("rejected_total", "reason=\"event_oversize\""), token) == 1 + workload.Rejected;
            }, backend.ObservationWindow, token, "B1 排空与数据库/指标计数对账");
            await backend.WaitAsync(async () =>
            {
                await Flush();
                return !await backend.AlertFiringAsync("FullNetAuditB1QueuePressure", instance, token)
                    && !await backend.AlertFiringAsync("FullNetAuditB1WaitOrFailure", instance, token)
                    && await backend.ValueAsync("increase(" + Metric("rejected_total", "reason=\"event_oversize\"") + "[5m])", token) == 0;
            }, TimeSpan.FromMinutes(6), token, "B1 原五分钟统计窗口滚动后告警恢复");
            var series = await backend.QueryAsync("{__name__=~\"fullnet_auditing_microbatch_.*\",job=\"" + ServiceName + "\"}", token);
            foreach (var sample in series.EnumerateArray())
            foreach (var label in sample.GetProperty("metric").EnumerateObject())
                Assert.IsTrue(label.Name is "__name__" or "job" or "instance" or "kind" or "reason" or "outcome"
                    or "le" or "otel_scope_name" or "otel_scope_version", "未批准的 B1 指标标签：" + label.Name);
            await backend.AssertNoOutOfOrderAsync();
        }
        catch
        {
            await backend.WriteFailureDiagnosticsAsync();
            throw;
        }
        finally
        {
            workload.DelayMilliseconds = 0;
            await load.CancelAsync();
            try { await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(20)); }
            finally { await IgnoreLoadCancellationAsync(rejected, load.Token); }
        }
    }

    [TestCleanup]
    public async Task Delete_owned_database_after_host_disposal()
    {
        if (_ownedConnectionString is null) return;
        var database = SharedDatabaseFixture.GetSqlServerDatabaseName(_ownedConnectionString);
        Assert.IsTrue(database.StartsWith("fullnet_it_", StringComparison.Ordinal)
            && Guid.TryParseExact(database["fullnet_it_".Length..], "N", out _));
        await using var admin = new SqlConnection(new SqlConnectionStringBuilder(
            await SharedDatabaseFixture.GetSqlServerMasterConnectionStringAsync()) { Pooling = false }.ConnectionString);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await admin.OpenAsync(deadline.Token);
        await using var command = admin.CreateCommand();
        command.CommandTimeout = 15;
        var quoted = SharedDatabaseFixture.QuoteSqlServerIdent(database);
        command.CommandText = $"ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted};";
        await command.ExecuteNonQueryAsync(deadline.Token);
        Console.Error.WriteLine($"[b1-metrics] owned database removed: {database}");
    }

    private static async Task IgnoreLoadCancellationAsync(Task task, CancellationToken loadToken)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(20)); }
        catch (OperationCanceledException) when (loadToken.IsCancellationRequested) { }
    }

    private static OperationLogWriteModel Create(string trace) => new("auditing.metrics.probe", "POST",
        "/api/v1/auditing/metrics-probe", 200, 1, true, null, null, trace, null, "auditing.metrics.probe");

    private sealed class Workload
    {
        public volatile int DelayMilliseconds;
        public long Completed;
        public long Rejected;
    }

    /// <summary>SDK 保留各信号路径；夹具只允许实际请求访问本用例后端，环境覆盖不能向外发送。</summary>
    private sealed class TestBackendOnlyHandler(Uri endpoint) : DelegatingHandler(new SocketsHttpHandler { UseProxy = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            if (uri is null || uri.Scheme != endpoint.Scheme || uri.Host != endpoint.Host || uri.Port != endpoint.Port
                || uri.AbsolutePath is not ("/v1/metrics" or "/v1/traces"))
                throw new InvalidOperationException("OTLP 测试请求偏离自有后端或信号路径。");
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>仅在测试 Writer 前增加可取消等待；实际事务和写入仍由生产实现执行。</summary>
    private sealed class DelayedCommands(ICommandExecutor inner, Workload workload) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            var delay = workload.DelayMilliseconds;
            if (delay > 0) await Task.Delay(delay, cancellationToken);
            return await inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }

    /// <summary>加载部署原配置的真实指标后端，只使用已缓存固定镜像及本用例容器。</summary>
    private sealed class MetricsBackend(IContainer prometheus, IContainer collector,
        HttpClient http, TimeSpan observationWindow) : IAsyncDisposable
    {
        private const string CollectorImage = "ghcr.io/open-telemetry/opentelemetry-collector-releases/opentelemetry-collector-contrib:0.163.1@sha256:1175b03003053962012f5a231f5567f66a3e8fdb960c7835c0e48f7aa1555806";
        private const string PrometheusImage = "prom/prometheus:v3.15.0@sha256:efd719c99d83b060d9daefdcf00360461adf279f45ef5391f8d111892118753e";
        public TimeSpan ObservationWindow => observationWindow;
        public Uri OtlpEndpoint => new UriBuilder("http", prometheus.Hostname, prometheus.GetMappedPublicPort(4318)).Uri;

        public static async Task<MetricsBackend> StartAsync(CancellationToken token)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Full.NET.slnx"))) root = root.Parent;
            Assert.IsNotNull(root);
            var values = await File.ReadAllTextAsync(Path.Combine(root.FullName, "deploy/observability/otel-collector-values.yaml"), token);
            Assert.IsTrue(values.Contains(CollectorImage.Split('@')[1], StringComparison.Ordinal)
                && values.Contains("tag: \"0.163.1\"", StringComparison.Ordinal));
            var block = Regex.Match(values, @"^config:\r?\n([\s\S]*?)(?=^[^ \r\n#]|\z)", RegexOptions.Multiline);
            Assert.IsTrue(block.Success);
            var config = Regex.Replace(block.Groups[1].Value, "^  ", "", RegexOptions.Multiline)
                .Replace("http://prometheus.observability.svc:9090/api/v1/write", "http://127.0.0.1:9090/api/v1/write", StringComparison.Ordinal);
            var interval = Regex.Match(config, @"truncate_frequency:\s*(\d+)([sm])");
            Assert.IsTrue(interval.Success);
            var seconds = int.Parse(interval.Groups[1].Value, CultureInfo.InvariantCulture)
                * (interval.Groups[2].Value == "m" ? 60 : 1);
            Assert.IsTrue(seconds is > 0 and <= 120);
            var rules = await File.ReadAllBytesAsync(Path.Combine(root.FullName, "deploy/observability/prometheus-rules.yaml"), token);
            var prometheusConfig = Encoding.UTF8.GetBytes("""
                global:
                  evaluation_interval: 1s
                  scrape_interval: 1s
                rule_files: [/work/rules.yaml]
                scrape_configs: []
                """);
            var prometheus = Budget(new ContainerBuilder(PrometheusImage))
                .WithPortBinding(9090, true).WithPortBinding(4318, true)
                .WithResourceMapping(prometheusConfig, "/work/prometheus.yaml")
                .WithResourceMapping(rules, "/work/rules.yaml")
                .WithCommand("--config.file=/work/prometheus.yaml", "--storage.tsdb.path=/tmp/prometheus", "--web.enable-remote-write-receiver")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(9090)).Build();
            IContainer? collector = null;
            try
            {
                await prometheus.StartAsync(token);
                collector = Budget(new ContainerBuilder(CollectorImage)).WithCreateParameterModifier(parameters =>
                    {
                        parameters.User = "0:0";
                        (parameters.HostConfig ??= new()).NetworkMode = "container:" + prometheus.Id;
                    })
                    .WithResourceMapping(Encoding.UTF8.GetBytes(config), "/work/collector.yaml")
                    .WithResourceMapping(Array.Empty<byte>(), "/var/otel-collector/storage/.keep")
                    .WithCommand("--config=/work/collector.yaml")
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Everything is ready")).Build();
                await collector.StartAsync(token);
                var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
                {
                    BaseAddress = new UriBuilder("http", prometheus.Hostname, prometheus.GetMappedPublicPort(9090)).Uri,
                    Timeout = TimeSpan.FromSeconds(5),
                };
                return new(prometheus, collector, http, TimeSpan.FromSeconds(seconds + 20));
            }
            catch
            {
                try { if (collector is not null) await collector.DisposeAsync(); }
                finally { await prometheus.DisposeAsync(); }
                throw;
            }
        }

        private static ContainerBuilder Budget(ContainerBuilder builder) => builder.WithImagePullPolicy(PullPolicy.Never)
            .WithCreateParameterModifier(parameters =>
            {
                var limits = parameters.HostConfig ??= new();
                limits.Memory = 512L * 1024 * 1024;
                limits.NanoCPUs = 1_000_000_000;
            });

        public async Task<JsonElement> QueryAsync(string expression, CancellationToken token)
        {
            using var response = await http.GetAsync("api/v1/query?query=" + Uri.EscapeDataString(expression), token);
            response.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            Assert.AreEqual("success", body.RootElement.GetProperty("status").GetString());
            return body.RootElement.GetProperty("data").GetProperty("result").Clone();
        }

        public async Task<double> ValueAsync(string expression, CancellationToken token)
        {
            var result = await QueryAsync(expression, token);
            Assert.IsTrue(result.GetArrayLength() <= 1, "出现重复时间序列：" + expression);
            return result.GetArrayLength() == 0 ? double.NaN
                : double.Parse(result[0].GetProperty("value")[1].GetString()!, CultureInfo.InvariantCulture);
        }

        public async Task<bool> AlertFiringAsync(string name, string instance, CancellationToken token)
        {
            using var response = await http.GetAsync("api/v1/alerts", token);
            response.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return body.RootElement.GetProperty("data").GetProperty("alerts").EnumerateArray().Any(alert =>
                alert.GetProperty("labels").GetProperty("alertname").GetString() == name
                && alert.GetProperty("labels").GetProperty("job").GetString() == ServiceName
                && alert.GetProperty("labels").GetProperty("instance").GetString() == instance
                && alert.GetProperty("labels").GetProperty("severity").GetString()
                    == (name == "FullNetAuditB1QueuePressure" ? "warning" : "critical")
                && alert.GetProperty("state").GetString() == "firing");
        }

        public async Task WaitAsync(Func<Task<bool>> condition, TimeSpan timeout, CancellationToken token, string stage)
        {
            var expires = DateTimeOffset.UtcNow + timeout;
            do
            {
                if (await condition()) { Console.Error.WriteLine($"[b1-metrics] {stage}: {DateTimeOffset.UtcNow:O}"); return; }
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            } while (DateTimeOffset.UtcNow < expires);
            Assert.Fail(stage + " 未在 " + timeout + " 内完成。");
        }

        public async Task AssertNoOutOfOrderAsync()
        {
            var logs = await collector.GetLogsAsync();
            Assert.IsFalse((logs.Stdout + logs.Stderr).Contains("out of order sample", StringComparison.Ordinal));
        }

        public async Task WriteFailureDiagnosticsAsync()
        {
            foreach (var container in new[] { collector, prometheus })
            {
                var logs = await container.GetLogsAsync();
                Console.Error.WriteLine(logs.Stdout + logs.Stderr);
            }
        }

        public async ValueTask DisposeAsync()
        {
            http.Dispose();
            try { await collector.DisposeAsync(); }
            finally { await prometheus.DisposeAsync(); }
        }
    }
}
