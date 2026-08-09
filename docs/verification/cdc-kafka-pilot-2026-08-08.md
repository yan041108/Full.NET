# CDC Kafka 试点切流验证记录（2026-08-08）

> 基线提交：Task 11 工作区（`messaging-cdc-kafka-task11` 快照，HEAD `b70e1fc0` 之上未提交变更）。
> 环境：本地 Windows + Docker Testcontainers（SQL Server 2022、MySQL 8.4）。
> 2026-08-09 复审结论：本记录的 **Build-verified / Pilot 已撤销**，当前仅为 **Designing / Shadow-only**。
> 原始命令只能证明切流控制面与模拟数据路径，不能证明真实业务生产者 → CDC → Kafka → Inbox → 业务消费者链路。

## 试点范围

| 项 | 值 |
| --- | --- |
| 消息类型 | `fullnet.organization.unit.changed` |
| Schema | `1` |
| Topic | `organization.unit-changed.v1` |
| 切流 API | `POST /api/v1/messaging/delivery/cutover` |
| 回退 API | `POST /api/v1/messaging/delivery/rollback` |
| 持久化表 | `fn_messaging_stream_ownership`（迁移 `094`） |

## 已执行命令与结果

| 命令 | 结果 |
| --- | --- |
| `dotnet test tests/Full.NET.IntegrationTests --filter FullyQualifiedName~EventDeliveryCutoverTests.SqlServer` | PASS |
| `dotnet test tests/Full.NET.IntegrationTests --filter FullyQualifiedName~EventDeliveryCutoverTests.MySql` | PASS |
| `dotnet test tests/Full.NET.IntegrationTests --filter FullyQualifiedName~EventDeliveryRollbackTests.SqlServer` | PASS |
| `dotnet test tests/Full.NET.IntegrationTests --filter FullyQualifiedName~EventDeliveryRollbackTests.MySql` | PASS |
| `dotnet test tests/Full.NET.UnitTests --filter FullyQualifiedName~IntegrationEventSubscriptionCatalogTests` | PASS（9） |

集成测试覆盖：

- Legacy Outbox 处理试点事件后切流，持久化 `CdcKafka` 所有权与 cutoff 边界。
- 切流后 Legacy `OutboxProcessor` 不再调用 Handler（`outbox.legacy_owner_revoked`）。
- 回退后恢复 `LegacyPolling` 所有权并再次由 Legacy Worker 处理。

## 未验证 / Capacity-not-verified

- 生产等价环境 Kafka + Debezium 端到端 lag 与 Soak。
- 双库 CDC 影子比对全量门禁（Task 8 范围，试点切流测试使用镜像 append-only 行模拟 cutoff）。
- N+1 Broker、retention 排空与灾难恢复演练。
- Organization 生产配置下 append-only Outbox 与 Legacy 轮询并存的完整排空时序（Organization 当前仍使用 Legacy `IOutboxWriter` 无 metadata  overload）。

## 2026-08-09 复审发现

- `TenantUnitManagementService` 仍通过 Legacy `IOutboxWriter` 写 `fn_outbox_message`，Debezium 不会从真实业务写入获得试点事件。
- 生产依赖注入中没有任何 `IIntegrationEventSubscription`；Kafka Worker 即使启动也没有业务路由。Worker 现已在 `CdcKafka` 模式下对此失败关闭。
- `MessagingWorkerMode.CdcKafka` 会停止整个 Legacy `OutboxProcessor`，与“仅切一个事件流、其他事件流继续轮询”的所有权模型冲突。
- 原集成测试通过镜像 append-only 行模拟 cutoff，没有验证真实生产者、Debezium、Kafka、Inbox 和 Identity 投影副作用的完整链路。

在 [`2026-08-09-cdc-kafka-real-pilot-correction`](../superpowers/plans/2026-08-09-cdc-kafka-real-pilot-correction.md) 全部验收前，不得恢复 `Pilot` 状态或调用正式切流 API。

## 运维入口

见 [`docs/operations/cdc-kafka-event-delivery.md`](../../operations/cdc-kafka-event-delivery.md)。
