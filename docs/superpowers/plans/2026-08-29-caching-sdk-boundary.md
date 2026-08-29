# Cache SDK Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让业务模块只依赖 Full.NET 自有缓存契约，不再直接构造或传递 FusionCache/HybridCache Options，同时保持当前缓存一致性与 Native AOT 行为。

**Architecture:** 新建有 Tenancy、Settings 和 Fusion Provider 三个真实消费者的 `Full.NET.Caching.Abstractions`。第一切片只抽象提交后失效，Fusion Provider 将稳定的 `CacheInvalidationScope` 映射为现有 Options；第二切片在基准确认无显著退化后再抽象 Get/Set 热路径，最后移除模块对缓存 SDK 的编译期引用。

**Tech Stack:** .NET 10、FusionCache、HybridCache、MSTest、Native AOT analyzers。

## Global Constraints

- 不改变 C0/S0-L2/S1/S2/N0 分类、TTL、Fail-Safe、序列化或缓存键。
- 提交后失效顺序保持“本机 L1 → 共享 L2 → Backplane”，不得改用 Outbox。
- `AllLayersSynchronous` 必须同步等待 L2 与 Backplane，并传播 Provider 异常；`CurrentNodeOnly` 不访问 L2/Backplane。
- 业务模块不得引用 `ZiggyCreatures.Caching.Fusion` 或暴露其 Options。
- 第二阶段热路径没有相同场景基准前不得实施，也不得宣称性能提升。
- 保留工作区中与本任务无关的现有改动，只暂存本计划列出的文件。

---

### Task 1: 建立失效契约与 Fusion Provider

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Caching.Abstractions/Full.NET.Caching.Abstractions.csproj`
- Create: `src/BuildingBlocks/Full.NET.Caching.Abstractions/ICacheInvalidator.cs`
- Create: `src/BuildingBlocks/Full.NET.Caching.Abstractions/CacheInvalidationScope.cs`
- Create: `src/BuildingBlocks/Full.NET.Caching.Fusion/FusionCacheInvalidator.cs`
- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/Full.NET.Caching.Fusion.csproj`
- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/ServiceCollectionExtensions.cs`
- Modify: `Full.NET.slnx`
- Test: `tests/Full.NET.UnitTests/Caching/FusionCacheInvalidatorTests.cs`

**Interfaces:**
- Produces: `ICacheInvalidator.RemoveAsync(string entryName, string key, CacheInvalidationScope scope, CancellationToken cancellationToken)`。
- Produces: `ICacheInvalidator.RemoveByTagAsync(string entryName, string tag, CacheInvalidationScope scope, CancellationToken cancellationToken)`。
- `CacheInvalidationScope` 仅包含 `CurrentNodeOnly` 与 `AllLayersSynchronous`，禁止模糊的后台模式。

- [x] **Step 1: Write the failing test**

新增测试，要求 DI 能解析 `ICacheInvalidator`；本机模式在故障 L2 下成功；全层同步模式传播 L2/Backplane 异常。

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~FusionCacheInvalidatorTests --no-restore`

Expected: FAIL，缺少 `ICacheInvalidator` / `FusionCacheInvalidator`。

- [x] **Step 3: Write minimal implementation**

`FusionCacheInvalidator` 从 `ICachePolicyRegistry` 取得 Options：

```csharp
private FusionCacheEntryOptions CreateOptions(string entryName, CacheInvalidationScope scope)
{
    var options = policies.CreateEntryOptions(entryName);
    if (scope == CacheInvalidationScope.CurrentNodeOnly)
    {
        options.SetSkipDistributedCache(true, true);
        return options;
    }

    options.AllowBackgroundDistributedCacheOperations = false;
    options.ReThrowDistributedCacheExceptions = true;
    options.AllowBackgroundBackplaneOperations = false;
    options.ReThrowBackplaneExceptions = true;
    return options;
}
```

- [x] **Step 4: Run test to verify it passes**

Run the Task 1 focused command. Expected: all discovered tests pass.

- [x] **Step 5: Commit**

Commit: `refactor: encapsulate fusion cache invalidation`

### Task 2: 迁移模块提交后失效路径

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenantCacheInvalidator.cs`
- Modify: `src/Modules/Full.NET.Modules.Settings/Features/ManageDiagnosticPolicy/DiagnosticPolicyCacheInvalidator.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Full.NET.Modules.Tenancy.csproj`
- Modify: `src/Modules/Full.NET.Modules.Settings/Full.NET.Modules.Settings.csproj`
- Test: `tests/Full.NET.UnitTests/Tenancy/TenantCacheInvalidatorTests.cs`
- Test: existing Settings diagnostic-policy tests selected by name.

**Interfaces:**
- Consumes: Task 1 `ICacheInvalidator` and `CacheInvalidationScope`.
- Produces: module invalidators with unchanged public/internal behavior and telemetry outcomes.

- [x] **Step 1: Write the failing architecture test**

Add a source boundary assertion that the two invalidator files contain neither `IFusionCache` nor `FusionCacheEntryOptions`.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter FullyQualifiedName~CachePolicyBoundaryTests --no-restore`

Expected: FAIL and list both current module invalidators.

- [x] **Step 3: Write minimal migration**

Inject `ICacheInvalidator`; use `CurrentNodeOnly` for the first pass and `AllLayersSynchronous` after commit. Preserve logging, metrics, cancellation and compatibility-handler exception propagation.

- [x] **Step 4: Run behavior and architecture tests**

Run focused Tenancy and Settings tests, then the Task 2 architecture filter. Expected: all pass.

- [x] **Step 5: Commit**

Commit: `refactor: move module invalidation behind cache contract`

### Task 3: Gate and prototype cache read/write abstraction

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/CacheAccessBoundaryBenchmarks.cs`
- Create: `docs/verification/2026-08-29-cache-access-boundary-evidence.md`
- Modify only after Go: `src/BuildingBlocks/Full.NET.Caching.Abstractions/ICacheStore.cs`
- Modify only after Go: `src/BuildingBlocks/Full.NET.Caching.Fusion/FusionCacheStore.cs`

**Interfaces:**
- Produces only on Go: stateful generic `GetOrCreateAsync<TState,T>` plus policy-driven `SetAsync`/`RemoveAsync`, without SDK types.
- Go gate: median overhead below 2%, allocations do not increase, and existing S1/S2 failure semantics remain unchanged.

- [x] **Step 1: Add the direct-vs-adapter benchmark with identical policy, key, tags and payload.**
- [x] **Step 2: Run Release BenchmarkDotNet with warmup and preserve raw artifacts.**
- [x] **Step 3: Record Go/No-Go; on No-Go stop without changing production Get/Set.**
- [x] **Step 4: On Go only, add failing behavior/architecture tests and implement the minimal adapter.** No-Go，本步骤不改生产代码。
- [x] **Step 5: Commit evidence separately from any production migration.**

### Task 4: Native AOT and integration closure

**Files:**
- Modify: `tests/Full.NET.ArchitectureTests/CachePolicyBoundaryTests.cs`
- Create: `docs/verification/2026-08-29-caching-sdk-boundary.md`

**Interfaces:**
- Consumes: completed Task 1–3 contracts.
- Produces: permanent module SDK-reference guard and fresh verification record.

- [x] **Step 1: Assert module cache invalidators do not reference FusionCache SDK types after migration; Get/Set remains an evidence-backed exception.**
- [x] **Step 2: Run affected cache Unit/Integration selection using snapshot `caching-sdk-boundary`.**
- [x] **Step 3: Run `pnpm test:aot:analyzers` and API Native AOT Architecture selection.**
- [x] **Step 4: Run `pnpm test:governance`, `git diff --check`, and inspect exact staged files.**
- [x] **Step 5: Commit the final guard and verification record.**

## Self-review

- Spec coverage: dependency direction, cache invalidation semantics, hot-path performance gate, Native AOT and architecture guard each map to a task.
- Placeholder scan: no TBD/TODO or unspecified implementation step remains.
- Type consistency: Task 2 consumes the exact Task 1 types; Task 3 is explicitly evidence-gated and does not block Task 1–2 delivery.
