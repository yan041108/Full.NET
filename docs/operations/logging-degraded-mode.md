# 日志高优先级通道与降级处置

## 当前边界

Full.NET 的三个官方宿主统一通过 `AddFullNetServiceDefaults()` 建立两条互不共享容量的 Serilog 异步通道：

| 通道 | 等级 | 默认容量 | 队列满时行为 |
| --- | --- | ---: | --- |
| `general` | `Information`、`Warning` | 10000 | 丢弃新增事件并累计指标 |
| `high_priority` | `Error`、`Critical` | 1000 | 丢弃新增事件并累计指标 |

两条通道都固定为非阻塞。`FullNet:Logging:BlockWhenFull=true` 会在启动时被拒绝，避免慢 Sink 或平台故障把请求线程拖入同步等待。`Debug/Verbose` 仍受全局最小等级限制，不进入这两条生产通道。

```json
{
  "FullNet": {
    "Logging": {
      "AsyncBufferSize": 10000,
      "HighPriorityAsyncBufferSize": 1000,
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

`channel` 只允许 `general` 与 `high_priority`。禁止加入租户、用户、路径、异常消息或其他高基数值。

`high_priority_logging` 作为 `ready` 健康检查注册。当前高优先级深度达到容量的 90% 时返回 `Degraded`；普通通道过载不会驱逐实例。累计丢弃值不直接让实例永久降级，必须由监控平台按时间窗口计算增量。

建议告警：

1. `high_priority` 的 `events.dropped` 在任意 5 分钟窗口增量大于 0：立即告警。
2. `high_priority` 的 `queue.depth / queue.capacity` 连续 5 分钟大于等于 0.8：高优先级告警。
3. `general` 的丢弃持续增长：容量或日志等级治理告警，不应自动扩容掩盖无界日志。
4. `high_priority_logging` 连续 `Degraded`：检查 Sink 消费速度与平台采集状态。

## 退出排空

`ShutdownFlushTimeout` 是普通与高优先级通道共享的总退出预算，默认 5 秒，只允许大于 0 且不超过 30 秒。宿主释放 Logger 时会同时停止两条通道接收新事件，让两个后台 Worker 并行排空；等待阶段优先确认高优先级 Worker，再把同一截止时间内的剩余时间交给普通 Worker，因此最坏等待不会变成“两条通道各等待一次超时”。

到期后，Full.NET 只放弃尚未进入 Sink 的内存队列事件并累计丢弃数。已经进入阻塞 Sink 的单条事件无法安全中止，只能留在后台线程等待 Sink 自行返回；后台线程不会阻止进程退出。操作系统调度可能带来少量超时误差，配置值不是投递成功保证。

正常退出且 Sink 在预算内可用时，两条队列会完整排空并释放内部 Sink。强制终止、进程崩溃、节点掉电或超过预算时仍可能丢失日志，因此该机制不能替代持久化审计、磁盘 Spool 或外部投递确认。

## 审计边界

日志队列不是审计存储。认证审计、租户/超级管理员安全操作、Seed 执行记录和可靠业务事件继续由数据库事务或 Outbox 持久化。不得因为高优先级队列独立而把这些记录改成 `ILogger` 调用；日志队列满不得改变业务事务和审计写入结果。

## 降级处置

1. 先比较两个通道的深度与丢弃增量，判断是普通日志洪峰还是高优先级 Sink 受阻。
2. 检查容器标准输出采集器、宿主磁盘/管道和 OpenTelemetry Collector 状态；不要在请求线程临时切换到同步网络写入。
3. 若只有普通通道丢弃，先降低噪声来源的日志等级或修复重复日志；不要扩大高优先级容量代替治理。
4. 若高优先级通道丢弃，保留 TraceId 和受保护的服务端诊断，按事故处理；敏感异常、Token、Cookie、连接串和 SQL 不得写入临时日志。
5. 恢复后确认高优先级深度回落、丢弃增量归零，并记录故障窗口。

## 尚未完成

当前 Console Sink 依赖部署平台采集标准输出，不提供 Full.NET 自有的磁盘 Spool、跨重启重放或外部 Sink 投递确认。Task 8B1 已验证正常退出完整排空，以及两个 Sink 同时阻塞时共享一个有界退出预算；尚未完成的 Task 8B 必须在引入持久能力前明确容量、保留、加密、磁盘满策略和重复投递语义，并完成平台不可用、磁盘满与跨重启故障注入。
