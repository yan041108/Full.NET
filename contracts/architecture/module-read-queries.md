# 受控跨模块查询登记

依据 [ADR-0014](../../docs/architecture/adr/ADR-0014-controlled-cross-module-read-queries.md)，`module-read-queries.json` 是正式读取依赖目录，不是历史债务白名单。当前登记 Organization 的用户机构、用户职位隶属后台列表，按真实查询需求维护条目。

两个列表关联 Identity 的 Host 用户，展示当前用户名与显示名；不要求用户启用，失效关联在 COUNT 与分页前统一排除。机构列表继续通过 Identity 的数据范围契约解析授权，在组织模块内求出可见机构后，以 JSON 标识集合约束静态 JOIN；职位列表仍是租户内目录级读取。详情及写操作继续使用原有契约，不引用这些列表查询。

两个隶属管理服务在事务前取得 Identity 用户展示信息；创建复用成员校验返回的资料，更新和停用在进入事务前通过详情查询中的 Identity 契约取得资料。事务内只回读 Organization 自有数据并用该展示快照组装响应，避免展示读取加入本地事务，也避免提交后展示读取失败使已成功操作被报告为失败。双库管理生命周期用例观察实际 Scoped 事务状态，覆盖静态扫描难以追踪的详情服务间接调用。

## 条目格式

根对象为 `schemaVersion: 1` 与 `entries` 数组。每个条目对应一个静态 SQL 声明；分页与 COUNT、SQL Server 与 MySQL 变体分别登记。

| 字段 | 内容 |
| --- | --- |
| `sourceModule` | 来源模块键，大小写不敏感，不含 `.Contracts` |
| `file` | 仓库相对路径，仅 `src/Modules/Full.NET.Modules.<Module>/Features/<UseCase>/Queries/*Sql.cs` 或模块下 `Queries/*Sql.cs` |
| `member` | 精确的 `SqlClass.Field`；专用文件只允许非 partial 静态类及 `static readonly SqlStatement` 字段 |
| `statementName` | 实际 SqlStatement 名称 |
| `sqlSha256` | SQL 字面量解码后的 UTF-8 SHA-256 小写十六进制；CRLF 规范为 LF，其他空白不变 |
| `purpose` | `admin-list`、`admin-search` 或 `admin-report`；同语义导出复用这些查询 |
| `dependencies` | 外部读取依赖数组，每项为 `module`、`table`、`columns`；列名包括 SELECT、JOIN、WHERE、ORDER BY 中的依赖，不允许通配或重复 |
| `securityNotes` | 具体说明租户/成员关系、数据权限、软删除和敏感字段策略；不以“后台查询”代替授权理由 |

静态声明使用 `new SqlStatement(...)` 或目标类型 `new(...)`，名称和 SQL 必须为直接字符串字面量（支持 C# 原始字符串）；不允许插值、拼接或生成函数。外部表使用显式别名，引用外部列时必须限定别名。SQL 摘要变化要求重新审查依赖列与安全说明。

查询调用者集中在本模块 `Queries/` 中并返回业务 DTO；禁止引用命令事务、DbSession、DbTransaction、TransactionScope、写执行方法或以别名/静态导入隐藏查询类型。架构扫描是源代码结构门禁，不是完整调用图或 SQL 权限证明；审查仍须确认没有通过间接辅助函数加入事务或执行业务决策。

## 失败关闭与其他门禁

- 当前受限语法支持单条 SELECT、参数化关联/子查询、OFFSET/FETCH 分页、参数输入的 OPENJSON/固定路径 JSON_TABLE 行集和普通字符串/注释；纯字符串 REPLACE 不视为写入，序列取号 NEXT VALUE FOR 仍禁止。不支持 CTE、动态 SQL、批量多语句、存储过程、跨数据库对象或有副作用函数。不支持的形状应拆为静态查询，不能用债务登记放行。
- 禁止跨模块写表，也禁止 `INSERT ... SELECT` 从外部表写本地表、`SELECT INTO`、`INTO OUTFILE`、锁定读及事务控制。MySQL 可执行注释、双库含义不一致的注释/转义失败关闭。
- 许可只覆盖该成员 SQL 字面量的位置；同文件其他表引用仍受所有权扫描。旧 `module-table-access-debt.json` 只保留既有历史债务，不允许新增后台查询许可，且不能豁免写语句。
- 必需所有者必须在实际 `Dependencies` 闭包内，不能仅登记为可选 Contract 依赖。出现依赖环时重新选择查询归属。
- `SqlDataScope`、`SqlTenantBinding`、Global 目录、租户/数据权限、外键与事务门禁继续独立执行；登记不证明权限正确，不能提升为 Global 以绕过隔离。

## 变更与拆分

新增查询时：确认后台用途与当前信息/历史快照语义 → 集中声明 SQL 与 DTO → 登记表/列和安全策略 → 核对运行时依赖 → 按本次场景验证双库、分页统计与权限。索引和实际性能仍按执行计划与测量判断。

表结构变化时按目录查找直接消费者并同步查询、DTO 和聚焦测试。独立拆分某个模块时，只改造其实际读取依赖；按需求使用聚合接口或查询模型，并处理首次回填、同步延迟、失败恢复及契约行为。目录不意味着已经实现微服务适配。
