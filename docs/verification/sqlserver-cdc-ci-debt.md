# SQL Server CDC CI 环境债务

**状态：** 默认共享夹具限制保留；独享 Agent 受控影子链路已本地通过

**更新时间：** 2026-10-25

**关联验收：** [ADR-0006](../../docs/architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md) 验收项 #1（双库 CDC）

## 结论

| 环境 | SQL Server CDC 集成测试 | 判据 |
|------|-------------------------|------|
| 默认 Testcontainers（`mcr.microsoft.com/mssql/server:2022-*` Linux） | **Inconclusive**（预期） | SQL Server Agent 未运行，CDC capture job 无法启动 |
| 独享 2022 CU14 Testcontainers，显式启用 Agent | **Pass**（2026-10-25 受控影子场景） | 真实事务 → CDC → Debezium → Kafka → 生产影子比较器，含暂停/恢复；不代表正式业务切流或 Inbox 全链路 |
| MySQL Testcontainers（binlog ROW/FULL） | **Pass/Fail** | 双库对称证据已具备 |
| Nightly 外部 SQL Server（Agent 已启用） | **Pass/Fail** | 见下文 `FULLNET_TEST_SQLSERVER_CDC_CONNECTION_STRING` |

**不得**将 Testcontainers 上的 SQL Server Inconclusive 当作 merge 通过的双库 CDC 验收证据。

## 根因

1. 既有共享夹具未启用 Agent，不能推导为官方 Linux 镜像不包含 Agent。新受控影子场景在独享容器设置 `MSSQL_AGENT_ENABLED=true`，实际启动捕获作业并完成真实交付；不重启共享实例。变量语义见 [Microsoft Learn](https://learn.microsoft.com/en-us/sql/linux/configure/environment-variables?view=sql-server-ver16)。
2. CDC 变更捕获依赖 Agent 调度 capture job；仅执行 `sp_cdc_enable_db` / `sp_cdc_enable_table` 不足以在 CI 容器内产生 `cdc.*_CT` 变更。
3. MySQL 路径已在 `SharedDatabaseFixture` 启动参数中启用 binlog ROW/FULL，故 E2E 可 Pass/Fail。
4. SQL Server Outbox 列 `OccurredAtUtc` 为 `datetimeoffset(7)`；Debezium Outbox EventRouter 的 `table.field.event.timestamp` 需要 INT64，配置该字段会导致 Connect task 在处理首条 CDC 变更时 FAILED。SQL Server 连接器模板因此省略该键，业务时间仍经 `OccurredAtUtc:header:occurred_at_utc` 传递。
5. 宿主机 SQL Server + Docker Debezium Connect 时，JDBC 可能触发 Java 证书算法约束；集成测试栈通过挂载 `tests/Full.NET.IntegrationTests/Messaging/Assets/debezium-connect-java.config` 放宽策略，并在 SQL Server 配置管理器中允许非加密连接（`ForceEncryption=0`）。

## 受影响的测试（`messaging-heavy` 分片）

- `SqlServerCdcDebeziumInboxE2ETests`
- `SqlServerCdcShadowTests`（CDC 相关方法；Kafka fingerprint 对照不依赖 CDC Agent）
- `KafkaOutboxCdcCapacityRunnerTests` — `[DataRow(DatabaseProvider.SqlServer)]`
- `OrganizationUnitCdcKafkaEndToEndTests`（SqlServer DataRow）
- `OrganizationUnitCdcKafkaFaultMatrixTests`（MySQL；messaging-heavy）

实现入口：`tests/Full.NET.IntegrationTests/Messaging/SqlServerCdcTestSupport.cs`。

## CI 行为

### main CI / 本地 `pnpm test:integration:messaging-heavy`

- 测试**允许** Inconclusive；分片脚本在 TRX 中**单独统计** `Inconclusive` 计数（不得与 Passed 合并解读）。
- 门禁仍要求：0 Failed；实际数量和各场景最低通过数只从 `eng/testing/test-matrix.json` 读取；不能把默认共享夹具的历史跳过结论套用到显式启动 Agent 的独享场景。

### Nightly 外部实例（可选证据链）

工作流：[`.github/workflows/sqlserver-cdc-nightly.yml`](../../.github/workflows/sqlserver-cdc-nightly.yml)

| Secret / 变量 | 用途 |
|---------------|------|
| `FULLNET_TEST_SQLSERVER_CDC_CONNECTION_STRING` | 指向已启用 Agent + CDC 的外部 SQL Server；库需可写且由测试自行迁移 |

未配置 Secret 时 workflow **跳过**（exit 0），不伪造 Pass。

## 本地复现外部 SQL Server 路径

```powershell
$env:FULLNET_TEST_SQLSERVER_CDC_CONNECTION_STRING = "Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True"
dotnet test tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll `
  --filter "FullyQualifiedName~SqlServerCdc"
```

## 后续（非本债务关闭条件）

- [真实受控影子验收](../superpowers/plans/2026-10-25-messaging-real-cdc-validation.md)已通过双库场景，最低通过数及独立选择器为矩阵 `messaging-real-cdc`；真实暂停/恢复与影子比较不再属于该场景的环境债务。
- 业务 HTTP → 正式切流/回退、多进程恢复和容量仍需相应范围的实际证据，按当前规则可在本地验证；本记录不升级 Delivery 为 Production-verified。

## 交叉引用

- [`cdc-debezium-inbox-e2e-2026-08-09.md`](cdc-debezium-inbox-e2e-2026-08-09.md)
- [`messaging-runtime-topology.md`](../operations/messaging-runtime-topology.md)
- [`kafka-capacity-runner.md`](../operations/kafka-capacity-runner.md)
