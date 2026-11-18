using System.Diagnostics;
using System.Text.Json;
using Full.NET.IntegrationTests.Api;
using StackExchange.Redis;

namespace Full.NET.IntegrationTests.Caching;

/// <summary>两个真实进程经正式 Redis 注册验证双向失效和丢通知收敛，不外推容量或独立节点。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CacheBackplaneProcessTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Independent_processes_invalidate_both_directions_and_converge_after_a_lost_notification()
    {
        var redis = await SharedDatabaseFixture.GetRedisConnectionStringAsync();
        var run = Guid.NewGuid().ToString("N");
        using var connection = await ConnectionMultiplexer.ConnectAsync(redis);
        var database = connection.GetDatabase();
        var sentinel = $"fullnet:test:backplane-preserve:{run}";
        await database.StringSetAsync(sentinel, "unchanged");
        try
        {
            await using var first = ProbeProcess.Start(run, "a", redis);
            await using var second = ProbeProcess.Start(run, "b", redis);
            Assert.AreNotEqual(first.Id, second.Id);
            var results = await Task.WhenAll(first.CompleteAsync(), second.CompleteAsync());
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < results.Length; index++)
            {
                var result = results[index];
                Assert.AreEqual(0, result.ExitCode, "缓存探针必须实际完成，不能跳过或依赖 TTL 冒充通知。");
                Assert.AreEqual("", result.Error);
                using var json = JsonDocument.Parse(result.Output);
                var record = json.RootElement;
                Assert.AreEqual(index == 0 ? "a" : "b", record.GetProperty("role").GetString());
                Assert.AreEqual(index == 0 ? first.Id : second.Id, record.GetProperty("processId").GetInt32());
                Assert.AreEqual(run, record.GetProperty("run").GetString());
                Assert.IsTrue(identities.Add(record.GetProperty("instance").GetString()!));
                Assert.AreEqual(120, record.GetProperty("l1TtlSeconds").GetInt32());
                Assert.IsTrue(record.GetProperty("l2Removal").GetBoolean());
                Assert.IsTrue(record.GetProperty("authoritativeRefill").GetBoolean());
                Assert.AreEqual("Healthy", record.GetProperty("health").GetString());
                var samples = record.GetProperty("receivedInvalidationMilliseconds");
                Assert.AreEqual(1, samples.GetArrayLength());
                Assert.IsTrue(samples[0].GetDouble() is >= 0 and < 5000);
                var missed = record.GetProperty("missedNotificationConvergenceMilliseconds");
                if (index == 0) Assert.AreEqual(JsonValueKind.Null, missed.ValueKind);
                else Assert.IsTrue(missed.GetDouble() is > 0 and < 4000);
                TestContext.WriteLine("本轮独立进程缓存回执：" + result.Output.Trim());
            }
            await using var cleanup = ProbeProcess.Start(run, "cleanup", redis);
            Assert.AreEqual(0, (await cleanup.CompleteAsync()).ExitCode);
            Assert.AreEqual("unchanged", (string?)await database.StringGetAsync(sentinel));
        }
        finally
        {
            await database.KeyDeleteAsync(sentinel).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [TestMethod]
    public async Task Startup_configuration_failure_uses_fixed_redacted_diagnostics()
    {
        await using var process = ProbeProcess.Start(Guid.NewGuid().ToString("N"), "a",
            "127.0.0.1:1,password=credential-sentinel", invalidDuration: "duration-sentinel");
        var result = await process.CompleteAsync();
        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual("", result.Output);
        Assert.IsTrue(result.Error.StartsWith("缓存双实例验证失败：role=a; type=", StringComparison.Ordinal));
        Assert.IsFalse(result.Error.Contains("credential-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(result.Error.Contains("duration-sentinel", StringComparison.Ordinal));
        Assert.IsFalse(result.Error.Contains("127.0.0.1", StringComparison.Ordinal));
    }

    private sealed class ProbeProcess : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly Task<string> _output;
        private readonly Task<string> _error;

        private ProbeProcess(Process process)
        {
            _process = process;
            _output = process.StandardOutput.ReadToEndAsync();
            _error = process.StandardError.ReadToEndAsync();
        }

        internal int Id => _process.Id;

        internal static ProbeProcess Start(string run, string role, string redis, string? invalidDuration = null)
        {
            var current = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            var executable = Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "tests", "Tools", "Full.NET.CacheBackplaneProbe",
                "bin", current.Parent!.Name, current.Name, "Full.NET.CacheBackplaneProbe.dll");
            Assert.IsTrue(File.Exists(executable), "探针必须随本轮 Integration 构建，不使用旧缓存产物。");
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.ArgumentList.Add(executable);
            start.ArgumentList.Add(run);
            start.ArgumentList.Add(role);
            start.Environment["Cache__RedisConnectionString"] = redis;
            start.Environment["Cache__ApplicationName"] = "process-probe-" + run;
            start.Environment["NODE_NAME"] = "fixture-" + role;
            if (invalidDuration is not null) start.Environment["Cache__DefaultDuration"] = invalidDuration;
            return new ProbeProcess(Process.Start(start)!);
        }

        internal async Task<(int ExitCode, string Output, string Error)> CompleteAsync()
        {
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(80));
            return (_process.ExitCode, await _output, await _error);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                await Task.WhenAll(_output, _error);
            }
            finally { _process.Dispose(); }
        }
    }
}
