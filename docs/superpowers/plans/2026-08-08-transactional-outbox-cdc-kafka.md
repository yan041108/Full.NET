# Transactional Outbox CDC Kafka Implementation Plan

> **2026-08-09 审查更正：** Task 11 的现有交付没有满足本计划“真实事件、不得合成切流”的退出门槛，`Build-verified / Pilot` 结论已撤销。Task 11 不得按已完成处理；后续以 [`2026-08-09-cdc-kafka-real-pilot-correction.md`](2026-08-09-cdc-kafka-real-pilot-correction.md) 为执行入口。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不引入 CAP/MassTransit、不降低业务事务原子性的前提下，为 Full.NET 建立追加式 Outbox、SQL Server CDC/MySQL Binlog、Kafka 发布订阅、消费 Inbox、重试/DLQ、影子验证和受控切换闭环。

**Architecture:** 业务事务只追加 `fn_messaging_outbox_event`；Debezium 从 SQL Server CDC 或 MySQL ROW Binlog 捕获提交后的 `INSERT` 并发布 Kafka；.NET Consumer 在本地事务内通过 `(ConsumerName, MessageId)` Inbox 去重、处理业务写入和下游 Outbox，提交后手工提交 Offset。旧轮询 Worker 按事件流保留，正式切换时只有一个发布所有者。

**Tech Stack:** .NET 10、Dapper、SQL Server、MySQL、MessagePack、Apache Kafka 4.1.2、Debezium 3.4.3.Final、Confluent.Kafka 2.15.0、Testcontainers.Kafka 4.13.0、OpenTelemetry、MSTest/Microsoft Testing Platform。

## Global Constraints

- 批准依据：[`ADR-0006`](../../architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md) 与[正式 Spec](../specs/2026-08-08-transactional-outbox-cdc-kafka-design.md)。
- 可靠业务事件端到端只声明至少一次；禁止声明 Exactly-Once。
- 业务事务内禁止访问 Broker；业务状态与 Outbox 必须同数据库事务提交。
- SQL Server 与 MySQL 是正式 Provider；所有表、迁移、CDC、Inbox 和恢复路径必须成对验证。
- 不引入 CAP、MassTransit、商业 Schema Registry、每租户 Topic 或业务模块 Kafka 直接依赖。
- 同一正式事件流只有一个发布所有者；Shadow Topic 不得绑定业务消费者。
- 缓存失效、日志、Trace、Metrics、普通 HTTP Operation Log 和 Audit 不进入可靠事件 Outbox。
- 当前工作区已有大量用户改动。执行前必须创建新的任务快照并只提交本计划任务修改；禁止清理、覆盖或顺手格式化无关文件。
- 每个任务独立评审、独立测试、独立提交；任何停止条件命中时不得继续下一任务。

---

## File Structure

### 新增核心项目

- `src/BuildingBlocks/Full.NET.Messaging.Abstractions/`：Envelope、Metadata、Subscription、Topic Catalog 和失败分类，不引用 Kafka/Dapper。
- `src/BuildingBlocks/Full.NET.Messaging.Kafka/`：Confluent.Kafka Provider、Consumer Loop、Options、健康和遥测。
- `src/Modules/Full.NET.Modules.Messaging/`：受保护的消息运维查询、DLQ/重放和切换命令；不承载 Broker SDK。

### 复用现有项目

- `Full.NET.Data.Abstractions`：保留 `IOutboxWriter` 公共数据事务边界并引用 Messaging Abstractions。
- `Full.NET.Data.Dapper`：实现追加式 Outbox 与 Inbox Store，避免创建没有额外隔离收益的 Dapper 项目。
- `Full.NET.Migrations.DbUp`：SQL Server/MySQL 成对迁移。
- `Full.NET.Host.Worker`：装配旧 Outbox Worker、Kafka Consumer 与单一所有权配置。
- `deploy/messaging/`：Kafka/Debezium 开发配置、Connector 模板和运维说明；生产状态服务不安装进应用 Helm Chart。

## Cursor execution contract

Cursor 每次只执行一个 `Task N`：先读取本计划、ADR-0006、正式 Spec、根 `AGENTS.md` 和任务涉及目录的规则；以两位任务号运行 `pnpm test:task:start -- messaging-cdc-kafka-taskNN`（例如 Task 4 使用 `messaging-cdc-kafka-task04`）；执行 RED → GREEN → 聚焦验证；检查影响集和 diff；提交后再进入下一 Task。Cursor 不得把计划中的后续任务合并成一次大改，也不得在 Shadow 验证完成前启用正式业务 Consumer。

### Task 1: 建立 Messaging Abstractions 与契约测试

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/Full.NET.Messaging.Abstractions.csproj`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/IntegrationEventEnvelope.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/IntegrationEventMetadata.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/IIntegrationEventSubscription.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/IntegrationEventFailure.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/MessagingNames.cs`
- Modify: `Full.NET.slnx`
- Modify: `src/BuildingBlocks/Full.NET.Data.Abstractions/Full.NET.Data.Abstractions.csproj`
- Modify: `src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxWriter.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/IntegrationEventEnvelopeTests.cs`
- Test: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`

**Interfaces:**
- Produces: `IntegrationEventEnvelope`, `IntegrationEventMetadata`, `IIntegrationEventSubscription` exactly as declared in Spec §4/§7.3.
- Produces: old `IOutboxWriter.AddAsync(eventType, schemaVersion, payload, cancellationToken)` remains source-compatible; new overload accepts `IntegrationEventMetadata` before `CancellationToken`.

- [ ] **Step 1: create a task snapshot and write failing contract tests**

Run:

```powershell
git rev-parse HEAD
pnpm test:task:start -- messaging-cdc-kafka-task01
```

Write tests asserting invalid empty `PartitionKey`, invalid `SchemaVersion`, overlong names and invalid `TraceParent` are rejected, and Architecture Tests reject any module reference to `Confluent.Kafka`.

- [ ] **Step 2: run RED verification**

Run:

```powershell
pnpm test:dotnet:unit -- --filter "FullyQualifiedName~IntegrationEventEnvelopeTests"
```

Expected: FAIL because the Messaging Abstractions types do not exist.

- [ ] **Step 3: implement the minimal contracts**

Implement constructors/factories that validate Spec §4. Keep Kafka names as values, never expose `TopicPartitionOffset` or other SDK types. Add the new project to `Full.NET.slnx` and the Data Abstractions project reference.

- [ ] **Step 4: run GREEN and architecture verification**

```powershell
pnpm test:dotnet:unit -- --filter "FullyQualifiedName~IntegrationEventEnvelopeTests"
pnpm test:dotnet:architecture
```

Expected: both commands PASS; Architecture Tests still discover the matrix minimum.

- [ ] **Step 5: commit only Task 1 files**

```powershell
git add Full.NET.slnx src/BuildingBlocks/Full.NET.Messaging.Abstractions src/BuildingBlocks/Full.NET.Data.Abstractions tests/Full.NET.UnitTests/Messaging/IntegrationEventEnvelopeTests.cs tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs
git commit -m "feat: add integration event messaging contracts"
```

### Task 2: 新增追加式 Outbox 与双库迁移

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/091_MessagingOutboxInboxExpand.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/091_MessagingOutboxInboxExpand.sql`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/AppendOnlyOutboxMessage.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperAppendOnlyOutboxWriter.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/ServiceCollectionExtensions.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/MessagingOutboxSchemaAssertions.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/MessagingOutboxSqlServerTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/MessagingOutboxMySqlTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Migrations/Migration091MessagingOutboxInboxRecoveryTests.cs`
- Modify: `eng/testing/test-matrix.json`

**Interfaces:**
- Consumes: Task 1 `IntegrationEventMetadata` and existing `ICommandExecutor/ICommandTransaction`.
- Produces: `fn_messaging_outbox_event` and `fn_messaging_inbox_message` schemas from Spec §5.
- Produces: `DapperAppendOnlyOutboxWriter` registered as `IOutboxWriter` only when `Messaging:Outbox:Mode=AppendOnlyV2`; default remains legacy until Task 8.

- [ ] **Step 1: write failing dual-provider schema and transaction tests**

Tests must assert table/column/type/index/constraint parity, `Id` UUID storage, append-only insert, business rollback removes Outbox, and a migration rerun from partially-created tables converges.

- [ ] **Step 2: run RED affected plan and tests**

```powershell
pnpm test:integration:affected:plan -- --snapshot messaging-cdc-kafka-task02 --phase inner
pnpm test:integration:affected -- --snapshot messaging-cdc-kafka-task02 --phase inner
```

Expected: migration/schema tests FAIL because migration 091 and writer do not exist.

- [ ] **Step 3: implement migrations and writer**

Use UUID v7 application IDs, SQL Server nonclustered PK plus `(OccurredAtUtc, Id)` clustered index, MySQL `BINARY(16)`, explicit constraints and paired names. Do not add polling status columns. Register every new `Global` SQL statement in `contracts/architecture/global-sql-statements.json` if the implementation introduces one.

- [ ] **Step 4: run naming, SQL safety and dual-provider GREEN**

```powershell
pnpm test:naming
pnpm test:sql-safety
pnpm test:integration:affected -- --snapshot messaging-cdc-kafka-task02 --phase slice
pnpm test:integration:partitions
pnpm test:governance
```

Expected: all commands PASS; both Provider fixtures execute migration 091 and transaction assertions.

- [ ] **Step 5: commit Task 2**

```powershell
git add src/BuildingBlocks/Full.NET.Migrations.DbUp src/BuildingBlocks/Full.NET.Data.Dapper tests/Full.NET.IntegrationTests/Messaging tests/Full.NET.IntegrationTests/Migrations/Migration091MessagingOutboxInboxRecoveryTests.cs eng/testing/test-matrix.json contracts/architecture/global-sql-statements.json
git commit -m "feat: add append-only messaging outbox and inbox schema"
```

### Task 3: 实现订阅目录与多订阅路由

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/IntegrationEventSubscriptionCatalog.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/IntegrationEventTopicDefinition.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/EventDeliveryOwner.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/LegacyIntegrationEventHandlerSubscriptionAdapter.cs`
- Modify: `src/BuildingBlocks/Full.NET.Abstractions/Messaging/IntegrationEventHandlerMatcher.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/IntegrationEventSubscriptionCatalogTests.cs`
- Test: `tests/Full.NET.ArchitectureTests/SerializationRulesTests.cs`

**Interfaces:**
- Produces: route identity `(ConsumerName, EventType, SchemaVersion)`.
- Produces: static `EventDeliveryOwner.LegacyPolling`, `ShadowCdc`, `CdcKafka`; only one owner per event stream.

- [ ] **Step 1: write RED tests for two Consumer Groups consuming one event**

Cover duplicate `ConsumerName`, same event with different ConsumerName, same route within one ConsumerName, unknown schema, invalid Topic code and simultaneous `LegacyPolling/CdcKafka` ownership.

- [ ] **Step 2: verify RED**

```powershell
pnpm test:dotnet:unit -- --filter "FullyQualifiedName~IntegrationEventSubscriptionCatalogTests"
```

Expected: FAIL because the catalog does not exist.

- [ ] **Step 3: implement catalog and legacy adapter**

Keep the legacy matcher for legacy polling. Kafka routing uses the subscription catalog and does not weaken legacy route uniqueness inside its existing owner.

- [ ] **Step 4: verify GREEN**

```powershell
pnpm test:dotnet:unit -- --filter "FullyQualifiedName~IntegrationEventSubscriptionCatalogTests|FullyQualifiedName~IntegrationEventHandlerMatcherTests"
pnpm test:dotnet:architecture
```

Expected: PASS; multiple subscriptions work only with distinct ConsumerName.

- [ ] **Step 5: commit Task 3**

```powershell
git add src/BuildingBlocks/Full.NET.Messaging.Abstractions src/BuildingBlocks/Full.NET.Abstractions/Messaging/IntegrationEventHandlerMatcher.cs tests/Full.NET.UnitTests/Messaging tests/Full.NET.ArchitectureTests/SerializationRulesTests.cs
git commit -m "feat: add integration event subscription catalog"
```

### Task 4: 实现 Inbox 本地事务幂等管道

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/IIntegrationEventInbox.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Abstractions/InboxConsumeResult.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Inbox/DapperIntegrationEventInbox.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Inbox/InboxSql.cs`
- Create: `src/BuildingBlocks/Full.NET.Modularity/Messaging/IntegrationEventConsumerDispatcher.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/IntegrationEventConsumerDispatcherTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/MessagingInboxAssertions.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/MessagingInboxSqlServerTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/MessagingInboxMySqlTests.cs`

**Interfaces:**
- Produces: `ConsumeAsync(consumerName, envelope, handler, cancellationToken)` that opens one local command transaction, claims Inbox, invokes one subscription, writes downstream Outbox and marks processed.
- Produces: duplicate `processed` returns `AlreadyProcessed`; same MessageId with a different SHA-256 returns permanent `messaging.contract.message_id_payload_mismatch`.

- [ ] **Step 1: write RED crash-boundary and duplicate tests**

Cover first processing, duplicate after commit, failure before commit, handler exception, downstream Outbox atomicity, concurrent duplicate and payload mismatch for both Providers.

- [ ] **Step 2: verify RED**

```powershell
pnpm test:dotnet:unit -- --filter "FullyQualifiedName~IntegrationEventConsumerDispatcherTests"
pnpm test:integration:affected -- --snapshot messaging-cdc-kafka-task04 --phase inner
```

Expected: FAIL because Inbox services do not exist.

- [ ] **Step 3: implement the shortest correct transaction**

Use Provider-specific insert/lock semantics behind Dapper statements. Do not hold a database transaction while waiting for Kafka. Restore `CurrentTenantAccessor` from the trusted Envelope only after catalog validation and clear it in `finally`.

- [ ] **Step 4: verify GREEN on both databases**

```powershell
pnpm test:dotnet:unit -- --filter "FullyQualifiedName~IntegrationEventConsumerDispatcherTests"
pnpm test:integration:affected -- --snapshot messaging-cdc-kafka-task04 --phase slice
```

Expected: PASS, including the “DB committed, Broker Offset not committed” duplicate simulation.

- [ ] **Step 5: commit Task 4**

```powershell
git add src/BuildingBlocks/Full.NET.Data.Abstractions src/BuildingBlocks/Full.NET.Data.Dapper/Inbox src/BuildingBlocks/Full.NET.Modularity/Messaging/IntegrationEventConsumerDispatcher.cs tests/Full.NET.UnitTests/Messaging tests/Full.NET.IntegrationTests/Messaging
git commit -m "feat: add transactional integration event inbox"
```

### Task 5: 实现可选 Kafka Provider

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/Full.NET.Messaging.Kafka.csproj`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaMessagingOptions.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaConsumerWorker.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaEnvelopeReader.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaOffsetCommitter.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaMessagingTelemetry.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/Health/KafkaHealthCheck.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/ServiceCollectionExtensions.cs`
- Modify: `Directory.Packages.props`
- Modify: `Full.NET.slnx`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaMessagingOptionsTests.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaEnvelopeReaderTests.cs`
- Test: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`
- Modify: `THIRD-PARTY-NOTICES`
- Modify: `tests/client-workspace.test.mjs`

**Interfaces:**
- Consumes: Task 3 catalog and Task 4 dispatcher.
- Produces: manual Offset commit only after `Processed/AlreadyProcessed`; transient failures do not commit; permanent failures publish DLQ before committing the source Offset.

- [ ] **Step 1: add central versions and RED tests**

Add `Confluent.Kafka` `2.15.0` and `Testcontainers.Kafka` `4.13.0` centrally. Test fail-closed production options, `EnableAutoCommit=false`, `Acks=All`, idempotence, bounded timeouts, message-size validation and secret redaction.

- [ ] **Step 2: verify RED**

```powershell
pnpm test:dotnet:unit -- --filter "FullyQualifiedName~KafkaMessagingOptionsTests|FullyQualifiedName~KafkaEnvelopeReaderTests"
```

Expected: FAIL because Kafka Provider does not exist.

- [ ] **Step 3: implement Provider without business references**

Use `Confluent.Kafka` only inside this project. Consumer Poll runs outside database transactions. Map Kafka Key/Header/Value into `IntegrationEventEnvelope`, validate catalog before tenant restoration, and expose bounded shutdown.

- [ ] **Step 4: verify Unit, Architecture, licenses and vulnerabilities**

```powershell
pnpm test:dotnet:unit -- --filter "FullyQualifiedName~Kafka"
pnpm test:dotnet:architecture
pnpm test:workspace
pnpm audit:dotnet
```

Expected: PASS; license inventory records Apache-2.0 and no module references Kafka SDK.

- [ ] **Step 5: commit Task 5**

```powershell
git add Directory.Packages.props Full.NET.slnx src/BuildingBlocks/Full.NET.Messaging.Kafka tests/Full.NET.UnitTests/Messaging tests/Full.NET.ArchitectureTests THIRD-PARTY-NOTICES tests/client-workspace.test.mjs
git commit -m "feat: add optional kafka messaging provider"
```

### Task 6: Kafka 多订阅、重试与 DLQ 集成闭环

**Files:**
- Create: `tests/Full.NET.IntegrationTests/Messaging/KafkaFixture.cs`
- Create: `tests/Full.NET.IntegrationTests/Messaging/KafkaSubscriptionTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Messaging/KafkaFailureRecoveryTests.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaRetryRouter.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaDeadLetterPublisher.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaFailureClassifier.cs`
- Modify: `eng/testing/test-matrix.json`

**Interfaces:**
- Produces: retry stages `5s`, `1m`, `15m`; final Topic suffix `.dlq`.
- Produces: stable DLQ headers from Spec §6/§8 without exception stack or Payload logging.

- [ ] **Step 1: write Kafka RED integration tests**

Use `Testcontainers.Kafka` with `apache/kafka:4.1.2`. Cover two Consumer Groups, same-group competition, same-key order, manual commit, rebalance, Broker restart, transient retry, permanent DLQ and shutdown cancellation.

- [ ] **Step 2: verify RED**

```powershell
pnpm test:integration:affected -- --snapshot messaging-cdc-kafka-task06 --phase inner
```

Expected: FAIL at missing retry/DLQ behavior, not at zero discovered tests.

- [ ] **Step 3: implement retry and dead-letter routing**

Keep Topic names from the static catalog. Reject dynamic user Topic input. Publish DLQ successfully before committing the source Offset; if DLQ publish fails, leave source uncommitted and alert.

- [ ] **Step 4: run Kafka affected slice and governance**

```powershell
pnpm test:integration:affected -- --snapshot messaging-cdc-kafka-task06 --phase slice
pnpm test:integration:partitions
pnpm test:governance
```

Expected: all Kafka tests PASS and the new suite is registered in the matrix.

- [ ] **Step 5: commit Task 6**

```powershell
git add src/BuildingBlocks/Full.NET.Messaging.Kafka tests/Full.NET.IntegrationTests/Messaging eng/testing/test-matrix.json
git commit -m "feat: add kafka retry and dead-letter delivery"
```

### Task 7: 建立双库 CDC 与 Shadow Topic 开发配置

**Files:**
- Create: `deploy/messaging/README.md`
- Create: `deploy/messaging/compose.kafka-debezium.yml`
- Create: `deploy/messaging/connectors/sqlserver-outbox-shadow.json`
- Create: `deploy/messaging/connectors/mysql-outbox-shadow.json`
- Create: `deploy/messaging/sqlserver/enable-outbox-cdc.sql`
- Create: `deploy/messaging/sqlserver/disable-outbox-cdc.sql`
- Create: `deploy/messaging/mysql/verify-binlog.sql`
- Create: `tests/deployment/messaging-cdc-contract.test.mjs`
- Modify: `package.json`
- Modify: `pnpm-workspace.yaml` only if the existing script routing requires it

**Interfaces:**
- Produces: development/test image `quay.io/debezium/connect:3.4.3.Final` and Connect configuration restricted to `fn_messaging_outbox_event` and Shadow Topic prefix `fullnet.dev.shadow.*`.
- Produces: SQL Server CDC is an explicit privileged operation, not a DbUp/API startup side effect.

- [ ] **Step 1: write RED deployment contract**

Assert pinned images, no `latest`, table include list contains only the append-only Outbox, Snapshot does not emit historical business events, MySQL ROW/FULL requirements, SQL Server stable Capture Instance, separate internal Topic and no business Consumer for Shadow. Assert application production Helm values do not reference `quay.io/debezium/*` because those images are test/evaluation inputs rather than approved production artifacts.

- [ ] **Step 2: verify RED**

```powershell
node --test tests/deployment/messaging-cdc-contract.test.mjs
```

Expected: FAIL because deployment artifacts do not exist.

- [ ] **Step 3: implement deploy artifacts and runbook**

Pin Kafka/Debezium versions and document environment-only Secret injection. Document that production Connect images must be built by the trusted platform from fixed Debezium Connector artifacts or use an approved supported distribution, with scan/sign/SBOM/digest evidence. Do not add Kafka or Debezium to `deploy/helm/fullnet` as application-owned production state services; only later Task 10 adds application connection references.

- [ ] **Step 4: verify deployment contracts**

```powershell
node --test tests/deployment/messaging-cdc-contract.test.mjs
pnpm test:helm
pnpm test:observability-deploy
```

Expected: PASS; templates contain no real credentials.

- [ ] **Step 5: commit Task 7**

```powershell
git add deploy/messaging tests/deployment/messaging-cdc-contract.test.mjs package.json pnpm-workspace.yaml
git commit -m "infra: add dual-provider cdc shadow configuration"
```

### Task 8: 双库 CDC 端到端影子验证

**Files:**
- Create: `tests/Full.NET.IntegrationTests/Messaging/CdcShadowFixture.cs`
- Create: `tests/Full.NET.IntegrationTests/Messaging/SqlServerCdcShadowTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Messaging/MySqlBinlogShadowTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Messaging/CdcCrashPointTests.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/ShadowEventComparison.cs`
- Create: `src/Hosts/Full.NET.Host.Worker/ShadowEventComparisonProcessor.cs`
- Modify: `eng/testing/test-matrix.json`

**Interfaces:**
- Produces: comparison by `EventId`, `MessageType`, `SchemaVersion`, `PartitionKey`, `PayloadHash` and monotonic source position; comparison never invokes business Handler.

- [ ] **Step 1: write RED CDC tests**

Cover committed insert appears, rolled-back insert does not appear, Connector restart from Source Offset, duplicate after Offset window, SQL Server Capture Job stop/recover, MySQL Binlog retention boundary and same Envelope semantics across Providers.

- [ ] **Step 2: verify RED in the registered CDC capability filter**

```powershell
pnpm test:integration:affected -- --snapshot messaging-cdc-kafka-task08 --phase inner
```

Expected: FAIL because shadow comparison and CDC fixtures are absent. If Docker lacks SQL Server Agent/CDC support, stop and record the environment gap; do not mark the Task complete.

- [ ] **Step 3: implement shadow comparison and fault controls**

Shadow consumer writes only comparison evidence/metrics. It must not call `IIntegrationEventSubscription`, write business projections or commit external side effects.

- [ ] **Step 4: run dual-provider CDC slice**

```powershell
pnpm test:integration:affected -- --snapshot messaging-cdc-kafka-task08 --phase slice
```

Expected: SQL Server CDC and MySQL Binlog suites both PASS with non-zero discovery and crash-point recovery.

- [ ] **Step 5: commit Task 8**

```powershell
git add tests/Full.NET.IntegrationTests/Messaging src/BuildingBlocks/Full.NET.Messaging.Abstractions/ShadowEventComparison.cs src/Hosts/Full.NET.Host.Worker/ShadowEventComparisonProcessor.cs eng/testing/test-matrix.json
git commit -m "test: verify dual-provider cdc shadow delivery"
```

### Task 9: 受控重放与消息运维模块

**Files:**
- Create: `src/Modules/Full.NET.Modules.Messaging/Full.NET.Modules.Messaging.csproj`
- Create: `src/Modules/Full.NET.Modules.Messaging/MessagingModule.cs`
- Create: `src/Modules/Full.NET.Modules.Messaging/MessagingAuthorizationContributor.cs`
- Create: `src/Modules/Full.NET.Modules.Messaging/Features/GetDeadLetters/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Messaging/Features/ReplayDeadLetter/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Messaging/Features/GetDeliveryStatus/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Messaging/Features/ChangeDeliveryOwner/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Messaging/Persistence/MessagingOperationsSql.cs`
- Modify: `src/Composition/Full.NET.Composition/FullNetModuleCatalog.cs`
- Modify: `Full.NET.slnx`
- Test: `tests/Full.NET.UnitTests/Messaging/MessagingAuthorizationTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/MessagingOperationsAssertions.cs`

**Interfaces:**
- Produces permissions: `messaging.events.read`, `messaging.dead_letters.read`, `messaging.dead_letters.replay`, `messaging.delivery.cutover`.
- Produces replay key: exact `ConsumerName + MessageId`; business re-execution is a separate compensation command and never deletes Inbox records.

- [ ] **Step 1: write RED authorization and replay tests**

Cover no permission/403, Host-only scope, exact replay audit, duplicate replay through Inbox, unknown Topic/Consumer rejection and cutover precondition failure.

- [ ] **Step 2: verify RED**

```powershell
pnpm test:dotnet:unit -- --filter "FullyQualifiedName~MessagingAuthorizationTests"
pnpm test:integration:affected -- --snapshot messaging-cdc-kafka-task09 --phase inner
```

Expected: FAIL because Messaging Module/endpoints do not exist.

- [ ] **Step 3: implement protected operations**

Use ProblemDetails, precise permissions, Domain Audit for cutover/replay, stable reason codes and paginated bounded queries. Do not expose Payload by default; an explicitly authorized detail projection must redact/classify fields.

- [ ] **Step 4: verify module, authorization and dual-provider behavior**

```powershell
pnpm test:dotnet:unit -- --filter "FullyQualifiedName~Messaging"
pnpm test:dotnet:architecture
pnpm test:integration:affected -- --snapshot messaging-cdc-kafka-task09 --phase slice
```

Expected: PASS; direct unauthorized API remains 403 and replay is idempotent.

- [ ] **Step 5: commit Task 9**

```powershell
git add Full.NET.slnx src/Modules/Full.NET.Modules.Messaging src/Composition/Full.NET.Composition tests/Full.NET.UnitTests/Messaging tests/Full.NET.IntegrationTests/Messaging
git commit -m "feat: add protected messaging operations"
```

### Task 10: Worker/Helm 装配、健康和低基数指标

**Files:**
- Modify: `src/Hosts/Full.NET.Host.Worker/Program.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/appsettings.json`
- Create: `src/Hosts/Full.NET.Host.Worker/MessagingWorkerOptions.cs`
- Modify: `deploy/helm/fullnet/values.yaml`
- Modify: `deploy/helm/fullnet/values.schema.json`
- Modify: `deploy/helm/fullnet/templates/worker-deployment.yaml`
- Modify: `deploy/helm/fullnet/templates/configmap.yaml`
- Modify: `deploy/observability/prometheus-rules.yaml`
- Modify: `deploy/observability/grafana-dashboard.json`
- Modify: `tests/deployment/helm-contract.test.mjs`
- Modify: `tests/deployment/observability-contract.test.mjs`
- Test: `tests/Full.NET.UnitTests/Messaging/MessagingWorkerOptionsTests.cs`

**Interfaces:**
- Produces: explicit modes `LegacyPolling`, `ShadowCdc`, `CdcKafka`; production default remains `LegacyPolling` until Task 11 gate.
- Produces metrics from Spec §9 without MessageId/TenantId/raw Topic/error text labels.

- [ ] **Step 1: write RED mode, Helm and metric tests**

Reject simultaneous formal owners, missing TLS/security when enabled, unbounded buffers and high-cardinality labels. Assert Shadow mode cannot register business subscriptions.

- [ ] **Step 2: verify RED**

```powershell
pnpm test:dotnet:unit -- --filter "FullyQualifiedName~MessagingWorkerOptionsTests"
pnpm test:helm
pnpm test:observability-deploy
```

Expected: FAIL at missing options/configuration.

- [ ] **Step 3: implement explicit host profiles and observability**

Keep Kafka/Debezium as external platform dependencies; Helm injects endpoints/Secret references only. Readiness fails when a required formal Consumer dependency is unavailable; liveness must not restart on ordinary lag.

- [ ] **Step 4: verify configuration and deployment**

```powershell
pnpm test:dotnet:unit -- --filter "FullyQualifiedName~MessagingWorkerOptionsTests"
pnpm test:helm
pnpm test:observability-deploy
pnpm test:dotnet:architecture
```

Expected: PASS; default chart cannot accidentally enable `CdcKafka`.

- [ ] **Step 5: commit Task 10**

```powershell
git add src/Hosts/Full.NET.Host.Worker deploy/helm/fullnet deploy/observability tests/deployment tests/Full.NET.UnitTests/Messaging
git commit -m "feat: wire messaging worker modes and telemetry"
```

### Task 11: 单一事件流试点切换与回退演练

**Files:**
- Create: `docs/operations/cdc-kafka-event-delivery.md`
- Create: `tests/Full.NET.IntegrationTests/Messaging/EventDeliveryCutoverTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Messaging/EventDeliveryRollbackTests.cs`
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/IntegrationEventSubscriptionCatalog.cs`
- Modify: one approved low-risk event producer and consumer selected from the current event catalog
- Create after successful verification: `docs/verification/cdc-kafka-pilot-2026-08-08.md`
- Modify after successful verification: `docs/roadmap/capability-status.md`

**Interfaces:**
- Pilot selection rule: consumer is naturally idempotent or Inbox-backed, has no payment/security/irreversible external side effect, and has an existing legacy replay path.
- Produces: a stable stream ownership record containing old owner, new owner, source position, cutoff EventId/time, operator, reason and rollback boundary.

- [ ] **Step 1: select and record the pilot using repository evidence**

List candidate handlers and choose the lowest-risk real event. If no event satisfies the rule, stop Task 11 and leave capability `Build-verified / Shadow-only`; do not invent a synthetic production cutover.

- [ ] **Step 2: write RED cutover/rollback tests**

Cover legacy backlog drain, owner transition, old Worker rejection after cutoff, CDC start at recorded position, no duplicate business side effect, rollback isolation and post-rollback reconciliation.

- [ ] **Step 3: verify RED, then implement the one-stream catalog change**

```powershell
pnpm test:integration:affected -- --snapshot messaging-cdc-kafka-task11 --phase inner
```

Expected: tests first FAIL at missing cutover behavior, then PASS after the minimal catalog/owner implementation.

- [ ] **Step 4: run merge-candidate affected verification**

```powershell
pnpm test:integration:affected:plan -- --snapshot messaging-cdc-kafka-task11 --phase merge
pnpm test:integration:affected -- --snapshot messaging-cdc-kafka-task11 --phase merge
pnpm test:dotnet:unit
pnpm test:dotnet:architecture
pnpm test:helm
pnpm test:observability-deploy
pnpm test:naming
pnpm test:sql-safety
pnpm test:workspace
pnpm audit:dotnet
dotnet build Full.NET.slnx -c Release --no-restore
git diff --check
```

Expected: all selected tests and build PASS with no skipped required Provider, and `git diff --check` prints nothing.

- [ ] **Step 5: write verification truthfully and update status**

Only if Shadow、cutover、rollback and all required commands passed, write the dated Verification with commit baseline, environment, commands, counts, lag, duplicates and unverified production items. Capability status may become `Build-verified / Pilot`; it must remain below `Production-verified` until production-equivalent Soak/N+1/retention/recovery certification.

- [ ] **Step 6: commit Task 11**

```powershell
git add docs/operations/cdc-kafka-event-delivery.md docs/verification/cdc-kafka-pilot-2026-08-08.md docs/roadmap/capability-status.md tests/Full.NET.IntegrationTests/Messaging src/BuildingBlocks/Full.NET.Messaging.Abstractions
git commit -m "feat: pilot cdc kafka event delivery cutover"
```

### Task 12: 旧轮询路径收缩（独立后续，默认不执行）

**Files:**
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxStore.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxStore.cs`
- Create: paired SQL Server/MySQL Contract migrations using the next available migration number at execution time
- Modify: `docs/operations/outbox-worker-topology.md`
- Modify: `docs/operations/data-retention.md`
- Test: legacy and migrated stream retirement tests

**Interfaces:**
- Consumes: all reliable event streams migrated, old Outbox backlog zero, DLQ/retry/inbox retention closed, rollback window expired and production-equivalent recovery certified.
- Produces: removal only of proven-unused legacy polling fields/indexes/code; it does not delete historical evidence without retention approval.

- [ ] **Step 1: prove all retirement preconditions**

Run catalog/backlog/DLQ/rollback audits. If any stream remains `LegacyPolling` or rollback window is open, stop and do not create a Contract migration.

- [ ] **Step 2: create a separate approved Contract plan**

Because this step is destructive and its migration number depends on the repository at that future time, create a new dated plan referencing the exact current schema and approval. This Task authorizes analysis only, not deletion.

## Plan self-review

- Spec coverage: Tasks 1–4 cover Envelope/Outbox/Subscription/Inbox；Tasks 5–6 cover Kafka/Retry/DLQ；Tasks 7–8 cover双库 CDC 与 Shadow；Tasks 9–10 cover运维、安全、权限、健康和指标；Task 11 cover单流切换与回退；Task 12 keeps destructive retirement outside initial implementation.
- Placeholder scan: implementation tasks contain exact paths, interfaces, commands and stop conditions; destructive future migration intentionally requires a new approved plan instead of guessing a filename.
- Type consistency: `EventId` maps to Outbox `Id`, Kafka `event_id`, Inbox `MessageId`; route key is consistently `(ConsumerName, EventType, SchemaVersion)`; delivery modes are consistently `LegacyPolling/ShadowCdc/CdcKafka`.

## Final implementation stop conditions

Cursor must stop and report instead of weakening behavior if any of these occurs:

- SQL Server CDC or MySQL Binlog test cannot run in the target environment；
- business+Outbox or Inbox+business+downstream Outbox cannot share one local transaction；
- same formal stream would have two publishers；
- Shadow comparison finds a missing EventId or different PayloadHash；
- dependency license/vulnerability or container image provenance is unresolved；
- production Secret/TLS/ACL cannot be configured fail-closed；
- verification would require skipping a formal Provider or lowering discovery thresholds.
