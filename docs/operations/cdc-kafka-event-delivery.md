# CDC Kafka 事件交付运维与试点切流

本文记录 Full.NET 事务 Outbox + CDC + Kafka 试点流的运维边界、切流/回退流程与生产门禁。权威架构决策见 [`ADR-0006`](../../architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)。

## 试点事件流（Build-verified / Pilot）

| 字段 | 值 |
| --- | --- |
| 消息类型 | `fullnet.organization.unit.changed` |
| Schema | `1` |
| Topic 机器码 | `organization.unit-changed.v1` |
| 目录默认所有权 | `LegacyPolling` |
| 生产者 | Organization 模块 `TenantUnitManagementService`（同事务 Outbox 追加） |
| 消费者 | Identity 模块 `OrganizationUnitChangedIntegrationEventHandler`（版本比较天然幂等） |
| 遗留重放 | Identity 机构单元投影对账 API（`reconcile_dry_run` / `reconcile_apply`） |

选择依据：无支付/安全不可逆外部副作用；消费方通过投影版本比较收敛重复与乱序；存在 Legacy 对账路径。

## 持久化所有权记录

表 `fn_messaging_stream_ownership`（迁移 `094_MessagingStreamOwnership.sql`）保存：

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

1. 目标流在 Topic 目录中注册且当前有效所有权为 `LegacyPolling`。
2. 全局 Legacy Outbox 积压已排空（无 pending/due retry/active lease）。
3. 目标流版本退役快照无 pending/dead letter。

切流成功后 Legacy `OutboxProcessor` 对该流新消息写入死信，原因码 `outbox.legacy_owner_revoked`。

## Worker 模式

| 模式 | Outbox 轮询 | 正式 Kafka Consumer |
| --- | --- | --- |
| `LegacyPolling`（默认） | 是 | 否 |
| `ShadowCdc` | 是（影子比对） | 否 |
| `CdcKafka` | 否 | 是 |

生产默认保持 `LegacyPolling`，直至试点切流门禁通过且运维完成切流 API。

## 生产门禁（未验证项）

下列项在专用生产等价环境认证前必须标记 **Capacity-not-verified**：

- 双库 CDC 影子 Soak 与 lag SLO
- Kafka N+1 与 retention/recovery 演练
- 试点流生产切流后的端到端 lag 与重复消费审计

Capability 状态见 [`capability-status.md`](../../roadmap/capability-status.md)。
