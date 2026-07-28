# Audit 与 Outbox 数据保留运维说明

## 当前状态

Audit Access、Operation、Exception 汇总表和已成功处理的 Outbox 已支持 Worker 小批量保留
清理。生产配置默认关闭，只有部署方确认适用的法律、财务和行业制度后才能启用。不得通过
手工 SQL、外部定时任务或复用 Audit 清理器扩大 Outbox 删除资格。

## Audit 配置

Worker 使用 `Auditing:Retention`：

```json
{
  "Auditing": {
    "Retention": {
      "Enabled": false,
      "AccessRetentionDays": 30,
      "OperationRetentionDays": 365,
      "ExceptionRetentionDays": 90,
      "BatchSize": 200,
      "MaxBatchesPerRun": 15,
      "PollSeconds": 3600
    }
  }
}
```

- 保留天数允许 `1–3650`；
- `BatchSize` 允许 `1–2000`；
- `MaxBatchesPerRun` 允许 `1–100`；
- `PollSeconds` 允许 `60–86400`；
- 任一边界非法时 Worker 启动失败，不会采用回退值；
- `Enabled` 支持配置重载。改为 `false` 后不再开始新批次，正在执行的单批事务按数据库结果结束。

## 删除语义

清理严格删除 `OccurredAtUtc < 截止时间` 的记录，等于截止时间的记录保留。单轮按
Access → Operation → Exception 轮转，每类最多执行一个批次后让出机会；达到单轮批次上限，
或三类都返回不足一批时结束。

SQL Server 使用 `UPDLOCK, READPAST, ROWLOCK` 的有界候选 CTE。MySQL 在短事务中使用
`FOR UPDATE SKIP LOCKED` 领取有序 ID，再只删除已领取集合。三张表已有
`(OccurredAtUtc, Id)` 索引，本能力不新增迁移。

## 启用与暂停

1. 先确认三类数据的保留期并记录审批来源；
2. 以默认 `BatchSize=200`、`MaxBatchesPerRun=15` 启用；
3. 观察请求 P95/P99、数据库锁等待、事务日志或 undo、连接池和 Worker 错误；
4. 若出现预算退化，先将 `Enabled` 改为 `false`，确认不再产生新清理批次；
5. 只有容量证据支持时才逐档增加批次大小或单轮上限。

指标 Meter 为 `Full.NET.Auditing.Retention`，包含删除行数、失败数、最近成功 Unix 时间和
单轮耗时。标签只使用 `category`、`provider` 和 `result`。

## Outbox 配置与删除边界

Worker 使用独立的 `OutboxRetention` 配置：

```json
{
  "OutboxRetention": {
    "Enabled": false,
    "RetentionDays": 30,
    "BatchSize": 200,
    "MaxBatchesPerRun": 15,
    "PollSeconds": 3600
  }
}
```

范围与暂停语义和 Audit 一致。清理只删除 `ProcessedAtUtc` 非空、`DeadLetteredAtUtc` 为空且
严格满足 `ProcessedAtUtc < 截止时间` 的成功终态记录。等于截止时间的记录，以及 Pending、
待重试、持租约和 Dead Letter 都禁止自动删除。

SQL Server 使用有界锁候选 CTE；MySQL 在短事务中通过 `FOR UPDATE SKIP LOCKED` 领取 ID 后
按领取集合删除。指标 Meter 为 `Full.NET.Outbox.Retention`，记录删除行数、失败数、最近成功
时间和单轮耗时。配置改为 `false` 后，当前数据库批次结束，但不会开始下一批。

现有 `Full.NET.Outbox` 积压采样仍按 `OutboxWorker:BacklogSampleSeconds` 执行一次只读查询，
同一快照额外暴露到期且无活动租约的重试数、活动租约数、死信数和最老死信年龄；不会为每个
分类增加独立数据库往返。Pending 总数保持“所有未成功且未死信消息”，因此它包含尚未到期的
重试和活动租约；各分类指标用于解释总量，不应相加后再与 Pending 比较。

2026-07-29 的 SQL Server/MySQL 持续写入短矩阵验证了默认 `BatchSize=200` 和
`MaxBatchesPerRun=15`；两库并发 1/4 的请求、Worker 与 cleanup 错误均为 0。该结果只支持
保留当前默认值，不授权提高批大小、单轮上限或 `OutboxWorker:MaxConcurrency`。无单轮上限
的 MySQL c=4 压力形状曾产生秒级尾延迟，因此运维配置不得把清理改成无界连续循环；完整证据
见 [`outbox-retention-2026-07-29.md`](../verification/outbox-retention-2026-07-29.md)。
