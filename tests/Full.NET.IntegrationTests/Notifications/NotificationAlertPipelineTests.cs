using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Notifications.Execution;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using static Full.NET.IntegrationTests.NativeAot.NativeNotificationGovernanceProbe;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>真实数据库采样经部署 Collector 进入 Prometheus，保持原规则五分钟窗口验证触发与恢复。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NotificationAlertPipelineTests
{
    private const string CollectorImage = "ghcr.io/open-telemetry/opentelemetry-collector-releases/opentelemetry-collector-contrib:0.163.1@sha256:1175b03003053962012f5a231f5567f66a3e8fdb960c7835c0e48f7aa1555806";
    private const string PrometheusImage = "prom/prometheus:v3.15.0@sha256:efd719c99d83b060d9daefdcf00360461adf279f45ef5391f8d111892118753e";
    private static readonly string[] Alerts = ["FullNetNotificationBacklogAging", "FullNetNotificationDeadLetters", "FullNetNotificationUnknownPending"];
    private static readonly string[] Metrics = ["fullnet_notifications_delivery_oldest_age_seconds", "fullnet_notifications_delivery_dead_lettered", "fullnet_notifications_delivery_awaiting_reconciliation"];
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Notification_backlog_dead_letters_and_unknown_alerts_fire_then_resolve()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(16));
        var ct = deadline.Token;
        var root = NativeApiArtifactLocator.FindRepositoryRoot();
        var evidence = Path.Combine(Path.GetTempPath(), "fullnet-notification-alerts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        TestContext.WriteLine("通知告警原始证据：" + evidence);
        var values = await File.ReadAllTextAsync(Path.Combine(root, "deploy/observability/otel-collector-values.yaml"), ct);
        var block = Regex.Match(values, @"^config:\r?\n([\s\S]*?)(?=^[^ \r\n#]|\z)", RegexOptions.Multiline);
        Assert.IsTrue(block.Success);
        var collectorConfig = Regex.Replace(block.Groups[1].Value, "^  ", "", RegexOptions.Multiline)
            .Replace("http://prometheus.observability.svc:9090/api/v1/write", "http://127.0.0.1:9090/api/v1/write", StringComparison.Ordinal);
        var rules = await File.ReadAllBytesAsync(Path.Combine(root, "deploy/observability/prometheus-rules.yaml"), ct);
        await using var prometheus = Budget(new ContainerBuilder(PrometheusImage))
            .WithPortBinding(9090, true).WithPortBinding(4318, true)
            .WithResourceMapping(Encoding.UTF8.GetBytes("""
                global:
                  evaluation_interval: 1s
                  scrape_interval: 1s
                rule_files: [/work/rules.yaml]
                scrape_configs: []
                """), "/work/prometheus.yaml")
            .WithResourceMapping(rules, "/work/rules.yaml")
            .WithCommand("--config.file=/work/prometheus.yaml", "--storage.tsdb.path=/tmp/prometheus", "--web.enable-remote-write-receiver")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(9090)).Build();
        await prometheus.StartAsync(ct);
        await using var collector = Budget(new ContainerBuilder(CollectorImage))
            .WithCreateParameterModifier(p => { p.User = "0:0"; (p.HostConfig ??= new()).NetworkMode = "container:" + prometheus.Id; })
            .WithResourceMapping(Encoding.UTF8.GetBytes(collectorConfig), "/work/collector.yaml")
            .WithResourceMapping(Array.Empty<byte>(), "/var/otel-collector/storage/.keep")
            .WithCommand("--config=/work/collector.yaml")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Everything is ready")).Build();
        await collector.StartAsync(ct);
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            BaseAddress = new UriBuilder("http", prometheus.Hostname, prometheus.GetMappedPublicPort(9090)).Uri,
        };
        using var meter = Sdk.CreateMeterProviderBuilder().AddMeter(NotificationDeliveryTelemetry.MeterName)
            .AddOtlpExporter((exporter, reader) =>
            {
                // 显式 Endpoint 不会补充信号路径，必须发送到 OTLP 指标接收地址。
                exporter.Endpoint = new UriBuilder("http", prometheus.Hostname, prometheus.GetMappedPublicPort(4318), "/v1/metrics").Uri;
                exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                exporter.Headers = string.Empty;
                exporter.HttpClientFactory = () => new HttpClient(new SocketsHttpHandler { UseProxy = false });
                reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 1000;
            }).Build();
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, connectionString, SmtpSettings);
        await factory.InitializeAsync(ct);
        using var client = factory.CreateClientForHost("localhost");
        var token = await NotificationProfileBindingAssertions.LoginAsHostAdminAsync(client, ct);
        var user = await NotificationProfileBindingAssertions.GetCurrentUserAsync(client, token, ct);
        var route = await CreateEmailRouteAsync(client, token, "transactional", ct);
        var ids = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            var intent = await NotificationDeliveryWorkerAssertions.CreateIntentAsync(client, token, route, user.Id, "alert-" + index, ct);
            ids.Add((await NotificationDeliveryWorkerAssertions.WaitDeliveryAsync(client, token, intent.Id, ct)).Id);
        }
        await using DbConnection db = new SqlConnection(connectionString);
        await db.OpenAsync(ct);
        for (var index = 0; index < ids.Count; index++)
            await db.ExecuteAsync(new CommandDefinition("""
                UPDATE fn_notifications_delivery SET StatusKey = @State, CreatedAtUtc = @Past,
                    NextAttemptAtUtc = NULL, LeaseExpiresAtUtc = NULL WHERE Id = @Id
                """, new { Id = ids[index], State = new[] { "accepted", "dead_lettered", "unknown" }[index],
                    Past = DateTimeOffset.UtcNow.AddMinutes(-10) }, cancellationToken: ct));
        try
        {
            await WaitForAlertsAsync(true);
            await File.WriteAllTextAsync(Path.Combine(evidence, "firing.json"), await http.GetStringAsync("api/v1/rules", ct), ct);
            foreach (var metric in Metrics)
            {
                var raw = await http.GetStringAsync("api/v1/query?query=" + Uri.EscapeDataString(metric), ct);
                await File.WriteAllTextAsync(Path.Combine(evidence, metric + ".json"), raw, ct);
                using var payload = JsonDocument.Parse(raw);
                var result = payload.RootElement.GetProperty("data").GetProperty("result");
                Assert.AreEqual(1, result.GetArrayLength(), "真实 Collector 的指标名称及序列应与规则一致：" + metric);
                Assert.IsTrue(double.Parse(result[0].GetProperty("value")[1].GetString()!, CultureInfo.InvariantCulture) > 0);
                foreach (var label in result[0].GetProperty("metric").EnumerateObject())
                    Assert.IsFalse(new[] { "tenant_id", "user_id", "profile_key", "recipient", "provider_message_id" }.Contains(label.Name));
            }
            await db.ExecuteAsync(new CommandDefinition("UPDATE fn_notifications_delivery SET StatusKey = 'sent' WHERE Id IN @Ids",
                new { Ids = ids.ToArray() }, cancellationToken: ct));
            await WaitForAlertsAsync(false);
            await File.WriteAllTextAsync(Path.Combine(evidence, "resolved.json"), await http.GetStringAsync("api/v1/rules", ct), ct);
            foreach (var metric in Metrics)
            {
                using var payload = JsonDocument.Parse(await http.GetStringAsync("api/v1/query?query=" + metric, ct));
                Assert.AreEqual(0d, double.Parse(payload.RootElement.GetProperty("data").GetProperty("result")[0]
                    .GetProperty("value")[1].GetString()!, CultureInfo.InvariantCulture));
            }
        }
        finally
        {
            // 失败时仍保存实际规则状态与传输日志，不能用配置存在替代触发证据。
            using var diagnostics = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await File.WriteAllTextAsync(Path.Combine(evidence, "last-rules.json"), await http.GetStringAsync("api/v1/rules", diagnostics.Token), diagnostics.Token);
                var logs = await collector.GetLogsAsync(ct: diagnostics.Token);
                await File.WriteAllTextAsync(Path.Combine(evidence, "collector.log"), logs.Stdout + logs.Stderr, diagnostics.Token);
            }
            catch (Exception exception) { TestContext.WriteLine("告警诊断保存失败：" + exception.GetType().Name); }
        }

        async Task WaitForAlertsAsync(bool firing)
        {
            var started = DateTime.UtcNow;
            var metricsChecked = false;
            var until = DateTime.UtcNow.AddMinutes(firing ? 9 : 3);
            do
            {
                await using var scope = factory.Services.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
                await scope.ServiceProvider.GetRequiredService<NotificationDeliveryBatchProcessor>().SampleBacklogAsync(ct);
                Assert.IsTrue(meter.ForceFlush(5000), "真实通知指标未完成导出。");
                if (firing && !metricsChecked && DateTime.UtcNow - started > TimeSpan.FromMinutes(2))
                {
                    var raw = await http.GetStringAsync("api/v1/query?query=" + Uri.EscapeDataString("{__name__=~\"fullnet_notifications_delivery_.*\"}"), ct);
                    await File.WriteAllTextAsync(Path.Combine(evidence, "exported-series.json"), raw, ct);
                    using var series = JsonDocument.Parse(raw);
                    var names = series.RootElement.GetProperty("data").GetProperty("result").EnumerateArray()
                        .Select(item => item.GetProperty("metric").GetProperty("__name__").GetString()).ToArray();
                    Assert.IsTrue(Metrics.All(name => names.Contains(name)), "真实出口名称未匹配告警规则，原始序列见：" + evidence);
                    metricsChecked = true;
                }
                using var document = JsonDocument.Parse(await http.GetStringAsync("api/v1/rules", ct));
                var rulesState = document.RootElement.GetProperty("data").GetProperty("groups").EnumerateArray()
                    .SelectMany(group => group.GetProperty("rules").EnumerateArray())
                    .Where(rule => rule.TryGetProperty("name", out var name) && Alerts.Contains(name.GetString()))
                    .ToArray();
                Assert.AreEqual(3, rulesState.Length);
                Assert.IsTrue(rulesState.All(rule => rule.GetProperty("health").GetString() == "ok"));
                if (rulesState.All(rule => rule.GetProperty("state").GetString() == (firing ? "firing" : "inactive")))
                {
                    TestContext.WriteLine($"{DateTimeOffset.UtcNow:O} 三类通知告警：{(firing ? "firing" : "inactive")}");
                    return;
                }
                await Task.Delay(1000, ct);
            } while (DateTime.UtcNow < until);
            throw new TimeoutException("通知告警未达到预期状态：" + (firing ? "firing" : "inactive"));
        }
    }

    private static ContainerBuilder Budget(ContainerBuilder builder) => builder.WithCreateParameterModifier(p =>
    {
        var config = p.HostConfig ??= new();
        config.Memory = 512L * 1024 * 1024;
        config.NanoCPUs = 1_000_000_000;
    });
}
