# CDC Kafka 事件交付运维与试点切流

本文记录 Full.NET 事务 Outbox + CDC + Kafka 试点流的运维边界、切流/回退流程与生产门禁。权威架构决策见 [`ADR-0006`](../../architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)。

## 候选试点事件流（Designing / Shadow-only）

> 当前禁止正式切流。2026-08-09 复审确认真实生产写入、业务订阅注册和单流/Legacy 并存尚未闭环；`CdcKafka` 在没有真实订阅时会拒绝启动。

| 字段 | 值 |
| --- | --- |
| 消息类型 | `fullnet.organization.unit.changed` |
| Schema | `1` |
| Topic 机器码 | `organization.unit-changed.v1` |
| 目录默认所有权 | `LegacyPolling` |
| 候选生产者 | Organization 模块 `TenantUnitManagementService`（当前仍写 Legacy Outbox） |
| 候选消费者 | Identity 模块 `OrganizationUnitChangedIntegrationEventHandler`（尚未注册 Kafka 订阅） |
| 遗留重放 | Identity 机构单元投影对账 API（`reconcile_dry_run` / `reconcile_apply`） |

选择依据：无支付/安全不可逆外部副作用；消费方通过投影版本比较收敛重复与乱序；存在 Legacy 对账路径。

## 持久化所有权记录

表 `fn_messaging_stream_ownership`（迁移 `094_MessagingStreamOwnership.sql` 创建，`095_MessagingStreamOwnershipConvergence.sql` 收敛约束和试点基线）保存：

- `CurrentOwner` / `PreviousOwner`
- `CutoffEventId` / `CutoffOccurredAtUtc`（Legacy 排空后的切流边界）
- `RollbackBoundaryEventId` / `RollbackOccurredAtUtc`（回退边界）
- `Reason`、`CdcSourcePositionJson`（可选 CDC 位点）

运行时有效所有权 = 持久化记录（若存在）否则 Topic 目录默认值。

## 运维 API

| 操作 | 路径 | 权限 |
| --- | --- | --- |
| 查询交付状态 | `GET /api/v1/messaging/delivery/status` | `messaging.events.read` |
| 切流到 CDC Kafka | `POST /api/v1/messaging/delivery/cutover` | `messaging.delivery.cutover` |
| 回退到 Legacy | `POST /api/v1/messaging/delivery/rollback` | `messaging.delivery.rollback` |

切流前置条件：

1. `Messaging:DeliveryCutover:Enabled=true` 已由运维显式配置；默认值为 `false`。
2. 目标流在 Topic 目录和 `fn_messaging_stream_ownership` 中注册，且当前有效所有权为 `LegacyPolling`。
3. 目标流 Legacy Outbox 已排空（无 pending、due retry、active lease、dead letter）；其他 Legacy 流的积压不阻塞本流切换。
4. 正式 Connector、Topic、ACL、监控、保留期和恢复演练已在目标环境完成。

生产者写 Outbox、Kafka Inbox Handler 与所有权切换使用同一数据库流级事务门：切流等待在途生产者，回退等待在途 Consumer；回退后的 Kafka 消息保持未提交，不进入业务 Handler、Retry 或 DLQ。

回退采用持久化 generation 的两阶段协议。第一数据库事务取得流级独占锁，等待已取得共享锁的生产者提交，然后写入 `RollbackState=Preparing`、`RollbackGeneration` 与准备时间并提交；此后新生产者在写任何 Outbox 前失败，Kafka Consumer 仍可完成排空。控制面随后在数据库事务外停止并 fence Connector/Consumer、排空或隔离 Broker，并取得 SQL Server CDC LSN 或 MySQL binlog position 覆盖数据库 producer fence 的可机器验证证明。最终事务重新取得独占锁，只接受同一 generation、足够新鲜且 source position 已覆盖 producer fence 的证明，再切回 Legacy 并清除准备状态。失败补偿必须先确认控制面按同一 generation 恢复成功，之后才能在数据库事务内解除 producer fence；控制面恢复失败或进程中断时保留 `Preparing`，让生产者继续失败关闭并等待运维恢复。默认实现始终失败关闭；在生产适配器和真实演练完成前，回退 API 会拒绝执行。

上述 API 只是控制面实现，不代表当前已满足数据面切流条件。真实 SQL Server/MySQL CDC 端到端验收完成前必须保持开关关闭。

## Worker 模式

| 模式 | Outbox 轮询 | 正式 Kafka Consumer |
| --- | --- | --- |
| `LegacyPolling`（默认） | 是 | 否 |
| `ShadowCdc` | 是（影子比对） | 否 |
| `HybridKafka` | 是；只处理仍由 Legacy/Shadow 拥有的流 | 是；只允许 `CdcKafka` 所有权流进入 Inbox |

生产在真实双库 CDC E2E 验收前必须保持 `LegacyPolling`。`CdcKafka` 枚举值仅作为 `HybridKafka` 的一版过渡别名；不能因一个试点流关闭全局轮询。

## 生产门禁（未验证项）

下列项在专用生产等价环境认证前必须标记 **Capacity-not-verified**：

- 双库 CDC 影子 Soak 与 lag SLO
- Kafka N+1 与 retention/recovery 演练
- 试点流生产切流后的端到端 lag 与重复消费审计

Capability 状态见 [`capability-status.md`](../../roadmap/capability-status.md)。
