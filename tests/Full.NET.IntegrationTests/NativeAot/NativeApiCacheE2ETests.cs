using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Encodings.Web;
using Dapper;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Settings.Contracts;
using Full.NET.Modules.Settings.Serialization;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using StackExchange.Redis;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>真实 Native 进程与 Redis 往返：冷节点 L2 反序列化及提交后跨进程 L1 失效。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeApiCacheE2ETests
{
    private const string GridPath = "/api/v1/me/grid-preferences/identity.users";

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Native_cold_node_reads_Redis_and_backplane_invalidates_remote_L1(DatabaseProvider provider)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connection);
        var settings = new Dictionary<string, string?>
        {
            ["Cache__RedisConnectionString"] = await SharedDatabaseFixture.GetRedisConnectionStringAsync(),
            ["Cache__ApplicationName"] = $"native-cache-{Guid.NewGuid():N}",
            // 两个独立节点各自签名和登录，避免同客户端会话撤销干扰缓存实验。
            ["Identity__SessionLoginPolicy"] = "AllowMultiple",
            [$"Cache__Entries__{CacheEntryNames.GridPreference}__OwnerModule"] = "settings",
            [$"Cache__Entries__{CacheEntryNames.GridPreference}__ConsistencyClass"] = "S2",
            // 实验窗口远小于 TTL，不能让时间收敛伪装成 Backplane 成功。
            [$"Cache__Entries__{CacheEntryNames.GridPreference}__L1Duration"] = "00:05:00",
            [$"Cache__Entries__{CacheEntryNames.GridPreference}__L2Duration"] = "00:10:00",
            [$"Cache__Entries__{CacheEntryNames.GridPreference}__Jitter"] = "00:00:00",
        };
        await using var primary = await NativeApiProcessHost.StartAsync(artifact, provider, connection,
            settings, NativeAotTestTimeouts.ProcessStartup);
        TestContext.WriteLine($"Native primary log: {primary.LogFilePath}");
        using var writer = primary.CreateClient();
        writer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await NativeApiE2EAssertions.LoginAsync(writer, primary.LogFilePath));
        var initial = await ReadAsync(writer, primary);
        var saved = await WriteAsync(writer, primary, initial, 160);
        Assert.AreEqual(160, Width(await ReadAsync(writer, primary)));

        using var me = await writer.GetAsync("/api/v1/me");
        var meBody = await me.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, me.StatusCode, meBody + primary.LogFilePath);
        var actor = await me.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.IsNotNull(actor);
        // 只在隔离测试库制造未通知的权威值差异；若冷节点回源会读到 192，不能冒充 Redis 命中。
        await using (DbConnection database = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(connection)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(connection,
                MySqlGuidStorageMode.Binary16, allowUserVariables: false)))
        {
            var rows = await database.ExecuteAsync("""
                UPDATE fn_settings_user_grid_preference SET ColumnsJson = @Columns
                WHERE UserId = @UserId AND GridKey = @GridKey
                """, new
                {
                    UserId = actor.Id,
                    GridKey = "identity.users",
                    Columns = JsonSerializer.Serialize(new[] { new GridColumnPreference("employeeNumber", 0, 192, true, null) },
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                });
            Assert.AreEqual(1, rows);
        }

        await using var secondary = await NativeApiProcessHost.StartAsync(artifact, provider, connection,
            settings, NativeAotTestTimeouts.ProcessStartup);
        TestContext.WriteLine($"Native secondary log: {secondary.LogFilePath}");
        using var reader = secondary.CreateClient();
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await NativeApiE2EAssertions.LoginAsync(reader, secondary.LogFilePath));
        using var experiment = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        Assert.AreEqual(160, Width(await ReadAsync(reader, secondary, experiment.Token)),
            "全新进程没有 L1；192 是权威值，只有实际 L2 反序列化才能返回 160。" + secondary.LogFilePath);

        // 删除仅本实验的物理 L2 键且不广播；再次返回 160 才证明 B 已建立 L1。
        await RemoveOnlyL2Async(settings, actor.Id, saved.SchemaVersion, experiment.Token);
        Assert.AreEqual(160, Width(await ReadAsync(reader, secondary, experiment.Token)),
            "L2 已不存在且数据库为 192；此处必须实际命中 B 的 L1。" + secondary.LogFilePath);

        var updated = await WriteAsync(writer, primary, saved, 224, experiment.Token);
        Assert.AreEqual(saved.Version + 1, updated.Version);
        using var propagation = CancellationTokenSource.CreateLinkedTokenSource(experiment.Token);
        propagation.CancelAfter(TimeSpan.FromSeconds(10));
        var elapsed = Stopwatch.StartNew();
        GridPreferenceResponse observed;
        do
        {
            observed = await ReadAsync(reader, secondary, propagation.Token);
            propagation.Token.ThrowIfCancellationRequested();
            if (Width(observed) == 224) break;
            await Task.Delay(100, propagation.Token);
        } while (elapsed.Elapsed < TimeSpan.FromSeconds(10));
        Assert.AreEqual(224, Width(observed), "远端 L1 须在五分钟 TTL 前由 Backplane 清理。" + secondary.LogFilePath);
        Assert.IsLessThan(TimeSpan.FromSeconds(10), elapsed.Elapsed);
        Assert.AreEqual(updated.Version, observed.Version);
        Assert.AreEqual(224, Width(await ReadAsync(writer, primary, experiment.Token)));
        await VerifyNativeBudgetBoundariesAsync(artifact, provider, connection, settings, actor.Id, updated);
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await secondary.StopGracefullyAsync(shutdown.Token);
        await primary.StopGracefullyAsync(shutdown.Token);
        primary.AssertNoFatalMarkersInLogs();
        secondary.AssertNoFatalMarkersInLogs();
    }

    private static int? Width(GridPreferenceResponse response) =>
        response.Columns.Single(column => column.ColumnKey == "employeeNumber").Width;

    /// <summary>在同一发布物上验证精确边界及少一字节预算；新进程防止已有 L1 掩盖实际 L2 解析。</summary>
    private static async Task VerifyNativeBudgetBoundariesAsync(NativeApiArtifact artifact, DatabaseProvider provider,
        string connection, IReadOnlyDictionary<string, string?> settings, Guid userId, GridPreferenceResponse cached)
    {
        const int inputMaximum = 1024 * 1024;
        using var window = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = window.Token;
        var configuration = ConfigurationOptions.Parse(settings["Cache__RedisConnectionString"]!);
        configuration.AsyncTimeout = 1_000;
        configuration.ConnectTimeout = 1_000;
        configuration.ConnectRetry = 0;
        using var redis = await ConnectionMultiplexer.ConnectAsync(configuration);
        token.ThrowIfCancellationRequested();
        var logical = CacheKeyBuilder.ForGlobal("Testing", "settings", "grid-preference",
            $"{userId:N}:identity.users", $"v{cached.SchemaVersion}");
        var prefix = $"fullnet:testing:{settings["Cache__ApplicationName"]}:v2:";
        var database = redis.GetDatabase();
        var expectedPayload = JsonSerializer.SerializeToUtf8Bytes(cached,
            new JsonSerializerOptions(JsonSerializerDefaults.General));
        var keys = new List<RedisKey>();
        HashEntry[] original;
        byte[]? originalData;
        // 等待实际后台写入完成，不用已有 L1 的成功读取推断 L2 已经写好。
        while (true)
        {
            keys.Clear();
            await foreach (var key in redis.GetServer(redis.GetEndPoints().Single())
                .KeysAsync(pattern: $"{prefix}*{logical}*").WithCancellation(token)) keys.Add(key);
            Assert.IsTrue(keys.Count <= 1, "预算实验不能修改多个物理条目。");
            if (keys.Count == 1)
            {
                Assert.AreEqual(RedisType.Hash, await database.KeyTypeAsync(keys[0]).WaitAsync(token));
                original = await database.HashGetAllAsync(keys[0]).WaitAsync(token);
                originalData = (byte[]?)original.SingleOrDefault(entry => entry.Name == "data").Value;
                if (originalData is not null && originalData.AsSpan().IndexOf(expectedPayload) >= 0) break;
            }
            await Task.Delay(50, token);
        }
        Assert.IsNotNull(originalData);
        var envelope = JsonNode.Parse(originalData)!.AsObject();
        // 未知包装属性沿用 JSON 的忽略语义；多字节内容确保测试的边界按 UTF-8 字节计算。
        envelope["budgetProbe"] = new string('中', 64) + "😀";
        var encoded = JsonSerializer.SerializeToUtf8Bytes(envelope,
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.IsLessThan(inputMaximum, encoded.Length);
        byte[] Padded(int count)
        {
            var bytes = new byte[count];
            Array.Fill(bytes, (byte)' ');
            encoded.CopyTo(bytes, 0);
            return bytes;
        }
        async Task RestoreAsync(byte[] bytes)
        {
            await database.HashSetAsync(keys[0], original.Select(entry => entry.Name == "data"
                ? new HashEntry(entry.Name, bytes) : entry).ToArray()).WaitAsync(token);
            Assert.IsTrue(await database.KeyExpireAsync(keys[0], TimeSpan.FromMinutes(2)).WaitAsync(token));
        }

        var businessMaximum = JsonSerializer.SerializeToUtf8Bytes(cached,
            SettingsJsonSerializerContext.Default.GridPreferenceResponse).Length;
        Assert.IsGreaterThan(1, businessMaximum);
        var exactSettings = new Dictionary<string, string?>(settings)
        {
            ["Cache__MaxDeserializationBytes"] = inputMaximum.ToString(CultureInfo.InvariantCulture),
            [$"Cache__Entries__{CacheEntryNames.GridPreference}__MaxSerializedBytes"] = businessMaximum.ToString(CultureInfo.InvariantCulture),
        };
        await SetAuthorityWidthAsync(provider, connection, userId, 256, token);
        await RestoreAsync(Padded(inputMaximum));
        await using (var exact = await NativeApiProcessHost.StartAsync(artifact, provider, connection,
            exactSettings, NativeAotTestTimeouts.ProcessStartup))
        {
            using var client = exact.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                await NativeApiE2EAssertions.LoginAsync(client, exact.LogFilePath));
            Assert.AreEqual(224, Width(await ReadAsync(client, exact, token)),
                "权威值已是256；返回224才证明 Native 接受解析输入与业务载荷的精确上限。");
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await exact.StopGracefullyAsync(shutdown.Token);
            exact.AssertNoFatalMarkersInLogs();
        }

        var strictSettings = new Dictionary<string, string?>(exactSettings)
        {
            [$"Cache__Entries__{CacheEntryNames.GridPreference}__MaxSerializedBytes"] = (businessMaximum - 1).ToString(CultureInfo.InvariantCulture),
        };
        await using var strict = await NativeApiProcessHost.StartAsync(artifact, provider, connection,
            strictSettings, NativeAotTestTimeouts.ProcessStartup);
        using var reader = strict.CreateClient();
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            await NativeApiE2EAssertions.LoginAsync(reader, strict.LogFilePath));
        Assert.AreEqual(256, Width(await ReadAsync(reader, strict, token)), "超出业务预算的旧值不可返回。");
        Assert.IsFalse(await database.KeyExistsAsync(keys[0]).WaitAsync(token));
        await SetAuthorityWidthAsync(provider, connection, userId, 288, token);
        Assert.AreEqual(288, Width(await ReadAsync(reader, strict, token)), "超限回源结果不可重新填充 L1。");
        Assert.IsFalse(await database.KeyExistsAsync(keys[0]).WaitAsync(token), "超限回源结果不可重新填充 L2。");

        var oversized = Padded(inputMaximum + 1);
        await RestoreAsync(oversized);
        await SetAuthorityWidthAsync(provider, connection, userId, 320, token);
        Assert.AreEqual(320, Width(await ReadAsync(reader, strict, token)));
        await SetAuthorityWidthAsync(provider, connection, userId, 352, token);
        Assert.AreEqual(352, Width(await ReadAsync(reader, strict, token)), "原始输入拒绝后不能缓存超业务预算的回源值。");
        var remaining = await database.HashGetAsync(keys[0], "data").WaitAsync(token);
        if (!remaining.IsNull)
        {
            var remainingData = (byte[]?)remaining;
            Assert.IsNotNull(remainingData);
            CollectionAssert.AreEqual(oversized, remainingData,
                "残留原始拒绝条目可以重试或删除，但不能被超业务预算的新值覆盖。");
        }
        using var finalShutdown = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await strict.StopGracefullyAsync(finalShutdown.Token);
        await strict.DisposeAsync();
        var controlledRejection = false;
        foreach (var line in await File.ReadAllLinesAsync(strict.LogFilePath, finalShutdown.Token))
        {
            if (!line.StartsWith('{')) continue;
            using var logged = JsonDocument.Parse(line);
            if (logged.RootElement.TryGetProperty("ExceptionType", out var exceptionType)
                && exceptionType.GetString() == "CacheDeserializationLimitException") controlledRejection = true;
        }
        // 统一日志只输出审定的异常类型；不能要求恢复异常消息或堆栈来证明解析前字节保护。
        Assert.IsTrue(controlledRejection,
            "必须观察到受控的 CacheDeserializationLimitException 类型，不能把任意 JSON 解析失败当作解析前保护。");
        strict.AssertNoFatalMarkersInLogs();
    }

    private static async Task SetAuthorityWidthAsync(DatabaseProvider provider, string connection, Guid userId,
        int width, CancellationToken token)
    {
        await using DbConnection database = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(connection)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(connection,
                MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        var affected = await database.ExecuteAsync(new CommandDefinition("""
            UPDATE fn_settings_user_grid_preference SET ColumnsJson = @Columns
            WHERE UserId = @UserId AND GridKey = @GridKey
            """, new
            {
                UserId = userId, GridKey = "identity.users",
                Columns = JsonSerializer.Serialize(new[] { new GridColumnPreference("employeeNumber", 0, width, true, null) },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            }, cancellationToken: token));
        Assert.AreEqual(1, affected);
    }

    private static async Task<GridPreferenceResponse> ReadAsync(HttpClient client, NativeApiProcessHost host,
        CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(GridPath, cancellationToken);
        return await RequireResponseAsync(response, host, cancellationToken);
    }

    private static async Task<GridPreferenceResponse> WriteAsync(HttpClient client, NativeApiProcessHost host,
        GridPreferenceResponse current, int width, CancellationToken cancellationToken = default)
    {
        using var response = await client.PutAsJsonAsync(GridPath, new UpdateGridPreferenceRequest(current.SchemaVersion,
            [new GridColumnPreference("employeeNumber", 0, width, true, null)], current.Version), cancellationToken);
        return await RequireResponseAsync(response, host, cancellationToken);
    }

    private static async Task<GridPreferenceResponse> RequireResponseAsync(HttpResponseMessage response, NativeApiProcessHost host,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body + "\nNative log: " + host.LogFilePath);
        var result = await response.Content.ReadFromJsonAsync<GridPreferenceResponse>(cancellationToken);
        Assert.IsNotNull(result);
        return result;
    }

    private static async Task RemoveOnlyL2Async(IReadOnlyDictionary<string, string?> settings, Guid userId,
        int schemaVersion, CancellationToken cancellationToken)
    {
        var configuration = ConfigurationOptions.Parse(settings["Cache__RedisConnectionString"]!);
        configuration.AsyncTimeout = 1_000;
        configuration.ConnectTimeout = 1_000;
        configuration.ConnectRetry = 0;
        // 连接创建本身有界；取得所有权后再检查窗口取消，避免 WaitAsync 留下迟到的未释放连接。
        using var redis = await ConnectionMultiplexer.ConnectAsync(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        var logical = CacheKeyBuilder.ForGlobal("Testing", "settings", "grid-preference",
            $"{userId:N}:identity.users", $"v{schemaVersion}");
        var physical = new List<RedisKey>();
        var prefix = $"fullnet:testing:{settings["Cache__ApplicationName"]}:v2:";
        await foreach (var key in redis.GetServer(redis.GetEndPoints().Single())
            .KeysAsync(pattern: $"{prefix}*{logical}*").WithCancellation(cancellationToken))
            physical.Add(key);
        cancellationToken.ThrowIfCancellationRequested();
        Assert.HasCount(1, physical, "只允许删除该隔离应用中当前用户/网格的唯一 L2 条目。");
        Assert.IsTrue(await redis.GetDatabase().KeyDeleteAsync(physical[0]).WaitAsync(cancellationToken));
    }
}
