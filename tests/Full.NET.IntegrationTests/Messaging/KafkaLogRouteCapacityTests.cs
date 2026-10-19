using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>显式容量配置中的两路线持续投递；不进入日常默认测试发现。</summary>
[TestClass]
[DoNotParallelize]
public sealed class KafkaLogRouteCapacityTests
{
    [TestMethod]
    public async Task Low_latency_profile_preserves_non_timing_input_and_service_contracts()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Full.NET.slnx"))) root = root.Parent;
        Assert.IsNotNull(root);
        var baseline = await File.ReadAllTextAsync(Path.Combine(root.FullName, "deploy/observability/fluent-bit-values.yaml"));
        var profile = await File.ReadAllTextAsync(Path.Combine(root.FullName, "deploy/observability/fluent-bit-low-latency-values.yaml"));
        CollectionAssert.AreEquivalent(new[] { "service", "inputs" }, Regex.Matches(profile, @"^  ([A-Za-z]+): \|", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value).ToArray());
        foreach (var block in new[] { "service", "inputs" })
        {
            string Extract(string text) => Regex.Match(text, $@"^  {block}: \|\r?\n([\s\S]*?)(?=^  [A-Za-z]+: \||^extraVolumes:|\z)", RegexOptions.Multiline).Groups[1].Value;
            string Normalized(string text) => string.Join('\n', Extract(text).Split('\n').Select(line => line.Trim())
                .Where(line => line.Length != 0 && !Regex.IsMatch(line, @"^(Flush|Progress_Check_Interval|Progress_Check_Interval_Nsec)\s")));
            Assert.AreEqual(Normalized(baseline), Normalized(profile), "低延迟配置不得悄悄改变路由、存储或解析。");
        }
        StringAssert.Contains(profile, "Flush         0.2");
        StringAssert.Contains(profile, "Progress_Check_Interval 0");
        StringAssert.Contains(profile, "Progress_Check_Interval_Nsec 200000000");
    }

    [TestMethod]
    public async Task Cri_bridge_rejects_invalid_limits_and_stops_at_declared_bound()
    {
        var directory = CapacityOutput("bridge-boundary");
        var source = Path.Combine(directory, "source.jsonl");
        foreach (var invalid in new[] { 0, -1, 60001, int.MaxValue })
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
                LiveCollectorCriBridge.StartAsync(source, directory, CancellationToken.None, invalid));
        Assert.IsFalse(File.Exists(source), "无效预算必须在文件 I/O 前拒绝。");
        var bridge = await LiveCollectorCriBridge.StartAsync(source, directory, CancellationToken.None, 2);
        try
        {
            await File.AppendAllTextAsync(source, "{\"LogEventId\":\"one\"}\n{\"LogEventId\":\"two\"}\n{\"LogEventId\":\"three\"}\n");
            await Assert.ThrowsExactlyAsync<AssertFailedException>(() => bridge.CompleteAsync());
            Assert.AreEqual(2, (await File.ReadAllLinesAsync(Path.Combine(directory, LiveCollectorCriBridge.PodFile("fullnet-collector")))).Length);
        }
        finally
        {
            // 越界故障由受管 pump 传播，释放也必须观察同一故障，不能遗留后台任务。
            await Assert.ThrowsExactlyAsync<AssertFailedException>(() => bridge.DisposeAsync().AsTask());
        }
    }

    [TestMethod]
    public async Task Sustained_routes_reconcile_every_record_within_declared_capacity_budgets()
    {
        Assert.IsTrue(SharedDatabaseFixture.BoundedContainers && !SharedDatabaseFixture.ReuseContainers,
            "容量验收必须由独享有界资源控制器启动。");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Full.NET.slnx"))) root = root.Parent;
        Assert.IsNotNull(root);
        var selectedRate = Environment.GetEnvironmentVariable("FULLNET_LOG_CAPACITY_ONLY_RATE");
        var selectedRoute = Environment.GetEnvironmentVariable("FULLNET_LOG_CAPACITY_ONLY_ROUTE");
        if (selectedRate is not (null or "500" or "1000"))
            throw new ArgumentException("容量选择只允许 500/1000 或不指定。");
        if (selectedRoute is not (null or "Collector" or "ApplicationKafka"))
            throw new ArgumentException("路线选择只允许 Collector/ApplicationKafka 或不指定。");
        var rates = new[] { 500, 1000 }.Where(rate => selectedRate is null || rate.ToString(System.Globalization.CultureInfo.InvariantCulture) == selectedRate).ToArray();
        var directory = CapacityOutput("route-capacity");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(45));
        var reports = new List<JsonElement>();
        var budgetFailures = new List<object>();
        foreach (var rate in rates)
        foreach (var (route, round, position) in new[] { ("Collector", 1, 1), ("ApplicationKafka", 1, 2),
                     ("ApplicationKafka", 2, 1), ("Collector", 2, 2) })
        {
            if (selectedRoute is not null && selectedRoute != route) continue;
            var leaf = Path.Combine(directory, $"{rate}-{round}-{position}-{route}");
            Directory.CreateDirectory(leaf);
            var report = JsonSerializer.SerializeToElement(await KafkaLogRouteComparisonTests.RunCaseAsync(root.FullName, leaf, route, round, position, deadline.Token, rate));
            reports.Add(report);
            await File.WriteAllTextAsync(Path.Combine(directory, "cases.json"), JsonSerializer.Serialize(reports), deadline.Token);
            var metrics = report.GetProperty("measurement").GetProperty("requestReport");
            var failed = new List<string>();
            if (metrics.GetProperty("observedRequestsPerSecond").GetDouble() < rate * 0.95) failed.Add("throughput");
            if (metrics.GetProperty("statistics").GetProperty("P99Milliseconds").GetDouble() >= 100) failed.Add("http_p99");
            if (metrics.GetProperty("plannedCompletionStatistics").GetProperty("P99Milliseconds").GetDouble() >= 250) failed.Add("planned_p99");
            if (metrics.GetProperty("sharedProcessCpuMilliseconds").GetDouble()
                / metrics.GetProperty("elapsedMilliseconds").GetDouble() / 2 * 100 > 95) failed.Add("cpu");
            if (metrics.GetProperty("processLifetimePeakWorkingSetBytes").GetInt64() >= 1L << 30) failed.Add("memory");
            var proof = report.GetProperty("proof");
            if (proof.GetProperty("endWindowCommittedMeasuredRecords").GetInt32() < rate * 30 * 0.95
                || proof.GetProperty("endWindowSampleAgeMilliseconds").GetDouble() > 1500) failed.Add("consumer_window_progress");
            if (proof.GetProperty("offsetCatchupWaitMilliseconds").GetDouble() >= 5000) failed.Add("consumer_tail_wait");
            // 单项延迟超预算不能隐藏，也不阻止采集其他独立样本；正确性/资源故障仍由原断言停止。
            if (failed.Count != 0) budgetFailures.Add(new { route, round, rate, failed });
            Assert.IsTrue(metrics.GetProperty("observedServerActiveRequestsPeak").GetInt32() > 0);
            // 已完成真实 ES/Offset 核验后压缩保留逐条原始收据，回收当前场景可再生成的大日志。
            foreach (var name in route == "Collector" ? new[] { "broker.jsonl", "Projected-1.jsonl" } : new[] { "broker.jsonl" })
            {
                var compressed = Path.Combine(directory, $"{rate}-{round}-{position}-{name}.gz");
                await using (var input = File.OpenRead(Path.Combine(leaf, name)))
                await using (var output = File.Create(compressed))
                await using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
                    await input.CopyToAsync(gzip, deadline.Token);
                await using var bytes = File.OpenRead(compressed);
                await File.WriteAllTextAsync(compressed + ".sha256",
                    Convert.ToHexStringLower(await SHA256.HashDataAsync(bytes, deadline.Token)), deadline.Token);
            }
            Directory.Delete(leaf, recursive: true);
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new
        {
            passed = budgetFailures.Count == 0, budgetFailures, cleanupCompleted = true, reports, selectedRates = rates, selectedRoute, durationSecondsPerCase = 30,
            scope = "Bounded loopback Kestrel and TLS Kafka/ES, synthetic CRI, selected finite 500/1000 requests/s cases in reverse order; "
                + "every measured event, trusted collector source, ES document, offset and zero DLQ reconciled; no business API or 10K/Soak claim",
        }), deadline.Token);
        Assert.AreEqual(0, budgetFailures.Count, "存在容量预算失败，查看完整 result.json；不得以部分通过替代整批。");
    }

    internal static string CapacityOutput(string name)
    {
        var configured = Environment.GetEnvironmentVariable("FULLNET_LOG_CAPACITY_OUTPUT");
        Assert.IsFalse(string.IsNullOrWhiteSpace(configured), "缺少独享容量输出目录。");
        var root = Path.GetFullPath(configured!);
        var temporary = Path.TrimEndingDirectorySeparator(Path.GetTempPath()) + Path.DirectorySeparatorChar;
        Assert.IsTrue(root.StartsWith(temporary, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        var directory = Path.Combine(root, name);
        Assert.IsFalse(Directory.Exists(directory) || File.Exists(directory), "不得覆盖旧容量结果。");
        Directory.CreateDirectory(directory);
        return directory;
    }

}
