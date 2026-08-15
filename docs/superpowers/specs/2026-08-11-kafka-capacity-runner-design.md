# Kafka Capacity Runner 设计规格

- 状态：已批准
- 批准日期：2026-08-11
- 适用范围：`Full.NET.Benchmarks` 的独立 Kafka 传输容量 Runner
- 架构依据：[ADR-0006](../../architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)、[事务 Outbox/CDC/Kafka Spec](2026-08-08-transactional-outbox-cdc-kafka-design.md)、[Wolverine 参考硬化计划](../plans/2026-08-10-wolverine-reference-kafka-hardening.md)
- 容量状态：`Capacity-not-verified`

## 1. 目标与边界

新增独立命令 `kafka-capacity`，连接外部专用 Kafka，测量 Full.NET 当前 Kafka Producer 配置下的 Producer → Broker → Consumer 传输能力。Runner 必须同时覆盖低速尾延迟和有界开环吞吐阶梯，输出可复现、可中断、可脱敏的证据，并以消息级完整性作为硬门禁。

第一版只实现 `KafkaTransport` 范围：

```text
Full.NET Kafka 配置
  -> Confluent.Kafka Producer
  -> 专用 Kafka Topic
  -> 独立临时 Consumer Group
  -> 统计、完整性核对与报告
```

第一版明确不包含：

- 业务事务、Outbox 表和 CDC/Debezium；
- Full.NET `KafkaConsumerWorker`、Inbox、Dispatcher、Handler 和业务数据库；
- SQL Server/MySQL 容量差异；
- 正式 Consumer Group Offset、Retry Topic、DLQ 或范围重放；
- 生产环境执行、Soak、N+1、故障域容量认证和固定 QPS 承诺。

Runner 实现完成只证明工具与缩小版真实 Kafka 链路通过开发验证。没有专用生产等价环境的完整执行记录时，CDC/Kafka 整体继续标记为 `Capacity-not-verified`。

## 2. 方案选择

采用“独立 Confluent.Kafka Runner＋复用生产配置构建器”。Runner 放入既有 `benchmarks/Full.NET.Benchmarks`，不增加项目，不启动 API、Worker 或数据库容器。

Producer 和 Consumer 分别通过 `KafkaMessagingOptions.BuildProducerConfig()` 与 `BuildConsumerConfig()` 构建，继承 Full.NET 已验证的 TLS/SASL、幂等 Producer、Acks、批量、队列、Consumer 协议和超时语义。Runner 可以增加仅用于采样的 librdkafka Statistics 回调，但不得覆盖可靠性配置。

没有采用以下方案：

- 完整 Worker：会把分槽、Inbox、Handler、数据库和 Offset Commit 混入传输指标，无法隔离 Kafka 瓶颈；
- 外部通用压测工具：不能可靠复用 Full.NET 配置、消息 Header 与完整性核对语义；
- Runner 自启 Kafka 容器作为容量环境：容器只用于开发集成回归，不构成生产等价容量证据。

## 3. 代码边界

命令入口仍由 `benchmarks/Full.NET.Benchmarks/Program.cs` 分派。Kafka 文件集中在 `benchmarks/Full.NET.Benchmarks/Kafka/`，按下列职责拆分：

| 组件 | 职责 |
| --- | --- |
| `KafkaCapacityOptions` | 解析非敏感 CLI 参数，校验数值上限和矩阵规模 |
| `KafkaCapacityConfiguration` | 从 `KafkaCapacity` 配置节绑定连接、安全、执行许可和集群身份 |
| `KafkaCapacityEnvironmentGuard` | 执行开关、环境、ClusterId、审批标识和计划预算的失败关闭 |
| `KafkaCapacityScenarioCatalog` | 生成稳定、有界、可指纹化的低速与吞吐样本 |
| `KafkaCapacityTopicManager` | 创建唯一 Topic，记录 TopicId，并保护显式删除 |
| `IKafkaCapacityScenarioDriver` | 隔离工作负载范围；本次只提供 `KafkaTransportScenarioDriver` |
| `KafkaCapacityDriverRegistry` | 在连接 Kafka 前按稳定 ScopeCode 选择唯一 Driver Factory；未知或重复范围失败关闭 |
| `KafkaCapacityOpenLoopScheduler` | 按目标到达率调度，记录调度迟滞，并向有界发送通道施加背压 |
| `KafkaCapacityIntegrityTracker` | 核对 Ack、消费、Hash、重复、丢失、分区顺序和排空状态 |
| `KafkaCapacityLatencyHistogram` | 以固定内存记录延迟分布和溢出，输出 P50/P95/P99/Max |
| `KafkaCapacityBudgetEvaluator` | 执行正确性硬门禁和可选环境性能预算 |
| `KafkaCapacityCheckpoint` | 只保存完整样本，支持安全断点续跑 |
| `KafkaCapacityReportWriter` | 写入 manifest、样本、直方图、统计快照和 Markdown 摘要 |
| `KafkaCapacityRunner` | 编排预检、Topic、Consumer、预热、采样、排空、评估和报告 |

`IKafkaCapacityScenarioDriver` 的稳定输入是不可变的 `KafkaCapacitySampleContext`，稳定输出是 `KafkaCapacitySampleEvidence`。命令通过 `--scope` 选择已注册的唯一 Driver Factory；第一版默认值与唯一内置值仍为 `kafka_transport`，未注册范围必须在加载 Secret、连接 Kafka 或创建 Topic 前失败关闭。Factory 接收已统一加载的完整 Runner 配置并返回 Driver 与可选统计源，使未来数据库链路可以扩展同一配置根而不绕开保护入口；Factory 只允许构造运行时对象，不得连接数据库、Kafka 或其他外部依赖。Runner 必须在构建 AdminClient 或执行任何 Kafka I/O 前创建 Runtime 并校验 Factory/Driver Scope 一致。公共 Runner 继续独占配置保护、Topic、预算、checkpoint 和报告编排。未来 B“Worker＋Inbox＋Handler”和 C“业务事务＋Outbox＋CDC＋Kafka＋Inbox”只能新增 Driver/Factory；不得复制配置保护、统计、预算、checkpoint、报告或 Topic 所有权逻辑。不同 Driver 的 `ScopeCode` 必须进入样本、预算键、场景指纹、checkpoint 与 manifest，禁止跨范围比较或续跑。

## 4. 配置与 Secret

连接与安全配置使用 `KafkaCapacity:Kafka`，属性语义与 `KafkaMessagingOptions` 保持一致。环境变量使用 .NET 双下划线映射，例如：

```text
KafkaCapacity__ExecutionEnabled=true
KafkaCapacity__EnvironmentName=Capacity
KafkaCapacity__ExpectedClusterId=<dedicated-cluster-id>
KafkaCapacity__Kafka__Enabled=true
KafkaCapacity__Kafka__BootstrapServers=<secret-injected-endpoints>
KafkaCapacity__Kafka__SecurityProtocol=SaslSsl
KafkaCapacity__Kafka__SaslMechanism=ScramSha512
KafkaCapacity__Kafka__SaslUsername=<secret-injected-user>
KafkaCapacity__Kafka__SaslPassword=<secret-injected-password>
```

CLI 只接受非敏感参数。允许用 `--settings <path>` 指定未提交的 UTF-8 JSON 配置文件；配置文件路径、Secret 值和原始连接端点不得写入报告。`KafkaCapacity:Kafka:Enabled` 必须为 `true`，BootstrapServers 必须非空。Runner 必须复用 `KafkaMessagingOptions` 的安全和可靠性验证，再追加容量环境专属验证；不能在 Benchmark 中维护第二套 TLS/SASL 枚举或放宽 `Acks=All`、`EnableIdempotence=true`、`EnableAutoCommit=false`。

`ClientId`、临时 Consumer Group 和 Topic 都由 Runner 基于 RunId 生成；用户配置不能让 Runner 加入正式 Consumer Group。报告只保存安全协议、SASL 机制、非敏感数值配置及 ClusterId/BootstrapServers/SaslUsername 的 SHA-256 摘要。

## 5. CLI 与有界参数

命令格式：

```text
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj \
  -c Release -- kafka-capacity [options]
```

默认执行 dry-run，只打印脱敏后的样本计划。产生流量必须同时满足配置中的 `ExecutionEnabled=true` 和 CLI `--execute true`，并提供非空 `--approval-id` 与 `--reason`。

| 参数 | 默认值 | 有效范围/规则 |
| --- | --- | --- |
| `--scenarios` | `low-rate,throughput` | 不重复子集 |
| `--scope` | `kafka_transport` | 稳定小写机器码；必须存在唯一已注册 Driver Factory |
| `--low-rates` | `10` | 每项 1..10,000 msg/s |
| `--throughput-rates` | `1000` | 每项 1..1,000,000 msg/s，严格递增 |
| `--payload-sizes` | `256` | 每项 64..1,048,576 bytes |
| `--partitions` | `6` | 1..128 |
| `--replication-factor` | `1` | 1..5，且不得超过 Broker 数 |
| `--producer-concurrency` | `1` | 每项 1..256 |
| `--warmup-seconds` | `10` | 0..600 |
| `--duration-seconds` | `30` | 1..3,600 |
| `--drain-seconds` | `60` | 1..900 |
| `--max-messages-per-sample` | `1000000` | 1..100,000,000 |
| `--repetitions` | `1` | 1..20 |
| `--resume` | `true` | `false` 时不得覆盖已有 checkpoint |
| `--max-new-samples` | `0` | 0..1,000；非零时必须启用 resume |
| `--budget` | 无 | 可选 UTF-8 JSON 性能预算文件 |
| `--delete-topic` | `false` | 只允许删除本次创建且 TopicId 未变化的 Topic |
| `--output` | UTC 唯一目录 | 位于 `BenchmarkDotNet.Artifacts/kafka-capacity/` |

样本总数最多 1,000，总计划消息数最多 100,000,000，总计划采样时长最多 24 小时；任一上限在 dry-run 阶段失败关闭。实际样本在达到配置时长或 `max-messages-per-sample` 任一边界时停止生产。

## 6. Topic、消息和 Consumer Group

Topic 名称为 `fullnet.capacity.<normalized-run-id>.v1`。RunId 默认是 UTC 时间加随机后缀，也可由调用方提供；规范化后只允许小写 ASCII 字母、数字、点和连字符，完整 Topic 名不得超过 249 字符。

Runner 通过 AdminClient 查询 ClusterId、Broker 数和 Topic。目标 Topic 已存在时默认失败，只有 checkpoint 同时证明 ClusterId 摘要、TopicId、RunId、代码提交和场景指纹一致时才允许 resume。创建后记录 Broker 返回的 TopicId；显式删除前重新查询并精确比较 ClusterId、TopicId 和 Topic 名，任一不一致都拒绝删除。默认保留 Topic以便证据复核。

每个样本使用独立 GroupId：

```text
fullnet.capacity.<run-id>.<sample-id>.transport
```

Scope A 保持上述兼容格式；未来 Scope 的 GroupId 与 ClientId 额外包含其稳定 `ScopeCode` 段。标识超过长度上限时保留 Scope/角色后缀并加入截断前完整前缀的稳定摘要，禁止不同样本因朴素截断发生碰撞。Consumer 必须在预热前完成分区分配，并保持 `EnableAutoCommit=false`、`EnableAutoOffsetStore=false`。Runner 不修改任何正式 Consumer Group Offset。样本结束后只提交或关闭自己的临时 Group；提交行为不属于吞吐成功条件。

消息值是固定版本的二进制测试信封。`--payload-sizes` 表示完整 `Message.Value` 的字节数，固定字段占用后由确定性填充数据补足；不能把 Envelope 开销隐藏在报告之外。信封至少包含：

- 格式版本、RunId 摘要、SampleId；
- 全局 Sequence、分区内 Sequence、计划发送单调时钟、实际入队单调时钟；
- Payload 长度、确定性 Payload 和 SHA-256 Hash。

消息 Key 使用稳定分区 Lane。每个 Lane 只有一个有序入队者，允许共享线程安全 Producer；因此 Consumer 可按实际 Partition 核对分区内 Sequence，Producer 并发不能制造测试自身的伪乱序。发送热路径使用 Confluent.Kafka 的非阻塞 `Produce` 和 DeliveryReport 回调，以有界调度通道控制在途数量；禁止用逐条 `await ProduceAsync` 把客户端等待误当成 Broker 饱和能力。

## 7. 场景与调度

### 7.1 Low-rate

Low-rate 使用开环定速调度。每个目标速率、Payload、Producer 并发和 repetition 形成独立样本。该场景重点测量 Producer 批量等待对 Ack 与端到端 P95/P99 的影响，禁止以高吞吐平均值替代低速证据。

### 7.2 Throughput

Throughput 按用户提供的严格递增目标速率逐级执行。每一级仍是开环目标到达率，不使用无界 `while` 或闭环“上一条完成后再发下一条”。调度器把工作写入固定容量 Channel，Producer 并发从同一有界通道读取；通道满时记录调度迟滞而不是无限分配。

出现以下任一条件时，当前样本结束并停止后续升档：

- 正确性错误或不可恢复 Produce/Consume 错误；
- 实际调度速率连续 10 秒低于目标的 95%；
- 调度 P99 迟滞超过可选预算，或无预算时超过 5 秒；
- 样本停止生产后未在 `drain-seconds` 内排空；
- Runner 进程托管堆超过 2 GiB 或延迟直方图发生范围溢出；
- 用户取消、Broker 断连超过 DeliveryTimeout 或 Topic/Cluster 身份变化。

停止升档是保护行为，不等于容量通过；报告必须保留触发原因。

## 8. 测量和完整性

每个样本至少输出：

- 目标速率、实际调度速率、Producer 入队速率、Ack 速率、Consumer 接收速率和排空吞吐；
- Producer Ack 延迟和 Producer 入队至 Consumer 接收延迟的 P50/P95/P99/Max；
- 调度迟滞 P50/P95/P99/Max；
- Scheduled、Enqueued、Acked、Consumed、Lost、Duplicate、Corrupted、OutOfOrder 和 Unflushed；
- 样本停止时与排空完成时的 Broker Offset 积压、最老未消费年龄；
- Runner 进程 CPU、总分配、Gen0/1/2、托管堆和峰值工作集；
- librdkafka 的允许字段：消息队列数量/字节、发送/接收字节、请求延迟、Broker 状态和错误计数；禁止保存 Broker 地址、原始 ClientId、Topic 原文或异常文本。

延迟按同一进程的 `Stopwatch.GetTimestamp()` 计算，避免依赖主机墙钟同步。`KafkaCapacityLatencyHistogram` 使用 1 微秒至 1 小时的对数桶，最大相对误差不超过 1%，每个指标固定内存；小于 1 微秒按 1 微秒记录，超出范围增加 Overflow 并使样本无效。

完整性跟踪使用按样本预分配的有界位图和每分区最后连续 Sequence，不保存无限 MessageId 集合。只有 `Acked == Consumed` 且 Lost、Duplicate、Corrupted、OutOfOrder、Unflushed 全为 0，排空完成，样本才满足正确性硬门禁。Produce 未 Ack 的消息不计为成功，但必须单列失败原因；Ack 后未消费始终是完整性失败。

## 9. 性能预算

正确性硬门禁不可配置关闭。性能预算文件可按 `ScopeCode + Scenario + TargetRate + PayloadSize + Partitions + ProducerConcurrency` 指定：

- 最低实际调度、Ack 和消费吞吐；
- 最大调度迟滞 P95/P99；
- 最大 Ack 延迟 P95/P99；
- 最大端到端延迟 P95/P99；
- 最大排空时长、CPU、托管堆和 librdkafka 本地队列深度。

预算必须声明环境标识、ClusterId 摘要、基线 Git 提交和生成日期。环境、ClusterId、ScopeCode 或场景指纹不一致时拒绝套用，不能跨硬件或跨链路范围比较。没有预算文件时仍执行正确性硬门禁并生成证据，但性能结论为 `Observed`，容量状态保持 `Capacity-not-verified`。

## 10. Checkpoint、报告和退出码

工件目录包含：

```text
manifest.json
checkpoint.json
samples.ndjson
latency-histograms.json
librdkafka-statistics.ndjson
summary.json
summary.md
```

Checkpoint 使用 SchemaVersion 和原子临时文件替换，只登记完整关闭的样本。不完整样本写入报告但不进入可跳过集合。Resume 指纹包含 Git 提交、Runner SchemaVersion、ScopeCode、脱敏 Kafka 配置、ClusterId 摘要、TopicId 和全部场景参数；任一变化都拒绝续跑。

退出码稳定定义为：

| 退出码 | 含义 |
| --- | --- |
| `0` | 所有已执行样本满足正确性硬门禁和已提供的性能预算 |
| `2` | CLI、配置或预算格式错误 |
| `3` | 执行许可、环境、ClusterId 或 Topic 所有权保护拒绝 |
| `4` | Kafka 依赖失败、超时或样本不完整 |
| `5` | 正确性硬门禁失败 |
| `6` | 性能预算失败 |
| `130` | 用户取消；已尽力排空并保存不完整证据 |

报告必须输出本次注册 Driver 的稳定 ScopeCode 和固定 `CapacityStatus=Capacity-not-verified`。控制台及全部工件需要经过同一 allowlist 投影，不允许先序列化完整 Kafka 配置再做字符串替换。

## 11. 错误、取消与资源释放

Ctrl+C、CancellationToken、Producer/Consumer 致命错误或 AdminClient 失败都会先停止新调度，再在剩余 drain 预算内等待已入队消息 Ack 和消费。排空完成后关闭 Consumer 和 Producer；排空失败时记录准确差额并以不完整状态退出。关闭阶段不得无限等待，也不得吞掉 Flush、Close 或报告写入异常。

非致命 Kafka 错误按稳定错误码聚合，原始异常只显示到受保护控制台的单条摘要，不进入指标标签或持久报告。报告写入失败不能把已执行样本标成成功；checkpoint 写入失败后禁止继续下一样本。

## 12. 验证

实现采用测试先行：每个行为测试必须先因缺少实现而失败，再加入最小实现。

### 12.1 Unit

- 参数边界、未知/重复参数、矩阵上限和 dry-run；
- ExecutionEnabled、Production、审批标识和 ClusterId 失败关闭；
- Full.NET Kafka 安全/可靠性验证复用；
- TopicId 所有权、存在冲突、resume 和删除保护；
- 开环调度、Channel 背压、升档停止和取消；
- 位图完整性、Hash、重复、丢失和分区顺序；
- 直方图精度、合并、并发、上溢与百分位；
- 预算匹配和失败分类；
- checkpoint 原子性、指纹和不完整样本；
- JSON/Markdown/librdkafka allowlist 脱敏。

### 12.2 Kafka Integration

使用仓库固定的 Kafka Testcontainer 镜像运行缩小矩阵：

- 创建唯一多分区 Topic；
- `low-rate` 与 `throughput` 各至少一个短样本；
- 全部消息 Ack、消费、Hash 正确、分区有序并排空；
- 临时 Group 不修改其他 Group Offset；
- Topic 默认保留，显式删除只能删除当前 TopicId；
- Broker 中断、用户取消和排空超时产生不完整证据；
- Plaintext 测试只证明开发链路，TLS/SASL 仍由配置契约和专用环境验证。

### 12.3 完成检查

- `Full.NET.UnitTests` 的 Kafka Capacity 聚焦测试；
- 受影响 Kafka Integration 聚焦测试；
- `Full.NET.Benchmarks` Release 构建；
- 任务快照的 affected inner/slice/merge 影响集；
- Architecture、Naming、Governance、Performance Governance；
- `git diff --check` 与工作区审查。

本地不执行正式吞吐阶梯、Soak 或 N+1，也不产生 QPS 结论。Runner 合入后只把 F0–F3 计划中的“独立容量工具实现”标为完成；生产等价延迟/吞吐执行项继续未完成，直到专用环境 Verification 保存日期、Git 基线、Kafka/硬件拓扑、命令、原始工件、结论和未验证项。

## 13. 运维示例

Dry-run：

```powershell
$env:KafkaCapacity__ExecutionEnabled = 'true'
$env:KafkaCapacity__EnvironmentName = 'Capacity'
$env:KafkaCapacity__ExpectedClusterId = '<dedicated-cluster-id>'
$env:KafkaCapacity__Kafka__BootstrapServers = '<secret-injected-endpoints>'
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release -- kafka-capacity --throughput-rates 1000,5000,10000
```

正式执行必须追加：

```text
--execute true --approval-id PERF-20260811-01 --reason dedicated-kafka-baseline
```

示例只表达调用方式，不是已运行证据。Secret 必须由 Secret Store、CI Secret 或未提交配置文件注入，禁止提交真实值。
