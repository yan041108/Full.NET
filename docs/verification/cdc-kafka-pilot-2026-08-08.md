# CDC Kafka 试点切流验证记录（2026-08-08）

> 基线提交：Task 11 工作区（`messaging-cdc-kafka-task11` 快照，HEAD `b70e1fc0` 之上未提交变更）。
> 环境：本地 Windows + Docker Testcontainers（SQL Server 2022、MySQL 8.4）。
> 2026-08-09 复审结论：原 **Build-verified / Pilot 已撤销**；**2026-08-16 Task 6** 在 Organization 真实 API 写路径 + CDC 全链路 E2E（MySQL Pass；SQL Server 依赖外部 Agent + nightly）后恢复为 **Build-verified / Pilot**；**仍禁止** Production 切流与 `DeliveryCutover:Enabled=true`。
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
- Organization 生产写入经 `DapperRoutedOutboxWriter` 与 **effective stream ownership** 路由；`CdcKafka` 所有权下使用 metadata overload 写入 append-only Outbox（`TenantUnitManagementService.PublishUnitChangedAsync`）。

## 2026-08-09 复审发现（2026-08-16 更新）

- Organization 生产写入路径仍在向 Legacy Outbox 演进；真实 **Routed Outbox + metadata** 与 CDC 全链路 E2E 见 [`2026-08-09-cdc-kafka-real-pilot-correction`](../superpowers/plans/2026-08-09-cdc-kafka-real-pilot-correction.md) 验收项。
- Identity 已在 `AddBackgroundServices` 注册 `OrganizationUnitChangedKafkaSubscription`；HybridKafka 模式下 Worker 可路由到业务 Handler，但 **Delivery 路径仍为 Designing / Shadow-only**，切流开关默认关闭。
- `MessagingWorkerMode.CdcKafka` 作为全局 Worker 模式已收敛为 `HybridKafka` + 流级所有权；仍须按流切流，禁止误关全局 Legacy 轮询。
- 原集成测试通过镜像 append-only 行模拟 cutoff；真实生产者、Debezium、Kafka、Inbox 与 Identity 投影副作用的完整链路仍待 [`2026-08-09-cdc-kafka-real-pilot-correction`](../superpowers/plans/2026-08-09-cdc-kafka-real-pilot-correction.md) 全部验收。
- **2026-08-16 Task 6：** `OrganizationUnitCdcKafkaEndToEndTests`（Organization API → append-only Outbox → CDC → Kafka → Inbox → Identity 投影）；`OrganizationUnitCdcKafkaFaultMatrixTests`（重复 Kafka 投递幂等，MySQL）。SQL Server Pass/Fail 需 `FULLNET_TEST_SQLSERVER_CDC_CONNECTION_STRING` + nightly；Testcontainers SQL Server 仍 Inconclusive。

## 运维入口

见 [`docs/operations/cdc-kafka-event-delivery.md`](../../operations/cdc-kafka-event-delivery.md)。
