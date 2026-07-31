# SerialNumbers 纵向切片实施计划

**任务快照：** `adminnet-serialnumbers-task4-20260730`
**迁移预留：** SQL Server/MySQL `039_SerialNumbers.sql`
**参考来源：** Admin.NET `SysSerial`、`SysSerialService`、规则管理与预览页面。

## 目标

吸收 Admin.NET 流水号的规则目录、启停、预览、Host/租户作用域与
日/月/年重置能力；实现遵守 Full.NET 的模块边界、可信租户上下文、
UUID v7、显式 Dapper SQL、SQL Server/MySQL 双库和标准 ProblemDetails。

业务取号只通过 `ISerialNumberAllocator` 强类型端口开放，不提供通用
“任意调用者取下一个号”的 HTTP 接口。

## 明确不复制

- 不复制分布式缓存锁；唯一性与单调性由数据库原子更新保证。
- 不复制反射式变量 Provider、任意自定义槽位或本地时间。
- 不复制 Admin.NET 源码、注释、前端资产或统一响应包络。
- 不把预览连接到真实计数器；预览是纯函数且不消费号码。

## 领域与持久化不变量

- Pattern 必须且只能包含一个 `{sequence:N}`，`N` 为 `1..18`。
- 允许固定文本、UTC 时间 token、租户标识 token；未知 token、未配对花括号、
  本地时间 token、越界 Pattern/输出长度全部拒绝。
- 唯一且单调的边界是 `(Scope, RuleKey, ResetBucket)`；允许事务回滚或并发冲突
  造成断号，但绝不重复。
- 同一 `(Rule, Scope, IdempotencyKey)` 永久重放原结果，跨 bucket 重试也不重新取号。
- 首次分配后，`Scope`、重置周期、Pattern、最小值和最大值不可再变更；
  取号使用共享规则锁，管理变更使用排他规则锁，消除首次取号竞态。
- `fn_serialnumbers_rule`、`fn_serialnumbers_counter`、
  `fn_serialnumbers_allocation` 的写入使用同一事务；租户规则绑定可信
  `ICurrentTenant`，Host 规则使用精确 `TenantId IS NULL` 的 Global SQL。

## TDD 顺序

1. 解析器与纯预览 RED→GREEN：token、宽度、UTC、长度、scope、不消费计数。
2. 双库 RED：并发唯一性、同键幂等、租户隔离、Host 全局、bucket rollover、
   exhaustion、分配后语义冻结/元数据可更新、039 缺失/畸形索引恢复。
3. 最小实现：Contracts、规则服务、Provider SQL、事务分配器、039 双库迁移。
4. 纵向接线：Host 规则 API、权限、JSON 源生成、Composition、solution、
   affected selector、OpenAPI、全局 SQL 目录和架构所有权。
5. 收口：Unit、SQL Server/MySQL affected、迁移恢复、SQL safety、Naming、
   Architecture、Release build、唯一测试矩阵和验证记录。

## 完成状态

本任务没有 Vue/Layui 规则管理页面，因此最多标记为 `Build-verified`；
双管理端交付与真实栈 E2E 在后续独立切片完成前不得标记 `Verified`。
