# Jobs 按任务禁止重叠执行设计

**状态：** Approved for implementation  
**日期：** 2026-08-17  
**基线：** `main` @ `c5567037`  
**适用范围：** Host Jobs 定义、Worker 领取、计划物化、Vue 管理端  
**Admin.NET 映射：** `SysJobDetail.Concurrent` → Full.NET `AllowConcurrentExecutions`（语义相同；Admin.NET 默认 `true`，Full.NET 默认 **false**）

## 1. 决策摘要

Full.NET Jobs 使用数据库队列 + 租约领取。当前多 Worker 可能对同一 `JobDefinitionId` 同时领取不同 pending 执行，违反 Admin.NET `Concurrent=false` 的集群语义。本设计在定义级增加 `AllowConcurrentExecutions`，并在 **Acquire** 与 **Schedule 物化** 两层 gate，保证默认禁止重叠。

## 2. 字段与默认值

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `AllowConcurrentExecutions` | `bool` | `false` | `true` 允许多条 execution 同时 `running`；`false` 集群内同定义最多一条有效 `running` |

Admin.NET 迁移：`AllowConcurrentExecutions = SysJobDetail.Concurrent`。

## 3. 有效 running 定义

`fn_jobs_execution` 行满足：

- `Status = running`
- `LeaseExpiresAtUtc > @Now`（租约未过期）

过期租约的 `running` 视为可恢复，不阻塞新领取。

## 4. Acquire gate

修改 `AcquireExecutions*`：对 `AllowConcurrentExecutions = 0` 的定义，**不得领取** pending 行，若该 `JobDefinitionId` 已存在有效 running。

`AllowConcurrentExecutions = 1` 保持现有 FIFO 领取语义。

## 5. Schedule 物化 gate

[`JobScheduleDispatcher`](../../../src/Modules/Full.NET.Modules.Jobs/Scheduling/JobScheduleDispatcher.cs) 在 cron/one-time 到期时：

- 若定义 `AllowConcurrentExecutions=false` 且已有有效 running → **跳过** 插入新 execution，仍推进 `NextExecutionAtUtc` 与统计，避免无界 pending 堆积
- 与 misfire `skip`/`fire_once` 正交；跳过物化不计为 misfire 补发

## 6. 手动触发

`POST .../trigger` **仍创建 pending**（排队语义）；Acquire gate 保证不会重叠 running。不返回 409（避免打断批量运维触发）。

## 7. API / 权限

- 创建/更新定义可读写 `allowConcurrentExecutions`
- 无新权限码；沿用 `jobs.definitions.create/update`

## 8. 拒绝项

- 不引入 Furion/Sundial 进程内锁
- 不改变 `Jobs:Worker:MaxConcurrency` 默认值
- 不做调度主节点选举

## 9. 验证

- 双 Worker Integration：同定义两条 pending，仅一条 `running`
- Dispatcher：running 存在时不新增 pending
- Vue 开关默认关
