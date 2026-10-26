# CDC Kafka 事件交付运维与试点切流

本文记录 Full.NET 事务 Outbox + CDC + Kafka 试点流的运维边界、切流/回退流程与生产门禁。权威架构决策见 [`ADR-0006`](../architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)。

> 2026-10-25：MySQL Binlog 与独享 SQL Server Agent 的完整真实切流/回退场景均已通过（2 通过、0 失败、0 跳过）。真实业务 HTTP 经 Legacy 采集、影子证明、HTTP 切流、Debezium/Kafka 与 Inbox 交付，再验证暂停期间业务回滚、最终审计失败后的 Connector 恢复和成功回退。实际命令、修复及边界见[完整链路记录](../superpowers/plans/2026-10-25-messaging-real-cutover-rollback.md)。生产 Worker 组件在同一测试进程中运行；多进程、断网、Soak、容量和独立复核未验证，正式切流默认关闭。

## 候选试点事件流（Build-verified / Pilot，默认不切流）

> [历史试点记录](../verification/cdc-kafka-pilot-2026-08-08.md) 的 MySQL E2E 证据范围保持历史事实；2026-10-25 已用当前消息运维实现补齐上述双库真实链路，不能据此提升生产或容量认证。正式部署仍保持 `Messaging:DeliveryCutover:Enabled=false`。

| 字段 | 值 |
| --- | --- |
| 消息类型 | `fullnet.organization.unit.changed` |
| Schema | `1` |
| Topic 机器码 | `organization.unit-changed.v1` |
| 目录默认所有权 | `LegacyPolling` |
| 候选生产者 | Organization 模块 `TenantUnitManagementService`（经 `DapperRoutedOutboxWriter` 按 effective ownership 路由；`CdcKafka` 时写 append-only Outbox + metadata） |
| 候选消费者 | Identity 模块 `OrganizationUnitChangedIntegrationEventHandler`；`OrganizationUnitChangedKafkaSubscription` 经 `AddBackgroundServicesCore` 同时供 API 与 Worker 注册，只有 Worker HybridKafka 启动常驻 Consumer，API 可供受控重放解析订阅；注册本身不表示正式切流 |
| 遗留重放 | Identity 机构单元投影对账 API（`reconcile_dry_run` / `reconcile_apply`） |

选择依据：消费方通过投影版本比较收敛重复与乱序，且存在 Legacy 对账路径。该流更新 Identity 的机构投影，不能把“可重建投影”解释为没有身份或数据范围影响；验收仍须覆盖租户边界与业务对账。

## 持久化所有权记录

表 `fn_messaging_stream_ownership`（迁移 `094_MessagingStreamOwnership.sql` 创建，`095_MessagingStreamOwnershipConvergence.sql` 收敛约束和试点基线）保存：

- `CurrentOwner` / `PreviousOwner`
- `CutoffEventId` / `CutoffOccurredAtUtc`（Legacy 排空后的切流边界）
- `RollbackBoundaryEventId` / `RollbackOccurredAtUtc`（回退边界）
- `Reason`、`CdcSourcePositionJson`（可选 CDC 位点）

运行时有效所有权 = 持久化记录（若存在）否则 Topic 目录默认值。

## 运维 API

| 操作 | 路径 | 权限 |
| --- | --- | --- |
| 查询影子采集 | `GET /api/v1/messaging/delivery/shadow-captures/current?eventType=…&schemaVersion=1` | `messaging.events.read` |
| 开始影子采集 | `POST /api/v1/messaging/delivery/shadow-captures/` | `messaging.delivery.cutover` |
| 关闭指定采集代次 | `POST /api/v1/messaging/delivery/shadow-captures/{generation}/close` | `messaging.delivery.cutover` |
| 查询交付状态 | `GET /api/v1/messaging/delivery-status` | `messaging.events.read` |
| 切流到 CDC Kafka | `POST /api/v1/messaging/delivery/cutover` | `messaging.delivery.cutover` |
| 建立切流证明 | `POST /api/v1/messaging/delivery/cutover-evidence` | `messaging.delivery.cutover` |
| 查询最近回退 | `GET /api/v1/messaging/delivery/rollback-operations?eventType=…&schemaVersion=1` | `messaging.events.read` |
| 查询指定回退 | `GET /api/v1/messaging/delivery/rollback-operations/{generation}` | `messaging.events.read` |
| 恢复未完成回退 | `POST /api/v1/messaging/delivery/rollback-operations/{generation}/recover` | `messaging.delivery.rollback` |
| 回退到 Legacy | `POST /api/v1/messaging/delivery/rollback` | `messaging.delivery.rollback` |

切流服务当前已检查的条件：

1. `Messaging:DeliveryCutover:Enabled=true` 已由运维显式配置；默认值为 `false`。
2. 目标流在 Topic 目录和 `fn_messaging_stream_ownership` 中注册，且当前有效所有权为 `LegacyPolling`。
3. 目标流 Legacy Outbox 已排空（无 pending、due retry、active lease、dead letter）；其他 Legacy 流的积压不阻塞本流切换。

4. 存在未过期的持久影子证明；实际 Connector、Broker 集群、Topic 身份与全部目录订阅实例仍匹配，源数据摘要没有改变。
5. 当前专用 Connector 协议只接受一个活动 CdcKafka 试点流；其他持久或目录默认 CdcKafka 流均阻断切换，不能隐式影响共享发布者。

证明接口只接收 eventType、schemaVersion、reason；只读取独立影子表中**当前已关闭且无缺口的一代**。实际行数/字节必须等于采集控制记录，样本 1～10000 条、载荷不超过 32 MiB、最新事件在 5 分钟内。比较器从源最早发生时间定位专用影子 Topic 的有界固定窗口（最多 50000 条 / 64 MiB），核对 `capture_generation`、完整信封和源集合；完全相同的至少一次副本只计一次，缺失、额外或变更的同代样本、缺少代次、拓扑或保留窗口变化均拒绝。格式合法的其他代次不属于当前源集。资源摘要同时绑定正式及影子 Connector 配置、环境、真实 ClusterId/TopicId 与全部实际 Worker 成员；不能把健康但路由/载荷配置错误的 Connector 当作就绪。证明 5 分钟后过期，切流事务在流门内重新检查源摘要和有效期。

`Messaging:ShadowCapture` 默认关闭。所有业务生产者实例必须使用同一流和预算策略，手动开始当前代次后，匹配流的真实 Legacy 写入在**同一业务事务**复用 ID、序列化字节、租户和发生时间，写入 `fn_messaging_shadow_event`；正式 `fn_messaging_outbox_event` 不产生副本。默认关闭/不匹配路径不增加 SQL 或序列化；匹配路径增加一条有界预留/插入批次。缺失 metadata 会阻断本代，数量/字节/期限到达后 Legacy 继续。开始/关闭与审计同事务，先等待持久 Legacy 所有权屏障；缺行不能偷偷补行。新代次需 `ExpectedGeneration`，幂等请求键绑定完整正文与可信操作者；只清除上一代诊断样本并使旧证明失效。完整配置与特权脚本见 [部署说明](../../deploy/messaging/README.md#bounded-real-legacy-capture)。本地真实 Debezium/Agent 验收范围见文首记录；其他目标环境仍须独立核对资源与演练结果。

状态接口按流有界聚合 Legacy 积压、Kafka DLQ 投影、保留正式源数量、所有者、订阅及阻断原因。读取失败/截断保持未知，不伪装零值；保留源数量不是 Broker lag，本地配置不表示目标运行健康。总览不返回 ready，切流仍由证明服务实际核验。后台页入口 `messaging.operations.read` 与区域读取/各写操作权限分离。

生产者写 Outbox、Kafka Inbox Handler 与所有权切换使用同一数据库流级事务门：切流等待在途生产者，回退等待在途 Consumer。对于成功解析并匹配订阅、被所有权门拒绝的消息，Worker 保持未提交且不走业务 Handler、Retry 或 DLQ；信封解析或路由失败发生在该门之前，不能把此保证外推为所有 Broker 记录均停止路由，实际消费者隔离必须另外证明。

回退服务采用持久化 generation 的两阶段数据库协议。第一事务取得流级独占锁，等待已取得共享锁的生产者提交，然后写入 `RollbackState=Preparing`、`RollbackGeneration` 与准备时间并提交；此后新生产者在写任何 Outbox 前失败，Kafka Consumer 仍可排空。控制面准备在数据库事务外执行，最终事务只接受同一 generation、1 分钟内且源位点覆盖 producer fence 的证明，再切回 Legacy。

**持久恢复与装配：**迁移 291 保存 generation、资源身份、操作者/理由、阶段和不可撤销的暂停意图，迁移 292 保存切流证明。API 与 Worker 共用 AddFullNetKafkaDeliveryControl；API 不启动常驻 Consumer。默认读取器仍失败关闭，只有 Kafka 与 KafkaConnectRollback 配置有效且显式启用时才装配真实适配器。

回退全过程持有数据库会话锁；外部 HTTP/Broker 调用在业务事务外。Prepared → PauseRequested → Paused 后才能最终提交 Completed 与 Legacy 所有权。暂停前先等待 CDC 覆盖冻结边界；暂停后确认全部任务 PAUSED，并连续两轮确认目录内全部消费组及所有 Retry Topic 的完整分区提交位点为零 lag，未知或超前位点均拒绝。最终事务的流级门等待在途 Consumer。共享 Connector、其他活动流或身份无法证明时拒绝。

失败恢复用新数据库作用域重读结果。Completed 绝不 resume；同一 CdcKafka Preparing generation 才进入 Recovering，恢复原持久资源并确认全部任务 RUNNING 后，才同事务清除 fence、写 Recovered 与审计。绑定资源但从未发出暂停意图时只验证原链路健康，不主动 resume。失败、取消、配置/环境/集群改变或结果未知均保留保护；GET 可定位代次，recover 可跨实例重试，重复终态请求不重复恢复 Connector。返回状态不暴露内部资源快照或凭据。

SQL Server producer fence 使用 CT 中指定冻结流的完整插入集及其最大提交 LSN，不能用全库旧 max LSN 替代。若 CT 保留期已清除仍留在 Outbox 的历史行，无法建立完整证明，回退失败关闭；须按授权恢复/对账处置，禁止强改所有权或清 fence。

上述 API 只是控制面实现，不代表当前已满足生产切流条件。MySQL E2E 的历史通过不覆盖当前工作区；SQL Server 需具备 Agent/CDC 的可用环境及实际通过记录。相关恢复、Soak、lag、N+1/DR 门禁完成前必须保持开关关闭。

## Worker 模式

| 模式 | Outbox 轮询 | 正式 Kafka Consumer |
| --- | --- | --- |
| `LegacyPolling`（默认） | 是 | 否 |
| `ShadowCdc` | 是（影子比对） | 否 |
| `HybridKafka` | 是；只处理仍由 Legacy/Shadow 拥有的流 | 是；只允许 `CdcKafka` 所有权流进入 Inbox |

生产默认保持 `LegacyPolling` 与 `DeliveryCutover:Enabled=false`。MySQL 试点流保留历史 `Build-verified / Pilot` 证据；生产切流前仍需相应代码状态下的双库证据、Soak 与运维门禁。

## 生产门禁（未验证项）

下列项可在本地隔离环境实际通过后验收，不再要求专用生产等价环境；未测规模继续标记 **Capacity-not-verified**，记录环境和范围：

- 双库 CDC 影子 Soak 与 lag SLO
- Kafka N+1 与 retention/recovery 演练
- 试点流生产切流后的端到端 lag 与重复消费审计

Capability 状态见 [`capability-status.md`](../roadmap/capability-status.md)。

逐步切流 / 停止 / Fence / 排空 / 回退 / DLQ 重放 / 对账与停止条件见 [`cdc-kafka-cutover-rollback.md`](../runbooks/cdc-kafka-cutover-rollback.md)。可观测契约见 [`messaging-cdc-observability-20260820.md`](../verification/messaging-cdc-observability-20260820.md)。


### Worker 模式与运维操作契约（第三批）

运行时及 Helm 统一使用 `LegacyPolling / ShadowCdc / HybridKafka`，不再接受 Worker 模式旧字符串 `CdcKafka`；`EventDeliveryOwner.CdcKafka` 仍表示按流正式所有权。Chart 的 Schema、Kafka 开关、Secret 条件与 CI values 使用同一模式名。部署前按目标模式重新渲染 Chart，不把改 Worker 模式当作所有权切换。

范围重放改用持久操作记录，提供创建、查询、执行/继续与取消，详情见 [拓扑与操作接口](messaging-runtime-topology.md#持久范围重放与分区检查点) 和 [Runbook](../runbooks/cdc-kafka-cutover-rollback.md#持久范围重放操作)。每次执行的计数和固定分区位点可恢复；未知结果经 Inbox 确认，不以 HTTP 成功冒充全范围完成。API 与 Worker 仍不自动播种影子载荷；受控影子采集必须和正式发布目的分离，现有样本验证不能替代自动采集流程。

## 持久范围重放与浏览器恢复

迁移 293 的操作日志记录 UUID v7、可信操作者、创建请求键、理由、真实 Cluster/Topic 身份、固定分区窗口、检查点、执行意图、租约与状态。`POST /api/v1/messaging/kafka/replay-operations/` 先创建并返回 201，再调用 `/{operationId}/execute`。查询列表/单项要求 `messaging.events.read`；创建/执行/取消要求 `messaging.kafka.range-replay`。界面始终保留服务端 operationId；取消 HTTP 请求不代表业务操作已取消，`/{operationId}/cancel` 只记录取消意图，不能撤销已提交副作用。

`partial`、`result_unknown` 与 `cancel_requested` 不能当作完成。Inbox 提交后检查点失败时，恢复同一操作，通过原订阅幂等确认未确认消息，再推进位点；绝不跳过未知副作用。恢复拒绝真实资源/保留窗口变化，不延长原有高水位、不改变正式消费组位点。时间上界为排他上界，消息上限跨所选分区合计。原同步范围接口也落入同一持久协议，不再创建无可查询的匿名执行。

第三批实施与实际验收范围见 [记录](../superpowers/plans/2026-10-24-messaging-operations-batch3.md)。本地 Broker 与故障模型不能替代真实 Connector/Agent、多进程断网或容量验证。

2026-10-25 的[受控影子 CDC 验收](../superpowers/plans/2026-10-25-messaging-real-cdc-validation.md)仅证明影子观察范围；后续[完整链路验收](../superpowers/plans/2026-10-25-messaging-real-cutover-rollback.md)已通过两库真实业务 HTTP、正式 CDC/Inbox、失败补偿及成功回退。四份模板统一 BinaryDataConverter + JSON delegate，MySQL 以 1000ms 心跳推进静默期已读位点，使用 `topic.heartbeat.prefix`；SQL Server 正式及影子路由均拒绝将 ZonedTimestamp 当作整数事件时间。配置参与严格校验和资源摘要，完整性与源位点门禁不放宽。多进程和容量仍未验证，采集与正式切流默认关闭。
