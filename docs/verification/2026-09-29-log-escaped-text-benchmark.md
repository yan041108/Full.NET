# 日志自由文本封套微基准（2026-09-29）

代码基线为 `c609759576c4bf54ff9e743161ecb829ca8d4d4a` 加未提交的日志切片。环境为 Windows 10 22H2、Intel Core i7-12700H（20 逻辑核）、.NET SDK 10.0.401、运行时 10.0.12、BenchmarkDotNet 0.15.8、Release。基准经公开 Host 日志注册入口写入双通道，后台 Console 输出为 `TextWriter.Null`；每个方法调用写入 32 条，IterationCleanup 要求队列排空且无新增丢弃。它测调用线程构造、封套和入队，不包含 Collector、Kafka、ES 或请求 P99。

三个场景为既有普通摘要、正常 Windows 路径文本和含 `pass\u0077ord` 的转义赋值文本。首轮使用 BenchmarkDotNet 默认构建超时，隔离进程构建到 120 秒被终止，虽命令退出码为 0，但没有执行任何基准。第二轮调高构建超时并取得数值，却出现全部方法迭代不足 100 毫秒的警告。最终命令在同一机器放大到每轮 256 次方法调用：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj --configuration Release --no-build -- --filter '*LoggingHotPathBenchmarks.General*' --job Short --warmupCount 3 --iterationCount 5 --invocationCount 256 --unrollFactor 1 --buildTimeout 600 --artifacts 'BenchmarkDotNet.Artifacts/logging-escape-20260929-valid'
```

BenchmarkDotNet 最终执行 3/3，原始结果位于本地忽略目录 `BenchmarkDotNet.Artifacts/logging-escape-20260929-valid/results/`。每条事件的汇总如下；Error 是 99.9% 置信区间半宽，不是请求延迟：

| 场景 | Mean | Error | StdDev | 托管分配 |
| --- | ---: | ---: | ---: | ---: |
| GeneralSummary | 30.16 μs | 62.76 μs | 16.30 μs | 6.19 KB |
| GeneralWindowsPath | 16.67 μs | 50.25 μs | 13.05 μs | 5.55 KB |
| GeneralEscapedAssignment | 25.48 μs | 45.16 μs | 11.73 μs | 5.84 KB |

Summary 与 WindowsPath 仍收到 `MinIterationTime` 警告；三个场景的误差范围均宽于均值，且摘要、路径和脱敏事件输出大小不同。因此这些数据不能用于判断转义扫描成本、吞吐优势或回归是否可接受，也不能替代 LG08 的同载荷 A/B 与请求 P50/P95/P99、CPU、RSS、丢弃及恢复矩阵。下一次有效测量应先固定等价载荷与输出路径，并消除迭代时长和后台消费扰动；本次不据此调整实现。`Capacity-not-verified`。
