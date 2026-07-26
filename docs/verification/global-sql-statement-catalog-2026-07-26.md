# Global SQL Statement 精确目录验证记录

- 日期：2026-07-26
- 分支：`main`
- 计划：[`2026-07-26-global-sql-statement-catalog.md`](../superpowers/plans/2026-07-26-global-sql-statement-catalog.md)
- 变更性质：架构契约、ArchitectureTests 与治理文档；未修改生产 SQL、迁移或运行时行为

## 交付范围

`contracts/architecture/global-sql-statements.json` 逐条登记当前 23 条生产 `SqlDataScope.Global` 声明。每项以 Statement Name、声明成员和源码文件三元组精确定位，并记录安全分类、中文理由和必须保留的 SQL 片段。

目录覆盖以下七类边界：

| 分类 | 用途 |
| --- | --- |
| `cross_context_audit_write` | 匿名、Host、租户上下文共享的显式审计写入 |
| `reliable_event_sink` | 携带可信租户值的 Outbox 可靠写入汇点 |
| `host_catalog` | 认证前或租户上下文内仍需读取、且由 SQL 固定限制 Host 行的目录 |
| `verified_identity` | 以已验证演员、会话、安全戳或版本作为联合锚点的身份操作 |
| `tenant_resolution` | 当前租户建立前按稳定自然键执行的租户解析或幂等检查 |
| `host_tenant_catalog` | 已授权 Host 管理员使用的显式租户目录 |
| `explicit_tenant_anchor` | Host 流程按显式目标租户与业务标识联合收敛的查询 |

新增 ArchitectureTests 双向拒绝：

- 新增但未登记的 Global 声明；
- 已删除但仍存在的过期目录项；
- 重复三元组或重复 Statement Name；
- Statement Name、声明或文件中的通配符；
- 未知分类、空安全理由或空 SQL 片段；
- 必需 SQL 片段漂移；
- Global Statement 携带非 `None` 的 `SqlTenantBinding`。
- IL 中的 `SqlStatement` 构造不在类型静态初始化器，或构造次数与可反射静态声明次数不一致，防止内联、实例成员、工厂、集合包装、`default`、cast 或 alias 绕过静态目录；
- 任一生产程序集类型加载不完整，禁止以部分反射结果继续放行。

当前唯一运行时 clone 例外是
`Full.NET.Modules.Organization.Persistence.TenantScopedSqlComposer.ApplyDataScopeFilter`：
它只能复制已登记的静态 Statement 并修改 Name/Text；IL 门禁同时拒绝该方法直接调用构造器或修改 Scope/TenantBinding。例外按完整声明成员精确登记，不使用类型、命名空间或文件通配符。

## RED → GREEN 证据

首次运行使用空目录：

```powershell
dotnet build tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --filter "FullyQualifiedName~GlobalSqlStatementCatalogTests" --minimum-expected-tests 2
```

结果：负向分析器夹具通过；生产目录测试失败，精确报告 **23** 条 `Unregistered global statement`，没有额外扫描噪声。

补齐 23 条精确目录后使用同一命令重跑，结果 **2/2** 通过、失败 **0**、跳过 **0**。

## 最终验证

| 验证 | 结果 |
| --- | --- |
| `dotnet build Full.NET.slnx -c Release` | **0 warning / 0 error** |
| Unit | **366/366**，失败 **0**、跳过 **0** |
| Compatibility | **7/7**，失败 **0**、跳过 **0** |
| Architecture | **46/46**，失败 **0**、跳过 **0** |
| `pnpm test:naming` | **23/23**，失败 **0** |
| `pnpm test:skills` | `fullnet-module-delivery` **52** 项契约检查通过 |
| `pnpm test:governance` | **11/11**，失败 **0**；canonical 门槛一致 |

本任务没有改变 SQL 文本、Dapper 执行器、事务、迁移或数据库映射，因此按 `rules/development-quality.md` 的 Integration 风险分层不重复运行容器套件。该工作树前一项租户 SQL 绑定任务已经取得 Organization/Tenancy SQL Server/MySQL 焦点 **12/12** 和全量 Integration **172/172** 的新鲜双库证据；本记录不把它伪装成本任务修改后的重复运行结果。

架构级独立复核首次发现纯反射/token 扫描可被内联 `default(SqlDataScope)` 绕过；改用 IL 构造门禁并将类型加载改为 fail-closed 后复核结果为 **Critical 0 / Important 0 / Minor 0**，结论 **Ready**。

## 规则与 Skills 复盘

- 规则：Global 是跨租户隔离的高风险例外，且项目所有者已确认强化模块化单体与 Dapper 数据边界为长期架构决策，因此在 `rules/development-quality.md` 既有 Dapper 与 Host 目录规则中补充精确目录要求，并以 ArchitectureTests 自动化；没有新增近义规则标识。
- Skills：本次流程已被 `fullnet-module-delivery` 的 Dapper/架构契约交付范围覆盖；没有出现第二类稳定、重复且需要三个以上工程判断的新工作流，因此不新增或扩展项目 Skill。

## 残余边界

- SQL 片段门禁验证不可变安全锚点是否存在，不替代 SQL 语义审查和双库真实执行。
- 未来新增 Global Statement 必须先证明其不能安全使用 `TenantRequired` 或 `HostOnly`，再逐条登记；目录存在本身不是放宽作用域的授权。
- `Dapper.SqlBuilder` 仍未引入，继续等待真实动态列表消费者命中既定准入门禁。
