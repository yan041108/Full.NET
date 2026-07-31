using System.Collections.Concurrent;
using System.Diagnostics;
using Full.NET.Benchmarks.MixedLoad;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Benchmarks.Jobs;

public static class JobsCapacityRunner
{
    public static async Task RunAsync(
        JobsCapacityOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var scenarios = JobsCapacityScenarioCatalog.Build(options);
        var buildFingerprint =
            JobsCapacityReportWriter.GetBuildFingerprint();
        var checkpoint = await JobsCapacityCheckpoint.LoadAsync(
            options,
            scenarios,
            buildFingerprint,
            cancellationToken);
        var providers = checkpoint.Providers.ToList();
        var newSamples = 0;
        foreach (var provider in options.Providers)
        {
            var existing = providers.SingleOrDefault(result =>
                string.Equals(
                    result.Provider,
                    provider,
                    StringComparison.OrdinalIgnoreCase));
            var runs = existing?.Runs.ToList() ?? [];
            var expectedRuns = scenarios.Count * options.Repetitions;
            if (runs.Count == expectedRuns)
            {
                Console.WriteLine(
                    $"[{provider}] checkpoint 已完成，跳过容器启动。");
                continue;
            }

            var poolName =
                $"fullnet-jobs-capacity-{provider}-{Guid.NewGuid():N}";
            await using var database =
                await JobsCapacityDatabase.StartAsync(
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
                        continue;
                    }

                    Console.WriteLine(
                        $"[{provider}] {scenario.Name} "
                        + $"{repetition}/{options.Repetitions}");
                    runs.Add(await RunScenarioAsync(
                        database,
                        poolName,
                        scenario,
                        repetition,
                        options,
                        cancellationToken));
                    Upsert(
                        providers,
                        new JobsCapacityProviderResult(
                            provider,
                            database.ContainerImage,
                            database.DatabaseVersion,
                            runs));
                    await JobsCapacityReportWriter.WriteAsync(
                        options,
                        scenarios,
                        OrderProviders(options, providers),
                        cancellationToken);
                    newSamples++;
                    if (options.MaximumNewSamples > 0
                        && newSamples >= options.MaximumNewSamples)
                    {
                        Console.WriteLine(
                            "达到 --max-new-samples，checkpoint 已保存。");
                        return;
                    }
                }
            }
        }

        await JobsCapacityReportWriter.WriteAsync(
            options,
            scenarios,
            OrderProviders(options, providers),
            cancellationToken);
        Console.WriteLine(
            $"Jobs capacity artifacts: "
            + $"{Path.GetFullPath(options.OutputDirectory)}");
    }

    private static async Task<JobsCapacityRunResult> RunScenarioAsync(
        JobsCapacityDatabase database,
        string poolName,
        JobsCapacityScenario scenario,
        int repetition,
        JobsCapacityOptions options,
        CancellationToken cancellationToken)
    {
        var createdAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await database.ResetAndSeedAsync(
            options.SeedJobs,
            options.HandlerKeyCount,
            options.FailingHandlerKeyCount,
            createdAtUtc,
            cancellationToken);
        var warmupProbe = new JobsCapacityProbe();
        await using (var warmupServices =
            JobsCapacityRuntime.BuildServices(
                database.Provider,
                database.ConnectionString,
                poolName,
                scenario,
                options,
                warmupProbe))
        {
            await RunWindowAsync(
                warmupServices,
                scenario,
                options.BatchSize,
                options.Warmup,
                new ConcurrentQueue<string>(),
                cancellationToken);
        }

        var requiredJobs =
            JobsCapacityBacklogPlanner.CalculateRequiredJobs(
                options.SeedJobs,
                warmupProbe.Snapshot().Invocations,
                options.Warmup,
                options.Duration,
                options.BatchSize,
                scenario.Replicas);
        createdAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await database.ResetAndSeedAsync(
            requiredJobs,
            options.HandlerKeyCount,
            options.FailingHandlerKeyCount,
            createdAtUtc,
            cancellationToken);
        var probe = new JobsCapacityProbe();
        await using var services = JobsCapacityRuntime.BuildServices(
            database.Provider,
            database.ConnectionString,
            poolName,
            scenario,
            options,
            probe);
        var processorErrors = new ConcurrentQueue<string>();
        var databaseBefore = await database.MixedLoad.CaptureStateAsync(
            cancellationToken);
        using var dapper = new MixedLoadDapperTelemetry();
        using var pool = MixedLoadConnectionPoolTelemetry.Create(
            database.MixedLoad.Provider,
            poolName);
        dapper.Reset();
        pool.Reset();
        var processBefore = CaptureProcessResources();
        await using var container = new MixedLoadContainerTelemetry(
            database.ContainerId);
        container.Start();
        var timing = await RunWindowAsync(
            services,
            scenario,
            options.BatchSize,
            options.Duration,
            processorErrors,
            cancellationToken);
        var probeSnapshot = probe.Snapshot();
        var dapperSnapshot = dapper.Snapshot();
        var poolSnapshot = pool.Snapshot();
        var processAfter = CaptureProcessResources();
        var containerSnapshot = await container.StopAsync();
        var state = await database.ReadStateAsync(cancellationToken);
        var databaseAfter = await database.MixedLoad.CaptureStateAsync(
            cancellationToken);
        var actualDuration = timing.SampleDuration + timing.DrainDuration;
        var leaseRenewals = dapperSnapshot.StatementExecutions
            .GetValueOrDefault("jobs.renew_host_execution_lease");
        return new JobsCapacityRunResult(
            database.Provider,
            scenario,
            repetition,
            actualDuration.TotalSeconds,
            state.TerminalExecutions,
            state.SucceededExecutions,
            state.FailedExecutions,
            state.PendingExecutions,
            state.RunningExecutions,
            state.TerminalExecutionsWithLease,
            state.AttemptCountGreaterThanOne,
            probeSnapshot.Invocations,
            probeSnapshot.ExpectedFailures,
            state.TerminalExecutions
                / Math.Max(actualDuration.TotalSeconds, 0.001d),
            probeSnapshot.HandlerLatency,
            state.QueueLatency,
            leaseRenewals,
            dapperSnapshot,
            poolSnapshot,
            containerSnapshot,
            processorErrors.ToArray())
        {
            DrainDuration = timing.DrainDuration,
            Process = CalculateProcessDelta(
                processBefore,
                processAfter,
                actualDuration),
            DatabaseBefore = databaseBefore,
            DatabaseAfter = databaseAfter,
        };
    }

    private static async Task<CapacityWindowTiming> RunWindowAsync(
        ServiceProvider services,
        JobsCapacityScenario scenario,
        int batchSize,
        TimeSpan duration,
        ConcurrentQueue<string> processorErrors,
        CancellationToken cancellationToken)
    {
        using var stopStarting =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        var processors = JobsCapacityRuntime.RunUntilStoppedAsync(
            services,
            scenario.Replicas,
            batchSize,
            processorErrors,
            stopStarting.Token,
            cancellationToken);
        var sample = Stopwatch.StartNew();
        await Task.Delay(duration, cancellationToken);
        sample.Stop();
        await stopStarting.CancelAsync();
        var drain = Stopwatch.StartNew();
        await processors;
        drain.Stop();
        return new CapacityWindowTiming(sample.Elapsed, drain.Elapsed);
    }

    private static IReadOnlyList<JobsCapacityProviderResult>
        OrderProviders(
            JobsCapacityOptions options,
            IReadOnlyList<JobsCapacityProviderResult> providers) =>
        options.Providers
            .Select(provider => providers.SingleOrDefault(result =>
                string.Equals(
                    result.Provider,
                    provider,
                    StringComparison.OrdinalIgnoreCase)))
            .Where(result => result is not null)
            .Cast<JobsCapacityProviderResult>()
            .ToArray();

    private static void Upsert(
        List<JobsCapacityProviderResult> providers,
        JobsCapacityProviderResult current)
    {
        var index = providers.FindIndex(result => string.Equals(
            result.Provider,
            current.Provider,
            StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            providers.Add(current);
        }
        else
        {
            providers[index] = current;
        }
    }

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

    private static MixedLoadProcessDelta CalculateProcessDelta(
        MixedLoadProcessSnapshot before,
        MixedLoadProcessSnapshot after,
        TimeSpan duration)
    {
        var processorDelta = Math.Max(
            0d,
            after.TotalProcessorMilliseconds
            - before.TotalProcessorMilliseconds);
        return new MixedLoadProcessDelta(
            processorDelta
            / Math.Max(1d, duration.TotalMilliseconds)
            / Math.Max(1, Environment.ProcessorCount)
            * 100d,
            Math.Max(
                0L,
                after.TotalAllocatedBytes - before.TotalAllocatedBytes),
            after.HeapSizeBytes,
            Math.Max(
                0,
                after.Gen0Collections - before.Gen0Collections),
            Math.Max(
                0,
                after.Gen1Collections - before.Gen1Collections),
            Math.Max(
                0,
                after.Gen2Collections - before.Gen2Collections));
    }

    private sealed record CapacityWindowTiming(
        TimeSpan SampleDuration,
        TimeSpan DrainDuration);
}
