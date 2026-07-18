# Full.NET Dapper 辅助能力实施计划

> **For Codex:** REQUIRED SUB-SKILL: Use `fullnet-module-delivery` and test-driven development for every data-path change. Keep Dapper and ADO.NET objects inside the infrastructure boundary.

**Goal:** 在不引入隐藏 ORM、通用 Repository 或事务旁路的前提下，增加原生多结果集读取和受控动态 SQL 构建能力，并把被拒绝的扩展包变成自动门禁。

**Architecture:** `QueryMultiple` 由 `Full.NET.Data.Abstractions` 的自有接口暴露并由现有 Executor 执行；`Dapper.SqlBuilder` 只存在于独立查询构建实现层，业务 Feature 消费构建后的 `SqlStatement`。Provider 和事务继续使用 Full.NET 自有抽象。

**Tech Stack:** .NET 10、Dapper 2.1.79、Dapper.SqlBuilder 2.1.66、SQL Server 2022、MySQL 8、MSTest、Testcontainers、BenchmarkDotNet。

---

### Task 1: 原生 QueryMultiple 自有抽象

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/IMultiResultQueryExecutor.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/IMultiResultReader.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/DapperSqlExecutor.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/ServiceCollectionExtensions.cs`
- Create: `tests/Full.NET.UnitTests/Data/MultiResultQueryExecutorTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Data/MultiResultQueryTests.cs`

1. 先建立失败测试：顺序读取、空结果、投影异常、取消、超时、租户注入和 Reader 释放。
2. `IMultiResultQueryExecutor.QueryMultipleAsync` 接受 `SqlStatement`、参数和 projector；`IMultiResultReader` 只提供受控的单行/列表顺序读取。
3. `GridReader`、`DbConnection`、`DbTransaction` 均不出 `Full.NET.Data.Dapper`；projector 结束或异常时确定释放。
4. 复用现有 `DbSession`、事务、`SqlScopeGuard`、超时、取消和慢 SQL 日志，不建立第二条执行链。
5. SQL Server/MySQL 验证详情＋子集合、分页 Items＋Total 和后续普通查询可继续复用连接。

### Task 2: 首个真实列表出现时引入 SqlBuilder 封装

**Execution gate:** 只有已批准的真实列表 Feature 同时具有两个以上可选条件或可选 JOIN/排序片段时执行本任务。门禁未命中时不得为了“以后可能用”增加包依赖。

**Files:**
- Modify: `Directory.Packages.props`
- Create: `src/BuildingBlocks/Full.NET.Data.QueryBuilding/Full.NET.Data.QueryBuilding.csproj`
- Create: `src/BuildingBlocks/Full.NET.Data.QueryBuilding/SqlQueryBuilder.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.QueryBuilding/BuiltSqlStatement.cs`
- Modify: `Full.NET.slnx`
- Modify: `THIRD-PARTY-NOTICES`
- Create: `tests/Full.NET.UnitTests/Data/SqlQueryBuilderTests.cs`
- Test: 首个真实列表 Feature 的 Unit 与 SQL Server/MySQL Integration 测试

1. 先用真实筛选契约建立失败测试：可选值参数化、空筛选、稳定排序、分页、危险排序列和恶意片段拒绝。
2. 固定 `Dapper.SqlBuilder` 2.1.66；封装层只接受代码定义的模板/片段、参数对象和枚举到标识符的白名单映射。
3. 请求输入绝不能成为列名、运算符、JOIN、WHERE 或 ORDER BY 片段；SqlBuilder 不是任意字符串净化器。
4. 输出只能是 Full.NET `SqlStatement`/`BuiltSqlStatement`，随后仍经统一 Executor 和租户守卫执行。
5. 不向 Handler 暴露 `SqlBuilder`，不建立表达式树翻译、通用 CRUD 或通用 Repository。
6. 更新 Apache-2.0 Notice、依赖锁定和许可证扫描；不得使用 Dapper Logo 暗示背书。

### Task 3: Provider 语义和事务边界门禁

**Files:**
- Modify: `docs/development/sql-portability.md`
- Modify: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`
- Modify: `.github/workflows/ci.yml`
- Create: `tests/Full.NET.UnitTests/Data/DataDependencyPolicyTests.cs`

1. 先写失败夹具，阻止业务模块引用 `Dapper`、`Dapper.SqlBuilder`、`Dapper.ProviderTools`、`Dapper.Transaction`、Rainbow、Contrib、FluentMap、Dommel 和其他自动 CRUD 包。
2. 保留 `DatabaseProvider + ISqlDialect + 成对语义 Statement`；Provider 分支只能在数据实现、迁移或模块 Persistence 边界。
3. Handler 不接触 `IDbConnection`/`IDbTransaction`；事务继续由 `ICommandTransaction + DbSession` 统一覆盖业务写入与 Outbox。
4. CTE、窗口、Upsert、锁、JSON 和日期函数按 SQL Server/MySQL 成对实现、等价语义与真实测试准入；不引入 ProviderTools 代替方言治理。
5. 强命名仅在所有发布程序集及依赖链均启用时通过 ADR 将 Dapper 替换为 StrongName，禁止两个包共存。

### Task 4: 批量写入决策门禁

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Data/BatchWriteBenchmarks.cs`
- Modify: `docs/development/sql-portability.md`

1. 小中批量先基准 Dapper Core 对参数集合的 `Execute`，显式限制单批行数和参数总量。
2. 只有真实数据规模未达 SLA 时设计 `IBulkWriter`；SQL Server 使用 `SqlBulkCopy`，MySQL 使用 `MySqlBulkCopy`，两者保持事务、租户、取消与审计语义。
3. 商业 Dapper Plus 不进入 Full.NET MIT 默认发布物；具体项目采购时只能作为隔离 Provider，并独立完成许可证审计。
4. 基准记录数据量、列宽、网络、事务、数据库版本、执行计划与内存分配，不以微型内存循环替代真实数据库结论。

### Task 5: 回归与状态更新

1. 运行 Release build、Unit、Architecture、SQL Server/MySQL Integration 和适用基准烟测。
2. 核对业务程序集依赖图中不存在被拒绝包，SqlBuilder 仅在门禁命中后出现于专用实现层。
3. 更新能力矩阵、总体架构、SQL 可移植性文档、Notice 和验证记录；未实现时保持 `Designing`。
4. 执行 `git diff --check`、规则/Skill 复盘和分支状态检查。

## 完成标准

- 多结果集读取不泄漏 Dapper/ADO.NET 生命周期，并通过双库真实测试。
- 动态 SQL 只由真实消费者驱动，用户值参数化，标识符来自封闭白名单。
- ProviderTools、Dapper.Transaction 和自动 CRUD 包被架构/依赖门禁拒绝。
- 批量能力只依据真实基准按 Provider 引入，不污染默认 MIT 核心。

设计依据：[`../specs/2026-07-18-dapper-tooling-design.md`](../specs/2026-07-18-dapper-tooling-design.md)。
