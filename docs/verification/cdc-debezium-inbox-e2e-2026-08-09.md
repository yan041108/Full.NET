# CDC → Debezium → Kafka → Inbox E2E 验证（2026-08-09）

**状态：** 进行中（Task 2）  
**切流门禁：** `Messaging:DeliveryCutover:Enabled` 保持 `false`；容量标记 `Capacity-not-verified`

## 固定测试镜像

| 组件 | 镜像 |
| --- | --- |
| Kafka | `apache/kafka:4.1.2` |
| Debezium Connect | `quay.io/debezium/connect:3.4.3.Final` |
| SQL Server | `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04` |
| MySQL | `mysql:8.0`（`ROW` + `FULL` binlog row image） |

## 已落地基础设施

- `CdcDebeziumPipelineFixture`：Kafka + Connect 测试栈（内部 `kafka:9092`，宿主 `EXTERNAL` 监听器）
- `DebeziumConnectAdminClient` / `DebeziumConnectorTemplateFactory`：Connect REST 与 `deploy/messaging/connectors/*` 模板替换
- `CdcDebeziumE2ESupport`：Kafka 消费与 Inbox 断言辅助
- `SqlServerCdcDebeziumInboxE2ETests` / `MySqlCdcDebeziumInboxE2ETests`：真实链路入口（环境不足时 `Assert.Inconclusive`）
- Connector 模板补充 Outbox → Kafka header 映射（`event_id`、`message_type`、`trace_parent` 等）
- `MessagingOutboxTestSupport`：CDC 测试写入 append-only 表（`CdcKafka` owner + permissive gate）

## 本地命令

```powershell
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj `
  --filter "FullyQualifiedName~MySqlCdcDebeziumInboxE2ETests"
```

**最新结果（2026-08-09）：** MySQL 7/7 通过（含失败场景）；SQL Server happy path 仍为 Inconclusive。

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj `
  --filter "FullyQualifiedName~SqlServerCdcDebeziumInboxE2ETests"
```

## 当前结果（2026-08-09）

| 场景 | SQL Server | MySQL |
| --- | --- | --- |
| Connect 启动 | 待验证 | 已通过（~18s 就绪） |
| Connector task 健康 | 待验证 | 已通过（schema history RF=1 + kafka:9092 bootstrap） |
| Outbox INSERT → Kafka | **Inconclusive**（CDC Agent/capture job 缺口，与既有 shadow 测试一致） | **已通过**（真实 Debezium Outbox Router） |
| Kafka → Inbox | 待验证 | **已通过**（`InboxConsumeStatus.Processed`） |
| 重复投递幂等 | 待验证 | **已通过**（第二次 `AlreadyProcessed`） |
| Connector 重启 + Schema History | 待验证 | **已通过**（删除并重建 Connector 后继续投递） |
| Offset 未提交重投 | 待验证 | **已通过**（同组未提交 Offset → `AlreadyProcessed`） |
| Connector 暂停/恢复 | 待验证 | **已通过**（暂停期间不投递，恢复后补发） |
| CDC 信封 Retry 路由 | 待验证 | **已通过**（真实 Debezium 消息进入 `.retry.5s`） |
| Broker 短暂中断 | 待验证 | **已通过**（Pause/Unpause 后继续投递） |

### 关键修复（本轮）

1. Connector `schema.history.internal.kafka.bootstrap.servers` + `topic.replication.factor=1`
2. Connector 级 `key.converter=StringConverter`（避免 JsonConverter 包装 partition key）
3. `KafkaEnvelopeReader` 兼容 Debezium Outbox：`event_id` Base64 Binary16（`bigEndian: true`）、微秒时间戳、`id` 头
4. `MessagingOutboxTestSupport` 写入 append-only 表（`CdcKafka` owner）

## 未验证项 / 风险

1. SQL Server CDC Agent/capture job 在 Testcontainers 中可能长期 `Inconclusive`（与既有 shadow 测试一致）。
2. MySQL Debezium 任务状态与 binlog 位点需进一步采集（失败时输出 Connector `/status` JSON）。
3. 失败场景矩阵：MySQL 已覆盖重复投递、Connector 重启、Offset 未提交重投、Connector 暂停/恢复、Retry 路由与 Broker 中断；DLQ/Rebalance/切流回退 E2E 仍待实现。
4. 生产 `IEventDeliveryRollbackReadinessReader` 已提供 Kafka Connect 适配器（`Messaging:KafkaConnectRollback:Enabled` 门控）；默认仍为失败关闭。

## 下一步

1. 补齐 DLQ、Rebalance、切流/回退 E2E 演练。
2. 在真实 Connect 环境验证 `KafkaConnectEventDeliveryRollbackReadinessReader` 与双库 producer fence 采集。
3. SQL Server 在具备 CDC Agent 的环境中复验 happy path。
