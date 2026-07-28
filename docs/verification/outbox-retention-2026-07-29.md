# Outbox 成功终态保留清理验证

- 日期：2026-07-29
- 状态：Build-verified
- 范围：Task 23 的 Outbox 子切片；持续写入容量矩阵与 Dead Letter 人工处置仍开放
- 任务基线：`a883e4d331c825ed117fa02fcd38dd3c77d3ba67`

## 交付边界

Worker 新增独立 `OutboxRetention` 配置，默认关闭，默认保留成功消息 30 天。配置支持热重载；
关闭后当前数据库批次按事务结果结束，但不会开始下一批。Options 对保留天数、批大小、单轮
批次上限和轮询间隔执行启动期范围校验。

删除资格固定为：

```text
ProcessedAtUtc IS NOT NULL
AND DeadLetteredAtUtc IS NULL
AND ProcessedAtUtc < CutoffUtc
```

因此 Pending、待重试、持租约、Dead Letter，以及处理时间恰好等于截止时间的消息均不会被
自动删除。SQL Server 使用 `UPDLOCK, READPAST, ROWLOCK` 有界候选 CTE；MySQL 在短事务中
使用 `FOR UPDATE SKIP LOCKED` 领取 ID，再按同一资格谓词删除领取集合。

`Full.NET.Outbox.Retention` Meter 提供删除行数、失败数、最近成功时间和单轮耗时；标签仅含
固定的 `provider` 与 `result`。

## 新鲜验证

| 验证 | 结果 |
| --- | --- |
| 新增 Outbox retention Unit | **3/3**，失败 0、跳过 0，约 4 秒 |
| SQL Server/MySQL retention Integration | **2/2**，失败 0、跳过 0，约 45 秒 |
| Integration 项目 Debug 构建 | 0 warning、0 error |
| 影响集 Release 构建 | 0 warning、0 error |
| 任务基线影响集 | tooling **20/20**、Outbox **14/14**、Smoke **8/8**，失败 0、跳过 0 |

双库测试真实插入过期成功、恰好截止、未过期成功、待重试、持租约和 Dead Letter 六类消息，
以 `BatchSize=1` 连续清理两批；首批仅删除过期成功消息，第二批返回 0，其余五类保持存在。

完整 **199** 项 Integration 未在本地运行，继续只由 `main` CI 四个互斥且穷尽的分片执行。
本任务最终验证使用任务基线影响选择器。

## 尚未关闭

- 尚未完成持续写入与清理并行的 SQL Server/MySQL 容量矩阵，不能据此提高批大小、单轮上限
  或 `OutboxWorker:MaxConcurrency` 默认值。
- Dead Letter 仍只允许受控人工处置，没有自动删除入口。
- 尚未形成生产多实例指标导出、告警阈值和长期容量证据，因此整体能力不能标记为 `Verified`。
