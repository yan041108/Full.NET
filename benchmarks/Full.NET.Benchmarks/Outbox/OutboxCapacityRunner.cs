extern alias workerhost;

using System.Collections.Concurrent;
using System.Diagnostics;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Benchmarks.MixedLoad;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Full.NET.Serialization.MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkerHost = workerhost::Full.NET.Host.Worker;

namespace Full.NET.Benchmarks.Outbox;

public static class OutboxCapacityRunner
{
    public static async Task RunAsync(
        OutboxCapacityOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var scenarios = OutboxCapacityScenarioCatalog.Build(options);
        var providerResults = new List<OutboxCapacityProviderResult>();
        foreach (var provider in options.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var poolName =
                $"fullnet-outbox-capacity-{provider}-{Guid.NewGuid():N}";
            await using var database = await MixedLoadDatabase.StartAsync(
                provider,
                poolName,
                cancellationToken);
            var runs = new List<OutboxCapacityRunResult>();
            foreach (var scenario in scenarios)
            {
                for (var repetition = 1;
                     repetition <= options.Repetitions;
                     repetition++)
                {
                    Console.WriteLine(
                        $"[{provider}] {scenario.Name} repeat "
                        + $"{repetition}/{options.Repetitions}");
                    runs.Add(await RunScenarioAsync(
                        database,
                        poolName,
                        scenario,
                        repetition,
                        options,
                        cancellationToken));
                }
            }

            providerResults.Add(new OutboxCapacityProviderResult(
                provider,
                database.ContainerImage,
                database.DatabaseVersion,
                runs));
        }

        await OutboxCapacityReportWriter.WriteAsync(
            options,
            scenarios,
            providerResults,
            cancellationToken);
        Console.WriteLine(
            $"Outbox capacity artifacts: "
            + $"{Path.GetFullPath(options.OutputDirectory)}");
    }

    private static async Task<OutboxCapacityRunResult> RunScenarioAsync(
        MixedLoadDatabase database,
        string poolName,
        OutboxCapacityScenario scenario,
        int repetition,
        OutboxCapacityOptions options,
        CancellationToken cancellationToken)
    {
        await database.ResetOutboxAsync(cancellationToken);
        await database.SeedPendingOutboxAsync(
            options.SeedMessages,
            scenario.PayloadSize,
            OutboxCapacityRunnerMessageType.Value,
            cancellationToken);
        var probe = new OutboxCapacityHandler(
            TimeSpan.FromMilliseconds(
                scenario.HandlerDelayMilliseconds));
        await using var services = BuildServices(database, probe);
        var processors = Enumerable.Range(0, scenario.Replicas)
            .Select(_ => CreateProcessor(
                services,
                scenario,
                options))
            .ToArray();
        using var runCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        var processorErrors = new ConcurrentQueue<string>();
        var processorTasks = processors
            .Select(processor => ConsumeAsync(
                processor,
                processorErrors,
                runCancellation.Token))
            .ToArray();

        if (options.Warmup > TimeSpan.Zero)
        {
            await Task.Delay(options.Warmup, cancellationToken);
        }

        probe.Reset();
        using var dapper = new MixedLoadDapperTelemetry();
        using var pool = MixedLoadConnectionPoolTelemetry.Create(
            database.Provider,
            poolName);
        dapper.Reset();
        pool.Reset();
        var databaseBefore = await database.CaptureStateAsync(
            cancellationToken);
        var processBefore = CaptureProcessResources();
        await using var container = new MixedLoadContainerTelemetry(
            database.ContainerId);
        container.Start();
        var stopwatch = Stopwatch.StartNew();
        await Task.Delay(options.Duration, cancellationToken);
        stopwatch.Stop();
        await runCancellation.CancelAsync();
        await Task.WhenAll(processorTasks);
        var databaseContainer = await container.StopAsync();
        var processAfter = CaptureProcessResources();
        var databaseAfter = await database.CaptureStateAsync(
            cancellationToken);

        return OutboxCapacityReportWriter.CreateRunResult(
            database.Provider.ToString(),
            database.ContainerImage,
            database.DatabaseVersion,
            scenario,
            repetition,
            stopwatch.Elapsed,
            probe.Snapshot(),
            dapper.Snapshot(),
            pool.Snapshot(),
            databaseContainer,
            processBefore,
            processAfter,
            databaseBefore,
            databaseAfter,
            processorErrors.ToArray());
    }

    private static ServiceProvider BuildServices(
        MixedLoadDatabase database,
        OutboxCapacityHandler handler)
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
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] =
                    "300",
            })
            .Build();
        var services = new ServiceCollection();
        OutboxCapacityServiceRegistration.Add(
            services,
            configuration,
            handler);
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static WorkerHost.OutboxProcessor CreateProcessor(
        ServiceProvider services,
        OutboxCapacityScenario scenario,
        OutboxCapacityOptions options) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        services.GetRequiredService<IClock>(),
        Options.Create(new WorkerHost.OutboxWorkerOptions
        {
            BatchSize = scenario.BatchSize,
            MaxConcurrency = scenario.Concurrency,
            LeaseSeconds = checked((int)options.Lease.TotalSeconds),
            LeaseRenewalSeconds =
                checked((int)options.LeaseRenewal.TotalSeconds),
            PollMilliseconds = 100,
            BacklogSampleSeconds = 30,
            MaxAttempts = 5,
        }),
        NullLogger<WorkerHost.OutboxProcessor>.Instance);

    private static async Task ConsumeAsync(
        WorkerHost.OutboxProcessor processor,
        ConcurrentQueue<string> errors,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var processed = await processor.ProcessOnceAsync(
                    cancellationToken);
                if (processed == 0)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(10),
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                errors.Enqueue(
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private static MixedLoadProcessSnapshot CaptureProcessResources()
    {
        using var process = Process.GetCurrentProcess();
        var memory = GC.GetGCMemoryInfo();
        return new MixedLoadProcessSnapshot(
            DateTimeOffset.UtcNow,
            process.TotalProcessorTime.TotalMilliseconds,
            GC.GetTotalAllocatedBytes(precise: false),
            memory.HeapSizeBytes,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }
}

public static class OutboxCapacityServiceRegistration
{
    public static IServiceCollection Add(
        IServiceCollection services,
        IConfiguration configuration,
        IIntegrationEventHandler handler)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(handler);

        services.AddLogging();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddFullNetDapper(configuration, "Benchmark");
        services.AddFullNetMessagePack();
        services.AddSingleton<IIntegrationEventHandler>(handler);
        return services;
    }
}

internal sealed class OutboxCapacityHandler(TimeSpan delay) :
    IIntegrationEventHandler
{
    private readonly ConcurrentDictionary<Guid, int> _deliveries = new();
    private readonly ConcurrentQueue<double> _latencies = new();
    private long _completed;

    public string EventType => OutboxCapacityRunnerMessageType.Value;

    public int SchemaVersion => 1;

    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    public async Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        _deliveries.AddOrUpdate(context.MessageId, 1, (_, count) => count + 1);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }

        _latencies.Enqueue(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        Interlocked.Increment(ref _completed);
    }

    public Task HandleAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "容量基准必须使用包含 MessageId 的投递上下文。");

    public void Reset()
    {
        _deliveries.Clear();
        while (_latencies.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _completed, 0);
    }

    public OutboxCapacityHandlerSnapshot Snapshot()
    {
        var latencies = _latencies.ToArray();
        return new OutboxCapacityHandlerSnapshot(
            Interlocked.Read(ref _completed),
            _deliveries.Count,
            _deliveries.Values.Sum(count => Math.Max(0, count - 1)),
            latencies.Length == 0
                ? null
                : MixedLoadLatencyStatistics.Calculate(latencies));
    }
}

internal static class OutboxCapacityRunnerMessageType
{
    public const string Value = "benchmark.outbox.capacity";
}
