using System.Text.Json;
using Dapper;
using DotNet.Testcontainers.Builders;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Kafka;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>原规则、真实 CDC 来源和真实 Meter 出口，经 Prometheus 实际等待告警 for 时长并核对恢复。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MessagingCdcAlertLoopTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Real_cdc_samples_fire_and_resolve_job_retention_staleness_and_exporter_alerts()
    {
        var password = "Health-" + Guid.NewGuid().ToString("N") + "!";
        await using var sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword(password).WithEnvironment("MSSQL_AGENT_ENABLED", "true").WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2048")
            .WithCreateParameterModifier(p => { var limits = p.HostConfig ??= new(); limits.Memory = 3L << 30; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000; }).Build();
        await using var mysql = new MySqlBuilder("mysql:8.0").WithDatabase("fullnet_alert_health").WithUsername("root").WithPassword(password)
            .WithCommand("--log-bin=mysql-bin", "--binlog-format=ROW", "--binlog-expire-logs-seconds=43200")
            .WithCreateParameterModifier(p => { var limits = p.HostConfig ??= new(); limits.Memory = 1536L << 20; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000; }).Build();
        await sql.StartAsync(); await mysql.StartAsync();
        var sqlSettings = new SqlConnectionStringBuilder(sql.GetConnectionString());
        await using (var admin = new SqlConnection(sqlSettings.ConnectionString)) await admin.ExecuteAsync("CREATE DATABASE fullnet_alert_health");
        sqlSettings.InitialCatalog = "fullnet_alert_health";
        await using var sqlConnection = new SqlConnection(sqlSettings.ConnectionString); await sqlConnection.OpenAsync();
        await using var mysqlConnection = new MySqlConnection(mysql.GetConnectionString()); await mysqlConnection.OpenAsync();
        await sqlConnection.ExecuteAsync("CREATE TABLE dbo.fn_messaging_stream_ownership (RollbackState int NOT NULL); CREATE TABLE dbo.fn_messaging_outbox_event (Id uniqueidentifier NOT NULL PRIMARY KEY); EXEC sys.sp_cdc_enable_db");
        await sqlConnection.ExecuteAsync("EXEC sys.sp_cdc_enable_table @source_schema=N'dbo', @source_name=N'fn_messaging_outbox_event', @role_name=NULL, @capture_instance=N'fullnet_fn_messaging_outbox_event', @supports_net_changes=0");
        await mysqlConnection.ExecuteAsync("CREATE TABLE fn_messaging_stream_ownership (RollbackState int NOT NULL)");
        await using var sqlServices = MessagingCdcHealthCollectionTests.Services(new() { Provider = DatabaseProvider.SqlServer, ConnectionString = sqlSettings.ConnectionString });
        await using var mysqlServices = MessagingCdcHealthCollectionTests.Services(new() { Provider = DatabaseProvider.MySql, ConnectionString = mysql.GetConnectionString(), MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16 });
        using var sqlWorker = Worker(sqlServices); using var mysqlWorker = Worker(mysqlServices);
        await using var exporter = await MessagingMetricsTestExporter.StartAsync();
        await sqlWorker.StartAsync(default); await mysqlWorker.StartAsync(default);

        var root = Environment.GetEnvironmentVariable("FULLNET_TEST_REPOSITORY_ROOT") ?? throw new InvalidOperationException("缺少仓库根目录。");
        var directory = Environment.GetEnvironmentVariable("FULLNET_MESSAGING_EVIDENCE_DIR") ?? throw new InvalidOperationException("缺少证据目录。");
        var config = Path.Combine(directory, "prometheus-cdc.yml");
        await File.WriteAllTextAsync(config, $"global:\n  scrape_interval: 2s\n  evaluation_interval: 2s\nrule_files:\n  - /etc/prometheus/fullnet-rules.yaml\nscrape_configs:\n  - job_name: fullnet-messaging-cdc\n    static_configs:\n      - targets: ['host.docker.internal:{exporter.Port}']\n");
        await using var prometheus = new ContainerBuilder("prom/prometheus:v3.15.0@sha256:efd719c99d83b060d9daefdcf00360461adf279f45ef5391f8d111892118753e")
            .WithExtraHost("host.docker.internal", "host-gateway").WithPortBinding(9090, true)
            .WithBindMount(config, "/etc/prometheus/prometheus.yml")
            .WithBindMount(Path.Combine(root, "deploy/observability/prometheus-rules.yaml"), "/etc/prometheus/fullnet-rules.yaml")
            .WithCreateParameterModifier(p => { var limits = p.HostConfig ??= new(); limits.Memory = 512L << 20; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000; })
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9090).ForPath("/-/ready"))).Build();
        await prometheus.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{prometheus.GetMappedPublicPort(9090)}"), Timeout = TimeSpan.FromSeconds(5) };
        await WaitAsync(async () => await QueryAsync("fullnet_messaging_cdc_sqlserver_capture_job_running == 1"), TimeSpan.FromSeconds(60), "真实 SQL Capture Job 健康采集");
        await WaitAsync(async () => await QueryAsync("fullnet_messaging_cdc_mysql_binlog_retention_hours == 12"), TimeSpan.FromSeconds(60), "真实 MySQL 12h 保留采集");
        await sqlConnection.ExecuteAsync("EXEC sys.sp_cdc_stop_job @job_type=N'capture'");
        Mark("Capture Job 已实际停止，等待原 5 分钟告警。");
        await WaitAsync(() => AlertAsync("FullNetMessagingSqlServerCdcCaptureJobStopped"), TimeSpan.FromMinutes(6), "Capture Job 告警 firing");
        await SaveAsync("capture-stopped");
        await sqlConnection.ExecuteAsync("EXEC sys.sp_cdc_start_job @job_type=N'capture'");
        await WaitAsync(async () => !await AlertAsync("FullNetMessagingSqlServerCdcCaptureJobStopped"), TimeSpan.FromSeconds(45), "Capture Job 告警恢复");
        await sqlWorker.StopAsync(default);
        Mark("SQL 采集已停止，出口继续提供观测；等待旧样本失效和原 10 分钟保留告警。");
        await WaitAsync(() => AlertAsync("FullNetMessagingMySqlBinlogRetentionLow"), TimeSpan.FromMinutes(6), "MySQL 原 10 分钟告警 firing");
        await WaitAsync(() => AlertAsync("FullNetMessagingObservationExpired"), TimeSpan.FromMinutes(1), "旧样本过期告警 firing");
        await SaveAsync("retention-and-stale");
        await mysqlConnection.ExecuteAsync("SET GLOBAL binlog_expire_logs_seconds=172800");
        await WaitAsync(async () => !await AlertAsync("FullNetMessagingMySqlBinlogRetentionLow"), TimeSpan.FromSeconds(45), "MySQL 保留告警恢复");
        await sqlWorker.SampleAsync(default);
        await WaitAsync(async () => !await AlertAsync("FullNetMessagingObservationExpired"), TimeSpan.FromSeconds(45), "新鲜实际采集使过期告警恢复");
        exporter.Unavailable = true;
        Mark("实际 exporter 端点返回 503，等待原 2 分钟目标失联告警。");
        await WaitAsync(() => AlertAsync("FullNetMessagingExporterDown"), TimeSpan.FromMinutes(3), "Exporter down 告警 firing");
        await SaveAsync("exporter-unavailable");
        exporter.Unavailable = false; await sqlWorker.SampleAsync(default);
        await WaitAsync(async () => !await AlertAsync("FullNetMessagingExporterDown"), TimeSpan.FromSeconds(45), "Exporter 恢复");
        await SaveAsync("recovered");
        await mysqlWorker.StopAsync(default);
        Mark("真实采集/规则/告警恢复闭环完成；不是全控制面或容量验收。");

        async Task<bool> QueryAsync(string expression)
        {
            using var response = await client.GetAsync("/api/v1/query?query=" + Uri.EscapeDataString(expression));
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("data").GetProperty("result").GetArrayLength() > 0;
        }
        Task<bool> AlertAsync(string name) => QueryAsync($"ALERTS{{alertname=\"{name}\",alertstate=\"firing\"}} == 1");
        async Task SaveAsync(string phase) => await File.WriteAllTextAsync(Path.Combine(directory, "alerts-" + phase + ".json"), await client.GetStringAsync("/api/v1/alerts"));
        void Mark(string message) => TestContext.WriteLine($"{DateTimeOffset.UtcNow:O}: {message}");
    }

    private static KafkaCdcMonitoringWorker Worker(IServiceProvider services) => new(services.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new KafkaCdcMonitoringOptions { Enabled = true, PollSeconds = 10 }), Options.Create(new KafkaConnectRollbackOptions()),
        Options.Create(new KafkaMessagingOptions()), new KafkaConnectAdminClient(new Uri("http://127.0.0.1:1")), new KafkaConsumerLagObserver());

    private static async Task WaitAsync(Func<Task<bool>> action, TimeSpan timeout, string condition)
    {
        using var deadline = new CancellationTokenSource(timeout);
        try { while (!await action()) await Task.Delay(1000, deadline.Token); }
        catch (OperationCanceledException) { Assert.Fail("超时：" + condition); }
    }
}
