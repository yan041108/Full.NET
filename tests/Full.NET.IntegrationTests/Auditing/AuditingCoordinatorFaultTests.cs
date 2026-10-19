using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Auditing;

/// <summary>真实 Host 的 B1 协调器故障验证；串行执行以隔离进程级 Meter 与临时库表锁。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AuditingCoordinatorFaultTests
{
    private string? _ownedConnectionString;
    private DatabaseProvider _ownedProvider;

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "deadline")]
    [DataRow(DatabaseProvider.MySql, "deadline")]
    [DataRow(DatabaseProvider.SqlServer, "disconnect")]
    [DataRow(DatabaseProvider.MySql, "disconnect")]
    [DataRow(DatabaseProvider.SqlServer, "stop")]
    [DataRow(DatabaseProvider.MySql, "stop")]
    public async Task Coordinator_fault_releases_waiters_and_queue_budget(DatabaseProvider provider, string failure)
    {
        var observation = new CommandObservation();
        _ownedProvider = provider;
        _ownedConnectionString = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, _ownedConnectionString,
            new Dictionary<string, string?>
            {
                ["Auditing:MicroBatch:Capacity"] = "2",
                ["Auditing:MicroBatch:MaxBatchRows"] = "1",
                ["Auditing:MicroBatch:MaxQueueAge"] = "00:00:02",
                ["Auditing:MicroBatch:EnqueueTimeout"] = "00:00:00.100",
                ["Auditing:MicroBatch:BatchExecutionTimeout"] = "00:00:10",
            }, configureTestServices: services =>
                // 只包裹真实 Writer 的命令；事务、连接及取消仍走项目自有 Data 边界。
                services.AddScoped<AuditWriteBatchWriter>(services =>
                    ActivatorUtilities.CreateInstance<AuditWriteBatchWriter>(services,
                        new ObservedCommands(services.GetRequiredService<ICommandExecutor>(),
                            services.GetRequiredService<IQueryExecutor>(), provider, observation))));
        await factory.InitializeAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = deadline.Token;
        var coordinator = factory.Services.GetRequiredService<AuditMicroBatchCoordinator>();
        using var metrics = new MetricsObservation();
        observation.Armed = true;
        var trace = "b1-coordinator-" + Guid.NewGuid().ToString("N");
        var active = Create(trace + "-active");
        var queued = new[] { Create(trace + "-queued-a"), Create(trace + "-queued-b") };
        long expectedBytes = AuditWriteEnvelope.ForOperation(active).EstimatedBytes
            + queued.Sum(model => AuditWriteEnvelope.ForOperation(model).EstimatedBytes);
        using var request = new CancellationTokenSource();
        var pending = new List<Task>();
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
        var queries = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        async Task<long> Count(string value) => await queries.QuerySingleOrDefaultAsync<long>(
            new SqlStatement("test.auditing.count_coordinator_fault",
                "SELECT COUNT(*) FROM fn_auditing_operation_log WHERE TraceId = @TraceId", SqlDataScope.Global),
            new { TraceId = value }, token);
        var blocker = await AuditingBatchFaultAssertions.TableBlocker.AcquireAsync(factory, token);
        var lockReleased = false;
        try
        {
            pending.Add(coordinator.FlushImportantAsync(active, null, request.Token));
            await observation.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            var blockedSession = await blocker.WaitForBlockedInsertAsync(factory, observation.SessionId, token);
            foreach (var model in queued)
                pending.Add(coordinator.FlushImportantAsync(model, null, token));
            await WaitAsync(() => coordinator.QueueBytesInUse == expectedBytes
                && metrics.Gauge("depth") == 2, token);
            await request.CancelAsync();
            Assert.IsFalse(pending[0].IsCompleted, "接受后的请求取消不能绕过实际 B1 写入尝试。");
            await coordinator.FlushImportantAsync(Create(trace + "-rejected"), null, token)
                .WaitAsync(TimeSpan.FromSeconds(2), token);
            Assert.AreEqual(expectedBytes, coordinator.QueueBytesInUse,
                "入队失败只归还自己的预约，不能提前释放实际在途批次的字节。");
            Assert.AreEqual(1, observation.Active);
            Assert.AreEqual(3d, metrics.Value("accepted", "kind=operation"));
            Assert.AreEqual(1d, metrics.Value("rejected", "reason=enqueue_timeout"));
            Assert.AreEqual(2d, metrics.Gauge("capacity"));
            Assert.AreEqual((double)expectedBytes, metrics.Gauge("bytes"));
            // 让两条积压项实际过期；断连或停机后不得再启动其数据库命令。
            await Task.Delay(TimeSpan.FromMilliseconds(2_200), token);
            Assert.IsTrue(metrics.Gauge("oldest_age") >= 2);
            Assert.IsFalse(pending.Any(task => task.IsCompleted));
            if (failure == "disconnect")
                await blocker.DisconnectWriterAsync(factory, blockedSession, token);
            if (failure == "stop")
                await coordinator.StopAsync(token).WaitAsync(TimeSpan.FromSeconds(20), token);
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(20), token);
            await WaitAsync(() => coordinator.QueueBytesInUse == 0, token);
            Assert.IsTrue(pending.All(task => task.IsCompletedSuccessfully));
            Assert.AreEqual(0, observation.Active, "不能仅取消请求等待而留下真实 I/O。");
            Assert.AreEqual(1, observation.Attempts, "过期积压及基础设施故障均不得触发重放或毒记录二分。");
            Assert.AreEqual(2d, metrics.Value("rejected", "reason=queue_expired"));
            var outcome = failure == "deadline" ? "batch_timeout"
                : failure == "stop" ? "cancelled" : "infrastructure";
            Assert.AreEqual(1d, metrics.Value("failed", "outcome=" + outcome));
            Assert.AreEqual(0d, metrics.Value("poisoned"));
            Assert.AreEqual(0d, metrics.Gauge("depth"));
            Assert.AreEqual(0d, metrics.Gauge("bytes"));
            Assert.AreEqual(0d, metrics.Gauge("oldest_age"));
            Assert.IsTrue(metrics.Value("wait") > 0, "必须记录实际 B1 等待时间。");
            await blocker.DisposeAsync();
            lockReleased = true;
            Assert.AreEqual(0L, await Count(active.TraceId!));
            foreach (var model in queued) Assert.AreEqual(0L, await Count(model.TraceId!));
            if (failure != "stop")
            {
                var recovered = Create(trace + "-recovered");
                await coordinator.FlushImportantAsync(recovered, null, token)
                    .WaitAsync(TimeSpan.FromSeconds(10), token);
                await WaitAsync(() => coordinator.QueueBytesInUse == 0, token);
                Assert.AreEqual(1L, await Count(recovered.TraceId!));
                Assert.AreEqual(2, observation.Attempts, "故障解除后原协调器必须恢复，且不重放失败项。");
                Assert.AreEqual(0, observation.Active);
                Assert.AreEqual(1d, metrics.Value("flushed"));
                Assert.AreEqual(1d, metrics.Value("batch_rows"));
                Assert.IsTrue(metrics.Value("batch_bytes") > 0);
            }
            Assert.AreEqual(0, metrics.InvalidLabels.Count, "真实指标出现未批准的标签。");
        }
        finally
        {
            // 无论断言结果如何，先释放自有表锁，再终结协调器并等待所有已接受调用。
            try
            {
                if (!lockReleased) await blocker.DisposeAsync();
            }
            finally
            {
                try { await coordinator.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)); }
                finally { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(30)); }
            }
        }

        Assert.AreEqual(0L, await Count(active.TraceId!));
        foreach (var model in queued) Assert.AreEqual(0L, await Count(model.TraceId!));
        Assert.AreEqual(0L, await Count(trace + "-rejected"));
        // Stop 后的原实例必须拒绝新写入，不能复活消费者或泄漏预约。
        await coordinator.FlushImportantAsync(Create(trace + "-closed"), null, token);
        Assert.AreEqual(1d, metrics.Value("rejected", "reason=channel_closed"));
        Assert.AreEqual(0L, coordinator.QueueBytesInUse);
        Assert.AreEqual(0L, await Count(trace + "-closed"));
    }

    [TestCleanup]
    public async Task Delete_owned_database_after_host_disposal()
    {
        if (_ownedConnectionString is null) return;
        var sqlServer = _ownedProvider == DatabaseProvider.SqlServer;
        var database = sqlServer
            ? SharedDatabaseFixture.GetSqlServerDatabaseName(_ownedConnectionString)
            : SharedDatabaseFixture.GetMySqlDatabaseName(_ownedConnectionString);
        // 只允许本用例刚创建的随机数据库；Host/作用域已由测试方法的 using 完成释放。
        Assert.IsTrue(database.StartsWith("fullnet_it_", StringComparison.Ordinal)
            && Guid.TryParseExact(database["fullnet_it_".Length..], "N", out _), "拒绝清理非自有临时库。");
        await using DbConnection admin = sqlServer
            ? new SqlConnection(new SqlConnectionStringBuilder(
                await SharedDatabaseFixture.GetSqlServerMasterConnectionStringAsync()) { Pooling = false }.ConnectionString)
            : new MySqlConnection(new MySqlConnectionStringBuilder(
                await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync()) { Pooling = false }.ConnectionString);
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await admin.OpenAsync(cleanup.Token);
        await using var command = admin.CreateCommand();
        command.CommandTimeout = 15;
        if (sqlServer)
        {
            var quoted = SharedDatabaseFixture.QuoteSqlServerIdent(database);
            command.CommandText = $"ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted};";
        }
        else
        {
            var quoted = SharedDatabaseFixture.QuoteMySqlIdent(database);
            command.CommandText = $"DROP DATABASE {quoted}; REVOKE ALL PRIVILEGES ON {quoted}.* FROM '{SharedDatabaseFixture.MySqlApplicationUserName}'@'%';";
        }
        await command.ExecuteNonQueryAsync(cleanup.Token);
        Console.Error.WriteLine($"[b1-coordinator] owned database removed: {database}");
    }

    private static OperationLogWriteModel Create(string trace) => new(
        "auditing.coordinator.probe", "POST", "/api/v1/auditing/coordinator-probe", 200, 1, true,
        null, null, trace, null, "auditing.coordinator.probe");

    private static async Task WaitAsync(Func<bool> condition, CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        while (!condition())
        {
            Assert.IsTrue(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(5), "协调器状态未在预算内收敛。");
            await Task.Delay(10, token);
        }
    }

    private sealed class CommandObservation
    {
        public volatile bool Armed;
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Attempts;
        public int Active;
        public long SessionId;
    }

    private sealed class ObservedCommands(ICommandExecutor inner, IQueryExecutor queries,
        DatabaseProvider provider, CommandObservation observation) : ICommandExecutor
    {
        public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (!observation.Armed) return await inner.ExecuteAsync(statement, parameters, cancellationToken);
            Interlocked.Increment(ref observation.Attempts);
            Interlocked.Increment(ref observation.Active);
            try
            {
                observation.SessionId = await queries.QuerySingleOrDefaultAsync<long>(
                    new SqlStatement("test.auditing.coordinator_session",
                        provider == DatabaseProvider.SqlServer ? "SELECT CAST(@@SPID AS bigint)" : "SELECT CONNECTION_ID()",
                        SqlDataScope.Global), cancellationToken: cancellationToken);
                observation.Started.TrySetResult();
                return await inner.ExecuteAsync(statement, parameters, cancellationToken);
            }
            catch (Exception exception) { observation.Started.TrySetException(exception); throw; }
            finally { Interlocked.Decrement(ref observation.Active); }
        }
    }

    /// <summary>记录本用例真实仪器；指标只允许既定的低基数标签，不写入 TraceId 等请求值。</summary>
    private sealed class MetricsObservation : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentDictionary<string, double> _values = new(StringComparer.Ordinal);
        public ConcurrentDictionary<string, byte> InvalidLabels { get; } = new(StringComparer.Ordinal);

        public MetricsObservation()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == AuditMicroBatchTelemetry.MeterName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
            _listener.Start();
        }

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var labels = new List<string>();
            foreach (var tag in tags)
            {
                // Telemetry 会吞掉旁路异常，因此回调只记录问题，在测试线程统一断言。
                if (tag.Key is not ("kind" or "reason" or "outcome")) InvalidLabels.TryAdd(tag.Key, 0);
                labels.Add(tag.Key + "=" + tag.Value);
            }
            var key = instrument.Name + ":" + string.Join(',', labels);
            if (instrument.Name.Contains(".queue.", StringComparison.Ordinal)) _values[key] = value;
            else _values.AddOrUpdate(key, value, (_, current) => current + value);
        }

        public double Value(string name, string labels = "") =>
            _values.GetValueOrDefault("fullnet.auditing.microbatch." + name + ":" + labels);

        public double Gauge(string name)
        {
            _listener.RecordObservableInstruments();
            var key = "fullnet.auditing.microbatch.queue." + name + ":";
            Assert.IsTrue(_values.ContainsKey(key), "缺失真实仪器：" + key);
            return _values[key];
        }

        public void Dispose() => _listener.Dispose();
    }
}
