# Outbox 成功终态保留清理验证

- 日期：2026-07-29
- 状态：Capacity-verified
- 范围：Task 23 的 Outbox 成功终态清理、运维指标与持续写入容量矩阵；Dead Letter 人工处置仍开放
- 任务基线：`0f434fde7d3182deb03fadfac6995052cd0bb113`

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

后续同日切片扩展了既有 `Full.NET.Outbox` 单次 backlog 快照，新增：

- `fullnet.outbox.retry.due`：已到重试时间且没有活动租约；
- `fullnet.outbox.lease.active`：租约截止时间仍在未来；
- `fullnet.outbox.dead_letter.messages`：死信总数；
- `fullnet.outbox.dead_letter.oldest_age`：最早死信终态年龄。

原 Pending 总数和最老待处理年龄语义保持不变；新增属性采用 init 扩展，保留原有
`OutboxBacklogSnapshot(long, DateTimeOffset?)` 构造兼容。

## 新鲜验证

| 验证 | 结果 |
| --- | --- |
| 新增 Outbox retention Unit | **3/3**，失败 0、跳过 0，约 4 秒 |
| SQL Server/MySQL retention Integration | **2/2**，失败 0、跳过 0，约 45 秒 |
| Integration 项目 Debug 构建 | 0 warning、0 error |
| 影响集 Release 构建 | 0 warning、0 error |
| 任务基线影响集 | tooling **20/20**、Outbox **14/14**、Smoke **8/8**，失败 0、跳过 0 |
| 运维分类 Unit | **1/1**，六项指标从同一快照记录 |
| 运维分类 SQL Server/MySQL Integration | **2/2**，失败 0、跳过 0，约 46 秒 |
| 运维分类任务基线影响集 | Outbox **14/14**，失败 0、跳过 0，约 2 分 25 秒 |
| retention mixed-load 契约 | **24/24**，失败 0、跳过 0，约 3 秒 |
| benchmark Release 构建 | 0 warning、0 error，约 3 秒 |
| 性能治理 | **3/3**，失败 0、跳过 0 |
| 规则治理 | **13/13**，失败 0、跳过 0 |
| 项目 Skills | module-delivery **52/52**、performance-hardening **33/33** |
| `0f434fd` 任务基线影响集 | `none`；未运行 Integration |

双库测试真实插入过期成功、恰好截止、未过期成功、待重试、持租约和 Dead Letter 六类消息，
以 `BatchSize=1` 连续清理两批；首批仅删除过期成功消息，第二批返回 0，其余五类保持存在。

完整 **199** 项 Integration 未在本地运行，继续只由 `main` CI 四个互斥且穷尽的分片执行。
本任务最终验证使用任务基线影响选择器。

## 持续写入与清理并行容量矩阵

基准复用真实 API Host、默认混合请求、生产 `IOutboxStore` 和
`IOutboxRetentionStore`。每个 Provider、并发和 off/on profile 使用独立迁移数据库；每档
预置 2000 条严格过期的成功消息。清理形状与生产默认一致：`BatchSize=200`、
`MaxBatchesPerRun=15`、批间不额外等待。每档预热 1 秒、采样 10 秒。

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release --no-build -- mixed-load `
  --providers sqlserver,mysql --concurrency 1,4 `
  --warmup-seconds 1 --duration-seconds 10 `
  --outbox-retention-profiles off,on `
  --outbox-retention-seed-processed 2000 `
  --outbox-retention-batch-size 200 `
  --outbox-retention-max-batches 15 `
  --outbox-retention-interval-ms 0 `
  --output .tmp/outbox-retention-capacity-final-20260729
```

| Provider | 并发 | 清理 | QPS | 请求 P95/P99 ms | Worker P95/P99 ms | 删除/吞吐 | 锁等待 次/ms | log bytes | undo 前/后 |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| SQL Server | 1 | off | 66.29 | 26.269 / 30.032 | 17.298 / 43.331 | 0 / 0 | 2 / 14 | 5,414,912 | - |
| SQL Server | 1 | on | 78.09 | 20.439 / 31.532 | 15.747 / 70.563 | 2000 / 199.97/s | 5 / 22 | 8,519,680 | - |
| SQL Server | 4 | off | 251.50 | 25.957 / 31.383 | 29.903 / 110.026 | 0 / 0 | 1274 / 3813 | 16,609,280 | - |
| SQL Server | 4 | on | 239.51 | 26.617 / 33.923 | 31.638 / 124.510 | 2000 / 199.84/s | 1258 / 3790 | 19,103,744 | - |
| MySQL | 1 | off | 58.80 | 28.226 / 32.964 | 32.184 / 100.436 | 0 / 0 | 0 / 0 | 2,539,520 | 24 / 78 |
| MySQL | 1 | on | 51.97 | 32.981 / 42.115 | 37.336 / 72.163 | 2000 / 199.89/s | 7 / 51 | 3,152,384 | 56 / 97 |
| MySQL | 4 | off | 170.77 | 38.823 / 56.802 | 150.685 / 261.293 | 0 / 0 | 0 / 0 | 5,934,080 | 70 / 41 |
| MySQL | 4 | on | 170.58 | 38.793 / 46.534 | 97.619 / 285.680 | 2000 / 199.74/s | 7 / 40 | 6,924,288 | 63 / 9 |

八档请求、Worker 和 cleanup 错误均为 0，连接池无超时，清理开启档都删除 2000 条。请求
和 Worker P99 使用“相对退化不超过 20%，或分别不超过 100ms/250ms 低延迟保护带”的门禁；
MySQL c=4 Worker P99 为 285.680ms，但相对 off 只增加 9.33%，因此通过。

工具开发期间另有一组超出生产拓扑的压力形状：`BatchSize=25`、每 100ms 持续执行且没有
单轮批次上限。MySQL c=4 在第 47 个清理事务附近出现约 1.1 秒数据库停顿，请求 QPS 从
170.97 降到 96.50。基准因此补齐并强制 `MaxBatchesPerRun`，不会再把无界连续清理结果误当
生产默认；这个否定证据也说明不能移除单轮上限或把清理改成无界循环。

原始 JSON、Markdown 和请求/Worker/cleanup NDJSON 位于
`.tmp/outbox-retention-capacity-final-20260729/`。该目录是本机工件，不提交仓库。

## 结论与仍开放项

- Task 23 Step 3 的持续写入双库短矩阵已关闭；保留 `BatchSize=200`、
  `MaxBatchesPerRun=15`、`PollSeconds=3600` 和生产默认关闭，不据此提高清理强度。
- 本矩阵不授权提高 `OutboxWorker:MaxConcurrency=1`；Handler 延迟、多副本、持续积压和
  payload 矩阵仍属于 Task 24。

- Dead Letter 仍只允许受控人工处置，没有自动删除入口。
- 尚未形成生产多实例指标导出、告警阈值和长期容量证据，因此整体能力不能标记为 `Verified`。
