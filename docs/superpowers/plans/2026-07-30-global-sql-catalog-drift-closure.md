# Global SQL Catalog Drift Closure Implementation Plan

> **For Codex:** Execute this plan inline in the current shared worktree. Preserve unrelated changes and do not create commits unless the user explicitly requests them.

**Goal:** 关闭 Global SQL 目录架构门禁中的 5 个违规，同时消除 Settings 字典项写入错误使用 Global 作用域的问题。

**Architecture:** Auditing 的 7 种批量写入组合改为可被架构扫描器枚举的静态 `SqlStatement` 声明，并逐条登记精确目录；Identity 的 3 条 Host 目录查询保留 Global 语义并逐条登记；Settings 字典项写入通过租户字典类型表执行 `INSERT ... SELECT`，改为 `TenantRequired` 并由执行器注入当前租户。

**Tech Stack:** .NET 10、C#、Dapper、SQL Server、MySQL、xUnit、JSON 架构契约。

---

## 约束

- 不引入 Global SQL 通配或扫描豁免。
- Auditing 和 Identity 现有 SQL 语义、稳定语句名保持不变。
- Settings 的租户边界必须在 SQL 内成立，并同时兼容 SQL Server 与 MySQL。
- 不修改与本问题无关的代码、规则、Skill 或测试门槛。

### Task 1: 将 Auditing 动态语句构造改为静态声明

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/WriteAuditBatch/AuditWriteBatchSql.cs`
- Modify: `contracts/architecture/global-sql-statements.json`

1. 保留已复现的 `GlobalSqlStatementCatalogTests` 失败作为回归验证。
2. 为 7 种审计写入组合分别声明静态只读 `SqlStatement`。
3. 让 `Get` 仅返回上述静态实例，删除普通方法内的构造。
4. 为 7 个声明添加精确目录项及稳定 SQL 片段。
5. 运行聚焦架构测试，确认仅剩 Identity/Settings 已知漂移。

### Task 2: 登记 Identity Host 目录查询

**Files:**
- Modify: `contracts/architecture/global-sql-statements.json`

1. 为计数、SQL Server 分页、MySQL 分页 3 条语句添加精确目录项。
2. 使用 `host_catalog` 分类并约束 Host、空租户和启用状态片段。
3. 运行聚焦架构测试，确认仅剩 Settings 已知漂移。

### Task 3: 将 Settings 字典项写入收紧为租户语句

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Settings/Persistence/TenantDictItemSql.cs`

1. 将 `VALUES` 写入改为从 `fn_settings_dict_type` 按 `Id` 与 `TenantId` 选择后插入。
2. 将作用域改为 `TenantRequired`，绑定设为 `CurrentTenantId`。
3. 运行聚焦架构测试，要求目录扫描全部通过。
4. 运行 Settings SQL Server/MySQL 受影响集，验证双提供程序行为。

### Task 4: 完成影响集和静态验证

1. 使用任务快照运行 inner/slice 影响集计划与命中测试。
2. 运行 `git diff --check`、相关 Release 构建和架构测试。
3. 检查 `git status`，只报告本任务实际变更与新鲜验证结果。
4. 规则演进与 Skill 演进仅在出现真实缺口时更新；若现有门禁已正确捕获问题，则不扩张治理体系。
