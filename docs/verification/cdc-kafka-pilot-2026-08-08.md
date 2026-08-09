# CDC Kafka 试点切流验证记录（2026-08-08）

> 基线提交：Task 11 工作区（`messaging-cdc-kafka-task11` 快照，HEAD `b70e1fc0` 之上未提交变更）。
> 环境：本地 Windows + Docker Testcontainers（SQL Server 2022、MySQL 8.4）。
> Capability 结论：**Build-verified / Pilot**（低于 Production-verified）。

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

## 运维入口

见 [`docs/operations/cdc-kafka-event-delivery.md`](../../operations/cdc-kafka-event-delivery.md)。
