# LG02 日志调用热路径探索性测量（2026-09-28）

代码基线为 `32fc0330757dc74b614e8c1b54e57e8824c0537b` 加未提交的 LG01/LG02 改动；本记录对应新增的 `LoggingHotPathBenchmarks`，不是可复现的正式前后 A/B，也不代表容量验收。运行环境：Windows 10 22H2、Intel Core i7-12700H（20 逻辑处理器）、.NET SDK 10.0.401、运行时 10.0.12、Release。数据为每次调用 32 条两字段合成日志，单生产线程；宿主使用正式 `AddFullNetServiceDefaults` 注册入口，Console 指向 `TextWriter.Null`，队列条数与字节上限仅为避免这个微基准饱和而提高到每通道 262144 条与 1 GiB。每轮检查队列排空且累计丢弃不增加。

命令：

```powershell
dotnet build benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-restore
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-build -- --filter '*LoggingHotPathBenchmarks*' --inProcess --invocationCount 512 --iterationCount 5 --warmupCount 2
```

构建通过，0 警告、0 错误；基准执行 3/3。原始结果保存于 [CSV](2026-09-28-logging-hot-path.csv)。每事件结果如下，误差为 BenchmarkDotNet 的 99.9% 区间半宽：

| 路径 | Mean | Error | StdDev | Allocated |
| --- | ---: | ---: | ---: | ---: |
| 禁用的 Debug | 329.7 ns | 140.0 ns | 36.36 ns | 88 B |
| 普通 Information 摘要 | 8.237 µs | 22.080 µs | 3.417 µs | 6312 B |
| 高优先级 Error 摘要 | 5.674 µs | 1.881 µs | 0.488 µs | 6334 B |

这组值不能用于判定性能达标。普通路径的区间大于均值，最短迭代仍低于 BenchmarkDotNet 建议的 100 ms；进程内运行会与基准宿主共享进程，后台消费者和 GC 也在同时运行。初次 32 条/轮的独立进程运行虽无丢弃，但迭代过短，普通路径约 46.9 µs；第一次使用默认 120 秒构建时限时只得到派生工程构建超时，调整 `--buildTimeout 360` 后才运行。不同运行的差异说明当前采样尚不稳定，不能把差值解释为代码性能变化。

下一步需在同一 Release 环境对旧双通道实现和当前实现使用相同真实字段、输出及队列预算；分开记录调用线程构造/快照/入队、后台输出与 RSS，并以固定负载和多并发请求测 P50/P95/P99、事件与字节吞吐、CPU、队列积压和丢弃。Collector、Kafka、Elasticsearch 及故障恢复不在本微基准范围。`Capacity-not-verified`。
