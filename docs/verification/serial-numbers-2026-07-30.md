# SerialNumbers 官方模块验证记录

- 日期：2026-07-30
- 状态：**Build-verified**
- 计划：[`2026-07-30-adminnet-design-absorption-program.md` Task 4](../superpowers/plans/2026-07-30-adminnet-design-absorption-program.md#task-4-建立-serialnumbers-官方模块)
- 任务快照：`adminnet-serialnumbers-task4-20260730`

## 交付范围

本切片以 Admin.NET 流水号的产品语义为参考，采用洁净室方式按 Full.NET
边界重新实现：

| 层级 | Full.NET 落点 |
|---|---|
| 规则目录 | Host-only 列表、详情、创建、更新、启用、禁用；`Version` 乐观并发 |
| 语义冻结 | 首次分配后冻结作用域、重置周期、Pattern 和数值边界；名称、描述、排序和启停仍可更新 |
| 预览 | 纯函数预览，不读取或推进计数器 |
| Pattern | 固定文本、受限 UTC token、租户标识和唯一 `{sequence:N}`；未知、本地时间、越界长度全部拒绝 |
| 业务端口 | `ISerialNumberAllocator` 强类型端口；不发布通用 HTTP 取号接口 |
| 作用域 | Host 全局计数器和可信当前租户计数器；租户请求不能指定其他租户 |
| 重置 | `Never`、UTC 日、月、年 reset bucket |
| 幂等 | 同一规则、作用域和幂等键永久重放原结果，跨 bucket 不重复取号 |
| 持久化 | `fn_serialnumbers_rule`、`counter`、`allocation`；UUID v7、显式 Dapper SQL、同事务推进与落结果 |
| 双库 | SQL Server 使用事务级 `sp_getapplock` 串行化同一逻辑桶；MySQL 使用唯一作用域桶和原子 upsert |
| 迁移 | SQL Server/MySQL `039_SerialNumbers.sql`，可修复缺失或同名畸形唯一索引 |
| 安全 | Host 精确读写权限、标准 ProblemDetails、全局 SQL 精确目录、System.Text.Json 源生成 |

## 与 Admin.NET 的取舍

- 保留规则、预览、作用域和重置能力，不复制源码、表结构、统一包络或前端资产。
- 不使用分布式缓存锁承载正确性；缓存故障不会导致重复号码。
- 不吸收反射式自定义槽位、任意变量 Provider 或本地时间 token。
- 不使用万能实体基类或运行时过滤接口代替显式作用域 SQL。
- 号码保证同一逻辑桶内唯一且单调，允许事务回滚、幂等竞争造成间隙。

## 新鲜验证

| 门禁 | 结果 |
|---|---|
| Pattern + Preview Unit | **8/8** |
| SQL Server SerialNumbers API/并发 | **1/1** |
| MySQL SerialNumbers API/并发 | **1/1** |
| SQL Server/MySQL 039 半完成恢复 | **2/2** |
| Architecture | **50/50** |
| Full Unit | **819/819** |
| Naming（含 039 动态 DDL 精确债务） | **24/24** |
| affected 工具契约 | **24/24** |
| Integration Release build | **0 警告 / 0 错误** |
| Integration fresh discovery | **243（42/42/76/83）** |
| affected slice | **smoke 8/8；039 + SerialNumbers 4/4；governance 16/16** |
| Release solution build | **0 警告 / 0 错误** |
| Docker teardown | **running 0 / residual 0** |

双库分配用例覆盖 20 路首次并发、同键并发重放、租户隔离、Host 全局序列、
UTC 日切换、耗尽边界，以及首次分配后语义更新拒绝/元数据更新放行。
最终自审的 RED 证明旧实现会在已有历史编号后放行语义变更；双库实现现以
共享取号锁和排他管理锁串行化规则读取，返回稳定
`serial_numbers.rule.semantics_locked` 冲突，避免更新与首次取号竞态。

SQL Server 的 RED 首先稳定复现首次建桶范围锁转换死锁，
最终使用事务所有权的数据库应用锁按 `(Rule, Tenant, ResetBucket)` 精确串行；
MySQL 的 RED 发现耗尽分支会把持久计数写成零，修复为只清除连接返回信号而
保持计数值。两项均由同一跨 Provider 断言转绿。

039 恢复测试进一步发现 MySQL 畸形唯一索引可能被 InnoDB 选作外键支撑；
迁移先收敛显式 `RuleId` 索引，再修复业务唯一索引，避免依赖隐式索引选择。
命名终审同时修复了扫描器未识别 MySQL `) ENGINE=...` 建表结束行所造成的
局部变量列误报；039 两库恢复 DDL 则按文件精确登记 `dynamic_sql`，不使用
通配目录豁免。

## 已知边界

- 当前没有 Vue/Layui 规则管理页、客户端共享契约和真实栈浏览器 E2E。
- 业务模块必须通过强类型分配端口调用，不提供任意规则的公共取号 Endpoint。
- 完整 `main` CI 和双管理端关键流程通过前不得标记 `Verified`。

## 规则与 Skill 演进

本切片没有发现新的规则冲突或项目 Skill 缺口；数据库首次建桶并发、迁移
半完成恢复和双库门禁均已被现有规则与 `fullnet-module-delivery` 覆盖。
