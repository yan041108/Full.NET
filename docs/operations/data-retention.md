# Audit 与 Outbox 数据保留运维说明

## 当前状态

Audit Access、Operation 和 Exception 汇总表已经支持 Worker 小批量保留清理。生产配置默认
关闭，只有部署方确认适用的法律、财务和行业制度后才能启用。Outbox 自动清理尚未交付；
当前不得通过手工 SQL、定时任务或复用 Audit 清理器删除 Outbox 记录。

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

## Outbox 禁止边界

后续 Outbox 清理只能删除 `ProcessedAtUtc` 非空、Dead Letter 为空且严格早于截止时间的成功
终态记录。Pending、待重试、持租约和 Dead Letter 都禁止自动删除。Outbox 清理配置和真实
双库测试完成前，运维不得把本页的 Audit 开关理解为 Outbox 删除授权。
