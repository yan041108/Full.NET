using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using StackExchange.Redis;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>同一原生发布物的双节点冷启动与真实 Grid 读取观测；单次样本不作为吞吐、P99 或容量结论。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiCacheColdStartE2ETests
{
    private const string GridPath = "/api/v1/me/grid-preferences/identity.users";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Cold_process_and_first_Grid_reads_record_latency_with_actual_L2_and_peer_invalidation(DatabaseProvider provider)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var token = deadline.Token;
        // 迁移独立于进程启动计时，避免把初始化隔离数据库的成本当作 API 冷启动成本。
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connection, token);
        var settings = new Dictionary<string, string?>
        {
            ["Cache__RedisConnectionString"] = await SharedDatabaseFixture.GetRedisConnectionStringAsync(),
            ["Cache__ApplicationName"] = "native-cold-" + Guid.NewGuid().ToString("N"),
            ["Identity__SessionLoginPolicy"] = "AllowMultiple",
            [$"Cache__Entries__{CacheEntryNames.GridPreference}__OwnerModule"] = "settings",
            [$"Cache__Entries__{CacheEntryNames.GridPreference}__ConsistencyClass"] = "S2",
            [$"Cache__Entries__{CacheEntryNames.GridPreference}__L1Duration"] = "00:05:00",
            [$"Cache__Entries__{CacheEntryNames.GridPreference}__L2Duration"] = "00:10:00",
            [$"Cache__Entries__{CacheEntryNames.GridPreference}__Jitter"] = "00:00:00",
        };
        var timer = Stopwatch.StartNew();
        await using var primary = await NativeApiProcessHost.StartAsync(artifact, provider, connection,
            settings, NativeAotTestTimeouts.ProcessStartup, token);
        var primaryToLiveMs = timer.Elapsed.TotalMilliseconds;
        using var writer = primary.CreateClient();
        writer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await NativeApiE2EAssertions.LoginAsync(writer, primary.LogFilePath));
        timer.Restart();
        var initial = await ReadAsync(writer, primary);
        var firstGridRequestMs = timer.Elapsed.TotalMilliseconds;
        var saved = await WriteAsync(writer, primary, initial, 160);
        timer.Restart();
        Assert.AreEqual(160, Width(await ReadAsync(writer, primary)));
        var primedGridRequestMs = timer.Elapsed.TotalMilliseconds;
        var actor = await writer.GetFromJsonAsync<CurrentUserResponse>("/api/v1/me", token);
        Assert.IsNotNull(actor);
        var configuration = ConfigurationOptions.Parse(settings["Cache__RedisConnectionString"]!);
        configuration.AsyncTimeout = 1000;
        configuration.ConnectTimeout = 1000;
        configuration.ConnectRetry = 0;
        using var redis = await ConnectionMultiplexer.ConnectAsync(configuration);
        token.ThrowIfCancellationRequested();
        var logical = CacheKeyBuilder.ForGlobal("Testing", "settings", "grid-preference",
            $"{actor.Id:N}:identity.users", $"v{saved.SchemaVersion}");
        var pattern = $"fullnet:testing:{settings["Cache__ApplicationName"]}:v2:*{logical}*";
        var expected = JsonSerializer.SerializeToUtf8Bytes(saved, new JsonSerializerOptions(JsonSerializerDefaults.General));
        using (var fill = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            fill.CancelAfter(TimeSpan.FromSeconds(15));
            while (true)
            {
                var keys = new List<RedisKey>();
                await foreach (var key in redis.GetServer(redis.GetEndPoints().Single()).KeysAsync(pattern: pattern)
                    .WithCancellation(fill.Token)) keys.Add(key);
                Assert.IsTrue(keys.Count <= 1, "冷启动实验只允许访问自己的唯一物理缓存键。");
                if (keys.Count == 1)
                {
                    var bytes = (byte[]?)await redis.GetDatabase().HashGetAsync(keys[0], "data").WaitAsync(fill.Token);
                    if (bytes is not null && bytes.AsSpan().IndexOf(expected) >= 0) break;
                }
                await Task.Delay(50, fill.Token);
            }
        }
        await using (DbConnection database = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(connection, MySqlGuidStorageMode.Binary16, allowUserVariables: false)))
        {
            var affected = await database.ExecuteAsync(new CommandDefinition("""
                UPDATE fn_settings_user_grid_preference SET ColumnsJson = @Columns
                WHERE UserId = @UserId AND GridKey = @GridKey
                """, new
                {
                    UserId = actor.Id, GridKey = "identity.users",
                    Columns = JsonSerializer.Serialize(new[] { new GridColumnPreference("employeeNumber", 0, 192, true, null) },
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                }, cancellationToken: token));
            Assert.AreEqual(1, affected);
        }
        timer.Restart();
        await using var secondary = await NativeApiProcessHost.StartAsync(artifact, provider, connection,
            settings, NativeAotTestTimeouts.ProcessStartup, token);
        var secondaryToLiveMs = timer.Elapsed.TotalMilliseconds;
        using var reader = secondary.CreateClient();
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await NativeApiE2EAssertions.LoginAsync(reader, secondary.LogFilePath));
        timer.Restart();
        Assert.AreEqual(160, Width(await ReadAsync(reader, secondary)),
            "全新进程首次 Grid 读取须来自真实 L2；权威库已提交为192。");
        var coldPeerL2RequestMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        var updated = await WriteAsync(writer, primary, saved, 224);
        using (var propagation = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            propagation.CancelAfter(TimeSpan.FromSeconds(10));
            while (true)
            {
                var value = await ReadAsync(reader, secondary, propagation.Token);
                if (Width(value) == 224 && value.Version == updated.Version) break;
                await Task.Delay(50, propagation.Token);
            }
        }
        var writeRequestToPeerObservationMs = timer.Elapsed.TotalMilliseconds;
        TestContext.WriteLine("{0}", JsonSerializer.Serialize(new
        {
            provider = provider.ToString(), primaryToLiveMs, secondaryToLiveMs, firstGridRequestMs,
            primedGridRequestMs, coldPeerL2RequestMs, writeRequestToPeerObservationMs,
            includesLoginInGridTiming = false, includesMigrationInStartupTiming = false,
            startupProbe = "health/live", samplesPerStage = 1, capacityVerified = false,
        }));
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await secondary.StopGracefullyAsync(shutdown.Token);
        await primary.StopGracefullyAsync(shutdown.Token);
        primary.AssertNoFatalMarkersInLogs();
        secondary.AssertNoFatalMarkersInLogs();

        Task<GridPreferenceResponse> ReadAsync(HttpClient client, NativeApiProcessHost host, CancellationToken cancellationToken = default) =>
            ReadCoreAsync(client, host, cancellationToken == default ? token : cancellationToken);
        async Task<GridPreferenceResponse> ReadCoreAsync(HttpClient client, NativeApiProcessHost host, CancellationToken cancellationToken)
        {
            using var response = await client.GetAsync(GridPath, cancellationToken);
            return await RequireAsync(response, host, cancellationToken);
        }
        async Task<GridPreferenceResponse> WriteAsync(HttpClient client, NativeApiProcessHost host, GridPreferenceResponse current, int width)
        {
            using var response = await client.PutAsJsonAsync(GridPath, new UpdateGridPreferenceRequest(current.SchemaVersion,
                [new GridColumnPreference("employeeNumber", 0, width, true, null)], current.Version), token);
            return await RequireAsync(response, host, token);
        }
        static async Task<GridPreferenceResponse> RequireAsync(HttpResponseMessage response, NativeApiProcessHost host, CancellationToken cancellationToken)
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken) + host.LogFilePath);
            var result = await response.Content.ReadFromJsonAsync<GridPreferenceResponse>(cancellationToken);
            Assert.IsNotNull(result); return result;
        }
        static int? Width(GridPreferenceResponse response) => response.Columns.Single(column => column.ColumnKey == "employeeNumber").Width;
    }
}
