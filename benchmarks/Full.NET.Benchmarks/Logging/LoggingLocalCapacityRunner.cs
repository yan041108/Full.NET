using System.Runtime.InteropServices;
using System.Text.Json;

namespace Full.NET.Benchmarks.Logging;

/// <summary>Local txt 的有界持续请求矩阵；不替代双库审计、Kafka 或全业务容量。</summary>
public static class LoggingLocalCapacityRunner
{
    /// <summary>只接受新建的系统临时子目录，按实际负载预算决定是否继续升压。</summary>
    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 2 || args[0] != "--output")
            throw new ArgumentException("用法：logging-local-capacity --output <新的系统临时目录>");
        var output = Path.GetFullPath(args[1]);
        var temporary = Path.TrimEndingDirectorySeparator(Path.GetTempPath()) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(temporary, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || Directory.Exists(output) || File.Exists(output))
            throw new ArgumentException("输出必须是尚不存在的系统临时子目录。");
        Directory.CreateDirectory(output);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var cases = new List<JsonElement>();
        foreach (var rate in new[] { 500, 1000 })
        foreach (var (mode, round) in new[] { ("Disabled", 1), ("Summary", 1), ("Projected", 1),
                     ("Projected", 2), ("Summary", 2), ("Disabled", 2) })
        {
            var directory = Path.Combine(output, $"{rate}-{mode}-{round}");
            Directory.CreateDirectory(directory);
            var report = JsonSerializer.SerializeToElement(
                await LoggingRequestLatencyRunner.RunLocalFileCapacityCaseAsync(directory, mode, round, rate, deadline.Token));
            cases.Add(report);
            // 部分结果先保存；失败和未执行档位绝不被最终通过文件覆盖。
            await File.WriteAllTextAsync(Path.Combine(output, "cases.json"),
                JsonSerializer.Serialize(cases, new JsonSerializerOptions { WriteIndented = true }), deadline.Token);
            var statistics = report.GetProperty("statistics");
            var scheduled = report.GetProperty("plannedCompletionStatistics");
            var cpuBudgetPercent = report.GetProperty("sharedProcessCpuMilliseconds").GetDouble()
                / report.GetProperty("elapsedMilliseconds").GetDouble() / 2 * 100;
            if (report.GetProperty("observedRequestsPerSecond").GetDouble() < rate * 0.95
                || statistics.GetProperty("P99Milliseconds").GetDouble() >= 100
                || scheduled.GetProperty("P99Milliseconds").GetDouble() >= 250
                || cpuBudgetPercent > 95
                || report.GetProperty("processLifetimePeakWorkingSetBytes").GetInt64() >= 1_073_741_824)
                throw new InvalidOperationException($"Local 容量样本超出预设预算：{rate}/{mode}/{round}。");
            Console.WriteLine($"Local capacity {rate}/s {mode} round={round} reconciled");
            // 自有场景目录已逐条对账，保留有界原始时序及文件摘要，回收可重新生成的日志。
            Directory.Delete(directory, recursive: true);
        }
        await File.WriteAllTextAsync(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            passed = true, completedAtUtc = DateTimeOffset.UtcNow,
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            processorCount = Environment.ProcessorCount, concurrency = 8, warmup = 200,
            durationSecondsPerCase = 30, targetRates = new[] { 500, 1000 }, cases,
            budgets = new { minimumRateRatio = 0.95, httpP99Milliseconds = 100, plannedCompletionP99Milliseconds = 250,
                twoCoreCpuPercent = 95, processLifetimePeakWorkingSetBytes = 1_073_741_824 },
            scope = "Loopback Kestrel, real B2 Summary/Projected and Local txt output; shared client/server process, CPU normalized to two cores; "
                + "HTTP and planned-send-to-completion latency reported separately; observed server active request peak is not VU count; "
                + "full receipt reconciliation includes warmup and completed shutdown, no fsync acknowledgement; "
                + "no authenticated business API, B1 database, Kafka/Collector/ES, maximum capacity or 2K/5K/10K/Soak claim",
        }, new JsonSerializerOptions { WriteIndented = true }), deadline.Token);
    }
}
