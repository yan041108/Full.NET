# CDC / Kafka 切流、停止、Fence、排空、回退与 DLQ 重放 Runbook

权威架构见 [`ADR-0006`](../architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md) 与运维概述 [`cdc-kafka-event-delivery.md`](../operations/cdc-kafka-event-delivery.md)。本 runbook 记录目标操作顺序、当前实现限制及**停止 / 回滚条件**；标记为待实现的步骤不能作为现有自动化能力执行。

第二批已实现持久回退、跨实例恢复、API 专用控制面与短期影子证明；本地定向结果及未完成门禁见[第二批记录](../superpowers/plans/2026-10-24-messaging-operations-batch2.md)。本地 Broker/双库与 HTTP 故障模型不能替代真实 Debezium/Agent 切流回退演练。

2026-10-25 已补齐[双库完整真实链路](../superpowers/plans/2026-10-25-messaging-real-cutover-rollback.md)：MySQL Binlog、独享 SQL Server Agent → Debezium/Kafka → Inbox，覆盖真实业务 HTTP 切流、实际暂停后的最终审计失败补偿、成功回退与 Completed 恢复幂等，2 通过、0 失败、0 跳过。该场景使用同进程的生产 Worker 组件；多进程重启、断网、Soak、容量及独立复核未验收。

> **硬边界：**生产默认 Messaging:DeliveryCutover:Enabled=false。在当前代码的真实双库 CDC E2E、告警/恢复演练与容量门禁完成前，禁止正式切流，保持 Capacity-not-verified，不标记 Production-verified。

### 控制面配置与资源约束

受理请求的 API 需配置 Messaging:Kafka:Enabled 和 Messaging:KafkaConnectRollback；API 使用专用注册，不启动常驻消费者。未启用时仍失败关闭。以下仅展示配置字段，Enabled 保持 false：

```json
{
  "Messaging": {
    "ShadowCapture": {
      "Enabled": false,
      "EventType": "fullnet.organization.unit.changed",
      "SchemaVersion": 1,
      "MaximumEvents": 1000,
      "MaximumPayloadBytes": 33554432,
      "MaximumWindowSeconds": 300
    },
    "DeliveryCutover": { "Enabled": false },
    "KafkaConnectRollback": {
      "Enabled": false,
      "ConnectBaseUri": "http://connect:8083",
      "PrepareTimeoutSeconds": 120,
      "DrainTimeoutSeconds": 120,
      "DrainPollIntervalMilliseconds": 1000,
      "Streams": [{
        "EventType": "fullnet.organization.unit.changed",
        "SchemaVersion": 1,
        "ConnectorName": "fullnet-organization-pilot",
        "ShadowConnectorName": "fullnet-organization-pilot-shadow",
        "TopicName": "organization.unit-changed.v1",
        "ShadowTopicName": "fullnet.organization.unit-shadow.v1",
        "SourceDatabaseName": "实际受控源库名",
        "WorkerClientId": "实际目标Worker的Kafka客户端前缀",
        "ConsumerGroupId": "fullnet.identity.organization-unit-projection"
      }]
    }
  }
}
```

ConsumerGroupId 必须取自实际订阅目录，WorkerClientId 必须匹配目标 Worker 的 Kafka ClientId 前缀；两者均不能凭示例猜测。Connect 与 API 观测的 Broker ClusterId 必须一致；URI 不允许凭据、子路径、查询串或片段。单次准备/排空超时各为 1～300 秒，轮询间隔 10～10000 毫秒。

本协议只支持一个活动 CDC 试点流。目标 Connector 必须是正确源库/追加 Outbox 表的专用 SQL Server 或 MySQL Debezium Connector，tasks.max=1、snapshot.mode=no_data、仅 outbox EventRouter、显式 StringConverter key/ByteArrayConverter value、固定路由至 TopicName、仅插入事件及完整信封 Header。其他活动 CdcKafka 流（含目录默认值）、共享 Connector 映射和 producer.override.bootstrap.servers 均拒绝。仓库 *-outbox-shadow.json 是影子路由模板，不能直接当作已就绪正式 Connector；独立影子写入/捕获流程须提供同一受控源集的完整观察，不能靠请求布尔值或手工改状态制造通过。

LegacyPolling/ShadowCdc 的主发布路径写 Legacy 队列；只有显式部署策略及活动采集代次匹配时，才在同一业务事务把真实信封复制到独立 `fn_messaging_shadow_event`，不写正式追加表。空源、未关闭、元数据缺口、代次或行数/字节不完整时证明拒绝。总览中的开始/关闭入口要求 `messaging.delivery.cutover` 和理由；新代次会清理上一代诊断样本并使旧证明失效。所有生产者实例须配置同一策略，部署步骤见 [影子采集说明](../../deploy/messaging/README.md#bounded-real-legacy-capture)。本地双库/Broker 证据不能替代实际 Debezium/Agent 环境验收。

## 0. 观测前置（切流前必查）

| 指标（代码仪表名 → Prometheus） | 用途 |
| --- | --- |
| `fullnet.outbox.backlog.oldest_age` → `fullnet_outbox_backlog_oldest_age_seconds` | Legacy 积压年龄 |
| `fullnet.outbox.legacy.empty_poll.backoff` → `fullnet_outbox_legacy_empty_poll_backoff_seconds` | 空轮询退避 |
| `fullnet.outbox.commit_to_capture` → `fullnet_outbox_commit_to_capture_seconds` | 提交到捕获延迟（影子路径或平台填充） |
| `fullnet.messaging.kafka.consumer.lag` → `fullnet_messaging_kafka_consumer_lag` | Consumer 消息滞后 |
| `fullnet.messaging.kafka.lag_retention_ratio` → `fullnet_messaging_kafka_lag_retention_ratio` | 滞后相对保留窗口占比 |
| `fullnet.messaging.connector.lag` / `offset.unrecoverable` | Connector 滞后与位点不可恢复（可由 `UpdateConnectorHealth` 填充） |
| `fullnet.messaging.cdc.sqlserver.capture_job_running` | SQL Server CDC Capture Job（平台填充） |
| `fullnet.messaging.cdc.mysql.binlog_retention_hours` | MySQL Binlog 保留小时（平台填充） |
| `fullnet.messaging.inbox.duplicates` / `kafka.retry.routed` / `kafka.dead_letter.published` | 幂等命中、Retry、DLQ |
| `fullnet.messaging.ownership.wait` / `ownership.transitions` | 所有权等待与转换 |

**停止条件（任一项）：** 上表关键告警处于 firing；`lag_retention_ratio > 0.8`；Capture Job=0；Binlog 保留 &lt; 24h；Connector `offset.unrecoverable=1`；Outbox/Jobs 最老年龄持续 &gt; SLA。

指标标签禁止：Secret、Payload、原始 SQL、TenantId、UserId、异常文本、MessageId。

## 1. 切流（Legacy → CdcKafka）

1. 保持 `Messaging:DeliveryCutover:Enabled=false`。先核对真实 DLQ 运维、切流证据、API 回退装配与持久恢复协议已经实现，且拟切流范围的实际验收通过；任一缺口未关闭即停止，不得先打开开关再排查。
2. 核对目标流在 Topic Catalog 与 `fn_messaging_stream_ownership` 中存在，且当前有效所有权为 `LegacyPolling`。
3. 排空目标流 Legacy Outbox：pending / due retry / active lease / dead letter 均为 0。`GET /api/v1/messaging/delivery-status` 返回有界单流聚合和稳定阻断原因；未知/截断不能按零处理，切流事务仍须重新检查。先在显式开启采集策略、配置独立影子 Connector 且持久 Legacy 所有权存在的流上，经总览开始采集真实业务写入。关闭当前代次后核对缺口为零、真实行数/字节与固定预算一致；期限到达但尚未持久关闭时须先关闭。先调用 POST /api/v1/messaging/delivery/cutover-evidence，提交 eventType、schemaVersion、reason。服务端只比对当前已关闭代次的独立影子源与专用影子 Topic；样本 1～10000 条、载荷不超过 32 MiB、最新源事件距当前不超过 5 分钟，证明有效期 5 分钟。任一源变化、缺失/变更副本/损坏、分区或保留窗口变化、双 Connector 身份/配置或成员变化均需重新验证；完全相同的至少一次副本只计一次。新采集代次会使旧证明失效，不能复用旧 generation。
4. 记录切流前位点：SQL Server LSN 或 MySQL binlog position、Kafka high watermark、Consumer Group offset、Connector offsets。
5. 完成上述核验后，仅在已批准变更窗口为受理请求的 API 显式启用切流配置并确认生效，再调用 `POST /api/v1/messaging/delivery/cutover`（权限 `messaging.delivery.cutover`），Reason 必填。Worker/Connector 的实际模式和运行状态在前置核验中确认，不能靠切流开关自动改变。
6. 核对持久化所有权、切流领域审计和业务投影收敛，并观察 Consumer lag、Inbox duplicate、Retry/DLQ。`fullnet.messaging.ownership.transitions` 的当前生产调用记录 Consumer 的 `revoked`/`restored`，不能假定切流 API 已发出 `cutover` 计数；按实际 exporter 名称查询 `result=~"revoked|restored"`，不能使用精确匹配表达式 `result="cutover|revoked|restored"`。
7. 变更结束后将 API 切流开关恢复为 false 并确认生效；这只阻止新的切流请求，不撤销已持久化的所有权。

**停止条件：** 步骤 1～5 任一步失败；切流后出现双发布；Inbox 同 ID 不同 Hash；外部不可逆副作用重复。

**回滚条件：** 切流后 15 分钟内 lag/错误率超预算，或对账失败 → 进入第 4 节受控恢复。通过第 4 节查询原代次并受控恢复；不得强改数据库所有者或删除保护。

## 2. 停止（紧急停止生产副作用）

1. 将 `DeliveryCutover:Enabled` 置回 `false`（阻止新的切流 API）。
2. 先确认受影响流、共享 Connector/Worker 和在途处理范围，通过部署层已有控制停止或隔离相关消费者；暂停 Connector 本身不能阻止消费者处理 Broker 中已有消息。当前没有已交付的通用单流紧急停止 API，不能据此假定可无影响地停止一个流。
3. 按已确认范围暂停 Debezium Connector，验证 Connector/任务状态并保留 offsets Topic；需要中止新增业务事件时，另经授权限制对应生产入口。改变 Worker 模式不能代替数据库所有权回退或 producer fence；所有权仍为 CdcKafka 时，新事件继续写追加表，暂停 Connector 后会积压。紧急隔离不等于已经完成正常回退，后续恢复仍须重新取得有效位点和排空证明。
4. 记录停止时刻的位点与告警快照。

**停止条件：** 已满足“无新 CdcKafka 写入副作用”证明前，不得恢复切流开关。

## 3. Fence 与排空 / 隔离

1. 回退准备态（`RollbackState=Preparing`）期间：新生产者写 Outbox 必须失败关闭；Kafka Consumer 可继续排空。
2. 控制面按同一 `RollbackGeneration` 持久记录资源与执行意图，在 producer fence 下等待 CDC 覆盖边界后暂停 Connector 并验证任务状态；不能先暂停落后的 Connector 再期待它继续推进位点。阶段和不可撤销的暂停意图先持久化，资源摘要不包含凭据。
3. Broker：排空所有相关 Consumer Group、Retry Topic 与在途处理，随后完成消费者隔离证明；无法证明排空时执行经批准的隔离处置，禁止静默丢弃未消费消息。当前 reader 遍历目录内全部相关组及 Retry Topic，连续两轮完整零 lag 才通过；未知或超前提交位点均拒绝。最终流级独占门等待在途处理。
4. 取得覆盖数据库 producer fence 的 CDC 位点机器证明后再进入最终回退事务。

SQL Server 要求受控 CT 中插入记录覆盖全部保留的冻结源行；历史行已超出 CT 保留期时，即使全库 max LSN 很新也拒绝。此情况先对账并走授权恢复处置，不能清表或伪造证明。

**停止条件：** 位点不覆盖 producer fence；控制面 fence token 与 generation 不一致；排空超时且未完成隔离。

MySQL 必须部署已启用 1000ms 心跳的正式/影子模板；回退冻结边界包含控制表提交，不能只等最后一条业务记录的 offset。使用 Debezium 3.4 的 `topic.heartbeat.prefix`，元数据交给 BinaryDataConverter 的 JSON delegate，禁止将标准 ByteArrayConverter 当作完整传输配置。配置、心跳及 delegate 变化会使资源摘要失配；不得为排空超时移除位点覆盖检查。配置说明见[部署文档](../../deploy/messaging/README.md#verification)。

Preparing 期间，受该流 producer fence 影响的业务 HTTP 写入返回 503 ProblemDetails，
稳定错误码为 `messaging.delivery.producer_fenced`，带 `Retry-After: 1`。业务与 Outbox
事务一起回滚，响应不暴露内部流名或代次。先查询当前回退操作与所有权，待回退完成或
恢复确认解除保护后，再按原业务的幂等约定重试；不能通过修改 Worker 模式或手工清除 fence 放行。

## 4. 回退（CdcKafka → LegacyPolling）

1. 先确认真实控制面已在 API 注册、持久恢复缺陷已关闭且对应演练实际通过，再使用 `POST /api/v1/messaging/delivery/rollback`（权限 `messaging.delivery.rollback`）。未启用或资源不符仍拒绝；必须先在目标环境完成实际演练。请求必须包含可信认证身份与不超过 512 字符的 reason。
2. 确认两阶段：Preparing → 控制面 fence/排空/位点证明 → 最终事务切回 Legacy。
3. 失败或提交结果未知时，先重读数据库所有权、RollbackGeneration 与持久化操作状态。只有仍由同一 generation 的 CdcKafka Preparing 操作持有时，才补偿恢复控制面；确认 Connector/全部任务实际运行后再解除 producer fence，HTTP resume 成功不足以证明恢复。若数据库已完成 Legacy 回退，应核对成功结果，不能无条件恢复已撤销的 CDC 发布路径；无法判定时保留保护并人工接管。使用 GET /api/v1/messaging/delivery/rollback-operations?eventType=…&schemaVersion=1 发现最近代次，再用 GET /api/v1/messaging/delivery/rollback-operations/{generation} 核对阶段；读权限 messaging.events.read。POST /api/v1/messaging/delivery/rollback-operations/{generation}/recover 使用原回退权限和 reason。phase 数值为 Prepared=0、PauseRequested=1、Paused=2、Recovering=3、Recovered=4、Completed=5。Prepared/PauseRequested/Paused → Recovering → Recovered 表示放弃本次回退并恢复 CdcKafka；Completed 表示已切回 Legacy，重复恢复不发送 resume。任一恢复失败或原环境/非凭据配置/集群身份改变时保留保护，不可另建代次覆盖。
4. 对成功解析并匹配订阅、进入所有权门后收到 ownership revoked 的消息，当前 Worker 保持未提交且不走 Handler / Retry / DLQ。损坏信封或缺失路由在所有权门前即可进入失败处理，因此不能把数据库回退等同于 Consumer 已停止或所有 Kafka 记录都停止路由；仍须独立核对消费者隔离与在途状态。

**回滚条件（回退本身失败时）：** 先核对实际所有权与操作状态；若仍为 Preparing 且无法证明安全恢复，则保持保护，按第 2～3 节受控隔离。数据库已提交 Legacy 时不能假定仍在 Preparing；禁止强行改所有权或清除 fence。

## 5. DLQ 重放

第一批已增加真实 `.dlq` 持久查询投影 `fn_messaging_dead_letter`，Vue Kafka 页签及 `/api/v1/messaging/kafka/dead-letters` 查询该表。历史 `/api/v1/messaging/dead-letters` 仍查询 Inbox failed 行，不作为真实 DLQ 来源。全部人工恢复要求可信操作者与必填理由；验收证据见 [第一批记录](../superpowers/plans/2026-10-24-messaging-operations-batch1.md)，Legacy 恢复见 [Outbox 运维说明](../operations/outbox-worker-topology.md)。

1. 先核对迁移 287、Kafka Worker 的 DLQ 投影组、读 ACL、位点与 Broker 保留期。空列表不能证明 Broker 没有死信。查询需要 `messaging.dead_letters.read`；在详情核对 Consumer、MessageId、来源及 DLQ Topic/Partition/Offset，先修复根因。
2. 使用页面或 `POST /api/v1/messaging/kafka/dead-letters/{id}/replay`（`messaging.dead_letters.replay`），请求体提供不超过 512 字符的 `reason`。仅当前 CdcKafka 所有权、信封完整且订阅仍匹配时允许执行，经保留信封与原订阅进入 Inbox；领域审计和业务/Inbox 事务共同提交。重复执行返回 `already_processed`，不能凭 HTTP 成功省略业务对账。
3. 损坏信封按 Broker 位置定位，页面保留元数据但拒绝重放；原始字节只保留在受控数据库快照中，不经详情接口返回。禁止临时改业务 Consumer 自动订阅 DLQ。恢复不删除 Broker 记录或数据库快照，也不重置历史尝试次数。
4. 观察 `dead_letter.published` 下降不得作为成功标准；以业务对账与 Inbox 结果为准。
5. 范围重放应返回稳定 OperationId、可查询进度与部分完成/结果未知状态；这是待实现要求。当前 Requested/终态审计不能保证进程中断可恢复，也不能宣称跨消息消费与最终审计原子提交。

**停止条件：** Payload 损坏、同 ID 不同 Hash、目标所有权不是预期 Owner。

## 6. 对账（Reconcile）

1. 对试点流执行 Identity 机构单元投影对账：`reconcile_dry_run` → 审查 → `reconcile_apply`。
2. 比对 Outbox / Kafka / Inbox / 投影版本；缺口写入事件单，禁止手工改 Inbox 主键。

**停止条件：** dry_run 显示不可解释缺口或跨租户差异。

## 7. 告警与平台占位指标

下列指标可由应用 `KafkaMessagingTelemetry.UpdateCdcPlatformHealth` / `UpdateConnectorHealth` 或外部 exporter 填充；缺省开发值不得当作生产健康证明：

- SQL Server Capture Job 停止 → `FullNetMessagingSqlServerCdcCaptureJobStopped`
- MySQL Binlog 保留不足 → `FullNetMessagingMySqlBinlogRetentionLow`
- Connector Offset 不可恢复 → `FullNetMessagingConnectorOffsetUnrecoverable`
- Kafka lag 近保留 → `FullNetMessagingKafkaLagNearRetention`

规则文件：[`deploy/observability/prometheus-rules.yaml`](../../deploy/observability/prometheus-rules.yaml)。


## 持久范围重放操作

范围重放用于已登记 CDC 流，部署默认关闭。Vue「消息运维 → 重放操作」先创建记录，再执行/继续，避免长请求失败后丢失操作定位。页面入口、记录读取、范围重放分别需要 `messaging.operations.read`、`messaging.events.read`、`messaging.kafka.range_replay`；服务端独立校验 Host 和精确接口权限。

1. 固定 Topic/业务消费者、分区、起止位点（结束包含）及每次预算，填写理由后创建。创建失败重试必须复用原请求键和正文；未知结果期间不能更改正文制造另一个操作。
2. 记录 201 返回的 `operationId`，查询确认 `prepared`，核对原范围后执行。每次执行仍受部署消息数和时间上限约束，正式 Group 位点不改变。
3. `partial` 表示固定范围尚未处理完；核对拒绝尝试与检查点后明确继续。`result_unknown` 或 `hasUnconfirmedMessage=true` 时，业务可能已经提交，使用同一操作经 Inbox 幂等确认，不新建替代操作绕过检查点。
4. 取消使用持久 cancel 接口，HTTP 请求中止不等于服务端取消完成。`cancel_requested` 仍可有在途结果；查询直至稳定，已确认的业务副作用不会撤销。
5. `blocked` 时核对原 Cluster/Topic 身份和 Kafka 保留窗口。不能扩大原范围或修改持久 JSON 伪造恢复；涉及数据缺口另行对账。执行锁冲突或租约仍活动时先查询，等待原执行完成或租约失效。

总览中的未知、读取失败、关闭与零计数有不同含义。API 配置开启不证明 Worker、Connector 或 Agent 就绪，当前总览不生成切流就绪证明。页面「验证影子证明」「切换至 CDC」「回退至 Legacy」「取消回退并恢复 CDC」仍使用原有服务端安全门禁；Completed 回退禁止恢复原 Connector。
