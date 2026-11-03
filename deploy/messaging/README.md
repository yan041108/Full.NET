# Full.NET Messaging CDC Shadow (Development / Test Only)

Kafka 4.1.2 and Debezium Connect overlays for **local development and integration testing only**. The application Helm chart (`deploy/helm/fullnet`) does **not** install Kafka, Debezium, or any CDC infrastructure.

> 2026-10-30 本机补充：Native双Worker在默认预取1及显式预取8/低水位0/单槽1、PerMessage下，每库60分钟/3600条长稳均双库2/2；正式/首个重试Topic真实自动TTL各双库2/2，过期后保留原资源与producer fence、拒绝resume。预取8短时5/10档、最终单帧10档复验和默认1的10档失败均保留独立输入。生产容量未验证，不改变Worker默认LegacyPolling、正式切流关闭或部署保留配置。下文历史故障证据保持原范围，见[最新记录](../../docs/verification/2026-10-28-messaging-reliability-continuation.md#2026-10-30预取8长稳与自动ttl续办)。

## CDC runtime monitoring

Worker 的 `Messaging:CdcMonitoring` 默认 `Enabled=false`；显式开启后通过 Full.NET 数据执行器读取当前数据库 CDC 与 producer fence，按已配置的专用控制面映射观察 Connect 任务与正式消费组。Helm 对应 `worker.messaging.cdcMonitoring`，默认轮询间隔 30 秒、单轮期限 10 秒、样本有效期 120 秒；最大年龄须覆盖轮询与期限。采集串行，不执行启停、迁移或所有权修改；标签只使用固定组件和资源机器码。

控制面映射允许1～64条流；每轮共用期限并轮转起点，逐流校验真实源与路由。单流资源码保持 `pilot`，多流使用目录Topic机器码，不暴露随机物理Connector名。一条流失败仍保留其它流的独立观测；共享消费组的消息积压只在所有映射流均可读时汇总，保留比只有全部已确认时取最大值，缺失或失败即撤下该组汇总。单Topic的只读排空探测不会覆盖消费组汇总。

多流时间保留观测使用 `RetentionTimeTopics` / Helm `retentionTimeTopics`，按目录Topic机器码映射各自不可变Topic ID；与单流 `RetentionTimeTopicId` 互斥，未登记流只观测消息数。两种配置均默认空，仍须满足下述自创建起LogAppendTime、保留策略与外部时钟保证，不能根据健康消息数推断健康时间保留比。2026-10-31已在新Native API/Worker的Workflow组合中完成真实双库2/2、零跳过：OTLP采集四流健康和共享组映射正式流汇总，持有真实控制会话锁时暂停一个Connector，只有对应流触发现行两分钟生产告警，恢复后解除。此处不证明多流迟到REST自动纠偏；后续实现已按持久激活基线允许独占多流纠偏，并保留缺失基线/资源复用时的shared_connector失败关闭及逐流失败隔离，40项定向单测及当时源码闭包的双库Native纠偏2/2、零跳过通过，覆盖双API失锁后的迟到pause/resume及一流失败不饿死另一流。范围、失败及原始输入见[Workflow交接记录](../../docs/superpowers/plans/2026-10-28-workflow-cross-machine-handoff.md)。

SQL Server 2022 需允许只读查询 `sys.dm_server_services`（`VIEW SERVER SECURITY STATE`）和 `msdb.dbo.cdc_jobs/sysjobactivity/syssessions` 的必要列；建议平台通过签名授权或最小监控身份提供，不给业务账号授予 sysadmin。权限规则以 [Microsoft 文档](https://learn.microsoft.com/sql/relational-databases/system-dynamic-management-objects/sys-dm-server-services-transact-sql?view=sql-server-ver16) 为准。MySQL 读取实际 `log_bin` 与 `binlog_expire_logs_seconds`；0 表示关闭自动过期，不等于保留不足。失败或权限不足报告 error/unknown，不回填健康默认值。

`fullnet.messaging.observation.status` 区分 healthy/unhealthy/unknown/error/disabled/stale；采集失败撤下旧数值，原始观测时间与最大年龄使 Prometheus 能识别 Collector 中缓存的旧 OTLP 样本。部署需完整传递 `Full.NET.Messaging` Meter，并把相应指标出口登记为 `job=fullnet-messaging-cdc`，匹配目标不可用告警；不能只验证规则语法。

源窗口采集已接入，2026-10-28 双库真实清理与受限权限专项 2/2 通过：核对当前数据库名后，SQL Server 对照固定 Outbox capture instance 的 min/max LSN，并以同库提交时间差测量已捕获源高水位的积压；min LSN 为零可能是无权限，必须 unknown。MySQL 使用真实 `SHOW BINARY LOGS` 检查文件与坐标，需要 `REPLICATION CLIENT`；权限依据 [MySQL 8.0 文档](https://dev.mysql.com/doc/refman/8.0/en/show-binary-logs.html)，SQL Server 边界依据 [SQL Server 2022 文档](https://learn.microsoft.com/sql/relational-databases/system-functions/sys-fn-cdc-get-min-lsn-transact-sql?view=sql-server-ver16)。加密 Binlog 的物理文件头和逻辑坐标关系暂不猜测，返回 unknown；异常规模（超过 4096 个文件）也不提供健康结论。查询沿 Full.NET 执行边界共享单轮超时预算。另一个真实 Prometheus 源窗口告警专项也已 2/2 通过：原阈值 firing、新隔离诊断基线使观测恢复，旧 Connector 保持暂停；不代表原业务数据恢复。

恢复控制面也使用源窗口读取：API 必须具有对应的最小只读权限。取消回退并恢复 CDC 时，在发送 resume 前及确认任务 RUNNING 后，均核对原 Connector 的已提交源坐标；丢失、权限不足或未知均拒绝解除 producer fence。应先恢复读取权限或处理真实日志缺口，不能通过重新初始化 Connector 把新基线当作原数据已恢复。

以上只证明配置源库在采集时的日志坐标窗口，不代替业务投影对账、producer fence、幂等和切流/恢复门禁。可选时间观测按以下条件提供，证据不足仍为 unknown；实际验收见[连续计划](../../docs/superpowers/plans/2026-10-28-messaging-reliability-continuation.md)。

MySQL 时间 lag 使用 Connect 持久化 offset 的 `gtids`（已完成事务检查点）与同一服务器的 `@@GLOBAL.gtid_executed`：仅支持 GTID=ON、开启 Binlog、`binlog_order_commits=ON`、本机单 UUID 从 1 开始的连续前缀。完整覆盖高水位时为零，不随静默时间增长；未覆盖时只比较 Performance Schema 中两个确切已提交 GTID 的 `TIMER_END`，不是墙钟年龄或业务消息延迟。计时开启、`events_transactions_history_long` 消费器开启、历史环容量 1～10000 且服务器 uptime 小于 18446743 秒才可读时间差；缺历史、关闭计时、多源/有缺口集合、权限不足均不填健康数值。运维按需显式开启并最小授权读取相关 Performance Schema 表，应用不修改这些全局配置。此值是服务器复制检查点的事务粒度保守观测，可能包括未订阅库的事务，不代表业务投影已经落后相同时间。计时失败不会改变独立的文件坐标恢复性。字段语义见 [MySQL 8.0 事务事件](https://dev.mysql.com/doc/refman/8.0/en/performance-schema-events-transactions-current-table.html)；检查点取值见 [Debezium 3.4 BinlogOffsetContext](https://github.com/debezium/debezium/blob/3.4/debezium-connector-binlog/src/main/java/io/debezium/connector/binlog/BinlogOffsetContext.java)。

Kafka 时间保留比默认不启用：`RetentionTimeTopicId` / Helm `retentionTimeTopicId` 为空。运维只有确认专用 Topic **自创建起**使用 `message.timestamp.type=LogAppendTime` 后才填写其真实不可变 Topic ID；不得把已有 CreateTime Topic 原地改配置后声明符合条件。采样验证 ID、`cleanup.policy=delete`、有限正 `retention.ms`、`retention.bytes=-1`，并前后复查配置/ID/低水位。原生 Admin 异步时间索引在至多 32 分区、30 天跨度和 22 轮二分内定位截至提交位置的时间戳前缀最大值，不假设 Broker 墙钟永不回拨，也不依赖最大时间戳落在哪个批次。部署必须保证每个 Broker 和采集器**各自相对真实 UTC**的绝对误差不超过 `MaximumBrokerClockSkewSeconds` / Helm `maximumBrokerClockSkewSeconds`（默认 5，允许 0～30 秒）；历史回拨及当前节点差异合计加入四倍预算，得到下一未读位置相对时间保留窗的保守风险上界。超出两倍预算的未来时间戳拒绝采样；只读索引不能证明所有节点都满足外部时钟保证。控制记录/offset 缺口可能进一步扩大保守程度，短保留期尤其明显；此值不是实际删除倒计时。Topic 重建、未知提交、已越低水位、混合/压缩/按字节/无限保留策略、缺权限、取消或超时均不给健康比例；可选时间读取错误保留独立消息数观测并标记 error。SDK 单个时间请求上限 2 秒，采样截止后等待在途请求结束再释放，不宣称硬截止。配置依据 [Kafka 4.1 Topic 配置](https://kafka.apache.org/41/configuration/topic-configs/)。

## Immutable CDC delivery baseline

正式和全部重试业务 Topic 必须**自创建起保持纯 `cleanup.policy=delete`**。切流基线、暂停前、排空和恢复共用的拓扑入口，在窗口读取前后批量查询实际有效策略（含 Broker 继承值）；compact、compact+delete、未知或权限不足均拒绝。控制账号须具有这些精确 Topic 的只读 `DESCRIBE_CONFIGS` 权限。依据 [Kafka 4.1 cleanup.policy](https://kafka.apache.org/41/configuration/topic-configs/#cleanup.policy)，compaction 可能删除同 key 的中间事件而不推进 Low，不能用 ID/水位/组提交值替代这一准入条件；已发生 compaction 的历史不能通过改回 delete 来认证，应独立核对源事件/Inbox，并使用新的专用业务 Topic 重建证据。此固定协议不新增可切换的压缩策略，保存的原资源基线只允许在纯 delete 条件下建立。禁止运维绕过正式组提交规则。Connect 自身 config/offset 等内部 Topic 正常使用 compact，不适用此业务 Topic 规则。DLQ 也应按逐事件保留要求配置 delete；不得把它直接加入正式组排空清单冒充正式组订阅了 DLQ。

升级控制协议前，先冻结 Messaging 变更入口，在原版本完成或恢复所有活动代次，确认旧控制请求不再在途；已有试点先按原协议回退 Legacy。然后由 Migrator 完成双库 `342_MessagingDeliveryTopology.sql`，更新全部 API/Worker 后再允许新切流，禁止混用不识别激活身份的旧控制执行者。无法确认旧请求或源事件完整性时停止升级，不重新认领同名资源。切流必须在真实资源仍匹配已批准影子证明时读取稳定拓扑，并在所有权事务内保存到 `fn_messaging_stream_ownership.CdcKafkaBindingJson`；源日志坐标仍独立保存，不要求尚无 CDC 业务的 Connector 已产生持久 offset。基线绑定每次切流生成的 ActivationId、原 Connect URI/环境、Kafka 集群、原 Connector 配置摘要、正式及全部重试 Topic ID/分区/原始窗口、消费组和重试阶段。迁移只加可空列，不以当前同名资源回填旧 CDC 记录；缺基线的旧试点须独立对账与人工处置。

自动回退和恢复只支持**自切流以来原保留起点 Low 未变且 High 未倒退**的窗口。正式/重试 Topic 重建、分区变化、裁剪、消费组或重试阶段变化均拒绝；即使当前 offset 已追到 High，也不能冒充被清理区间已 Inbox 确认。回退开始前已发生的资源变动同样对照原切流基线，不能重新认领；已知失配留下原资源记录，使自动补偿也保留 producer fence。恢复只选择原快照中的 Connector，重新核对原配置、双方 ClusterId、实际源日志和完整 Topic 窗口。发生保留清理后，应按源事件、Inbox/DLQ 做独立对账和恢复，而非直接改基线或清除 fence。控制面在每次外部变更和终态提交前验证原数据库连接仍持锁；失锁执行者不得重新获取锁延续旧动作或继续补偿。HybridKafka Worker 在控制面启用时重申当前持久目标，处理已经在途的有限迟到 REST；本轮JIT双库故障2/2、JIT双Worker各60分钟长稳2/2，以及Linux Native API/Worker完整Messaging故障链双库2/2均通过；不把这些本机检查称为远端 fencing、Native长稳或生产容量，范围见[续办记录](../../docs/verification/2026-10-28-messaging-reliability-continuation.md#2026-10-29分片发现与完整-native-控制链续办)。

## Shadow scope

- Capture committed `INSERT` rows from the isolated diagnostic `fn_messaging_shadow_event` via SQL Server CDC or MySQL ROW Binlog.
- Route events to one explicitly configured `${FULLNET_SHADOW_TOPIC}` (for example `fullnet.dev.shadow.organization-unit.v1`) using Debezium Outbox Event Router.
- Shadow Topics are for comparison evidence only; **no business Consumer** may subscribe to them.
- Heartbeat, Schema History, and Connect internal topics use `fullnet.dev.shadow.internal.*` and are likewise not business-facing.

## Pinned images (no `:latest`)

| Component | Image |
| --- | --- |
| Kafka broker | `apache/kafka:4.1.2` |
| Debezium Connect | `quay.io/debezium/connect:3.4.3.Final` |

Production must **not** run these public images directly. Production Connect must be built by the trusted platform from fixed Debezium Connector artifacts (or an approved supported distribution), with vulnerability scan, signing, SBOM, and digest pinning evidence on file.

## Quick start (dev)

```bash
export FULLNET_SQLSERVER_HOST=host.docker.internal
export FULLNET_SQLSERVER_PORT=1433
export FULLNET_SQLSERVER_USER=fullnet_cdc_reader
export FULLNET_SQLSERVER_PASSWORD=replace-me
export FULLNET_SQLSERVER_DATABASE=fullnet_dev

export FULLNET_MYSQL_HOST=host.docker.internal
export FULLNET_MYSQL_PORT=3306
export FULLNET_MYSQL_USER=fullnet_cdc_reader
export FULLNET_MYSQL_PASSWORD=replace-me
export FULLNET_MYSQL_DATABASE=fullnet_dev

docker compose -f deploy/messaging/compose.kafka-debezium.yml up -d
```

Set `FULLNET_SHADOW_TOPIC` to the dedicated catalog binding. Expand connector placeholders before registration; Kafka Connect does not implicitly expand shell variables in submitted JSON.

Run `deploy/messaging/sqlserver/enable-shadow-cdc.sql` (privileged DBA operation, **not DbUp**) and `deploy/messaging/mysql/verify-binlog.sql` before registering connectors.

## Secret injection

Compose and connector templates use placeholder environment variables. Inject secrets via shell env, gitignored `.env`, or your platform secret store. Never commit passwords or connection strings.

## SQL Server CDC (privileged operations)

SQL Server CDC is an **explicit privileged DBA operation**:

1. Database-level CDC and SQL Server Agent must be enabled by operations - **not** by DbUp migrations, API startup, or Worker bootstrap.
2. Shadow CDC for `dbo.fn_messaging_shadow_event` uses stable capture instance `fullnet_fn_messaging_shadow_event`. Formal capacity profiles separately capture `fn_messaging_outbox_event`.
3. To disable: run `sqlserver/disable-shadow-cdc.sql` during approved maintenance.

## MySQL Binlog prerequisites

- `log_bin=ON`
- `binlog_format=ROW`
- `binlog_row_image=FULL`

Use `mysql/verify-binlog.sql` to inspect current settings.

MySQL 的 Shadow 与 Capacity 模板将 schema history 限制在配置的目标数据库及
对应的单表：Shadow 为 `fn_messaging_shadow_event`，Capacity 为 `fn_messaging_outbox_event`。两项 `schema.history.internal.store.only.captured.*.ddl`
均显式设为 `true`。`no_data` 仍需读取表结构，不能依赖数据库/表捕获过滤器避免扫描
实例中的其他历史库。此连接器仅负责固定 Outbox；未来若扩大捕获范围，须按
[Debezium schema history 恢复规则](https://debezium.io/documentation/reference/3.4/connectors/mysql.html#mysql-capturing-data-from-tables-not-captured-by-the-initial-snapshot-no-schema-change)
重新准备对应结构历史，不能直接假定旧 history 已包含新增表。

## Verification

All four Outbox templates explicitly use `io.debezium.converters.BinaryDataConverter`,
with `value.converter.delegate.converter.type=org.apache.kafka.connect.json.JsonConverter`
and `value.converter.delegate.converter.type.schemas.enable=false`. Business MemoryPack
bytes pass through unchanged; schema-change and heartbeat metadata use the delegate
on their separate infrastructure topics. The standard Kafka ByteArrayConverter rejects
those STRUCT records and stops the task. Cutover and rollback bind all three settings
to the resource identity. See the [Debezium 3.4 converter contract](https://debezium.io/documentation/reference/3.4/transformations/outbox-event-router.html#_using_apache_avro_as_the_payload_format).

Use the Debezium 3.4 key `topic.heartbeat.prefix`; `heartbeat.topics.prefix` is
rejected. The two MySQL profiles set `heartbeat.interval.ms=1000` so committed
source offsets can advance while the captured Outbox is quiet. Rollback captures
the database binlog boundary after the producer fence and control-table commits;
the last business record alone cannot prove that boundary. Heartbeats stay on
infrastructure topics and use the JSON delegate. The control plane binds the
heartbeat settings, rejects arbitrary SQL heartbeat actions, and retains the
source-position coverage check. See the [MySQL heartbeat options](https://debezium.io/documentation/reference/3.4/connectors/mysql.html#mysql-property-heartbeat-interval-ms).

SQL Server 空闲时 CDC 扫描上界不保证自行前进。先应用 352 双库迁移，再由具备 CDC 管理权限的运维在目标数据库执行 `sqlserver/enable-heartbeat-cdc.sql`，并单独向 Connector 数据库账户授予 `dbo.fn_messaging_cdc_heartbeat` 的 `UpdatedAtUtc` 列 UPDATE 和 `Id` 列 SELECT 权限（固定更新的 WHERE 需要读取 Id）；这些权限仅用于心跳，Connector 读取业务 CDC 所需权限仍须另行配置。两个 SQL Server 模板使用固定单行 `heartbeat.action.query`；该表必须启用独立捕获实例，但不得加入业务 Connector 的 `table.include.list`，心跳变更不进入业务 Topic。控制面只接受该精确 SQL，不接受任意动作。该机制主动产生基础设施捕获事务，因此保证目标是业务表空闲时推进，而不是宣称无任何捕获事务也会凭空生成 LSN。应监控捕获作业、心跳更新权限、单行存在性和 CDC 清理积压；停止任一环节时回退应保持失败关闭。

The cutover and rollback route check rejects Connect `errors.tolerance` other
than `none`, Debezium `event.processing.failure.handling.mode` other than
`fail`, and `skipped.operations` values other than the default truncate-only
setting or `none`. These settings participate in the resource fingerprint:
a source offset must not advance past a skipped Outbox insert or failed event.

```powershell
node --test tests/deployment/messaging-cdc-contract.test.mjs
pnpm test:helm
pnpm test:observability-deploy
```

## Production boundary

- `deploy/helm/fullnet` must not reference `quay.io/debezium/*` or ship Kafka/Debezium as application-owned stateful services.
- Shadow validation must complete before any formal business Consumer is enabled on CDC-delivered topics.

## Bounded real Legacy capture

`Messaging:ShadowCapture` defaults to disabled. Apply the paired 296 migrations before enabling this policy. Configure the same enabled policy on every business-producing API/Worker instance: `EventType`, `SchemaVersion`, `MaximumEvents` (1..10000), `MaximumPayloadBytes` (1..33554432) and `MaximumWindowSeconds` (1..300). A configured single pilot stream and persisted Legacy ownership are prerequisites. A missing ownership row is a blocker, never automatically created by capture endpoints.

Host Messaging → Delivery overview → Shadow capture reads the current generation. Starting and closing require `messaging.delivery.cutover`, trusted Host context and an audit reason. Readers require `messaging.events.read`. Creation retries retain RequestId and ExpectedGeneration; new generations remove only prior diagnostic payloads and invalidate prior evidence. Real `IOutboxWriter` Legacy writes reuse one ID/serialization/tenant/time and commit the diagnostic copy in the same transaction. Missing metadata blocks the generation. Quota/time termination keeps Legacy operating, never writes a formal copy, and cannot establish evidence on its own.

Bind separate `ConnectorName` and `ShadowConnectorName`, `TopicName` and `ShadowTopicName` under `Messaging:KafkaConnectRollback:Streams`. Both real connector configurations and identities participate in cutover proof. Both shadow connectors emit `CaptureGeneration:header:capture_generation` and preserve the exact `occurred_at_utc` header. MySQL uses `OccurredAtUtc` as the record timestamp. SQL Server `datetimeoffset` maps to a string ZonedTimestamp, so its template uses the default Debezium processing timestamp; do not add `table.field.event.timestamp=OccurredAtUtc` or silently remove a production setting inside a test factory. The verifier uses the earliest source time to locate a bounded immutable broker window and then checks every ID, generation, exact business timestamp and payload. Clock skew that excludes samples causes proof rejection. Identical at-least-once copies count once; shadow verification never invokes business handlers.

The [2026-10-25 shadow validation](../../docs/superpowers/plans/2026-10-25-messaging-real-cdc-validation.md) established controlled-capture evidence. The subsequent [full control-chain validation](../../docs/superpowers/plans/2026-10-25-messaging-real-cutover-rollback.md) passed both real MySQL and SQL Server Agent scenarios (2 passed, no failures or skips): business HTTP, durable proof, HTTP cutover, CDC/Kafka/Inbox, genuine pause, audit-failure recovery, successful rollback and Legacy delivery. The formal Topic high-water mark remains unchanged after rollback. Production Worker components run in the test process; multi-process recovery, network partitions, throughput, soak and independent review remain unverified. Capture and formal cutover remain disabled by default.

## 迟到 REST 与控制目标收敛

专用流切换要求 Connect 在 EventRouter 前加载 `net.full.connect.FullNetOutboxStreamFilter`。本地 Compose 从 `connect-filter/Dockerfile` 构建包含插件的镜像；生产 Connect 由平台交付并固定镜像摘要，配置检查不能证明远端插件字节的可信性。仓库中的基础 Connector JSON 是通用采集模板，不能直接作为专用流切换配置：部署时须将 `transforms` 设为 `fullnet_filter,outbox`（或后接既有 `select`），设置 `transforms.fullnet_filter.type` 为上述类名、`event.type` 和 `schema.version` 为该流的精确身份、`source.topic` 为 Debezium 原始表 Topic（SQL Server：`<topic.prefix>.<database>.dbo.<table>`；MySQL：`<topic.prefix>.<database>.<table>`），且不得为过滤器配置 predicate。影子流使用影子表名。未满足配置时切换检查拒绝操作；插件的真实双库部署及不同版本隔离仍需本批物理验收。

Connect REST 不接受数据库 fencing token，客户端取消也不能撤销服务端已接收的 pause/resume。Worker 先在锁外只读探测物理偏差，健康时不争用人工控制锁；发现偏差后获取同一数据库会话锁并完整重验。活动 producer fence 禁止自动纠偏；Recovering 仍须由授权运维恢复。CDC 使用当前激活基线重申 RUNNING，Legacy 仅按所有权行直接关联的 Completed 代次重申 PAUSED。清除 fence 时保留 RollbackGeneration 作为终态关联；是否保护由 RollbackState 判断，后续切流会清除旧关联并生成新 ActivationId，禁止按日志时间推断当前代次。

每次动作核对冻结端点/环境、专用映射、配置指纹、双方 ClusterId、正式/全部重试 Topic 的 ID、分区、原窗口和纯 delete 策略；resume 还须确认原源位点可恢复。缺失新激活字段的旧基线不自动认领。Worker 不写所有者、不清 fence、不伪造用户请求；沿用已提交控制意图，仅记录 EventId 1480（已纠偏）/1481（无法确认，保留保护）。默认每 10 秒尝试观察，受资源查询、服务端异步动作和超时影响，不承诺硬恢复截止；只在最后一个有限迟到动作之后、原资源仍受支持且至少一个 Worker 可用时收敛。

检修或紧急隔离需要持续人工暂停时，先按部署层流程停止相关控制 Worker，或关闭其回退控制配置并重启，确认没有遗留控制操作后再发直接 REST。当前 CDC 所有者的默认物理目标为 RUNNING，单独 pause 不会建立持久维护意图，会被纠偏恢复；消费者隔离及生产入口限制仍按 Runbook 单独执行。

启用回退控制的 Worker 还需具备既有恢复源窗口的只读权限、原 Connector 的状态/配置/offset 读取与 pause/resume 权限，以及正式/重试 Topic 的 DESCRIBE_CONFIGS 和组位点读取权限；不能只给 API 配置这些权限。授权不足时报告稳定原因码并停止纠偏，既有 producer fence 保持不变，不授予业务账号 sysadmin。

观测指标使用 observed_topics 名称，范围仅限已实际采样的正式Topic，不包含未映射订阅及重试Topic；保留窗口告警仅反映配置了身份的已采样Topic，不能视为完整消费组健康。该命名调整及告警规则待本批新鲜验收。
