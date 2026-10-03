using System.Runtime.InteropServices;
using System.Text.Json;
using Full.NET.Logging.Kafka;

namespace Full.NET.Benchmarks.Logging;

/// <summary>统一的本地子进程测量入口；不输出独立链路通过声明。</summary>
public static class LoggingIsolatedRouteProbe
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length != 3 || args[1] != "--output" || args[0] is not ("Collector" or "ApplicationKafka"))
            throw new ArgumentException("用法：logging-route-case <Collector|ApplicationKafka> --output <目录>");
        var route = args[0];
        var directory = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(directory);
        var resultPath = Path.Combine(directory, "request.result.json");
        File.Delete(resultPath);
        var configuration = route == "ApplicationKafka" ? new Dictionary<string, string?>
        {
            [KafkaLogProducerOptions.SectionName + ":BootstrapServers"] = Required("BOOTSTRAP_SERVERS"),
            [KafkaLogProducerOptions.SectionName + ":GeneralTopic"] = Required("GENERAL_TOPIC"),
            [KafkaLogProducerOptions.SectionName + ":PriorityTopic"] = Required("PRIORITY_TOPIC"),
            [KafkaLogProducerOptions.SectionName + ":SecurityProtocol"] = "Ssl",
            [KafkaLogProducerOptions.SectionName + ":SslCaLocation"] = Required("CA_PATH"),
            [KafkaLogProducerOptions.SectionName + ":MessageTimeoutMs"] = "15000",
            [KafkaLogProducerOptions.SectionName + ":ShutdownFlushTimeoutMs"] = "15000",
        } : null;
        DateTimeOffset startUtc = default, endUtc = default;
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var report = await LoggingRequestLatencyRunner.RunIsolatedRouteCaseAsync(directory, route, configuration,
            route == "ApplicationKafka" ? KafkaLogSnapshotExporter.Create : null, active =>
            {
                if (active)
                {
                    startUtc = DateTimeOffset.UtcNow;
                    Console.Error.WriteLine("FULLNET_MEASUREMENT_BEGIN " + startUtc.ToString("O"));
                }
                else
                {
                    endUtc = DateTimeOffset.UtcNow;
                    Console.Error.WriteLine("FULLNET_MEASUREMENT_END " + endUtc.ToString("O"));
                }
            }, deadline.Token);
        await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new
        {
            measurementOnly = true, requestProcessId = Environment.ProcessId, route,
            measurementStartUtc = startUtc, measurementEndUtc = endUtc, requestReport = report,
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
            processorCount = Environment.ProcessorCount,
        }, new JsonSerializerOptions { WriteIndented = true }), deadline.Token);
    }

    private static string Required(string suffix)
        => Environment.GetEnvironmentVariable("FULLNET_LOG_PROBE_" + suffix) is { Length: > 0 } value
            ? value : throw new ArgumentException("缺少测试参数：" + suffix);
}
