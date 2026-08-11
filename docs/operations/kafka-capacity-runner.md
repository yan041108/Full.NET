# Kafka Transport Capacity Runner

## 1. 适用边界

`kafka-capacity` 是独立的 Scope A 传输容量工具，只测量 Full.NET 生产 Kafka 配置构建器生成的 Producer、临时 Topic 和临时 Consumer Group。它不启动 CDC、Outbox、Inbox、业务 Handler、Retry/DLQ 或正式 Worker，因此结果不能代表完整业务事件链路，也不能解除 ADR-0006 的影子运行与切流门禁。

当前能力状态固定为 `Capacity-not-verified`。只有在专用生产等价 Kafka 上完成低速延迟、饱和吞吐、故障、Soak、N+1 和恢复演练，并归档可复核证据后，才能另行评审生产容量状态。后续 Scope B（Worker＋Inbox＋Handler）与 Scope C（业务事务＋Outbox＋CDC＋Kafka＋Inbox）必须新增 `IKafkaCapacityScenarioDriver`，复用现有配置保护、Topic 所有权、调度、正确性、预算、checkpoint 和报告边界。

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

## 3. 执行流程

先查看参数并做不连接 Broker 的计划验证：

```powershell
dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release -- kafka-capacity --help

dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj `
  -c Release -- kafka-capacity `
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

正式手工认证必须使用已批准的矩阵、持续时间、副本因子和预算文件，并保留默认 `--delete-topic false` 供复核。Runner 为每次运行创建 `fullnet.capacity.<run-id>.v1`，每个样本使用独立临时 Group；Consumer 在 Producer 启动前完成分区分配，从当时 Topic 末端读取，不提交 Offset，也不修改任何正式或旁路 Group 水位。

## 4. 正确性、预算和背压

以下正确性条件是不可关闭的硬门禁：Ack 与消费数量一致、零丢失、零重复、零损坏、分区内零乱序、零未 Flush、零非法序号且排空完成。发送使用绝对到达时间的有界开放环调度；队列满时产生背压，连续 10 个一秒窗口低于目标 95% 会停止样本并报告稳定错误码，禁止无界追赶掩盖饱和。

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

通过 `--budget '<budget.json>'` 启用。预算不能覆盖正确性失败；无预算只表示未执行性能阈值比较，不表示容量达标。

## 5. Checkpoint、续跑和 Topic 清理

每个完整关闭样本后原子更新 `checkpoint.json`。续跑必须保持 Git 提交、Runner Schema、Scope、RunId、TopicId、ClusterId、脱敏 Kafka 性能配置和全部场景参数一致；任一漂移都失败关闭。不传 `--run-id` 时，Runner 会从现有 checkpoint 恢复原始 RunId。

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

不完整样本会进入报告但不会进入可跳过集合，续跑时会重新执行。默认保留 Topic。只有显式 `--delete-topic true`、全部计划样本均完整成功且运行未取消时才允许删除；删除前重新查询 ClusterId、Topic 名和 TopicId，防止误删同名替换 Topic。分段执行、取消或失败时即使请求删除也会保留 Topic。

## 6. 工件与退出码

输出目录包含：

- `checkpoint.json`：原子续跑状态与完整样本；
- `manifest.json`：脱敏运行清单，状态固定为 `Capacity-not-verified`；
- `samples.ndjson`：正确性、速率、延迟和资源样本；
- `latency-histograms.json`：调度、Broker Ack 和端到端延迟分位数；
- `librdkafka-statistics.ndjson`：只含数值白名单的客户端统计；
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
- `producer_flush_incomplete`、`delivery_not_persisted`、`consumer_stopped`、`cancelled`：保留 Topic 和工件，恢复依赖后使用同一输出目录续跑。
- `payload_corrupted`、`consume_tracking_failed`、丢失、重复或乱序：按正确性事故处理，禁止只重跑到绿色后覆盖原始工件。
- 报告或 checkpoint 写入失败：视为没有形成可信完成证据；修复受保护目录的空间、权限和原子替换条件后再续跑。

仓库中的真实 Kafka 缩小测试使用 `apache/kafka:4.1.2`，覆盖低速/吞吐正确性、旁路 Group Offset 不变、TopicId 替换删除保护与取消证据。若本机 Docker 不可用，只能记录为环境阻断；编译或单元通过不能替代真实 Kafka 和专用生产等价环境认证。
