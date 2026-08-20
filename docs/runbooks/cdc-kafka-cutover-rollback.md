# CDC / Kafka 切流、停止、Fence、排空、回退与 DLQ 重放 Runbook

权威架构见 [`ADR-0006`](../architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md) 与运维概述 [`cdc-kafka-event-delivery.md`](../operations/cdc-kafka-event-delivery.md)。本 runbook 给出可执行步骤与**停止 / 回滚条件**。

> **硬边界：** 生产默认 `Messaging:DeliveryCutover:Enabled=false`。在双库真实 E2E、告警/恢复演练与容量门禁完成前，禁止正式切流，能力状态保持 `Capacity-not-verified`，**不得**标记 `Production-verified`。

## 0. 观测前置（切流前必查）

| 指标（代码仪表名 → Prometheus） | 用途 |
| --- | --- |
| `fullnet.outbox.backlog.oldest_age` → `fullnet_outbox_backlog_oldest_age_seconds` | Legacy 积压年龄 |
| `fullnet.outbox.legacy.empty_poll.backoff` → `fullnet_outbox_legacy_empty_poll_backoff_seconds` | 空轮询退避 |
| `fullnet.outbox.commit_to_capture` → `fullnet_outbox_commit_to_capture_seconds` | 提交到捕获延迟（影子路径或平台填充） |
| `fullnet.messaging.kafka.consumer.lag` → `fullnet_messaging_kafka_consumer_lag` | Consumer 消息滞后 |
| `fullnet.messaging.kafka.lag_retention_ratio` → `fullnet_messaging_kafka_lag_retention_ratio` | 滞后相对保留窗口占比 |
| `fullnet.messaging.connector.lag` / `offset.unrecoverable` | Connector 滞后与位点不可恢复（可由 `UpdateConnectorHealth` 填充） |
| `fullnet.messaging.cdc.sqlserver.capture_job_running` | SQL Server CDC Capture Job（平台填充） |
| `fullnet.messaging.cdc.mysql.binlog_retention_hours` | MySQL Binlog 保留小时（平台填充） |
| `fullnet.messaging.inbox.duplicates` / `kafka.retry.routed` / `kafka.dead_letter.published` | 幂等命中、Retry、DLQ |
| `fullnet.messaging.ownership.wait` / `ownership.transitions` | 所有权等待与转换 |

**停止条件（任一项）：** 上表关键告警处于 firing；`lag_retention_ratio > 0.8`；Capture Job=0；Binlog 保留 &lt; 24h；Connector `offset.unrecoverable=1`；Outbox/Jobs 最老年龄持续 &gt; SLA。

指标标签禁止：Secret、Payload、原始 SQL、TenantId、UserId、异常文本、MessageId。

## 1. 切流（Legacy → CdcKafka）

1. 确认开关仍为默认关闭，仅在已批准变更窗口将 `Messaging:DeliveryCutover:Enabled=true` 写入目标环境配置并滚动 Worker/API。
2. 核对目标流在 Topic Catalog 与 `fn_messaging_stream_ownership` 中存在，且当前有效所有权为 `LegacyPolling`。
3. 排空目标流 Legacy Outbox：pending / due retry / active lease / dead letter 均为 0（`GET /api/v1/messaging/delivery/status`）。
4. 记录切流前位点：SQL Server LSN 或 MySQL binlog position、Kafka high watermark、Consumer Group offset、Connector offsets。
5. 调用 `POST /api/v1/messaging/delivery/cutover`（权限 `messaging.delivery.cutover`），Reason 必填。
6. 观察 `ownership.transitions{result="cutover|revoked|restored"}`、Consumer lag、Inbox duplicate、Retry/DLQ；验证业务投影收敛。

**停止条件：** 步骤 3～5 任一步失败；切流后出现双发布；Inbox 同 ID 不同 Hash；外部不可逆副作用重复。

**回滚条件：** 切流后 15 分钟内 lag/错误率超预算，或对账失败 → 立即进入第 4 节回退。

## 2. 停止（紧急停止生产副作用）

1. 将 `DeliveryCutover:Enabled` 置回 `false`（阻止新的切流 API）。
2. 暂停 Debezium Connector（Connect REST pause），保留 offsets Topic。
3. Worker 切回 `LegacyPolling` 或 `HybridKafka` 下仅保留已批准 Legacy 流；禁止新建 CdcKafka 订阅。
4. 记录停止时刻的位点与告警快照。

**停止条件：** 已满足“无新 CdcKafka 写入副作用”证明前，不得恢复切流开关。

## 3. Fence 与排空 / 隔离

1. 回退准备态（`RollbackState=Preparing`）期间：新生产者写 Outbox 必须失败关闭；Kafka Consumer 可继续排空。
2. 控制面按同一 `RollbackGeneration` 停止并 fence Connector/Consumer。
3. Broker：优先排空目标 Consumer Group；无法证明排空时隔离 Topic/ACL，禁止静默丢弃未消费消息。
4. 取得覆盖数据库 producer fence 的 CDC 位点机器证明后再进入最终回退事务。

**停止条件：** 位点不覆盖 producer fence；控制面 fence token 与 generation 不一致；排空超时且未完成隔离。

## 4. 回退（CdcKafka → LegacyPolling）

1. `POST /api/v1/messaging/delivery/rollback`（权限 `messaging.delivery.rollback`）。
2. 确认两阶段：Preparing → 控制面 fence/排空/位点证明 → 最终事务切回 Legacy。
3. 失败补偿：先按同一 generation 恢复控制面，再解除 producer fence；控制面失败则保持 Preparing。
4. 回退后 Kafka 消息保持未提交，不得进入业务 Handler / Retry / DLQ。

**回滚条件（回退本身失败时）：** 保留 Preparing；人工按本 runbook 第 2～3 节隔离；禁止强行清除 fence。

## 5. DLQ 重放

1. 仅使用受控 API/作业重放；必填审计原因；禁止临时改 Consumer 自动订阅 DLQ。
2. 重放前确认 Inbox 幂等与当前所有权；`already_processed` 不得产生二次副作用。
3. 观察 `dead_letter.published` 下降不得作为成功标准；以业务对账与 Inbox 结果为准。

**停止条件：** Payload 损坏、同 ID 不同 Hash、目标所有权不是预期 Owner。

## 6. 对账（Reconcile）

1. 对试点流执行 Identity 机构单元投影对账：`reconcile_dry_run` → 审查 → `reconcile_apply`。
2. 比对 Outbox / Kafka / Inbox / 投影版本；缺口写入事件单，禁止手工改 Inbox 主键。

**停止条件：** dry_run 显示不可解释缺口或跨租户差异。

## 7. 告警与平台占位指标

下列指标可由应用 `KafkaMessagingTelemetry.UpdateCdcPlatformHealth` / `UpdateConnectorHealth` 或外部 exporter 填充；缺省开发值不得当作生产健康证明：

- SQL Server Capture Job 停止 → `FullNetMessagingSqlServerCdcCaptureJobStopped`
- MySQL Binlog 保留不足 → `FullNetMessagingMySqlBinlogRetentionLow`
- Connector Offset 不可恢复 → `FullNetMessagingConnectorOffsetUnrecoverable`
- Kafka lag 近保留 → `FullNetMessagingKafkaLagNearRetention`

规则文件：[`deploy/observability/prometheus-rules.yaml`](../../deploy/observability/prometheus-rules.yaml)。
