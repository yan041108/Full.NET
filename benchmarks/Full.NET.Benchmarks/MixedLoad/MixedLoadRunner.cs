extern alias apiHost;

using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using ApiProgram = apiHost::Program;

namespace Full.NET.Benchmarks.MixedLoad;

public static class MixedLoadRunner
{
    private const string TestPassword = "FullNet_MixedLoad!2026";

    public static async Task RunAsync(
        MixedLoadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var providerResults = new List<MixedLoadProviderResult>();
        var scenarios = MixedLoadScenarioCatalog.Get(options.Workload);

        foreach (var provider in options.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runs = new List<MixedLoadRunResult>();
            MixedLoadOutboxRetentionProfile?[] retentionProfiles =
                options.OutboxRetentionMatrixEnabled
                    ? options.OutboxRetentionProfiles
                        .Select(profile =>
                            (MixedLoadOutboxRetentionProfile?)profile)
                        .ToArray()
                    : [null];
            foreach (var concurrency in options.ConcurrencyLevels)
            {
                foreach (var retentionProfile in retentionProfiles)
                {
                    var profileSuffix = retentionProfile is null
                        ? string.Empty
                        : $"-retention-{retentionProfile.Value.ToString().ToLowerInvariant()}";
                    var poolName =
                        $"fullnet-mixed-load-{provider}-c{concurrency}"
                        + $"{profileSuffix}-{Guid.NewGuid():N}";
                    using var poolTelemetry = MixedLoadConnectionPoolTelemetry.Create(
                        ParseProvider(provider),
                        poolName);
                    Console.WriteLine(
                        $"[{provider}] 并发 {concurrency}{profileSuffix}："
                        + "启动隔离数据库并迁移...");
                    await using var database = await MixedLoadDatabase.StartAsync(
                        provider,
                        poolName,
                        cancellationToken);
                    await using var host = new MixedLoadApiFactory(
                        database.Provider,
                        database.ConnectionString);
                    using var client = host.CreateBenchmarkClient();
                    var auditWriteTelemetry = host.Services
                        .GetRequiredService<MixedLoadAuditWriteTelemetry>();
                    MixedLoadSetup setup;
                    MixedLoadCredentials credentials;
                    MixedLoadWorkerState[] workerStates;
                    using (MixedLoadConsoleSilencer.Suppress())
                    {
                        setup = await host.InitializeAsync(
                            concurrency,
                            cancellationToken);
                        credentials = await PrepareCredentialsAsync(
                            client,
                            setup.AdminUserId,
                            cancellationToken);
                        workerStates = setup.Tenants
                            .Select((tenant, index) => new MixedLoadWorkerState(
                                index,
                                tenant.Id,
                                tenant.Version))
                            .ToArray();
                        await VerifyPreflightAsync(
                            client,
                            workerStates[0],
                            credentials,
                            scenarios,
                            cancellationToken);
                        await Task.Delay(100, cancellationToken);
                    }

                    Console.WriteLine(
                        $"[{provider}] 并发 {concurrency}：预热 {options.Warmup.TotalSeconds:0}s...");
                    using var telemetry = new MixedLoadDapperTelemetry();
                    using (MixedLoadConsoleSilencer.Suppress())
                    {
                        await RunPhaseAsync(
                            client,
                            credentials,
                            workerStates,
                            scenarios,
                            options.AuditWriteProfiles,
                            options.Seed,
                            options.Warmup,
                            samples: null,
                            cancellationToken);
                        await Task.Delay(100, cancellationToken);
                    }

                    if (retentionProfile is not null)
                    {
                        Console.WriteLine(
                            $"[{provider}] 并发 {concurrency}{profileSuffix}："
                            + $"预置 {options.OutboxRetentionSeedProcessed} "
                            + "条过期成功消息...");
                        await database.SeedExpiredProcessedOutboxAsync(
                            options.OutboxRetentionSeedProcessed,
                            cancellationToken);
                    }

                    telemetry.Reset();
                    auditWriteTelemetry.Reset();
                    poolTelemetry.Reset();
                    var resourceBefore = CaptureProcessResources();
                    var databaseBefore = await database.CaptureStateAsync(cancellationToken);
                    var samples = new ConcurrentQueue<MixedLoadRequestSample>();
                    await using var containerTelemetry =
                        new MixedLoadContainerTelemetry(database.ContainerId);
                    containerTelemetry.Start();
                    Console.WriteLine(
                        $"[{provider}] 并发 {concurrency}：采样 {options.Duration.TotalSeconds:0}s...");
                    TimeSpan elapsed;
                    MixedLoadOutboxActivitySnapshot? outboxActivity = null;
                    using (MixedLoadConsoleSilencer.Suppress())
                    {
                        var requestTask = RunPhaseAsync(
                            client,
                            credentials,
                            workerStates,
                            scenarios,
                            options.AuditWriteProfiles,
                            options.Seed,
                            options.Duration,
                            samples,
                            cancellationToken);
                        if (retentionProfile is null)
                        {
                            elapsed = await requestTask;
                        }
                        else
                        {
                            var outboxSamples =
                                new ConcurrentQueue<MixedLoadOutboxOperationSample>();
                            var outboxTask = RunOutboxActivityAsync(
                                host.Services,
                                retentionProfile.Value,
                                options,
                                outboxSamples,
                                cancellationToken);
                            await Task.WhenAll(requestTask, outboxTask);
                            elapsed = await requestTask;
                            outboxActivity = new MixedLoadOutboxActivitySnapshot(
                                retentionProfile.Value,
                                outboxSamples
                                    .OrderBy(sample => sample.StartedAtUtc)
                                    .ToArray());
                        }
                    }
                    var containerResources = await containerTelemetry.StopAsync();
                    await Task.Delay(100, cancellationToken);
                    var databaseAfter = await database.CaptureStateAsync(cancellationToken);
                    var resourceAfter = CaptureProcessResources();
                    var requestSamples = samples
                        .OrderBy(sample => sample.StartedAtUtc)
                        .ThenBy(sample => sample.WorkerId)
                        .ToArray();
                    var result = MixedLoadReportWriter.CreateRunResult(
                        provider,
                        database.ContainerImage,
                        database.DatabaseVersion,
                        concurrency,
                        options,
                        elapsed,
                        requestSamples,
                        telemetry.Snapshot(),
                        auditWriteTelemetry.Snapshot(),
                        poolTelemetry.Snapshot(),
                        containerResources,
                        resourceBefore,
                        resourceAfter,
                        databaseBefore,
                        databaseAfter,
                        outboxActivity);
                    var checkpointedResult = await MixedLoadReportWriter
                        .WriteRunCheckpointAsync(
                            options.OutputDirectory,
                            result,
                            cancellationToken);
                    runs.Add(checkpointedResult);
                    var checkpointProviders = providerResults
                        .Append(new MixedLoadProviderResult(
                            provider,
                            checkpointedResult.ContainerImage,
                            checkpointedResult.DatabaseVersion,
                            runs))
                        .ToArray();
                    await MixedLoadReportWriter.WriteAsync(
                        options.OutputDirectory,
                        MixedLoadReportWriter.CreateReport(
                            options,
                            checkpointProviders),
                        cancellationToken);

                    Console.WriteLine(
                        $"[{provider}] c={concurrency}{profileSuffix}: "
                        + $"QPS={result.RequestsPerSecond:0.##}, "
                        + $"P95={result.Latency.P95Milliseconds:0.###}ms, "
                        + $"P99={result.Latency.P99Milliseconds:0.###}ms, "
                        + $"unexpected={result.UnexpectedErrorRate:P3}");
                }
            }

            var firstRun = runs[0];
            providerResults.Add(new MixedLoadProviderResult(
                provider,
                firstRun.ContainerImage,
                firstRun.DatabaseVersion,
                runs));
        }

        var report = MixedLoadReportWriter.CreateReport(options, providerResults);
        await MixedLoadReportWriter.WriteAsync(
            options.OutputDirectory,
            report,
            cancellationToken);
        MixedLoadReportWriter.EnsurePassed(report);
        Console.WriteLine($"混合负载工件已写入：{Path.GetFullPath(options.OutputDirectory)}");
    }

    private static DatabaseProvider ParseProvider(string provider) =>
        provider switch
        {
            "sqlserver" => DatabaseProvider.SqlServer,
            "mysql" => DatabaseProvider.MySql,
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "不支持的数据库 Provider。"),
        };

    private static async Task<TimeSpan> RunPhaseAsync(
        HttpClient client,
        MixedLoadCredentials credentials,
        IReadOnlyList<MixedLoadWorkerState> workerStates,
        IReadOnlyList<MixedLoadScenario> scenarios,
        IReadOnlyList<MixedLoadAuditWriteProfile> auditWriteProfiles,
        int seed,
        TimeSpan duration,
        ConcurrentQueue<MixedLoadRequestSample>? samples,
        CancellationToken cancellationToken)
    {
        if (duration == TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var stopTimestamp = Stopwatch.GetTimestamp()
            + (long)(duration.TotalSeconds * Stopwatch.Frequency);
        var stopwatch = Stopwatch.StartNew();
        var tasks = workerStates.Select(worker =>
            RunWorkerAsync(
                client,
                credentials,
                worker,
                scenarios,
                auditWriteProfiles,
                seed,
                stopTimestamp,
                samples,
                cancellationToken)).ToArray();
        await Task.WhenAll(tasks);
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private static async Task RunOutboxActivityAsync(
        IServiceProvider services,
        MixedLoadOutboxRetentionProfile profile,
        MixedLoadOptions options,
        ConcurrentQueue<MixedLoadOutboxOperationSample> samples,
        CancellationToken cancellationToken)
    {
        var stopTimestamp = Stopwatch.GetTimestamp()
            + (long)(options.Duration.TotalSeconds * Stopwatch.Frequency);
        var worker = RunOutboxDrainAsync(
            services,
            stopTimestamp,
            samples,
            cancellationToken);
        if (profile == MixedLoadOutboxRetentionProfile.Off)
        {
            await worker;
            return;
        }

        var cleanup = RunOutboxCleanupAsync(
            services,
            stopTimestamp,
            options,
            samples,
            cancellationToken);
        await Task.WhenAll(worker, cleanup);
    }

    private static async Task RunOutboxDrainAsync(
        IServiceProvider services,
        long stopTimestamp,
        ConcurrentQueue<MixedLoadOutboxOperationSample> samples,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested
            && Stopwatch.GetTimestamp() < stopTimestamp)
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var processed = 0;
            string? error = null;
            try
            {
                await using var scope = services.CreateAsyncScope();
                scope.ServiceProvider
                    .GetRequiredService<CurrentTenantAccessor>()
                    .SetHost();
                var store = scope.ServiceProvider
                    .GetRequiredService<IOutboxStore>();
                var messages = await store.AcquireAsync(
                    batchSize: 20,
                    lease: TimeSpan.FromSeconds(30),
                    cancellationToken);
                foreach (var message in messages)
                {
                    await store.MarkProcessedAsync(
                        message.Id,
                        message.LockId,
                        cancellationToken);
                    processed++;
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                error = $"{exception.GetType().Name}: {exception.Message}";
            }
            finally
            {
                stopwatch.Stop();
            }

            if (processed > 0 || error is not null)
            {
                samples.Enqueue(new MixedLoadOutboxOperationSample(
                    startedAtUtc,
                    "worker",
                    stopwatch.Elapsed.TotalMilliseconds,
                    processed,
                    error));
            }

            if (processed == 0 && error is null)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(10),
                    cancellationToken);
            }
        }
    }

    private static async Task RunOutboxCleanupAsync(
        IServiceProvider services,
        long stopTimestamp,
        MixedLoadOptions options,
        ConcurrentQueue<MixedLoadOutboxOperationSample> samples,
        CancellationToken cancellationToken)
    {
        var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-30);
        var batchesExecuted = 0;
        while (!cancellationToken.IsCancellationRequested
            && Stopwatch.GetTimestamp() < stopTimestamp
            && batchesExecuted < options.OutboxRetentionMaxBatches)
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var deleted = 0;
            string? error = null;
            try
            {
                await using var scope = services.CreateAsyncScope();
                scope.ServiceProvider
                    .GetRequiredService<CurrentTenantAccessor>()
                    .SetHost();
                deleted = await scope.ServiceProvider
                    .GetRequiredService<IOutboxRetentionStore>()
                    .DeleteProcessedBatchAsync(
                        cutoffUtc,
                        options.OutboxRetentionBatchSize,
                        cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                error = $"{exception.GetType().Name}: {exception.Message}";
            }
            finally
            {
                stopwatch.Stop();
            }

            samples.Enqueue(new MixedLoadOutboxOperationSample(
                startedAtUtc,
                "cleanup",
                stopwatch.Elapsed.TotalMilliseconds,
                deleted,
                error));
            batchesExecuted++;
            if (error is null
                && deleted < options.OutboxRetentionBatchSize)
            {
                break;
            }

            if (options.OutboxRetentionInterval > TimeSpan.Zero)
            {
                await Task.Delay(
                    options.OutboxRetentionInterval,
                    cancellationToken);
            }
        }
    }

    private static async Task RunWorkerAsync(
        HttpClient client,
        MixedLoadCredentials credentials,
        MixedLoadWorkerState worker,
        IReadOnlyList<MixedLoadScenario> scenarios,
        IReadOnlyList<MixedLoadAuditWriteProfile> auditWriteProfiles,
        int seed,
        long stopTimestamp,
        ConcurrentQueue<MixedLoadRequestSample>? samples,
        CancellationToken cancellationToken)
    {
        var selector = new MixedLoadScenarioSelector(
            scenarios,
            unchecked(seed + (worker.WorkerId * 7919)));
        var auditWriteProfileSelector = new MixedLoadAuditWriteProfileSelector(
            auditWriteProfiles,
            worker.WorkerId);
        long sequence = 0;
        while (!cancellationToken.IsCancellationRequested
            && Stopwatch.GetTimestamp() < stopTimestamp)
        {
            var scenario = selector.Next();
            var auditWriteProfile = auditWriteProfileSelector.Select(sequence);
            var startedAtUtc = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            int? statusCode = null;
            string? error = null;
            try
            {
                using var request = CreateRequest(
                    scenario,
                    worker,
                    credentials,
                    sequence,
                    auditWriteProfile);
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                statusCode = (int)response.StatusCode;
                var updated = await MixedLoadResponseConsumer.ConsumeAsync(
                    response,
                    scenario,
                    cancellationToken);
                if (response.StatusCode == HttpStatusCode.OK
                    && scenario.ProducesOutbox)
                {
                    if (updated is null)
                    {
                        error = "成功写请求未返回租户响应。";
                    }
                    else
                    {
                        worker.Version = updated.Version;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                error = $"{exception.GetType().Name}: {exception.Message}";
            }
            finally
            {
                stopwatch.Stop();
            }

            if (samples is not null)
            {
                samples.Enqueue(new MixedLoadRequestSample(
                    sequence,
                    worker.WorkerId,
                    scenario.Name,
                    startedAtUtc,
                    stopwatch.Elapsed.TotalMilliseconds,
                    statusCode,
                    (int)scenario.ExpectedStatusCode,
                    error,
                    auditWriteProfile));
            }

            sequence++;
        }
    }

    private static HttpRequestMessage CreateRequest(
        MixedLoadScenario scenario,
        MixedLoadWorkerState worker,
        MixedLoadCredentials credentials,
        long sequence,
        MixedLoadAuditWriteProfile auditWriteProfile)
    {
        var path = scenario.Path.Replace(
            "{tenantId}",
            worker.TenantId.ToString("D"),
            StringComparison.Ordinal);
        var request = new HttpRequestMessage(
            new HttpMethod(scenario.RequestMethod),
            path);
        if (scenario.Operation == MixedLoadOperation.Write)
        {
            var name = scenario.IsExpectedValidationFailure
                ? string.Empty
                : $"Mixed load {worker.WorkerId:D3}-{sequence:D10}";
            request.Content = JsonContent.Create(
                new UpdateHostTenantRequest(name, worker.Version));
        }

        var token = scenario.Authentication == MixedLoadAuthentication.Jwt
            ? credentials.Jwt
            : credentials.ApiKey;
        request.Headers.Authorization = new AuthenticationHeaderValue(
            scenario.Authentication == MixedLoadAuthentication.Jwt
                ? "Bearer"
                : "ApiKey",
            token);
        request.Headers.Add(
            MixedLoadAuditWritePolicy.HeaderName,
            MixedLoadAuditWritePolicy.GetToken(auditWriteProfile));
        return request;
    }

    private static async Task<MixedLoadCredentials> PrepareCredentialsAsync(
        HttpClient client,
        Guid adminUserId,
        CancellationToken cancellationToken)
    {
        using var loginRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("admin", TestPassword)),
        };
        loginRequest.Headers.Add("Origin", "http://localhost");
        using var loginResponse = await client.SendAsync(loginRequest, cancellationToken);
        loginResponse.EnsureSuccessStatusCode();
        var token = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken)
            ?? throw new InvalidOperationException("登录响应缺少 Token。");

        using var createApiKeyRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/identity/api-keys")
        {
            Content = JsonContent.Create(new CreateHostApiKeyRequest(
                adminUserId,
                "Mixed load benchmark",
                [
                    "platform.dashboard.read",
                    IdentityUserManagementPermissions.Read,
                    TenancyTenantManagementPermissions.Write,
                    "auditing.access.read",
                ],
                null)),
        };
        createApiKeyRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.AccessToken);
        using var createApiKeyResponse = await client.SendAsync(
            createApiKeyRequest,
            cancellationToken);
        createApiKeyResponse.EnsureSuccessStatusCode();
        var apiKey = await createApiKeyResponse.Content
            .ReadFromJsonAsync<CreateHostApiKeyResponse>(cancellationToken)
            ?? throw new InvalidOperationException("API Key 创建响应为空。");
        return new MixedLoadCredentials(token.AccessToken, apiKey.Secret);
    }

    private static async Task VerifyPreflightAsync(
        HttpClient client,
        MixedLoadWorkerState worker,
        MixedLoadCredentials credentials,
        IReadOnlyList<MixedLoadScenario> scenarios,
        CancellationToken cancellationToken)
    {
        using (var health = await client.GetAsync("/health/ready", cancellationToken))
        {
            health.EnsureSuccessStatusCode();
        }

        foreach (var scenario in scenarios)
        {
            using var request = CreateRequest(
                scenario,
                worker,
                credentials,
                sequence: 0,
                MixedLoadAuditWriteProfile.All);
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode != scenario.ExpectedStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"预检场景 {scenario.Name} 期望 {(int)scenario.ExpectedStatusCode}，"
                    + $"实际 {(int)response.StatusCode}：{body}");
            }

            var updated = await MixedLoadResponseConsumer.ConsumeAsync(
                response,
                scenario,
                cancellationToken);
            if (scenario.ProducesOutbox)
            {
                if (updated is null)
                {
                    throw new InvalidOperationException(
                        $"预检场景 {scenario.Name} 未返回租户。");
                }

                worker.Version = updated.Version;
            }
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

    private sealed record MixedLoadCredentials(string Jwt, string ApiKey);

    private sealed class MixedLoadWorkerState(
        int workerId,
        Guid tenantId,
        int version)
    {
        public int WorkerId { get; } = workerId;

        public Guid TenantId { get; } = tenantId;

        public int Version { get; set; } = version;
    }

    private sealed class MixedLoadApiFactory(
        DatabaseProvider provider,
        string connectionString) : WebApplicationFactory<ApiProgram>, IAsyncDisposable
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var settings = new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] = provider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] = connectionString,
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
                [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] = "Binary16",
                ["Identity:AllowDevelopmentEphemeralSigningKey"] = "true",
                ["Identity:EnableRemoteSuperAdministratorManagement"] = "true",
                ["Identity:AccessTokenMinutes"] = "60",
                ["Identity:LoginRateLimitPermitLimitPerMinute"] = "1000",
                ["Identity:AllowedOrigins:0"] = "http://localhost",
                ["Tenancy:HostDomains:0"] = "localhost",
                ["RateLimiting:GlobalApiPermitLimitPerMinute"] = "1000000",
                ["Files:Local:RootPath"] = Path.Combine(
                    Path.GetTempPath(),
                    "fullnet-mixed-load-files",
                    Guid.NewGuid().ToString("N")),
            };
            foreach (var setting in settings.Where(pair => pair.Value is not null))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseEnvironment("Testing");
            builder.UseContentRoot(Path.Combine(
                FindRepositoryRoot(),
                "src",
                "Hosts",
                "Full.NET.Host.Api"));
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(DecorateAuditCommandExecutor);
        }

        public HttpClient CreateBenchmarkClient()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("http://localhost"),
            });
            client.DefaultRequestHeaders.Host = "localhost";
            return client;
        }

        public async Task<MixedLoadSetup> InitializeAsync(
            int tenantCount,
            CancellationToken cancellationToken)
        {
            using var bootstrapClient = CreateBenchmarkClient();
            await using var scope = Services.CreateAsyncScope();
            var currentTenant = scope.ServiceProvider
                .GetRequiredService<CurrentTenantAccessor>();
            currentTenant.SetHost();
            try
            {
                var bootstrap = await scope.ServiceProvider
                    .GetRequiredService<IIdentityBootstrapService>()
                    .BootstrapHostAdminAsync(
                        new BootstrapHostAdminRequest(
                            "admin",
                            TestPassword,
                            "混合负载管理员"),
                        cancellationToken);
                if (!bootstrap.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"管理员引导失败：{bootstrap.Error?.Code}");
                }

                var tenants = new List<TenantSummary>(tenantCount);
                var provisioning = scope.ServiceProvider
                    .GetRequiredService<ITenantProvisioningService>();
                for (var index = 0; index < tenantCount; index++)
                {
                    var identifier = $"load-{index:D3}";
                    var result = await provisioning.ProvisionAsync(
                        new ProvisionTenantRequest(
                            identifier,
                            $"Mixed load tenant {index:D3}",
                            $"{identifier}.localhost"),
                        cancellationToken);
                    if (!result.IsSuccess)
                    {
                        throw new InvalidOperationException(
                            $"租户 {identifier} 准备失败：{result.Error?.Code}");
                    }

                    tenants.Add(result.Value!);
                }

                return new MixedLoadSetup(bootstrap.Value!.UserId, tenants);
            }
            finally
            {
                currentTenant.Clear();
            }
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("无法定位 Full.NET 仓库根目录。");
        }

        private static void DecorateAuditCommandExecutor(IServiceCollection services)
        {
            var descriptor = services.LastOrDefault(item =>
                    item.ServiceType == typeof(ICommandExecutor))
                ?? throw new InvalidOperationException(
                    "Benchmark Host 缺少 ICommandExecutor 注册。");
            services.Remove(descriptor);
            services.AddHttpContextAccessor();
            services.TryAddSingleton<MixedLoadAuditWriteTelemetry>();
            services.RemoveAll<IAuditWriteCapturePolicy>();
            services.AddScoped<
                IAuditWriteCapturePolicy,
                MixedLoadAuditWriteCapturePolicy>();
            services.AddScoped<ICommandExecutor>(provider =>
                new MixedLoadAuditCommandExecutor(
                    CreateOriginalCommandExecutor(provider, descriptor),
                    provider.GetRequiredService<IHttpContextAccessor>(),
                    provider.GetRequiredService<MixedLoadAuditWriteTelemetry>()));
        }

        private static ICommandExecutor CreateOriginalCommandExecutor(
            IServiceProvider provider,
            ServiceDescriptor descriptor)
        {
            var service = descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(provider)
                ?? (descriptor.ImplementationType is { } implementationType
                    ? ActivatorUtilities.CreateInstance(provider, implementationType)
                    : null);
            return service as ICommandExecutor
                ?? throw new InvalidOperationException(
                    "无法创建 Benchmark Host 的原始 ICommandExecutor。");
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            await base.DisposeAsync();
        }
    }

    private sealed record MixedLoadSetup(
        Guid AdminUserId,
        IReadOnlyList<TenantSummary> Tenants);
}

internal sealed class MixedLoadAuditCommandExecutor(
    ICommandExecutor inner,
    IHttpContextAccessor httpContextAccessor,
    MixedLoadAuditWriteTelemetry telemetry) : ICommandExecutor
{
    public async Task<int> ExecuteAsync(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var profile = ReadProfile();
        if (!MixedLoadAuditWritePolicy.ShouldExecute(profile, statement.Name))
        {
            return 1;
        }

        if (!MixedLoadAuditWritePolicy.IsAuditInsert(statement.Name))
        {
            return await inner.ExecuteAsync(
                    statement,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var stopwatch = Stopwatch.StartNew();
        var observedStatements =
            MixedLoadAuditWritePolicy.GetObservedStatements(statement.Name);
        try
        {
            var result = await inner.ExecuteAsync(
                    statement,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            foreach (var observedStatement in observedStatements)
            {
                telemetry.Record(
                    profile,
                    observedStatement,
                    stopwatch.Elapsed.TotalMilliseconds,
                    succeeded: true);
            }

            return result;
        }
        catch
        {
            stopwatch.Stop();
            foreach (var observedStatement in observedStatements)
            {
                telemetry.Record(
                    profile,
                    observedStatement,
                    stopwatch.Elapsed.TotalMilliseconds,
                    succeeded: false);
            }

            throw;
        }
    }

    private MixedLoadAuditWriteProfile ReadProfile()
    {
        var value = httpContextAccessor.HttpContext?
            .Request.Headers[MixedLoadAuditWritePolicy.HeaderName]
            .ToString();
        return string.IsNullOrWhiteSpace(value)
            ? MixedLoadAuditWriteProfile.All
            : MixedLoadAuditWritePolicy.ParseToken(value);
    }
}

internal sealed class MixedLoadAuditWriteCapturePolicy(
    IHttpContextAccessor httpContextAccessor) : IAuditWriteCapturePolicy
{
    public bool ShouldCapture(AuditWriteKinds kind)
    {
        var value = httpContextAccessor.HttpContext?
            .Request.Headers[MixedLoadAuditWritePolicy.HeaderName]
            .ToString();
        var profile = string.IsNullOrWhiteSpace(value)
            ? MixedLoadAuditWriteProfile.All
            : MixedLoadAuditWritePolicy.ParseToken(value);
        var requested = kind switch
        {
            AuditWriteKinds.Access => MixedLoadAuditWriteProfile.Access,
            AuditWriteKinds.Operation => MixedLoadAuditWriteProfile.Operation,
            AuditWriteKinds.Exception => MixedLoadAuditWriteProfile.Exception,
            _ => MixedLoadAuditWriteProfile.None,
        };
        return requested != MixedLoadAuditWriteProfile.None
            && profile.HasFlag(requested);
    }
}

public sealed class MixedLoadDapperTelemetry : IDisposable
{
    private const string MeterName = "fullnet.data.dapper";
    private readonly MeterListener _listener = new();
    private readonly ConcurrentDictionary<string, long> _statements =
        new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<double> _durations = new();
    private long _failures;
    private long _cancellations;

    public MixedLoadDapperTelemetry()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, MeterName, StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(OnLongMeasurement);
        _listener.SetMeasurementEventCallback<double>(OnDoubleMeasurement);
        _listener.Start();
    }

    public void Reset()
    {
        _statements.Clear();
        while (_durations.TryDequeue(out _))
        {
        }

        Interlocked.Exchange(ref _failures, 0);
        Interlocked.Exchange(ref _cancellations, 0);
    }

    public MixedLoadDapperSnapshot Snapshot()
    {
        var durations = _durations.ToArray();
        return new MixedLoadDapperSnapshot(
            _statements.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            durations.Length == 0
                ? null
                : MixedLoadLatencyStatistics.Calculate(durations),
            Interlocked.Read(ref _failures),
            Interlocked.Read(ref _cancellations));
    }

    public void Dispose() => _listener.Dispose();

    private void OnLongMeasurement(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        if (instrument.Name == "fullnet.data.sql.executions")
        {
            var statement = GetTag(tags, "statement_name") ?? "unknown";
            _statements.AddOrUpdate(statement, measurement, (_, value) => value + measurement);
        }
        else if (instrument.Name == "fullnet.data.sql.failures")
        {
            if (string.Equals(
                    GetTag(tags, "outcome"),
                    "canceled",
                    StringComparison.Ordinal))
            {
                Interlocked.Add(ref _cancellations, measurement);
            }
            else
            {
                Interlocked.Add(ref _failures, measurement);
            }
        }
    }

    private void OnDoubleMeasurement(
        Instrument instrument,
        double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        if (instrument.Name == "fullnet.data.sql.duration")
        {
            _durations.Enqueue(measurement);
        }
    }

    private static string? GetTag(
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        string key)
    {
        foreach (var tag in tags)
        {
            if (string.Equals(tag.Key, key, StringComparison.Ordinal))
            {
                return Convert.ToString(tag.Value, CultureInfo.InvariantCulture);
            }
        }

        return null;
    }
}

internal abstract class MixedLoadDatabase : IAsyncDisposable
{
    private const string Password = "FullNet_MixedLoadDb!123";

    protected MixedLoadDatabase(string connectionString)
    {
        ConnectionString = connectionString;
    }

    public abstract DatabaseProvider Provider { get; }

    public abstract string ContainerImage { get; }

    public abstract string DatabaseVersion { get; }

    public abstract string ContainerId { get; }

    public string ConnectionString { get; }

    public static async Task<MixedLoadDatabase> StartAsync(
        string provider,
        string poolName,
        CancellationToken cancellationToken)
    {
        MixedLoadDatabase database = provider switch
        {
            "sqlserver" => await SqlServerMixedLoadDatabase.StartContainerAsync(
                Password,
                poolName,
                cancellationToken),
            "mysql" => await MySqlMixedLoadDatabase.StartContainerAsync(
                Password,
                poolName,
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "不支持的数据库 Provider。"),
        };
        try
        {
            await database.MigrateAsync(cancellationToken);
            if (database.Provider == DatabaseProvider.SqlServer)
            {
                SqlConnection.ClearAllPools();
            }
            else
            {
                MySqlConnection.ClearAllPools();
            }

            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public async Task<MixedLoadDatabaseSnapshot> CaptureStateAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var accessLogs = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM fn_auditing_access_log",
                cancellationToken: cancellationToken));
        var operationLogs = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM fn_auditing_operation_log",
                cancellationToken: cancellationToken));
        var exceptionLogs = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM fn_auditing_exception_log",
                cancellationToken: cancellationToken));
        var outboxPending = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM fn_outbox_message
                WHERE ProcessedAtUtc IS NULL AND DeadLetteredAtUtc IS NULL
                """,
                cancellationToken: cancellationToken));
        var oldestOutbox = await ReadOldestPendingOutboxAsync(
            connection,
            cancellationToken);
        var databaseMetrics = await CaptureDatabaseMetricsAsync(
            connection,
            cancellationToken);
        return new MixedLoadDatabaseSnapshot(
            DateTimeOffset.UtcNow,
            accessLogs,
            outboxPending,
            oldestOutbox,
            databaseMetrics.DatabaseSessions,
            databaseMetrics.ActiveLocks,
            databaseMetrics.LockWaitCount,
            databaseMetrics.LockWaitMilliseconds,
            databaseMetrics.Error,
            operationLogs,
            exceptionLogs,
            databaseMetrics.LogBytesWritten,
            databaseMetrics.UndoHistoryLength);
    }

    public async Task SeedExpiredProcessedOutboxAsync(
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        var occurredAtUtc = DateTimeOffset.UtcNow.AddDays(-61);
        var processedAtUtc = occurredAtUtc.AddMinutes(1);
        const int insertBatchSize = 250;
        for (var offset = 0; offset < count; offset += insertBatchSize)
        {
            var currentBatchSize = Math.Min(insertBatchSize, count - offset);
            var sql = new StringBuilder(
                """
                INSERT INTO fn_outbox_message
                    (Id, MessageType, SchemaVersion, ContentType, TenantId,
                     TraceId, Payload, OccurredAtUtc, ProcessedAtUtc, Attempts)
                VALUES
                """);
            var parameters = new DynamicParameters();
            parameters.Add("MessageType", "benchmark.retention.expired");
            parameters.Add("SchemaVersion", 1);
            parameters.Add("ContentType", "application/x-msgpack");
            parameters.Add("Payload", new byte[] { 0x90 });
            parameters.Add("OccurredAtUtc", occurredAtUtc);
            parameters.Add("ProcessedAtUtc", processedAtUtc);
            for (var index = 0; index < currentBatchSize; index++)
            {
                if (index > 0)
                {
                    sql.AppendLine(",");
                }

                var parameterName = $"Id{index}";
                sql.Append(
                    $"(@{parameterName}, @MessageType, @SchemaVersion, "
                    + "@ContentType, NULL, NULL, @Payload, @OccurredAtUtc, "
                    + "@ProcessedAtUtc, 0)");
                parameters.Add(parameterName, Guid.CreateVersion7());
            }

            await connection.ExecuteAsync(new CommandDefinition(
                sql.ToString(),
                parameters,
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ResetOutboxAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM fn_outbox_message",
            cancellationToken: cancellationToken));
    }

    public async Task SeedPendingOutboxAsync(
        int count,
        int payloadSize,
        string messageType,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfLessThan(payloadSize, 64);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            cancellationToken);
        var payload = new byte[payloadSize];
        Random.Shared.NextBytes(payload);
        var occurredAtUtc = DateTimeOffset.UtcNow;
        const int insertBatchSize = 250;
        for (var offset = 0; offset < count; offset += insertBatchSize)
        {
            var currentBatchSize = Math.Min(insertBatchSize, count - offset);
            var sql = new StringBuilder(
                """
                INSERT INTO fn_outbox_message
                    (Id, MessageType, SchemaVersion, ContentType, TenantId,
                     TraceId, Payload, OccurredAtUtc, Attempts)
                VALUES
                """);
            var parameters = new DynamicParameters();
            parameters.Add("MessageType", messageType);
            parameters.Add("SchemaVersion", 1);
            parameters.Add("ContentType", "application/x-msgpack");
            parameters.Add("Payload", payload);
            parameters.Add("OccurredAtUtc", occurredAtUtc);
            for (var index = 0; index < currentBatchSize; index++)
            {
                if (index > 0)
                {
                    sql.AppendLine(",");
                }

                var parameterName = $"Id{index}";
                sql.Append(
                    $"(@{parameterName}, @MessageType, @SchemaVersion, "
                    + "@ContentType, NULL, NULL, @Payload, @OccurredAtUtc, 0)");
                parameters.Add(parameterName, Guid.CreateVersion7());
            }

            await connection.ExecuteAsync(new CommandDefinition(
                sql.ToString(),
                parameters,
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public abstract ValueTask DisposeAsync();

    protected abstract DbConnection CreateConnection();

    protected abstract Task<MixedLoadDatabaseMetrics> CaptureDatabaseMetricsAsync(
        DbConnection connection,
        CancellationToken cancellationToken);

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await new DbUpMigrationRunner(
                Options.Create(new DatabaseOptions
                {
                    Provider = Provider,
                    ConnectionString = ConnectionString,
                    MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
                    CommandTimeoutSeconds = 300,
                }),
                NullLoggerFactory.Instance,
                Options.Create(new UuidBinaryContractOptions
                {
                    MaintenanceMode = true,
                    BackupVerified = true,
                    LegacyWritersStopped = true,
                    DestructiveDdlApprovalId = "benchmark-mixed-load-uuid",
                }),
                Options.Create(new PreV1NamingContractOptions
                {
                    MaintenanceMode = true,
                    BackupVerified = true,
                    LegacyWritersStopped = true,
                    LegacyOutboxDrained = true,
                    DestructiveDdlApprovalId = "benchmark-mixed-load-naming",
                }))
            .MigrateAsync(cancellationToken);
    }

    private static async Task<DateTimeOffset?> ReadOldestPendingOutboxAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var value = await connection.ExecuteScalarAsync<object?>(
            new CommandDefinition(
                """
                SELECT MIN(OccurredAtUtc)
                FROM fn_outbox_message
                WHERE ProcessedAtUtc IS NULL AND DeadLetteredAtUtc IS NULL
                """,
                cancellationToken: cancellationToken));
        return value switch
        {
            null or DBNull => null,
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime(),
            DateTime dateTime => new DateTimeOffset(
                DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => DateTimeOffset.Parse(
                Convert.ToString(value, CultureInfo.InvariantCulture)!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
        };
    }
}

internal sealed class SqlServerMixedLoadDatabase(
    MsSqlContainer container,
    string connectionString,
    string databaseVersion) : MixedLoadDatabase(connectionString)
{
    public const string Image = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";

    public override DatabaseProvider Provider => DatabaseProvider.SqlServer;

    public override string ContainerImage => Image;

    public override string DatabaseVersion => databaseVersion;

    public override string ContainerId => container.Id;

    public static async Task<SqlServerMixedLoadDatabase> StartContainerAsync(
        string password,
        string poolName,
        CancellationToken cancellationToken)
    {
        var container = new MsSqlBuilder(Image)
            .WithPassword(password)
            .Build();
        await container.StartAsync(cancellationToken);
        try
        {
            var baseConnectionString = container.GetConnectionString();
            await using var connection = new SqlConnection(baseConnectionString);
            await connection.OpenAsync(cancellationToken);
            var version = await connection.ExecuteScalarAsync<string>(
                new CommandDefinition(
                    "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128))",
                    cancellationToken: cancellationToken))
                ?? "unknown";
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "CREATE DATABASE [fullnet_mixed_load]",
                    cancellationToken: cancellationToken));
            var connectionString = new SqlConnectionStringBuilder(baseConnectionString)
            {
                InitialCatalog = "fullnet_mixed_load",
                ApplicationName = poolName,
                MaxPoolSize = MixedLoadConnectionPoolTelemetry.MaximumPoolSize,
            }.ConnectionString;
            return new SqlServerMixedLoadDatabase(container, connectionString, version);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await container.DisposeAsync();
    }

    protected override DbConnection CreateConnection() =>
        new SqlConnection(ConnectionString);

    protected override async Task<MixedLoadDatabaseMetrics> CaptureDatabaseMetricsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            var metrics = await connection.QuerySingleAsync<SqlServerMetrics>(
                new CommandDefinition(
                    """
                    SELECT
                        (SELECT COUNT(*)
                         FROM sys.dm_exec_sessions
                         WHERE database_id = DB_ID()) AS DatabaseSessions,
                        (SELECT COUNT(*)
                         FROM sys.dm_tran_locks
                         WHERE resource_database_id = DB_ID()) AS ActiveLocks,
                        (SELECT COALESCE(SUM(waiting_tasks_count), 0)
                         FROM sys.dm_os_wait_stats
                         WHERE wait_type LIKE 'LCK[_]%') AS LockWaitCount,
                        (SELECT COALESCE(SUM(wait_time_ms), 0)
                         FROM sys.dm_os_wait_stats
                         WHERE wait_type LIKE 'LCK[_]%') AS LockWaitMilliseconds,
                        (SELECT COALESCE(SUM(vfs.num_of_bytes_written), 0)
                         FROM sys.dm_io_virtual_file_stats(DB_ID(), NULL) AS vfs
                         INNER JOIN sys.database_files AS database_file
                             ON database_file.file_id = vfs.file_id
                         WHERE database_file.type_desc = 'LOG') AS LogBytesWritten
                    """,
                    cancellationToken: cancellationToken));
            return new MixedLoadDatabaseMetrics(
                metrics.DatabaseSessions,
                metrics.ActiveLocks,
                metrics.LockWaitCount,
                metrics.LockWaitMilliseconds,
                metrics.LogBytesWritten,
                null,
                null);
        }
        catch (Exception exception)
        {
            return new MixedLoadDatabaseMetrics(
                null,
                null,
                null,
                null,
                null,
                null,
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private sealed record SqlServerMetrics(
        int DatabaseSessions,
        int ActiveLocks,
        long LockWaitCount,
        long LockWaitMilliseconds,
        long LogBytesWritten);
}

internal sealed class MySqlMixedLoadDatabase(
    MySqlContainer container,
    string connectionString,
    string diagnosticConnectionString,
    string databaseVersion) : MixedLoadDatabase(connectionString)
{
    public const string Image = "mysql:8.0";

    public override DatabaseProvider Provider => DatabaseProvider.MySql;

    public override string ContainerImage => Image;

    public override string DatabaseVersion => databaseVersion;

    public override string ContainerId => container.Id;

    public static async Task<MySqlMixedLoadDatabase> StartContainerAsync(
        string password,
        string poolName,
        CancellationToken cancellationToken)
    {
        var container = new MySqlBuilder(Image)
            .WithCommand("--log-bin-trust-function-creators=1")
            .WithDatabase("fullnet_mixed_load")
            .WithUsername("fullnet")
            .WithPassword(password)
            .Build();
        await container.StartAsync(cancellationToken);
        try
        {
            var policyConnectionString = MySqlConnectionStringPolicy.Create(
                container.GetConnectionString(),
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false);
            var connectionString = new MySqlConnectionStringBuilder(
                policyConnectionString)
            {
                ApplicationName = poolName,
                MaximumPoolSize =
                    MixedLoadConnectionPoolTelemetry.MaximumPoolSize,
            }.ConnectionString;
            var diagnosticConnectionString = new MySqlConnectionStringBuilder(
                connectionString)
            {
                UserID = "root",
                Password = password,
                ApplicationName = $"{poolName}-diagnostics",
                MaximumPoolSize = 2,
            }.ConnectionString;
            await using var connection = new MySqlConnection(connectionString);
            var version = await connection.ExecuteScalarAsync<string>(
                new CommandDefinition(
                    "SELECT VERSION()",
                    cancellationToken: cancellationToken))
                ?? "unknown";
            return new MySqlMixedLoadDatabase(
                container,
                connectionString,
                diagnosticConnectionString,
                version);
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        MySqlConnection.ClearAllPools();
        await container.DisposeAsync();
    }

    protected override DbConnection CreateConnection() =>
        new MySqlConnection(ConnectionString);

    protected override async Task<MixedLoadDatabaseMetrics> CaptureDatabaseMetricsAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            // 业务负载继续使用最小权限账号；仅隔离容器内的诊断连接读取 PROCESS 指标。
            await using var diagnosticConnection =
                new MySqlConnection(diagnosticConnectionString);
            await diagnosticConnection.OpenAsync(cancellationToken);
            var sessions = await ReadStatusAsync(
                diagnosticConnection,
                "Threads_connected",
                cancellationToken);
            var currentWaiters = await ReadStatusAsync(
                diagnosticConnection,
                "Innodb_row_lock_current_waits",
                cancellationToken);
            var lockWaits = await ReadStatusAsync(
                diagnosticConnection,
                "Innodb_row_lock_waits",
                cancellationToken);
            var lockWaitMilliseconds = await ReadStatusAsync(
                diagnosticConnection,
                "Innodb_row_lock_time",
                cancellationToken);
            var logBytesWritten = await ReadStatusAsync(
                diagnosticConnection,
                "Innodb_os_log_written",
                cancellationToken);
            var engineStatus = await diagnosticConnection
                .QuerySingleAsync<MySqlEngineStatusRow>(
                    new CommandDefinition(
                        "SHOW ENGINE INNODB STATUS",
                        cancellationToken: cancellationToken));
            var undoHistoryLength =
                MixedLoadMySqlStatusParser.ParseHistoryListLength(
                    engineStatus.Status)
                ?? throw new InvalidOperationException(
                    "MySQL InnoDB status 缺少 History list length。");
            return new MixedLoadDatabaseMetrics(
                sessions,
                currentWaiters,
                lockWaits,
                lockWaitMilliseconds,
                logBytesWritten,
                undoHistoryLength,
                null);
        }
        catch (Exception exception)
        {
            return new MixedLoadDatabaseMetrics(
                null,
                null,
                null,
                null,
                null,
                null,
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static async Task<long> ReadStatusAsync(
        DbConnection connection,
        string variableName,
        CancellationToken cancellationToken)
    {
        var commandText = variableName switch
        {
            "Threads_connected" => "SHOW GLOBAL STATUS LIKE 'Threads_connected'",
            "Innodb_row_lock_current_waits" =>
                "SHOW GLOBAL STATUS LIKE 'Innodb_row_lock_current_waits'",
            "Innodb_row_lock_waits" =>
                "SHOW GLOBAL STATUS LIKE 'Innodb_row_lock_waits'",
            "Innodb_row_lock_time" =>
                "SHOW GLOBAL STATUS LIKE 'Innodb_row_lock_time'",
            "Innodb_os_log_written" =>
                "SHOW GLOBAL STATUS LIKE 'Innodb_os_log_written'",
            _ => throw new ArgumentOutOfRangeException(
                nameof(variableName),
                variableName,
                "不支持的 MySQL 状态变量。"),
        };
        var row = await connection.QuerySingleAsync<MySqlStatusRow>(
            new CommandDefinition(
                commandText,
                cancellationToken: cancellationToken));
        return long.Parse(
            row.Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture);
    }

    private sealed record MySqlStatusRow(string Variable_name, string Value);

    private sealed record MySqlEngineStatusRow(
        string Type,
        string Name,
        string Status);
}

internal sealed record MixedLoadDatabaseMetrics(
    long? DatabaseSessions,
    long? ActiveLocks,
    long? LockWaitCount,
    double? LockWaitMilliseconds,
    long? LogBytesWritten,
    long? UndoHistoryLength,
    string? Error);

public static partial class MixedLoadMySqlStatusParser
{
    public static long? ParseHistoryListLength(string status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var match = HistoryListLengthRegex().Match(status);
        return match.Success
            && long.TryParse(
                match.Groups[1].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value)
            ? value
            : null;
    }

    [GeneratedRegex(
        @"History list length\s+(\d+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex HistoryListLengthRegex();
}

internal sealed class MixedLoadConsoleSilencer : IDisposable
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private bool _disposed;

    private MixedLoadConsoleSilencer()
    {
        _output = Console.Out;
        _error = Console.Error;
        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
    }

    public static MixedLoadConsoleSilencer Suppress() => new();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Console.SetOut(_output);
        Console.SetError(_error);
        _disposed = true;
    }
}
