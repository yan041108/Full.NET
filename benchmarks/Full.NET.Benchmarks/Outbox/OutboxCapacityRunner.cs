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
using Full.NET.Serialization.MemoryPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        var checkpoint = await OutboxCapacityCheckpoint.LoadAsync(
            options,
            scenarios,
            cancellationToken);
        var runBudget = new OutboxCapacityRunBudget(
            options.MaximumNewSamples);
        var providerResults = checkpoint.Providers.ToList();
        foreach (var provider in options.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = providerResults.SingleOrDefault(result =>
                ProviderEquals(result.Provider, provider));
            var runs = existing?.Runs.ToList() ?? [];
            var recoveries = existing?.Recoveries.ToList() ?? [];
            if (runs.Count == scenarios.Count * options.Repetitions
                && (!options.RecoveryEnabled
                    || recoveries.Count == options.Repetitions))
            {
                Console.WriteLine(
                    $"[{provider}] checkpoint ???????????");
                continue;
            }

            var poolName =
                $"fullnet-outbox-capacity-{provider}-{Guid.NewGuid():N}";
            await using var database = await MixedLoadDatabase.StartAsync(
                provider,
                poolName,
                cancellationToken);
            foreach (var scenario in scenarios)
            {
                for (var repetition = 1;
                     repetition <= options.Repetitions;
                     repetition++)
                {
                    if (runs.Any(run =>
                            run.Scenario == scenario
                            && run.Repetition == repetition))
                    {
                        Console.WriteLine(
                            $"[{provider}] {scenario.Name} repeat "
                            + $"{repetition}/{options.Repetitions} "
                            + "checkpoint skip");
                        continue;
                    }

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
                    await SaveCheckpointAsync(
                        options,
                        scenarios,
                        providerResults,
                        new OutboxCapacityProviderResult(
                            provider,
                            database.ContainerImage,
                            database.DatabaseVersion,
                            runs,
                            recoveries),
                        cancellationToken);
                    if (options.MaximumNewSamples > 0
                        && runBudget.RecordCompletedSample())
                    {
                        WriteBudgetPause(options, runBudget);
                        return;
                    }
                }
            }
            if (options.RecoveryEnabled)
            {
                for (var repetition = 1;
                     repetition <= options.Repetitions;
                     repetition++)
                {
                    if (recoveries.Any(recovery =>
                            recovery.Repetition == repetition))
                    {
                        Console.WriteLine(
                            $"[{provider}] abandoned-lease-recovery repeat "
                            + $"{repetition}/{options.Repetitions} "
                            + "checkpoint skip");
                        continue;
                    }

                    Console.WriteLine(
                        $"[{provider}] abandoned-lease-recovery repeat "
                        + $"{repetition}/{options.Repetitions}");
                    recoveries.Add(await RunRecoveryAsync(
                        database,
                        repetition,
                        options,
                        cancellationToken));
                    await SaveCheckpointAsync(
                        options,
                        scenarios,
                        providerResults,
                        new OutboxCapacityProviderResult(
                            provider,
                            database.ContainerImage,
                            database.DatabaseVersion,
                            runs,
                            recoveries),
                        cancellationToken);
                    if (options.MaximumNewSamples > 0
                        && runBudget.RecordCompletedSample())
                    {
                        WriteBudgetPause(options, runBudget);
                        return;
                    }
                }
            }

            UpsertProviderResult(
                providerResults,
                new OutboxCapacityProviderResult(
                    provider,
                    database.ContainerImage,
                    database.DatabaseVersion,
                    runs,
                    recoveries));
        }

        await OutboxCapacityReportWriter.WriteAsync(
            options,
            scenarios,
            OrderProviderResults(options, providerResults),
            cancellationToken);
        Console.WriteLine(
            $"Outbox capacity artifacts: "
            + $"{Path.GetFullPath(options.OutputDirectory)}");
    }

    private static void WriteBudgetPause(
        OutboxCapacityOptions options,
        OutboxCapacityRunBudget runBudget)
    {
        Console.WriteLine(
            $"????? {runBudget.CompletedSamples} ????"
            + "?? --max-new-samples?????? checkpoint?");
        Console.WriteLine(
            $"Outbox capacity artifacts: "
            + $"{Path.GetFullPath(options.OutputDirectory)}");
    }

    private static async Task SaveCheckpointAsync(
        OutboxCapacityOptions options,
        IReadOnlyList<OutboxCapacityScenario> scenarios,
        List<OutboxCapacityProviderResult> providerResults,
        OutboxCapacityProviderResult current,
        CancellationToken cancellationToken)
    {
        UpsertProviderResult(providerResults, current);
        if (!options.ResumeEnabled)
        {
            return;
        }

        await OutboxCapacityReportWriter.WriteAsync(
            options,
            scenarios,
            OrderProviderResults(options, providerResults),
            cancellationToken);
    }

    private static void UpsertProviderResult(
        List<OutboxCapacityProviderResult> providerResults,
        OutboxCapacityProviderResult current)
    {
        var index = providerResults.FindIndex(result =>
            ProviderEquals(result.Provider, current.Provider));
        if (index < 0)
        {
            providerResults.Add(current);
            return;
        }

        providerResults[index] = current;
    }

    private static IReadOnlyList<OutboxCapacityProviderResult>
        OrderProviderResults(
            OutboxCapacityOptions options,
            IReadOnlyList<OutboxCapacityProviderResult> providerResults) =>
        options.Providers
            .Select(provider => providerResults.SingleOrDefault(result =>
                ProviderEquals(result.Provider, provider)))
            .Where(result => result is not null)
            .Cast<OutboxCapacityProviderResult>()
            .ToArray();

    private static bool ProviderEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

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
        var processorErrors = new ConcurrentQueue<string>();
        await using var services = BuildServices(
            database,
            probe,
            processorErrors);
        var processorLogger =
            new OutboxCapacityProcessorLogger<WorkerHost.OutboxProcessor>(
                processorErrors);
        var processors = Enumerable.Range(0, scenario.Replicas)
            .Select(_ => CreateProcessor(
                services,
                scenario,
                options,
                processorLogger))
            .ToArray();
        if (options.Warmup > TimeSpan.Zero)
        {
            using var stopWarmupBatches =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            var warmupTasks = processors
                .Select(processor => ConsumeAsync(
                    processor,
                    processorErrors,
                    stopWarmupBatches.Token,
                    cancellationToken))
                .ToArray();
            await Task.Delay(options.Warmup, cancellationToken);
            await stopWarmupBatches.CancelAsync();
            await Task.WhenAll(warmupTasks);
        }

        var warmupSnapshot = probe.Snapshot();
        var backlogAfterWarmup = await database.CaptureStateAsync(
            cancellationToken);
        var samplingTarget =
            OutboxCapacityBacklogPlanner.CalculateSamplingTarget(
                options.SeedMessages,
                warmupSnapshot.CompletedMessages,
                options.Warmup,
                options.Duration,
                scenario.BatchSize,
                scenario.Replicas);
        var samplingDeficit =
            OutboxCapacityBacklogPlanner.CalculateDeficit(
                backlogAfterWarmup.PendingOutboxCount,
                samplingTarget);
        if (samplingDeficit > 0)
        {
            await database.SeedPendingOutboxAsync(
                samplingDeficit,
                scenario.PayloadSize,
                OutboxCapacityRunnerMessageType.Value,
                cancellationToken);
        }

        probe.Reset();
        // ???????????????????????????????
        // ????????????????????????????????
        using var stopStartingBatches =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        var processorTasks = processors
            .Select(processor => ConsumeAsync(
                processor,
                processorErrors,
                stopStartingBatches.Token,
                cancellationToken))
            .ToArray();
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
        await stopStartingBatches.CancelAsync();
        // ???????????????????????????????????
        var handlerSnapshot = probe.Snapshot();
        var dapperSnapshot = dapper.Snapshot();
        var poolSnapshot = pool.Snapshot();
        var processAfter = CaptureProcessResources();
        var databaseAfter = await database.CaptureStateAsync(
            cancellationToken);
        var processorErrorSnapshot = processorErrors.ToArray();
        var databaseContainer = await container.StopAsync();
        await Task.WhenAll(processorTasks);

        return OutboxCapacityReportWriter.CreateRunResult(
            database.Provider.ToString(),
            database.ContainerImage,
            database.DatabaseVersion,
            scenario,
            repetition,
            stopwatch.Elapsed,
            handlerSnapshot,
            dapperSnapshot,
            poolSnapshot,
            databaseContainer,
            processBefore,
            processAfter,
            databaseBefore,
            databaseAfter,
            processorErrorSnapshot);
    }

    private static async Task<OutboxCapacityRecoveryResult> RunRecoveryAsync(
        MixedLoadDatabase database,
        int repetition,
        OutboxCapacityOptions options,
        CancellationToken cancellationToken)
    {
        await database.ResetOutboxAsync(cancellationToken);
        await database.SeedPendingOutboxAsync(
            count: 1,
            payloadSize: options.PayloadSizes[0],
            OutboxCapacityRunnerMessageType.Value,
            cancellationToken);
        var handler = new OutboxCapacityHandler(TimeSpan.Zero);
        await using var services = BuildServices(database, handler);

        OutboxEnvelope abandoned;
        await using (var abandonedScope = services.CreateAsyncScope())
        {
            var accessor = abandonedScope.ServiceProvider
                .GetRequiredService<ICurrentTenantContextWriter>();
            accessor.SetHost();
            var store = abandonedScope.ServiceProvider
                .GetRequiredService<IOutboxStore>();
            var messages = await store.AcquireAsync(
                batchSize: 1,
                options.Lease,
                cancellationToken);
            if (messages.Count != 1)
            {
                throw new InvalidOperationException(
                    $"???????????? 1 ??????? {messages.Count}?");
            }

            abandoned = messages[0];
            // ?? Handler ?????????????????????????????? MessageId?
            handler.RecordAbandonedDelivery(abandoned.Id);
        }

        var databaseBefore = await database.CaptureStateAsync(
            cancellationToken);
        using var dapper = new MixedLoadDapperTelemetry();
        dapper.Reset();
        var processor = CreateProcessor(
            services,
            new OutboxCapacityScenario(
                Concurrency: 1,
                HandlerDelayMilliseconds: 0,
                Replicas: 1,
                BatchSize: 1,
                PayloadSize: options.PayloadSizes[0]),
            options,
            NullLogger<WorkerHost.OutboxProcessor>.Instance);
        using var recoveryCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        recoveryCancellation.CancelAfter(
            options.Lease + options.RecoveryGrace);
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var processed = await processor.ProcessOnceAsync(
                recoveryCancellation.Token);
            if (processed > 0)
            {
                break;
            }

            var delay = processor.GetDelayAfterBatch(processed);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, recoveryCancellation.Token);
            }
        }

        stopwatch.Stop();
        var dapperSnapshot = dapper.Snapshot();
        var databaseAfter = await database.CaptureStateAsync(
            cancellationToken);
        var handlerSnapshot = handler.Snapshot();
        var attempts = await database.ReadOutboxAttemptsAsync(
            abandoned.Id,
            cancellationToken);
        var acquireExecutions =
            OutboxCapacityRecoveryResult.CountAcquireExecutions(
                dapperSnapshot.StatementExecutions);
        return OutboxCapacityRecoveryResult.Create(
            database.Provider.ToString(),
            repetition,
            abandoned.Id,
            handler.LastCompletedMessageId,
            stopwatch.Elapsed,
            options.Lease,
            options.RecoveryGrace,
            attempts,
            handlerSnapshot.DuplicateDeliveries,
            dapperSnapshot.Failures,
            databaseBefore.PendingOutboxCount,
            databaseAfter.PendingOutboxCount,
            dapperSnapshot.Cancellations,
            acquireExecutions);
    }

    private static ServiceProvider BuildServices(
        MixedLoadDatabase database,
        OutboxCapacityHandler handler,
        ConcurrentQueue<string>? errors = null)
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
        if (errors is not null)
        {
            services.AddLogging(builder =>
                builder.AddProvider(
                    new OutboxCapacityFailureLoggerProvider(errors)));
        }

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static WorkerHost.OutboxProcessor CreateProcessor(
        ServiceProvider services,
        OutboxCapacityScenario scenario,
        OutboxCapacityOptions options,
        ILogger<WorkerHost.OutboxProcessor> logger) => new(
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
        logger);

    private static async Task ConsumeAsync(
        WorkerHost.OutboxProcessor processor,
        ConcurrentQueue<string> errors,
        CancellationToken stopStartingBatches,
        CancellationToken executionCancellationToken)
    {
        while (!stopStartingBatches.IsCancellationRequested)
        {
            try
            {
                var processed = await processor.ProcessOnceAsync(
                    executionCancellationToken);
                if (processed == 0)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(10),
                        stopStartingBatches);
                }
            }
            catch (OperationCanceledException)
                when (stopStartingBatches.IsCancellationRequested
                    || executionCancellationToken.IsCancellationRequested)
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

/// <summary>
/// ?? Outbox ????????????????????????????
/// </summary>
/// <typeparam name="TCategoryName">???????</typeparam>
public sealed class OutboxCapacityProcessorLogger<TCategoryName> :
    ILogger<TCategoryName>
{
    private readonly ConcurrentQueue<string> _errors;

    /// <summary>
    /// ???????????????????
    /// </summary>
    /// <param name="errors">????????????????</param>
    public OutboxCapacityProcessorLogger(ConcurrentQueue<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        _errors = errors;
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) =>
        logLevel >= LogLevel.Warning;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel < LogLevel.Warning || exception is null)
        {
            return;
        }

        if (eventId.Id == 2001
            && state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            var properties = values.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
            _errors.Enqueue(
                $"dapper.event.{eventId.Id} | {exception.GetType().Name}"
                + $" | statement={GetProperty(properties, "StatementName")}"
                + $" | provider={GetProperty(properties, "Provider")}"
                + $" | code={GetProperty(properties, "DatabaseErrorCode")}");
            return;
        }

        _errors.Enqueue(
            $"outbox.event.{eventId.Id} | "
            + $"{exception.GetType().Name}: "
            + exception.Message.ReplaceLineEndings(" "));
    }

    private static string GetProperty(
        IReadOnlyDictionary<string, object?> properties,
        string name) =>
        properties.TryGetValue(name, out var value)
            ? value?.ToString() ?? "not_available"
            : "not_available";
}

/// <summary>
/// ??????? DI ??????????????????????????????????
/// </summary>
public sealed class OutboxCapacityFailureLoggerProvider(
    ConcurrentQueue<string> errors) : ILoggerProvider
{
    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) =>
        new OutboxCapacityProcessorLogger<object>(errors);

    /// <inheritdoc />
    public void Dispose()
    {
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
        services.AddScoped<ICurrentTenantContextWriter>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddFullNetDapper(configuration, "Benchmark");
        services.AddFullNetMemoryPack();
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
    private Guid? _lastCompletedMessageId;

    public string EventType => OutboxCapacityRunnerMessageType.Value;

    public int SchemaVersion => 1;

    public IntegrationEventIdempotencyStrategy IdempotencyStrategy =>
        IntegrationEventIdempotencyStrategy.NaturallyIdempotent;

    public Guid? LastCompletedMessageId => _lastCompletedMessageId;

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
        _lastCompletedMessageId = context.MessageId;
        Interlocked.Increment(ref _completed);
    }

    public Task HandleAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "?????????? MessageId ???????");

    public void Reset()
    {
        _deliveries.Clear();
        while (_latencies.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _completed, 0);
        _lastCompletedMessageId = null;
    }

    public void RecordAbandonedDelivery(Guid messageId) =>
        _deliveries.AddOrUpdate(messageId, 1, (_, count) => count + 1);

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
