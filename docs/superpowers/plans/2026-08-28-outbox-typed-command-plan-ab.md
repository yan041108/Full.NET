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
