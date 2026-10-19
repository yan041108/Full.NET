using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using DotNet.Testcontainers.Containers;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Settings.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using StackExchange.Redis;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.Redis;

namespace Full.NET.IntegrationTests.Capacity;

/// <summary>显式链接到冻结测试消费者的容量生命周期驱动；方法通过只代表实验已完成，负载预算单独判定。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeCacheCapacityDriver
{
    private const string Grid = "/api/v1/me/grid-preferences/identity.users";
    private const string Password = "FullNet_Capacity!2026";
    private const int ExperimentPermitLimitPerMinute = 1_000_000;

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Run_isolated_native_cache_capacity(DatabaseProvider provider)
    {
        var output = Environment.GetEnvironmentVariable("FULLNET_CAPACITY_RESULTS")
            ?? throw new InvalidOperationException("Capacity output must be explicitly selected.");
        var owner = Environment.GetEnvironmentVariable("FULLNET_CAPACITY_OWNER")
            ?? throw new InvalidOperationException("Capacity owner is required.");
        var abort = Path.Combine(output, "abort.json");
        if (File.Exists(abort)) throw new InvalidOperationException("Capacity owner requested cleanup.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(50));
        var token = deadline.Token;
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        // 容器资源和所有权在创建前声明，不修改共享数据库或其他任务的容器配置。
        await using var db = provider == DatabaseProvider.SqlServer
            ? (IContainer)new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
                .WithPassword(Password).WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2048")
                .WithLabel("fullnet.cache-capacity.owner", owner)
                .WithCreateParameterModifier(p => { p.HostConfig ??= new(); p.HostConfig.NanoCPUs = 2_000_000_000; p.HostConfig.Memory = 3L * 1024 * 1024 * 1024; p.HostConfig.MemorySwap = p.HostConfig.Memory; }).Build()
            : new MySqlBuilder("mysql:8.0").WithDatabase("fullnet_capacity").WithUsername("fullnet").WithPassword(Password)
                // 与 SharedDatabaseFixture 一致，允许正式迁移创建函数；仅配置当前隔离容器。
                .WithCommand("--log-bin-trust-function-creators=1")
                .WithLabel("fullnet.cache-capacity.owner", owner)
                .WithCreateParameterModifier(p => { p.HostConfig ??= new(); p.HostConfig.NanoCPUs = 2_000_000_000; p.HostConfig.Memory = 2L * 1024 * 1024 * 1024; p.HostConfig.MemorySwap = p.HostConfig.Memory; }).Build();
        await using var redis = new RedisBuilder("redis:8.6").WithLabel("fullnet.cache-capacity.owner", owner)
            .WithCreateParameterModifier(p => { p.HostConfig ??= new(); p.HostConfig.NanoCPUs = 1_000_000_000; p.HostConfig.Memory = 512L * 1024 * 1024; p.HostConfig.MemorySwap = p.HostConfig.Memory; }).Build();
        await db.StartAsync(token);
        await redis.StartAsync(token);
        var connection = provider == DatabaseProvider.SqlServer
            ? ((MsSqlContainer)db).GetConnectionString() : ((MySqlContainer)db).GetConnectionString();
        var budgetBytes = await File.ReadAllBytesAsync(Path.Combine(output, "harness", "database-budget.json"), token);
        using var budgetDocument = JsonDocument.Parse(budgetBytes);
        var budget = JsonSerializer.Deserialize<DatabaseCapacityOptions>(budgetBytes)
            ?? throw new InvalidOperationException("Explicit database budget is required.");
        Assert.IsTrue(budget.Enabled);
        var budgetHash = Convert.ToHexString(SHA256.HashData(budgetBytes)).ToLowerInvariant();
        if (provider == DatabaseProvider.SqlServer)
        {
            await using var master = new SqlConnection(connection);
            await master.ExecuteAsync(new CommandDefinition("CREATE DATABASE fullnet_capacity", cancellationToken: token));
            connection = new SqlConnectionStringBuilder(connection) {InitialCatalog = "fullnet_capacity"}.ConnectionString;
        }
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connection, token);
        // 迁移和控制连接保持独立；两个 API 显式限制同一池预算，不能各用默认 100 抢占 151 个服务端连接。
        var apiConnection = provider == DatabaseProvider.SqlServer
            ? new SqlConnectionStringBuilder(connection) {MaxPoolSize = budget.ExpectedMaxPoolSize, MinPoolSize = 0}.ConnectionString
            : new MySqlConnectionStringBuilder(connection) {MaximumPoolSize = checked((uint)budget.ExpectedMaxPoolSize), MinimumPoolSize = 0}.ConnectionString;
        var actualPoolSize = provider == DatabaseProvider.SqlServer
            ? new SqlConnectionStringBuilder(apiConnection).MaxPoolSize
            : checked((int)new MySqlConnectionStringBuilder(apiConnection).MaximumPoolSize);
        Assert.AreEqual(budget.ExpectedMaxPoolSize, actualPoolSize);
        if (provider == DatabaseProvider.MySql)
        {
            await using var check = new MySqlConnection(connection);
            var actualServerLimit = await check.ExecuteScalarAsync<int>(new CommandDefinition("SELECT @@max_connections", cancellationToken: token));
            Assert.IsTrue(budget.TotalBudget <= actualServerLimit, "Declared database budget exceeds actual server limit.");
        }
        using var signing = RSA.Create(2048);
        // 仅实验控制连接允许 INFO/CLIENT 管理命令，业务 API 仍使用原来的 Redis 配置。
        var redisControl = ConfigurationOptions.Parse(redis.GetConnectionString());
        redisControl.AllowAdmin = true;
        redisControl.AsyncTimeout = 10_000;
        using var redisClient = await ConnectionMultiplexer.ConnectAsync(redisControl);
        foreach (var mode in new[] {"C0", "S2"})
        {
            if (File.Exists(abort)) throw new InvalidOperationException("Capacity owner requested cleanup.");
            var dir = Path.Combine(output, provider + "-" + mode);
            Directory.CreateDirectory(dir);
            var settings = new Dictionary<string, string?>
            {
                ["Cache__RedisConnectionString"] = redis.GetConnectionString(),
                ["Cache__ApplicationName"] = "capacity-" + Guid.NewGuid().ToString("N"),
                ["Cache__Entries__settings.grid-preference__OwnerModule"] = "settings",
                ["Cache__Entries__settings.grid-preference__ConsistencyClass"] = mode,
                ["Cache__Entries__settings.grid-preference__L1Duration"] = mode == "C0" ? "00:00:00" : "00:00:02",
                ["Cache__Entries__settings.grid-preference__L2Duration"] = mode == "C0" ? "00:00:00" : "00:00:30",
                ["Cache__Entries__settings.grid-preference__Jitter"] = "00:00:00",
                ["Cache__Entries__settings.grid-preference__FailSafeEnabled"] = "false",
                ["Identity__ActiveKeyId"] = "capacity",
                // 独立压测实例使用显式高额度，避免把默认 1200/分钟的反滥用限流误认成缓存容量。
                ["RateLimiting__EnableGlobalApiLimit"] = "true",
                ["RateLimiting__GlobalApiPermitLimitPerMinute"] = ExperimentPermitLimitPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Identity__SigningKeys__capacity__PublicKeyPem"] = signing.ExportSubjectPublicKeyInfoPem().Replace("\n", "\\n", StringComparison.Ordinal),
                ["Identity__SigningKeys__capacity__PrivateKeyPem"] = signing.ExportPkcs8PrivateKeyPem().Replace("\n", "\\n", StringComparison.Ordinal),
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = Environment.GetEnvironmentVariable("FULLNET_CAPACITY_OTLP"),
                ["OTEL_METRIC_EXPORT_INTERVAL"] = "1000",
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "grpc",
                ["OTEL_SERVICE_NAME"] = "Full.NET.Host.Api",
            };
            // 使用 API 角色的既有准入实现；有界队列只属于实验配置，不修改生产默认或认证失败语义。
            foreach (var name in new[] {"Enabled", "HostRole", "PermitLimit", "QueueLimit", "AcquireTimeoutMilliseconds",
                "ExpectedMaxPoolSize", "HealthReserve", "CriticalWorkerReserve", "AuthorizationReadReserve", "ApiMaxReplicas",
                "ApiMaxPoolSize", "WorkerMaxReplicas", "WorkerMaxPoolSize", "MigrationReserve", "TotalBudget"})
                settings["DatabaseCapacity__" + name] = budgetDocument.RootElement.GetProperty(name).GetRawText();
            await using var primary = await NativeApiProcessHost.StartAsync(artifact, provider, apiConnection, settings, NativeAotTestTimeouts.ProcessStartup, token);
            await using var peer = await NativeApiProcessHost.StartAsync(artifact, provider, apiConnection, settings, NativeAotTestTimeouts.ProcessStartup, token);
            var primaryIdentity = await ReadIdentityAsync("cache-node-one", primary, Path.GetFileName(dir), token);
            var peerIdentity = await ReadIdentityAsync("cache-node-two", peer, Path.GetFileName(dir), token);
            Assert.AreNotEqual(primaryIdentity.instance, peerIdentity.instance);
            await AtomicAsync(Path.Combine(dir, "api-identities.json"), new[] {primaryIdentity, peerIdentity}, token);
            using var writer = primary.CreateClient();
            var access = await NativeApiE2EAssertions.LoginAsync(writer, primary.LogFilePath, token);
            writer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var reader = peer.CreateClient();
            reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
            await AtomicAsync(Path.Combine(dir, "experiment-config.json"), new
            {
                provider = provider.ToString(), mode, apiInstances = 2, workerInstances = 0,
                globalApiLimitEnabled = true, globalApiPermitLimitPerMinute = ExperimentPermitLimitPerMinute,
                databaseBudget = new {profileSha256 = budgetHash, profile = budgetDocument.RootElement.Clone(), apiConnectionMaximumPoolSize = actualPoolSize},
                quotaScope = "Isolated experiment only; production defaults unchanged; authentication/session/tenant checks retained",
            }, token);
            Assert.IsNotNull(await writer.GetFromJsonAsync<GridPreferenceResponse>(Grid, token));
            Assert.IsNotNull(await reader.GetFromJsonAsync<GridPreferenceResponse>(Grid, token));
            using (var policyResponse = await writer.GetAsync("/api/v1/observability/cache-policies", token))
            {
                policyResponse.EnsureSuccessStatusCode();
                var bytes = await policyResponse.Content.ReadAsStringAsync(token);
                await File.WriteAllTextAsync(Path.Combine(dir, "runtime-policies.json"), bytes, token);
                using var policies = JsonDocument.Parse(bytes);
                var policy = policies.RootElement.EnumerateArray().Single(p => p.GetProperty("entryName").GetString() == "settings.grid-preference");
                Assert.AreEqual(mode, policy.GetProperty("consistencyClass").GetString(), ignoreCase: true);
            }
            // 同一签名、同一真实会话进入两个节点，保持默认会话安全策略；令牌只存在短期控制文件。
            var ready = Path.Combine(dir, "private-ready.json");
            async Task PublishReadyAsync() => await AtomicAsync(ready, new
            {
                urls = new[] {primary.BaseAddress.ToString().TrimEnd('/'), peer.BaseAddress.ToString().TrimEnd('/')},
                tokens = new[] {access, access}, expiresAtUtc = TokenExpiresAt(access),
                databaseContainer = db.Id, redisContainer = redis.Id, provider = provider.ToString(), mode,
            }, token);
            try
            {
                await PublishReadyAsync();
                var last = "";
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (File.Exists(abort)) throw new InvalidOperationException("Capacity owner requested cleanup.");
                    var commandFile = Path.Combine(dir, "command.json");
                    if (!File.Exists(commandFile)) { await Task.Delay(100, token); continue; }
                    // 允许 Windows 控制器在读取期间原子替换路径；当前句柄继续读取完整旧命令，下一轮读取新命令。
                    JsonDocument commandDocument;
                    await using (var commandStream = new FileStream(commandFile, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous))
                        commandDocument = await JsonDocument.ParseAsync(commandStream, cancellationToken: token);
                    using var command = commandDocument;
                    var id = command.RootElement.GetProperty("id").GetString()!;
                    if (id == last) { await Task.Delay(100, token); continue; }
                    last = id;
                    var action = command.RootElement.GetProperty("action").GetString();
                    if (action == "finish") break;
                    if (action == "refresh")
                    {
                        // 只在测段之间重新走真实登录，不延长默认 JWT 生命周期；新会话同时用于两个节点。
                        writer.DefaultRequestHeaders.Authorization = null;
                        access = await NativeApiE2EAssertions.LoginAsync(writer, primary.LogFilePath, token);
                        writer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
                        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
                        Assert.IsNotNull(await writer.GetFromJsonAsync<GridPreferenceResponse>(Grid, token));
                        Assert.IsNotNull(await reader.GetFromJsonAsync<GridPreferenceResponse>(Grid, token));
                        await PublishReadyAsync();
                        await AtomicAsync(Path.Combine(dir, "reply.json"), new {id, result = new {expiresAtUtc = TokenExpiresAt(access)}}, token);
                        continue;
                    }
                    object result = action switch
                    {
                        "snapshot" => await SnapshotAsync(provider, connection, redisClient, token),
                        "pause" => (await redisClient.GetDatabase().ExecuteAsync("CLIENT", "PAUSE", "5000", "ALL")).ToString(),
                        "write" => await WriteAsync(writer, reader, token),
                        "prime-grid" => await PrimeGridAsync(command.RootElement.GetProperty("role").GetString()!, writer, reader, token),
                        _ => throw new InvalidOperationException("Unknown capacity command."),
                    };
                    await AtomicAsync(Path.Combine(dir, "reply.json"), new {id, result}, token);
                }
            }
            finally { File.Delete(ready); File.Delete(ready + ".tmp"); }
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await peer.StopGracefullyAsync(shutdown.Token);
            await primary.StopGracefullyAsync(shutdown.Token);
            primary.AssertNoFatalMarkersInLogs(); peer.AssertNoFatalMarkersInLogs();
            await File.WriteAllTextAsync(Path.Combine(dir, "lifecycle-completed.json"), "{\"completed\":true}", token);
        }
    }

    private sealed record ApiIdentity(string role, string instance, string application, string context, string baseAddress,
        string logFile, string logFileName, string announcement, string announcementSha256);

    // 角色来自本轮受控宿主对象及其日志公告，不把外部固定 OTEL 属性当成真实进程身份。
    private static async Task<ApiIdentity> ReadIdentityAsync(string role, NativeApiProcessHost host, string context, CancellationToken token)
    {
        var identities = new List<ApiIdentity>();
        foreach (var line in await File.ReadAllLinesAsync(host.LogFilePath, token))
        {
            if (!line.StartsWith('{')) continue;
            using var document = JsonDocument.Parse(line);
            var entry = document.RootElement;
            if (!entry.TryGetProperty("SourceContext", out var source)
                || source.GetString() != "Full.NET.Hosting.Observability.LoggingResourceAnnouncementService") continue;
            Assert.AreEqual("Full.NET.Host.Api", entry.GetProperty("Application").GetString());
            Assert.AreEqual("Api", entry.GetProperty("HostRole").GetString());
            var instance = entry.GetProperty("Instance").GetString()!;
            Assert.IsTrue(Guid.TryParseExact(instance, "D", out _));
            identities.Add(new ApiIdentity(role, instance, "Full.NET.Host.Api", context, host.BaseAddress.ToString(),
                host.LogFilePath, Path.GetFileName(host.LogFilePath), line,
                Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(line))).ToLowerInvariant()));
        }
        Assert.AreEqual(1, identities.Count);
        return identities[0];
    }

    private static async Task AtomicAsync(string file, object value, CancellationToken token)
    {
        await File.WriteAllTextAsync(file + ".tmp", JsonSerializer.Serialize(value), token);
        File.Move(file + ".tmp", file, overwrite: true);
    }

    private static DateTimeOffset TokenExpiresAt(string access)
    {
        var payload = access.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        using var claims = JsonDocument.Parse(Convert.FromBase64String(payload));
        return DateTimeOffset.FromUnixTimeSeconds(claims.RootElement.GetProperty("exp").GetInt64());
    }

    private static async Task<object> PrimeGridAsync(string role, HttpClient writer, HttpClient reader, CancellationToken token)
    {
        // 端口属于当前 SDK 容器；只向尚未真实回源的节点读取相同热键，等待自然 TTL，不修改业务值。
        var client = role switch
        {
            "cache-node-one" => writer,
            "cache-node-two" => reader,
            _ => throw new InvalidOperationException("Unknown baseline API role."),
        };
        var value = await client.GetFromJsonAsync<GridPreferenceResponse>(Grid, token);
        Assert.IsNotNull(value);
        Assert.AreEqual("identity.users", value.GridKey);
        Assert.IsNotNull(value.Columns);
        return new {role, status = 200};
    }

    private static async Task<object> WriteAsync(HttpClient writer, HttpClient reader, CancellationToken token)
    {
        var current = await writer.GetFromJsonAsync<GridPreferenceResponse>(Grid, token);
        Assert.IsNotNull(current);
        var width = current.Columns.FirstOrDefault()?.Width == 160 ? 224 : 160;
        var clock = Stopwatch.StartNew();
        using var response = await writer.PutAsJsonAsync(Grid, new UpdateGridPreferenceRequest(current.SchemaVersion,
            [new GridColumnPreference("employeeNumber", 0, width, true, null)], current.Version), token);
        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<GridPreferenceResponse>(token);
        Assert.IsNotNull(saved);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        var requests = 2;
        while (true)
        {
            var observed = await reader.GetFromJsonAsync<GridPreferenceResponse>(Grid, budget.Token); requests++;
            if (observed?.Version == saved.Version && observed.Columns.Single().Width == width) break;
            await Task.Delay(25, budget.Token);
        }
        return new {propagationMs = clock.Elapsed.TotalMilliseconds, requests};
    }

    private static async Task<object> SnapshotAsync(DatabaseProvider provider, string connection, IConnectionMultiplexer redis, CancellationToken token)
    {
        object database;
        int? serverConnectionLimit = null;
        if (provider == DatabaseProvider.SqlServer)
        {
            await using var sql = new SqlConnection(connection);
            var io = (await sql.QueryAsync(new CommandDefinition("""
                SELECT num_of_reads AS Reads,num_of_writes AS Writes,num_of_bytes_read AS ReadBytes,
                    num_of_bytes_written AS WriteBytes,io_stall_read_ms AS ReadStallMs,io_stall_write_ms AS WriteStallMs
                FROM sys.dm_io_virtual_file_stats(DB_ID(),NULL);
                """, cancellationToken: token))).Select(row => (IDictionary<string, object>)row).ToArray();
            var waits = (await sql.QueryAsync(new CommandDefinition("""
                SELECT wait_type AS WaitType,waiting_tasks_count AS WaitingTasks,wait_time_ms AS WaitTimeMs
                FROM sys.dm_os_wait_stats WHERE wait_type LIKE 'LCK[_]M[_]%';
                """, cancellationToken: token))).Select(row => (IDictionary<string, object>)row).ToArray();
            database = new {io, lockWaits = waits};
        }
        else
        {
            await using var mysql = new MySqlConnection(connection);
            database = (await mysql.QueryAsync(new CommandDefinition("""
                SHOW GLOBAL STATUS WHERE Variable_name IN ('Threads_running','Threads_connected','Connections',
                'Aborted_connects','Max_used_connections','Connection_errors_max_connections','Threads_created','Threads_cached',
                'Innodb_data_read','Innodb_data_written','Innodb_row_lock_waits','Innodb_row_lock_time')
                """, cancellationToken: token))).Select(row => (IDictionary<string, object>)row).ToArray();
            serverConnectionLimit = await mysql.ExecuteScalarAsync<int>(new CommandDefinition("SELECT @@max_connections", cancellationToken: token));
        }
        var info = await redis.GetDatabase().ExecuteAsync("INFO", "stats");
        return new {at = DateTimeOffset.UtcNow, database, serverConnectionLimit, redisStats = info.ToString()};
    }
}
