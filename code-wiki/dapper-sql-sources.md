# Dapper SQL 来源与门禁

> 更新时间：2026-08-16。Full.NET 业务持久化默认 **手写 Dapper SQL**；下列三入口不得混用同一语句而不登记。

## 决策表

| 来源 | 何时使用 | 产物位置 | 门禁 |
| --- | --- | --- | --- |
| **手写 SQL** | 模块内 CRUD、查询、事务 | `Features/*Sql.cs`、`Persistence/*Sql.cs` | Architecture scope/binding；`SqlDataScope` 租户过滤 |
| **Global 目录** | 跨模块/Global 语句（极少） | [`contracts/architecture/global-sql-statements.json`](../../contracts/architecture/global-sql-statements.json) | `GlobalSqlStatementCatalogTests` 精确匹配 |
| **CodeGeneration** | Admin.NET 对标 CRUD 批量导入 | `*Sql.g.cs`、迁移模板 | 生成后仍受 Architecture 与 `SqlDataScope` 约束 |

## 手写 SQL（默认）

- 每个模块拥有 `fn_{module}_*` 表；SQL 常量类与 Dapper 参数显式映射 PascalCase 列。
- 禁止 EF Core、通用 Repository、自动 CRUD（见 [`rules/development-quality.md`](../../rules/development-quality.md)）。

## Global 目录

- 仅登记 **真正 Global** 或跨模块只读语句；禁止把模块内 SQL 抄进 catalog 逃避所有权扫描。
- 变更必须同时更新 JSON 与 `GlobalSqlStatementCatalogTests` 期望。

## CodeGeneration

- CLI/Host 预览生成 `backend/*Sql.g.cs`；Apply 后进入模块 Generated 目录。
- **Layui 客户端产物** 默认不生成（`includeLayuiClientArtifacts=false`）；Frozen 维护任务显式启用。
- 生成 SQL 不得使用未登记的 `SqlDataScope.Global`。

## 交叉引用

- [`architecture-overview.md`](../architecture-overview.md) §4 数据访问
- [`naming-conventions.md`](../../rules/naming-conventions.md) 表/列命名
