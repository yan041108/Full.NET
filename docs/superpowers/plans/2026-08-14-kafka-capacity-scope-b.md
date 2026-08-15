# Kafka Capacity Scope B Implementation Plan

**Status:** 2026-08-14 Tasks 1-5 已实现并完成本地验证；Task 6 的专用 Scope B 工作流仍保留为后续运维接入任务。SQL Server/MySQL + 真实 Kafka 缩减测试已通过，但生产等价容量矩阵尚未运行，因此状态保持 `Capacity-not-verified`。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 `worker_inbox_handler` 容量范围，测量真实 `Kafka → 分区调度/Offset 水位 → Inbox 事务 → Dispatcher → Handler` 链路，同时保持 Scope A 兼容并为 Scope C 复用消费侧测量内核。

**Architecture:** Scope B 继续使用独立 Runner 的唯一临时 Topic 和专用 Consumer Group，但消息使用生产 Integration Event Envelope，消费端复用从 `KafkaConsumerWorker` 抽出的生产消息处理器、现有分区有界调度和连续 Offset 提交水位。Inbox 使用现有 Dapper 实现和外部预迁移的专用容量数据库；Runner 不自动迁移、不接受 Production 环境，并在任何 Kafka I/O/Topic 副作用前完成数据库身份、Schema、事件所有权和清理策略预检。

**Tech Stack:** .NET 10、Confluent.Kafka、Dapper、SQL Server、MySQL、MSTest、MessagePack、GitHub Actions。

## Global Constraints

- Scope A `kafka_transport` 的 CLI、报告、Checkpoint、Topic 所有权和正常长度客户端标识保持兼容。
- Scope B 稳定机器码固定为 `worker_inbox_handler`；Scope C 本计划不实现。
- 只允许非 Production、显式执行开关、ClusterId 精确匹配、数据库名精确匹配的专用容量环境。
- Runner 不创建、迁移或删除数据库；数据库必须预迁移，并由预检验证 `fn_messaging_inbox`、`fn_messaging_stream_ownership` 契约。
- SQL Server 与 MySQL 行为必须同时验证；数据库凭据不得进入 CLI、报告、Checkpoint 或日志。
- 正确性门禁不可关闭：Broker Ack、Handler 成功、Inbox Processed/AlreadyProcessed、连续 Offset、零丢失/重复业务副作用/损坏/乱序和完整排空必须同时成立。
- 性能预算继续精确绑定 Scope、场景、环境、ClusterId、基线提交和完整参数矩阵。
- 未在专用生产等价环境执行前继续标记 `Build-verified / Capacity-not-verified`。

---

### Task 1: Scope B 数据库控制面与副作用前预检

**Files:**
- Modify: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityConfiguration.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityDriverRegistry.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityRunner.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityDatabasePreflight.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaCapacityOptionsTests.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaCapacitySchedulerTests.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaCapacityControlPlaneTests.cs`

**Interfaces:**
- Produces: `KafkaCapacityScopeCodes.WorkerInboxHandler`、`KafkaCapacityDatabaseConfiguration`、`IKafkaCapacityDriverPreflight.ValidateAsync(...)`。
- Invariant: `ValidateAsync` 必须在 `AdminClientBuilder`、`DescribeCluster` 和 `EnsureTopicAsync` 前完成。

- [ ] **Step 1: 写配置、Secret 脱敏与预检顺序 RED 测试**

覆盖：Scope B 缺少 Provider/ConnectionString/ExpectedDatabaseName 时失败；环境变量可覆盖数据库字段；`ToString()` 不泄露连接字符串；预检失败时 AdminClient/Topic I/O 调用数为零；Scope A 不要求数据库配置。

- [ ] **Step 2: 运行 RED**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~KafkaCapacityOptionsTests|FullyQualifiedName~KafkaCapacitySchedulerTests|FullyQualifiedName~KafkaCapacityControlPlaneTests"`

Expected: FAIL，原因是 Scope B 常量、数据库配置和 Driver Preflight 尚不存在。

- [ ] **Step 3: 实现最小控制面**

数据库配置只接受 `SqlServer`/`MySql`、非空连接字符串、精确 `ExpectedDatabaseName`、1–300 秒命令超时和显式清理模式。Runner 在生成 Admin 配置前调用可选 Preflight；Scope A Runtime 的 Preflight 为 `null`。

- [ ] **Step 4: 运行 GREEN 并提交**

Run: Task 1 聚焦测试。Expected: PASS。

Commit: `feat(benchmarks): guard Kafka worker capacity database`

---

### Task 2: 抽取生产 Worker 单消息处理内核

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaConsumerMessageProcessor.cs`
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/KafkaConsumerWorker.cs`
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/ServiceCollectionExtensions.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaConsumerMessageProcessorTests.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaFailureRecoveryTests.cs`

**Interfaces:**
- Produces: `IKafkaConsumerMessageProcessor.ProcessAsync(KafkaConsumerRoute, ConsumeResult<string, byte[]>, CancellationToken)`。
- Result: 返回 `ShouldCommit`、`InboxConsumeStatus?`、稳定结果码和可选 Envelope；不直接调用 Consumer 的 Commit/Seek/Pause/Resume。

- [ ] **Step 1: 写生产成功、重复、永久失败、重试、DLQ 与所有权撤销 RED 测试**

测试必须证明处理器仍通过 `IntegrationEventConsumerDispatcher` 执行真实 Inbox/事务/Handler，并保持现有 Retry/DLQ 与所有权语义。

- [ ] **Step 2: 运行 RED**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~KafkaConsumerMessageProcessorTests|FullyQualifiedName~KafkaFailureRecoveryTests"`

Expected: FAIL，处理内核尚未从 Hosted Worker 抽出。

- [ ] **Step 3: 最小抽取并让 Worker 委托处理**

Poll、分区调度、Pause/Resume/Seek/Commit 仍留在 Worker；Envelope、路由、Dispatcher、Retry/DLQ 与遥测移动到可复用 Scoped 处理器。不得改变生产默认配置或可靠性语义。

- [ ] **Step 4: 运行 GREEN、Worker 回归并提交**

Run: 聚焦处理器、Worker、Offset、分区调度测试。Expected: 全部 PASS。

Commit: `refactor(messaging): share Kafka consumer processing core`

---

### Task 3: Scope B 业务信封、Handler 与正确性测量

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaWorkerCapacityEnvelope.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaWorkerCapacitySubscription.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaWorkerCapacityTracker.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityModels.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaCapacityWorkerHandlerTests.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaCapacityReportTests.cs`

**Interfaces:**
- Produces: 生产 Envelope Header 构建器、`KafkaWorkerCapacitySubscription`、固定内存 Tracker、Scope B 数据库/Handler 延迟证据。
- EventType: `fullnet.capacity.worker.message.processed`，SchemaVersion `1`，ContentType `application/x-msgpack`。

- [ ] **Step 1: 写信封与 Handler RED 测试**

覆盖 EventId 确定映射、Header 完整、Payload Hash、Run/Sample 隔离、同 Key 顺序、重复投递只产生一次业务副作用、Handler 异常不记成功、直方图溢出失败关闭。

- [ ] **Step 2: 运行 RED**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~KafkaCapacityWorkerHandlerTests`

Expected: FAIL，新类型尚不存在。

- [ ] **Step 3: 实现固定内存测量内核**

容量 Payload 继续复用确定性编码，Kafka Header 映射到生产 Envelope；Handler 只做校验和固定成本业务处理，不发外部 I/O。Tracker 分离 Broker Ack、Inbox Processed、AlreadyProcessed、Handler 成功与 Commit 水位，禁止把 Kafka Consume 当作业务完成。

- [ ] **Step 4: 运行 GREEN、报告脱敏测试并提交**

Commit: `feat(benchmarks): measure Kafka inbox handler outcomes`

---

### Task 4: Scope B Driver、生产分区调度与 Offset 水位

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaWorkerCapacityDriver.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaWorkerCapacityExecutor.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/ConfluentKafkaWorkerCapacityTransport.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityDriverRegistry.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaCapacityWorkerDriverTests.cs`

**Interfaces:**
- Consumes: Task 1 Preflight、Task 2 处理内核、Task 3 Subscription/Tracker。
- Produces: 默认 Registry 中可执行的 `worker_inbox_handler` Factory/Driver。

- [ ] **Step 1: 写生命周期与背压 RED 测试**

断言数据库 Preflight→Kafka Admin→Topic→Consumer assignment→Producer→Handler/Inbox→连续 Commit→排空顺序；同 Key 串行、不同 Key 分槽并行；较晚 Offset 失败时 Seek 最早未决 Offset；Inbox 重投不重复 Handler 副作用。

- [ ] **Step 2: 运行 RED**

Run: `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~KafkaCapacityWorkerDriverTests`

Expected: FAIL，Scope B Driver 尚不存在。

- [ ] **Step 3: 实现 Driver 与 DI 生命周期**

每个样本建立独立 ServiceProvider/Consumer Group，Dapper Scoped 事务按消息创建；Consumer 的所有 native 调用只在 Poll 线程；生产 `KafkaPartitionWorkScheduler` 和 `KafkaConsumerPartitionCoordinator` 负责有界并行、背压和连续提交。Warmup 使用独立 EventId/Consumer 身份并在测量前排空。

- [ ] **Step 4: 运行 GREEN、Scope A 回归、Release build 并提交**

Commit: `feat(benchmarks): run Kafka worker inbox capacity scope`

---

### Task 5: SQL Server/MySQL＋真实 Kafka 缩小集成验证

**Files:**
- Create: `tests/Full.NET.IntegrationTests/Messaging/KafkaWorkerCapacityRunnerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Messaging/KafkaFixture.cs`
- Modify: `eng/testing/test-matrix.json`

**Interfaces:**
- Consumes: 预迁移临时 SQL Server/MySQL 数据库、真实 Kafka Testcontainer、Scope B CLI。
- Produces: 双库首次处理/重复投递/Offset/失败回滚/排空证据。

- [ ] **Step 1: 写双库真实链路 RED 测试**

分别运行 SQL Server 与 MySQL：2 分区、RF1、20/200 msg/s、128-byte Payload、1 秒预热、2 秒测量；验证 Inbox 行数、Processed 状态、Handler 次数、零重复业务副作用、Offset 不越过失败消息及 Scope A/B Topic/Group 隔离。

- [ ] **Step 2: 运行 RED**

Run: `dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter FullyQualifiedName~KafkaWorkerCapacityRunnerTests`

Expected: FAIL，Runner 尚未闭合真实双库链路。

- [ ] **Step 3: 完成测试夹具与清理边界**

测试只创建并销毁自身临时数据库/Topic；生产 Runner 不获得迁移或广泛删除权限。Docker/CDC 基础设施不可用必须如实 Inconclusive，不得伪造通过。

- [ ] **Step 4: 运行 GREEN、分片治理并提交**

Run: 聚焦 Integration、`pnpm test:integration:partitions`、`pnpm test:governance`。Expected: 可用环境全部 PASS。

Commit: `test(messaging): verify Kafka worker capacity scope`

---

### Task 6: 手动工作流、预算、文档与最终审查

**Files:**
- Modify: `.github/workflows/kafka-capacity.yml`
- Modify: `tests/performance/kafka-capacity-workflow-contract.test.mjs`
- Modify: `docs/operations/kafka-capacity-runner.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/superpowers/plans/2026-08-10-wolverine-reference-kafka-hardening.md`
- Modify: `docs/superpowers/plans/2026-08-11-kafka-capacity-runner.md`

**Interfaces:**
- Produces: 显式 Scope B 手动 Profile、按 Provider 隔离的数据库 Secret、独立 Budget 和可复核工件。

- [ ] **Step 1: 写工作流安全 RED 契约**

断言 Scope B 只能手动选择，SQL Server/MySQL Secret 按选中 Provider/Scope 的步骤隔离，数据库名必须精确配置，工作流不会迁移数据库，Scope A 不接收数据库 Secret，参数规模被精确锁定。

- [ ] **Step 2: 运行 RED 并实现工作流**

Run: `node --test tests/performance/kafka-capacity-workflow-contract.test.mjs`。先确认失败，再实现最小入口。

- [ ] **Step 3: 更新运维与状态文档**

记录专用数据库准备、权限、保留/清理、双库运行、证据解释和 `Capacity-not-verified` 边界；Scope C 仍标记未实现。

- [ ] **Step 4: 最终验证与独立复审**

Run: Scope B Unit/Integration、Release build、Architecture、Naming、SQL Safety、Governance、Performance Governance、affected inner/slice/merge、`git diff --check`。请求架构级复审；Critical/Important 必须关闭。

- [ ] **Step 5: 提交**

Commit: `ci(benchmarks): add Kafka worker capacity certification`
