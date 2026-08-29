using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Benchmarks.MixedLoad;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.Dapper.Outbox;
using Full.NET.Messaging.Abstractions;
using Full.NET.Serialization.MemoryPack;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace Full.NET.Benchmarks.Outbox;

public static class OutboxWriteProfileRunner
{
    private const string LegacyEventType = "benchmark.outbox.write.profile.legacy";
    private const string AppendEventType = "benchmark.outbox.write.profile.append";
    private const int SchemaVersion = 1;

    public static async Task RunAsync(
        OutboxWriteProfileOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        Directory.CreateDirectory(options.OutputDirectory);
        var results = new List<OutboxWriteProfileRunResult>();
        foreach (var provider in options.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var poolName =
                $"fullnet-outbox-write-profile-{provider}-{Guid.NewGuid():N}";
            await using var database = await MixedLoadDatabase.StartAsync(
                provider,
                poolName,
                cancellationToken);
            foreach (var scenario in OutboxWriteProfileScenarioMatrix.Create(options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Console.WriteLine(
                    $"[{provider}] {scenario.Target} "
                    + $"path={scenario.CommandPath.ToToken()} "
                    + $"concurrency={scenario.Concurrency} "
                    + $"repeat {scenario.Repetition}/{options.Repetitions}");
                results.Add(await RunScenarioAsync(
                    database,
                    poolName,
                    provider,
                    scenario.Target,
                    scenario.CommandPath,
                    scenario.Concurrency,
                    scenario.Repetition,
                    options,
                    cancellationToken));
            }
        }

        await OutboxWriteProfileReportWriter.WriteAsync(
            options,
            results,
            cancellationToken);
        Console.WriteLine(
            $"Outbox write profile artifacts: "
            + $"{Path.GetFullPath(options.OutputDirectory)}");
    }

    private static async Task<OutboxWriteProfileRunResult> RunScenarioAsync(
        MixedLoadDatabase database,
        string poolName,
        string provider,
        OutboxWriteProfileTarget target,
        OutboxWriteProfileCommandPath commandPath,
        int concurrency,
        int repetition,
        OutboxWriteProfileOptions options,
        CancellationToken cancellationToken)
    {
        await ResetTargetTableAsync(database, target, cancellationToken);
        if (target == OutboxWriteProfileTarget.AppendOnly)
        {
            await ResetLegacyTableAsync(database, cancellationToken);
        }
        await using var services = BuildServices(
            database,
            target,
            commandPath);
        using var dapperTelemetry = new MixedLoadDapperTelemetry();
        using var connectionTelemetry =
            new MixedLoadDatabaseConnectionTelemetry(provider);
        using var poolTelemetry = MixedLoadConnectionPoolTelemetry.Create(
            database.Provider,
            poolName);
        await RunWindowAsync(
            services,
            target,
            concurrency,
            options.PayloadSizeBytes,
            options.Warmup,
            new ConcurrentQueue<double>(),
            new OutboxWriteProfileCounters(),
            cancellationToken);
        await ResetTargetTableAsync(database, target, cancellationToken);
        if (target == OutboxWriteProfileTarget.AppendOnly)
        {
            await ResetLegacyTableAsync(database, cancellationToken);
        }

        var databaseBefore = await database.CaptureStateAsync(cancellationToken);
        dapperTelemetry.Reset();
        connectionTelemetry.Reset();
        poolTelemetry.Reset();
        var processBefore = CaptureProcessResources();
        var writeLatencies = new ConcurrentQueue<double>();
        var counters = new OutboxWriteProfileCounters();
        var windowStarted = Stopwatch.StartNew();
        await RunWindowAsync(
            services,
            target,
            concurrency,
            options.PayloadSizeBytes,
            options.Duration,
            writeLatencies,
            counters,
            cancellationToken);
        windowStarted.Stop();
        var processAfter = CaptureProcessResources();
        var databaseAfter = await database.CaptureStateAsync(cancellationToken);
        var statementName = GetStatementName(target);
        var dapperSnapshot = dapperTelemetry.Snapshot();
        var connectionSnapshot = connectionTelemetry.Snapshot();
        var poolSnapshot = poolTelemetry.Snapshot();
        var latencySamples = writeLatencies.ToArray();
        var writesPerSecond = counters.SuccessfulWrites
            / Math.Max(windowStarted.Elapsed.TotalSeconds, 0.001d);
        return new OutboxWriteProfileRunResult(
            provider,
            database.ContainerImage,
            database.DatabaseVersion,
            target,
            commandPath,
            concurrency,
            repetition,
            options.PayloadSizeBytes,
            windowStarted.Elapsed.TotalSeconds,
            counters.SuccessfulWrites,
            counters.Errors,
            counters.DuplicateAttempts,
            writesPerSecond,
            latencySamples.Length == 0
                ? null
                : MixedLoadLatencyStatistics.Calculate(latencySamples),
            dapperSnapshot.StatementExecutions.GetValueOrDefault(statementName),
            dapperSnapshot.Duration,
            dapperSnapshot.Failures,
            poolSnapshot.WaitDuration,
            poolSnapshot.PeakPendingRequests,
            poolSnapshot.ConnectionTimeouts,
            databaseBefore,
            databaseAfter,
            CreateProcessDelta(processBefore, processAfter, counters.SuccessfulWrites),
            statementName)
        {
            ConnectionAcquisition = connectionSnapshot,
            SqlCancellations = dapperSnapshot.Cancellations,
            SqlFailureReasons = dapperSnapshot.FailureReasons,
            AttemptFailures = counters.GetFailures(),
            WindowCanceledAttempts = counters.WindowCanceledAttempts,
        };
    }

    private static async Task RunWindowAsync(
        ServiceProvider services,
        OutboxWriteProfileTarget target,
        int concurrency,
        int payloadSizeBytes,
        TimeSpan duration,
        ConcurrentQueue<double> latencies,
        OutboxWriteProfileCounters counters,
        CancellationToken cancellationToken)
    {
        using var windowCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        windowCancellation.CancelAfter(duration);
        var workers = Enumerable.Range(0, concurrency)
            .Select(_ => WorkerLoopAsync(
                services,
                target,
                payloadSizeBytes,
                latencies,
                counters,
                windowCancellation.Token))
            .ToArray();
        await Task.WhenAll(workers);
    }

    private static async Task WorkerLoopAsync(
        ServiceProvider services,
        OutboxWriteProfileTarget target,
        int payloadSizeBytes,
        ConcurrentQueue<double> latencies,
        OutboxWriteProfileCounters counters,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var outboxWriter = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        var transaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
        var payload = new byte[payloadSizeBytes];
        while (!cancellationToken.IsCancellationRequested)
        {
            Random.Shared.NextBytes(payload);
            var started = Stopwatch.GetTimestamp();
            try
            {
                await transaction.ExecuteAsync(
                    async token =>
                    {
                        if (target == OutboxWriteProfileTarget.AppendOnly)
                        {
                            var partitionKey = Guid.CreateVersion7().ToString("D");
                            var metadata = IntegrationEventMetadata.Create(
                                partitionKey,
                                "fullnet.benchmark.outboxwriteprofile",
                                correlationId: partitionKey);
                            await outboxWriter.AddAsync(
                                AppendEventType,
                                SchemaVersion,
                                payload,
                                metadata,
                                token).ConfigureAwait(false);
                        }
                        else
                        {
                            await outboxWriter.AddAsync(
                                LegacyEventType,
                                SchemaVersion,
                                payload,
                                token).ConfigureAwait(false);
                        }

                        return true;
                    },
                    cancellationToken).ConfigureAwait(false);
                latencies.Enqueue(
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                counters.RecordSuccess();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                counters.RecordWindowCancellation();
                break;
            }
            catch (Exception exception) when (IsDuplicate(exception))
            {
                counters.RecordDuplicate();
            }
            catch (Exception exception)
            {
                counters.RecordError(
                    exception,
                    cancellationToken.IsCancellationRequested);
            }
        }
    }

    private static bool IsDuplicate(Exception exception) =>
        exception.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
        || exception.Message.Contains("unique", StringComparison.OrdinalIgnoreCase);

    private static async Task ResetLegacyTableAsync(
        MixedLoadDatabase database,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM fn_outbox_message",
            cancellationToken: cancellationToken));
    }

    private static async Task ResetTargetTableAsync(
        MixedLoadDatabase database,
        OutboxWriteProfileTarget target,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        var sql = target switch
        {
            OutboxWriteProfileTarget.LegacyInsert =>
                "DELETE FROM fn_outbox_message",
            OutboxWriteProfileTarget.AppendOnly =>
                "DELETE FROM fn_messaging_outbox_event",
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            cancellationToken: cancellationToken));
    }

    private static DbConnection CreateConnection(MixedLoadDatabase database) =>
        database.Provider switch
        {
            DatabaseProvider.SqlServer =>
                new SqlConnection(database.ConnectionString),
            DatabaseProvider.MySql =>
                new MySqlConnection(database.ConnectionString),
            _ => throw new ArgumentOutOfRangeException(
                nameof(database),
                database.Provider,
                "Unsupported database provider."),
        };

    private static ServiceProvider BuildServices(
        MixedLoadDatabase database,
        OutboxWriteProfileTarget target,
        OutboxWriteProfileCommandPath commandPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] =
                    database.Provider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] =
                    database.ConnectionString,
                [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                    MySqlGuidStorageMode.Binary16.ToString(),
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(NullLoggerProvider.Instance));
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddFullNetDapper(configuration, "Benchmark");
        services.RemoveAll<DapperOutboxWriter>();
        services.RemoveAll<DapperAppendOnlyOutboxWriter>();
        var runtimeCommandPath = commandPath switch
        {
            OutboxWriteProfileCommandPath.Registry =>
                DapperOutboxCommandPath.StaticRegistry,
            OutboxWriteProfileCommandPath.Typed =>
                DapperOutboxCommandPath.TypedPlan,
            _ => throw new ArgumentOutOfRangeException(
                nameof(commandPath),
                commandPath,
                "Unsupported Outbox command path."),
        };
        services.AddScoped(provider =>
            ActivatorUtilities.CreateInstance<DapperOutboxWriter>(
                provider,
                runtimeCommandPath));
        services.AddScoped(provider =>
            ActivatorUtilities.CreateInstance<DapperAppendOnlyOutboxWriter>(
                provider,
                runtimeCommandPath));
        services.AddFullNetMemoryPack();
        services.RemoveAll<IEffectiveEventDeliveryOwnerResolver>();
        services.RemoveAll<IEventStreamOwnershipGate>();
        services.AddSingleton<IEventStreamOwnershipGate, PermissiveOwnershipGate>();
        services.AddSingleton<IEffectiveEventDeliveryOwnerResolver>(
            target == OutboxWriteProfileTarget.AppendOnly
                ? new AppendOnlyOwnerResolver()
                : new LegacyOwnerResolver());
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static string GetStatementName(OutboxWriteProfileTarget target) =>
        target switch
        {
            OutboxWriteProfileTarget.LegacyInsert => "outbox.insert",
            OutboxWriteProfileTarget.AppendOnly => "messaging.outbox.append",
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };

    private static MixedLoadProcessSnapshot CaptureProcessResources()
    {
        using var process = Process.GetCurrentProcess();
        return new MixedLoadProcessSnapshot(
            DateTimeOffset.UtcNow,
            process.TotalProcessorTime.TotalMilliseconds,
            GC.GetTotalAllocatedBytes(precise: false),
            GC.GetGCMemoryInfo().HeapSizeBytes,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }

    private static OutboxWriteProfileProcessDelta CreateProcessDelta(
        MixedLoadProcessSnapshot before,
        MixedLoadProcessSnapshot after,
        long successfulWrites)
    {
        var allocatedDelta = Math.Max(
            0,
            after.TotalAllocatedBytes - before.TotalAllocatedBytes);
        var allocatedPerWrite = successfulWrites == 0
            ? 0
            : allocatedDelta / (double)successfulWrites;
        return new OutboxWriteProfileProcessDelta(
            after.TotalProcessorMilliseconds - before.TotalProcessorMilliseconds,
            allocatedDelta,
            allocatedPerWrite,
            Math.Max(0, after.Gen0Collections - before.Gen0Collections),
            Math.Max(0, after.Gen1Collections - before.Gen1Collections),
            Math.Max(0, after.Gen2Collections - before.Gen2Collections),
            Math.Max(0, after.HeapSizeBytes - before.HeapSizeBytes));
    }

    private sealed class OutboxWriteProfileCounters
    {
        private long _successfulWrites;
        private long _errors;
        private long _duplicateAttempts;
        private long _windowCanceledAttempts;
        private readonly ConcurrentDictionary<
            OutboxWriteProfileFailureClassification,
            long> _failures = new();

        public long SuccessfulWrites => Interlocked.Read(ref _successfulWrites);

        public long Errors => Interlocked.Read(ref _errors);

        public long DuplicateAttempts => Interlocked.Read(ref _duplicateAttempts);

        public long WindowCanceledAttempts =>
            Interlocked.Read(ref _windowCanceledAttempts);

        public void RecordSuccess() =>
            Interlocked.Increment(ref _successfulWrites);

        public void RecordError(Exception exception, bool windowOwned)
        {
            Interlocked.Increment(ref _errors);
            var classification = OutboxWriteProfileFailureClassifier.Classify(
                exception,
                windowOwned);
            _failures.AddOrUpdate(
                classification,
                1,
                (_, value) => value + 1);
        }

        public void RecordDuplicate() =>
            Interlocked.Increment(ref _duplicateAttempts);

        public void RecordWindowCancellation() =>
            Interlocked.Increment(ref _windowCanceledAttempts);

        public IReadOnlyList<OutboxWriteProfileFailureSummary> GetFailures() =>
            _failures
                .OrderBy(pair => pair.Key.Reason, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.DatabaseErrorCode, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.WindowOwned)
                .Select(pair => new OutboxWriteProfileFailureSummary(
                    pair.Key.Reason,
                    pair.Key.DatabaseErrorCode,
                    pair.Key.WindowOwned,
                    pair.Value))
                .ToArray();
    }

    private sealed class LegacyOwnerResolver : IEffectiveEventDeliveryOwnerResolver
    {
        public Task<EventDeliveryOwner> GetDeliveryOwnerAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(EventDeliveryOwner.LegacyPolling);
    }

    private sealed class AppendOnlyOwnerResolver : IEffectiveEventDeliveryOwnerResolver
    {
        public Task<EventDeliveryOwner> GetDeliveryOwnerAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(EventDeliveryOwner.CdcKafka);
    }

    private sealed class PermissiveOwnershipGate : IEventStreamOwnershipGate
    {
        public Task<bool> AcquireProducerAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> AcquireConsumerAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> AcquireOwnershipChangeAsync(
            string eventType,
            int schemaVersion,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}

public sealed record OutboxWriteProfileRunResult(
    string Provider,
    string ContainerImage,
    string DatabaseVersion,
    OutboxWriteProfileTarget Target,
    OutboxWriteProfileCommandPath CommandPath,
    int Concurrency,
    int Repetition,
    int PayloadSizeBytes,
    double ActualDurationSeconds,
    long SuccessfulWrites,
    long Errors,
    long DuplicateAttempts,
    double WritesPerSecond,
    MixedLoadLatencyStatistics? WriteLatency,
    long StatementExecutions,
    MixedLoadLatencyStatistics? SqlLatency,
    long SqlFailures,
    MixedLoadLatencyStatistics? ConnectionWait,
    double? PeakPendingConnections,
    long? ConnectionTimeouts,
    MixedLoadDatabaseSnapshot DatabaseBefore,
    MixedLoadDatabaseSnapshot DatabaseAfter,
    OutboxWriteProfileProcessDelta Process,
    string StatementName)
{
    /// <summary>Full.NET 会话边界记录的跨 Provider 连接获取等待。</summary>
    public MixedLoadDatabaseConnectionSnapshot? ConnectionAcquisition { get; init; }

    /// <summary>Dapper SQL 在测量窗口结束时观察到的取消次数。</summary>
    public long SqlCancellations { get; init; }

    /// <summary>按稳定低基数原因汇总的非取消 SQL 失败。</summary>
    public IReadOnlyDictionary<string, long> SqlFailureReasons { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);

    /// <summary>写入尝试失败的稳定分类、Provider 错误码与窗口归属。</summary>
    public IReadOnlyList<OutboxWriteProfileFailureSummary> AttemptFailures
    {
        get;
        init;
    } = [];

    /// <summary>因测量窗口按期结束而取消的在途写入尝试数。</summary>
    public long WindowCanceledAttempts { get; init; }
}

public sealed record OutboxWriteProfileProcessDelta(
    double CpuMilliseconds,
    long TotalAllocatedBytes,
    double AllocatedBytesPerWrite,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long HeapGrowthBytes);

public static class OutboxWriteProfileReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task WriteAsync(
        OutboxWriteProfileOptions options,
        IReadOnlyList<OutboxWriteProfileRunResult> results,
        CancellationToken cancellationToken)
    {
        var reportPath = Path.Combine(
            options.OutputDirectory,
            "outbox-write-profile.json");
        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            baselineCommit = await ReadGitHeadAsync(cancellationToken),
            environment = new
            {
                os = Environment.OSVersion.VersionString,
                processorCount = Environment.ProcessorCount,
                runtime = Environment.Version.ToString(),
            },
            options = new
            {
                providers = options.Providers,
                concurrency = options.ConcurrencyLevels,
                targets = options.Targets,
                commandPaths = options.CommandPaths,
                payloadSizeBytes = options.PayloadSizeBytes,
                repetitions = options.Repetitions,
                warmupSeconds = options.Warmup.TotalSeconds,
                durationSeconds = options.Duration.TotalSeconds,
            },
            results,
        };
        await File.WriteAllTextAsync(
            reportPath,
            JsonSerializer.Serialize(payload, JsonOptions),
            cancellationToken);
    }

    private static async Task<string> ReadGitHeadAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return "unknown";
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return output.Trim();
        }
        catch
        {
            return "unknown";
        }
    }
}
