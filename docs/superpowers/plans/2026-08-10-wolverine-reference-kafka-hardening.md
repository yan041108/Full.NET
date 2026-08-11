# Wolverine 参考基线与 Kafka 高性能能力吸收实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 保持 Full.NET 自有“事务 Outbox + CDC + Kafka + Inbox”主链路，不引入 Wolverine 运行时依赖，分阶段吸收其成熟 Kafka、持久化、重放、代码生成和可观测设计。

**Architecture:** 出站继续使用追加式 `fn_messaging_outbox_event` 和 Debezium CDC，禁止恢复应用 Outbox 热表轮询。消费侧继续由单一 Kafka SDK 命令循环持有 Consumer；并行执行、Offset 水位、Inbox 原子性、Retry/DLQ 和回放均通过 Full.NET 自有边界实现。所有高吞吐模式默认关闭或保持保守默认值，只有完成双库、Kafka 故障矩阵和生产等价压测后才可提升能力状态。

**Tech Stack:** .NET 10、Confluent.Kafka 2.15、Dapper、SQL Server、MySQL、MessagePack、OpenTelemetry、MSTest、Testcontainers、Debezium/Kafka Connect。

## Global Constraints

- 不增加 `WolverineFx*`、CAP、MassTransit、MediatR 或 EF Core 运行时依赖。
- 不改变 ADR-0006 的至少一次语义，不宣称端到端 Exactly-Once。
- 同一 Kafka PartitionKey/业务 Key 必须保持顺序；任何并行模式不得越过未成功消息提交 Offset。
- Inbox、业务副作用和下游 Outbox 必须处于同一本地事务；禁止为了批量化提前提交 Inbox。
- SQL Server 与 MySQL 数据库行为必须成对实现和验证。
- Kafka Consumer 的 `Consume/Pause/Resume/Seek/Commit` 仍只能在单一 Poll 循环调用。
- 默认生产配置保持 `acks=all`、幂等 Producer、关闭自动 Offset 提交和静态 Topic Catalog。
- 未完成生产等价容量测试时状态保持 `Capacity-not-verified`。

---

## 能力盘点与实施阶段

| 能力 | 当前状态 | 计划阶段 |
| --- | --- | --- |
| 多 Topic 共用单 Consumer | 已实现：同一 ConsumerName 的 Topic 合并订阅 | F0 补契约测试 |
| Native Retry/DLQ 与诊断 Header | 已实现主体；DLQ/Rebalance E2E 未闭环 | F2 |
| 分区连续 Offset 水位 | 已实现 | F0 保持回归 |
| Cooperative Sticky | 已实现显式离线迁移门禁；存量 Group 默认 `LegacyRange`，真实迁移演练待完成 | F0 |
| Kafka 4.x Consumer Protocol | 已实现配置与 Broker 主版本互斥校验；真实入组/滚动演练待完成 | F0 |
| Static Membership | 已映射 `group.instance.id`；Deployment 滚动替换的稳定实例身份待决策 | F0/F1 |
| Producer 批量/队列/并发调优 | 已实现有界配置与单元/Helm 契约；生产等价基准待完成 | F0 |
| 高低水位统一背压 | 仅有每分区暂停和 librdkafka 队列上限 | F1 |
| 可配置 Offset Commit | 仅同步逐水位提交 | F1 |
| 同分区按 Key 分槽并行 | 缺失 | F1 |
| Inbox 批量 Claim | 单消息单命令；不能直接跨业务事务批量领取 | F2，仅实现安全批量预检/持久化方案 |
| 时间/Offset 范围一次性重放 | 缺失 | F2 |
| Handler 代码生成 | 缺失 | F3 |
| 完整 OTel、积压和所有权诊断 | 部分 Counter，缺 Gauge/Activity | F3 |

### Task 1: 固化 Wolverine 参考边界与能力状态

**Files:**
- Modify: `docs/architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md`
- Modify: `docs/superpowers/specs/2026-08-08-transactional-outbox-cdc-kafka-design.md`
- Modify: `docs/verification/cdc-debezium-inbox-e2e-2026-08-09.md`

**Interfaces:**
- Consumes: ADR-0006 当前 CDC/Kafka 可靠性交付边界。
- Produces: “参考 Wolverine、禁止引入运行时依赖”的正式决策和上表能力状态。

- [x] **Step 1: 更新 ADR 决策**

在“替代方案”中登记 Wolverine：它提供成熟 Inbox/Outbox、Kafka、Saga、Retry/DLQ，但其可靠 Outbox 依赖数据库持久化与 DurabilityAgent 轮询，不满足 Full.NET 消除 Outbox 热表轮询的目标，因此只作为参考和性能对标对象。

- [x] **Step 2: 更新事件交付 Spec**

增加配置键、互斥关系、分阶段状态和停止条件；明确 Kafka 4.x Consumer Protocol 只有 Broker 兼容性门禁通过后才可启用。

- [x] **Step 3: 更新 Verification 状态**

只把已有测试证据标记为已验证；这是 F0 基线记录，当时 F1-F3 能力继续标记 `Planned`。后续任务已按各自验证证据提升为 `Build-verified`，但生产等价容量仍为 `Capacity-not-verified`。

- [x] **Step 4: 运行文档治理测试**

Run: `pnpm test:governance`

Expected: 27 项或测试矩阵当前登记数量全部通过，Markdown UTF-8 扫描无违规。

### Task 2: Consumer Protocol、Cooperative Sticky 与 Static Membership

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaMessagingOptions.cs`
- Modify: `tests/Full.NET.UnitTests/Messaging/KafkaMessagingOptionsTests.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/appsettings.json`
- Modify: `src/Hosts/Full.NET.Host.Worker/appsettings.Development.json`

**Interfaces:**
- Produces: `KafkaConsumerGroupProtocolMode`、`KafkaMessagingOptions.ConsumerGroupProtocol`。
- Produces: `ConsumerConfig.GroupInstanceId`、`PartitionAssignmentStrategy.CooperativeSticky` 或 `GroupProtocol.Consumer`。

- [x] **Step 1: 写失败测试**

```csharp
[TestMethod]
public void BuildConsumerConfig_uses_cooperative_static_membership_for_classic_protocol()
{
    var options = CreateValidDevelopmentOptions();
    options.ConsumerGroupProtocol = KafkaConsumerGroupProtocolMode.Classic;
    var config = options.BuildConsumerConfig("fullnet.messaging.test");

    Assert.AreEqual(GroupProtocol.Classic, config.GroupProtocol);
    Assert.AreEqual(PartitionAssignmentStrategy.CooperativeSticky, config.PartitionAssignmentStrategy);
    Assert.AreEqual("fullnet.messaging.test-01", config.GroupInstanceId);
}

[TestMethod]
public void BuildConsumerConfig_consumer_protocol_removes_classic_only_settings()
{
    var options = CreateValidDevelopmentOptions();
    options.ConsumerGroupProtocol = KafkaConsumerGroupProtocolMode.Consumer;
    var config = options.BuildConsumerConfig("fullnet.messaging.test");

    Assert.AreEqual(GroupProtocol.Consumer, config.GroupProtocol);
    Assert.IsNull(config.PartitionAssignmentStrategy);
    Assert.IsNull(config.SessionTimeoutMs);
}
```

- [x] **Step 2: 运行测试确认 RED**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~KafkaMessagingOptionsTests`

Expected: FAIL，原因是枚举/配置尚不存在或 ConsumerConfig 未设置目标属性。

- [x] **Step 3: 最小实现配置映射**

```csharp
public enum KafkaConsumerGroupProtocolMode
{
    Classic = 0,
    Consumer = 1,
}
```

`Classic` 默认设置 `GroupProtocol.Classic + Range + SessionTimeoutMs`，保持与存量 eager Consumer 兼容；只有排空并停止全部旧 Group 成员、完成回退演练且设置 `CooperativeStickyMigrationCompleted=true` 后，才切换为 `CooperativeSticky`。`Consumer` 只设置 `GroupProtocol.Consumer`，不得携带 `PartitionAssignmentStrategy`、`SessionTimeoutMs`、`HeartbeatIntervalMs` 等 Classic-only 参数。两个模式均把 `ConsumerInstanceId` 映射为 `GroupInstanceId`。

- [x] **Step 4: 增加验证**

生产启用 Kafka 时 `ConsumerInstanceId` 必填；Consumer Protocol 启用时配置文本必须显式标记 Broker 最低版本为 4.0，避免误连旧 Broker。

- [x] **Step 5: 运行测试确认 GREEN**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~KafkaMessagingOptionsTests`

Expected: PASS。

### Task 3: Producer 批量、延迟和本地队列上限

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaMessagingOptions.cs`
- Modify: `tests/Full.NET.UnitTests/Messaging/KafkaMessagingOptionsTests.cs`
- Modify: `deploy/helm/fullnet/values.yaml`
- Modify: `deploy/helm/fullnet/values.schema.json`
- Modify: `deploy/helm/fullnet/templates/worker-deployment.yaml`

**Interfaces:**
- Produces: `ProducerLingerMilliseconds`、`ProducerBatchSizeBytes`、`ProducerQueueMaxMessages`、`ProducerQueueMaxKbytes`、`ProducerMaxInFlightRequests`。

- [x] **Step 1: 写失败测试**

```csharp
[TestMethod]
public void BuildProducerConfig_applies_bounded_batching_without_weakening_idempotence()
{
    var options = CreateValidDevelopmentOptions();
    options.ProducerLingerMilliseconds = 5;
    options.ProducerBatchSizeBytes = 65_536;
    options.ProducerQueueMaxMessages = 20_000;
    options.ProducerQueueMaxKbytes = 65_536;
    options.ProducerMaxInFlightRequests = 5;

    var config = options.BuildProducerConfig();

    Assert.AreEqual(5, config.LingerMs);
    Assert.AreEqual(65_536, config.BatchSize);
    Assert.AreEqual(20_000, config.QueueBufferingMaxMessages);
    Assert.AreEqual(65_536, config.QueueBufferingMaxKbytes);
    Assert.AreEqual(5, config.MaxInFlight);
    Assert.IsTrue(config.EnableIdempotence);
    Assert.AreEqual(Acks.All, config.Acks);
}
```

- [x] **Step 2: 运行 RED**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~KafkaMessagingOptionsTests`

Expected: FAIL，原因是批量配置尚未暴露。

- [x] **Step 3: 实现有界配置和失败关闭验证**

`LingerMs` 允许 0-1000，`BatchSize` 允许 1 KiB-1 MiB，Queue Messages 允许 1-1,000,000，Queue KiB 允许 1 MiB-2 GiB，幂等 Producer 的 `MaxInFlight` 只允许 1-5。

- [x] **Step 4a: 独立 Runner 与缩小验证入口落盘**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~KafkaMessagingOptionsTests`

Result: 生产配置构建器的单元门禁已通过；独立 `kafka-capacity` Runner、低速延迟与有界开放环吞吐、正确性硬门禁、预算、checkpoint、TopicId 删除保护和真实 Kafka 缩小测试均已落盘。当前开发机 Docker Engine 未运行，真实 Kafka 测试在容器启动前环境阻断，因此不登记为已通过的 Kafka 运行证据。

- [ ] **Step 4b: 专用生产等价 Kafka 低速延迟与饱和吞吐认证**

按 [`docs/operations/kafka-capacity-runner.md`](../../operations/kafka-capacity-runner.md) 手工执行并归档低速流 p50/p95/p99、高吞吐 msg/s、正确性、资源、故障与恢复证据。禁止只看批量吞吐；完成前继续 `Capacity-not-verified`。

### Task 4: 高低水位背压与可配置 Offset Commit

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaConsumerBufferPressure.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaOffsetCommitCoordinator.cs`
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaConsumerPartitionCoordinator.cs`
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaConsumerWorker.cs`
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaMessagingOptions.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaConsumerBufferPressureTests.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaOffsetCommitCoordinatorTests.cs`

**Interfaces:**
- Produces: `KafkaOffsetCommitMode.PerMessage`、`KafkaOffsetCommitMode.PeriodicWatermark`。
- Produces: `KafkaConsumerBufferPressure.TryAccept()`、`OnCompleted()`、`ShouldPause`、`ShouldResume`。

- [x] **Step 1: 写高低水位 RED 测试**

```csharp
var pressure = new KafkaConsumerBufferPressure(highWatermark: 100, lowWatermark: 60);
for (var i = 0; i < 100; i++) Assert.IsTrue(pressure.TryAccept());
Assert.IsTrue(pressure.ShouldPause);
for (var i = 0; i < 40; i++) pressure.OnCompleted();
Assert.IsTrue(pressure.ShouldResume);
```

- [x] **Step 2: 写提交批量 RED 测试**

验证周期模式只合并每分区最新连续安全水位；Rebalance、停止和批量上限触发 Flush；失败 Offset 永远不会进入待提交集合。

- [x] **Step 3: 实现状态机**

所有 Pause/Resume/Commit 命令仍由 Poll 循环执行。默认 `PerMessage` 保持现状；`PeriodicWatermark` 只有在故障矩阵通过后才能在生产配置启用。

- [x] **Step 4: 运行聚焦测试**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~KafkaConsumerBufferPressureTests|FullyQualifiedName~KafkaOffsetCommitCoordinatorTests|FullyQualifiedName~KafkaConsumerPartitionCoordinatorTests"`

Expected: PASS。

### Task 5: 同分区按业务 Key 分槽并行

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaPartitionKeySlotSelector.cs`
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaPartitionWorkScheduler.cs`
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaConsumerPartitionCoordinator.cs`
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaMessagingOptions.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaPartitionKeySlotSelectorTests.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaPartitionWorkSchedulerTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/KafkaSubscriptionTests.cs`

**Interfaces:**
- Produces: `PartitionKeyConcurrencySlots`，默认 1，最大 64。
- Produces: 稳定槽位 `slot = XxHash64(UTF8(KafkaKey)) % slotCount`。

- [x] **Step 1: 写顺序与并行 RED 测试**

同 Key 的 offset 1/3 必须严格串行；不同 Key 的 offset 1/2 可以同时进入 Handler；offset 2 先完成时提交水位不得越过仍未完成的 offset 1。

- [x] **Step 2: 实现每分区固定槽位通道**

每槽容量必须受总高水位控制；禁止为每个随机 Key 永久创建 Channel。空 Key 统一进入槽 0，超长 Key 在 Envelope 校验阶段拒绝。

- [x] **Step 3: 调整分区 Pause 规则**

槽位未满时不暂停分区；达到每分区/全局高水位才暂停；降至低水位后恢复。Revoke 取消该分区全部槽，迟到完成继续由 assignment epoch 丢弃。

- [x] **Step 4: 验证 Kafka 故障矩阵**

Run: `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~KafkaSubscriptionTests|FullyQualifiedName~KafkaFailureRecoveryTests"`

Expected: 同 Key 顺序、跨 Key 并行、Rebalance、失败不越位全部通过。

### Task 6: Inbox 安全批量化

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/IIntegrationEventInbox.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Inbox/InboxBatchPrecheckSql.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Inbox/DapperIntegrationEventInbox.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/DapperIntegrationEventInboxTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/MessagingInboxSqlServerTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/MessagingInboxMySqlTests.cs`

**Interfaces:**
- Produces: `PrecheckBatchAsync(string consumerName, IReadOnlyList<InboxMessageFingerprint> messages, ...)`。
- 保留: 单消息 `ClaimAsync` 与 Handler 业务事务原子性。

- [x] **Step 1: 写批量重复路径 RED 测试**

批量预检一次返回 `Unknown/AlreadyProcessed/PayloadMismatch`；同一批次重复 MessageId 必须失败关闭；PayloadHash 不得被覆盖。

- [x] **Step 2: 实现只读批量预检**

SQL Server 使用 TVP 或受控 JSON rowset，MySQL 使用有界派生表；批量上限 100。预检只能减少明显重复消息的往返，未知消息仍必须在各自业务事务中执行正式 `ClaimAsync`。

- [x] **Step 3: 双库并发验证**

预检与正式 Claim 之间发生并发插入时，正式 Claim 仍给出唯一正确结果；禁止把预检当锁或事务所有权。

- [x] **Step 4: 运行双库测试**

Run: `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~MessagingInboxSqlServerTests|FullyQualifiedName~MessagingInboxMySqlTests"`

Expected: 两个 Provider 全部通过。

### Task 7: 一次性 Kafka 范围重放与 DLQ 运维闭环

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/KafkaReplayContracts.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaReplayService.cs`
- Create: `src/Tools/Full.NET.Messaging.Cli/Program.cs`
- Create: `src/Modules/Full.NET.Modules.Messaging/Features/ReplayKafkaRange/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Messaging/Features/ReplayKafkaRange/KafkaRangeReplayOperationsService.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaReplayRequestTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/KafkaReplayTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/MessagingOperationsAssertions.cs`

**Interfaces:**
- Produces: `KafkaReplayRequest(TopicCode, FromTimestampUtc, ToTimestampUtc, FromOffset, ToOffset, Partitions, ReplayConsumerName, MaxMessages)`。
- Produces: `IKafkaReplayService.ReplayAsync(...)`。

- [x] **Step 1: 写请求互斥与边界 RED 测试**

时间范围和 Offset 范围不得混用；Topic 必须来自 Catalog；底层请求模型最大消息数 1-100000，当前 API 同步入口硬上限 1000 条/32 分区；生产执行需要独立权限、审计原因、显式开关、Broker Secret 和 5-45 秒整个操作超时。

- [x] **Step 2: 实现独立 Assign Consumer**

使用唯一临时 GroupId，调用 `OffsetsForTimes` 或显式 Offset，固定高水位后正向读取，不修改正式 Consumer Group Offset。消息重新进入同一 Inbox/Dispatcher，天然复用去重和 PayloadHash 校验。

- [x] **Step 3: 完善 DLQ Header**

固定保留原 TopicCode、Partition、Offset、首次失败时间、尝试次数、稳定错误码和 TraceParent；异常文本只进入脱敏正文，不进入指标标签。

- [x] **Step 4: 运行 Kafka 与 API 运维测试**

Run: `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~KafkaReplayTests|FullyQualifiedName~MessagingOperationsAssertions"`

Expected: 重放不改变正式水位，重复副作用被 Inbox 阻止，越权请求返回 403；默认配置下管理员请求也失败关闭，Helm 启用时必须显式注入 API Kafka Secret。

### Task 8: Handler 代码生成与完整可观测性

**Files:**
- Create: `src/Generators/Full.NET.Messaging.Generators/Full.NET.Messaging.Generators.csproj`
- Create: `src/Generators/Full.NET.Messaging.Generators/IntegrationEventHandlerRegistryGenerator.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/IIntegrationEventHandlerRegistry.cs`
- Modify: `src/BuildingBlocks/Full.NET.Modularity/Messaging/IntegrationEventConsumerDispatcher.cs`
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaMessagingTelemetry.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/IntegrationEventHandlerRegistryGeneratorTests.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/MessagingWorkerOptionsTests.cs`

**Interfaces:**
- Produces: `IIntegrationEventHandlerRegistry.TryResolve(string messageType, int schemaVersion, string consumerName, out IntegrationEventHandlerDescriptor descriptor)`。
- Produces: Activity `fullnet.messaging.kafka.consume`、`fullnet.messaging.inbox.transaction`、`fullnet.messaging.kafka.commit`。
- Produces: ObservableGauge `fullnet.messaging.kafka.inflight`、`buffer.depth`、`assigned.partitions`、`paused.partitions`、`ownership.revoked`。

- [x] **Step 1: 写 Generator RED 测试**

输入两个 Subscription，断言生成稳定、无反射的三键 Switch；重复键和非法 MessageType 产生编译诊断；没有 Subscription 时生成空注册表。

- [x] **Step 2: 实现增量生成器**

生成器只读取公开 Subscription 元数据，不加载业务程序集；生产 Dispatcher 优先使用生成注册表，测试/插件兼容路径保留显式 Catalog，禁止运行时扫描全部程序集。

- [x] **Step 3: 写遥测 RED 测试**

断言只使用低基数标签 `provider/database_provider/topic_code/consumer_code/message_type_code/result/reason_code`；禁止 MessageId、TenantId、原始 Topic 和异常文本。

- [x] **Step 4: 实现 Activity 和 Gauge 状态源**

Gauge 回调只读取原子快照，不枚举无限集合；遥测异常旁路不得影响消费、提交和 Rebalance。

- [x] **Step 5: 架构与性能验证**

Run: `pnpm test:dotnet:architecture`

Run: `pnpm test:performance-governance`

Expected: 依赖方向、低基数指标和性能证据门禁全部通过。

## 最终合并门禁

- [x] `pnpm test:integration:affected:plan -- --snapshot codex-wolverine-f1-20260810 --phase merge`
- [x] `pnpm test:integration:affected -- --snapshot codex-wolverine-f1-20260810 --phase merge`
- [x] `pnpm test:dotnet:architecture`
- [x] `pnpm test:naming`
- [x] `pnpm test:sql-safety`
- [x] `pnpm test:governance`
- [x] `pnpm test:performance-governance`
- [x] `git diff --check`
- [x] `git status --short`

只有 F0-F3 对应测试、故障演练和配置回退全部完成后，才把相关能力从 `Planned` 提升为 `Build-verified`；没有生产等价负载、Soak 和 N+1 证据时，整体容量状态继续为 `Capacity-not-verified`。

### F1-F3 合并候选验证证据（2026-08-10）

- `dotnet build Full.NET.slnx -c Release --no-restore`：0 警告、0 错误。
- Kafka/Replay/Offset/Dispatcher/Telemetry 聚焦单元测试：85/85 通过。
- 真实 Kafka Range Replay 与 Subscription 聚焦集成测试：7/7 通过；最终 Scoped Catalog 调整后，SQL Server/MySQL API 组合链路再次 2/2 通过。
- `pnpm test:helm`：12/12 通过，包含 API Replay 正向渲染、缺 Secret、无效 SASL 装配与非法 SASL 枚举失败关闭。
- Architecture 99/99、Naming 24/24、SQL Safety 5/5、Governance 27/27、Performance Governance 9/9 全部通过。
- `pnpm test:integration:affected -- --snapshot codex-wolverine-f1-20260810 --phase merge`：106/106 通过，用时 21 分 57 秒。
- 独立代码复审结论为 `Ready: Yes`，Critical/Important 均为 0；生产等价容量状态仍为 `Capacity-not-verified`。

### Kafka Transport Capacity Runner 补证状态（2026-08-12）

- Scope A 独立 Confluent.Kafka Runner 已实现，复用生产 TLS/SASL、幂等 Producer、队列和 Consumer 配置构建器；为后续 Scope B/C 保留 `IKafkaCapacityScenarioDriver` 扩展点。
- 聚焦 Kafka Capacity 单元测试与完整 Unit 套件通过，Integration 项目 Release 构建 0 警告、0 错误，测试分片与治理门禁通过。
- 真实 Kafka 缩小测试已覆盖低速/吞吐正确性、旁路 Group Offset 隔离、同名替换 TopicId 删除保护和取消证据；本机 Docker Engine 未运行，本轮没有把该套件记为通过。
- 专用生产等价 Kafka 的正式低速、饱和、Soak、N+1、故障和恢复证据仍未执行，Task 3 Step 4b 保持未完成，整体继续 `Capacity-not-verified`。
