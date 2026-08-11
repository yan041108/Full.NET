# Kafka Capacity Runner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `Full.NET.Benchmarks` 中交付独立、失败关闭、可断点续跑的 `kafka-capacity` 命令，测量外部专用 Kafka 的低速传输延迟和有界开环吞吐，并对消息完整性执行不可关闭的硬门禁。

**Architecture:** Runner 复用 `KafkaMessagingOptions` 的 Producer/Consumer/安全配置构建器，以独立 Confluent.Kafka Producer、临时 Topic 和临时 Consumer Group 隔离 `KafkaTransport` 测量范围。配置保护、Topic 所有权、开环调度、完整性、固定内存直方图、预算、checkpoint 和报告相互分离；未来完整 Worker 或 CDC 链路只能新增 `IKafkaCapacityScenarioDriver`，不得复制公共控制面。

**Tech Stack:** .NET 10、C#、Confluent.Kafka 2.15、MSTest/Microsoft Testing Platform、Testcontainers.Kafka、System.Text.Json。

## Global Constraints

- 批准规格是 [`docs/superpowers/specs/2026-08-11-kafka-capacity-runner-design.md`](../specs/2026-08-11-kafka-capacity-runner-design.md)，实现不得扩大到 Worker、Inbox、数据库或 CDC。
- 基线提交为 `ccf130a8`；任务快照为 `codex-kafka-capacity-20260811`。
- 命令默认 dry-run；真实流量同时要求 `KafkaCapacity:ExecutionEnabled=true`、`--execute true`、审批标识和原因。
- 第一版拒绝 `Production`，ClusterId 必须精确匹配，Topic 默认保留，删除必须复核 ClusterId、TopicId 和所有权。
- `Acks=All`、`EnableIdempotence=true`、`EnableAutoCommit=false` 和 Full.NET TLS/SASL 验证不可放宽。
- 所有发送、缓冲、调度、消息数、矩阵、时长、排空和报告采样均必须有界。
- 正确性硬门禁不可关闭；性能预算可选，缺少专用环境证据时固定输出 `Capacity-not-verified`。
- 所有手写代码注释使用中文；公开类型和成员按规则提供中文 XML 文档。
- 不新增第三方包；延迟分布由固定内存、约 1% 相对误差的内部直方图实现。
- 本地只运行缩小版 Kafka Integration，不运行正式容量阶梯、Soak 或 N+1，不声明固定 QPS。

---

## 执行状态（2026-08-12）

- Task 1–5 与 Task 7 已按计划实现并完成 Unit、Release build、Architecture、Naming、SQL Safety、Governance、Performance Governance 和 Integration 分片静态门禁。
- 两轮独立代码复审发现的 QueueFull、逐样本预算、统一排空截止时间、P99 语义、真实 Broker 水位、逐样本 statistics、ClientId、指纹与输出并发问题均已修复并建立回归测试；Unit 当前为 1406 项。
- Task 6 的命令入口、真实 Kafka 测试代码与测试矩阵已完成；当前机器的 Docker Engine 不可用，真实 Kafka 缩小集成测试未取得可用运行证据。
- Task 8 的 affected inner/slice/merge 选择计划已验证；对应容器测试均在基础设施初始化阶段被 Docker 不可用阻断，未表述为通过。
- 专用生产等价 Kafka 的低速延迟、饱和吞吐、Soak、N+1 与恢复演练尚未执行，因此状态继续保持 `Capacity-not-verified`。

---

## File Structure

### 新增 Benchmark 文件

- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityOptions.cs`：CLI、配置文件路径、矩阵上限。
- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityConfiguration.cs`：JSON/环境变量绑定、Secret 边界、共享 Kafka 校验。
- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityScenario.cs`：场景、样本键、稳定指纹和目录。
- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityEnvironmentGuard.cs`：dry-run/执行许可/ClusterId/Production/计划预算。
- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityEnvelope.cs`：固定二进制信封编解码和 Payload Hash。
- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityLatencyHistogram.cs`：固定内存延迟桶与百分位。
- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityIntegrityTracker.cs`：有界 Ack/Consume 位图、重复、丢失、损坏、顺序。
- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityModels.cs`：上下文、证据、状态、资源快照与退出码。
- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityTopicManager.cs`：AdminClient 集群/Topic 身份和删除保护。
- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityOpenLoopScheduler.cs`：定速调度、有界 Channel、迟滞与升档停止。
- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityTransportDriver.cs`：独立 Producer/Consumer 的实际测量。
- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityBudget.cs`：预算读取、精确匹配与评估。
- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityCheckpoint.cs`：完整样本 checkpoint 和 resume 指纹。
- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityReportWriter.cs`：allowlist JSON/NDJSON/Markdown 工件。
- `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityRunner.cs`：样本编排、取消、排空与退出码。

### 修改文件

- `benchmarks/Full.NET.Benchmarks/Program.cs`：注册 `kafka-capacity` 命令与稳定退出码。
- `benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj`：直接引用 `Full.NET.Messaging.Kafka`。
- `src/BuildingBlocks/Full.NET.Messaging.Kafka/AssemblyInfo.cs`：只向 Benchmark 工具开放内部配置校验和 ClientConfig 构建器。
- `tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj`：引用 Benchmark 项目。
- `eng/testing/test-matrix.json`：按实际新增测试数提高 Unit 最低发现数。
- `docs/superpowers/plans/2026-08-10-wolverine-reference-kafka-hardening.md`：区分 Runner 实现和专用环境执行。
- `docs/roadmap/capability-status.md`：登记工具可用但容量仍未验证。

### 新增测试与运维文档

- `tests/Full.NET.UnitTests/Messaging/KafkaCapacityOptionsTests.cs`
- `tests/Full.NET.UnitTests/Messaging/KafkaCapacityMeasurementTests.cs`
- `tests/Full.NET.UnitTests/Messaging/KafkaCapacityControlPlaneTests.cs`
- `tests/Full.NET.UnitTests/Messaging/KafkaCapacitySchedulerTests.cs`
- `tests/Full.NET.UnitTests/Messaging/KafkaCapacityReportTests.cs`
- `tests/Full.NET.IntegrationTests/Messaging/KafkaCapacityRunnerTests.cs`
- `docs/operations/kafka-capacity-runner.md`

---

### Task 1: CLI、配置、场景目录和环境保护

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityOptions.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityConfiguration.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityScenario.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityEnvironmentGuard.cs`
- Modify: `src/BuildingBlocks/Full.NET.Messaging.Kafka/AssemblyInfo.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaCapacityOptionsTests.cs`

**Interfaces:**
- Produces: `KafkaCapacityOptions.Parse(IReadOnlyList<string>)`。
- Produces: `KafkaCapacityConfiguration.Load(KafkaCapacityOptions)`。
- Produces: `KafkaCapacityScenarioCatalog.Build(KafkaCapacityOptions)`。
- Produces: `KafkaCapacityEnvironmentGuard.ValidatePlan(...)` 和 `ValidateCluster(...)`。
- Produces: `KafkaCapacityGuardResult(bool IsAllowed, string ReasonCode, string Message)`。
- Consumes: `KafkaMessagingOptionsValidation.Validate(...)`、`KafkaMessagingOptions.BuildClientConfig()`。

- [ ] **Step 1: 写 CLI 和矩阵 RED 测试**

```csharp
[TestMethod]
public void Defaults_build_two_bounded_transport_samples()
{
    var options = KafkaCapacityOptions.Parse([]);
    var samples = KafkaCapacityScenarioCatalog.Build(options);

    Assert.IsFalse(options.Execute);
    Assert.HasCount(2, samples);
    Assert.IsTrue(samples.Any(x => x.Scenario == KafkaCapacityScenario.LowRate && x.TargetMessagesPerSecond == 10));
    Assert.IsTrue(samples.Any(x => x.Scenario == KafkaCapacityScenario.Throughput && x.TargetMessagesPerSecond == 1000));
    Assert.IsTrue(samples.All(x => x.ScopeCode == "kafka_transport"));
}

[TestMethod]
public void Parser_rejects_unbounded_or_ambiguous_plans()
{
    Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
        KafkaCapacityOptions.Parse(["--throughput-rates", "1000001"]));
    Assert.ThrowsExactly<ArgumentException>(() =>
        KafkaCapacityOptions.Parse(["--scenarios", "throughput,throughput"]));
    Assert.ThrowsExactly<ArgumentException>(() =>
        KafkaCapacityOptions.Parse(["--max-new-samples", "1", "--resume", "false"]));
}
```

- [ ] **Step 2: 运行 RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~KafkaCapacityOptionsTests
```

Expected: FAIL，类型尚不存在。

- [ ] **Step 3: 实现有界 Options 和稳定目录**

实现以下核心契约，并把规格中的默认值、单值上限、1,000 样本、100,000,000 消息和 24 小时总预算全部放入解析/目录校验：

```csharp
public enum KafkaCapacityScenario
{
    LowRate = 0,
    Throughput = 1,
}

public sealed record KafkaCapacitySample(
    string ScopeCode,
    string SampleId,
    KafkaCapacityScenario Scenario,
    int TargetMessagesPerSecond,
    int PayloadSizeBytes,
    int ProducerConcurrency,
    int Repetition);
```

未知参数、重复参数、重复列表值、非严格递增吞吐阶梯、总预算溢出和输出目录为空均失败关闭。样本 ID 由全部语义字段生成，不包含 Secret 或本地绝对路径。

- [ ] **Step 4: 写配置和环境保护 RED 测试**

覆盖：`KafkaCapacity:Kafka:Enabled=false`、空 BootstrapServers、Production、缺审批、`ExecutionEnabled=false`、ClusterId 不匹配、复制因子超过 Broker 数、dry-run 不要求审批、SASL 密码不会进入异常或 `ToString()`。

```csharp
var failure = KafkaCapacityEnvironmentGuard.ValidateCluster(
    configuration,
    options,
    new KafkaCapacityClusterIdentity("actual", BrokerCount: 3));
Assert.IsFalse(failure.IsAllowed);
Assert.AreEqual("cluster_id_mismatch", failure.ReasonCode);
StringAssert.DoesNotContain(failure.Message, configuration.Kafka.SaslPassword!);
```

- [ ] **Step 5: 实现配置加载和共享验证边界**

配置文件使用 `System.Text.Json` 读取根对象 `KafkaCapacity`；环境变量只覆盖登记属性，前缀固定为 `KafkaCapacity__`。Kafka 子属性通过 `KafkaMessagingOptions` 的公开属性元数据解析，数组只允许逗号分隔字符串。向 `Full.NET.Messaging.Kafka` 增加：

```csharp
[assembly: InternalsVisibleTo("Full.NET.Benchmarks")]
```

Benchmark 通过该受控内部边界调用现有验证器和 `BuildClientConfig()`，不复制 TLS/SASL 分支。Runner 专属校验追加 Enabled、BootstrapServers、执行许可和 Production 拒绝。

- [ ] **Step 6: 运行 GREEN 并提交**

Run: Task 1 聚焦测试。Expected: PASS。

Commit:

```powershell
git add benchmarks/Full.NET.Benchmarks/Kafka tests/Full.NET.UnitTests/Messaging/KafkaCapacityOptionsTests.cs src/BuildingBlocks/Full.NET.Messaging.Kafka/AssemblyInfo.cs
git commit -m "feat(benchmarks): add Kafka capacity configuration guard"
```

---

### Task 2: 固定信封、延迟直方图和消息完整性

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityEnvelope.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityLatencyHistogram.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityIntegrityTracker.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityModels.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaCapacityMeasurementTests.cs`

**Interfaces:**
- Produces: `KafkaCapacityEnvelopeCodec.Encode(...)`、`TryDecode(...)`。
- Produces: `KafkaCapacityLatencyHistogram.RecordMicroseconds(long)`、`Snapshot()`、`Merge(...)`。
- Produces: `KafkaCapacityIntegrityTracker.OnEnqueued/OnAcknowledged/OnConsumed/Complete`。

- [ ] **Step 1: 写信封与 Hash RED 测试**

```csharp
var encoded = KafkaCapacityEnvelopeCodec.Encode(
    payloadSizeBytes: 256,
    runHash: 17,
    sampleHash: 23,
    globalSequence: 41,
    partitionSequence: 7,
    scheduledTimestamp: 100,
    enqueuedTimestamp: 120);

Assert.AreEqual(256, encoded.Length);
Assert.IsTrue(KafkaCapacityEnvelopeCodec.TryDecode(encoded, out var envelope));
Assert.AreEqual(41L, envelope.GlobalSequence);
Assert.IsFalse(KafkaCapacityEnvelopeCodec.TryDecode(encoded.AsSpan(0, 255), out _));
```

信封使用固定 magic/version、Little Endian 数值和 SHA-256 校验；确定性填充由 Run/Sample/Sequence 派生，不使用随机分配。

- [ ] **Step 2: 写直方图 RED 测试**

覆盖 1 微秒下限、1 小时上限、P50/P95/P99、并发记录、Merge、1% 相对误差和 Overflow 使证据无效。

```csharp
var histogram = new KafkaCapacityLatencyHistogram();
foreach (var value in Enumerable.Range(1, 10_000)) histogram.RecordMicroseconds(value);
var snapshot = histogram.Snapshot();
Assert.IsTrue(Math.Abs(snapshot.P99Microseconds - 9900) / 9900d <= 0.01d);
```

- [ ] **Step 3: 写完整性 RED 测试**

分别制造 Ack 后丢失、重复消费、Hash 损坏、同分区 Sequence 逆序、未 Flush 和完美排空；断言只有最后一种 `CorrectnessPassed=true`。位图容量必须等于样本最大消息数，越界 Sequence 立即失败关闭。

- [ ] **Step 4: 运行 RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~KafkaCapacityMeasurementTests
```

Expected: FAIL，测量类型尚不存在。

- [ ] **Step 5: 实现最小固定内存测量**

直方图按 `floor(log(valueMicroseconds) / log(1.01))` 映射固定桶，使用线程局部桶减少热路径竞争，结束时合并；范围固定为 1 微秒至 1 小时。完整性使用预分配位图和分区状态数组，不保存 MessageId 集合。完成模型至少包含：

```csharp
public sealed record KafkaCapacityIntegrityEvidence(
    long Enqueued,
    long Acknowledged,
    long Consumed,
    long Lost,
    long Duplicate,
    long Corrupted,
    long OutOfOrder,
    long Unflushed,
    bool DrainCompleted)
{
    public bool CorrectnessPassed =>
        Acknowledged == Consumed && Lost == 0 && Duplicate == 0
        && Corrupted == 0 && OutOfOrder == 0 && Unflushed == 0
        && DrainCompleted;
}
```

- [ ] **Step 6: 运行 GREEN 并提交**

Run: Task 2 聚焦测试。Expected: PASS。

Commit:

```powershell
git add benchmarks/Full.NET.Benchmarks/Kafka tests/Full.NET.UnitTests/Messaging/KafkaCapacityMeasurementTests.cs
git commit -m "feat(benchmarks): add Kafka capacity measurements"
```

---

### Task 3: Topic 所有权、预算、checkpoint 和脱敏报告

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityTopicManager.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityBudget.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityCheckpoint.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityReportWriter.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityModels.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaCapacityControlPlaneTests.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaCapacityReportTests.cs`

**Interfaces:**
- Produces: `IKafkaCapacityAdminClient` 和 Confluent 适配器。
- Produces: `KafkaCapacityTopicIdentity(ClusterIdHash, TopicName, TopicId, Partitions, ReplicationFactor)`。
- Produces: `KafkaCapacityBudget.LoadAsync/Assess`。
- Produces: `KafkaCapacityCheckpoint.LoadAsync/SaveCompletedAsync`。
- Produces: `KafkaCapacityReportWriter.WriteAsync`。

- [ ] **Step 1: 写 Topic 身份 RED 测试**

使用记录型 AdminClient 验证：新 Topic 创建成功；未知既有 Topic 拒绝；checkpoint TopicId 相同允许 resume；TopicId 或 ClusterId 改变拒绝；默认不删除；显式删除前重新 Describe，只有全部身份一致才调用 DeleteTopicsAsync。

- [ ] **Step 2: 写预算和 checkpoint RED 测试**

预算必须精确匹配 Scope、场景参数、环境、ClusterIdHash 和基线提交。Checkpoint 只登记 `Completed` 样本，SchemaVersion/BuildFingerprint/TopicId/场景指纹不一致均拒绝续跑；临时文件替换后不得残留 `.tmp`。

- [ ] **Step 3: 写报告脱敏 RED 测试**

构造含 BootstrapServers、用户名、密码和原始 Topic 的配置与 librdkafka JSON，写入临时目录后递归读取全部工件，断言敏感字符串完全不存在；同时断言 allowlist 资源指标、`Scope=KafkaTransport`、`CapacityStatus=Capacity-not-verified`、失败原因码和不完整状态存在。

- [ ] **Step 4: 运行 RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~KafkaCapacityControlPlaneTests|FullyQualifiedName~KafkaCapacityReportTests"
```

Expected: FAIL，控制面类型尚不存在。

- [ ] **Step 5: 实现控制面和原子工件**

AdminClient 适配器使用 `DescribeClusterAsync`、`DescribeTopicsAsync`、`CreateTopicsAsync` 和 `DeleteTopicsAsync`。报告只从显式证据 DTO 序列化，禁止序列化 `KafkaMessagingOptions`。NDJSON 每行独立写入 allowlist DTO；checkpoint/summary 使用 UTF-8 无 BOM 临时文件加同卷原子替换。

稳定退出码定义：

```csharp
public enum KafkaCapacityExitCode
{
    Success = 0,
    InvalidConfiguration = 2,
    EnvironmentRejected = 3,
    DependencyOrIncomplete = 4,
    CorrectnessFailed = 5,
    PerformanceBudgetFailed = 6,
    Cancelled = 130,
}
```

- [ ] **Step 6: 运行 GREEN 并提交**

Run: Task 3 聚焦测试。Expected: PASS。

Commit:

```powershell
git add benchmarks/Full.NET.Benchmarks/Kafka tests/Full.NET.UnitTests/Messaging/KafkaCapacityControlPlaneTests.cs tests/Full.NET.UnitTests/Messaging/KafkaCapacityReportTests.cs
git commit -m "feat(benchmarks): secure Kafka capacity evidence"
```

---

### Task 4: 开环调度器和 Driver 扩展边界

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityOpenLoopScheduler.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityTransportDriver.cs`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaCapacitySchedulerTests.cs`

**Interfaces:**
- Produces: `IKafkaCapacityScenarioDriver.ExecuteAsync(KafkaCapacitySampleContext, CancellationToken)`。
- Produces: `KafkaCapacityOpenLoopScheduler.RunAsync(...)`。
- Consumes: Task 2 测量原语、Task 3 证据模型。

- [ ] **Step 1: 写开环 RED 测试**

使用 `TimeProvider`/可控时间替身验证 10 msg/s 的计划时间不依赖上一条完成；Channel 满时不扩容并记录调度迟滞；连续 10 秒实际调度低于目标 95% 时返回稳定停止码；取消后不再调度新消息。

```csharp
var result = await scheduler.RunAsync(
    targetMessagesPerSecond: 10,
    duration: TimeSpan.FromSeconds(2),
    maximumMessages: 100,
    writeAsync: item => sink.WriteAsync(item),
    cancellationToken);
Assert.AreEqual(20L, result.Scheduled);
```

- [ ] **Step 2: 写 Driver 边界 RED 测试**

断言 `KafkaTransportScenarioDriver.ScopeCode == "kafka_transport"`；未来不同 Scope 的证据不能进入同一 checkpoint；Driver 只消费预先创建的 Topic 和临时 Group，不能接收正式 GroupId。

- [ ] **Step 3: 运行 RED**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter FullyQualifiedName~KafkaCapacitySchedulerTests
```

Expected: FAIL，调度器/Driver 尚不存在。

- [ ] **Step 4: 实现调度和接口骨架**

使用 `Channel.CreateBounded<KafkaCapacityScheduledMessage>`，容量由 `min(maxMessages, max(1024, producerConcurrency * 4096))` 计算且不超过 1,000,000。调度时基于 `Stopwatch.GetTimestamp()` 计算绝对 deadline，迟到不通过突发补发无限追赶；每秒滚动窗口评估实际/目标比率。Driver 接口保持工作负载无关：

```csharp
public interface IKafkaCapacityScenarioDriver
{
    string ScopeCode { get; }

    Task<KafkaCapacitySampleEvidence> ExecuteAsync(
        KafkaCapacitySampleContext context,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 5: 运行 GREEN 并提交**

Run: Task 4 聚焦测试。Expected: PASS。

Commit:

```powershell
git add benchmarks/Full.NET.Benchmarks/Kafka tests/Full.NET.UnitTests/Messaging/KafkaCapacitySchedulerTests.cs
git commit -m "feat(benchmarks): add bounded Kafka open-loop scheduler"
```

---

### Task 5: 真实 Producer/Consumer 传输 Driver 与 Runner 编排

**Files:**
- Modify: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityTransportDriver.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Kafka/KafkaCapacityRunner.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj`
- Test: `tests/Full.NET.UnitTests/Messaging/KafkaCapacitySchedulerTests.cs`

**Interfaces:**
- Consumes: `KafkaMessagingOptions.BuildProducerConfig/BuildConsumerConfig`。
- Produces: 完整 `KafkaCapacitySampleEvidence` 与 Runner 退出码。

- [ ] **Step 1: 写 Producer/Consumer 生命周期 RED 测试**

通过注入 `IKafkaCapacityProducerFactory`、`IKafkaCapacityConsumerFactory` 验证：Consumer 先完成分区分配；预热不进入正式证据；生产停止后才排空；DeliveryReport 非 Persisted 计失败；Consumer 解码失败计 Corrupted；取消按“停止新发送→限时排空→关闭”排序；checkpoint 失败阻止下一样本。

- [ ] **Step 2: 运行 RED**

Run: Task 4/5 聚焦测试。Expected: FAIL，新生命周期断言未满足。

- [ ] **Step 3: 实现共享 Producer 和独立 Consumer**

Benchmark 项目增加直接项目引用：

```xml
<ProjectReference Include="..\..\src\BuildingBlocks\Full.NET.Messaging.Kafka\Full.NET.Messaging.Kafka.csproj" />
```

Driver 使用一个线程安全 `IProducer<string, byte[]>`，多个有界发送 Worker 调用非阻塞 `Produce`；DeliveryReport 回调只做无阻塞计数、位图和直方图记录。每个分区 Lane 由唯一有序写入者分配 PartitionSequence。Consumer 在专用线程 Poll，禁止在回调或热路径同步写文件。librdkafka Statistics JSON 在独立有界采样通道中投影 allowlist。

- [ ] **Step 4: 实现预热、资源采样、排空和升档停止**

预热使用同一 Topic、不同 SampleId 并在正式样本前排空。正式样本记录进程 CPU、分配、GC、堆和工作集前后差；生产停止后等待 Ack/Consume 差额归零或 drain 超时。正确性失败、依赖失败和调度停止都阻止后续吞吐升档。

- [ ] **Step 5: 运行 GREEN 和 Release build**

Run:

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~KafkaCapacity"
dotnet build benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release
```

Expected: Kafka Capacity Unit 全部 PASS；Benchmark 0 error。

- [ ] **Step 6: 提交**

```powershell
git add benchmarks/Full.NET.Benchmarks tests/Full.NET.UnitTests/Messaging/KafkaCapacitySchedulerTests.cs
git commit -m "feat(benchmarks): run Kafka transport capacity samples"
```

---

### Task 6: 命令入口和真实 Kafka 缩小集成验证

**Files:**
- Modify: `benchmarks/Full.NET.Benchmarks/Program.cs`
- Modify: `tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj`
- Create: `tests/Full.NET.IntegrationTests/Messaging/KafkaCapacityRunnerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Messaging/KafkaFixture.cs`

**Interfaces:**
- Produces: `kafka-capacity --help`、dry-run 和稳定 `Environment.ExitCode`。
- Consumes: 现有 `KafkaFixture` 的 `apache/kafka:4.1.2` 容器。

- [ ] **Step 1: 写真实 Kafka RED 测试**

Integration 使用缩小参数：2 partitions、RF 1、payload 128、low-rate 20、throughput 200、warmup 1 秒、duration 2 秒、drain 15 秒、max 1,000 消息。覆盖：

1. low-rate/throughput 均 Ack=Consumed、零丢失/重复/损坏/乱序；
2. Runner Group 不改变预建旁路 Group Offset；
3. 默认保留 Topic；
4. 显式删除当前 TopicId 成功，替换 TopicId 后删除拒绝；
5. Broker 暂停或取消生成不完整证据，不返回成功。

- [ ] **Step 2: 运行 RED**

Run:

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --filter FullyQualifiedName~KafkaCapacityRunnerTests
```

Expected: FAIL，Integration 尚不能引用/执行 Runner。

- [ ] **Step 3: 接入命令和测试项目引用**

Program 分支：

```csharp
else if (args.FirstOrDefault() is "kafka-capacity")
{
    Environment.ExitCode = (int)await KafkaCapacityRunner.RunCommandAsync(args.Skip(1).ToArray());
}
```

Integration 项目增加 Benchmark ProjectReference。`KafkaFixture` 只补充可配置分区/复制因子的 Topic helper，不改变既有测试默认行为。

- [ ] **Step 4: 运行 GREEN**

Run: Task 6 聚焦 Integration。Expected: 所有新增场景 PASS，取消/故障测试返回预期非零状态。

- [ ] **Step 5: 更新测试矩阵并提交**

根据最终发现数提高 `eng/testing/test-matrix.json` 的 Unit `minimum`，再运行：

```powershell
pnpm test:integration:partitions
pnpm test:governance
```

Expected: PASS。

Commit:

```powershell
git add benchmarks/Full.NET.Benchmarks/Program.cs tests/Full.NET.IntegrationTests eng/testing/test-matrix.json
git commit -m "test(messaging): verify Kafka capacity runner"
```

---

### Task 7: 运维说明与 F0–F3 状态同步

**Files:**
- Create: `docs/operations/kafka-capacity-runner.md`
- Modify: `docs/superpowers/plans/2026-08-10-wolverine-reference-kafka-hardening.md`
- Modify: `docs/roadmap/capability-status.md`

- [ ] **Step 1: 写运维文档**

文档必须包含：Scope=A、Secret 注入、ClusterId 保护、dry-run、缩小 smoke、正式手动执行、预算 JSON、断点续跑、Topic 保留/删除、工件目录、退出码、故障处理和 `Capacity-not-verified` 限制。示例使用占位值，不出现真实地址或 Secret。

- [ ] **Step 2: 更新旧计划而不伪造容量证据**

把原 Task 3 Step 4 拆为：

- Runner 实现与缩小 Kafka Integration：完成后勾选；
- 专用生产等价环境低速/吞吐执行：继续未勾选。

能力矩阵只登记“独立 Runner 可用”，仍保留真实 CDC 全链路、Soak、N+1、恢复演练未完成及 `Capacity-not-verified`。

- [ ] **Step 3: 文档检查并提交**

Run:

```powershell
pnpm test:governance
git diff --check
```

Expected: PASS，无失真状态提升。

Commit:

```powershell
git add docs/operations/kafka-capacity-runner.md docs/superpowers/plans/2026-08-10-wolverine-reference-kafka-hardening.md docs/roadmap/capability-status.md
git commit -m "docs(messaging): document Kafka capacity runner"
```

---

### Task 8: 最终受影响验证和完成审查

**Files:**
- Modify only if evidence reveals a defect in Task 1–7 files.

- [ ] **Step 1: 运行 affected inner 计划和测试**

```powershell
pnpm test:integration:affected:plan -- --snapshot codex-kafka-capacity-20260811 --phase inner
pnpm test:integration:affected -- --snapshot codex-kafka-capacity-20260811 --phase inner
```

Expected: selector 只命中 Kafka/Benchmark/治理相关影响集；全部 PASS。

- [ ] **Step 2: 运行纵向 slice 和 merge 门禁**

```powershell
pnpm test:integration:affected:plan -- --snapshot codex-kafka-capacity-20260811 --phase slice
pnpm test:integration:affected -- --snapshot codex-kafka-capacity-20260811 --phase slice
pnpm test:integration:affected:plan -- --snapshot codex-kafka-capacity-20260811 --phase merge
pnpm test:integration:affected -- --snapshot codex-kafka-capacity-20260811 --phase merge
```

Expected: 受影响双库/共享基础设施/Smoke 按选择器执行并全部 PASS；不得调用 `test:integration:full`。

- [ ] **Step 3: 运行构建和治理门禁**

```powershell
dotnet build Full.NET.slnx -c Release --no-restore
pnpm test:dotnet:architecture
pnpm test:naming
pnpm test:sql-safety
pnpm test:governance
pnpm test:performance-governance
pnpm test:integration:partitions
git diff --check
```

Expected: 全部 PASS；Release 0 error；任何 warning 如实报告。

- [ ] **Step 4: 运行最终 Kafka 聚焦验证**

```powershell
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --no-build --filter FullyQualifiedName~KafkaCapacity
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-build --filter FullyQualifiedName~KafkaCapacityRunnerTests
```

Expected: Unit 和真实 Kafka 缩小 Integration 全部 PASS，报告发现数、失败数和跳过数。

- [ ] **Step 5: 审查工作区和治理演进触发**

```powershell
git status --short
git log --oneline ccf130a8..HEAD
```

确认只包含本计划文件；规则演进未命中时只在交付说明写一行，不修改规则。Skill 演进仅在实现暴露可复现缺口时处理。

- [ ] **Step 6: 请求架构级代码复审并修复阻断项**

使用 `superpowers:requesting-code-review` 对安全保护、Topic 删除、调度正确性、完整性、报告脱敏和取消排空做独立复审。Critical/Important 必须修复并重新执行相关 RED/GREEN 与最终门禁。

- [ ] **Step 7: 最终聚焦提交**

若最终审查产生修复：

```powershell
git add -- benchmarks/Full.NET.Benchmarks src/BuildingBlocks/Full.NET.Messaging.Kafka/AssemblyInfo.cs tests/Full.NET.UnitTests/Messaging/KafkaCapacity*.cs tests/Full.NET.IntegrationTests/Messaging/KafkaCapacityRunnerTests.cs tests/Full.NET.IntegrationTests/Messaging/KafkaFixture.cs tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj eng/testing/test-matrix.json docs/operations/kafka-capacity-runner.md docs/superpowers/plans/2026-08-10-wolverine-reference-kafka-hardening.md docs/roadmap/capability-status.md
git commit -m "fix(benchmarks): close Kafka capacity review findings"
```

最终 `git status --short` 必须为空。交付明确说明 Runner 已实现但未执行专用环境容量认证，保持 `Capacity-not-verified`。
