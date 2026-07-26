# Global SQL Statement Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为所有生产代码中的 `SqlDataScope.Global` 语句建立精确、可审查、默认拒绝的机器目录，防止新增 Global SQL、目录漂移或关键行过滤丢失后静默越过租户作用域门禁。

**Architecture:** 目录放在 `contracts/architecture/global-sql-statements.json`，以语句名、声明成员和源码文件三元组唯一定位生产声明；ArchitectureTests 反射生产程序集取得实际 `SqlStatement`，双向比较目录，并逐项验证安全分类、理由和 SQL 必需片段。该门禁只治理 Global 例外，不改变执行器、SQL 文本或运行时租户语义。

**Tech Stack:** .NET 10、C#、MSTest、System.Text.Json、PowerShell、现有 Full.NET ArchitectureTests 生产程序集扫描器。

## Global Constraints

- 保持强化型模块化单体、Dapper-first、SQL Server/MySQL 双提供程序基线不变。
- 不修改任何生产 SQL 行为；本任务只新增架构契约、测试和同步文档。
- 目录不得使用 `*`、`?` 或模糊路径；每条 Global 声明必须逐项登记。
- 每个目录项必须包含稳定分类、中文安全理由和至少一个不可变 SQL 片段。
- ArchitectureTests 必须同时拒绝未登记声明、过期登记、重复登记、通配符、未知分类、空理由和必需 SQL 片段漂移。
- 保留当前工作树全部既有变更，不提交、不清理 `.cache/` 和 `.tmp/art-design-pro/`。

---

## Task 1: 建立可失败的 Global 目录门禁

**Files:**

- Create: `contracts/architecture/global-sql-statements.json`
- Create: `tests/Full.NET.ArchitectureTests/GlobalSqlStatementCatalogTests.cs`

- [x] **Step 1: 创建空目录契约**

目录使用以下根结构，初始 `entries` 为空，以便新测试先暴露全部未登记 Global 声明：

```json
{
  "schemaVersion": 1,
  "entries": []
}
```

- [x] **Step 2: 添加生产目录双向比较测试**

新增 `Production_global_sql_statements_are_exactly_cataloged`，扫描生产程序集中的静态 `SqlStatement` 字段和属性，仅保留 `Scope == SqlDataScope.Global`。声明身份固定为：

```text
{DeclaringType.FullName}.{MemberName}
```

分析器输出确定性排序的违规消息，并执行：

1. 实际声明不存在匹配目录项：`Unregistered global statement`。
2. 目录项不存在匹配实际声明：`Stale global statement catalog entry`。
3. `statementName + declaration + file` 重复：`Duplicate global statement catalog entry`。
4. 身份字段包含 `*` 或 `?`：`Wildcard is not allowed`。
5. `category` 不属于允许集合：`Unknown global statement category`。
6. `reason` 为空：`Security reason is required`。
7. `requiredSqlFragments` 为空或任一片段未在 SQL 中出现：`Required SQL fragment`。
8. 实际 Global 声明必须保持 `SqlTenantBinding.None`。

允许分类固定为：

```text
cross_context_audit_write
reliable_event_sink
host_catalog
verified_identity
tenant_resolution
host_tenant_catalog
explicit_tenant_anchor
```

- [x] **Step 3: 添加分析器负向夹具测试**

新增 `Global_sql_catalog_analyzer_rejects_unregistered_stale_wildcard_duplicate_and_drifted_entries`，使用内存声明和目录项分别证明以下输入被拒绝：

- 新增但未登记的 Global 声明；
- 已删除但仍保留的目录项；
- 文件、声明或语句名中的通配符；
- 重复三元组；
- 未知分类和空安全理由；
- `TenantId IS NULL` 等必需 SQL 片段丢失。

- [x] **Step 4: 运行 RED 验证**

```powershell
dotnet build tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --filter "FullyQualifiedName~GlobalSqlStatementCatalogTests" --minimum-expected-tests 2
```

预期：负向夹具通过，生产目录测试失败并精确列出当前 23 条未登记 Global 声明。

## Task 2: 精确登记当前 23 条 Global Statement

**Files:**

- Modify: `contracts/architecture/global-sql-statements.json`

- [x] **Step 1: 登记跨上下文写入与显式租户锚点**

| Statement | Category | 必需 SQL 片段 |
| --- | --- | --- |
| `auditing.insert_access_log` | `cross_context_audit_write` | `INSERT INTO fn_auditing_access_log`, `@TenantId` |
| `auditing.insert_exception_log` | `cross_context_audit_write` | `INSERT INTO fn_auditing_exception_log`, `@TenantId` |
| `auditing.insert_operation_log` | `cross_context_audit_write` | `INSERT INTO fn_auditing_operation_log`, `@TenantId` |
| `outbox.insert` | `reliable_event_sink` | `INSERT INTO fn_outbox_message`, `@TenantId` |
| `organization.find_active_unit_by_tenant_and_id` | `explicit_tenant_anchor` | `Id = @UnitId`, `TenantId = @TenantId`, `IsActive = 1` |

- [x] **Step 2: 登记 Host 目录和可信身份锚点**

| Statement | Category | 必需 SQL 片段 |
| --- | --- | --- |
| `identity.find_api_key_for_authentication` | `host_catalog` | `FROM fn_identity_api_key`, `ScopeKey = 'host'`, `TenantId IS NULL` |
| `identity.find_host_user_by_id` | `host_catalog` | `Id = @UserId`, `ScopeKey = 'host'`, `TenantId IS NULL` |
| `identity.get_user_active_role_data_scopes` | `host_catalog` | `roleObject.ScopeKey = 'host'`, `roleObject.TenantId IS NULL` |
| `identity.list_active_host_menus` | `host_catalog` | `ScopeKey = 'host'`, `TenantId IS NULL`, `IsActive = 1` |
| `identity.list_host_users_by_ids` | `host_catalog` | `Id IN @UserIds`, `ScopeKey = 'host'`, `TenantId IS NULL` |
| `identity.touch_api_key_last_used` | `host_catalog` | `UPDATE fn_identity_api_key`, `ScopeKey = 'host'`, `TenantId IS NULL` |
| `identity.find_profile_by_verified_identity` | `verified_identity` | `Id = @UserId`, `ScopeKey = @ScopeKey` |
| `identity.find_refresh_session_by_explicit_session_id` | `verified_identity` | `session.Id = @SessionId`, `JOIN fn_identity_user` |
| `identity.get_actor_authorization` | `verified_identity` | `userObject.Id = @UserId`, `roleObject.ScopeKey = @ScopeKey` |
| `identity.insert_explicit_context_audit` | `cross_context_audit_write` | `INSERT INTO fn_identity_context_audit`, `@TenantId` |
| `identity.update_locale_preference_by_verified_identity` | `verified_identity` | `Id = @UserId`, `SecurityStamp = @SecurityStamp`, `session.Id = @SessionId` |
| `identity.update_refresh_session_explicit_context` | `verified_identity` | `Id = @SessionId`, `UserId = @UserId`, `Version = @Version` |

- [x] **Step 3: 登记租户解析与 Host 租户目录**

| Statement | Category | 必需 SQL 片段 |
| --- | --- | --- |
| `tenancy.count_by_domain` | `tenant_resolution` | `FROM fn_tenancy_tenant`, `LOWER(Domain) = LOWER(@Domain)` |
| `tenancy.find_by_domain` | `tenant_resolution` | `FROM fn_tenancy_tenant`, `LOWER(Domain) = LOWER(@Domain)`, `IsActive = 1` |
| `tenancy.find_by_identifier` | `tenant_resolution` | `FROM fn_tenancy_tenant`, `LOWER(Identifier) = LOWER(@Identifier)`, `IsActive = 1` |
| `tenancy.tenant.find_summary_by_identifier` | `tenant_resolution` | `FROM fn_tenancy_tenant`, `LOWER(Identifier) = LOWER(@Identifier)` |
| `tenancy.find_by_explicit_id` | `host_tenant_catalog` | `Id = @TenantId` |
| `tenancy.get_available_for_host_administrator` | `host_tenant_catalog` | `FROM fn_tenancy_tenant`, `WHERE IsActive = 1` |

- [x] **Step 4: 运行 GREEN 验证**

```powershell
dotnet build tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --filter "FullyQualifiedName~GlobalSqlStatementCatalogTests" --minimum-expected-tests 2
```

预期：2/2 通过；目录与 23 条生产 Global 声明精确一致。

## Task 3: 同步权威规则、架构状态和测试门槛

**Files:**

- Modify: `rules/development-quality.md`
- Modify: `docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `README.md`
- Modify: `docs/development/getting-started.md`
- Modify: `.github/workflows/ci.yml`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`
- Create: `docs/verification/global-sql-statement-catalog-2026-07-26.md`

- [x] **Step 1: 更新 Dapper Global 例外规则**

在现有 Dapper 边界规则中明确：任何 `SqlDataScope.Global` 生产声明必须逐条登记目录；目录身份精确匹配声明、文件和 Statement Name；安全理由与关键 SQL 行约束为强制项；禁止通配符和批量豁免。

- [x] **Step 2: 更新架构 Spec 和路线图**

将 TenantRequired 语义绑定与 Global 精确目录记录为已落地门禁；后续 SqlBuilder 仍只在出现真实动态组合消费者且通过 Dapper 工具边界评审后引入。

- [x] **Step 3: 将 ArchitectureTests 最低阈值从 44 更新为 46**

仅更新当前权威入口及阈值审计记录，不回写历史验证记录中的历史数量。

- [x] **Step 4: 写入验证记录**

记录目录项数量、分类、RED 证据、最终命令、实际通过数量、双库验证适用性和残余风险。

## Task 4: 完整验证、规则复盘与独立复核

**Files:**

- Modify only if thresholds are met: `rules/*`, `.agents/skills/fullnet-module-delivery/*`

- [x] **Step 1: 运行完整静态与测试验证**

```powershell
dotnet build Full.NET.slnx -c Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 366
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 7
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 46
dotnet tests/Full.NET.NamingTests/bin/Release/net10.0/Full.NET.NamingTests.dll --no-ansi --progress off --minimum-expected-tests 23
pnpm test:skills
pnpm test:governance
git diff --check
git status --short
```

预期：构建 0 warning/0 error；Unit 366、Compatibility 7、Architecture 46、Naming 23、Skill 52 均零失败；governance 和 diff 检查通过。

本任务不修改生产 SQL、迁移或运行时行为，因此不重复执行 SQL Server/MySQL Integration 套件；沿用本轮前置新鲜双库 Integration 172/172 与焦点数据库 12/12 证据，并在验证记录中明确该适用性判断。

- [x] **Step 2: 执行规则和 Skill 演进复盘**

按 `rules/rule-evolution.md` 和 `rules/skill-evolution.md` 判断是否满足升级门槛；若没有重复遗漏模式或可复用新流程，则记录“无需新增规则/Skill”，避免建立近义规则。

- [x] **Step 3: 执行架构级独立代码复核**

使用 `superpowers:requesting-code-review` 检查 Critical、Important、Minor 问题；修复所有 Critical/Important 后重跑受影响测试，最终报告剩余问题数量。
