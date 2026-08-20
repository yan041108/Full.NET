# Kafka Transport Capacity Runner

## 1. 适用边界

`kafka-capacity` 提供三个已实现的独立容量范围：Scope A `kafka_transport` 测量 Producer、临时 Topic 与 Consumer 传输；Scope B `worker_inbox_handler` 测量真实 `Kafka → 生产分区调度/连续 Offset 水位 → Dapper Inbox 事务 → Dispatcher → Handler`；Scope C `transaction_outbox_cdc` 测量 `开环调度 → 业务事务 Outbox → Debezium CDC → Kafka → 生产 Inbox/Handler` 全链路。Scope B 不包含业务写事务、Outbox 或 CDC；Scope C 通过外部 Connect REST 注册容量专用 Connector，Debezium Topic 前缀为 `fullnet.capacity.cdc.*`，Runner 默认不删除这些 Topic。三者均不能解除 ADR-0006 的影子运行与切流门禁。

当前能力状态固定为 `Capacity-not-verified`。Scope B 与 Scope C 均已通过 MySQL + 真实 Kafka/Connect 的缩减集成测试；Scope C 的 SQL Server 路径在 Testcontainers CDC Agent 不可用时按设计 `Inconclusive`（见 [`sqlserver-cdc-ci-debt.md`](../verification/sqlserver-cdc-ci-debt.md)）。**Scope B/C 集成测试只证明链路可运行，不等于生产等价 1/2/4/8 实例矩阵或 Soak 已通过**；正式认证 checklist 见 [`eng/load/README.md`](../../eng/load/README.md)。尚未在专用生产等价环境执行正式矩阵或 Soak。

### 1.1 DI 边界对照（Worker vs Capacity）

| 组件 | Worker 宿主 | Capacity **Fast**（默认） | Capacity **WorkerParity** |
| --- | --- | --- | --- |
| Inbox / Dispatcher / Kafka 处理器 | 生产注册 | 同左（Scope B/C） | 同左 |
| `IEventStreamOwnershipGate` | `DapperEventStreamOwnershipGate` | Scope C：`KafkaCapacityPermissiveOwnershipGate` | 保留 Dapper 默认（无 Permissive override） |
| `IEffectiveEventDeliveryOwnerResolver` | 数据库所有权表 | Scope C：固定 `CdcKafka` 合成解析 | 保留 Dapper 默认 |
| `IIntegrationEventSubscription` | 模块显式注册 | 合成 `fullnet.capacity.worker.message` | 同 Fast（仍为容量专用事件） |
| 切流/认证语义 | 生产配置 | **非切流证据** | 更接近 Worker 门控，仍 **非** Production-verified |

CLI / 配置：`--host-parity-mode fast|worker` 或 `KafkaCapacity__HostParityMode=WorkerParity`。Fast 用于开发迭代；正式对比 Worker 行为或 merge 候选复核时使用 WorkerParity。

CLI 通过 `--scope <code>` 选择 Driver。默认注册表提供 `kafka_transport`、`worker_inbox_handler` 与 `transaction_outbox_cdc`；未知 Scope 会在设置文件、Secret 和 Kafka 连接加载前以退出码 `2` 失败关闭。ScopeCode 会进入预算键、续跑指纹、checkpoint、报告和临时客户端标识；不同 Scope 的证据不得混用或续跑。

## 2. 安全配置

Runner 默认只做 dry-run。真实流量必须同时满足：

- `KafkaCapacity:ExecutionEnabled=true`；
- CLI 显式传入 `--execute true`、`--approval-id` 和 `--reason`；
- `EnvironmentName` 不是 `Production`；
- `ExpectedClusterId` 与连接后的真实 ClusterId 精确一致；
- Kafka 配置通过生产 `KafkaMessagingOptions` 验证，保持 `acks=all`、幂等 Producer、关闭自动提交和自动 Offset Store；
- 副本因子不超过实际 Broker 数。

建议只在受保护的临时设置文件中保存非敏感参数：

```json
{
  "kafkaCapacity": {
    "executionEnabled": false,
    "environmentName": "CapacityCertification",
    "expectedClusterId": "<approved-cluster-id>",
    "kafka": {
      "enabled": true,
      "bootstrapServers": "<overridden-by-secret-environment>",
      "clientId": "fullnet-kafka-capacity",
      "consumerInstanceId": "fullnet-kafka-capacity-01",
      "securityProtocol": "SaslSsl",
      "saslMechanism": "ScramSha512",
      "producerLingerMilliseconds": 5,
      "producerBatchSizeBytes": 65536,
      "producerQueueMaxMessages": 20000,
      "producerQueueMaxKbytes": 65536,
      "producerMaxInFlightRequests": 5
    }
  }
}
```

Broker、用户名和密码从 Secret 环境注入，不写进仓库、命令行或报告：

```powershell
$env:KafkaCapacity__Kafka__BootstrapServers = '<broker-list>'
$env:KafkaCapacity__Kafka__SaslUsername = '<sasl-user>'
$env:KafkaCapacity__Kafka__SaslPassword = '<sasl-password>'
$env:KafkaCapacity__ExecutionEnabled = 'true'
```

Runner 复用 `KafkaMessagingOptions.BuildClientConfig/BuildProducerConfig/BuildConsumerConfig` 的 TLS、SASL、幂等和队列设置。持久报告只保存连接地址、用户名、RunId、Topic 和审批号的 SHA-256 摘要；`checkpoint.json` 保存续跑必需的 RunId、Topic 身份和完整样本，不包含 Broker 凭据。工件目录仍应按敏感运维证据限制访问。

Scope B 与 Scope C 还必须提供 `KafkaCapacity:Database`，或使用同名环境变量注入：`Provider`、`ConnectionString`、`ExpectedDatabaseName`、`CommandTimeoutSeconds` 与 MySQL 的 `MySqlGuidStorageMode=Binary16`。数据库必须预先完成正式迁移，并为 `fullnet.capacity.worker.message` schema 1 准备 `CurrentOwner=CdcKafka` 的所有权记录；Scope C 额外要求 `fn_messaging_outbox_event` 已存在且 Binlog/CDC 前提满足。Runner 不迁移、不建库、不自动修改所有权；它在任何 Kafka Admin/建 Topic 操作前精确验证数据库名、Inbox/Outbox/所有权表和当前 Owner。连接字符串不会进入配置摘要、checkpoint 或报告。

Scope C 还必须提供 `KafkaCapacity:Connect:BaseUri`（Connect REST 根地址），可选 `ConnectorNamePrefix`（默认 `fullnet-capacity`）、`RequestTimeoutSeconds`、`HealthTimeoutSeconds`、`DatabaseHostGateway`、`InternalKafkaBootstrapServers` 与 MySQL Connector 凭据覆盖。Runner **不**自启 Connect 容器；预检仅做 REST 健康探测，Connector 注册/删除由 Scope C Driver 生命周期管理。容量 Connector 模板位于 `deploy/messaging/connectors/*-outbox-capacity.json`，Topic 路由为 `fullnet.capacity.cdc.{MessageType}`；manifest/checkpoint 只保存 Connect 端点与 Connector 名的 SHA-256 摘要。Scope C 默认 `--delete-topic false`（Debezium Topic 供复核）；Runner 结束时不删除容量 Connector 以外的正式 Topic。

## 3. 执行流程

### 3.1 手动认证工作流

仓库提供 `.github/workflows/kafka-capacity.yml`，它只允许 `workflow_dispatch`，不会随 push、PR 或定时任务自动产生负载；Job 只接受 `main` 分支，并以专用集群为全局并发键串行执行。Job 固定绑定受保护的 GitHub Environment `kafka-capacity` 和 `[self-hosted, linux, x64, kafka-capacity]` Runner 标签；首次运行前必须为 Environment 配置并核验 required reviewers 与只允许 `main` 的 deployment branch policy。Linux Runner 必须位于获准访问专用 Kafka 的隔离网络，禁止把标签挂到普通共享 Runner。

Environment 需要配置以下 Secret：

- `KAFKA_CAPACITY_EXPECTED_CLUSTER_ID`
- `KAFKA_CAPACITY_BOOTSTRAP_SERVERS`
- `KAFKA_CAPACITY_SASL_USERNAME`、`KAFKA_CAPACITY_SASL_PASSWORD`（使用 SASL 时）
- `KAFKA_CAPACITY_SMOKE_BUDGET_JSON`（可选；必须精确覆盖 smoke 计划）
- `KAFKA_CAPACITY_MATRIX_BUDGET_JSON`（可选；必须精确覆盖 matrix 计划）
- `KAFKA_CAPACITY_MYSQL_CONNECTION_STRING`（`scope_b_smoke` / `scope_c_smoke`；缺失时对应 Profile **安全跳过** `exit 0`，只打印 Secret 名）
- `KAFKA_CAPACITY_CONNECT_BASE_URI`（仅 `scope_c_smoke`；缺失时安全跳过；禁止写入日志或命令行参数）

两份 Budget 独立配置，禁止用 matrix Budget 运行 smoke 或反向复用。选中 Profile 的 Budget 原文只写入 Runner 临时目录；工作流在写入前清理同名残留，并通过退出 Trap 与 `always()` 清理步骤双重删除。

非敏感 GitHub Environment Variables：

- `KAFKA_CAPACITY_SECURITY_PROTOCOL`
- `KAFKA_CAPACITY_SASL_MECHANISM`（使用 SASL 时）
- `KAFKA_CAPACITY_MYSQL_DATABASE_NAME`（Scope B/C；默认 `fullnet_capacity`）
- `KAFKA_CAPACITY_CONNECT_INTERNAL_BOOTSTRAP`（可选；Connect 容器内 Kafka bootstrap）

触发时必须填写 `approval_id` 与 `reason`，并选择 Profile：

| Profile | Scope | 用途 |
| --- | --- | --- |
| `smoke`（默认） | `kafka_transport` | 两档缩小链路检查，只证明执行链与正确性门禁可运行 |
| `matrix` | `kafka_transport` | 60 个有界低速/吞吐样本；正式测量合计 60 分钟，计入预热与最坏排空后理论上界约 190 分钟 |
| `scope_b_smoke` | `worker_inbox_handler` + `--host-parity-mode worker` | MySQL Inbox/Handler 缩小 smoke；缺 DB Secret 时跳过 |
| `scope_c_smoke` | `transaction_outbox_cdc` + **强制** `--host-parity-mode worker` | Outbox→CDC→Kafka→Inbox 缩小 smoke；缺 DB 或 Connect Secret 时跳过；`--drain-seconds 45` |

工作流硬超时为 240 分钟。`matrix` 仍不包含 Soak、N+1 或故障恢复。`smoke`/`matrix` 固定 `--delete-topic false` 和唯一输出目录。`scope_c_smoke` 复用同一证据目录与 `Capacity-not-verified` 元数据；Connect 端点与数据库连接串仅经环境变量注入 Runner，不得出现在 `echo`、命令参数或上传工件明文中。缺少受保护 Kafka Cluster Secret、审批、专用 Runner 或 ClusterId 不匹配时，工作流应失败关闭；Scope B/C 依赖 Secret 缺失则按上表安全跳过，禁止用空连接串硬跑。容量 Budget 仍是可选的性能判定门禁；没有 Budget 的 `matrix` 只能形成观测证据，不能形成性能预算通过结论。每次运行只上传当前 `run_id/run_attempt` 的报告、Checkpoint 和工作流元数据并保留 30 天，正式评审前应转存到受控证据库。

正式生产等价矩阵与 Soak 仍须在按 Provider 隔离的 Environment、Secret 和清理策略齐备后手工或专用 Profile 执行；本工作流的 `scope_c_smoke` **不能**解除 ADR-0006 影子运行，也不得把 `Messaging:DeliveryCutover:Enabled` 设为 `true`。

### 3.2 本地或专用主机命令

先查看参数并做不连接 Broker 的计划验证：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release -- kafka-capacity --help

dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release -- kafka-capacity `
  --scope 'kafka_transport' `
  --settings '<protected-settings.json>' `
  --scenarios 'low-rate,throughput' `
  --low-rates '10' `
  --throughput-rates '1000' `
  --payload-sizes '256' `
  --execute false
```

缩小 smoke 只用于验证执行链，不是容量结论：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release -- kafka-capacity `
  --settings '<protected-settings.json>' `
  --execute true `
  --approval-id '<change-or-ticket-id>' `
  --reason 'approved transport smoke' `
  --run-id 'smoke-<utc-id>' `
  --output '<protected-artifact-directory>' `
  --scenarios 'low-rate,throughput' `
  --low-rates '20' `
  --throughput-rates '200' `
  --payload-sizes '128' `
  --producer-concurrency '2' `
  --partitions 2 `
  --replication-factor 1 `
  --warmup-seconds 1 `
  --duration-seconds 2 `
  --drain-seconds 15 `
  --max-messages-per-sample 1000
```

正式手工认证必须使用已批准的矩阵、持续时间、副本因子和预算文件，并保留默认 `--delete-topic false` 供复核。Scope A/B 为每次运行创建 `fullnet.capacity.<run-id>.v1`；Scope C 解析/预创建 Debezium 路由 Topic `fullnet.capacity.cdc.fullnet.capacity.worker.message`（按 MessageType 替换末段）。每个样本使用独立临时 Group；Consumer 在 Producer/Outbox 启动前查询全部计划分区的具体高水位并以该 Offset 显式分配，只有分配完成后才允许发送首条消息。Runner 不提交 Offset，也不修改任何正式或旁路 Group 水位。Admin、Producer 和 Consumer 都使用按 Run/Sample/角色隔离且有长度上限的 ClientId。

Scope C 缩小 smoke 示例（Connect + 预迁移 MySQL 容量库；凭据从 Secret 注入）：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release -- kafka-capacity `
  --scope 'transaction_outbox_cdc' `
  --host-parity-mode 'worker' `
  --settings '<protected-settings.json>' `
  --execute true `
  --approval-id '<change-or-ticket-id>' `
  --reason 'approved scope c smoke' `
  --run-id 'scope-c-smoke-<utc-id>' `
  --output '<protected-artifact-directory>' `
  --scenarios 'low-rate' `
  --low-rates '20' `
  --payload-sizes '128' `
  --producer-concurrency '2' `
  --partitions 2 `
  --replication-factor 1 `
  --warmup-seconds 0 `
  --duration-seconds 2 `
  --drain-seconds 45 `
  --max-messages-per-sample 100
```

## 4. 正确性、预算和背压

以下正确性条件是不可关闭的硬门禁：Ack 与消费数量一致、零丢失、零重复、零损坏、分区内零乱序、零未 Flush、零非法序号且排空完成。Scope C 额外要求 `Enqueued == Acknowledged == CdcPublished == Consumed`（`samples.ndjson` 的 `outboxCdc.cdcPublished` 扩展字段），Outbox 负载必须使用 Envelope V2 认可的 MessagePack ContentType 进入 CDC Header，否则生产 `KafkaEnvelopeReader` 会在 Inbox 前拒绝消息。发送使用绝对到达时间的有界开放环调度；librdkafka 本地队列满时在同一有界阶段内可取消重试，消息只有被 Producer 接受后才保留入队证据。连续 10 个一秒窗口低于目标 95%、周期快照及最终证据的实际调度 P99 超过预算上限（无预算时为 5 秒）、托管堆峰值超过 2 GiB、延迟直方图溢出或发现正确性错误时，立即停止当前样本并阻止后续升档。停止后 Flush、Broker 水位取证、消费排空和 Consumer Close 共用剩余 `drain-seconds`，关闭阶段不得重新获得完整超时。

性能预算是可选门禁，必须精确绑定环境、ClusterId 摘要、基线提交和完整场景键。示例：

```json
{
  "schemaVersion": 1,
  "environmentName": "CapacityCertification",
  "clusterIdHash": "<sha256-lower-hex>",
  "baselineGitCommit": "<full-git-commit>",
  "generatedAtUtc": "2026-08-12T00:00:00Z",
  "entries": [
    {
      "scopeCode": "kafka_transport",
      "scenario": "LowRate",
      "targetMessagesPerSecond": 20,
      "payloadSizeBytes": 128,
      "partitions": 2,
      "producerConcurrency": 2,
      "minimumConsumedMessagesPerSecond": 19,
      "maximumEndToEndP95Microseconds": 50000,
      "maximumEndToEndP99Microseconds": 100000
    }
  ]
}
```

通过 `--budget '<budget.json>'` 启用。Runner 在创建 Topic 前验证预算身份和完整矩阵，预算内容摘要进入续跑指纹；每个样本完成后立即评估，失败样本不会进入可跳过 checkpoint，也不会继续更高吞吐档。预算不能覆盖正确性失败；无预算只表示未执行性能阈值比较，不表示容量达标。

## 5. Checkpoint、续跑和 Topic 清理

每个完整关闭且通过已提供预算的样本后原子更新 `checkpoint.json`。续跑必须保持 Git 提交、Runner Schema、Scope、RunId、TopicId、ClusterId、SASL 用户摘要、脱敏 Kafka 性能配置、预算摘要和全部场景参数一致；任一漂移都失败关闭。不传 `--run-id` 时，Runner 会从现有 checkpoint 恢复原始 RunId。同一输出目录通过 `.run.lock` 排除并发写入；默认输出目录包含随机后缀，避免同秒启动碰撞。

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release -- kafka-capacity `
  --settings '<protected-settings.json>' `
  --execute true `
  --approval-id '<change-or-ticket-id>' `
  --reason 'resume approved transport run' `
  --output '<same-protected-artifact-directory>' `
  --resume true `
  --max-new-samples 1
```

不完整样本和预算失败样本会进入报告但不会进入可跳过集合，续跑时会重新执行。默认保留 Topic。只有显式 `--delete-topic true`、全部计划样本均完整成功、通过已提供预算且运行未取消时才允许删除；删除前重新查询 ClusterId、Topic 名和 TopicId，防止误删同名替换 Topic。分段执行、取消或失败时即使请求删除也会保留 Topic。

## 6. 工件与退出码

输出目录包含：

- `checkpoint.json`：原子续跑状态与完整样本；
- `manifest.json`：脱敏运行清单，保存兼容显示名 `Scope`、稳定机器码 `ScopeCode`，状态固定为 `Capacity-not-verified`；
- `samples.ndjson`：正确性、调度/入队/Ack/消费/排空速率、基于各分区 Broker 高水位与 Consumer Position 的真实 Offset 积压、未消费年龄上界、延迟和资源峰值；
- `latency-histograms.json`：调度、Broker Ack 和端到端延迟分位数；
- `librdkafka-statistics.ndjson`：按 SampleId 和阶段分别保留有界快照，只含队列、字节、请求延迟、Broker 状态及错误计数白名单；每个样本的队列峰值只聚合 measurement/drain，截断数量由样本证据显式记录；
- `summary.json`、`summary.md`：完成数、不完整数和稳定失败码。

| 退出码 | 含义 |
| --- | --- |
| `0` | dry-run 成功，或全部已执行样本通过正确性及可选预算 |
| `2` | CLI、JSON、预算、checkpoint 或指纹配置无效 |
| `3` | 执行许可、环境、ClusterId、Broker 数或 Topic 所有权被拒绝 |
| `4` | Kafka/文件依赖失败，或样本不完整 |
| `5` | 正确性硬门禁失败 |
| `6` | 正确性通过，但性能预算失败 |
| `130` | 用户取消；Runner 停止新发送、限时排空并尽力写入不完整证据 |

## 7. 故障处理

- `cluster_id_mismatch`、`cluster_identity_changed`：停止操作，核对审批目标和 DNS/Bootstrap 配置，禁止修改 ExpectedClusterId 迁就未知集群。
- `topic_exists`、`topic_identity_changed`：不要手工删除；先核对 checkpoint、TopicId 和同名 Topic 的创建来源。
- `scheduling_rate_below_95_percent`：说明本机、Producer 队列或 Broker 已连续饱和；降低目标只用于定位，不能把较低目标冒充原预算通过。
- `schedule_latency_limit_exceeded`、`managed_heap_limit_exceeded`、`latency_histogram_overflow`：保护性停止已触发；保留本档证据并先定位资源或调度瓶颈，禁止继续升档。
- `producer_flush_incomplete`、`delivery_not_persisted`、`consumer_stopped`、`cancelled`：保留 Topic 和工件，恢复依赖后使用同一输出目录续跑。
- `cdc_drain_timeout`、`scope_c_correctness_failed`：Scope C CDC 或 Handler 未在 `drain-seconds` 内对齐；先延长排空或降低速率，禁止删除 Debezium Topic 后伪造通过。
- `connect_not_healthy`、`connect_configuration_invalid`：核对 Connect REST、Connector 模板占位符与容量库网络；禁止在预检失败时继续 Kafka 负载。
- `payload_corrupted`、`consume_tracking_failed`、丢失、重复或乱序：按正确性事故处理，禁止只重跑到绿色后覆盖原始工件。
- 报告或 checkpoint 写入失败：视为没有形成可信完成证据；修复受保护目录的空间、权限和原子替换条件后再续跑。

仓库中的真实 Kafka 缩小测试使用 `apache/kafka:4.1.2`，覆盖低速/吞吐正确性、旁路 Group Offset 不变、TopicId 替换删除保护与取消证据。若本机 Docker 不可用，只能记录为环境阻断；编译或单元通过不能替代真实 Kafka 和专用生产等价环境认证。
