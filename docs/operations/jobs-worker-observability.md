# Jobs Worker 可观测性

Jobs Worker 通过 OpenTelemetry 暴露 `Full.NET.Jobs` Meter。指标只描述已经成功写入数据库的
状态转换；租约所有权丢失导致终态更新影响 0 行时不会记录，避免把未提交状态误报为结果。

## 指标

| 指标 | 单位 | 标签 | 语义 |
| --- | --- | --- | --- |
| `fullnet.jobs.execution.transitions` | `{execution}` | `outcome` | 已持久化的执行状态转换 |
| `fullnet.jobs.retry.scheduled` | `{execution}` | 无 | 可重试失败成功返回 `pending` 的次数 |
| `fullnet.jobs.retry.delay` | `s` | 无 | 成功返回 `pending` 时实际写入的下一次排期延迟 |
| `fullnet.jobs.retry.exhausted` | `{execution}` | 无 | 可重试失败达到总尝试次数并进入 `failed` 的次数 |
| `fullnet.jobs.backlog.executions` | `{execution}` | 无 | 全部 `pending` 执行数量，包含尚未到期的重试 |
| `fullnet.jobs.backlog.oldest_age` | `s` | 无 | 当前可领取执行中最老记录的等待秒数 |
| `fullnet.jobs.retry.due` | `{execution}` | 无 | 已到重试时间的 `pending` 执行数量 |
| `fullnet.jobs.retry.oldest_due_age` | `s` | 无 | 最早已到期重试超过到期时间的秒数 |

`outcome` 仅允许以下固定值：

- `succeeded`：执行已写入成功终态；
- `failed`：执行已写入失败终态；
- `retry_scheduled`：执行已写回待处理并设置下一次可领取时间。

禁止增加 JobKey、ExecutionId、TenantId、异常类型、异常消息、SQL 或 URL 等高基数或敏感标签。
需要定位单条执行时，应使用受权限保护的 Host 执行查询，通过 `status`、`errorMessage`、
`attemptCount` 和 `nextAttemptAtUtc` 关联排查。

## 告警建议

- `fullnet.jobs.retry.exhausted` 在观察窗口内增长时告警，并通过执行查询确认具体终态记录。
- 按部署基线观察 `failed / (succeeded + failed)` 的变化率；没有稳定业务基线前不要固化跨环境
  通用阈值。
- `retry_scheduled` 只能表示排期速率，不能代表当前积压深度。持续增长但缺少对应成功转换时，
  应结合 `backlog.executions`、`retry.due`、年龄指标和 Worker 日志排查。
- `retry.delay` 记录固定/指数退避、封顶与抖动计算后的实际秒数，只用于观察部署的排期分布；
  它不是队列等待时间，也不携带 JobKey、租户或执行标识。

`Jobs:Worker:BacklogSampleSeconds` 控制数据库快照采样周期，默认 30 秒且启动期限制为 5～3600 秒。
每次采样在 Host Context 内对 `fn_jobs_execution` 执行一次只读聚合查询；采样失败会先推进下一采样点，
不会在每个轮询周期重复施压，也不会阻断任务领取。`backlog.executions` 包含未来重试，而
`backlog.oldest_age` 只计算当前已经可领取的记录；没有可领取记录时年龄为 0。

代表性规模查询成本通过 `jobs-backlog-query` benchmark 复现，详见
[双库成本证据](../verification/jobs-backlog-query-evidence-2026-07-30.md)。10 万行计划显示 SQL Server
选择全表聚集扫描，MySQL 使用 037 索引的 `Status` 前缀后再过滤 Host 数据；在完成双库索引 A/B、
写放大和领取/终态路径回归前，不应缩短默认采样周期或声明生产性能收益。

首个固定候选 `IX_fn_jobs_execution_BacklogStatusTenant` 已完成
[双库镜像 A/B](../verification/jobs-backlog-index-ab-2026-07-30.md)，但 SQL Server 查询尾延迟和
三条写路径均未通过门禁，MySQL 优化器也未选择候选索引。该候选不得进入生产迁移；这一否定结果
同样不构成缩短默认采样周期、提高 Worker 并发或承诺生产 SLA 的依据。

## 故障语义

指标属于观测旁路。Meter 监听器或导出器异常不得反转已经提交的任务状态，也不得终止 Worker
轮询。数据库状态写入失败时，原异常仍按现有执行路径传播，且不会提前记录指标。
