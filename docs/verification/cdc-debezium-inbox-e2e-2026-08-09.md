# CDC → Debezium → Kafka → Inbox E2E 验证（2026-08-09）

**状态：** 进行中（Consumer 性能硬化已完成代码与聚焦验证，真实容量未认证）
**切流门禁：** `Messaging:DeliveryCutover:Enabled` 保持 `false`；容量标记 `Capacity-not-verified`

## 固定测试镜像

| 组件 | 镜像 |
| --- | --- |
| Kafka | `apache/kafka:4.1.2` |
| Debezium Connect | `quay.io/debezium/connect:3.4.3.Final` |
| SQL Server | `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04` |
| MySQL | `mysql:8.0`（`ROW` + `FULL` binlog row image） |

## 已落地基础设施

- `CdcDebeziumPipelineFixture`：Kafka + Connect 测试栈（内部 `kafka:9092`，宿主 `EXTERNAL` 监听器）
- `DebeziumConnectAdminClient` / `DebeziumConnectorTemplateFactory`：Connect REST 与 `deploy/messaging/connectors/*` 模板替换
- `CdcDebeziumE2ESupport`：Kafka 消费与 Inbox 断言辅助
- `SqlServerCdcDebeziumInboxE2ETests` / `MySqlCdcDebeziumInboxE2ETests`：真实链路入口（环境不足时 `Assert.Inconclusive`）
- Connector 模板补充 Outbox → Kafka header 映射（`event_id`、`message_type`、`trace_parent` 等）
- `MessagingOutboxTestSupport`：CDC 测试写入 append-only 表（`CdcKafka` owner + permissive gate）

## 本地命令

```powershell
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj `
  --filter "FullyQualifiedName~MySqlCdcDebeziumInboxE2ETests"
```

**最新结果（2026-08-09）：** MySQL 7/7 通过（含失败场景）；SQL Server happy path 仍为 Inconclusive。

```powershell
dotnet test tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj `
  --filter "FullyQualifiedName~SqlServerCdcDebeziumInboxE2ETests"
```

## 当前结果（2026-08-09）

| 场景 | SQL Server | MySQL |
| --- | --- | --- |
| Connect 启动 | 待验证 | 已通过（~18s 就绪） |
| Connector task 健康 | 待验证 | 已通过（schema history RF=1 + kafka:9092 bootstrap） |
| Outbox INSERT → Kafka | **Inconclusive**（CDC Agent/capture job 缺口，与既有 shadow 测试一致） | **已通过**（真实 Debezium Outbox Router） |
| Kafka → Inbox | 待验证 | **已通过**（`InboxConsumeStatus.Processed`） |
| 重复投递幂等 | 待验证 | **已通过**（第二次 `AlreadyProcessed`） |
| Connector 重启 + Schema History | 待验证 | **已通过**（删除并重建 Connector 后继续投递） |
| Offset 未提交重投 | 待验证 | **已通过**（同组未提交 Offset → `AlreadyProcessed`） |
| Connector 暂停/恢复 | 待验证 | **已通过**（暂停期间不投递，恢复后补发） |
| CDC 信封 Retry 路由 | 待验证 | **已通过**（真实 Debezium 消息进入 `.retry.5s`） |
| Broker 短暂中断 | 待验证 | **已通过**（Pause/Unpause 后继续投递） |

### 关键修复（本轮）

1. Connector `schema.history.internal.kafka.bootstrap.servers` + `topic.replication.factor=1`
2. Connector 级 `key.converter=StringConverter`（避免 JsonConverter 包装 partition key）
3. `KafkaEnvelopeReader` 兼容 Debezium Outbox：`event_id` Base64 Binary16（`bigEndian: true`）、微秒时间戳、`id` 头
4. `MessagingOutboxTestSupport` 写入 append-only 表（`CdcKafka` owner）

## 未验证项 / 风险

1. SQL Server CDC Agent/capture job 在 Testcontainers 中可能长期 `Inconclusive`（与既有 shadow 测试一致）。
2. MySQL Debezium 任务状态与 binlog 位点需进一步采集（失败时输出 Connector `/status` JSON）。
3. 失败场景矩阵：MySQL 已覆盖重复投递、Connector 重启、Offset 未提交重投、Connector 暂停/恢复、Retry 路由与 Broker 中断；DLQ/Rebalance/切流回退 E2E 仍待实现。
4. 生产 `IEventDeliveryRollbackReadinessReader` 已提供 Kafka Connect 适配器（`Messaging:KafkaConnectRollback:Enabled` 门控）；默认仍为失败关闭。
5. Consumer 已改为每分区容量 1 的有界通道、跨分区并行、分区局部 Pause/Seek、分配代次 Fence 与连续成功 Offset 水位；尚未在生产等价 Kafka/数据库环境完成吞吐、P95/P99 与资源上限认证，继续标记 `Capacity-not-verified`。

## Consumer 性能硬化验证（2026-08-09）

- 基线提交：`35d07cefafeb9879330eddb68228b0c0d8240b2b`
- Kafka/Inbox/Dispatcher/Ownership 聚焦单元测试：71/71 通过，覆盖跨分区并行、同分区单在途、待完成命令短 Poll、失败局部 Seek、退避恢复、Offset 空洞、连续水位、Rebalance 迟到完成、有界关闭、Lane Task 释放、单查询 Fence 与单命令 Inbox claim。
- Kafka Subscription/Failure Recovery 与 SQL Server/MySQL Inbox 集成测试：19/19 通过；手动提交场景按 Kafka 真实语义使用同组新实例验证未提交 Offset 重投。
- `slice` 受影响集：Smoke 8/8、Outbox 双 Provider 聚焦集合 14/14 通过；Release 构建 0 警告、0 错误。
- `merge` 受影响集：Outbox + Smoke 双 Provider 合并候选集合 22/22 通过；Release 构建 0 警告、0 错误。
- Schema 定向复验：SQL Server 与 MySQL Inbox 均为 12 列；MySQL 主键按服务端规范名 `PRIMARY` 校验，修正后的双库断言通过。
- 架构门禁：99/99 通过；新增 Inbox/Ownership SQL 和 Cursor 前置提交遗漏的 5 条 producer fence SQL 已精确登记。
- 本轮没有专用生产等价压测数据，不据此声明固定 QPS 或容量达标。

## 下一步

1. 补齐 DLQ、Rebalance、切流/回退 E2E 演练。
2. 在真实 Connect 环境验证 `KafkaConnectEventDeliveryRollbackReadinessReader` 与双库 producer fence 采集。
3. SQL Server 在具备 CDC Agent 的环境中复验 happy path。

## Wolverine 参考能力吸收进度（2026-08-10）

- 已实现并通过聚焦单元测试：Classic Cooperative Sticky 的显式离线迁移门禁（存量 Group 默认保持 Legacy Range）、Static Membership、Kafka 4.x Consumer Protocol 显式互斥配置、Producer 有界批量/等待/队列/在途参数。
- 已确认现有实现：同一 `ConsumerName` 多 Topic 共用单 Consumer、连续 Offset 水位、Retry/DLQ 与诊断 Header 主体、单查询所有权 Fence。
- `Build-verified`：同分区按 Key 固定槽并行、全局/分区高低水位背压、连续水位的 PerMessage/Periodic Commit、Inbox 双库只读批量预检、时间或 Offset 范围重放、Handler 源生成优先路由、Activity/Gauge 与运维文档。范围重放默认关闭并受 1000 条/32 分区/45 秒同步上限约束。
- 新证据覆盖 Rebalance 最终提交失败后的水位丢弃、旧 epoch 同 Offset ABA 隔离、Handler 运行中 Gauge 的有序快照更新、真实 Kafka 固定范围不改变正式 Group 水位，以及真实 Kafka → `KafkaReplayMessageProcessor` → Dispatcher → SQL Server/MySQL Dapper Inbox 的组合链路；同一消息首次处理成功、再次重放命中 Inbox 去重。双库 Inbox 并发、Helm 启用/缺 Secret/非法 SASL 机制反例也已覆盖；仍无生产等价吞吐、低速延迟、Soak 与完整双库 CDC 故障矩阵，因此总体状态继续为 `Capacity-not-verified`，不得据此开启生产切流。
- Helm 当前以 Deployment Pod 名注入 `group.instance.id`：可保持同一 Pod 内容器重启身份，但滚动替换会产生新 Pod 名，不能据此宣称已消除滚动发布 Rebalance；要获得跨 Pod 替换的静态身份收益，仍需在 F1 阶段完成稳定实例身份拓扑决策与演练。

### F0 可追溯验证证据

- 任务基线：`2e49b022a108551241563f1192ba4adabd98e027`；任务快照：`codex-wolverine-reference-hardening-20260810`；环境：Windows、.NET SDK `10.0.400-preview.0.26322.102`、Confluent.Kafka `2.15.0`。
- `dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~Kafka|FullyQualifiedName~IntegrationEventConsumerDispatcherTests|FullyQualifiedName~DapperIntegrationEventInboxTests"`：78/78 通过。
- `pnpm test:helm`：12/12 通过；包含 CdcKafka 正向渲染，以及旧 Broker 启用 Consumer Protocol、未迁移启用 Cooperative Sticky、Producer 队列低于 1 MiB 三类失败关闭反例。
- `pnpm test:dotnet:architecture`：99/99 通过；Release 构建 0 警告、0 错误。
- `pnpm test:governance`：27/27；`pnpm test:naming`：24/24；`pnpm test:performance-governance`：9/9，全部通过。
- `pnpm test:integration:affected -- --snapshot codex-wolverine-reference-hardening-20260810 --phase slice`：Integration 工具链 39/39、Smoke 8/8 通过；Smoke 用时 2 分 10 秒。
- 未执行生产等价 Kafka 4 入组、Assignor 离线迁移、静态成员滚动替换、低速延迟或峰值吞吐基准，因此这些能力不能提升为 `Production-verified`。
