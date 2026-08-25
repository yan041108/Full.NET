# Database Command Lifetime And AOT Reuse Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将非事务数据库连接缩短为单次命令租约，并让 Native AOT 固定 SQL/固定标量参数形状接入 Dapper.AOT 官方 CommandFactory 命令复用。

**Architecture:** `DbSession.AcquireConnectionAsync` 对非事务调用创建并打开独占连接租约，命令结束立即释放连接与准入许可证；显式事务仍持有唯一连接到提交、回滚或会话释放。Native AOT 只为启动期显式登记的稳定语句名、固定 SQL 与固定标量参数顺序创建 `CommandFactory<DynamicParameters>`，由官方 `TryReuseInterlocked`/`TryRecycleInterlocked` 更新参数并复用一个空闲命令；未登记语句和集合参数继续走创建后即释放的安全回退。基准证明运行期按 SQL/参数形状自动发现并缓存 Plan 的成本高于收益，因此不保留该实现。

**Tech Stack:** .NET 10、ADO.NET、Dapper 2.1.79、Dapper.AOT 1.0.48、MSTest、NSubstitute、BenchmarkDotNet、SQL Server、MySQL。

## Global Constraints

- 非事务连接与准入许可证必须在一次 Query/Execute/QueryMultiple 完成后释放；事务连接必须保持到事务终态。
- Reader 必须先释放，随后才能回收 DbCommand；异常或取消后的命令不得进入复用槽。
- Command Plan 只接受启动期显式登记的稳定语句名、固定参数数量和固定参数顺序；集合展开或未登记语句必须回退。
- 每个 Plan 按 Provider 隔离工厂，每个工厂最多缓存一个空闲命令；运行期不得创建无界 Plan。
- 回收前必须拆除 Connection/Transaction 并清空参数值，避免跨租约状态泄漏和大对象滞留。
- 租户校验、命令超时、错误映射、SQL Server/MySQL 行为与 Native AOT 静态闭包不得改变。
- 未运行生产等价负载与分配基准前只声明机制已接通，保持 `Capacity-not-verified`。

---

### Task 1: Command-scoped connection lease

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/DbSessionConnectionLease.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/DbSession.cs`
- Modify: `tests/Full.NET.UnitTests/Data/DbSessionCapacityTests.cs`

**Interfaces:**
- Produces: `DbSession.AcquireConnectionAsync(CancellationToken)` returning an async-disposable lease with `Connection` and `Transaction`.
- Preserves: `IDbTransactionCoordinator.BeginAsync/CommitAsync/RollbackAsync/HasTransaction`.

- [x] **Step 1: Write failing tests** proving two sequential non-transaction leases create/dispose two connections and return admission after each lease, while two leases inside a transaction borrow the same retained connection without disposing it.
- [x] **Step 2: Run** `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~DbSessionCapacityTests` and verify failures are caused by the missing lease API/lifetime.
- [x] **Step 3: Implement `DbSessionConnectionLease`.** An owned lease disposes connection, records hold duration and then returns admission exactly once; a borrowed transaction lease performs no disposal.
- [x] **Step 4: Refactor `DbSession`.** `AcquireConnectionAsync` returns a borrowed lease when a transaction exists and otherwise opens an owned lease. `BeginAsync` opens and retains its own connection/permit; commit/rollback release retained resources after transaction disposal, while failure leaves cleanup available to rollback/session disposal.
- [x] **Step 5: Re-run the filtered tests** and require zero failures.

### Task 2: Executor lease integration

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperSqlExecutor.cs`
- Modify: existing executor and real-provider tests selected by `rg "DapperSqlExecutor|DatabaseCapacityConcurrency" tests`.

**Interfaces:**
- Consumes: `DbSession.AcquireConnectionAsync` from Task 1.
- Produces: command-local `CommandDefinition` using `lease.Transaction`; QueryMultiple keeps the lease until projector and GridReader disposal finish.

- [x] **Step 1: Add failing executor/lifetime assertions** that a non-transaction command releases capacity before the scoped session is disposed and a transaction command does not.
- [x] **Step 2: Run the selected tests** and verify the non-transaction assertion fails against scope-held connections.
- [x] **Step 3: Acquire an async lease inside every executor operation.** Validate scope and construct parameters before acquisition, then build `CommandDefinition` with the lease transaction. Declare the lease before reader/GridReader resources so disposal order is reader then connection.
- [x] **Step 4: Re-run selected Unit tests** and require zero failures.

### Task 3: Official Dapper.AOT static command-plan bridge

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Full.NET.Data.Dapper.csproj`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperAotCommandFactory.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperAotStaticCommandPlanRegistry.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperAotInfrastructureRegistration.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperAotEnumerableParameterExpander.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperAotSqlExecution.cs`
- Create: `tests/Full.NET.UnitTests/Data/DapperAotStaticCommandPlanRegistryTests.cs`
- Modify: `tests/Full.NET.UnitTests/Data/DapperAotEnumerableParameterExpanderTests.cs`

**Interfaces:**
- Produces: startup-only `DapperAotStaticCommandPlanRegistry.Register(statementName, parameterNames)` and Provider-isolated factories; only successful command execution is recycled.
- Uses: Dapper.AOT `CommandFactory<DynamicParameters>.TryReuseInterlocked` and `TryRecycleInterlocked`.

- [x] **Step 1: Write failing tests** proving stable scalar shape reuses the same command/parameter objects with updated values, concurrent executions cannot share a command, Provider slots are isolated, conflicting startup shapes fail closed, and scalar expansion returns the original parameter bag.
- [x] **Step 2: Run the filtered test classes** and verify failures are due to the missing static registry/factory and avoidable scalar copy.
- [x] **Step 3: Make Dapper.AOT runtime types available to the shared project** while keeping its analyzer enabled only under `FullNetIsAotCompile`.
- [x] **Step 4: Benchmark and reject runtime shape discovery.** The automatic SQL/shape cache increased both time and allocation, so remove it and keep registration explicit and bounded by startup code.
- [x] **Step 5: Implement the fixed-shape factory and static registry.** The factory snapshots parameter names, creates parameters once, updates values by ordinal, uses one interlocked recycle slot, and detaches connection/transaction plus clears retained values before recycle.
- [x] **Step 6: Register the two stable Outbox insert plans** and the missing append-only parameter binder.
- [x] **Step 7: Change the enumerable expander** to return the original `DynamicParameters` when no expandable value exists. Collection expansion retains current SQL semantics and is non-reusable by reference inequality.
- [x] **Step 8: Integrate all AOT Query/QuerySingle/Execute paths.** Dispose readers before recycling the command and keep unregistered or collection-expanded shapes on the existing create/dispose path.
- [x] **Step 9: Re-run filtered tests** and require zero failures.

### Task 4: Allocation benchmark and verification

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Properties/AssemblyInfo.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Data/DapperAotCommandReuseBenchmarks.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Program.cs`
- Create: `docs/verification/2026-08-26-db-command-lifetime-and-aot-reuse.md`

**Interfaces:**
- Produces: BenchmarkDotNet selector comparing create/bind/dispose with rent/update/recycle using a real SqlCommand object graph without database I/O.

- [x] **Step 1: Add a benchmark type** with `[MemoryDiagnoser]`, identical SQL/parameter values and warmed command-plan state; register it in `BenchmarkSwitcher`.
- [x] **Step 2: Build the benchmark project in Release** and run a short benchmark; do not treat command-object-only timings as a production performance conclusion.
- [x] **Step 3: Run targeted Unit, one SQL Server smoke, one MySQL smoke, AOT analyzers, API Native AOT architecture selection, Release build and the snapshot-based inner affected set.**
- [x] **Step 4: Run `git diff --check`, snapshot impact check, `git status --short`, branch and HEAD checks.**
- [x] **Step 5: Record exact fresh results, remaining dynamic-parameter boxing, collection fallback and `Capacity-not-verified`** in the verification document.
