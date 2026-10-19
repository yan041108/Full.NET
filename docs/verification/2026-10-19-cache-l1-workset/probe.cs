using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Full.NET.Caching.Fusion;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion;

// 仅测实际 Full.NET L1/AsHybridCache 热路径的载荷密度；不启动网络、数据库或负载发生器。
if (args.Length != 3 || !int.TryParse(args[0], out var capacity) || !int.TryParse(args[1], out var payloadBytes)
    || capacity is < 1 or > 10_000 || payloadBytes is < 1 or > 65_536 || (long)capacity * payloadBytes > 80 * 1024 * 1024)
    throw new ArgumentException("Expected bounded capacity, payload bytes and output path.");
var output = Path.GetFullPath(args[2]);
if (File.Exists(output)) throw new InvalidOperationException("Preserve existing measurements.");
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Cache:L1PayloadCapacity"] = capacity.ToString(),
    ["Cache:DefaultDuration"] = "00:30:00",
    ["Cache:Jitter"] = "00:00:00",
    ["Cache:ApplicationName"] = "l1-workset-measurement",
}).Build();
await using var services = new ServiceCollection().AddLogging().AddFullNetCaching(config, "Testing").BuildServiceProvider();
var hybrid = services.GetRequiredService<HybridCache>();
var fusion = services.GetRequiredService<IFusionCache>();
const string tag = "workset-payload";
var factoryCalls = 0;
var maximumKeys = Math.Min(20_000, capacity * 2);
var levels = new[] { Math.Max(1, capacity / 16), Math.Max(1, capacity / 4), capacity, maximumKeys }.Distinct().ToArray();
var points = new List<object>();
await hybrid.SetAsync("warmup", new byte[64]);
await hybrid.RemoveAsync("warmup");
Collect();
var baseline = Snapshot();
var prior = 0;
foreach (var keys in levels)
{
    var elapsed = new List<double>();
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var stage = Stopwatch.StartNew();
    for (var i = prior; i < keys; i++)
    {
        var index = i;
        var start = Stopwatch.GetTimestamp();
        var result = await hybrid.GetOrCreateAsync<byte[]>(Key(index), _ =>
        {
            factoryCalls++;
            var value = new byte[payloadBytes];
            // 触达全部页面，并避免用共享数组或零页掩盖实际载荷分配。
            Array.Fill(value, (byte)(index % 251 + 1));
            return ValueTask.FromResult(value);
        }, tags: [tag]);
        elapsed.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        if (result.Length != payloadBytes || result[0] != (byte)(index % 251 + 1))
            throw new InvalidOperationException("Factory result changed under capacity pressure.");
    }
    stage.Stop();
    var allocatedAfterFill = GC.GetTotalAllocatedBytes(precise: true);
    await Task.Delay(100);
    var beforeCollection = Snapshot();
    var retained = Retained(keys);
    if (retained > capacity || factoryCalls != keys) throw new InvalidOperationException("Payload weight or factory result invariant failed.");
    Collect();
    var afterCollection = Snapshot();
    var retainedAfterCollection = Retained(keys);
    if (retainedAfterCollection > capacity) throw new InvalidOperationException("Payload weight exceeded after collection.");
    elapsed.Sort();
    double Quantile(double p) => elapsed.Count == 0 ? 0 : elapsed[(int)Math.Ceiling(p * elapsed.Count) - 1];
    points.Add(new
    {
        keys, retainedPayloads = retained, configuredWeightCapacity = capacity, payloadBytes,
        nominalRetainedPayloadBytes = (long)retained * payloadBytes,
        retainedPayloadsAfterCollection = retainedAfterCollection,
        retainedStableAcrossCollection = retainedAfterCollection == retained,
        factoryCalls, allocatedBytesIncludingTimingCollection = allocatedAfterFill - allocatedBefore,
        operations = elapsed.Count, elapsedMs = stage.Elapsed.TotalMilliseconds,
        p50Ms = Quantile(.50), p95Ms = Quantile(.95), p99Ms = Quantile(.99), beforeCollection, afterCollection,
        forcedFullCollectionForPostSample = true,
    });
    prior = keys;
}
await hybrid.RemoveByTagAsync(tag);
var remaining = Retained(maximumKeys);
if (remaining != 0) throw new InvalidOperationException("Tag invalidation retained old payloads.");
Collect();
var resultDocument = new
{
    measuredAtUtc = DateTimeOffset.UtcNow, kind = "Full.NET component L1 working-set measurement",
    runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(), processorCount = Environment.ProcessorCount,
    isServerGc = System.Runtime.GCSettings.IsServerGC, gcLatencyMode = System.Runtime.GCSettings.LatencyMode.ToString(),
    sourceAssembly = typeof(CacheOptions).Assembly.Location, capacity, payloadBytes, maximumKeys,
    capacityStatus = "Capacity-not-verified", networkOrDatabaseUsed = false,
    baseline, points, remainingAfterTagInvalidation = remaining, afterInvalidation = Snapshot(),
    limitations = new[] { "JIT component only; not Native API or database throughput", "Keys are not concurrent requests", "Weight units are not a byte/process limit", "Post-GC samples are forced-collection observations; RSS is allocator/runtime dependent", "Only stable retained-count samples may be used for a memory-density fit" },
};
await File.WriteAllTextAsync(output, JsonSerializer.Serialize(resultDocument, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Measured {levels.Length} stages; retained <= {capacity}; tag invalidation complete.");

string Key(int index) => $"workset-payload:{index}";
int Retained(int count) => Enumerable.Range(0, count).Count(i => fusion.TryGet<byte[]>(Key(i)).HasValue);
static void Collect() => GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
static object Snapshot()
{
    using var process = Process.GetCurrentProcess();
    process.Refresh();
    var memory = GC.GetGCMemoryInfo();
    return new { managedLiveBytes = GC.GetTotalMemory(false), gcHeapBytes = memory.HeapSizeBytes,
        gcCommittedBytes = memory.TotalCommittedBytes, workingSetBytes = process.WorkingSet64,
        privateMemoryBytes = process.PrivateMemorySize64, allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) };
}
