# 日志高优先级通道与降级处置

本页描述应用双通道的故障处理。模式、字段和平台现状见[日志模块说明](logging-module.md)；Local、Collector 与 ApplicationKafka 的配置见 [Wiki](../../code-wiki/logging-guide.md)。2026-10-17 整改状态以[活动计划](../superpowers/plans/2026-10-17-logging-remediation.md)为准。宿主接收、Kafka ACK 与 ES 可查是不同阶段；直发保留独立 SDK 预算/确认监控，不默认故障双写到采集器。

## 当前边界

Full.NET 的三个官方宿主统一通过 `AddFullNetServiceDefaults()` 建立两条互不共享容量的 Serilog 异步通道：

| 通道 | 等级 | 默认条数/字节容量 | 队列满时行为 |
| --- | --- | ---: | --- |
| `general` | `Information`、`Warning` | 10000 条 / 64 MiB | 丢弃新增事件并累计指标 |
| `high_priority` | `Error`、`Critical` | 1000 条 / 8 MiB | 丢弃新增事件并累计指标 |

两条通道都固定为非阻塞。`FullNet:Logging:BlockWhenFull=true` 会在启动时被拒绝，避免慢 Sink 或平台故障把请求线程拖入同步等待。`Debug/Verbose` 仍受全局最小等级限制，不进入这两条生产通道。

每次调用先非阻塞预留条数名额，再按单事件最大费用预留字节预算，快照完成后退还差额；未取得预算时不会继续构造快照。后台仅持有受限安全 JSON 和事件 ID，旧 Elasticsearch Sink 的专用事件副本已移除。字节指标是保守队列/在途费用，不代表进程 RSS。

```json
{
  "FullNet": {
    "Logging": {
      "AsyncBufferSize": 10000,
      "HighPriorityAsyncBufferSize": 1000,
      "MaxEventBytes": 16384,
      "GeneralQueueMaxBytes": 67108864,
      "HighPriorityQueueMaxBytes": 8388608,
      "BlockWhenFull": false,
      "ShutdownFlushTimeout": "00:00:05"
    }
  }
}
```

## 指标与健康

Meter `Full.NET.Logging` 暴露：

- `fullnet.logging.queue.depth{channel}`
- `fullnet.logging.queue.capacity{channel}`
- `fullnet.logging.events.dropped{channel}`
- `fullnet.logging.queue.bytes{channel}` / `fullnet.logging.queue.bytes.capacity{channel}`
- `fullnet.logging.events.oversize{channel}` / `fullnet.logging.events.byte_budget_dropped{channel}`
- `fullnet.logging.events.offered{channel,level}` / `fullnet.logging.events.consumed{channel,outcome}`
- `fullnet.logging.worker.state{channel}` / `fullnet.logging.worker.progress_age{channel}`
- `fullnet.logging.queue.oldest_age{channel}`（包含下游在途记录，单位秒）

`channel` 只允许 `general` 与 `high_priority`。禁止加入租户、用户、路径、异常消息或其他高基数值。

`high_priority_logging` 作为 `ready` 健康检查注册。高优先级条数或已预留字节达到 90%、消费者不在运行状态、最老在途/排队记录超过 30 秒时返回 `Degraded`。普通通道过载不改变此检查。累计 Counter 在停止后保留，按窗口增量告警；停机不归零。`worker.state` 的 0/1/2/3/4 表示未装配/运行/停止接收/已停止/故障。`consumed{outcome=accepted}` 只表示下游调用接收，不表示外部持久确认。

Prometheus 命名由 OTel 转换，例如 `fullnet_logging_events_dropped_total{channel="high_priority"}` 和 `fullnet_logging_queue_oldest_age_seconds`。B1 使用 `fullnet_auditing_microbatch_queue_depth/capacity/bytes`、`fullnet_auditing_microbatch_failed_total{outcome}` 及 `fullnet_auditing_microbatch_wait_seconds_bucket`；容量压力按深度/容量计算，不能写死 3000 条阈值。正式规则和仪表盘见 `deploy/observability`。

若 Host 有指标而 Prometheus 无数据，先运行 `node eng/testing/otel-collector-config-test.mjs`，再检查 OTLP 地址、Collector 接收/拒绝和 Remote Write 回执。参考配置的 Metrics 队列为 `remote_write_queue` + `wal`，Trace 才使用 `sending_queue.storage: file_storage`。WAL 所在 emptyDir 不跨 Pod 替换保留；不能将 Collector 已接收等同 Prometheus 可查询。平台 Prometheus 需允许 Remote Write 接收，TLS 与认证按实际后端配置。

建议告警：

1. `high_priority` 的 `events.dropped` 在任意 5 分钟窗口增量大于 0：立即告警。
2. `high_priority` 的条数或字节占用率连续 5 分钟大于等于 0.8：高优先级告警。
3. `general` 的丢弃持续增长：容量或日志等级治理告警，不应自动扩容掩盖无界日志。
4. `high_priority_logging` 连续 `Degraded`：检查 Sink 消费速度与平台采集状态。

## Sink 单事件故障隔离

普通与高优先级后台 Worker 都会感知内部 Sink 的真实投递异常。单条事件失败时，Full.NET 丢弃当前事件、递增对应 `channel` 的 `events.dropped`，并继续消费后续事件；一次 Console 或未来外部 Sink 异常不会让整条通道永久停止。

后台内部故障通过限频 stderr 报告固定阶段及异常类型，不依赖启用 SelfLog，不记录异常消息、日志正文、路径或事件属性。连续 Sink 失败仍会持续丢弃事件，但不得反向阻塞请求线程。平台恢复后确认 dropped 增量归零、队列深度回落和消费者恢复，并核对外部采集缺口。

## 退出排空

`ShutdownFlushTimeout` 是普通与高优先级通道共享的总退出预算，默认 5 秒，只允许大于 0 且不超过 30 秒。宿主释放 Logger 时会同时停止两条通道接收新事件，让两个后台 Worker 并行排空；等待阶段优先确认高优先级 Worker，再把同一截止时间内的剩余时间交给普通 Worker，因此最坏等待不会变成“两条通道各等待一次超时”。

到期后，Full.NET 只放弃尚未进入 Sink 的内存队列事件、释放对应快照字节预算并累计丢弃数。已经进入阻塞 Sink 的单条事件无法安全中止，其字节预算继续保留到 Sink 返回；后台线程不会阻止进程退出。操作系统调度可能带来少量超时误差，配置值不是投递成功保证。

正常退出且 Sink 在预算内可用时，两条队列会完整排空并释放内部 Sink。强制终止、进程崩溃、节点掉电或超过预算时仍可能丢失日志，因此该机制不能替代持久化审计、磁盘 Spool 或外部投递确认。

## 审计边界

日志队列不是审计存储。B0 领域审计与业务状态同事务直接写库；B1 重要 HTTP 审计通过有界微批直接写审计库并等待写入尝试，默认 fail-open + 告警；认证审计、租户/超级管理员安全操作和 Seed 执行记录沿各自持久化边界。日志与 Audit 不使用 Outbox，只有可靠业务 Integration Event 使用事务 Outbox。不得因为高优先级队列独立而把这些记录改成 `ILogger` 调用；日志队列满不得改变业务事务和既定审计写入语义。

## 降级处置

1. 先比较两个通道的深度与丢弃增量，判断是普通日志洪峰还是高优先级 Sink 受阻。
2. 检查容器标准输出采集器、宿主磁盘/管道和实际日志目的地；当前 OTel 示例仅承载 Trace/Metrics，不能以 Collector 正常判定日志链正常。不要在请求线程临时切换到同步网络写入。
3. 若只有普通通道丢弃，先降低噪声来源的日志等级或修复重复日志；不要扩大高优先级容量代替治理。
4. 若高优先级通道丢弃，保留 TraceId 和受保护的服务端诊断，按事故处理；敏感异常、Token、Cookie、连接串和 SQL 不得写入临时日志。
5. 恢复后确认高优先级深度回落、丢弃增量归零，并记录故障窗口。

## 退出与恢复边界

Hosting 两个队列及 Kafka 的两个 Producer Flush 共用 `FullNet:Logging:ShutdownFlushTimeout` 的剩余时间。单独释放 Kafka 组件时使用其 `ShutdownFlushTimeoutMs`；宿主管理时不会重新领取这段预算。底层 native Dispose 和不响应取消的操作不能被这个期限强制终止。内存队列仍可能在退出或崩溃时丢失；本地 `.txt` 文件不是可重放的投递队列。

B1 的 `Auditing:MicroBatch` 增加最大排队年龄、批次执行期限和隔离尝试上限。连接/超时/死锁/未知故障不拆批；明确数据错误才有界隔离。取消传入实际事务，待真实操作结束再归还预约；不能把 fail-open 理解成请求侧提前返回并留下后台数据库写入。

令牌已取消时，驱动返回普通数据库异常也统一归入取消路径，先于毒记录分类处理。内部批次期限保持 fail-open，调用方传入 Writer 的取消保留传播；两者都终结所有信封并等待实际调用退出，不通过放松恢复断言隐藏迟到写入。当前 Writer 边界的双库故障/恢复组 8/8 与日志整改 Unit 125/125 已通过，整队列、真实指标及原生范围仍见活动计划。

Fluent Bit 缓冲、Kafka 保留、独立消费者 ES 写入/DLQ/Offset 各有独立故障边界。是否可恢复取决于实际持久卷、保留策略和确认位置；具体模式配置与测试证据以 Wiki 和活动计划为准。当前整改的双库、真实指标导出、日志链路与 Native AOT 验证尚待集中执行，历史通过不能外推到新修订。队列计费不是进程 RSS，也不替代容量与请求 P99 实测；Restricted 详情始终不能进入普通 stdout/File/Kafka/ES 副本。
