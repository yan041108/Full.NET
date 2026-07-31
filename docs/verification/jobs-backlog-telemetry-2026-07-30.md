# Jobs 积压快照指标验证（2026-07-30）

## 状态与范围

- 状态：`Build-verified`
- 任务基线：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`
- 任务快照：`jobs-backlog-telemetry-20260730`
- 范围：Jobs Worker 的 Host 级待处理深度、最老可领取等待年龄、到期重试数量、最老到期重试年龄，以及有界数据库采样。
- 不包含：生产指标导出与告警阈值、容量或吞吐收益、Cron/通用延迟调度、退避抖动和运维重放。

## 行为结论

Jobs Worker 在既有 Host Context 内按 `Jobs:Worker:BacklogSampleSeconds` 读取一次
`fn_jobs_execution` 聚合快照。默认采样间隔为 30 秒，启动期允许范围为 5～3600 秒。
同一采样窗口不会重复读取；数据库读取失败会先推进下一采样点并记录事件 `4002`，随后继续任务领取；
宿主取消仍原样传播。

SQL Server 与 MySQL 各使用一条 Provider 专用聚合语句，且都限制
`TenantId IS NULL AND Status = @PendingStatus`。一次数据库往返返回：

| 字段 | 语义 |
| --- | --- |
| `PendingCount` | 全部 `pending` 执行，包含尚未到期的重试 |
| `OldestClaimableCreatedAtUtc` | 当前已可领取执行中最早的创建时间 |
| `DueRetryCount` | `NextAttemptAtUtc` 已到期的 `pending` 重试数量 |
| `OldestDueRetryAtUtc` | 最早的已到期重试时间 |

MySQL 的 `datetime(6)` 结果在模块边界显式按 UTC 转换。未知数据库 Provider 会失败并包含 Provider
名称，不会静默回退到错误 SQL。

## 指标契约

`Full.NET.Jobs` Meter 新增四个无标签 Gauge：

| 指标 | 单位 | 空快照语义 |
| --- | --- | --- |
| `fullnet.jobs.backlog.executions` | `{execution}` | `0` |
| `fullnet.jobs.backlog.oldest_age` | `s` | `0` |
| `fullnet.jobs.retry.due` | `{execution}` | `0` |
| `fullnet.jobs.retry.oldest_due_age` | `s` | `0` |

年龄由采样时钟减去数据库时间计算，空时间或未来时间均钳制为 `0`。指标不携带 JobKey、
ExecutionId、TenantId、异常、SQL 或 URL 标签；Meter 监听器或导出器异常被限制在观测旁路，
不会改变任务状态或终止 Worker。

## TDD 与双库语义证据

1. 读取器 RED：`JobsBacklogReader` 与 `JobsBacklogSnapshot` 尚不存在时，聚焦测试按预期编译失败。
2. 读取器 GREEN：SQL Server 映射、MySQL UTC 映射和未知 Provider 共 3/3 通过。
3. Options RED/GREEN：先由缺失 `BacklogSampleSeconds` 形成 RED，再验证默认值和上下界，聚焦 2/2 通过。
4. HostedProcessor RED/GREEN：四参数构造调用在新增 `IClock` 前形成预期编译 RED；实现后验证 Host Context、
   同窗口单次采样、采样故障节流且不阻断领取，共 3/3 通过。
5. Gauge RED/GREEN：缺失 `JobsTelemetry.RecordBacklog` 时形成 RED；实现后验证四个无标签测量值以及空值/
   未来时间钳制，共 2/2 通过。
6. SQL Server 与 MySQL 各通过既有 Jobs API 聚焦入口 1/1。真实数据库生命周期断言覆盖空基线、
   初始可领取、未来重试仅计入 pending、到期重试进入 due，以及重试耗尽后快照清空；没有新增
   Integration 测试方法。

## 新鲜验证

| 验证 | 结果 |
| --- | --- |
| Jobs Unit 聚焦 | 19/19，失败 0，跳过 0 |
| Jobs SQL Server 聚焦 | 1/1，失败 0，跳过 0 |
| Jobs MySQL 聚焦 | 1/1，失败 0，跳过 0 |
| Jobs 模块 Release 构建 | 0 警告、0 错误 |
| Worker Release 构建 | 0 警告、0 错误 |
| Architecture | 49/49 |
| Naming | 23/23 |
| Governance | 16/16 |

双库聚焦结束后已完成 Testcontainers teardown；协作窗口随后再次确认 `docker ps` 为空并正式释放
Docker。共享工作区收口时的新鲜矩阵门槛为 Unit 681、Infrastructure 81、Full 227、
API SQL Server/MySQL 各 38、Migrations 70，并保留 migration selection 037；本窗口没有覆盖这些
由共享工作区统一维护的数值。

## 成本证据与声明边界

迁移 037 已提供 `IX_fn_jobs_execution_PendingNextAttemptLease`：

- SQL Server：`(Status, NextAttemptAtUtc, LeaseExpiresAtUtc, CreatedAtUtc)`，并以
  `Status = 'pending'` 过滤；
- MySQL：相同列顺序的复合索引。

本切片只以单条聚合 SQL 和默认 30 秒采样约束数据库往返频率。后续已用 10 万行固定分布数据补齐
SQL Server/MySQL 实际计划、P50/P95/P99 与正确性门禁，见
[Jobs 积压查询双库成本证据](jobs-backlog-query-evidence-2026-07-30.md)。计划显示 SQL Server 扫描全表，
MySQL 需读取全部 `status=pending` 后再过滤租户，因此仍不声明生产性能收益；双库索引 A/B 和生产
告警阈值仍是后续项。

没有运行完整 227 项 Integration；完整集合仍由 `main` CI 的互斥并行分片门禁执行。任务快照之后
存在其它窗口写入，affected inner 计划同时命中 CodeGeneration、integration-matrix、Jobs 与
Realtime；本窗口只运行 Jobs 聚焦影响集，避免重复占用其它窗口的验证范围。

规则演进检查未命中用户纠正、重复失败、高风险新类别或规则冲突；Skill 演进检查未发现项目 Skill
缺口，均不新增候选。
