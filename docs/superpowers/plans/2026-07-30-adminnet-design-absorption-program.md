# Admin.NET 设计吸收改造实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不复制 Admin.NET.Pro 技术耦合和高风险动态机制的前提下，把源码复核确认的高价值设计分波次吸收到 Full.NET。

**Architecture:** 以现有模块化单体、Dapper 显式 SQL、SQL Server/MySQL 双库、标准 HTTP、事务 Outbox、FusionCache 和 Vue 单一后台产品线为唯一实现基线。计划先增强生成模型，再交付独立产品切片，随后落实逐页面/逐操作授权、字段授权、签名认证和模块治理；Document、Workflow、AI 等大型模块保持独立规格和计划。Layui 自 2026-08-02 起冻结，既有 Task 的双端记录只保留历史证据。

**Tech Stack:** .NET 10、ASP.NET Core Minimal API、Dapper、DbUp、SQL Server、MySQL、System.Text.Json Source Generation、MessagePack Outbox、FusionCache、Vue 3、TypeScript、Element Plus、MSTest、Vitest、Playwright；Layui 仅为冻结存量。

## Global Constraints

- 依据：[总体架构 Spec §22.2](../specs/2026-07-17-fullnet-architecture-design.md#222-adminnet-功能演进)、[Admin.NET 功能对标路线](../../roadmap/adminnet-feature-parity.md)和[源码复核](../../verification/adminnet-source-design-absorption-review-2026-07-30.md)。
- Admin.NET.Pro 只作为功能和实现参考；没有明确再许可证据时不得复制源码、资源、注释或生成模板。
- 业务模块只能使用 Dapper 和 Full.NET 自有 SQL 执行边界；禁止引入 SqlSugar、通用 Repository、动态 C#、运行时任意 WebAPI 或任意 SQL Parser。
- 所有新增 Admin.NET 对标模块必须遵守 [`ADR-0002` 的模块数据与事务标准](../../architecture/adr/ADR-0002-modular-monolith-evolution.md#模块内模块间数据关联与事务标准)：模块内关联本模块表；跨模块只使用最小批量 Port 或版本化事件投影；禁止跨模块 JOIN、外键、写表和新跨模块本地事务。
- 业务参数归对应领域模块所有并使用强类型、作用域、版本和生效语义；Settings 不得成为预约、支付、工作流等领域参数的任意字符串/JSON 仓库。可靠跨模块传播必须与所有者状态同事务写 Outbox，消费者按 MessageId/业务键和源版本幂等收敛。
- 租户 SQL 必须同时使用 `SqlDataScope.TenantRequired`、`SqlTenantBinding.CurrentTenantId` 和真实 `TenantId = @TenantId` 条件。
- 数据库变更必须同时提供 SQL Server/MySQL 迁移、半完成恢复测试和真实双库 Integration。
- 新后台管理功能只交付 Vue，并通过逐页面/逐操作权限、租户、错误处理、可访问性和真实栈 E2E；禁止新增或扩展 Layui 页面、按钮、适配器、生成模板和功能对等测试。
- 每个调用受保护 API、读取敏感数据、导出或产生业务副作用的 Vue 操作必须使用独立稳定权限码；角色授权页必须按模块/页面/操作授权，Endpoint 使用同一权限码失败关闭。
- 每个任务从同一任务快照执行 `inner -> slice -> merge` 影响集；完整 Integration 只由 `main` CI 分片执行。
- 状态只有在完整验收后才能更新为 `Verified`；计划存在、代码提交或局部构建通过均不构成完成证据。

每个可执行切片使用固定任务快照名：

| Task | 快照名 |
| --- | --- |
| 1 | `adminnet-absorb-01-codegen-capabilities` |
| 2 | `adminnet-absorb-02-codegen-lifecycle` |
| 3 | `adminnet-absorb-03-grid-preferences` |
| 4 | `adminnet-absorb-04-serial-numbers` |
| 5 | `adminnet-absorb-05-jobs-schedules` |
| 6 | `adminnet-absorb-06-files-provider` |
| 7 | `adminnet-absorb-07-field-projection` |
| 8 | `adminnet-absorb-08-signature-auth` |
| 9 | `adminnet-absorb-09-outbound-audit` |
| 10 | `adminnet-absorb-10-module-catalog` |
| 11 | `adminnet-absorb-11-large-module-queue` |
| 12 | `admin-action-permissions-identity-users-20260802` |

横切硬化（ADR-0002 数据访问与一致性边界）独立于上表编号，见[模块数据访问与一致性边界硬化计划](2026-08-07-module-data-access-and-consistency-hardening.md)：

| 轨道 | 快照名 |
| --- | --- |
| 模块数据访问与一致性边界硬化 | `module-data-consistency-boundary-20260807` |

2026-08-08 复核结论：Admin.NET 首轮吸收任务与上述横切框架底座已经进入 `main`，后续不再把存量一致性、安全扫描或客户端契约缺口回填到 Task 1–12。它们统一进入 [`2026-08-08-architecture-gap-follow-up.md`](2026-08-08-architecture-gap-follow-up.md)，每个 Cursor 窗口独立 snapshot、独立 RED→GREEN、独立受影响验证；大型 Document/Files 与 Identity/Organization 状态机不得并行修改共享迁移、Unit 输出或 Docker。

每个切片按下列固定命令执行影响集生命周期：

```powershell
# Task 1
pnpm test:task:start -- adminnet-absorb-01-codegen-capabilities
pnpm test:integration:affected:plan -- --snapshot adminnet-absorb-01-codegen-capabilities --phase inner
pnpm test:integration:affected -- --snapshot adminnet-absorb-01-codegen-capabilities --phase slice
pnpm test:integration:affected -- --snapshot adminnet-absorb-01-codegen-capabilities --phase merge

# Task 2
pnpm test:task:start -- adminnet-absorb-02-codegen-lifecycle
pnpm test:integration:affected:plan -- --snapshot adminnet-absorb-02-codegen-lifecycle --phase inner
pnpm test:integration:affected -- --snapshot adminnet-absorb-02-codegen-lifecycle --phase slice
pnpm test:integration:affected -- --snapshot adminnet-absorb-02-codegen-lifecycle --phase merge

# Task 3
pnpm test:task:start -- adminnet-absorb-03-grid-preferences
pnpm test:integration:affected:plan -- --snapshot adminnet-absorb-03-grid-preferences --phase inner
pnpm test:integration:affected -- --snapshot adminnet-absorb-03-grid-preferences --phase slice
pnpm test:integration:affected -- --snapshot adminnet-absorb-03-grid-preferences --phase merge

# Task 4
pnpm test:task:start -- adminnet-absorb-04-serial-numbers
pnpm test:integration:affected:plan -- --snapshot adminnet-absorb-04-serial-numbers --phase inner
pnpm test:integration:affected -- --snapshot adminnet-absorb-04-serial-numbers --phase slice
pnpm test:integration:affected -- --snapshot adminnet-absorb-04-serial-numbers --phase merge

# Task 5
pnpm test:task:start -- adminnet-absorb-05-jobs-schedules
pnpm test:integration:affected:plan -- --snapshot adminnet-absorb-05-jobs-schedules --phase inner
pnpm test:integration:affected -- --snapshot adminnet-absorb-05-jobs-schedules --phase slice
pnpm test:integration:affected -- --snapshot adminnet-absorb-05-jobs-schedules --phase merge

# Task 6
pnpm test:task:start -- adminnet-absorb-06-files-provider
pnpm test:integration:affected:plan -- --snapshot adminnet-absorb-06-files-provider --phase inner
pnpm test:integration:affected -- --snapshot adminnet-absorb-06-files-provider --phase slice
pnpm test:integration:affected -- --snapshot adminnet-absorb-06-files-provider --phase merge

# Task 7
pnpm test:task:start -- adminnet-absorb-07-field-projection
pnpm test:integration:affected:plan -- --snapshot adminnet-absorb-07-field-projection --phase inner
pnpm test:integration:affected -- --snapshot adminnet-absorb-07-field-projection --phase slice
pnpm test:integration:affected -- --snapshot adminnet-absorb-07-field-projection --phase merge

# Task 8
pnpm test:task:start -- adminnet-absorb-08-signature-auth
pnpm test:integration:affected:plan -- --snapshot adminnet-absorb-08-signature-auth --phase inner
pnpm test:integration:affected -- --snapshot adminnet-absorb-08-signature-auth --phase slice
pnpm test:integration:affected -- --snapshot adminnet-absorb-08-signature-auth --phase merge

# Task 9
pnpm test:task:start -- adminnet-absorb-09-outbound-audit
pnpm test:integration:affected:plan -- --snapshot adminnet-absorb-09-outbound-audit --phase inner
pnpm test:integration:affected -- --snapshot adminnet-absorb-09-outbound-audit --phase slice
pnpm test:integration:affected -- --snapshot adminnet-absorb-09-outbound-audit --phase merge

# Task 10
pnpm test:task:start -- adminnet-absorb-10-module-catalog
pnpm test:integration:affected:plan -- --snapshot adminnet-absorb-10-module-catalog --phase inner
pnpm test:integration:affected -- --snapshot adminnet-absorb-10-module-catalog --phase slice
pnpm test:integration:affected -- --snapshot adminnet-absorb-10-module-catalog --phase merge

# Task 11（文档队列；无服务端行为变更时不强制 Integration）
pnpm test:task:start -- adminnet-absorb-11-large-module-queue
```

---

## Program Gates

| Gate | 进入条件 | 退出证据 |
| --- | --- | --- |
| G0 来源与范围 | Admin.NET.Pro 基线 commit 已冻结 | 源码复核、差异清单、许可处理方式可追踪 |
| G1 生成基础 | 当前 CodeGeneration 安全写盘和模块接入门禁保持通过 | 实体能力/场景模型能够确定性生成并完成双库模板测试 |
| G2 产品切片 | G1 通过 | Grid Preferences、SerialNumbers、Jobs Trigger、Files Provider 分别形成可运行纵向切片 |
| G3 安全扩展 | 对字段授权和请求签名已有批准规格 | 字段投影不泄漏、签名防重放、认证失败审计通过 |
| G3.1 精确后台授权 | 页面/操作授权设计已批准 | 角色能逐页面/逐操作授权，Vue 无权限不渲染，直接 API 403，未知 Endpoint/权限失败关闭 |
| G4 M5+ 模块 | 1.0 核心能力和生产硬化不再被阻塞 | 每个大型模块有独立 Spec、Plan、双库/双端/安全验收 |

### Task 1: 扩展代码生成实体能力模型

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetCrudEntityCapabilities.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetCrudDeleteMode.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetCrudOwnershipMode.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetCrudSchema.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseCrudImportOptions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/DatabaseCrudSchemaAssembler.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/CrudSchemaDocument.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/DatabaseBatchMappingDocument.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/FullNetCrudSchemaTests.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/DatabaseCrudSchemaAssemblerTests.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationCliTests.cs`

**Interfaces:**
- Produces: `FullNetCrudEntityCapabilities(DeleteMode, HasCreatedAudit, HasUpdatedAudit, HasDeletedAudit, HasVersion, OwnershipMode)`
- Preserves: existing `FullNetCrudSchema.CreateProject(..., bool hasVersion, ...)` overload as a pre-1.0 compatibility shim until all repository callers migrate.

- [x] **Step 1: Write RED schema tests**

Add cases proving:

```csharp
var capabilities = new FullNetCrudEntityCapabilities(
    FullNetCrudDeleteMode.SoftDelete,
    HasCreatedAudit: true,
    HasUpdatedAudit: true,
    HasDeletedAudit: true,
    HasVersion: true,
    FullNetCrudOwnershipMode.OrganizationUnit);
```

The schema must require `IsDeleted`, `DeletedAtUtc`, `DeletedById`, created/updated audit fields, `Version`, and a stable ownership field. `HardDelete` must reject delete-audit columns, and `Immutable` must reject generated update/delete operations.

- [x] **Step 2: Run RED tests**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --filter "FullyQualifiedName~FullNetCrudSchemaTests|FullyQualifiedName~DatabaseCrudSchemaAssemblerTests|FullyQualifiedName~CodeGenerationCliTests" --no-restore
```

Expected: FAIL because the capability types and strict validation do not exist.

- [x] **Step 3: Implement immutable capability metadata**

Use enums and a sealed record; do not introduce entity inheritance or runtime global filters. `OwnershipMode.OrganizationUnit` identifies generation semantics only and must not accept a client-provided organization ID as authorization proof.

- [x] **Step 4: Update strict JSON and database import**

Require explicit capability values in new schema documents. Existing documents using `hasVersion` continue through the compatibility overload but generation reports must mark the legacy shape for pre-1.0 migration.

安全落地边界：Task 2 完成服务端审计赋值、组织授权与生命周期产物生成前，公共生成入口必须拒绝非 legacy 能力，禁止把保留字段暴露为客户端写入契约。

- [x] **Step 5: Run GREEN tests and naming gate**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --filter "FullyQualifiedName~FullNetCrudSchemaTests|FullyQualifiedName~DatabaseCrudSchemaAssemblerTests|FullyQualifiedName~CodeGenerationCliTests" --no-restore
pnpm test:naming
```

Expected: selected tests and naming checks PASS.

### Task 2: 生成软删除、审计、并发和场景安全 SQL

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetCrudScene.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetCrudRelationship.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetCrudWireValues.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Schema/FullNetCrudSchema.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/CrudSchemaDocument.cs`
- Modify: `src/Tools/Full.NET.CodeGeneration.Cli/DatabaseBatchMappingDocument.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudArtifactGenerator.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudBackendFeatureGenerator.cs`
- Review: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudMigrationTemplateGenerator.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.CodeGeneration/Generation/CrudClientPageModelGenerator.cs`
- Verify unchanged legacy fixture: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/backend/ProductSql.g.cs`
- Verify unchanged legacy fixture: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/templates/migrations/SqlServer/CreateProduct.sql.template`
- Verify unchanged legacy fixture: `tests/Full.NET.UnitTests/CodeGeneration/Fixtures/CatalogProduct/templates/migrations/MySql/CreateProduct.sql.template`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/CrudArtifactGeneratorTests.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/FullNetCrudSchemaTests.cs`
- Test: `tests/Full.NET.UnitTests/CodeGeneration/CodeGenerationCliTests.cs`
- Test: `tests/Full.NET.IntegrationTests/CodeGeneration/ModuleIntegrationCompilationTests.cs`

**Interfaces:**
- Consumes: `FullNetCrudEntityCapabilities`
- Produces schema/CLI modeling for: `FullNetCrudScene.Single`, `Tree`, `MasterDetail`, `ManyToMany`
- Executable generation: `Single` only; `Tree`, `MasterDetail` and `ManyToMany` remain fail-closed until their hierarchy or aggregate invariants have dedicated ports and tests.
- Invariant: generated tenant update/delete SQL includes `Id + TenantId + Version + not-deleted` whenever the corresponding capability applies.

- [x] **Step 1: Write RED generated-artifact tests**

Assert exact generated fragments:

```sql
WHERE Id = @Id
  AND TenantId = @TenantId
  AND Version = @Version
  AND IsDeleted = 0
```

Soft delete must update `IsDeleted`, `DeletedAtUtc`, `DeletedById` and increment `Version`; immutable entities are append-only and generate create/read but no update/delete endpoint. Tree scenes must require `ParentId`; executable Tree generation remains fail-closed until same-tenant parent validation, dangling-parent prevention and cycle detection are available. Relationship scenes must declare both sides, reject cross-scope relationships and remain fail-closed until aggregate transaction semantics are explicit.

- [x] **Step 2: Run RED generator tests**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj --filter FullyQualifiedName~CrudArtifactGeneratorTests --no-restore
```

Expected: FAIL on missing scene/capability generation.

- [x] **Step 3: Implement minimal deterministic generation**

Generate only declared capabilities. Do not force `IsActive`, soft delete or full audit fields onto every schema. Do not copy Admin.NET templates. Preserve Manifest ownership, atomic workspace apply and committed tombstone rules. Organization-owned output remains fail-closed until a trusted organization write-authorization port exists; client `OrganizationUnitId` is never authorization proof.

CLI 与生成报告中的枚举使用显式小写点分机器值；pre-1.0 PascalCase 输入只作为大小写敏感的兼容别名读取，报告只输出规范值。legacy `hasVersion` 报告必须标记 `legacyLifecycle=disable`，不得把兼容占位能力误报为实际 `HardDelete`。

- [x] **Step 4: Verify compile projection and dual-provider templates**

Run:

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --filter FullyQualifiedName~ModuleIntegrationCompilationTests --no-restore
pnpm test:sql-safety
pnpm test:naming
```

Expected: generated projection compiles; generated Vue/Layui client source passes syntax validation; SQL safety and naming gates PASS. 此门禁验证双 Provider 模板的确定性形状，不等同于动态生成 SQL 已完成 SQL Server/MySQL 运行时语义验证。

验证记录：[CodeGeneration Admin.NET 生命周期设计吸收验证](../../verification/codegeneration-adminnet-lifecycle-2026-07-30.md)。

### Task 3: 交付用户 Grid/Column 偏好

**Files:**
- Create: `src/Modules/Full.NET.Modules.Settings.Contracts/GridPreferenceContracts.cs`
- Create: `src/Modules/Full.NET.Modules.Settings/Catalogs/GridPreferenceCatalog.cs`
- Create: `src/Modules/Full.NET.Modules.Settings/Persistence/GridPreferenceSql.cs`
- Create: `src/Modules/Full.NET.Modules.Settings/Features/ManageMyGridPreferences/GridPreferencePolicy.cs`
- Create: `src/Modules/Full.NET.Modules.Settings/Features/ManageMyGridPreferences/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Settings/Features/ManageMyGridPreferences/MyGridPreferenceService.cs`
- Modify: `src/Modules/Full.NET.Modules.Settings/SettingsModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Settings/Serialization/SettingsJsonSerializerContext.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/038_SettingsGridPreference.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/038_SettingsGridPreference.sql`
- Create: `packages/client-contracts/src/grid-preferences.ts`
- Modify: `packages/client-contracts/src/index.ts`
- Create: `ui/admin/src/preferences/grid-preferences.ts`
- Create: `ui/admin-layui/js/core/grid-preferences.js`
- Test: `tests/Full.NET.UnitTests/Settings/GridPreferenceTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Settings/SettingsGridPreferenceAssertions.cs`
- Test: `tests/Full.NET.IntegrationTests/Migrations/Migration038SettingsGridPreferenceRecoveryTests.cs`
- Create: `contracts/openapi/settings-grid-preferences-v1.json`
- Test: `tests/openapi/settings-grid-preferences-contract.test.mjs`
- Test: `packages/client-contracts/tests/grid-preferences.test.ts`
- Test: `ui/admin/src/preferences/grid-preferences.test.ts`
- Test: `ui/admin-layui/tests/grid-preferences.test.js`

**Interfaces:**
- Produces: `GridPreferenceResponse(GridKey, SchemaVersion, IReadOnlyList<GridColumnPreference>, Version)`
- Produces endpoints: `GET`, `PUT`, `DELETE /api/v1/me/grid-preferences/{gridKey}`
- Security: Grid/Column keys must come from the local client catalog; preferences affect presentation only and never grant data access.

- [x] **Step 1: Confirm the reserved migration number**

Confirm that `038` is unused in both migration directories. If either provider already uses `038`, stop and rebase this plan so every later paired reservation remains unique and ordered; never rename or edit a published migration.

- [x] **Step 2: Write RED unit and dual-database tests**

Cover per-user isolation, unknown Grid/Column rejection, duplicate column rejection, schema-version reset, optimistic conflict and idempotent reset.

- [x] **Step 3: Implement Dapper persistence and API**

Store normalized JSON only after validating against the server/client shared catalog. Cache by user + grid + schema version through FusionCache; invalidate after successful commit.

- [x] **Step 4: Implement Vue/Layui adapters**

Both clients must restore width, order, visibility and fixed state, and fall back safely when a grid schema version changes.

- [x] **Step 5: Verify slice**

Run affected unit, SQL Server/MySQL Integration, client-contracts, Vue/Layui tests and same-scenario E2E selected by the task snapshot. Expected: all selected checks PASS with non-zero discovered tests.

### Task 4: 建立 SerialNumbers 官方模块

**Files:**
- Create: `src/Modules/Full.NET.Modules.SerialNumbers/Full.NET.Modules.SerialNumbers.csproj`
- Create: `src/Modules/Full.NET.Modules.SerialNumbers/SerialNumbersModule.cs`
- Create: `src/Modules/Full.NET.Modules.SerialNumbers/Contracts/SerialNumberContracts.cs`
- Create: `src/Modules/Full.NET.Modules.SerialNumbers/Domain/SerialNumberPattern.cs`
- Create: `src/Modules/Full.NET.Modules.SerialNumbers/Persistence/SerialNumberSql.cs`
- Create: `src/Modules/Full.NET.Modules.SerialNumbers/Features/ManageHostSerialRules/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.SerialNumbers/Features/ManageHostSerialRules/HostSerialRuleService.cs`
- Create: `src/Modules/Full.NET.Modules.SerialNumbers/Features/AllocateSerialNumbers/SerialNumberAllocator.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/039_SerialNumbers.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/039_SerialNumbers.sql`
- Modify: `src/Composition/Full.NET.Composition/Full.NET.Composition.csproj`
- Modify: `src/Composition/Full.NET.Composition/FullNetModuleCatalog.cs`
- Test: `tests/Full.NET.UnitTests/SerialNumbers/SerialNumberPatternTests.cs`
- Test: `tests/Full.NET.IntegrationTests/SerialNumbers/SerialNumberAllocationAssertions.cs`
- Test: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`

**Interfaces:**
- Produces: `ISerialNumberAllocator.AllocateAsync(string ruleKey, string idempotencyKey, CancellationToken)`
- Pattern tokens: fixed text, UTC date parts, tenant identifier, and `{sequence:N}` only.
- Semantics: unique and monotonic within `(Scope, RuleKey, ResetBucket)`; gaps are allowed and documented.

- [x] **Step 1: Confirm migration `039` is unused, then write RED parser tests**

Stop and rebase all later paired reservations if `039` is occupied in either provider. Reject unknown tokens, multiple sequence tokens, width outside the approved range, local-time tokens and unbounded output length. Preview must not consume a number.

- [x] **Step 2: Write RED dual-database concurrency tests**

Run concurrent allocation across multiple scopes, verify uniqueness, idempotent result replay and reset-bucket rollover. Do not use FusionCache locks as the correctness boundary.

- [x] **Step 3: Implement database-atomic allocation**

Use Provider-specific, parameterized SQL behind paired `SqlStatement` definitions. Persist idempotency key and allocated result in the same transaction as the sequence update.

- [x] **Step 4: Register module and verify**

Update Composition once, add module dependency/ownership architecture tests, then run Unit, SQL Server/MySQL Integration, naming, SQL safety and Release build.

**结果（2026-07-30）：** `039` 成对落地并完成缺失/畸形索引恢复；规则 API、
纯预览、Host/租户作用域、UTC 重置、事务级幂等和双库原子并发已交付。
当前无 Vue/Layui 页面，状态保持 Build-verified；证据见
[`serial-numbers-2026-07-30.md`](../../verification/serial-numbers-2026-07-30.md)。

### Task 5: 扩展 Jobs 触发器与执行历史

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/Contracts/JobContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Persistence/JobRecords.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Persistence/JobSql.cs`
- Create: `src/Modules/Full.NET.Modules.Jobs/Scheduling/JobScheduleCalculator.cs`
- Create: `src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobSchedules/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobSchedules/HostJobScheduleService.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionHostedProcessor.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/040_JobsSchedules.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/040_JobsSchedules.sql`
- Test: `tests/Full.NET.UnitTests/Jobs/JobScheduleCalculatorTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Jobs/JobsScheduleAssertions.cs`

**Interfaces:**
- Produces trigger kinds: `manual`, `one_time`, `cron`
- Produces misfire policies: `skip`, `fire_once`
- Invariant: schedule calculation uses IANA/Windows-normalized time-zone IDs but persists every execution instant in UTC.

- [x] **Step 1: Confirm migration `040` is unused, then write RED schedule tests**

Stop and rebase all later paired reservations if `040` is occupied in either provider. Cover disabled definitions, DST gap/overlap, one-time completion, cron next occurrence, misfire skip/fire-once, pause/resume and lease recovery.

- [x] **Step 2: Implement schedule persistence and calculator**

Use a reviewed cron library only after license, maintenance and Native AOT impact checks; otherwise implement the approved limited grammar. Dynamic C# jobs remain prohibited.

- [x] **Step 3: Integrate Worker claiming**

Claim due schedules with stable ordering, bounded batches and existing execution leases. Creating the execution record and advancing the schedule must be atomic.

- [x] **Step 4: Verify Jobs slice**

Run Jobs Unit, SQL Server/MySQL Integration, Worker smoke, migration recovery, Jobs backlog benchmark contract and Release build.

Task 5 已于 2026-07-31 完成。Cronos 仅承担五段 Cron、时区与 DST 计算；计划真源、原子物化、误触发策略、暂停恢复、执行历史关联和双库 `040` 恢复均已落地。证据见
[`jobs-schedules-2026-07-31.md`](../../verification/jobs-schedules-2026-07-31.md)。

### Task 6: 建立 Files 存储 Provider 边界

**Files:**
- Create: `src/Modules/Full.NET.Modules.Files/Storage/IFileStorageProvider.cs`
- Create: `src/Modules/Full.NET.Modules.Files/Storage/FileStorageProviderRegistry.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Storage/LocalHostFileBlobStorage.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Features/ManageHostFiles/HostFileManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/FilesModule.cs`
- Test: `tests/Full.NET.UnitTests/Files/FileStorageProviderRegistryTests.cs`
- Test: `tests/Full.NET.UnitTests/Files/LocalHostFileBlobStorageTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Files/FilesHostFileManagementAssertions.cs`

**Interfaces:**
- Produces: `IFileStorageProvider` with stable `ProviderKey`, streaming `SaveAsync`, `OpenReadAsync`, idempotent `DeleteAsync`, and optional health probe.
- Invariant: provider choice comes from trusted configuration or stored metadata, never an arbitrary request type name.

- [x] **Step 1: Write RED provider contract tests**

Cover duplicate keys, unknown providers, path traversal, interrupted upload, atomic publish, cancellation, idempotent delete and provider mismatch.

- [x] **Step 2: Adapt the local provider**

Preserve the existing same-directory staging and atomic move behavior. Replace module code that assumes a single local implementation with the registry.

- [x] **Step 3: Prove compensation and cleanup**

Verify database failure after upload triggers best-effort compensation, soft-delete cleanup calls the recorded provider, and a failed provider does not delete another provider's object.

- [x] **Step 4: Stop before an external provider package**

Do not create `Files.Contracts`, S3, OSS or MinIO projects in this task. The first real external provider must justify the contracts assembly, add dependency/license review and run a provider-specific integration environment.

Task 6 已于 2026-08-01 完成。`IFileStorageProvider`、规范 key 注册表、受校验默认配置与
`047_FilesStorageProvider` 双库迁移已落地；上传持久化 Provider，下载、删除、补偿和墓碑清理按
记录路由，未知 Provider 失败关闭。当前仍只交付本地实现，证据见
[`files-storage-provider-boundary-2026-08-01.md`](../../verification/files-storage-provider-boundary-2026-08-01.md)。

### Task 7: 设计并实现稳定字段投影授权

**Files:**
- Create: `docs/superpowers/specs/2026-07-30-field-projection-authorization-design.md`
- Create: `src/Modules/Full.NET.Modules.Identity.Contracts/FieldProjectionContracts.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/FieldProjection/FieldProjectionCatalog.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/FieldProjection/UserFieldProjectionResolver.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/ManageHostRoleFieldGrants/Endpoint.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/041_IdentityRoleFieldGrant.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/041_IdentityRoleFieldGrant.sql`
- Test: `tests/Full.NET.UnitTests/Identity/FieldProjectionResolverTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Identity/IdentityRoleFieldGrantAssertions.cs`
- Test: `tests/Full.NET.ArchitectureTests/EndpointAuthorizationTests.cs`

**Interfaces:**
- Produces stable keys: `ResourceKey`, `FieldKey`, `Sensitivity`, `DefaultVisibility`
- Produces: `IUserFieldProjectionResolver.ResolveAsync(userId, tenantId, resourceKey, cancellationToken)`
- Security: physical table/column names are never public grants; multiple roles produce a union bounded by the resource catalog.

- [x] **Step 1: Complete and approve the security spec**

Confirm that migration `041` is unused in both providers; otherwise stop and rebase all later paired reservations. The spec must decide list/detail/export behavior, mandatory identity fields, sensitive-field deny semantics, cache invalidation, host/tenant scope and how SQL avoids selecting unauthorized sensitive data. Stop implementation if these decisions are not approved.

- [x] **Step 2: Write RED resolver and API tests**

Cover unknown resource/field rejection, cross-tenant grant denial, role union, no-role defaults, super-administrator tenant boundary, export parity and cache invalidation after commit.

- [x] **Step 3: Implement catalog and resolver**

Modules contribute semantic field catalogs through Contracts. Identity stores grants and resolves effective fields; clients consume only effective semantic keys.

- [x] **Step 4: Integrate one real resource first**

Use Host Users as the first vertical consumer. Do not batch-convert all modules until list, detail, export and dual-client behavior pass.

`041_IdentityRoleFieldGrant` 已成对落地。Host Users 列表、详情和导出共享服务端有效字段集合，Vue/Layui 均从语义目录管理角色授权；密码哈希、安全戳和规范化用户名不进入公共目录。验证证据见 [`field-projection-authorization-2026-08-01.md`](../../verification/field-projection-authorization-2026-08-01.md)。

### Task 8: 实现请求签名和开放访问审计

**Files:**
- Create: `docs/superpowers/specs/2026-07-30-request-signature-authentication-design.md`
- Create: `src/Modules/Full.NET.Modules.Identity/Security/SignatureAuthenticationOptions.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Security/SignatureAuthenticationHandler.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Security/SignatureCanonicalRequest.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Persistence/SignatureNonceSql.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/042_IdentitySignatureNonce.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/042_IdentitySignatureNonce.sql`
- Test: `tests/Full.NET.UnitTests/Identity/SignatureCanonicalRequestTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Identity/IdentitySignatureAuthenticationAssertions.cs`

**Interfaces:**
- Canonical input: method, normalized path, sorted canonical query, content SHA-256, access key ID, Unix timestamp and nonce.
- Signature: HMAC-SHA256 with fixed-time comparison.
- Replay boundary: atomic unique `(AccessKeyId, Nonce)` with bounded expiry; cache may accelerate but never replace persistence.

- [x] **Step 1: Approve the wire-contract spec**

Confirm that migration `042` is unused in both providers; otherwise stop and rebase all later paired reservations. Freeze header names, canonicalization, clock-skew range, nonce length, body hashing, key rotation, challenge ProblemDetails and logging redaction.

- [x] **Step 2: Write RED canonicalization and replay tests**

Cover query ordering, percent encoding, empty body, changed content, expired/future timestamp, repeated nonce, rotated/disabled key, tenant mismatch and concurrent replay.

- [x] **Step 3: Implement handler using existing API Key records**

Reuse hashed key identity and permission scope; never store or log the raw secret, client signature, full authorization header or unbounded request body.

- [x] **Step 4: Verify dual database and proxy boundary**

Run SQL Server/MySQL authentication tests plus trusted-proxy, rate-limit, audit redaction and OpenAPI contract checks.

已由 `4bb58ce` 完成并进入 `Build-verified`。2026-08-02 主分支审查另补 Unix 时间戳越界失败关闭；开放访问产品化、请求体上限和损坏 KeyHash 失败关闭仍列入下一波安全硬化，不据此提升为 `Verified`。

### Task 9: 扩展审计分类而不复制敏感正文

**Files:**
- Create: `src/Modules/Full.NET.Modules.Auditing/Contracts/OutboundCallLogContracts.cs`
- Create: `src/Modules/Full.NET.Modules.Auditing/Features/WriteOutboundCallLogs/OutboundCallAuditHandler.cs`
- Create: `src/Modules/Full.NET.Modules.Auditing/Persistence/OutboundCallLogSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/AuditingModule.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/043_AuditingOutboundCall.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/043_AuditingOutboundCall.sql`
- Test: `tests/Full.NET.UnitTests/Auditing/OutboundCallAuditHandlerTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Auditing/AuditingOutboundCallAssertions.cs`

**Interfaces:**
- Records: provider key, operation key, destination host category, status, duration, retry count, trace ID and bounded safe error code.
- Excludes by default: request/response bodies, authorization headers, cookies, tokens, connection strings and raw exception serialization.

- [x] **Step 1: Write RED redaction and truncation tests**

Confirm that migration `043` is unused in both providers; otherwise stop and rebase the reservation. Use payloads containing passwords, bearer tokens, API keys, cookies, connection strings and PII; assert none enter persistence or logs.

- [x] **Step 2: Implement opt-in typed audit handler**

Providers emit structured safe metadata. Do not install a global handler that buffers every request/response body.

- [x] **Step 3: Add retention and query support**

Extend Auditing retention with a separate outbound-call retention setting and bounded pagination.

- [ ] **Step 4: Verify**

Run Auditing Unit/Integration, retention, logging throughput contract and Release build.

`6e156f5` 已完成步骤 1–3。2026-08-02 主分支审查补充 043 双库索引恢复测试和幂等修复；步骤 4 保持未勾选，直到 Docker 可用环境完成新的 SQL Server/MySQL 恢复用例和 affected merge。

### Task 10: 建立只读模块清单，拒绝运行时动态 C#

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Modularity/Modules/FullNetModuleDescriptor.cs`
- Modify: `src/BuildingBlocks/Full.NET.Modularity/Modules/FullNetModuleRegistry.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/QueryHostModuleCatalog/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/Features/QueryHostModuleCatalog/HostModuleCatalogQueryService.cs`
- Test: `tests/Full.NET.UnitTests/Modularity/FullNetModuleRegistryTests.cs`
- Test: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`

**Interfaces:**
- Descriptor fields: stable module key, display name, version, dependencies, host profiles, source classification and health capability.
- Explicitly absent: C# source, assembly bytes, runtime compile, arbitrary load path or dynamic endpoint registration.

- [x] **Step 1: Write RED descriptor and architecture tests**

Reject duplicate module keys, dependency cycles, unknown profiles and descriptors with absolute paths. Add a source scan rejecting production calls to Roslyn runtime compilation or dynamic ApplicationPart mutation outside an approved compatibility project.

- [x] **Step 2: Implement immutable registry snapshot**

Build descriptors from registered modules at composition time. The Host API is read-only and protected by a dedicated permission.

- [x] **Step 3: Verify module topology**

Run Modularity Unit, Architecture, Host API Integration and Release publish dependency scans.

已由 `4962729` 完成并进入 `Build-verified`；运行时动态 C#、动态 ApplicationPart 和任意程序集路径继续保持拒绝。

### Task 11: 为大型插件能力建立独立执行队列

**Files:**
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Create: `docs/verification/adminnet-large-module-execution-queue-2026-08-01.md`
- No module specification is created by this task. Document, Workflow, DataApproval, ImportExport/Reporting and AI/Agents each require a separately dated specification after Gate G4 approval.

**Interfaces:**
- Document consumes Files through an explicit contract and owns category/tag/version/share/permission/log data.
- Workflow owns immutable definition versions, instances, steps, todos, CC, execution logs and recovery.
- DataApproval integrates through explicit use-case contracts, not arbitrary HTTP middleware interception.
- AI uses provider-neutral abstractions and explicit Tool permission/audit contracts.

- [x] **Step 1: Keep every large module at `Mapped` until its spec is approved**

Do not create empty projects, common repositories or speculative Contracts assemblies.

- [x] **Step 2: Activate in dependency order**

Order: Files Provider and field projection → Document; Notifications and Jobs recovery → Workflow; Workflow → DataApproval; ImportExport/Reporting after stable field projection; AI/Agents after permissions, quotas and audit.

- [x] **Step 3: Apply the same vertical-slice exit gate**

Each module must separately provide SQL Server/MySQL migrations and recovery tests, standard API, exact page/action permissions, tenant/data scope, Outbox where applicable, Vue, E2E, operations docs and license evidence. Layui is frozen and must not receive new module work.

### Task 12: 建立 Vue 页面/操作精确授权并清零粗粒度权限

**Files:**
- Create: `docs/superpowers/specs/2026-08-02-vue-action-authorization-design.md`
- Create: `docs/superpowers/plans/2026-08-02-vue-action-authorization.md`
- Create during implementation: `docs/roadmap/admin-action-permission-inventory.md`
- Modify: `rules/client-frontend.md`
- Modify: `rules/development-quality.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`

**Interfaces:**
- Authorization hierarchy: module/navigation page → stable page permission → stable action permissions.
- Client boundary: Vue does not render unauthorized business actions; local-only controls do not enter the permission catalog.
- Server boundary: every management Endpoint binds a known exact permission; bypass requests return `403 authorization.permission_denied`.
- Persistence: roles keep exact codes in `fn_identity_role_permission`; coarse codes are expanded by independently recoverable SQL Server/MySQL migrations per resource.

- [x] **Step 1: Approve the design and Vue-only delivery decision**

The owner approved Vue as the sole active admin delivery track and froze Layui. The detailed design explicitly rejects Admin.NET's URL permission codes, super-administrator bypass and unknown-route allow behavior.

- [x] **Step 2: Execute the Identity Users reference slice**

Follow [`2026-08-02-vue-action-authorization.md`](2026-08-02-vue-action-authorization.md) Tasks 1–9. Do not mark the slice complete until role-tree grants, exact Vue button absence, direct API 403, SQL Server/MySQL migration recovery and Architecture gates are all fresh GREEN.（2026-08-02 已完成；见 [`vue-action-authorization-2026-08-02.md`](../../verification/vue-action-authorization-2026-08-02.md)。）

- [x] **Step 3: Inventory and migrate every active Vue business action**

Follow Task 10 of the detailed plan. Migrate in waves: remaining Identity → Tenancy/Organization → Settings/Auditing → Files/Notifications/Jobs/CodeGeneration → Document and later modules. Each resource owns its exact codes, compatibility expansion and E2E; no broad permission migration may guess high-risk grants across unrelated resources.（W0–W5 已完成代码、精确权限迁移与架构冻结门禁；库存见 [`admin-action-permission-inventory.md`](../../roadmap/admin-action-permission-inventory.md)。program affected merge 尚需完整复跑，因此能力状态保持 `Build-verified`。）

- [ ] **Step 4: Retire Layui from active delivery gates**

Update CI and generator aggregation in an independent focused plan so new feature checks do not select Layui. Preserve the frozen source and historical evidence until a separately approved retirement plan decides archival or deletion.

## Final Verification

- [x] For the active Task 1–10 slice, create the exact snapshot listed in the table above, then pass that same literal name to `pnpm test:integration:affected:plan -- --snapshot` with `--phase merge`; review every selected shard.（各 Task 已在各自快照上完成 merge；本 Task 11 无服务端行为变更。）
- [x] Run the selected affected Integration command; confirm non-zero test discovery for both databases.（Task 1–10 已各自完成；Task 11 文档任务按第 11.1 节不强制 Integration。）
- [x] Run `pnpm test:naming`, `pnpm test:sql-safety`, `pnpm test:governance`, `pnpm test:skills` and affected OpenAPI/client tests.（Task 11 仅文档；执行 governance/skills 作为收口。）
- [x] Run `dotnet build Full.NET.slnx --configuration Release --no-restore`。（Task 11 无代码变更；跳过或确认工作区无未提交代码。）
- [x] Run `git diff --check`, inspect `git status --short` and confirm only the active slice is included.
- [x] Update `eng/testing/test-matrix.json` only when discovered test counts change.（本任务未改测试数量。）
- [x] Update capability status only from fresh evidence; do not promote program-level rows because a child slice passed.（大型模块保持 Mapped；仅同步 Task 8–10 已有证据行。）
- [x] Re-check rule/Skill evolution triggers and license provenance before handoff.

## Stop Conditions

- Stop if a task requires copying Admin.NET.Pro source/assets without recorded MIT-compatible redistribution evidence.
- Stop if a proposed shortcut bypasses Dapper scope guards, trusted tenant context, dual-provider SQL or Migrator-only schema changes.
- Stop if field authorization, dynamic API, online DDL, workflow execution or AI Tool access lacks an approved security specification.
- Stop if a new project has no real consumer or cannot prove the repository's project-split gate.
- Stop if the task snapshot shows overlap with unrelated user changes that cannot be isolated safely.
