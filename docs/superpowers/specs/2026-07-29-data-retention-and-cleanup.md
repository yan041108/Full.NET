# Audit 与 Outbox 数据保留和小批量清理规格

- 状态：Approved
- 批准日期：2026-07-29
- 批准来源：当前授权用户要求继续执行
  [`2026-07-28-production-performance-hardening.md`](../plans/2026-07-28-production-performance-hardening.md)
  的 Task 23
- 任务基线：`0d9bab4290a19c9968c52c2850fb25dff095dbd8`
- 适用范围：三张 HTTP Audit 汇总表和 `fn_outbox_message`

## 1. 安全边界

清理能力生产默认关闭。默认保留期只是运维起点，不代表法律、财务或行业合规结论；启用前必须
由部署方按适用制度确认。配置校验失败时 Worker 启动失败，不能静默采用更短保留期。

| 数据 | 默认保留期 | 自动清理资格 |
| --- | ---: | --- |
| Access 请求遥测 | 30 天 | `OccurredAtUtc` 严格早于截止时间 |
| Operation 安全审计摘要 | 365 天 | `OccurredAtUtc` 严格早于截止时间 |
| Exception 安全异常摘要 | 90 天 | `OccurredAtUtc` 严格早于截止时间 |
| 已成功处理 Outbox | 30 天 | `ProcessedAtUtc` 非空、Dead Letter 为空且严格早于截止时间 |
| Pending、待重试或持租约 Outbox | 不适用 | 禁止自动删除 |
| Dead Letter Outbox | 人工审批 | 禁止自动删除；没有生产配置可放宽 |

HTTP Operation/Exception 仍不替代业务事务中的领域审计。业务模块自有审计、Identity Auth Audit
和其他历史表不在本规格内，不能因名称相似被清理。

## 2. 配置与暂停

Audit 使用 `Auditing:Retention`，Outbox 使用 `OutboxRetention`。两者独立配置：

- `Enabled` 默认 `false`，支持配置重载；改为 `false` 后不再开始新批次；
- `BatchSize` 默认 `200`，范围 `1–2000`；
- `MaxBatchesPerRun` 默认 `15`，范围 `1–100`；
- `PollSeconds` 默认 `3600`，范围 `60–86400`；
- 保留天数范围 `1–3650`。

宿主取消会停止当前数据库操作；已经提交的单批删除不补偿。重新启用后从数据库现状继续，清理
不维护进程内游标。

## 3. SQL、锁和公平性

三张 Audit 表已有 `(OccurredAtUtc, Id)` 索引，不新增迁移。每一小批只按
`OccurredAtUtc < @CutoffUtc` 选择，排序固定为 `(OccurredAtUtc, Id)`：

- SQL Server 使用 `UPDLOCK, READPAST, ROWLOCK` 的有界候选 CTE 后删除；
- MySQL 在短事务中使用 `FOR UPDATE SKIP LOCKED` 领取有界 ID，再按这些 ID 删除；
- 每轮按 Access → Operation → Exception 轮转，每类最多删除一批后再进入下一类，避免 Access
  积压饿死其他类别；
- 达到 `MaxBatchesPerRun` 或所有类别都返回不足一批后停止本轮。

Outbox 已复用相同小批量原则，资格谓词同时锁定成功终态；没有复用 Audit 的通用表名或动态
SQL。

## 4. 失败与指标

单批数据库失败回滚该批并结束本轮，记录结构化 Error；不得让清理异常终止消息处理和 Jobs
Worker。指标只允许 `category`、`provider`、`result` 等封闭标签，至少记录删除行数、失败数、
最近成功时间和本轮耗时。

Outbox 运维快照已在同一次采样查询中区分 Pending、到期且无活动租约的重试、持租约和
Dead Letter，并记录最老死信年龄；Dead Letter 不混入普通 backlog，也没有自动删除入口。

## 5. 分阶段验收

1. Audit：Options、暂停、轮转上限和 Provider SQL 单元测试；SQL Server/MySQL 真实验证旧记录
   小批删除、新记录保留、每类公平推进。
2. Outbox：真实验证仅删除已成功处理且到期记录；Pending、待重试、持租约和 Dead Letter 均
   保留。
3. 持续写入并行清理的短矩阵记录删除吞吐、请求/Worker P95/P99、锁等待和日志/undo 写放大。
4. 本地只运行受影响测试；完整 199 项继续只由 `main` CI 四个互斥分片执行。
