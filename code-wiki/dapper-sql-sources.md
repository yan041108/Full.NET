# Dapper SQL 来源与门禁

> 更新时间：2026-09-19。Full.NET 业务持久化默认 **手写 Dapper SQL**；下列三入口不得混用同一语句而不登记。

## 决策表

| 来源 | 何时使用 | 产物位置 | 门禁 |
| --- | --- | --- | --- |
| **手写 SQL** | 模块内 CRUD、查询、事务 | `Features/*Sql.cs`、`Persistence/*Sql.cs` | Architecture scope/binding；`SqlDataScope` 租户过滤 |
| **Global 目录** | 实际标为 SqlDataScope.Global 的语句 | [`contracts/architecture/global-sql-statements.json`](file:///G:/wwwroot/github_fork/Full.NET/contracts/architecture/global-sql-statements.json) | [`GlobalSqlStatementCatalogTests`](file:///G:/wwwroot/github_fork/Full.NET/tests/Full.NET.ArchitectureTests/GlobalSqlStatementCatalogTests.cs) 精确匹配 |
| **CodeGeneration** | Admin.NET 对标 CRUD 批量导入 | `*Sql.g.cs`、迁移模板 | 生成后仍受 Architecture 与 `SqlDataScope` 约束 |

## 手写 SQL（默认）

- 每个模块拥有 `fn_{module}_*` 表；SQL 常量类与 Dapper 参数显式映射 PascalCase 列。
- 禁止 EF Core、通用 Repository、自动 CRUD（见 [`rules/development-quality.md`](file:///G:/wwwroot/github_fork/Full.NET/rules/development-quality.md)）。
- 复杂 SQL 靠近 Feature 保存：`src/Modules/Full.NET.Modules.{Module}/Features/{VerbNoun}/Persistence/*Sql.cs`。

## Global 目录

> 文件：[`contracts/architecture/global-sql-statements.json`](file:///G:/wwwroot/github_fork/Full.NET/contracts/architecture/global-sql-statements.json)（约 300 KB；包含 `schemaVersion`、`statements` 与 `categories`）

- 仅登记实际 `SqlDataScope.Global` 语句；跨模块 JOIN 不等于 Global，不能更改 scope 逃避租户或所有权检查。
- 后台组合查询另按 [读取依赖规范](../contracts/architecture/module-read-queries.md) 登记在 `module-read-queries.json`。SQL 集中在 Queries/*Sql.cs，返回 DTO，明确模块/表/列、SQL 摘要和安全说明；业务操作走契约，禁止跨模块写表与事务。
- 允许的 `categories` 在测试硬编码：`cross_context_audit_write`、`reliable_event_sink`、`host_catalog`、`verified_identity`、`tenant_resolution`、`host_tenant_catalog`、`explicit_tenant_anchor`。
- 变更必须同时更新 JSON 与 `GlobalSqlStatementCatalogTests` 期望；测试精确匹配文件路径、行号、`actual` 文本。
- 运行时克隆方法（如 `TenantScopedSqlComposer.ApplyDataScopeFilter`）必须显式登记到测试 `AllowedRuntimeCloneMethods`，禁止目录级或通配豁免。

## CodeGeneration

- CLI/Host 预览生成 `backend/*Sql.g.cs`；Apply 后进入模块 Generated 目录。
- **Layui 客户端产物** 默认不生成（`includeLayuiClientArtifacts=false`）；Frozen 维护任务显式启用。
- 生成 SQL 不得使用未登记的 `SqlDataScope.Global`。

## 租户作用域：SqlDataScope

> 文件：[`src/BuildingBlocks/Full.NET.Data.Abstractions/SqlDataScope.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Abstractions/SqlDataScope.cs)

| Scope | 含义 | 允许的上下文 |
|-------|------|-----------|
| `Global` | 跨租户全局数据，**不允许携带 @TenantId 过滤条件** | Host 级元数据、系统级配置 |
| `TenantRequired` | 必须在租户上下文内执行，SQL 文本须使用 `@TenantId` 谓词或 INSERT 形状 | 业务数据表默认 Scope |
| `HostOnly` | 仅限 Host（超级管理员）上下文访问 | 租户管理、配额配置、全局报表 |

`SqlScopeGuard` 在执行前校验 `SqlDataScope` 与 `SqlTenantBinding` 的合法组合（如 `TenantRequired` 必须搭配 `CurrentTenantId` Binding，否则抛出 `TenantContextMissingException`）；任何越权访问在执行前抛出强类型异常，避免 SQL 文本层面遗漏租户过滤条件。

## SQL 安全门禁

> 文件：[`contracts/sql-safety/README.md`](file:///G:/wwwroot/github_fork/Full.NET/contracts/sql-safety/README.md) + [`contracts/sql-safety/waivers.json`](file:///G:/wwwroot/github_fork/Full.NET/contracts/sql-safety/waivers.json) + [`scripts/sql/validate-sql-safety.mjs`](file:///G:/wwwroot/github_fork/Full.NET/scripts/sql/validate-sql-safety.mjs)

| 规则码 | 含义 |
|--------|------|
| `FNSAFETY001` | `UPDATE`/`DELETE` 缺少 `WHERE` |
| `FNSAFETY002` | `TRUNCATE TABLE` |
| `FNSAFETY003` | `DROP TABLE` / `DROP COLUMN` |
| `FNSAFETY004` | 直接 `RENAME` |

豁免条目（`waivers.json`）必须包含 `ruleId`、`file`、`line`、`actual`、`reason`、`risk`、`reviewer`、`removalMilestone`、`backupVerified: true`；匹配精确到同文件、同行号、同 `actual`，禁止目录级或通配豁免。命名类违规（`SELECT *`、对象命名、迁移配对）仍由 `pnpm test:naming` 负责。

## 相关架构债务目录

> 目录：[`contracts/architecture/`](file:///G:/wwwroot/github_fork/Full.NET/contracts/architecture)

| 文件 | 职责 |
|------|------|
| `global-sql-statements.json` | 实际 Global SQL 语句精确登记 |
| `module-read-queries.json` | 正式后台跨模块只读查询及其表/列依赖登记 |
| `module-table-access-debt.json` | 既有表访问债务；不得用来新增后台查询许可 |
| `module-cross-foreign-key-debt.json` | 跨模块外键债务登记 |
| `module-local-transaction-debt.json` | 跨模块本地事务债务登记 |

## 交叉引用

- [`architecture-overview.md`](./architecture-overview.md) §4 数据访问
- [`naming-conventions-summary.md`](./naming-conventions-summary.md) 表/列命名与 Statement 标识
- [`rules/naming-conventions.md`](file:///G:/wwwroot/github_fork/Full.NET/rules/naming-conventions.md) §6 SQL 与迁移代码风格
- [`rules/development-quality.md`](file:///G:/wwwroot/github_fork/Full.NET/rules/development-quality.md) §5 Dapper、事务与双数据库
