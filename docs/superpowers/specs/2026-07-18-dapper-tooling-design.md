# Full.NET Dapper 辅助能力设计

- 日期：2026-07-18
- 状态：已批准
- 决策来源：项目所有者要求评估 Dapper.SqlBuilder、Dapper.ProviderTools、Dapper.Transaction、QueryMultiple、批量与通用 CRUD 扩展
- 实现状态：QueryMultiple 自有抽象、统一执行器、完整消费门禁和 SQL Server/MySQL 真实测试已实现；SqlBuilder 仍等待首个真实动态列表消费者命中门禁

## 1. 基线

Full.NET 当前使用 Dapper 2.1.79，业务模块只依赖 `Full.NET.Data.Abstractions`，由 `DapperSqlExecutor` 统一处理连接、事务、租户 SQL 作用域、超时、取消和日志。`DapperCommandTransaction + DbSession` 已提供命令事务，并保证业务写入与 Outbox 使用同一连接和事务。

辅助组件只有在不破坏以下边界时才有价值：显式 SQL、SQL Server/MySQL 双库、无通用 Repository、无隐藏 CRUD、所有执行仍经过 Full.NET Executor。

## 2. 评估矩阵

| 能力 | 来源/许可 | 结论 | Full.NET 边界 |
| --- | --- | --- | --- |
| Dapper.SqlBuilder | DapperLib 官方包，Apache-2.0；当前稳定版 2.1.66 | **批准但尚未命中引入门禁** | 仅由 Full.NET 数据查询构建层封装；只接受代码定义的片段与参数，用于列表/报表动态条件 |
| Dapper Core `QueryMultiple` | Dapper Core，Apache-2.0；当前已依赖 | **已实现 / Build-verified** | 通过 `IMultiResultQueryExecutor` 暴露，不把 `GridReader`、连接或裸 Dapper 泄漏给业务层 |
| Dapper.ProviderTools | DapperLib 旧包，Apache-2.0；最后发布 2.0.90（2021-04-29） | **不引入** | 它只是 Provider-agnostic ADO.NET 辅助工具，不能替代 SQL 方言和语义 Statement；继续使用 Full.NET 自有抽象 |
| Dapper.Transaction | ZZZ Projects，MIT；当前包 2.1.79 | **不引入** | 与 `ICommandTransaction + DbSession` 重复；直接扩展 `IDbTransaction` 会鼓励绕过 Executor、作用域和 Outbox 边界 |
| Dapper.Rainbow | DapperLib 官方包，Apache-2.0 | **不引入** | 自动单表 CRUD、类型/表约定与代码生成器和 `{owner}_{module}_{entity}` 命名边界重叠 |
| Dapper.StrongName | DapperLib 官方替代包，Apache-2.0 | **Decision Gate** | 只有 Full.NET 发布物整体启用强命名且依赖链验证通过时替换 Dapper；不能同时引用两个版本 |
| Dapper.FluentMap | 社区扩展 | **默认不引入** | 自有表使用 PascalCase 直接映射；历史库只允许 Compatibility/Provider 层显式适配 |
| Dapper.Contrib / SimpleCRUD / FastCrud / Dommel 等 | 官方社区或第三方 | **禁止默认引入** | 自动生成 SQL、特性 CRUD 或通用 Repository 与显式 SQL 治理冲突 |
| Dapper Plus / Z.Dapper.Plus | 商业产品 | **不进入 MIT 默认发布物** | 只有具体项目独立采购并通过许可证门禁后作为项目 Provider 使用 |

Dapper 与 Dapper.SqlBuilder 是 Apache-2.0 依赖，不会把 Full.NET 自有代码许可证改成 Apache-2.0，但发布物必须保留 `THIRD-PARTY-NOTICES`、许可证文本和依赖审计。禁止把 Dapper Logo 用作 Full.NET 包图标或暗示官方背书。

## 3. Dapper.SqlBuilder 的正确定位

SqlBuilder 解决“从一组受信任 SQL 片段中选择并组合”的样板代码，不解决任意字符串安全。`Where("Name = @Name", new { Name = input })` 的参数值安全；如果把请求提供的列名、运算符或完整条件直接放进 Clause，仍然会注入。

因此采用以下边界：

1. 新建 `Full.NET.Data.QueryBuilding`，内部依赖 `Dapper.SqlBuilder`，向模块暴露 Full.NET 自有 `SqlQueryBuilder` 和 `BuiltSqlStatement`；
2. 只有模块 `Persistence`、代码生成器和数据基础设施可引用该 BuildingBlock；Handler/Endpoint 禁止直接引用；
3. 基础模板、WHERE/JOIN/GROUP/HAVING/SET 片段必须是源代码或生成器产生的静态内容；排序、字段和运算符必须先映射到封闭白名单；
4. 参数值通过对象/DynamicParameters 合并，禁止字符串插值、拼接引号或把用户输入写入模板；
5. Build 结果仍是 `SqlStatement + Parameters`，必须交给 `IQueryExecutor`，不能直接调用连接；
6. `TenantRequired` 模板必须在最终 SQL 中保留 `@TenantId`，SqlScopeGuard 在 Build 后再次验证；
7. 模板 Marker 缺失、Clause 未消费、重复参数冲突和空排序必须失败，不静默生成退化 SQL；
8. SQL Server/MySQL 共有条件可复用；分页、JSON、锁、Upsert 等差异继续交给 `ISqlDialect`/Provider Statement，不能塞入条件 Builder 隐藏分支。

首个消费者应是 Identity/Tenancy 后续列表 CRUD，而不是为了引入依赖改写现有固定 SQL。

## 4. QueryMultiple 的统一执行模型

QueryMultiple 适合一次往返读取有共同参数和一致性窗口的多个结果集，例如：详情＋角色＋权限、工作台多个小统计、分页 Items＋Total。它不是把无关查询随意拼成大批次的理由。

`Full.NET.Data.Abstractions` 增加：

```csharp
public interface IMultiResultQueryExecutor
{
    Task<TResult> QueryMultipleAsync<TResult>(
        SqlStatement statement,
        object? parameters,
        Func<IMultiResultReader, CancellationToken, Task<TResult>> projector,
        CancellationToken cancellationToken = default);
}

public interface IMultiResultReader
{
    Task<T?> ReadSingleOrDefaultAsync<T>();
    Task<IReadOnlyList<T>> ReadAsync<T>();
}
```

实现要求：

- 复用 `DbSession`、当前事务、`SqlScopeGuard`、CommandTimeout、CancellationToken 和 SQL 日志；
- `GridReader` 只存在于 `Full.NET.Data.Dapper` 内部，并由 Executor 在 projector 返回或异常时释放；
- 结果集必须按 SQL 声明顺序串行、完整消费；跳过、并行读取、多读或 projector 返回时仍有未消费结果都失败；
- 每个结果集默认缓冲为明确类型，避免开放 Reader 期间嵌套使用同一 MySQL 连接；
- SQL Server 与 MySqlConnector 都用真实数据库验证多 Statement、多结果集、NULL、空集合、异常和取消；
- 使用前比较“一次 QueryMultiple”与“多个普通 Query”的执行计划、网络往返和内存；没有收益的简单查询保持普通 Executor。

## 5. Provider 差异隔离

不引入 Dapper.ProviderTools。Full.NET 的 Provider 隔离由三层组成：

```text
DatabaseProvider / DatabaseOptions
-> 小型 ISqlDialect 原语（分页、标识符等）
-> 同语义、双实现的 SqlStatement Catalog（锁、Upsert、JSON 等）
```

Provider 分支只能位于 `Full.NET.Data.Dapper`、迁移、模块 Persistence 的成对 Statement 注册或 Provider 包；Handler 只引用语义名称。每个 Provider 专有 Statement 必须具有相同参数、返回、空值、排序、并发语义和 SQL Server/MySQL 集成测试。ProviderTools 的“ADO.NET 辅助”不能替代这些领域语义。

## 6. 事务与批量

继续保留 `ICommandTransaction`：

- 顶层事务由 Command Pipeline/领域服务开始；嵌套调用复用当前事务；
- Executor 自动把 `DbSession.Transaction` 放入 `CommandDefinition`；
- 业务代码不接触 `IDbTransaction`，从而不能遗漏 Outbox 或绕过日志/超时/租户注入；
- 需要 Savepoint、隔离级别或事务后回调时扩展 Full.NET 抽象并做双库测试，不通过 Dapper.Transaction 直接下放事务对象。

批量默认分层：

1. 小中批量使用 Dapper Core 对参数集合的 `Execute`，显式分批并限制单批参数量；
2. 百万级导入先建立真实基准、失败恢复和内存门禁；
3. 确有收益时定义 Full.NET `IBulkWriter`，由 SQL Server `SqlBulkCopy` 与 MySqlConnector `MySqlBulkCopy` Provider 实现；
4. Seed Baseline 优先幂等和可恢复，不为了速度默认引入商业批量库。

## 7. 明确禁止项

- 业务 Handler/Endpoint 调用裸 `DbConnection`、Dapper 扩展、`GridReader` 或 `IDbTransaction`；
- 将请求排序字段、列名、运算符、JOIN、WHERE 或 SQL 片段直接交给 SqlBuilder；
- 用 SqlBuilder 实现隐藏通用 Repository、表达式树翻译或自动 CRUD；
- 为减少代码行数绕过 `SqlDataScope`、参数化、稳定 Statement 名、慢 SQL 日志或双库测试；
- 把 QueryMultiple 用于并行读取、长时间流式 Reader 或不相关的大结果集。

## 8. 验收

当前 QueryMultiple 子能力已通过双库真实测试；整项 Dapper 辅助能力达到 `Verified` 仍需要：

1. QueryMultiple 统一执行器通过 Unit 和 SQL Server/MySQL Integration；
2. SqlBuilder 封装由首个真实列表消费者验证，所有用户可控标识符经过白名单；
3. 架构测试阻止模块调用裸 Dapper/Connection/Transaction，并阻止被拒绝的扩展包进入依赖图；
4. 性能基准比较普通查询、QueryMultiple 和动态列表构建的分配、往返与真实执行计划；
5. Dapper.SqlBuilder Apache-2.0 进入依赖审计和 Notice；
6. Provider 成对 Statement 缺失、语义漂移或只验证一个数据库时 CI 失败。

## 9. 参考

- [Dapper 官方仓库与 QueryMultiple](https://github.com/DapperLib/Dapper)
- [Dapper.SqlBuilder NuGet](https://www.nuget.org/packages/Dapper.SqlBuilder/)
- [Dapper.ProviderTools NuGet](https://www.nuget.org/packages/Dapper.ProviderTools)
- [Dapper.Transaction 仓库](https://github.com/zzzprojects/Dapper.Transaction)
- [MySqlConnector 批处理选项](https://mysqlconnector.net/connection-options/)
- [Dapper 辅助能力实施计划](../plans/2026-07-18-dapper-tooling.md)
