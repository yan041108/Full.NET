using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Full.NET.Messaging.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>实际数据库平台采集，专用容器内停止/恢复 Capture Job 和修改 Binlog 保留；不操作共享实例。</summary>
[TestClass]
[DoNotParallelize]
public sealed class MessagingCdcHealthCollectionTests
{
    private static readonly string Password = "Health-" + Guid.NewGuid().ToString("N") + "!";
    [TestMethod]
    public async Task SqlServer_capture_job_stop_recovery_and_permission_failure_are_observed()
    {
        await using var server = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword(Password).WithEnvironment("MSSQL_AGENT_ENABLED", "true")
            .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2048").WithCreateParameterModifier(parameters =>
            {
                var limits = parameters.HostConfig ??= new();
                limits.Memory = 3L << 30; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000;
            }).Build();
        await server.StartAsync();
        var settings = new SqlConnectionStringBuilder(server.GetConnectionString());
        await using (var admin = new SqlConnection(settings.ConnectionString)) await admin.ExecuteAsync("CREATE DATABASE fullnet_cdc_health");
        settings.InitialCatalog = "fullnet_cdc_health";
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("CREATE TABLE dbo.fn_messaging_stream_ownership (RollbackState int NOT NULL); CREATE TABLE dbo.fn_messaging_outbox_event (Id uniqueidentifier NOT NULL PRIMARY KEY)");
        await connection.ExecuteAsync("EXEC sys.sp_cdc_enable_db");
        await connection.ExecuteAsync("EXEC sys.sp_cdc_enable_table @source_schema=N'dbo', @source_name=N'fn_messaging_outbox_event', @role_name=NULL, @capture_instance=N'fullnet_fn_messaging_outbox_event', @supports_net_changes=0");
        var options = new DatabaseOptions { Provider = DatabaseProvider.SqlServer, ConnectionString = settings.ConnectionString };
        await using var services = Services(options);
        await WaitAsync(async () => (await ReadAsync(services)).CaptureJobRunning == true, "实际 Capture Job 运行");
        var first = Guid.CreateVersion7(); var second = Guid.CreateVersion7();
        await connection.ExecuteAsync("INSERT INTO dbo.fn_messaging_outbox_event (Id) VALUES (@Id)", new { Id = first });
        await WaitAsync(async () => await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cdc.fullnet_fn_messaging_outbox_event_CT WHERE Id=@Id", new { Id = first }) == 1, "源位点首条实际 CDC 事件");
        var oldLsn = await connection.ExecuteScalarAsync<byte[]>("SELECT __$start_lsn FROM cdc.fullnet_fn_messaging_outbox_event_CT WHERE Id=@Id", new { Id = first });
        var oldPosition = CdcDeliveryPosition.ForSqlServerBytes(null, oldLsn!);
        Assert.AreEqual(true, (await ReadSourceAsync(services, oldPosition)).Recoverable);
        await connection.ExecuteAsync("INSERT INTO dbo.fn_messaging_outbox_event (Id) VALUES (@Id)", new { Id = second });
        await WaitAsync(async () => await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cdc.fullnet_fn_messaging_outbox_event_CT WHERE Id=@Id", new { Id = second }) == 1, "源位点后续实际 CDC 事件");
        var newLsn = await connection.ExecuteScalarAsync<byte[]>("SELECT __$start_lsn FROM cdc.fullnet_fn_messaging_outbox_event_CT WHERE Id=@Id", new { Id = second });
        await connection.ExecuteAsync("sys.sp_cdc_cleanup_change_table", new
        {
            capture_instance = "fullnet_fn_messaging_outbox_event", low_water_mark = newLsn, threshold = 5000,
        }, commandType: System.Data.CommandType.StoredProcedure);
        Assert.AreEqual(false, (await ReadSourceAsync(services, oldPosition)).Recoverable, "真实 CDC 清理后旧 LSN 不可恢复。");
        var currentPosition = CdcDeliveryPosition.ForSqlServerBytes(null, newLsn!);
        var currentSource = await ReadSourceAsync(services, currentPosition);
        Assert.AreEqual(true, currentSource.Recoverable);
        Assert.IsTrue(currentSource.SourceLagSeconds is >= 0);
        await connection.ExecuteAsync("EXEC sys.sp_cdc_stop_job @job_type=N'capture'");
        await WaitAsync(async () => (await ReadAsync(services)).CaptureJobRunning == false, "实际 Capture Job 停止");
        await connection.ExecuteAsync("EXEC sys.sp_cdc_start_job @job_type=N'capture'");
        await WaitAsync(async () => (await ReadAsync(services)).CaptureJobRunning == true, "实际 Capture Job 恢复");

        // 低权限账号只读本测试库保护表；缺少 DMV/msdb 权限必须失败，不能因查询不到行而报健康。
        var password = "Health-" + Guid.NewGuid().ToString("N") + "!";
        await connection.ExecuteAsync($"CREATE LOGIN fullnet_cdc_health_reader WITH PASSWORD='{password}'; CREATE USER fullnet_cdc_health_reader FOR LOGIN fullnet_cdc_health_reader; GRANT SELECT ON dbo.fn_messaging_stream_ownership TO fullnet_cdc_health_reader");
        settings.UserID = "fullnet_cdc_health_reader"; settings.Password = password;
        options.ConnectionString = settings.ConnectionString;
        await using var restricted = Services(options);
        await Assert.ThrowsAsync<SqlException>(() => ReadAsync(restricted));
        Assert.IsNull((await ReadSourceAsync(restricted, currentPosition)).Recoverable, "CDC 最小 LSN 为零可能是权限不足，必须未知。");
    }

    [TestMethod]
    public async Task MySql_real_binlog_retention_and_fence_are_read_without_assumed_defaults()
    {
        await using var server = new MySqlBuilder("mysql:8.0")
            .WithDatabase("fullnet_cdc_health").WithUsername("root").WithPassword(Password)
            .WithCommand("--log-bin=mysql-bin", "--binlog-format=ROW", "--binlog-row-image=FULL", "--binlog-expire-logs-seconds=43200")
            .WithCreateParameterModifier(parameters =>
            {
                var limits = parameters.HostConfig ??= new();
                limits.Memory = 1536L << 20; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000;
            }).Build();
        await server.StartAsync();
        var options = new DatabaseOptions { Provider = DatabaseProvider.MySql, ConnectionString = server.GetConnectionString(), MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16 };
        await using var connection = new MySqlConnection(MySqlConnectionStringPolicy.Create(options.ConnectionString, options.MySqlGuidStorageMode, false));
        await connection.OpenAsync();
        await connection.ExecuteAsync("CREATE TABLE fn_messaging_stream_ownership (RollbackState int NOT NULL); INSERT INTO fn_messaging_stream_ownership VALUES (1)");
        await using var services = Services(options);
        var sample = await ReadAsync(services);
        Assert.AreEqual("mysql", sample.Provider); Assert.IsNull(sample.CaptureJobRunning);
        Assert.AreEqual(12d, sample.BinlogRetentionHours); Assert.IsTrue(sample.ProducerFenced);
        await connection.ExecuteAsync("SET GLOBAL binlog_expire_logs_seconds=172800; UPDATE fn_messaging_stream_ownership SET RollbackState=0");
        sample = await ReadAsync(services);
        Assert.AreEqual(48d, sample.BinlogRetentionHours); Assert.IsFalse(sample.ProducerFenced);
        await connection.ExecuteAsync("SET GLOBAL binlog_expire_logs_seconds=0");
        Assert.AreEqual(0d, (await ReadAsync(services)).BinlogRetentionHours, "0 表示关闭自动过期，不能假造为默认 168 小时。");
        var old = await connection.QuerySingleAsync<(string File, long Position, string Include, string Exclude, string Gtid)>("SHOW MASTER STATUS");
        var oldPosition = CdcDeliveryPosition.ForMySql(null, old.File, old.Position);
        Assert.AreEqual(true, (await ReadSourceAsync(services, oldPosition)).Recoverable);
        await connection.ExecuteAsync("FLUSH BINARY LOGS");
        var current = await connection.QuerySingleAsync<(string File, long Position, string Include, string Exclude, string Gtid)>("SHOW MASTER STATUS");
        Assert.AreEqual(true, (await ReadSourceAsync(services, oldPosition)).Recoverable, "轮转但未清理时旧文件仍可恢复。");
        Assert.IsTrue(current.File.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-'));
        // 文件名只来自本用例真实服务器并经过白名单；仅清理独享实例中的旧日志。
        await connection.ExecuteAsync($"PURGE BINARY LOGS TO '{current.File}'");
        Assert.AreEqual(false, (await ReadSourceAsync(services, oldPosition)).Recoverable, "真实 PURGE 后不得继续报告旧位点可恢复。");
        var currentPosition = CdcDeliveryPosition.ForMySql(null, current.File, current.Position);
        Assert.AreEqual(true, (await ReadSourceAsync(services, currentPosition)).Recoverable);
        var password = "Health-" + Guid.NewGuid().ToString("N") + "!";
        await connection.ExecuteAsync($"CREATE USER fullnet_source_health_reader IDENTIFIED BY '{password}'; GRANT SELECT ON fullnet_cdc_health.* TO fullnet_source_health_reader");
        options.ConnectionString = new MySqlConnectionStringBuilder(options.ConnectionString) { UserID = "fullnet_source_health_reader", Password = password }.ConnectionString;
        await using var restricted = Services(options);
        await Assert.ThrowsAsync<MySqlException>(() => ReadSourceAsync(restricted, currentPosition));
    }

    private static async Task<CdcSourcePositionHealthSnapshot> ReadSourceAsync(ServiceProvider services, CdcDeliveryPosition position)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await scope.ServiceProvider.GetRequiredService<ICdcPlatformHealthReader>()
            .ReadSourcePositionAsync("fullnet_cdc_health", position, deadline.Token);
    }

    [TestMethod]
    [DataRow(10000)]
    [DataRow(20000)]
    public async Task MySql_gtid_checkpoint_time_uses_completed_transactions_and_missing_history_is_unknown(int historySize)
    {
        await using var server = new MySqlBuilder("mysql:8.0").WithDatabase("fullnet_cdc_health").WithUsername("root").WithPassword(Password)
            .WithCommand("--server-id=12002", "--log-bin=mysql-bin", "--gtid-mode=ON", "--enforce-gtid-consistency=ON",
                "--performance-schema-consumer-events-transactions-history-long=ON", "--performance-schema-instrument=transaction=ON",
                $"--performance-schema-events-transactions-history-long-size={historySize}")
            .WithCreateParameterModifier(parameters =>
            {
                var limits = parameters.HostConfig ??= new();
                limits.Memory = 1536L << 20; limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000;
            }).Build();
        await server.StartAsync();
        var options = new DatabaseOptions { Provider = DatabaseProvider.MySql, ConnectionString = server.GetConnectionString(), MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16 };
        await using var connection = new MySqlConnection(MySqlConnectionStringPolicy.Create(options.ConnectionString, options.MySqlGuidStorageMode, false));
        await connection.OpenAsync();
        await connection.ExecuteAsync("CREATE TABLE probe (Id int PRIMARY KEY); INSERT INTO probe VALUES(1)");
        var first = await connection.QuerySingleAsync<(string File, long Position, string Include, string Exclude, string Gtid)>("SHOW MASTER STATUS");
        var position = CdcDeliveryPosition.ForMySql(null, first.File, first.Position, first.Gtid);
        Assert.IsTrue(CdcDeliveryPosition.TryParse(position.ToJson(), out var parsed));
        Assert.AreEqual(first.Gtid, parsed!.Binlog!.CompletedGtidSet);
        await Task.Delay(1200);
        await connection.ExecuteAsync("INSERT INTO probe VALUES(2)");
        await using var services = Services(options);
        var lag = await ReadTimeAsync(services, position);
        if (historySize > 10000)
        {
            Assert.IsNull(lag, "超过采样预算的事务历史环不能提供健康时间值。");
            Assert.AreEqual(true, (await ReadSourceAsync(services, position)).Recoverable);
            return;
        }
        Assert.IsTrue(lag is >= 1 and < 15, $"真实提交间隔应约 1.2 秒：{lag}");
        Assert.IsNull(await ReadTimeAsync(services, CdcDeliveryPosition.ForMySql(null, first.File, first.Position)), "GTID 缺失不能推算时间。");
        Assert.IsNull(await ReadTimeAsync(services, CdcDeliveryPosition.ForMySql(null, first.File, first.Position, first.Gtid + ":99")), "有缺口的集合不能证明覆盖。");
        var current = await connection.QuerySingleAsync<(string File, long Position, string Include, string Exclude, string Gtid)>("SHOW MASTER STATUS");
        var caughtUp = CdcDeliveryPosition.ForMySql(null, current.File, current.Position, current.Gtid);
        await Task.Delay(1200);
        Assert.AreEqual(0d, await ReadTimeAsync(services, caughtUp), "静默且已覆盖的流必须为零，而非距最后事件的年龄。");
        Assert.AreEqual(true, (await ReadSourceAsync(services, position)).Recoverable);
        await connection.ExecuteAsync("UPDATE performance_schema.setup_instruments SET TIMED='NO' WHERE NAME='transaction'");
        Assert.IsNull(await ReadTimeAsync(services, position), "关闭计时后不使用旧健康值。");
        await connection.ExecuteAsync("UPDATE performance_schema.setup_instruments SET TIMED='YES' WHERE NAME='transaction'; TRUNCATE performance_schema.events_transactions_history_long");
        Assert.IsNull(await ReadTimeAsync(services, position), "历史淘汰后保持未知。");
        Assert.AreEqual(true, (await ReadSourceAsync(services, position)).Recoverable, "时间证据缺失不得影响文件坐标恢复性。");
        var password = "Time-" + Guid.NewGuid().ToString("N") + "!";
        await connection.ExecuteAsync($"CREATE USER fullnet_time_reader IDENTIFIED BY '{password}'; GRANT SELECT ON fullnet_cdc_health.* TO fullnet_time_reader; GRANT REPLICATION CLIENT ON *.* TO fullnet_time_reader");
        options.ConnectionString = new MySqlConnectionStringBuilder(options.ConnectionString) { UserID = "fullnet_time_reader", Password = password }.ConnectionString;
        await using var restricted = Services(options);
        Assert.AreEqual(true, (await ReadSourceAsync(restricted, position)).Recoverable);
        await Assert.ThrowsAsync<MySqlException>(() => ReadTimeAsync(restricted, position));
    }

    private static async Task<double?> ReadTimeAsync(ServiceProvider services, CdcDeliveryPosition position)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await scope.ServiceProvider.GetRequiredService<ICdcPlatformHealthReader>()
            .ReadSourceTimeLagAsync("fullnet_cdc_health", position, deadline.Token);
    }

    private static async Task<CdcPlatformHealthSnapshot> ReadAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        return await scope.ServiceProvider.GetRequiredService<ICdcPlatformHealthReader>().ReadAsync(deadline.Token);
    }

    internal static ServiceProvider Services(DatabaseOptions options)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = options.Provider.ToString(), ["Database:ConnectionString"] = options.ConnectionString,
            ["Database:MySqlGuidStorageMode"] = "Binary16", ["Database:CommandTimeoutSeconds"] = "10",
        }).Build();
        var services = new ServiceCollection().AddLogging().AddScoped<CurrentTenantAccessor>()
            .AddScoped<ICurrentTenant>(provider => provider.GetRequiredService<CurrentTenantAccessor>())
            .AddScoped<ICurrentTenantContextWriter>(provider => provider.GetRequiredService<CurrentTenantAccessor>())
            .AddSingleton<IClock, SystemClock>();
        services.AddFullNetDapper(configuration, "Testing");
        return services.BuildServiceProvider();
    }

    private static async Task WaitAsync(Func<Task<bool>> predicate, string reason)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (!await predicate()) await Task.Delay(500, timeout.Token);
        Assert.IsFalse(timeout.IsCancellationRequested, reason);
    }
}
