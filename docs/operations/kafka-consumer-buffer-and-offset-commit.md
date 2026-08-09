# Kafka Consumer Buffer、Key 槽与 Offset 提交

本文说明 Full.NET Kafka Consumer 的 F1 运行边界。能力已完成构建级验证，但在生产等价故障矩阵、负载与 Soak 通过前仍为 `Capacity-not-verified`，不得据此开启正式 CDC/Kafka 切流。

## 默认安全配置

| 配置 | 默认值 | 说明 |
| --- | ---: | --- |
| `ConsumerBufferHighWatermark` / `LowWatermark` | `256` / `128` | 单 Consumer 的全局在途与排队总量；达到高水位暂停全部已分配分区，降至低水位才恢复。 |
| `PartitionBufferHighWatermark` / `LowWatermark` | `1` / `0` | 单分区有界容量；默认值保持原有单在途语义。 |
| `PartitionKeyConcurrencySlots` | `1` | 固定槽数，范围 `1..64`，且不得超过分区高水位。 |
| `OffsetCommitMode` | `PerMessage` | 默认每次形成连续安全水位时提交。 |
| `OffsetCommitIntervalMilliseconds` | `1000` | 周期提交间隔，同时作为非致命提交失败的重试退避。 |
| `OffsetCommitBatchSize` | `100` | 周期模式累计安全水位更新达到该值时提前 Flush。 |
| `PeriodicOffsetCommitVerified` | `false` | Production/Staging 启用周期模式的显式故障矩阵门禁。 |

同一分区的业务 Key 使用 `XxHash64(UTF8(key)) % slotCount` 固定映射。相同 Key 串行，不同槽可以并行；空 Key 与超过 256 UTF-8 字节的 Key 进入槽 0，后者仍必须由 Envelope 校验失败关闭。槽位只提高 Handler 并发度，不改变 Kafka 分区、Inbox 幂等或连续 Offset 提交语义。

## 不变量

- `Consume/Pause/Resume/Seek/Commit` 只在 Poll Loop 执行；Handler 不持有 Consumer。
- Offset Tracker 只发布队首连续成功水位。后续 Offset 即使先完成，也不能越过未完成或失败消息。
- 任一槽失败会取消该分区所有槽，排队消息不再进入 Handler，Poll Loop Seek 到失败 Offset 后有界退避重投。
- `revoked` 分区允许强制 Flush 已确认安全水位；`lost` 分区已失去提交权，只丢弃本地待提交水位。
- 非致命 Commit 失败保留待提交水位并按间隔重试；Inbox 幂等保护 Broker 重投。致命错误继续向 Worker 传播。

## 调优与回退

先增加 `PartitionBufferHighWatermark`，再把 `PartitionKeyConcurrencySlots` 从 1 小步提高；全局高水位必须受实例内存、数据库连接预算和 Handler 最大并发约束。Key 分布倾斜时，增加槽数不能消除单一热点 Key 的串行瓶颈。

`PeriodicWatermark` 只减少 Broker Commit 往返，不减少 Inbox/业务事务。启用前必须验证 Handler 成功后进程崩溃、Commit 失败、Rebalance revoke/lost、滚动发布、Broker 中断、关闭排空和重投去重；然后设置 `PeriodicOffsetCommitVerified=true`。异常时先回退 `OffsetCommitMode=PerMessage`，再把 `PartitionKeyConcurrencySlots=1`、分区水位恢复为 `1/0`。回退可能增加重复投递与 Commit 负载，但不得丢消息或越过失败 Offset。

必须保存配置快照、Consumer Lag、Buffer 深度、Pause/Resume、Commit 失败/耗时、Inbox 重复命中、Rebalance 和回退耗时证据。缺少生产等价数据时，不得把本地测试吞吐声明为容量结论。
