# Outbox Native AOT typed CommandPlan P4 evidence



## Scope



- Baseline: `346689c02164dd3fb2fa02cab36e36a00a15904f` (P4 evidence-gate doc commit); benchmark/evidence commit follows on `main`

- Branch: `main`

- Task snapshot: `db-command-plan-p4`

- Environment: Windows 10 `10.0.19045`, .NET SDK `10.0.400`, .NET runtime `10.0.11`, x64, Intel Core i7-12700H (20 logical processors)

- Providers: Microsoft SQL Server `2022-CU14` and MySQL `8` via Testcontainers

- Capacity status: `Capacity-not-verified`



This verification evaluates whether Outbox Native AOT hot paths should move from **static Registry + `DynamicParameters`** to **`CommandPlan<TArgs>` + fixed plan handle**. Only benchmarks and profile harnesses were added; **no production execution path was changed**.



## Decision



**No-Go** — retain the current static registry.



Command and parameter-object preparation is not a material share of end-to-end Outbox write CPU or allocation after separating serialization, connection waiting and database execution. Even after correcting the typed recycle path and batching micro-benchmark iterations, typed savings collapse to about **1.6%** of real per-write allocation (well below the **10%** gate), and a **conservative CPU upper-bound estimate** remains **0.04%–0.23%** of per-write CPU (well below the **5%** gate). Production effort should stay on database latency, serialization, transaction/connection orchestration and pool sizing.



## Phase 1 — Fair command-object micro-benchmark



### Changes (benchmarks only)



- Rewrote `DapperAotCommandReuseBenchmarks` to use real `outbox.insert` SQL and `OutboxMessage` values.

- Added shared harness `OutboxInsertCommandBenchmarkHarness` with `FinalizeCommand` (factory paths) and `FinalizeTypedCommand` (typed path).

- **Typed path fix:** no longer calls `FinalizeCommand(..., factory: null)` (which always disposed). Typed now sets `Connection` / `Transaction` / `CommandTimeout`, reads `Parameters.Count`, calls `typedPrototype.TryRecycle`, and disposes only when the recycle slot is full.

- `GlobalSetup` asserts two consecutive typed rentals return the **same undisposed** `DbCommand` instance with detached connection/transaction after recycle.

- All four paths execute **`OperationsPerBatch = 80_000`** identical operations per benchmark invocation (`[Benchmark(OperationsPerInvoke = 80_000)]`). Baseline `CreateBindDispose` minimum iteration time exceeds **100 ms**; faster paths complete the same work in less wall time, which is expected.



### Commands



```powershell

dotnet build benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release

dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-build `

  -- --filter "*DapperAotCommandReuseBenchmarks*" --job medium

```



Raw log: `BenchmarkDotNet.Artifacts/p4-command-reuse-medium-v2.log`



### Results (BenchmarkDotNet `MediumRun`, 80_000 ops/invoke, per-operation means, no database I/O)



| Method | Mean | Error (99.9% CI) | Allocated | Alloc ratio vs baseline |

| --- | ---: | ---: | ---: | ---: |

| CreateBindDispose | 2.073 us | ±0.657 us | 6.07 KB | 1.00 |

| StaticRegistryPlan | 1.497 us | ±0.159 us | 4.52 KB | 0.75 |

| FixedFactoryHandle | 1.624 us | ±0.223 us | 4.52 KB | 0.75 |

| TypedParameterFactoryPrototype | 1.289 us | ±0.186 us | 3.47 KB | 0.57 |



### Phase 1 interpretation



- **Earlier typed numbers are invalid** — passing `factory: null` into `FinalizeCommand` disposed every typed command instead of recycling.

- **Registry lookup** costs time on `StaticRegistryPlan` vs `FixedFactoryHandle` (1.50 us vs 1.62 us; overlapping CIs) but not allocation (both 4.52 KB).

- **Typed prototype** lowers allocation **43%** vs baseline and **23%** vs static registry (3.47 KB vs 4.52 KB) with correct recycle semantics.

- **Typed time vs fixed handle** (1.29 us vs 1.62 us) shows a micro-benchmark gain, but this does not translate to end-to-end write CPU or allocation gates below.



## Phase 2 — Dual-provider Outbox write profile



### Harness (benchmarks only)



- Added `outbox-write-profile` command: `OutboxWriteProfileOptions`, `OutboxWriteProfileRunner`, JSON report writer.

- Targets: `legacy` (`outbox.insert` → `fn_outbox_message`) and `append` (`messaging.outbox.append` → `fn_messaging_outbox_event`).

- Concurrency: 1, 8, 32; payload: 256 bytes; warmup 10 s; sample window 30 s; 3 repetitions per cell.

- Metrics: writes/s, error/duplicate counts, write P50/P95/P99, SQL telemetry duration, connection-pool wait, process allocation and GC, database lock/session snapshots.



### Commands



```powershell

# Legacy insert — full matrix

dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-build `

  -- outbox-write-profile `

  --providers sqlserver,mysql --concurrency 1,8,32 --targets legacy `

  --payload-size 256 --repetitions 3 --warmup-seconds 10 --duration-seconds 30 `

  --output BenchmarkDotNet.Artifacts/outbox-write-profile-p4



# Append-only — full matrix (after producer-name fix, see below)

dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-build `

  -- outbox-write-profile `

  --providers sqlserver,mysql --concurrency 1,8,32 --targets append `

  --payload-size 256 --repetitions 3 --warmup-seconds 10 --duration-seconds 30 `

  --output BenchmarkDotNet.Artifacts/outbox-write-profile-p4-append

```



Artifacts:



- Legacy: `BenchmarkDotNet.Artifacts/outbox-write-profile-p4/outbox-write-profile.json`

- Append: `BenchmarkDotNet.Artifacts/outbox-write-profile-p4-append/outbox-write-profile.json`



### Profile correction note



The first combined run recorded **0 successful append writes** because the benchmark `Producer` string `fullnet.benchmark.outbox-write-profile` violated `MessagingNames.ProducerPattern` (hyphens). The harness was corrected to `fullnet.benchmark.outboxwriteprofile` and append scenarios were re-run. Legacy results from the first run remain valid.



### Aggregated write-profile results (3 repetitions averaged per cell)



| Provider | Target | Conc | Avg writes/s | Write P50 (ms) | Write P99 (ms) | Alloc/write (B) | SQL P50 (ms) | Errors |

| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |

| sqlserver | LegacyInsert | 1 | 93.7 | 9.64 | 116.28 | 90,966 | 2.07 | 0 |

| sqlserver | LegacyInsert | 8 | 779.5 | 9.32 | 21.11 | 88,494 | 2.03 | 0 |

| sqlserver | LegacyInsert | 32 | 1,398.6 | 22.08 | 41.21 | 88,571 | 6.39 | 13 |

| mysql | LegacyInsert | 1 | 90.1 | 10.95 | 18.23 | 30,728 | 1.10 | 0 |

| mysql | LegacyInsert | 8 | 482.4 | 16.06 | 28.77 | 36,177 | 1.38 | 0 |

| mysql | LegacyInsert | 32 | 1,178.4 | 26.39 | 50.67 | 35,094 | 2.39 | 0 |

| sqlserver | AppendOnly | 1 | 123.3 | 7.81 | 12.84 | 94,172 | 1.42 | 0 |

| sqlserver | AppendOnly | 8 | 702.5 | 10.88 | 20.69 | 94,800 | 2.13 | 1 |

| sqlserver | AppendOnly | 32 | 1,316.0 | 22.78 | 45.00 | 95,270 | 4.59 | 10 |

| mysql | AppendOnly | 1 | 78.9 | 12.27 | 21.48 | 32,770 | 1.31 | 0 |

| mysql | AppendOnly | 8 | 399.6 | 19.26 | 36.11 | 39,365 | 1.59 | 0 |

| mysql | AppendOnly | 32 | 981.8 | 30.17 | 180.48 | 38,064 | 2.83 | 0 |



All **36 completed cells** finished after the append producer-name fix. **SQL Server high-concurrency errors (concurrency 8 and 32, both targets, all repetitions, successful cells only): 75 total** (legacy concurrency 32: 40; append concurrency 8: 4; append concurrency 32: 31). MySQL recorded **0 errors** across all cells.



### Attribution (all 36 completed cells; profile JSON reused)



| Metric | Value |

| --- | ---: |

| Mean alloc/write | 63,705 B |

| Mean write P50 | 16.47 ms |

| Mean SQL telemetry P50 | 2.44 ms |

| CPU us/write (mean, from `Process.CpuMilliseconds / SuccessfulWrites`) | 1,340 us |

| CPU us/write range | 889 – 5,813 us |

| Static registry command-object alloc (Phase 1) | 4.52 KB |

| Typed prototype command-object alloc (Phase 1, corrected recycle) | 3.47 KB |

| Command-object alloc as % of write alloc | **7.2%** (static upper bound) |

| Typed savings as % of write alloc | **1.6%** |

| **Conservative CPU upper-bound estimate** | **0.04% – 0.23%** |



**Conservative CPU upper-bound estimate (not CPU stack attribution):** divide the slowest corrected micro-benchmark per-operation time (`CreateBindDispose` **2.073 us**) by per-write CPU microseconds (`Process.CpuMilliseconds × 1000 / SuccessfulWrites`) from the profile JSON. Mean profile cell → **0.15%**; fastest-write cells (highest CPU us/write) → **0.04%**; slowest-write cells → **0.23%**. This assumes the entire create/bind/dispose command graph could be eliminated for free, so it is an upper bound, not measured on-stack CPU.



SQL telemetry (`fullnet.data.sql.duration`) starts after `CreateParameters` in `DapperSqlExecutor.ExecuteAsync`, so parameter-bag creation is already excluded from SQL duration. Even using the static command-object allocation as an upper bound, typed savings remain far below the **10% allocation** gate. The conservative CPU upper bound remains far below the **5% CPU** gate.



**Bottleneck location:** database execution and connection acquisition (SQL P50 grows with concurrency), plus serialization/routing/transaction overhead between end-to-end write latency and SQL telemetry. Command recycling is not on the critical path.



## Gate evaluation



| Gate | Threshold | Result |

| --- | --- | --- |

| Start P4 implementation | Command/param CPU ≥ 5% **or** alloc ≥ 10% (ex-payload) | **Not met** (≈0.04%–0.23% conservative CPU upper bound; ≈1.6% alloc) |

| Typed prototype stable gain | Repeatable outside noise | **Met in micro-benchmark only**; **not met end-to-end** |

| Candidate acceptance (hypothetical) | ≥5% end-to-end alloc or ≥3% CPU, P99 ±2%, dual-provider | Not applicable — implementation gate failed |



## Verification executed



```powershell

git rev-parse HEAD

dotnet build benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release

dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release `

  --filter "FullyQualifiedName~DapperAotStaticCommandPlanRegistryTests"

pnpm test:governance

pnpm test:aot:analyzers

git diff --check

```



| Check | Result |

| --- | --- |

| Benchmark project Release build | 0 warnings, 0 errors |

| `DapperAotStaticCommandPlanRegistryTests` | 4 passed |

| `pnpm test:governance` | 52/52 passed |

| `pnpm test:aot:analyzers` | 0 warnings, 0 errors |

| `git diff --check` | clean |



Native AOT Linux publish and native-process Outbox E2E were **not re-run** for this evidence-only task; prior P2/P3 verification on the same registry path remains the closure baseline.



## Remaining boundaries



- Micro-benchmarks exclude network I/O and database execution; profile harness uses Testcontainers, not production hardware.

- Faster benchmark paths complete the same 80_000 operations in less wall time than `CreateBindDispose`; only the baseline path enforces the 100 ms minimum iteration recommendation.

- `TypedParameterFactoryPrototype` lives only under `benchmarks/`; production still uses `DapperAotStaticCommandPlanRegistry` and create/bind/dispose fallback.

- Rule/skill evolution: no trigger hit; no rule or Skill update.



## If revisiting later



Re-open P4 only when a Native AOT Outbox profile on production-equivalent hardware shows command preparation ≥5% CPU (measured, not upper-bound estimate) or ≥10% post-payload allocation **and** a typed prototype wins stably on end-to-end metrics. Until then, prefer SQL/connection tuning, serialization cost review and pool/admission budgeting.

