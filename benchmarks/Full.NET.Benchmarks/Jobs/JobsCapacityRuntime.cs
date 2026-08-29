using System.Collections.Concurrent;
using System.Diagnostics;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Full.NET.Modules.Jobs;
using Full.NET.Modules.Jobs.Execution;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Serialization.MemoryPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Full.NET.Benchmarks.Jobs;

public sealed class JobsCapacityExpectedFailureException(string message) :
    Exception(message);

public sealed class JobsCapacityHandler(
    string jobKey,
    TimeSpan delay,
    bool fails,
    Guid scopeId,
    JobsCapacityProbe probe) : IJobHandlerExecutor
{
    public string HandlerKind { get; } = jobKey;

    public async Task ExecuteAsync(
        JobExecutionContext context,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            if (fails)
            {
                probe.RecordExpectedFailure();
                throw new JobsCapacityExpectedFailureException(
                    $"Jobs capacity expected failure for '{HandlerKind}'.");
            }
        }
        finally
        {
            probe.RecordInvocation(
                scopeId,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}

public sealed class JobsCapacityProbe
{
    private readonly ConcurrentDictionary<Guid, byte> _scopeIds = new();
    private readonly ConcurrentQueue<double> _latencies = new();
    private long _invocations;
    private long _expectedFailures;

    public JobsCapacityProbeSnapshot Snapshot()
    {
        var latencies = _latencies.ToArray();
        return new JobsCapacityProbeSnapshot(
            Interlocked.Read(ref _invocations),
            Interlocked.Read(ref _expectedFailures),
            _scopeIds.Keys.Order().ToArray(),
            latencies.Length == 0
                ? null
                : JobsCapacityStatistics.Calculate(latencies));
    }

    internal void RecordInvocation(
        Guid scopeId,
        double latencyMilliseconds)
    {
        _scopeIds.TryAdd(scopeId, 0);
        _latencies.Enqueue(latencyMilliseconds);
        Interlocked.Increment(ref _invocations);
    }

    internal void RecordExpectedFailure() =>
        Interlocked.Increment(ref _expectedFailures);
}

public sealed record JobsCapacityProbeSnapshot(
    long Invocations,
    long ExpectedFailures,
    IReadOnlyList<Guid> ScopeIds,
    JobsCapacityStatistics? HandlerLatency);

public sealed class JobsCapacityScopeIdentity
{
    public Guid Value { get; } = Guid.CreateVersion7();
}

public static class JobsCapacityRuntime
{
    public static ServiceProvider BuildServices(
        string provider,
        string connectionString,
        string poolName,
        JobsCapacityScenario scenario,
        JobsCapacityOptions options,
        JobsCapacityProbe probe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(poolName);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(probe);
        var databaseProvider = provider.ToLowerInvariant() switch
        {
            "sqlserver" => DatabaseProvider.SqlServer,
            "mysql" => DatabaseProvider.MySql,
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "不支持的数据库 Provider。"),
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] =
                    databaseProvider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] =
                    connectionString,
                [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                    MySqlGuidStorageMode.Binary16.ToString(),
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] =
                    "300",
                [$"{JobsWorkerOptions.SectionName}:BatchSize"] =
                    options.BatchSize.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                [$"{JobsWorkerOptions.SectionName}:MaxConcurrency"] =
                    scenario.Concurrency.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                [$"{JobsWorkerOptions.SectionName}:PollMilliseconds"] =
                    "100",
                [$"{JobsWorkerOptions.SectionName}:LeaseSeconds"] =
                    checked((int)options.Lease.TotalSeconds).ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                [$"{JobsWorkerOptions.SectionName}:LeaseRenewalSeconds"] =
                    checked((int)options.LeaseRenewal.TotalSeconds).ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                [$"{JobsWorkerOptions.SectionName}:MaxAttempts"] = "1",
                [$"{JobsWorkerOptions.SectionName}:RetryDelaySeconds"] = "30",
                [$"{JobsWorkerOptions.SectionName}:BacklogSampleSeconds"] =
                    "30",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new JobsCapacityHostEnvironment());
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(serviceProvider =>
            serviceProvider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(serviceProvider =>
            serviceProvider.GetRequiredService<CurrentTenantAccessor>());
        services.AddFullNetDapper(configuration, "Benchmark");
        services.AddFullNetMemoryPack();
        services.AddScoped<ISettingsSecretValueResolver, UnavailableSecretValueResolver>();
        new JobsModule().AddBackgroundServices(services, configuration);
        services.RemoveAll<IHostedService>();
        services.AddSingleton(probe);
        services.AddScoped<JobsCapacityScopeIdentity>();
        for (var index = 0; index < options.HandlerKeyCount; index++)
        {
            var handlerIndex = index;
            var fails = handlerIndex < options.FailingHandlerKeyCount;
            var jobKey = fails
                ? $"jobs.benchmark.capacity.failure.{handlerIndex}"
                : "jobs.benchmark.capacity.success."
                    + $"{handlerIndex - options.FailingHandlerKeyCount}";
            services.AddScoped<IJobHandlerExecutor>(serviceProvider =>
                new JobsCapacityHandler(
                    jobKey,
                    TimeSpan.FromMilliseconds(
                        scenario.HandlerDelayMilliseconds),
                    fails,
                    serviceProvider
                        .GetRequiredService<JobsCapacityScopeIdentity>()
                        .Value,
                    probe));
        }

        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
    }

    public static Task RunUntilStoppedAsync(
        ServiceProvider services,
        int replicas,
        int batchSize,
        ConcurrentQueue<string> processorErrors,
        CancellationToken stopStartingBatches,
        CancellationToken executionCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(replicas);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentNullException.ThrowIfNull(processorErrors);
        return Task.WhenAll(Enumerable.Range(0, replicas).Select(
            _ => RunReplicaAsync(
                services,
                batchSize,
                processorErrors,
                stopStartingBatches,
                executionCancellationToken)));
    }

    private static async Task RunReplicaAsync(
        ServiceProvider services,
        int batchSize,
        ConcurrentQueue<string> processorErrors,
        CancellationToken stopStartingBatches,
        CancellationToken executionCancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider
            .GetRequiredService<ICurrentTenantContextWriter>();
        currentTenant.SetHost();
        try
        {
            var runner = scope.ServiceProvider
                .GetRequiredService<JobExecutionRunner>();
            while (!stopStartingBatches.IsCancellationRequested)
            {
                try
                {
                    var processed = await runner.ProcessPendingAsync(
                        batchSize,
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
                    processorErrors.Enqueue(exception.GetType().Name);
                }
            }
        }
        finally
        {
            currentTenant.Clear();
        }
    }
}

internal sealed class JobsCapacityHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;

    public string ApplicationName { get; set; } = "Full.NET.Benchmarks";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed class UnavailableSecretValueResolver : ISettingsSecretValueResolver
{
    public Task<Result<string>> ResolveSecretValueAsync(
        string configKey,
        CancellationToken cancellationToken = default) =>
        Task.FromException<Result<string>>(
            new InvalidOperationException(
                "Jobs capacity benchmark does not resolve HTTP secret values."));
}
