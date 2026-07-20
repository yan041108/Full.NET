# Pre-v1 Naming Normalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Full.NET 1.0 前用可恢复的双数据库迁移规范化现有 Tenancy、Outbox 和稳定机器代码命名，同时保证存量数据、待处理消息和旧客户端不会被静默破坏。

**前置条件状态（2026-07-19）**：`main` 基线 `55e82d4` 已完成 UUID 008/009 与 Task 5–7 自动化证据；010/011 编号已在 `UuidStorageContractV1` 冻结，可启动本计划实施（仍须独立维护窗口与验证记录）。

**Architecture:** 数据库采用 `expand -> application switch -> contract`：先新增规范表/列并回填，在受控维护窗口切换应用，再延后删除旧对象。公共错误码通过明确破坏性版本说明和兼容映射迁移；Outbox 旧 MessageType 保留并行 Handler，直到旧消息排空和退役窗口结束。本计划不修改 001-009 历史迁移，并以 UUID Binary16 008/009 已完成为前置条件。

**Tech Stack:** .NET 10、Dapper、DbUp、SQL Server、MySQL、Testcontainers、MessagePack Outbox、MSTest/Microsoft Testing Platform、Vue/Layui 契约测试。

## Global Constraints

- 必须先完成 `2026-07-18-naming-governance.md` 的 Naming Profile、精确债务清单和自动化门禁。
- 必须先完成 `2026-07-18-uuid-v7-primary-key-storage.md` 的 008/009 迁移和 Binary16 应用切换；本计划固定使用 010/011，禁止与主键或已实现的 Seed 007 交换编号。
- 生产/持久化数据库禁止直接 Rename/Drop；所有步骤可在迁移未记账且前序 DDL 已部分完成时安全重跑。
- SQL Server/MySQL 必须具有相同目标表、列、约束和索引名称，并分别执行真实恢复测试。
- 迁移切换期间 API、Worker 和 Migrator 的顺序必须明确；Outbox 未排空时禁止移除旧消息路由。
- 公共错误码的旧值不能被数据库别名解决；需要客户端兼容、资源键和发布说明共同处理。
- 删除旧对象必须使用有到期版本的数据变更豁免，包含备份、行数/摘要核对、前滚与独立数据审查。

---

### Task 1: 冻结现状、目标映射和维护窗口门禁

**Files:**
- Modify: `contracts/naming/naming-debt.json`
- Create: `contracts/naming/pre-v1-name-map.json`
- Create: `tests/naming/pre-v1-name-map.test.mjs`
- Create: `docs/development/pre-v1-naming-migration-runbook.md`
- Modify: `package.json`

**Interfaces:**
- Consumes: 当前 001-009 双库迁移、ErrorCodes、Audit/Statement IDs 和 Outbox MessageType
- Produces: 机器可读 `PreV1NameMapV1` 与停止写入、备份、迁移、验证、回退步骤

- [x] **Step 1: 写失败的映射完整性测试**

测试扫描当前 SQL 和代码，要求每个已登记债务有唯一目标且目标通过 Naming Profile；数据库映射必须说明 `expandName`、`switchRelease`、`contractRelease`，协议映射必须说明 `legacyValue`、`canonicalValue`、`compatibilityMode`。

- [x] **Step 2: 建立精确映射**

至少包含：

```text
fn_tenant_tenant -> fn_tenancy_tenant
fn_tenant_tenant.CreatedAt -> fn_tenancy_tenant.CreatedAtUtc
fn_tenant_tenant.UpdatedAt -> fn_tenancy_tenant.UpdatedAtUtc
fn_outbox_message.Type -> MessageType
fn_outbox_message.OccurredAt -> OccurredAtUtc
fn_outbox_message.ProcessedAt -> ProcessedAtUtc
fn_outbox_message.NextAttemptAt -> NextAttemptAtUtc
fn_outbox_message.LockedUntil -> LockedUntilUtc
fullnet.tenancy.tenant-provisioned -> fullnet.tenancy.tenant.provisioned
```

错误、Audit 和 Statement 值只把同一语义段内连字符替换为下划线，例如 `identity.bootstrap.invalid-password -> identity.bootstrap.invalid_password`、`tenancy.domain-exists -> tenancy.domain_exists`、`identity.login-succeeded -> identity.login_succeeded`、`outbox.acquire.sql-server -> outbox.acquire.sql_server`。

- [x] **Step 3: 编写维护窗口 Runbook**

Runbook 明确：停止 API/Worker；等待当前事务；记录未处理 Outbox 数量；执行备份；运行 Expand；核对行数/关键字段摘要；部署新 API/Worker；执行双端冒烟；保留旧对象；失败时停止新应用并回退旧版本。没有满足全部前置条件时迁移必须停止。

- [x] **Step 4: 运行映射测试**

Run: `pnpm test:naming`

Expected: PASS；不存在一个旧值映射到多个目标、目标超过 64 字符或未登记扫描结果。

- [x] **Step 5: 提交迁移合同**

```bash
git add contracts/naming tests/naming docs/development/pre-v1-naming-migration-runbook.md package.json
git commit -m "docs: freeze pre-v1 naming migration map"
```

### Task 2: 为 Tenancy 和 Outbox 建立双库 Expand 迁移

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/010_NamingExpand.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/010_NamingExpand.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/NamingExpandMigrationTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Migrations/NamingPartialRecoveryTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj`

**Interfaces:**
- Consumes: 001-009 当前结构与 `PreV1NameMapV1`
- Produces: 已回填的 `fn_tenancy_tenant` 和 Outbox 规范镜像列；旧表/列仍保留

- [x] **Step 1: 先写 SQL Server/MySQL 失败测试**

测试从 009 状态写入多个 Tenant 和处于 Pending/Processed/Retry/Locked 的 Outbox 行，执行 010 后断言新旧行数、Id、二进制 Payload、MessageType、四个 UTC 时间和 NULL 状态完全一致。分别模拟：新表已创建但未复制、只增加部分列、部分行已回填、迁移未记账，再次运行必须收敛。

- [x] **Step 2: 运行并确认 010 缺失而失败**

Run:

```powershell
dotnet build Full.NET.slnx -c Release
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --filter NamingExpand --minimum-expected-tests 1 --timeout 10m
```

Expected: FAIL，指出目标迁移或规范对象不存在。

- [x] **Step 3: 实现可重入 Expand**

010 创建 `fn_tenancy_tenant`，使用显式 `PK_/UX_/DF_` 名称和 `CreatedAtUtc/UpdatedAtUtc`；按 Id 幂等复制旧 Tenant。Outbox 在原表新增可空 `MessageType/OccurredAtUtc/ProcessedAtUtc/NextAttemptAtUtc/LockedUntilUtc` 并分批回填；每个 DDL/回填步骤先探测真实结构和行状态，不能只依赖 DbUp Journal。两库 UUID 列沿用 ADR-0003 的目标类型：SQL Server `uniqueidentifier`，MySQL `BINARY(16)`。

- [x] **Step 4: 增加数据冲突和超时保护**

若同一 Id 的新旧 Tenant 关键字段不同，或 Outbox 新旧列同时非空但值不同，迁移必须失败并输出行 Id/计数，不覆盖目标。日志不得输出 Payload、Token 或完整错误正文。大表回填使用可配置批次和确定排序，Runbook 记录预计锁时间。

- [x] **Step 5: 运行双库迁移与部分恢复测试**

先实现并固定 8 项测试：两种数据库分别覆盖完整 Expand、建表后恢复、部分列恢复和部分数据恢复。Run:

```powershell
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --filter "NamingExpand|NamingPartialRecovery" --minimum-expected-tests 8 --timeout 10m
```

Expected: SQL Server/MySQL 8 项全部 PASS；若拆分出更多用例，必须同步提高门槛，禁止降低到 8 以下。

- [ ] **Step 6: 提交 Expand 迁移**

```bash
git add src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations tests/Full.NET.IntegrationTests
git commit -m "feat: expand canonical database names"
```

### Task 3: 切换应用 SQL、领域模型和 Outbox 路由

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Domain/Tenant.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Features/ProvisionTenant/Handler.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantResolver.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxWriter.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/OutboxSql.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/OutboxMessage.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenantProvisionedCacheInvalidationHandler.cs`
- Modify: `tests/Full.NET.UnitTests/`
- Modify: `tests/Full.NET.IntegrationTests/`

**Interfaces:**
- Consumes: 010 Expand 后的新表/列
- Produces: 新写入只使用规范数据库名称；Worker 同时消费 legacy/canonical MessageType

- [x] **Step 1: 先写应用切换失败测试**

覆盖 Tenant 创建/解析/查询只访问 `fn_tenancy_tenant`；Outbox 新写入填充 `MessageType` 和 `...Utc` 列；Worker 对 `fullnet.tenancy.tenant-provisioned` 与 `fullnet.tenancy.tenant.provisioned` 都路由到同一幂等处理语义；未知类型仍失败/进入既定重试路径。

- [x] **Step 2: 运行聚焦测试并确认仍访问旧名称而失败**

Run: 使用 README 的 Release Build 后 Unit/Integration Test 命令，并通过 `--filter "Tenancy|Outbox|Naming"` 聚焦。

Expected: FAIL，断言 SQL 或新列尚未切换。

- [x] **Step 3: 切换 Tenancy 与 Outbox**

Tenant 领域属性统一为 `CreatedAtUtc/UpdatedAtUtc`；所有静态 SQL 使用新表和规范列。Outbox Row/Envelope 使用 `MessageType` 和规范 UTC 属性；旧数据库列只由迁移兼容逻辑读取，业务路径不得继续双写无期限债务。

- [x] **Step 4: 添加旧消息类型别名路由**

Handler 声明一个 Canonical MessageType 和显式 Legacy Aliases，启动时仍验证 `(MessageType, SchemaVersion)` 唯一。两种类型反序列化同一 MessagePack Schema，幂等键继续使用 MessageId，不能重新发布造成重复副作用。

- [x] **Step 5: 运行双库、Worker 和缓存失效验证**

Run: README 中 Unit、Architecture、Integration 的完整 Release 命令及 `pnpm test:naming`。

Expected: 全部 PASS；新写入旧列不再变化，旧 Pending 消息仍被成功处理。

- [x] **Step 6: 提交应用切换**

```bash
git add src tests
git commit -m "refactor: switch to canonical persisted names"
```

### Task 4: 规范错误码、Audit 和 Statement 标识

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/Contracts/IdentityErrorCodes.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Contracts/TenancyErrorCodes.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Resources/`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Resources/`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/`
- Modify: `src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantSql.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/OutboxSql.cs`
- Modify: `packages/admin-i18n/`
- Modify: `ui/admin/`
- Modify: `ui/admin-layui/`
- Modify: `clients/uniapp/`
- Modify: `tests/`
- Create: `docs/development/pre-v1-contract-name-migration.md`

**Interfaces:**
- Consumes: `PreV1NameMapV1` 中 protocol 类映射
- Produces: 新响应/审计/Statement 使用 canonical 值；兼容层和发布说明记录 legacy 值

- [x] **Step 1: 先写协议兼容失败测试**

从 ErrorCodes、资源源、Admin.NET Mapper、Vue/Layui/uni-app 回退目录枚举值，断言 canonical 值存在且 legacy 值只出现在映射/兼容夹具。Audit 与 Statement 属内部可观测契约，断言新写入只使用 canonical 值，历史查询可同时筛选旧/新值。

- [x] **Step 2: 按映射表执行最小规范化**

只把连字符替换为同一语义段的下划线，不趁机重写层级或文案。例如 `tenancy.domain-exists` 变为 `tenancy.domain_exists`，避免一次迁移同时改变 casing 和领域含义。

- [x] **Step 3: 同步资源、多客户端和兼容说明**

ProblemDetails 新响应返回 canonical code；兼容适配器可在明确的 Pre-v1 Legacy Profile 下映射旧 code。双管理端和 uni-app 同时识别迁移期旧/新值；文档列出破坏性变化和移除旧兼容的版本，禁止永久双码。

- [x] **Step 4: 运行协议、本地化和客户端测试**

Run:

```powershell
dotnet build Full.NET.slnx -c Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --filter "Naming|ErrorCode|Audit|Statement" --minimum-expected-tests 8
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --filter "Naming|ErrorCode|ProblemDetails" --minimum-expected-tests 4
pnpm test:localization
pnpm test:clients
pnpm test:naming
```

Expected: 上述新增聚焦用例全部 PASS，legacy code 不再由标准 API 新产生；随后运行 README 中 Unit 与 Compatibility 的最新全量命令并按新鲜发现数量更新门槛。

- [x] **Step 5: 提交协议规范化**

```bash
git add src packages ui clients tests docs/development/pre-v1-contract-name-migration.md
git commit -m "refactor: normalize pre-v1 contract names"
```

### Task 5: 收紧非空约束并完成 Contract 清理

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/011_NamingContract.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/011_NamingContract.sql`
- Create: `tests/Full.NET.IntegrationTests/Migrations/NamingContractMigrationTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Migrations/NamingContractPartialRecoveryTests.cs`
- Modify: `contracts/naming/naming-debt.json`
- Modify: `contracts/naming/pre-v1-name-map.json`
- Modify: `docs/development/pre-v1-naming-migration-runbook.md`

**Interfaces:**
- Consumes: 已部署并验证的新应用版本、旧 Outbox 排空证据、Expand 数据核对结果
- Produces: 规范对象成为唯一写入结构；到期债务从 Allowlist 移除

- [x] **Step 1: 写 Contract 前置条件失败测试**

只要新列有 NULL、旧新值冲突、旧表行数与新表不一致、存在 Legacy Pending Outbox 或数据库记录的应用兼容版本未达到门槛，011 必须拒绝执行。测试覆盖迁移未记账但部分旧对象已删除的恢复场景。

- [x] **Step 2: 实现先收紧、后删除的 Contract**

先把规范 Outbox 列收紧为正确 NULL/NOT NULL 语义并重建规范索引；再在独立、带豁免的步骤删除旧列和 `fn_tenant_tenant`。SQL Server/MySQL 每步探测结构并验证数据，删除操作引用具体批准的豁免 ID 和到期版本。

- [ ] **Step 3: 运行全新、升级和半完成双库矩阵**

矩阵至少包含：空数据库 001→011、009 存量数据→011、010 后新应用写入→011、Legacy Pending 消息拒绝、011 部分完成重跑。所有场景验证 Tenant/Outbox 行数、Payload SHA-256、UUID Binary16 往返、索引和约束名称。

- [x] **Step 4: 清除已完成债务并验证没有扩大豁免**

仅删除已有真实证据的债务项；协议兼容窗口未结束的条目继续保留。`pnpm test:naming` 必须在删除每个条目后仍通过，不能添加新通配项抵消失败。

- [ ] **Step 5: 运行完整发布门禁**

Run: README 中全部 Release 构建、Unit、Compatibility、Architecture、双库 Integration、客户端、本地化、E2E、依赖审计和 `git diff --check` 命令。

Expected: 全部自动化 PASS；维护窗口中的备份恢复、行数/摘要和人工数据库审查记录进入验证文档。未执行人工项时不得标记 `Verified`。

- [x] **Step 6: 提交 Contract 迁移**

```bash
git add src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations tests/Full.NET.IntegrationTests contracts/naming docs/development/pre-v1-naming-migration-runbook.md
git commit -m "feat: contract legacy persisted names"
```

### Task 6: 关闭迁移并更新公开状态

**Files:**
- Create: `docs/verification/pre-v1-naming-normalization.md`
- Modify: `README.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md`
- Modify: `docs/development/getting-started.md`
- Modify: `contracts/naming/naming-debt.json`

**Interfaces:**
- Consumes: Tasks 1-5 的迁移、兼容、双库、客户端和人工证据
- Produces: 可审计的命名治理最终状态和 legacy 退役日期

- [ ] **Step 1: 记录每个旧名的最终状态**

验证文档逐项记录：旧值、规范值、首次兼容版本、停止产生旧值版本、最后接受旧值版本、数据库迁移脚本、行数/摘要、旧 Outbox 排空时间和回退演练结果。

- [ ] **Step 2: 执行发布候选升级演练**

从上一发布版本的 SQL Server/MySQL 备份恢复，按 Runbook 升级到候选版本，再回放登录、租户切换、Tenant 创建、Outbox 重试/消费和 Vue/Layui/uni-app 错误展示。不能只验证全新数据库。

- [ ] **Step 3: 更新状态**

Profile/Lint/生成器通过但旧对象尚在时保持 `Implemented`；旧数据库对象和到期公共别名完成清理、双库升级/恢复及客户端验证后才标记 `Verified`。

- [ ] **Step 4: 最终提交**

```bash
git add README.md docs contracts/naming
git commit -m "docs: verify pre-v1 naming normalization"
```
