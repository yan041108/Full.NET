# Outbox Typed Command Plan A/B Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an Outbox-only internal typed command-plan candidate and compare it fairly with the existing static-registry path without changing the production default.

**Architecture:** Add a closed generic `DapperTypedCommandPlan<TArgs>` inside `Full.NET.Data.Dapper`, with one bounded idle command slot per provider and ordinal parameter updates. The two Outbox writers receive an internal command-path mode; normal DI selects `StaticRegistry`, while the benchmark harness explicitly selects `TypedPlan` and executes through the same `DbSession`, transaction, telemetry, serializer, connection pool and database schema.

**Tech Stack:** .NET 10, C#, ADO.NET `DbCommand`, Dapper/Dapper.AOT, BenchmarkDotNet/profile harness, MSTest, SQL Server 2022 and MySQL 8 Testcontainers.

## Global Constraints

- Production DI must continue selecting `StaticRegistry`; the candidate is not a production cutover.
- Business modules continue depending only on `IOutboxWriter`; no Factory or Provider type crosses the Dapper boundary.
- SQL text, statement names, transaction semantics, tenant values and affected-row checks remain unchanged.
- Each plan owns one idle command slot per `DatabaseProvider`, clears parameter values before pooling and never shares an in-use command.
- Native AOT uses closed generic types and explicit ordinal binding; no reflection, anonymous parameter object or runtime shape discovery is introduced.
- A/B covers both legacy and append-only Outbox writes, SQL Server/MySQL, concurrency 1/8/32, and records throughput, errors, P50/P95/P99, allocation, CPU and connection waits.
- Implementation is eligible for production only if end-to-end allocation falls at least 5% or CPU/write falls at least 3%, with no P99, error-rate, transaction or dual-provider regression.
- Capacity remains `Capacity-not-verified`.

---

### Task 1: Typed command-plan lifecycle

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperTypedCommandPlan.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/OutboxTypedCommandPlans.cs`
- Create: `tests/Full.NET.UnitTests/Data/DapperTypedOutboxCommandPlanTests.cs`

**Interfaces:**
- Produces: `DapperTypedCommandPlan<TArgs>.GetCommand(DbConnection, DatabaseProvider, TArgs)` and `TryRecycle(DatabaseProvider, DbCommand)`.
- Produces: fixed singleton plans for `OutboxMessage` and `AppendOnlyOutboxMessage`.

- [ ] **Step 1: Write failing lifecycle tests**

Add tests that request two sequential commands and assert command/parameter identity reuse, updated ordinal values, detached connection/transaction after recycle, provider-slot isolation, and no sharing while the first command remains rented.

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release `
  --filter "FullyQualifiedName~DapperTypedOutboxCommandPlanTests"
```

Expected: compilation fails because the typed plan types do not exist.

- [ ] **Step 3: Implement the minimal plan**

Implement an abstract closed generic plan that creates parameters once, updates them by ordinal, keeps separate SQL Server/MySQL interlocked single slots, clears values on recycle and disposes commands rejected by a full slot. Implement the exact 8-parameter legacy and 12-parameter append plans.

- [ ] **Step 4: Verify GREEN**

Run the Task 1 filter again. Expected: all typed-plan lifecycle tests pass.

### Task 2: Outbox-only execution boundary

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperSqlExecutor.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxWriter.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperAppendOnlyOutboxWriter.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/ServiceCollectionExtensions.cs`
- Create: `tests/Full.NET.UnitTests/Data/DapperTypedOutboxWriterTests.cs`

**Interfaces:**
- Consumes: typed plans from Task 1.
- Produces: internal `DapperOutboxCommandPath.StaticRegistry` and `TypedPlan` selection.
- Produces: `DapperSqlExecutor.ExecuteTypedAsync<TArgs>(SqlStatement, TArgs, DapperTypedCommandPlan<TArgs>, CancellationToken)`.

- [ ] **Step 1: Write failing routing tests**

Add tests proving default writers call `ICommandExecutor.ExecuteAsync`, while explicit `TypedPlan` selection refuses a non-Dapper executor instead of silently falling back. Preserve existing affected-row and validation behavior.

- [ ] **Step 2: Verify RED**

Run the new writer-test filter. Expected: compilation fails because the command-path mode and typed execution boundary do not exist.

- [ ] **Step 3: Implement typed execution**

Add a typed executor method that performs `SqlScopeGuard.Validate`, acquires the existing `DbSession` lease, attaches the current transaction, uses the configured timeout, executes once, maps provider exceptions through `DataCommandExceptionMapper`, records the existing `DapperTelemetry`, and only recycles after success. Writers use it only when explicitly constructed with `TypedPlan`; service registration passes `StaticRegistry` explicitly.

- [ ] **Step 4: Verify GREEN and existing writer behavior**

Run the new filter plus `DapperRoutedOutboxWriterTests`. Expected: all pass and the default path remains the substitute-observable generic executor.

### Task 3: Production-shaped A/B harness

**Files:**
- Modify: `benchmarks/Full.NET.Benchmarks/Outbox/OutboxWriteProfileOptions.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Outbox/OutboxWriteProfileRunner.cs`
- Create: `tests/Full.NET.UnitTests/Performance/OutboxWriteProfileContractTests.cs`

**Interfaces:**
- Consumes: `DapperOutboxCommandPath` from Task 2.
- Produces: CLI option `--command-paths registry,typed` and a `CommandPath` field in every JSON result cell.

- [ ] **Step 1: Write failing CLI contract tests**

Test defaults, explicit ordered unique values, unknown values and duplicates. Assert the report model distinguishes both paths.

- [ ] **Step 2: Verify RED**

Run the `OutboxWriteProfileContractTests` filter. Expected: compilation/assertion failure because command paths are absent.

- [ ] **Step 3: Implement A/B selection**

Loop command path inside provider/target/concurrency, construct both writers through explicit DI factories with the selected internal mode, and include the stable lowercase path token in progress output and JSON results. Keep all other workload inputs identical.

- [ ] **Step 4: Verify GREEN**

Run the Task 3 filter and benchmark-project Release build. Expected: tests pass with zero build warnings/errors.

### Task 4: Evidence and decision

**Files:**
- Modify: `docs/verification/2026-08-26-outbox-typed-command-plan-p4-evidence.md`
- Create if a separate run record is clearer: `docs/verification/2026-08-28-outbox-typed-command-plan-ab.md`

**Interfaces:**
- Consumes: A/B JSON output from Task 3.
- Produces: explicit Go/No-Go decision and rollback boundary.

- [ ] **Step 1: Run focused dual-provider A/B**

Run two comparable samples per cell for providers `sqlserver,mysql`, targets `legacy,append`, paths `registry,typed`, concurrency `1,8,32`, payload 256 B, warmup 5 s and sample 10 s. This is a focused candidate comparison, not a capacity certification.

- [ ] **Step 2: Evaluate gates**

Compare paired cells for writes/s, error count, P50/P95/P99, alloc/write, CPU/write, SQL duration and connection wait. Reject cutover if either provider regresses correctness/P99 or if neither allocation nor CPU gate is met.

- [ ] **Step 3: Record evidence**

Document baseline/candidate commits, environment, exact commands, artifact path, paired results, statistical limitations, decision, rollback (`StaticRegistry`) and `Capacity-not-verified` status.

### Task 5: Repository verification and delivery

**Files:**
- Modify only documentation genuinely affected by the measured decision.

**Interfaces:**
- Produces: verified, reviewable commit on `main`; no branch or untracked source residue.

- [ ] **Step 1: Review affected Integration plan**

```powershell
pnpm test:integration:affected:plan -- --snapshot outbox-typed-plan-ab-20260828 --phase inner
```

- [ ] **Step 2: Run selected Unit/Architecture/AOT gates**

Run focused Unit tests, `pnpm test:aot:analyzers`, `pnpm test:dotnet:architecture --selection api-native-aot`, governance and naming checks.

- [ ] **Step 3: Run affected SQL Server/MySQL smoke tests**

Execute only the selector-proposed Outbox integration filters for both providers. If Docker or a provider is unavailable, record the exact failure and do not claim dual-provider verification.

- [ ] **Step 4: Final checks and commit**

Run Release build, `git diff --check`, `git status`, rule/Skill evolution checks, commit the focused change and push `main` only after all required gates pass.

### Task 6: Native AOT typed-path runtime closure

**Approved basis:** The repository owner explicitly approved the Outbox-only internal Typed Plan candidate and then requested completion of its Native AOT handling. This task does not revise the A/B No-Go decision or enable the candidate in Production.

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxCommandPathPolicy.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/ServiceCollectionExtensions.cs`
- Modify: `tests/Full.NET.UnitTests/Data/DapperTypedOutboxWriterTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiNotificationsE2EAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiNotificationsMySqlE2ETests.cs`
- Modify: `tests/Full.NET.IntegrationTests/NativeAot/NativeApiNotificationsSqlServerE2ETests.cs`
- Modify: `docs/verification/2026-08-28-outbox-typed-command-plan-ab.md`

**Interfaces:**
- Produces: `DapperOutboxCommandPathPolicy.Resolve(IConfiguration, string)`; only `Testing:OutboxCommandPath=TypedPlan` in the `Testing` environment selects the candidate.
- Consumes: the existing Notifications native external-process flow, whose successful mutation response depends on a real legacy Outbox write committing through `IOutboxWriter`; its post-commit SignalR notification separately verifies the API realtime/JSON closure and is not an Outbox-consumption assertion.

- [x] **Step 1: Write failing selection tests**

Add tests proving that Testing plus the exact `TypedPlan` token selects the typed path, the default stays `StaticRegistry`, Production ignores the testing-only key, and an unknown Testing token fails closed.

- [x] **Step 2: Verify RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~DapperTypedOutboxWriterTests"
```

Expected: compilation fails because `DapperOutboxCommandPathPolicy` does not exist.

- [x] **Step 3: Implement the Testing-only selector**

Resolve the enum once during `AddFullNetDapper`; capture it in both writer factories. Outside `Testing`, always return `StaticRegistry`. In `Testing`, accept an absent/`StaticRegistry` value or exact `TypedPlan`, and throw for any other token. Keep the key internal and omit it from production configuration examples.

- [x] **Step 4: Route the existing native Notifications gate through Typed Plan**

Pass `Testing:OutboxCommandPath=TypedPlan` to the native process. Rename both provider tests so the machine-visible test name states that the typed Outbox path is exercised. Preserve the existing HTTP/JSON/SignalR lifecycle; the successful mutation response proves the transaction containing the Typed Outbox insert committed, while SignalR verifies its separate post-commit API path. This Host.Api-only gate does not claim Worker consumption.

- [x] **Step 5: Verify the Linux native process on both providers**

Publish `linux-x64`, then run `pnpm test:aot:native:notifications:e2e` on Linux. On a Windows development host, execute the built integration assembly in a Linux SDK container with the Docker socket and `TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal`; discovery-only skips do not satisfy this step.

- [x] **Step 6: Close verification and retain the decision**

Run focused Unit, affected inner, AOT analyzer, Native AOT architecture, governance, `git diff --check`, and branch/status checks. Update the existing verification record with the exact commit/artifact and dual-provider native result while retaining `StaticRegistry` as Production default and `Capacity-not-verified`.

### Task 7: Five-repetition fixed-host A/B decision

**Approved basis:** The repository owner requested execution of the next recommendation in the verification record: increase each dual-provider cell to at least five repetitions before reconsidering an Outbox-only default switch.

**Files:**
- Modify: `docs/verification/2026-08-28-outbox-typed-command-plan-ab.md`
- No production code, SQL, configuration, migration or public contract changes are authorized by this task.

**Interfaces:**
- Consumes: `outbox-write-profile` with the existing interleaved Registry/Typed order and corrected post-warmup process-resource window.
- Produces: 120 raw samples covering 2 providers × 2 targets × 3 concurrency levels × 2 paths × 5 repetitions, plus a renewed Go/No-Go decision.

- [x] **Step 1: Freeze the local test boundary**

Record commit `f75e150c`, Docker engine/version, active container load, host CPU count and output path. Run no other build or database workload concurrently. Treat this as a focused fixed-host comparison, not production capacity certification.

- [x] **Step 2: Run the five-repetition matrix**

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release -- outbox-write-profile `
  --providers sqlserver,mysql `
  --concurrency 1,8,32 `
  --targets legacy,append `
  --command-paths registry,typed `
  --payload-size 256 `
  --repetitions 5 `
  --warmup-seconds 5 `
  --duration-seconds 10 `
  --output BenchmarkDotNet.Artifacts/outbox-typed-plan-ab-5x-20260828
```

Expected: JSON contains exactly 120 unique scenario samples; every Provider/target/concurrency/path cell contains repetitions 1..5.

- [x] **Step 3: Evaluate paired distributions**

For each Provider/target/concurrency cell, compare repetition-paired throughput, P50/P95/P99, errors, allocation/write, CPU/write, SQL duration and connection waits. Report median relative change plus the number of repetitions improved. A production switch remains No-Go if correctness differs, any cell has a material P99 regression without a stable counter-explanation, or neither allocation nor CPU gate is met consistently.

- [x] **Step 4: Record the renewed decision**

Append the exact environment, command, raw artifact path, paired table, aggregate resource results, limitations and decision to the existing verification record. Keep `Capacity-not-verified`; do not promote Typed Plan from the Testing-only candidate unless every gate passes.

- [x] **Step 5: Verify and deliver evidence only**

Run focused benchmark contract tests, benchmark Release build, governance, `git diff --check`, status/branch checks, independent evidence review, then commit and push only the plan/verification updates. Rule/Skill files remain unchanged unless their explicit evolution gates are independently met.

### Task 8: Profile observability closure

**Approved basis:** The five-repetition review identified two evidence defects in the benchmark harness: SQL Server cannot publish the MySqlConnector-specific pool wait histogram, and the Outbox worker drops exception classification and measurement-window ownership. This task repairs benchmark evidence only; it does not alter the production Outbox path or reopen the No-Go decision.

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/MixedLoad/MixedLoadDatabaseConnectionTelemetry.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Outbox/OutboxWriteProfileFailureClassifier.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Outbox/OutboxWriteProfileRunner.cs`
- Modify: `tests/Full.NET.UnitTests/Performance/OutboxWriteProfileContractTests.cs`
- Modify: `docs/verification/2026-08-28-outbox-typed-command-plan-ab.md`

**Interfaces:**
- Preserves `ConnectionWait` as the Provider-driver pool metric and adds `ConnectionAcquisition` from Full.NET's `DbSession` acquisition boundary for both providers.
- Adds stable SQL failure reasons, SQL cancellation count, attempt failure reason/error code/window ownership, and explicit measurement-window cancellation count. Exception messages, SQL and parameters remain excluded.

- [x] **Step 1: Establish root cause and RED tests**

Trace both telemetry paths and prove that SQL Server's listener intentionally returns no wait histogram while MySqlConnector publishes `db.client.connections.wait_time`; prove the worker's terminal catch only increments an error counter. Add focused tests for SQL Server acquisition capture and stable wrapped database-error classification, then observe compilation fail because both contracts are absent.

- [x] **Step 2: Implement the minimum evidence closure**

Listen to `fullnet.data.connection_pool/fullnet.db.connection.wait`, filter by the stable Provider tag, and aggregate milliseconds/outcomes. Preserve the driver-specific pool snapshot separately. Classify caught failures without messages and record whether the measurement token had already been canceled.

- [x] **Step 3: Verify with one dual-provider smoke**

Run one Registry/legacy sample for SQL Server and MySQL with 1 s warmup and 5 s measurement. Confirm both results contain `ConnectionAcquisition`, SQL Server still reports Provider `ConnectionWait` as unavailable, and the new cancellation/failure fields reconcile with totals. This smoke validates instrumentation only and must not be used to revise the Typed Plan decision.

- [x] **Step 4: Close repository verification**

Run focused Unit, benchmark Release build, affected inner plan/tests, governance, `git diff --check`, status/branch checks and independent code review. Record fresh results and keep `Capacity-not-verified` and Production `StaticRegistry` unchanged.
