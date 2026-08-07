# 模块数据访问与一致性边界硬化实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 ADR-0002 已批准的模块内/模块间读取、关联、事务、领域参数和事件投影标准转化为自动化门禁与一个可复用的参考切片，使新增模块保持未来进程外迁移能力。

**Architecture:** 当前仍使用同进程、共享数据库的强化型模块化单体，但把共享数据库视为部署细节。模块只关联和事务写入自己的表；跨模块立即读取使用消费方最小 Port，高频读取使用所有者 Outbox 事件与消费方本地投影，新流程不依赖跨模块本地事务。

**Tech Stack:** .NET 10、ASP.NET Core、Dapper、DbUp、SQL Server、MySQL、MessagePack、事务 Outbox、Microsoft Testing Platform、MSTest、Node.js 治理脚本。

**Snapshot:** `module-data-consistency-boundary-20260807`

## 执行顺序

本计划为 Task 0（Admin.NET 吸收收口）之后的横切硬化轨道，按下列顺序执行；每个 Task 独立 snapshot、独立提交，前一 Task 的 runner/Docker 残留为 0 后才能开始下一项。

| 顺序 | Task | 目标 |
| --- | --- | --- |
| 1 | 冻结跨模块数据访问与事务债务目录 | 建立跨模块 SQL、外键和本地事务债务门禁；如实登记存量外键 |
| 2 | 固化事务失败语义 | 增加 `ExecuteResultAsync`，修复失败 `Result` 仍可能提交的问题 |
| 3 | 冻结 Identity → Organization 批量读取参考模式 | 固化批量 Contract 读取参考模式，禁止逐行回退查询 |
| 4 | 阻止通用 Settings 演变为领域参数仓库 | 阻止其他业务模块把 Settings `ConfigEntry` 当作领域参数仓库 |
| 5 | 收口治理、模板与开发说明 | 在 Tasks 1–4 有重复证据后更新模块交付 Skill 与能力状态 |

## 存量债务如实登记

`015_HostRoleDataScope` 在 SQL Server/MySQL 双库迁移中建立了 Identity → Organization 跨模块外键：`fn_identity_role_data_scope_unit.UnitId -> fn_organization_unit.Id`（约束名 `FK_fn_identity_role_data_scope_unit_Unit`）。该债务已登记于 [`module-cross-foreign-key-debt.json`](../../../contracts/architecture/module-cross-foreign-key-debt.json)，移除里程碑为本计划 Task 1 Step 4 的成对可恢复迁移。

在完成移除并通过双库恢复验证前，**不得宣称“跨模块外键债务已清零”**；[`capability-status.md`](../../roadmap/capability-status.md) 与对外能力表述必须保持这一限制。

## Global Constraints

- 批准依据：[`ADR-0002`](../../architecture/adr/ADR-0002-modular-monolith-evolution.md#模块内模块间数据关联与事务标准)与[总体架构 Spec §5.3、§9](../specs/2026-07-17-fullnet-architecture-design.md#53-模块通信规则)。
- 不提前引入 RabbitMQ、Kafka、服务发现、网关、DTC、`TransactionScope` 或网络化模块内调用。
- 生产 SQL 只能关联当前模块拥有的 `fn_<module>_*` 表；不得通过视图、同义词、存储过程、触发器或动态 SQL 隐藏跨模块访问。
- 新流程不得依赖跨模块本地事务；强不变量必须由唯一模块拥有。
- 领域参数必须强类型、带作用域和版本；通用 Settings 不代管领域不变量。
- 数据库行为和投影恢复必须覆盖 SQL Server/MySQL；事件按至少一次投递、显式 SchemaVersion 和幂等消费设计。
- 使用任务快照 `module-data-consistency-boundary-20260807`，按 `inner -> slice -> merge` 执行受影响验证；完整 Integration 只由 `main` CI 执行。

---

### Task 1: 冻结跨模块数据访问与事务债务目录

**Files:**
- Create: `contracts/architecture/module-local-transaction-debt.json`
- Create: `contracts/architecture/module-cross-foreign-key-debt.json`
- Modify: `contracts/architecture/module-table-access-debt.json`
- Create: `tests/Full.NET.ArchitectureTests/ModuleLocalTransactionBoundaryTests.cs`
- Create: `tests/Full.NET.ArchitectureTests/ModuleCrossForeignKeyBoundaryTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/ModuleTableOwnershipTests.cs`

**Interfaces:**
- Produces: exact transaction debt records with `consumerModule`, `ownerModule`, `entryPoint`, `reason`, `risk`, `removeByMilestone`.
- Produces: an exact foreign-key debt record for `fn_identity_role_data_scope_unit.UnitId -> fn_organization_unit.Id`, including both `015_HostRoleDataScope.sql` provider files and its removal milestone.
- Invariant: empty debt is valid; wildcard, missing file/entry point, expired milestone and stale records fail governance.

- [ ] **Step 1: Start the task snapshot and write failing catalog tests**

Run:

```powershell
pnpm test:task:start -- module-data-consistency-boundary-20260807
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj --configuration Release --filter "FullyQualifiedName~ModuleLocalTransactionBoundaryTests|FullyQualifiedName~ModuleCrossForeignKeyBoundaryTests|FullyQualifiedName~ModuleTableOwnershipTests"
```

Expected: the new transaction/foreign-key debt tests fail because the catalogs/scanners do not exist; existing table ownership tests remain discoverable.

- [ ] **Step 2: Implement exact debt validation**

The scanners must reject production module SQL that names another module's table through direct statements, paired migrations that add a cross-module foreign key, and classes that combine `ICommandTransaction` with another module's synchronous Contract inside the transaction delegate. Do not infer authorization from a shared `ICommandTransaction`; every temporary exception must match one exact catalog record. Populate the foreign-key catalog with the known `015_HostRoleDataScope` pair, and populate transaction debt only from fresh source evidence.

- [ ] **Step 3: Verify the architecture slice**

Run:

```powershell
dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj --configuration Release --filter "FullyQualifiedName~ModuleLocalTransactionBoundaryTests|FullyQualifiedName~ModuleCrossForeignKeyBoundaryTests|FullyQualifiedName~ModuleTableOwnershipTests"
pnpm test:governance
```

Expected: both commands pass with non-zero discovery; malformed and stale fixture catalogs are rejected.

- [ ] **Step 4: Remove the known cross-module foreign key in a paired recovery-safe migration**

After proving Identity can retain stable `UnitId` references and Organization deletion/deactivation publishes or exposes enough information for fail-closed validation/reconciliation, reserve the then-current migration number. Drop only `FK_fn_identity_role_data_scope_unit_Unit` in SQL Server/MySQL, preserve the Identity-owned `(RoleId, UnitId)` index/data, and test missing constraint, already-dropped constraint, orphan evidence and rerun recovery. Remove the exact catalog entry only in the same slice that passes both provider recovery tests.

### Task 2: 固化事务失败语义

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Abstractions/Messaging/ICommandTransaction.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperCommandTransaction.cs`
- Create: `tests/Full.NET.UnitTests/Data/CommandTransactionResultTests.cs`
- Audit: `src/Modules/Full.NET.Modules.Notifications/Features/SendHostInboxMessages/HostInboxMessageService.cs`
- Audit: `src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentItems/HostDocumentItemManagementService.cs`
- Audit: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUserUnits/TenantUserUnitManagementService.cs`
- Audit: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUserPositions/TenantUserPositionManagementService.cs`
- Audit: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUnits/TenantUnitManagementService.cs`
- Audit: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantPositions/TenantPositionManagementService.cs`
- Audit: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantPositionLevels/TenantPositionLevelManagementService.cs`

**Interfaces:**
- Produces: `ICommandTransaction.ExecuteResultAsync<T>(Func<CancellationToken, Task<Result<T>>>, CancellationToken)` whose failed result rolls back, while the existing exception-based overload remains source compatible during migration.
- Invariant: an operation that has written data cannot commit merely because it returned `Result.Failure` without throwing.

- [ ] **Step 1: Write RED transaction tests**

Cover success commit, exception rollback, cancellation rollback, failed Result rollback, nested participation and commit-result-unknown propagation. Use a recording `DbSession`/database fixture rather than asserting only mock call order.

- [ ] **Step 2: Implement the smallest compatible transaction API**

Add this explicitly named entry point; do not inspect arbitrary generic objects with reflection:

```csharp
Task<Result<T>> ExecuteResultAsync<T>(
    Func<CancellationToken, Task<Result<T>>> action,
    CancellationToken cancellationToken);
```

The implementation commits only when `result.IsSuccess` is true and otherwise rolls back with `CancellationToken.None`. Nested calls return the failed Result to the owning outer Result-aware transaction. Migrate only call sites that can return failure after their first write; pre-write validation paths may retain the existing overload.

- [ ] **Step 3: Run Unit and dual-provider transaction verification**

Run the focused Unit tests, then use the task snapshot affected planner. If SQL transaction behavior is selected, run the resulting SQL Server and MySQL slices; both providers must demonstrate rollback after a failed Result.

### Task 3: 冻结 Identity → Organization 批量读取参考模式

**Files:**
- Audit: `src/Modules/Full.NET.Modules.Identity.Contracts/IHostUserDirectory.cs`
- Audit: `src/Modules/Full.NET.Modules.Identity/HostUsers/HostUserDirectory.cs`
- Audit: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUserUnits/TenantUserUnitQueryService.cs`
- Audit: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUserPositions/TenantUserPositionQueryService.cs`
- Create: `tests/Full.NET.UnitTests/Organization/OrganizationHostUserDirectoryCompositionTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Organization/OrganizationUserUnitManagementAssertions.cs`
- Test: `tests/Full.NET.IntegrationTests/Organization/OrganizationUserPositionManagementAssertions.cs`

**Interfaces:**
- Consumes: `IHostUserDisplayDirectory.FindHostUsersAsync(IReadOnlyCollection<Guid>, CancellationToken)`.
- Invariant: one Organization page issues at most one distinct, bounded Host-user batch lookup; missing users do not trigger fallback per-row queries.

- [ ] **Step 1: Write batching characterization and negative-fixture tests**

Use a recording `IHostUserDisplayDirectory` and assert that a page containing repeated user IDs calls `FindHostUsersAsync` once with distinct IDs. Cover an empty page, missing users, cancellation and the configured maximum page size. Current conforming code may make the characterization test pass immediately; separately run a negative fixture that loops over single-user lookup and prove the architecture/contract assertion rejects it before changing production code.

- [ ] **Step 2: Preserve the minimal transport-neutral contract**

Keep the public signature exactly:

```csharp
Task<IReadOnlyDictionary<Guid, HostUserDirectoryEntry>> FindHostUsersAsync(
    IReadOnlyCollection<Guid> userIds,
    CancellationToken cancellationToken = default);
```

Normalize and bound the batch at the owner boundary, return only minimal display fields, and keep Organization unaware of Identity SQL or implementation types. Do not modify conforming production files merely to create a diff, and do not add HTTP/gRPC while both modules remain in one process.

- [ ] **Step 3: Verify Unit and dual-provider Organization behavior**

Run the new Unit tests and the task snapshot affected slice. Expected: SQL Server/MySQL preserve tenant/data-scope filtering, stable paging and one batch directory composition without cross-module SQL.

### Task 4: 阻止通用 Settings 演变为领域参数仓库

**Files:**
- Create: `tests/Full.NET.ArchitectureTests/DomainParameterOwnershipTests.cs`
- Audit: `src/Modules/Full.NET.Modules.Settings.Contracts/ConfigEntryManagementContracts.cs`
- Audit: `src/Modules/Full.NET.Modules.Settings/Features/ManageHostConfigEntries/HostConfigEntryManagementService.cs`
- Audit: `src/Modules/Full.NET.Modules.Settings/Features/ManageHostConfigEntries/HostConfigEntryQueryService.cs`
- Audit: `src/Modules/Full.NET.Modules.Settings/Features/ManageHostConfigEntries/Endpoint.cs`
- Audit root: `src/Modules`

**Interfaces:**
- Invariant: production modules other than Settings cannot consume ConfigEntry CRUD contracts or query `fn_settings_config_entry`; Settings-owned diagnostic policy remains an explicitly typed platform policy inside Settings.
- Future domain-policy contract shape: stable owner ID, trusted scope, typed fields, monotonic `Version`, effective time and update time; reliable change events carry a complete small snapshot and explicit SchemaVersion.

- [ ] **Step 1: Write RED ownership tests with violating fixtures**

The test fixture must demonstrate rejection of another production module importing `CreateConfigEntryRequest`, querying `fn_settings_config_entry`, or parsing a business rule from an arbitrary ConfigEntry value. It must allow Settings' own typed `DiagnosticPolicy` implementation and management UI CRUD.

- [ ] **Step 2: Implement the source/dependency gate**

Scan production module projects and C# sources outside `Full.NET.Modules.Settings`. Match exact ConfigEntry contract type names and the physical table token; do not reject unrelated dictionary, enum-catalog or grid-preference contracts merely because they live in Settings.Contracts.

- [ ] **Step 3: Register the first real domain parameter only in its owner plan**

Appointment is not yet an approved module in this plan, so this task must not create it. When a real domain parameter is approved, its independent module plan must name exact files and migration numbers, write owner-row plus Outbox dual-provider tests, and add consumer version-order/idempotency tests before implementation.

### Task 5: 收口治理、模板与开发说明

**Files:**
- Modify: `.agents/skills/fullnet-module-delivery/SKILL.md`
- Modify: `docs/superpowers/plans/2026-07-30-adminnet-design-absorption-program.md`
- Modify only if behavior is delivered: `docs/roadmap/adminnet-feature-parity.md`
- Modify test counts only if changed: `eng/testing/test-matrix.json`

**Interfaces:**
- Produces: a module-delivery checklist requiring data ownership, read mode, transaction owner, consistency SLA, idempotency key, projection rebuild and remote-adapter decision for every cross-module use case.

- [ ] **Step 1: Add the checklist after Tasks 1–4 are proven**

Do not update the project Skill merely because the documents exist. Add the workflow only after the reference slice and architecture gates produce repeatable evidence, following `rules/skill-evolution.md`.

- [ ] **Step 2: Run final affected verification**

```powershell
pnpm test:integration:affected:plan -- --snapshot module-data-consistency-boundary-20260807 --phase merge
pnpm test:integration:affected -- --snapshot module-data-consistency-boundary-20260807 --phase merge
pnpm test:integration:partitions
pnpm test:governance
pnpm test:skills
git diff --check
```

Expected: selected tests have non-zero discovery; SQL Server/MySQL selected slices pass; governance, skill validation and whitespace checks pass. Update `eng/testing/test-matrix.json` only from fresh discovery.

## Stop Conditions

- Stop if a proposed implementation reads, joins, writes or constrains another module's table.
- Stop if a new synchronous Contract call is placed inside a database transaction or presented as a cross-module snapshot; inventory existing occurrences as exact debt instead of silently expanding them.
- Stop if a failed `Result` after writes can still commit.
- Stop if a domain parameter is moved into Settings without proving it is platform-generic and has no domain invariant.
- Stop if an event consumer has no idempotency, version ordering, dead-letter/replay or rebuild path.
- Stop if a remote transport, Broker or distributed transaction is introduced without a real approved cross-process consumer and an ADR.
