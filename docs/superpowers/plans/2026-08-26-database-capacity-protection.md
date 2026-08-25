# Database Capacity Protection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 API、Worker 和 Native AOT 共用的数据访问边界补齐连接池指标、静态预算校验和有界准入保护，并提供稳定的 API/Worker 过载语义。

**Architecture:** `DbSession` 在首次打开连接前从单例 `DatabaseAdmissionGate` 获取许可证，许可证与连接同生命周期并在打开失败、取消或 Scope 释放时精确归还。`DatabaseCapacityOptionsValidator` 使用 Provider 官方连接字符串 Builder 校验真实池上限、角色声明和集群总预算；低基数 `Meter` 记录等待、持有、结果、使用中和排队。API 将数据库容量异常映射为 503 ProblemDetails，Worker 停止领取并使用独立退避。

> **P2 后续说明（2026-08-26）：** 非事务连接生命周期已由 Scope 缩短为单次命令，许可证随命令连接释放；只有显式事务继续持有到事务终态。详见 `2026-08-26-db-command-lifetime-and-aot-reuse.md`。

**Tech Stack:** .NET 10、ADO.NET、Microsoft.Data.SqlClient、MySqlConnector、OpenTelemetry Metrics、ASP.NET Core ProblemDetails、Helm、MSTest。

## Global Constraints

- SQL Server 与 MySQL 必须使用各自官方连接字符串 Builder，未声明池上限时按 Provider 默认值解析。
- 指标标签只允许 `provider`、`host_role` 和 `outcome`，禁止连接字符串、池名、租户、SQL 或异常消息。
- 准入许可证从打开连接前持有到连接真正释放；事务、取消和异常路径必须只释放一次。
- API 容量拒绝返回 HTTP 503 和稳定错误码 `common.database_capacity_exhausted`，客户端取消不得转换成 503。
- Worker 容量拒绝只暂停新领取并退避，不得改变已领取消息的至少一次交付语义。
- 自适应高水位和 DbCommand 生成式复用不进入本计划；它们需要本计划产生的等待/持有证据后作为独立切片实施。
- 没有生产等价容量验证时，交付状态保持 `Capacity-not-verified`。

---

### Task 1: Static pool budget contract

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/DatabaseCapacityOptions.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/DatabaseCapacityOptionsValidator.cs`
- Test: `tests/Full.NET.UnitTests/Data/DatabaseCapacityOptionsValidatorTests.cs`

**Interfaces:**
- Produces: `DatabaseCapacityOptions`, `DatabaseHostRole`, `DatabaseCapacityOptionsValidator`, `DatabasePoolConfiguration.ReadMaximumPoolSize(DatabaseOptions)`.

- [ ] **Step 1: Write failing tests** for SQL Server/MySQL explicit and default pool size, disabled policy, actual/declared mismatch, per-process reserve overflow and cluster budget overflow.
- [ ] **Step 2: Run** `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~DatabaseCapacityOptionsValidatorTests` and verify compilation/test failure is caused by the missing contract.
- [ ] **Step 3: Implement the minimal options, parser and validator.** The validator must calculate `ApiMaxReplicas * ApiMaxPoolSize + WorkerMaxReplicas * WorkerMaxPoolSize + MigrationReserve` with checked arithmetic and compare the active role's declared pool with the parsed connection string value.
- [ ] **Step 4: Re-run the filtered tests** and require zero failures.

### Task 2: Bounded admission and low-cardinality telemetry

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Results/ServiceCapacityExceededException.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/DatabaseConnectionTelemetry.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/DatabaseAdmissionGate.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/DbSession.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/ServiceCollectionExtensions.cs`
- Test: `tests/Full.NET.UnitTests/Data/DatabaseAdmissionGateTests.cs`
- Test: `tests/Full.NET.UnitTests/Data/DatabaseConnectionTelemetryTests.cs`

**Interfaces:**
- Consumes: validated `DatabaseCapacityOptions` from Task 1.
- Produces: `DatabaseAdmissionGate.AcquireAsync(CancellationToken)`, async-disposable lease, `DatabaseConnectionTelemetry.MeterName`.

- [ ] **Step 1: Write failing tests** proving immediate rejection for zero queue, one queued waiter, queue overflow, timeout, caller cancellation, successful recovery and metric tags/outcomes.
- [ ] **Step 2: Run the two filtered test classes** and verify they fail because the gate and telemetry do not exist.
- [ ] **Step 3: Implement the gate** with bounded queue accounting, linked timeout cancellation, exact-once lease disposal and no unbounded waiter allocation.
- [ ] **Step 4: Integrate `DbSession`** so opening failure releases the lease, successful opening records wait and connection disposal records hold duration before releasing the lease.
- [ ] **Step 5: Register options validation, gate and Full.NET-owned meters** in `AddFullNetDapper`; do not export raw Provider meter tags such as `pool.name`.
- [ ] **Step 6: Re-run the filtered tests** and require zero failures.

### Task 3: Stable API overload response

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Abstractions/Results/CommonErrorCodes.cs`
- Modify: `src/BuildingBlocks/Full.NET.Hosting/Api/StandardApiResultMapper.cs`
- Modify: `src/BuildingBlocks/Full.NET.Hosting/Resources/CommonErrors.resx`
- Modify: `src/BuildingBlocks/Full.NET.Hosting/Resources/CommonErrors.en-US.resx`
- Modify: `tests/Full.NET.UnitTests/Hosting/StandardApiResultMapperTests.cs`
- Modify: `tests/Full.NET.UnitTests/Localization/ErrorResourceCompletenessTests.cs`

**Interfaces:**
- Consumes: `ServiceCapacityExceededException` from Task 2.
- Produces: HTTP 503 ProblemDetails with code `common.database_capacity_exhausted`, `Retry-After`, localized title and trace id.

- [ ] **Step 1: Write a failing mapper test** asserting 503, stable code, sanitized title, trace id and `Retry-After`; retain the existing generic 500 test.
- [ ] **Step 2: Run the mapper and resource completeness tests** and verify the new test fails as 500.
- [ ] **Step 3: Add the stable code/resources and specialized exception mapping.** Do not expose queue length, pool size or exception text.
- [ ] **Step 4: Re-run both test classes** and require zero failures.

### Task 4: Worker acquisition backoff

**Files:**
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxWorkerOptions.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs`
- Modify: `tests/Full.NET.UnitTests/Outbox/OutboxProcessorTests.cs`

**Interfaces:**
- Consumes: `ServiceCapacityExceededException` from Task 2.
- Produces: `OutboxWorkerOptions.DatabaseCapacityBackoffMilliseconds` and capacity-specific batch delay state.

- [ ] **Step 1: Write failing tests** proving a capacity exception produces the configured backoff, does not increment normal empty-poll exponential state and does not swallow shutdown cancellation.
- [ ] **Step 2: Run the filtered Outbox tests** and verify expected failures.
- [ ] **Step 3: Add the bounded option validation and a specialized catch before the generic catch.** The catch must pause only subsequent acquisition; in-flight message terminal/renewal paths remain unchanged.
- [ ] **Step 4: Re-run the filtered tests** and require zero failures.

### Task 5: Helm wiring and Prometheus rules

**Files:**
- Modify: `deploy/helm/fullnet/values.yaml`
- Modify: `deploy/helm/fullnet/templates/configmap.yaml`
- Modify: `deploy/helm/fullnet/templates/_helpers.tpl`
- Modify: `deploy/observability/prometheus-rules.yaml`
- Modify: existing Helm/governance tests selected by `rg "connectionBudget|prometheus-rules" tests`.

**Interfaces:**
- Consumes: all `DatabaseCapacity` configuration keys from Task 1 and metric names from Task 2.
- Produces: role-specific runtime configuration tied to the existing chart budget and alerts grouped by provider/host role.

- [ ] **Step 1: Write or extend failing governance tests** for rendered API/Worker role, permit/queue/timeout/reserve values, total budget linkage, histogram grouping and timeout/rejection alerts.
- [ ] **Step 2: Run the selected Helm/governance tests** and verify expected failures.
- [ ] **Step 3: Wire chart values into `DatabaseCapacity__*` keys.** API defaults to a very small queue; Worker uses a small bounded queue and preserves explicit critical reserve in the pool arithmetic.
- [ ] **Step 4: Correct the Prometheus histogram query** to aggregate by `(le, provider, host_role)`, add a traffic guard, and add separate timeout/rejection alerts.
- [ ] **Step 5: Re-run selected tests** and require zero failures.

### Task 6: AOT and affected verification

**Files:**
- Create: `docs/verification/2026-08-26-database-capacity-protection.md`

**Interfaces:**
- Consumes: Tasks 1–5.
- Produces: reproducible verification evidence and explicit unverified capacity boundary.

- [ ] **Step 1: Run targeted Unit tests**, including capacity validator, admission, telemetry, mapper and Worker tests.
- [ ] **Step 2: Run** `pnpm test:aot:analyzers` and `pnpm test:dotnet:architecture --selection api-native-aot` because `DbSession` is Host.Api Native AOT reachable.
- [ ] **Step 3: Review the affected test plan** with `pnpm test:integration:affected:plan -- --base 166d0911cd09cb5307f1f751a76f35406fa09035 --phase inner` and execute only the selected local inner set.
- [ ] **Step 4: Run one SQL Server and one MySQL tiny-pool smoke** if local containers are available; otherwise record both as unverified without making capacity claims.
- [ ] **Step 5: Run Release build, `git diff --check`, `git status --short` and branch/HEAD checks.**
- [ ] **Step 6: Save exact commands, outputs, environment and `Capacity-not-verified` boundary** in the verification document.
