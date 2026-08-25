# Database command lifetime and Native AOT reuse verification

## Scope

- Baseline: `166d0911cd09cb5307f1f751a76f35406fa09035`
- Branch: `codex/db-capacity-protection`
- Task snapshot: `db-command-plan-p2-p3`
- Environment: Windows 10 `10.0.19045`, .NET SDK `10.0.400`, .NET runtime `10.0.11`, x64
- Providers: Microsoft SQL Server and MySQL reusable Testcontainers
- Capacity status: `Capacity-not-verified`

P2 changes non-transaction database ownership from DI-scope lifetime to command lifetime. A reader or `GridReader` is disposed before the connection lease; explicit transactions retain one connection until commit, rollback or session disposal. P3 adds an allow-listed Native AOT path for fixed scalar parameter plans using Dapper.AOT's official `CommandFactory<DynamicParameters>` reuse primitives.

The first P3 prototype discovered command plans from runtime SQL and `DynamicParameters` shape. A short benchmark showed that its dictionary/shape-discovery and rental overhead made it slower and more allocating than create/bind/dispose, so that implementation was removed. The retained design registers only known startup plans and keeps one idle command per Provider-specific factory.

## Results

### Unit and architecture tests

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~DbSessionCapacityTests|FullyQualifiedName~DapperAotStaticCommandPlanRegistryTests|FullyQualifiedName~DapperAotEnumerableParameterExpanderTests" --no-restore
```

Result: 14 passed, 0 failed, 0 skipped. Coverage includes non-transaction release, transaction borrowing, idempotent/conflicting plan registration, Provider-isolated slots, concurrent command separation, single idle command retention and scalar parameter-bag identity.

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~Full.NET.UnitTests.Data" --no-restore
```

Result: 72 passed, 0 failed, 0 skipped.

```powershell
pnpm test:dotnet:architecture --selection api-native-aot
```

Result: 49 passed, 0 failed, 0 skipped; build succeeded with 0 warnings and 0 errors. The added rule verifies the two Outbox plans and the append-only parameter binder.

### Tiny-pool dual-provider behavior

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter FullyQualifiedName~DatabaseCapacityConcurrencyTests
```

Result: 2 passed, 0 failed. SQL Server and MySQL each use `MaxPoolSize=2`, normal admission `1`, queue `1` and health reserve `1`. After queue, rejection, cancellation and recovery checks, the test keeps the first DI scope alive and proves that a second scope can acquire the single normal permit after the first command completes.

### Native AOT closure

```powershell
pnpm test:aot:analyzers
```

Result: succeeded with 0 warnings and 0 errors.

```powershell
pnpm test:aot:publish:linux
```

Result: Linux x64 Native AOT publish succeeded. The executable is 72,249,216 bytes; the warning gate accepted the repository's 9 allow-listed third-party aggregate warnings.

```powershell
pnpm test:aot:native:e2e
```

Result: discovery gate succeeded with 5 tests found and 0 failures, but all 5 external-process tests were skipped because the host is Windows. This is not evidence that the Linux native executable completed the real SQL Server/MySQL runtime flows; that remains a Linux CI/WSL gate.

### Affected integration set

```powershell
pnpm test:integration:affected:plan -- --snapshot db-command-plan-p2-p3 --phase inner
pnpm test:inner -- --snapshot db-command-plan-p2-p3
```

The plan selected `Data, smoke` from 20 files changed after the task snapshot. Result: 29 passed, 0 failed, 0 skipped in 7m 55s; the inner selector confirmed MySQL coverage. SQL Server coverage is supplied by the explicit dual-provider test above.

### Release and governance

```powershell
dotnet build Full.NET.slnx -c Release --no-restore
pnpm test:governance
```

Result: the Release solution build succeeded with 0 warnings and 0 errors; governance passed 52 of 52 tests.

```powershell
pnpm test:naming
```

Result: 29 passed and 1 failed. The failure is the existing four `FNSQL003 unsupported_ddl` findings in the unchanged SQL Server/MySQL `100_MessagingDomainAuditRequestedOutcome.sql` migrations. Those files are outside this task snapshot's impact set; this task does not modify or suppress the debt.

### Command-object allocation benchmark

```powershell
dotnet build benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-restore
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release --no-build -- --filter "*DapperAotCommandReuseBenchmarks*" --job ShortRun
```

Build result: succeeded with 0 warnings and 0 errors.

BenchmarkDotNet `ShortRun` result on an Intel Core i7-12700H:

| Method | Mean | Allocated | Time ratio | Allocation ratio |
| --- | ---: | ---: | ---: | ---: |
| CreateBindDispose | 899.1 ns | 3.73 KB | 1.00 | 1.00 |
| StaticPlanReuse | 837.1 ns | 3.27 KB | 0.93 | 0.88 |
| DirectFactoryReuse | 581.8 ns | 2.70 KB | 0.65 | 0.72 |

This benchmark measures only the `SqlCommand`/parameter object graph; it excludes pool checkout, network I/O and database execution. The retained registry lookup path reduced allocation by about 12% and mean time by about 7% in this short run. Direct factory access shows the remaining registry/parameter-bag overhead, but neither result is an end-to-end throughput claim.

## Remaining boundaries

- Only `outbox.insert` and `messaging.outbox.append` are registered for command reuse. All other statements continue through the correctness-first create/bind/dispose path.
- Collection-expanded parameters are intentionally not reusable because their SQL and parameter count vary.
- Static plans still receive a `DynamicParameters` object and call `Get<object>`; parameter-bag allocation and value-type boxing remain. Typed generated call sites are the next performance ceiling, but require changing the data-access boundary.
- Command-scoped connection disposal returns a logical provider connection to its pool; it does not close the physical socket on each query. It intentionally increases logical Open/Dispose calls in exchange for accurate admission occupancy and fairness.
- No production-equivalent soak, 2K/5K/10K concurrency run or application-level before/after allocation profile was executed. The repository remains `Capacity-not-verified`.
- `git diff --check` reported no whitespace errors; Git emitted only the repository's Windows LF-to-CRLF working-copy notices.

## Future P4 candidate: typed factory and fixed plan handle

Status: **Not approved / evidence-triggered**. This section records a future investigation point; it is not an implementation commitment or a statement that the current registry is a production bottleneck.

The `DirectFactoryReuse` benchmark is a lower bound rather than a production-equivalent alternative. It uses a factory resolved during `GlobalSetup`, skips the statement-name/Provider registry lookup, and does not set the command timeout performed by `StaticPlanReuse`. The production static-plan path already calls the same `DapperAotCommandFactory` after resolving the plan and must additionally preserve collection fallback, Provider selection, transaction attachment, reader lifetime and success-only recycle semantics.

### Evidence required to start P4

Do not implement P4 from the current nanosecond benchmark alone. Reconsider it only when a representative Native AOT Outbox profile shows that command preparation, parameter binding or related allocation is a material part of the write path after serialization, database I/O, locks and connection waiting are separated. Before implementation, define an explicit CPU/allocation threshold and capture the same workload, data, concurrency and duration for SQL Server and MySQL.

At minimum, preserve these inputs for the decision:

- Outbox throughput, error and duplicate rates;
- P50, P95 and P99 write latency;
- allocated bytes per write and GC rate;
- CPU stacks attributed to parameter creation/binding, plan resolution and command creation;
- database CPU, log/write latency, locks and connection-pool waiting.

If the full write path cannot distinguish the candidate from noise, keep the current registry even if a command-object microbenchmark remains faster.

### Candidate design boundary

The preferred candidate is a generated, strongly typed `CommandPlan<TArgs>` or equivalent fixed plan handle. It should resolve the Provider-specific factory once, update parameters by ordinal from `TArgs`, and avoid `DynamicParameters`, `Get<object>` and value-type boxing on the registered hot path. The generic executor and create/bind/dispose fallback must remain available for unregistered statements and collection-expanded SQL.

The candidate must not:

- expose Dapper.AOT factories directly to business modules;
- add a Dapper-specific dependency to the public `SqlStatement` abstraction;
- share commands across SQL Server and MySQL or across different parameter shapes;
- recycle a command after cancellation, execution/materialization failure, or before its reader is disposed;
- weaken tenant validation, transaction ownership, timeout, telemetry, admission or Outbox reliability semantics.

### P4 comparison and acceptance gate

Use an apples-to-apples command benchmark in which registry, fixed-handle and typed-direct candidates all perform the same timeout, transaction, connection attachment, cleanup and recycle work. Then run real SQL Server and MySQL Outbox writes with identical payloads and concurrency.

Accept P4 only when both Providers show a repeatable end-to-end CPU or allocation improvement outside benchmark noise, without worsening P99, errors, duplicates, connection waiting, transaction behavior or Native AOT closure. Required verification includes focused Unit/Architecture tests, dual-provider integration, AOT analyzers, Linux Native AOT publish and native-process Outbox flows. Otherwise record the result and retain the current static registry.
