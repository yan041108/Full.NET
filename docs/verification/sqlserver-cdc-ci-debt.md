# SQL Server CDC CI 环境债务

**状态：** 已知环境限制（非产品缺陷）  
**更新时间：** 2026-08-16  
**关联验收：** [ADR-0006](../../docs/architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md) 验收项 #1（双库 CDC）

## 结论

| 环境 | SQL Server CDC 集成测试 | 判据 |
|------|-------------------------|------|
| 默认 Testcontainers（`mcr.microsoft.com/mssql/server:2022-*` Linux） | **Inconclusive**（预期） | SQL Server Agent 未运行，CDC capture job 无法启动 |
| MySQL Testcontainers（binlog ROW/FULL） | **Pass/Fail** | 双库对称证据已具备 |
| Nightly 外部 SQL Server（Agent 已启用） | **Pass/Fail** | 见下文 `FULLNET_TEST_SQLSERVER_CDC_CONNECTION_STRING` |

**不得**将 Testcontainers 上的 SQL Server Inconclusive 当作 merge 通过的双库 CDC 验收证据。

## 根因

1. 官方 Linux SQL Server 容器镜像**不包含可用的 SQL Server Agent**（或 Agent 非 Running）。
2. CDC 变更捕获依赖 Agent 调度 capture job；仅执行 `sp_cdc_enable_db` / `sp_cdc_enable_table` 不足以在 CI 容器内产生 `cdc.*_CT` 变更。
3. MySQL 路径已在 `SharedDatabaseFixture` 启动参数中启用 binlog ROW/FULL，故 E2E 可 Pass/Fail。

## 受影响的测试（`messaging-heavy` 分片）

- `SqlServerCdcDebeziumInboxE2ETests`
- `SqlServerCdcShadowTests`（CDC 相关方法；Kafka fingerprint 对照不依赖 CDC Agent）
- `KafkaOutboxCdcCapacityRunnerTests` — `[DataRow(DatabaseProvider.SqlServer)]`

实现入口：`tests/Full.NET.IntegrationTests/Messaging/SqlServerCdcTestSupport.cs`。

## CI 行为

### main CI / 本地 `pnpm test:integration:messaging-heavy`

- 测试**允许** Inconclusive；分片脚本在 TRX 中**单独统计** `Inconclusive` 计数（不得与 Passed 合并解读）。
- 门禁仍要求：0 Failed；Executed 数量满足 `eng/testing/test-matrix.json` 中 `messaging-heavy.minimum`。

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

- 专用 Testcontainers 镜像 + init 脚本（仅当 Agent 可在容器内 Running 时有意义）。
- ADR-0006 生产切流前仍需专用环境 Pass/Fail 证据；本记录**不**升级 Delivery 为 Production-verified。

## 交叉引用

- [`cdc-debezium-inbox-e2e-2026-08-09.md`](cdc-debezium-inbox-e2e-2026-08-09.md)
- [`messaging-runtime-topology.md`](../operations/messaging-runtime-topology.md)
- [`kafka-capacity-runner.md`](../operations/kafka-capacity-runner.md)
