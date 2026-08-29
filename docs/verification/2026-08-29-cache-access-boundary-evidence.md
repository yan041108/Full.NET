# Cache Get/Set SDK Boundary Evidence

## 结论

**No-Go。** 保留生产代码当前的 `HybridCache` / `IFusionCache` Get/Set 路径，不新增通用 `ICacheStore`，也不迁移 `TenantResolver`、`MyGridPreferenceService` 或 `DiagnosticPolicyStore`。

已交付的 `ICacheInvalidator` 不受该结论影响：失效位于写后低频路径，并且其价值是集中保护 L1/L2/Backplane 顺序和异常传播，而不是宣称降低延迟。

## 性能契约

- 基线提交：`f61184decee6c4e64c1825eb802efc23f62c39f4`
- 任务快照：`caching-sdk-boundary`
- 场景：无 Redis、无数据库、无序列化，只比较 L1 命中与同键 L1 覆盖。
- 公平性：Direct 与 Adapter 使用相同 `HybridCache`、`ICachePolicyRegistry`、条目名、载荷、键、标签和取消令牌；两边每次都创建相同策略 Options。
- Go 门槛：Adapter 平均时间开销低于 `2%`，且单次分配不增加。
- 环境：Windows 10、Intel Core i7-12700H、.NET SDK 10.0.400、.NET 10.0.11、BenchmarkDotNet 0.15.8、Concurrent Workstation GC。
- 运行：5 次预热、15 次测量；每个 benchmark invocation 内批量执行 10,000 次缓存操作。

## 命令与原始结果

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-build -- --filter "*CacheAccessBoundaryBenchmarks*" --artifacts "BenchmarkDotNet.Artifacts/cache-access-boundary"
```

原始报告：`BenchmarkDotNet.Artifacts/cache-access-boundary/results/Full.NET.Benchmarks.Caching.CacheAccessBoundaryBenchmarks-report-github.md`。

## 结果

| 场景 | Direct Mean | Adapter Mean | Ratio | Direct / Adapter 分配 | 判定 |
| --- | ---: | ---: | ---: | ---: | --- |
| L1 Get hit | 950.5 ns | 954.2 ns | 1.01 | 512 B / 512 B | 平均值满足，但 Direct 呈双峰且置信区间重叠 |
| L1 Set overwrite | 357.6 ns | 377.1 ns | 1.06 | 512 B / 512 B | **失败：约 +5.4%，超过 2% 门槛** |

Get 的 Adapter 平均差值约 `+0.39%`，但 Direct 的 99.9% 置信区间为 890.8–1010.1 ns，且 BenchmarkDotNet 报告双峰分布，不能据此宣称稳定等价。Set 的 Adapter 平均差值约 `+5.44%`，即使置信区间有重叠，也已经违反预先定义的平均开销门槛。

## 决策边界

- 不创建生产 `ICacheStore`，避免为了隐藏 SDK 类型在热路径加入未达标的通用接口分派。
- 不改变缓存 TTL、Fail-Safe、标签、序列化或权威源回退语义。
- 继续保留 `ICacheInvalidator`，因为它封装的是容易误配的一致性语义，并且已通过 Unit、MySQL Integration 与 Native AOT analyzer 验证。
- 若未来要重新评估，候选应是源生成或静态泛型适配器，而不是当前虚接口原型；仍须使用相同公平场景重新过门槛。

本结果不是生产容量认证；`Capacity-not-verified`。
