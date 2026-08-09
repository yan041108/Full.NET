# Full.NET 事务 Outbox、CDC Relay 与 Kafka 事件交付规格

- 状态：已批准
- 日期：2026-08-08
- 批准来源：项目所有者明确要求按建议提前实施，不使用 CAP/MassTransit
- 架构决策：[`ADR-0006`](../../architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)
- 实施计划：[2026-08-08-transactional-outbox-cdc-kafka.md](../plans/2026-08-08-transactional-outbox-cdc-kafka.md)
- 当前实现基线：`c8c539915ebc33f666892f82b575f59aaf599453`
- 能力状态：`Designing`；本文批准开发，不代表功能已经实现或验证

## 1. 目标与非目标

### 1.1 目标

1. 保留业务数据与可靠 Integration Event 的单数据库事务原子性。
2. 用日志型 CDC 取代应用 Worker 对 Outbox 热表的领取、租约、续租和处理状态更新。
3. 提供 Kafka 发布订阅、不同 Consumer Group 扇出、同组竞争、分区顺序和保留重放。
4. 提供 Full.NET 自有 .NET 事件抽象、订阅目录、Inbox 幂等事务、重试、DLQ、受控重放、租户恢复、Trace 和运维指标。
5. SQL Server 与 MySQL 作为正式 Provider 分别完成真实 CDC、迁移、故障和恢复验证。
6. 通过 Shadow Topic 和逐事件流单一所有权完成无双发布切换。

### 1.2 非目标

- 不引入 CAP、MassTransit、MediatR 或 Event Sourcing。
- 不把模块化单体改成微服务，不授权服务网格、分片或运行时多数据库双写。
- 不在请求事务中直接发布 Kafka。
- 不提供端到端 Exactly-Once 声明。
- 不把缓存失效、日志、Trace、Metrics、普通 HTTP Operation Log 或 Audit 迁入 Outbox/Kafka。
- 不自行解析 SQL Server 内部事务日志格式。
- 首期不引入商业 Schema Registry、Kafka Streams、跨地域 MirrorMaker 或每租户 Topic。

## 2. 当前实现与迁移边界

当前链路：

```text
业务事务 -> fn_outbox_message
          -> OutboxProcessor.AcquireAsync
          -> IIntegrationEventHandler
          -> MarkProcessed/MarkFailed/MarkDeadLetter
```

目标链路：

```text
业务事务 -> fn_messaging_outbox_event (append-only)
          -> SQL Server CDC / MySQL ROW Binlog
          -> Debezium Outbox Event Router
          -> Kafka Topic
          -> Full.NET Kafka Consumer
          -> fn_messaging_inbox_message + 本地业务事务 + 下游 Outbox
          -> 手工提交 Kafka Offset
```

旧 `fn_outbox_message`、`IOutboxStore` 和 `OutboxProcessor` 在 F1–F4 保留，用于未迁移事件流和可控回退。不得直接重命名或删除旧表/列；所有切换按事件目录静态声明发布所有者。

## 3. 组件与依赖方向

| 组件 | 职责 | 允许依赖 |
| --- | --- | --- |
| `Full.NET.Messaging.Abstractions` | Envelope、订阅、消费上下文和可靠性抽象 | 仅 BCL 与现有 Abstractions |
| `Full.NET.Messaging.Dapper` | 追加式 Outbox Writer、Inbox Store、双库 SQL | Data Abstractions/Dapper，不依赖 Kafka |
| `Full.NET.Messaging.Kafka` | Kafka Producer/Consumer、配置、健康与遥测 | Messaging Abstractions、Confluent.Kafka |
| `Full.NET.Host.Worker` | 按 Host Profile 装配 Broker Consumer 与旧 Worker | Composition 和 Provider |
| Debezium/Kafka Connect | 数据库 CDC、Outbox 路由和 Source Offset | 外部部署单元，不进入业务程序集 |
| 业务模块 | 产生版本化事件并实现订阅 Handler | 只依赖 Messaging Abstractions/Contracts |

如果实现者证明单独的 `Full.NET.Messaging.Dapper` 项目没有独立消费者或依赖隔离收益，应将其保持为现有 `Full.NET.Data.Dapper` 内聚目录；禁止为了规格名称机械增加 `.csproj`。`Full.NET.Messaging.Kafka` 有真实第三方依赖隔离收益，允许作为可选 Provider 项目。

业务模块和 `Full.NET.Abstractions` 不得引用 `Confluent.Kafka`、Debezium 或 Kafka Connect 类型。Architecture Tests 必须失败关闭这一边界。

## 4. 稳定事件契约

### 4.1 `IntegrationEventEnvelope`

```csharp
public sealed record IntegrationEventEnvelope(
    Guid EventId,
    string MessageType,
    int SchemaVersion,
    string ContentType,
    Guid? TenantId,
    string PartitionKey,
    string? CorrelationId,
    Guid? CausationId,
    string? TraceParent,
    string Producer,
    DateTimeOffset OccurredAtUtc,
    ReadOnlyMemory<byte> Payload);
```

约束：

- `EventId`：应用端 UUID v7，跨 Outbox、Kafka、Inbox、日志和重放保持不变。
- `MessageType`：匹配 `^[a-z][a-z0-9]*(\.[a-z][a-z0-9_]*){3,}$`，继续使用 `{owner}.{module}.{entity}.{event}`。
- `SchemaVersion`：正整数，与 MessagePack 契约版本独立治理。
- `ContentType`：首期仅允许 `application/x-msgpack`。
- `PartitionKey`：非空、最大 256 UTF-8 字节；不得含 Secret、翻译文本或随机值。
- `CorrelationId`：跨用例关联，最大 128 字符；无现有值时使用 `EventId` 规范文本。
- `CausationId`：由上游消息触发时记录其 `EventId`。
- `TraceParent`：W3C Trace Context；不得把完整 Baggage 写入消息。
- `Producer`：稳定模块/宿主机器码，例如 `fullnet.tenancy`，不是 CLR 类型名。
- `Payload`：MessagePack 二进制，不可信反序列化安全策略保持开启。

### 4.2 写入接口

现有 `IOutboxWriter.AddAsync<TEvent>` 需要以向后兼容方式扩展，不让业务模块构造 Broker 类型：

```csharp
public sealed record IntegrationEventMetadata(
    string PartitionKey,
    string Producer,
    string? CorrelationId = null,
    Guid? CausationId = null);

public interface IOutboxWriter
{
    Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        IntegrationEventMetadata metadata,
        CancellationToken cancellationToken = default);
}
```

旧重载在迁移窗口内保留并生成可审计的默认 `PartitionKey`，但所有进入 Kafka 的事件必须显式提供 Metadata；Architecture/Contract 测试阻止 Kafka 目录事件使用旧重载。

## 5. 数据模型

### 5.1 `fn_messaging_outbox_event`

| 列 | SQL Server | MySQL | 约束 |
| --- | --- | --- | --- |
| `Id` | `uniqueidentifier` | `binary(16)` | UUID v7，PK NONCLUSTERED |
| `MessageType` | `nvarchar(256)` | `varchar(256)` | 非空 |
| `SchemaVersion` | `int` | `int` | `> 0` |
| `ContentType` | `varchar(128)` | `varchar(128)` | 非空 |
| `TenantId` | `uniqueidentifier NULL` | `binary(16) NULL` | 可信上下文 |
| `PartitionKey` | `nvarchar(256)` | `varchar(256)` | 非空 |
| `CorrelationId` | `nvarchar(128) NULL` | `varchar(128) NULL` | 可空 |
| `CausationId` | `uniqueidentifier NULL` | `binary(16) NULL` | 可空 |
| `TraceParent` | `varchar(128) NULL` | `varchar(128) NULL` | 可空 |
| `Producer` | `varchar(128)` | `varchar(128)` | 非空 |
| `Payload` | `varbinary(max)` | `longblob` | 非空 |
| `OccurredAtUtc` | `datetimeoffset(7)` | `datetime(6)` | 非空 |

SQL Server 显式聚集索引：`IX_fn_messaging_outbox_event_OccurredAtUtc_Id`；MySQL 使用相同索引名和列序。表是追加式事件日志，不包含 `ProcessedAtUtc`、`Attempts`、`LockId`、`LockedUntilUtc`、`NextAttemptAtUtc` 或业务死信状态。

### 5.2 `fn_messaging_inbox_message`

| 列 | 语义 |
| --- | --- |
| `ConsumerName` | 稳定订阅身份，复合主键第一列 |
| `MessageId` | 原始 `EventId`，复合主键第二列 |
| `MessageType` / `SchemaVersion` | 契约定位 |
| `TenantId` | 消费作用域 |
| `PayloadHash` | SHA-256，用于发现同 ID 不同正文 |
| `Status` | `processing`、`processed`、`failed` |
| `Attempts` | 当前消费者处理尝试数 |
| `ReceivedAtUtc` / `ProcessedAtUtc` | 时间线 |
| `LastErrorCode` / `LastError` | 稳定原因码与脱敏摘要 |

主键/唯一约束固定为 `(ConsumerName, MessageId)`。不同 Consumer Group 可独立消费同一消息；同一消费者重复收到同一 MessageId 时，PayloadHash 不同必须永久失败并进入安全告警，不能覆盖旧记录。

`processing` 不是跨进程长租约队列：Kafka Partition 所有权提供单组消费协调；数据库事务必须短小。进程在事务提交前宕机时 Inbox 写入回滚，消息重投；提交后 Offset 前宕机时 Inbox 命中 `processed` 并安全确认。

Inbox claim 的网络往返预算固定为一次：SQL Server 在单个 batch 内以 `UPDLOCK,HOLDLOCK` 锁定唯一键范围后完成首次插入或 failed 重置；MySQL 在单个 command 内以原子 upsert 完成领取并 `FOR UPDATE` 返回当前行。PayloadHash 不得被 upsert 覆盖；Handler 成功后的 `processed` 更新仍是同一本地事务内的独立语句，不能为了减少往返而移出事务。

## 6. CDC Provider 规格

### 6.1 SQL Server

- Migrator 只创建/调整应用表；启用数据库级 CDC、SQL Server Agent 权限和生产 Job 参数属于显式运维步骤，禁止 API/Worker 自动提升权限。
- 表级 CDC 由受控部署脚本启用，Capture Instance 使用稳定名称；DDL 变更遵循 Expand/Contract，旧 Capture Instance 在新实例追平后才移除。
- Connector 只读取批准 Capture Instance；监控 Capture Job、Cleanup Job、最大 LSN、Connector LSN、变更表保留和事务日志截断风险。
- 初次上线使用受控 `snapshot.mode=no_data` 或等价的仅 Schema 模式，历史 Outbox 回放由独立迁移/重放计划控制，禁止默认全表 Snapshot 产生业务消息。

### 6.2 MySQL

- `log_bin=ON`、ROW Binlog、`binlog_row_image=FULL`，Connector 使用唯一 `server_id` 和最小复制权限。
- Binlog 保留期必须大于最大允许 Connector 中断窗口和恢复时间；接近丢位点时告警。
- 首次上线同样禁止自动把历史 Outbox 当新业务事件；Snapshot 策略与 SQL Server 保持业务语义一致。

### 6.3 Debezium Outbox 路由

- 仅匹配 `fn_messaging_outbox_event` 的创建操作。
- `Id` 映射消息头 `event_id`，`PartitionKey` 映射 Kafka Key。
- `MessageType`、`SchemaVersion`、`ContentType`、`TenantId`、`CorrelationId`、`CausationId`、`TraceParent`、`Producer`、`OccurredAtUtc` 映射稳定 Header；`Payload` 原样作为 Value。
- Heartbeat、Schema History 和事务元数据进入独立内部 Topic，业务 Consumer 不得订阅。

## 7. Kafka Provider 规格

### 7.1 配置

配置节为 `Messaging:Kafka`，环境变量使用双下划线。至少包含：

- `Enabled`
- `BootstrapServers`
- `SecurityProtocol`
- `SaslMechanism`
- `ClientId`
- `ConsumerInstanceId`
- `ConsumerGroupProtocol`
- `BrokerMajorVersion`
- `ClassicPartitionAssignment`
- `CooperativeStickyMigrationCompleted`
- `SessionTimeoutMilliseconds`
- `MaxPollIntervalMilliseconds`
- `HandlerHeartbeatMilliseconds`
- `CompletionPollMilliseconds`
- `DeliveryTimeoutMilliseconds`
- `MessageMaxBytes`
- `ProducerLingerMilliseconds`
- `ProducerBatchSizeBytes`
- `ProducerQueueMaxMessages`
- `ProducerQueueMaxKbytes`
- `ProducerMaxInFlightRequests`
- `RetryStages`

Secret 通过 Secret Provider 提供，配置模型不返回密码。生产 `Enabled=true` 时 TLS、Broker 地址、客户端身份、Topic Catalog 和安全配置缺失必须启动失败；开发环境可以显式关闭 Provider。

`Classic` 协议在存量 Consumer Group 中默认保持 `LegacyRange`，避免 eager 与 Cooperative 客户端在滚动发布期间没有共同 Assignor。切换 `CooperativeSticky` 必须先排空并停止该 Group 的全部旧实例，完成离线切换与回退演练，再设置 `CooperativeStickyMigrationCompleted=true`；禁止在 `maxUnavailable=0` 的普通滚动发布中直接切换。`ConsumerInstanceId` 映射为 `group.instance.id`，与 `ClientId` 分离。Kafka 4.x `Consumer` Protocol 只有在 `BrokerMajorVersion >= 4`、兼容测试和 Rebalance/滚动发布演练通过后才能显式启用，并且不得携带 `partition.assignment.strategy`、`session.timeout.ms` 等 Classic-only 参数。Producer 保持 `acks=all` 与幂等，批量、等待、本地队列和在途请求必须有界。

### 7.2 Topic 目录

Topic 不是由业务输入动态拼接。建立版本化静态目录，记录：Topic、允许 MessageType/SchemaVersion、Partition Count、Replication Factor、`min.insync.replicas`、Retention、最大消息、Producer、Consumer Group、DLQ/Retry 和数据分类。

首次生产建议 `replication.factor=3`、`min.insync.replicas=2`、Producer `acks=all`；开发单节点使用独立环境配置，不得把单节点值发布为生产默认。

### 7.3 消费模型

```csharp
public interface IIntegrationEventSubscription
{
    string ConsumerName { get; }
    string EventType { get; }
    int SchemaVersion { get; }
    IntegrationEventIdempotencyStrategy IdempotencyStrategy { get; }
    Task HandleAsync(
        IntegrationEventContext context,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken);
}
```

现有 `IIntegrationEventHandler` 通过兼容适配器迁移；新 Kafka Consumer 使用 Subscription 身份。启动时验证目录、Handler、Schema、Topic 和 ConsumerName 唯一性。

当前 Build-verified 消费模型为“单 Consumer SDK 命令循环 + 每分区容量 1 的有界通道”；后续性能演进保持同一命令循环，并按实施计划升级为“每分区固定 Key 槽位 + 全局/分区高低水位”：

- Consume、Pause/Resume、Seek、Commit 和 Rebalance 回调只在 Consumer 循环串行执行；Handler 不得持有或调用 Kafka Consumer。
- 收到消息后只暂停该消息所属分区；同一分区在当前消息完成前不再接收下一条，不同已分配分区独立并行。
- Offset 水位按该分区实际交付序列推进，不假设 Kafka Offset 数值无空洞；只有队首连续成功的消息可以推进提交位置。
- 失败消息 Seek 到原 Offset 并只对该分区执行有界退避，禁止提交或恢复时越过失败消息。
- 每次分配具有单调递增代次；Rebalance 撤销会取消该分区通道，旧代次迟到完成不得 Commit 或 Resume 新 Owner 的分区。
- 吞吐并行上限由 Topic 分区数和当前实例获配分区数共同决定；增加 Worker 实例超过分区数不会继续提高同一 Consumer Group 的并行度。
- 同一 `ConsumerName` 的多个 Topic 继续合并到单 Consumer 订阅，避免为每个 Topic 创建连接和独立 Rebalance；禁止合并不同 Consumer Group 的订阅语义。
- Key 槽位使用稳定 `XxHash64` 映射，同 Key 串行、不同槽并行；Offset 仍只推进连续成功水位。该能力已完成构建级实现与局部故障测试，但在生产等价负载、Rebalance 与关闭排空矩阵完成前仍为 `Capacity-not-verified`。
- 全局/分区高低水位与 Offset 周期提交模式必须显式配置且可回退；默认仍为分区容量 `1/0`、单 Key 槽和 `PerMessage`。Production/Staging 的周期模式必须通过 `PeriodicOffsetCommitVerified` 门禁。详细配置与回退见[Kafka Consumer Buffer、Key 槽与 Offset 提交](../../operations/kafka-consumer-buffer-and-offset-commit.md)。Inbox 批量优化只能做只读预检或事务内批量，不能在 Handler 业务事务之前把未知消息标记为已领取。

Consumer 本地事务的事件流所有权检查优先使用一次锁定 Fence 查询同时返回 `CurrentOwner`，不得在热路径重复执行 Gate 和 Owner 数据库查询。兼容扩展可回退旧接口，但 Full.NET Dapper 正式路径必须使用单查询 Fence；未经版本/Fence 设计不得用普通内存缓存替代该数据库边界。

## 8. 失败分类

| 类别 | 示例 | 动作 |
| --- | --- | --- |
| `messaging.contract.*` | 未知版本、ContentType、Payload 损坏、同 ID 不同 Hash | 直接 DLQ |
| `messaging.security.*` | 非法租户、未批准 Topic/订阅 | 直接 DLQ + 安全告警 |
| `messaging.transient.*` | 数据库超时、短时网络/Broker 故障 | 分级 Retry，有上限 |
| `messaging.business.*` | 确定性业务前置条件失败 | 不自动无限重试；由补偿或人工处置 |
| `messaging.capacity.*` | 消息过大、Inbox/磁盘/保留逼近上限 | 失败关闭、告警并停止切流 |

Retry 默认三阶段：`5s`、`1m`、`15m`，每阶段一次；仍失败进入 DLQ。不同事件 SLA 可以在 Topic Catalog 静态覆盖，但禁止运行时按瞬时负载改变可靠性分类。

## 9. 可观测契约

Meter 使用 `Full.NET.Messaging`，指标前缀 `fullnet.messaging.*`。标签只允许 `provider`、`database_provider`、`topic_code`、`consumer_code`、`message_type_code`、`result` 和稳定原因码；禁止 MessageId、TenantId、原始 Topic、异常文本或 Payload 作为标签。

Trace Span 至少覆盖 `outbox.append`、`cdc.capture`（从 Connector 指标关联）、`kafka.consume`、`inbox.transaction` 和 `kafka.commit`；消息携带 `TraceParent`，Consumer 创建新的消费 Span 并保留 Link/Parent 语义。

性能演进还必须提供有界状态源：分区/全局 Buffer 深度、高低水位 Pause/Resume、在途 Handler、已分配/暂停分区、Consumer Lag、最老消息年龄、周期提交待刷新的连续水位、DLQ/重放结果和事件流所有权 Fence。Handler 源生成只替换反射或运行时扫描，不改变 DI 生命周期、订阅唯一性或事务边界；当前三键注册表、重复/非法元数据诊断、Catalog 显式回退和有界低基数状态源已通过构建与架构门禁，但生产采集、告警和容量证据仍须按 `Capacity-not-verified` 管理。

一次性范围重放使用独立权限、必填审计原因、唯一临时 GroupId 与显式 `Assign`；执行前先持久化 `requested` 审计，再固定分区起止水位，消息复用生成注册表优先路由、原 Inbox/Dispatcher 与正式所有权 Fence。API 运行角色只注册该受控操作，不得启动常驻 Kafka Consumer；该入口默认关闭，显式启用时同步上限不得超过 1000 条、32 分区且整个操作超时不得超过 45 秒，失败/取消/超时必须尽力写入终态审计。更大范围必须使用未来的持久化异步作业，不得放宽普通 HTTP/Ingress 超时替代。CLI 通过 API 执行且令牌不得进入命令行参数。详细操作见 [`docs/operations/kafka-range-replay.md`](../../operations/kafka-range-replay.md)。

## 10. 安全、许可和供应链

- `Confluent.Kafka 2.15.0`：Apache-2.0；加入中央包管理、依赖漏洞和 Notice。
- `Testcontainers.Kafka 4.13.0`：MIT；仅测试依赖。
- Apache Kafka：Apache-2.0；生产镜像使用官方镜像并固定版本/摘要。
- Debezium：Apache-2.0；开发/集成测试固定 `quay.io/debezium/connect:3.4.3.Final` 与摘要。该官方镜像只用于测试/评估；生产由受信任平台从固定 Connector 工件构建或选择经批准的受支持发行物，并强制漏洞扫描、签名、SBOM 与摘要固定。
- 商业 Confluent Platform/Control Center/Schema Registry 不在默认授权范围；需要时单独进行许可和成本决策。

## 11. 测试与验收矩阵

### 11.1 契约与架构

- Envelope 字段、MessagePack 往返、未知 Schema、PartitionKey 和 TraceParent 校验。
- 业务模块禁止引用 Kafka/Debezium；Host/Provider 依赖方向正确。
- Topic/Subscription Catalog 拒绝重复、未知、无 Handler 和未批准路由。

### 11.2 双库

- 业务写入与 Outbox 同事务成功/回滚。
- Outbox/Inbox 迁移首次、重复、部分完成恢复。
- Inbox 首次、重复、并发、同 ID 不同 Payload、下游 Outbox 原子性。
- SQL Server CDC Capture 与 MySQL Binlog Capture 均产生相同 Envelope 语义。

### 11.3 Kafka 与故障点

1. 数据库提交后、CDC 捕获前进程/Connector 宕机；恢复后事件必须出现。
2. Kafka 已确认、Source Offset 提交前 Connector 宕机；允许重复，不得丢失。
3. Consumer 收到后、本地事务提交前宕机；消息重投并重新处理。
4. 本地事务提交后、Kafka Offset 提交前宕机；消息重投但 Inbox 阻止重复副作用。
5. 两个 Consumer Group 都收到事件；同 Group 两实例只有一个执行业务副作用。
6. 同 PartitionKey 保持顺序；不同 Key 可并行。
7. Broker 中断、重平衡、消息过大、坏 Payload、Retry、DLQ 和受控重放。

### 11.4 切换

- Shadow Topic 数量、EventId、PayloadHash、MessageType、SchemaVersion、PartitionKey 和时序核对。
- 旧 Worker 排空、停止单流所有权、记录 CDC 位点、启动正式 Relay/Consumer。
- 切换中任一门禁失败自动停止，不出现旧 Worker 与正式 Relay 双发布。
- 回退演练证明不会从不明确位点重复外部副作用。

## 12. 分阶段完成定义

| 阶段 | 可声明状态 | 完成条件 |
| --- | --- | --- |
| F0 | `Designing` | ADR、Spec、计划、规则和基线命令齐全 |
| F1 | `Implemented` | Envelope/Outbox/Inbox 代码与双库迁移落盘，尚未全链验证 |
| F2 | `Build-verified`（Provider 基础能力） | Unit/Architecture/双库/Kafka 聚焦测试通过，但未生产切流 |
| F3 | `Build-verified / Shadow-only` | 双库 CDC Shadow 无缺口、故障矩阵通过，业务消费者仍关闭 |
| F4 | `Pilot` | 单一低风险事件流生产等价试点、切换和回退通过 |
| F5 | `Production-verified` | 正式拓扑的负载、Soak、N+1、保留、恢复、告警和值班验收通过 |

文档、计划勾选、Connector 启动或 Kafka Topic 存在均不能单独提升状态。

## 13. 停止条件

出现以下任一情况必须停止当前阶段并回到设计/修复：

- 无法证明业务事务与 Outbox 原子提交；
- SQL Server CDC/MySQL Binlog 任一正式 Provider 无法恢复位点；
- Shadow 核对出现缺失或同 ID 不同 Payload；
- 轮询 Worker 与正式 CDC Relay 同时拥有同一事件流；
- Inbox 与业务写入无法处于同一本地事务；
- Broker/Connector 许可证、漏洞、生产构建来源、签名/SBOM、镜像摘要或运维所有权未关闭；
- 需要通过降低租户、安全、Audit 或事务语义才能继续；
- 生产保留窗口小于最大恢复目标。

## 14. 相关权威文档

- [总体架构设计 §9.1](2026-07-17-fullnet-architecture-design.md#91-事件交付演进基线)
- [技术集成路线](2026-07-17-technology-integration-roadmap-design.md)
- [`ADR-0005`](../../architecture/adr/ADR-0005-high-concurrency-modular-monolith-multi-instance-production-baseline.md)
- [Outbox Worker 运维说明](../../operations/outbox-worker-topology.md)
- [Kafka Consumer Group 协议与 Assignor 迁移](../../operations/kafka-consumer-protocol-migration.md)
- [`rules/development-quality.md`](../../../rules/development-quality.md)
- [`rules/performance-engineering.md`](../../../rules/performance-engineering.md)
