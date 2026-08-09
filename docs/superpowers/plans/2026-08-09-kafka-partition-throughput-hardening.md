# Kafka Partition Throughput Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改变至少一次交付、分区内顺序、Inbox 原子事务和单一事件流所有权的前提下，让 Kafka Consumer 按已分配分区并行处理，并减少每条消息的数据库往返。

**Architecture:** Kafka SDK 的 Consume、Pause/Resume、Seek、Commit 与 Rebalance 回调只在 Consumer 循环串行执行；每个已分配分区拥有容量为 1 的有界处理通道，同分区单消费者顺序执行，不同分区独立并行。处理完成结果回送给循环，由带分配代次 Fence 的 Offset 跟踪器只推进连续成功水位；消费者所有权锁定查询直接返回当前 Owner，Inbox 使用双库各自的单次往返 claim 语句。

**Tech Stack:** .NET 10、System.Threading.Channels、Confluent.Kafka 2.15.0、Dapper、SQL Server、MySQL、MSTest/NSubstitute、OpenTelemetry。

## Global Constraints

- 端到端语义保持至少一次，禁止宣称 Exactly-Once。
- 同一 Kafka Partition 严格顺序；不同 Partition 可以并行；失败 Offset 之前不得提交，之后不得越过。
- `IConsumer` 的 Consume、Pause/Resume、Seek、Commit 和 Rebalance 状态变更不得从 Handler 任务调用。
- Rebalance 撤销分区时取消该分区通道；旧分配代次的迟到完成不得提交新 Owner 的 Offset。
- Inbox、业务写入、下游 Outbox 和 processed 标记继续处于同一本地数据库事务。
- SQL Server 与 MySQL 的 claim 优化必须成对实现和真实集成验证。
- 不引入 CAP、MassTransit、新 Broker、新缓存实现或数据库迁移。
- 容量认证前继续标记 `Capacity-not-verified`，不承诺固定生产 QPS。

---

### Task 1: 建立分区通道与 Offset 水位契约

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaPartitionWorkScheduler.cs`
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaPartitionOffsetTracker.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaPartitionWorkSchedulerTests.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaPartitionOffsetTrackerTests.cs`

**Interfaces:**
- Produces: `KafkaPartitionWorkScheduler.TrySchedule(ConsumeResult<string, byte[]>, long assignmentEpoch)`，每分区最多一条在途消息。
- Produces: `KafkaPartitionProcessingResult`，包含 TopicPartition、Offset、分配代次和是否可确认。
- Produces: `KafkaPartitionOffsetTracker.Track/Complete/Revoke`，仅返回连续成功水位对应的下一 Offset。

- [x] **Step 1: 写并行、顺序、有界和水位 RED 测试**

  测试必须证明：两个分区的 Handler 可同时开始；同分区第二条在第一条完成前不能调度；存在未成功消息时不能返回更高提交位点；Kafka Offset 数字有空洞时按消费序列推进；旧分配代次完成被忽略。

- [x] **Step 2: 运行 RED**

  Run: `pnpm test:dotnet:unit -- --filter "FullyQualifiedName~KafkaPartitionWorkSchedulerTests|FullyQualifiedName~KafkaPartitionOffsetTrackerTests"`

  Expected: FAIL，因为分区调度器和水位跟踪器尚不存在。

- [x] **Step 3: 实现最小调度器与跟踪器**

  使用 `Channel.CreateBounded`，`SingleReader=true`、`SingleWriter=true`、`FullMode=Wait`；通道任务只调用注入的消息处理委托，不持有 Kafka Consumer。跟踪器按实际消费顺序保存 Offset，不用数值连续性推断 Kafka 日志中间 Offset 必然存在。

- [x] **Step 4: 运行 GREEN**

  Run: `pnpm test:dotnet:unit -- --filter "FullyQualifiedName~KafkaPartitionWorkSchedulerTests|FullyQualifiedName~KafkaPartitionOffsetTrackerTests"`

  Expected: PASS。

### Task 2: 把 Consumer Loop 改成分区并行和局部背压

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaConsumerWorker.cs`
- Remove: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaConsumerFlowControl.cs`
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaMessagingOptions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaMessagingTelemetry.cs`
- Modify: `tests/Full.NET.UnitTests/Messaging/KafkaMessagingOptionsTests.cs`
- Replace: `tests/Full.NET.UnitTests/Messaging/KafkaConsumerFlowControlTests.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaConsumerLoopCoordinatorTests.cs`

**Interfaces:**
- Consumes: Task 1 scheduler/completion result/tracker。
- Produces: Consumer Loop 的单线程 Kafka 命令协调：仅暂停繁忙或失败的分区，其他分区持续 Consume；成功后提交 tracker 返回的水位；失败后 Seek 当前 Offset 并在有界退避后恢复该分区。

- [x] **Step 1: 写 Consumer 协调 RED 测试**

  用替代 Consumer 断言只暂停消息所属分区、其他分区继续进入处理；完成前不会 Commit；失败 Seek 不影响其他分区；Commit 使用 `offset + 1` 水位；Revoke 后迟到结果不 Commit。

- [x] **Step 2: 运行 RED**

  Run: `pnpm test:dotnet:unit -- --filter "FullyQualifiedName~KafkaConsumerLoopCoordinatorTests|FullyQualifiedName~KafkaMessagingOptionsTests"`

  Expected: FAIL，现有 FlowControl 会暂停全部 Assignment 且串行等待。

- [x] **Step 3: 实现 Consumer Loop 协调器**

  ConsumerBuilder 注册 Assigned/Revoked 回调维护单调递增分配代次；Poll 循环先排空处理完成结果和到期恢复队列，再做短时 Consume。消息调度后只 Pause 该 TopicPartition；所有 Kafka SDK 操作留在循环内。关闭时停止接收、取消通道并在 `ShutdownDrainSeconds` 内观察全部任务。

- [x] **Step 4: 增加低基数指标**

  记录 paused、resumed、retry_scheduled、offset_committed、offset_commit_failed、revoked 和 stale_completion 等稳定分区流结果；标签只使用 Provider、TopicCode、ConsumerName 和稳定结果码，不记录原始 key、tenant 或异常文本。实时 Gauge 与 Pause 时长 Histogram 留待容量压测阶段按证据补充。

- [x] **Step 5: 运行 GREEN 与 Kafka 集成测试**

  Run: `pnpm test:dotnet:unit -- --filter "FullyQualifiedName~Kafka"`

  Run: `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj --filter "FullyQualifiedName~KafkaSubscriptionTests|FullyQualifiedName~KafkaFailureRecoveryTests"`

  Expected: PASS；同 key/partition 顺序保持，不同 partition 并行，重投语义不变。

### Task 3: 合并消费者所有权 Gate 与 Owner 查询

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Abstractions/IEventStreamOwnershipGate.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperEventStreamOwnershipGate.cs`
- Modify: `src/BuildingBlocks/Full.NET.Modularity/Messaging/IntegrationEventConsumerDispatcher.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/ServiceCollectionExtensions.cs`
- Modify: `tests/Full.NET.UnitTests/Messaging/IntegrationEventConsumerDispatcherTests.cs`
- Create: `tests/Full.NET.UnitTests/Data/DapperEventStreamOwnershipGateTests.cs`

**Interfaces:**
- Produces: `EventStreamConsumerFenceResult` 与 `AcquireConsumerFenceAsync`；默认接口实现保留现有自定义 Gate 的兼容回退，Dapper 实现用锁定查询一次返回存在性和 `CurrentOwner`。

- [x] **Step 1: 写单查询 RED 测试**

  断言 Dapper fence 只调用一次 `IQueryExecutor`；Dispatcher 在 fence 受支持时不调用 `IEffectiveEventDeliveryOwnerResolver`；无记录和非 `CdcKafka` Owner 均失败关闭；兼容 Gate 仍走旧回退。

- [x] **Step 2: 运行 RED**

  Run: `pnpm test:dotnet:unit -- --filter "FullyQualifiedName~IntegrationEventConsumerDispatcherTests|FullyQualifiedName~DapperEventStreamOwnershipGateTests"`

  Expected: FAIL，因为现有 Dispatcher 固定执行 Gate 与 Owner 两次查询。

- [x] **Step 3: 实现 fence 查询和兼容回退**

  Dapper 共享锁语义保持不变，查询直接投影 `CurrentOwner`；Dispatcher 用 catalog 校验并解析持久化 Owner。禁止使用无版本普通缓存替代数据库 Fence。

- [x] **Step 4: 运行 GREEN**

  Run: `pnpm test:dotnet:unit -- --filter "FullyQualifiedName~IntegrationEventConsumerDispatcherTests|FullyQualifiedName~DapperEventStreamOwnershipGateTests|FullyQualifiedName~DapperRoutedOutboxWriterTests"`

  Expected: PASS。

### Task 4: 把 Inbox Claim 压缩为单次数据库往返

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Inbox/InboxSql.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Inbox/DapperIntegrationEventInbox.cs`
- Create: `tests/Full.NET.UnitTests/Messaging/DapperIntegrationEventInboxTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Messaging/MessagingInboxSqlServerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Messaging/MessagingInboxMySqlTests.cs`
- Modify: `contracts/architecture/global-sql-statements.json`

**Interfaces:**
- Produces: SQL Server `ClaimSqlServer` 和 MySQL `ClaimMySql`，每次 Claim 通过一次 `QuerySingleOrDefaultAsync` 返回 Status/PayloadHash；`MarkProcessedAsync` 仍在 Handler 成功后单独执行并保留事务边界。

- [x] **Step 1: 写往返预算与并发 RED 测试**

  Unit 断言首次、processed 重复、failed 重放和 payload mismatch 都只执行一次 Claim 查询且不再执行独立 Insert/Reset。双库集成继续覆盖并发相同 MessageId、不同 Payload 冲突和 Handler 回滚。

- [x] **Step 2: 运行 RED**

  Run: `pnpm test:dotnet:unit -- --filter "FullyQualifiedName~DapperIntegrationEventInboxTests"`

  Expected: FAIL，现有首次路径为 SELECT + INSERT，failed 路径为 SELECT + UPDATE。

- [x] **Step 3: 实现双库原子 claim**

  SQL Server 在一个 batch 内用唯一键范围 `UPDLOCK,HOLDLOCK` 后条件 INSERT/RESET 并返回一行；MySQL 用原子 `INSERT ... ON DUPLICATE KEY UPDATE` 后在同一 command 内 SELECT，绝不覆盖原 PayloadHash。业务 Handler 仍在 claim 返回后执行。

- [x] **Step 4: 运行双库 GREEN**

  Run: `pnpm test:integration:affected:plan -- --snapshot codex-kafka-partition-throughput-20260809 --phase slice`

  Run: `pnpm test:integration:affected -- --snapshot codex-kafka-partition-throughput-20260809 --phase slice`

  Expected: SQL Server/MySQL Inbox 并发、回滚、重复与下游 Outbox 原子性全部 PASS。

### Task 5: 文档、性能证据与合并候选验证

**Files:**
- Modify: `docs/superpowers/specs/2026-08-08-transactional-outbox-cdc-kafka-design.md`
- Modify: `docs/verification/cdc-debezium-inbox-e2e-2026-08-09.md`
- Modify: `eng/testing/test-matrix.json`（仅当新增测试场景需要选择器登记）

**Interfaces:**
- Produces: 设计文档中的分区并行、Offset 水位、Rebalance Fence、单次 Inbox Claim 往返和容量未认证边界。

- [x] **Step 1: 记录可复现验证**

  记录基线提交 `35d07cefafeb9879330eddb68228b0c0d8240b2b`、Release 配置、Kafka 分区数、消息规模、并发 Handler、吞吐/P95/P99、重复率、提交失败与数据库命令次数。没有生产等价环境时明确写 `Capacity-not-verified`。

- [x] **Step 2: 运行合并候选门禁**

  Run: `pnpm test:integration:affected:plan -- --snapshot codex-kafka-partition-throughput-20260809 --phase merge`

  Run: `pnpm test:integration:affected -- --snapshot codex-kafka-partition-throughput-20260809 --phase merge`

  Run: `pnpm test:dotnet:architecture`

  Run: `pnpm test:naming`

  Run: `pnpm test:sql-safety`

  Run: `git diff --check`

  Expected: 所有命中门禁 PASS；只报告真实执行结果，不把未运行的生产压测写成已验证容量。
