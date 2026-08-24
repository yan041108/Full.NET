# ADR-0006：事务 Outbox、CDC Relay 与 Kafka 事件交付

- 状态：已批准
- 日期：2026-08-08
- 决策者：项目所有者明确批准提前实施
- 适用范围：Full.NET 可靠业务 Integration Event 的生产、捕获、发布、订阅、幂等、死信、重放、运维和迁移
- 正式规格：[事务 Outbox、CDC Relay 与 Kafka 事件交付规格](../../superpowers/specs/2026-08-08-transactional-outbox-cdc-kafka-design.md)
- 实施计划：[事务 Outbox、CDC Relay 与 Kafka 实施计划](../../superpowers/plans/2026-08-08-transactional-outbox-cdc-kafka.md)
- Consumer 协议迁移：[Kafka Consumer Group 协议与 Assignor 迁移](../../operations/kafka-consumer-protocol-migration.md)
- 替代关系：替代 [`ADR-0005`](ADR-0005-high-concurrency-modular-monolith-multi-instance-production-baseline.md) 中“只有生产等价轮询瓶颈出现后才进入 Kafka/CDC Decision Gate”的时间门禁；ADR-0005 的模块化单体、事务、缓存、Audit、容量和生产认证边界继续有效

## 背景

当前 `fn_outbox_message` 同时承担事务事件存储、Worker 队列、租约、重试、死信和处理状态。Worker 通过数据库批量领取、续租和状态更新完成至少一次处理；该实现已经具备双库、重试、死信和可观测基础，但高频轮询、锁、续租和状态写入会持续占用数据库连接、索引和事务资源。

项目所有者要求不引入 CAP 或 MassTransit，保留 Full.NET 自有事件契约和事务边界，提前演进为“.NET 业务事件 + 事务 Outbox + Broker 发布订阅”，并尽量消除应用 Worker 对 Outbox 热表的轮询压力。

SQL Server CDC 由 SQL Server Agent 从事务日志捕获变更并写入 CDC 变更表，连接器仍需读取变更数据；因此本决策不承诺“数据库零读取”，目标是移除应用层对 Outbox 队列表的领取、租约、续租和处理状态更新。MySQL 使用 ROW Binlog 流捕获已提交行变更。

## 候选方案

### 方案一：继续优化数据库轮询

优点是依赖少、现有闭环成熟；缺点是领取、租约和状态写入仍由业务数据库承担，发布订阅扇出和跨进程消费者需要继续自行扩展数据库队列语义。

### 方案二：请求事务直接发布 Kafka

数据库提交与 Kafka 发布无法组成 Full.NET 支持的本地原子事务；任一侧成功、另一侧失败都会产生丢事件或幽灵事件，因此拒绝。

### 方案三：事务 Outbox + CDC Relay + Kafka + Inbox（采用）

业务事务只追加 Outbox；SQL Server CDC 或 MySQL Binlog 捕获已提交的 `INSERT`；Debezium/Kafka Connect 将事件路由到 Kafka；.NET Consumer 通过 Consumer Group 接收，在本地数据库事务内使用 Inbox 去重、执行业务写入并产生下游 Outbox，提交后才确认 Broker Offset。

### 方案四：自行解析 SQL Server Transaction Log/MySQL Binlog

SQL Server 内部日志格式不构成 Full.NET 可承担的稳定公共契约，自研日志读取器会扩大数据丢失、版本兼容和恢复风险，因此拒绝。CDC 捕获采用数据库官方能力，连接器优先采用 Debezium；Full.NET 自研范围止于事件契约、Provider 边界、Inbox、消费管道、治理和运维集成。

### 方案五：引入 Wolverine 作为运行时消息框架

Wolverine 提供成熟的 Inbox/Outbox、Kafka、重试、死信、重放、代码生成和可观测能力，适合作为行为完整性与性能对标对象；但其 Durable Outbox 由数据库持久化与 Durability Agent 协调，不能替代本决策用 CDC 移除应用层 Outbox 热表轮询的目标。Full.NET 因此不引入 `WolverineFx*` 运行时依赖，继续维护自有主链路，并分阶段吸收经过双库、Kafka 故障矩阵和生产等价负载验证的设计。

## 决策

### 1. 技术与依赖

1. Broker 默认采用 Apache Kafka；本地和集成测试基线采用 `apache/kafka:4.1.2`，生产版本升级必须通过兼容与恢复测试。
2. CDC Relay 默认采用 Debezium `3.4.3.Final` 及 Outbox Event Router：SQL Server 使用官方 CDC，MySQL 使用 ROW Binlog。开发和集成测试可以使用固定 `quay.io/debezium/connect:3.4.3.Final`；Debezium 官方将该镜像定位为测试/评估用途，生产必须由受信任平台从固定 Connector 工件构建或采用经批准的受支持发行物，并完成漏洞扫描、签名、SBOM 和摘要固定。
3. .NET Kafka Provider 使用 `Confluent.Kafka` `2.15.0`，通过 Full.NET 自有抽象暴露；业务模块不得直接引用 Kafka 客户端。
4. 本地集成测试使用 `Testcontainers.Kafka` `4.13.0`；数据库继续使用当前 SQL Server/MySQL Testcontainers。
5. 不引入 CAP、MassTransit、Confluent 商业 Schema Registry 或 Confluent 商业运行时作为默认依赖。Schema Registry 只有在 MemoryPack 契约治理无法满足真实跨语言消费者时才另行决策。
6. Wolverine 只作为成熟参考实现和性能对标对象，不进入生产依赖图；对标必须同时比较吞吐、P95/P99、数据库往返、重复投递、恢复时间和资源上限，禁止只比较理想路径 QPS。

上述版本是首次实施基线，不表示永久锁死；任何升级都必须经中央包管理、镜像摘要、漏洞、许可和兼容验证。

### 2. 不可变可靠性边界

1. 可靠业务事件必须与业务状态在同一数据库事务写入追加式 Outbox；事务内禁止发布 Kafka、调用外部网络或等待 Broker 确认。
2. 端到端语义固定为至少一次。Kafka Producer 幂等只减少单 Producer Session 的重复，不能代替稳定 `EventId` 和消费 Inbox，也不能宣称端到端 Exactly-Once。
3. 每个持久化订阅以 `(ConsumerName, MessageId)` 建立唯一 Inbox；Inbox 领取、业务写入、下游 Outbox 和完成标记必须处于同一个本地数据库事务，提交后才能提交 Offset。
4. 同进程模块内部事件继续走类型化 Contract/Dispatcher，不进入 Kafka。
5. 缓存失效、日志、Trace、Metrics、普通 HTTP Operation Log 和 Audit 继续禁止使用可靠业务 Outbox。

### 3. 事件契约

可靠事件 Envelope V2 至少包含：

- `EventId`
- `MessageType`
- `SchemaVersion`
- `ContentType`
- `TenantId`
- `PartitionKey`
- `CorrelationId`
- `CausationId`
- `TraceParent`
- `Producer`
- `OccurredAtUtc`
- `Payload`

`MessageType` 继续使用 `{owner}.{module}.{entity}.{event}`；`EventId` 使用应用端 UUID v7；`PartitionKey` 必须来自稳定业务标识，默认由 `TenantId + AggregateType + AggregateId` 组成，禁止随机值和翻译文本。MemoryPack 继续作为二进制 Payload 格式（`application/x-memorypack`），未知版本失败关闭。

### 4. Outbox 与 CDC

1. 新增追加式 `fn_messaging_outbox_event`，通过 Expand/Contract 与现有 `fn_outbox_message` 并存；新表只允许 `INSERT` 和受控保留期 `DELETE`，不得以 `UPDATE` 表达发布状态。
2. SQL Server 为新表启用 CDC；MySQL 为 Connector 配置唯一 `server_id`、ROW Binlog、FULL row image 和满足恢复窗口的保留期。
3. Debezium Connector 只捕获新 Outbox 表，Outbox Event Router 只处理创建事件，并将 `PartitionKey` 作为 Kafka Key。
4. CDC Source Offset、Schema History、心跳和 Connector 配置必须持久化、备份和监控。Outbox 清理只有在 CDC 低水位超过目标记录并经过安全窗口后才可执行。
5. SQL Server CDC Capture/Cleanup Job 停止、MySQL Binlog 被提前清理或 Connector 位点不可恢复时必须失败关闭并进入运维恢复，不得静默跳过事件。

### 5. Topic、订阅和消费

1. Topic 使用稳定小写点分层：`fullnet.{environment}.{owner}.{module}.{stream}.v{major}`；DLQ 使用原 Topic 加 `.dlq`，Retry Topic 使用 `.retry.{stage}`。
2. Consumer Group/`ConsumerName` 使用稳定小写点分层并进入订阅目录；同组竞争消费，不同组实现发布订阅扇出。
3. Handler 路由唯一键从全局 `(MessageType, SchemaVersion)` 调整为 `(ConsumerName, MessageType, SchemaVersion)`；不同订阅可以处理同一事件，同一订阅内仍禁止歧义。
4. 顺序只承诺同 Topic Partition 内顺序。需要实体顺序的事件必须使用同一 `PartitionKey`，并携带业务版本或序号；消费者必须定义重复、旧版本、乱序和缺口策略。
5. Producer 使用 `acks=all`、幂等、有限交付超时、压缩和有界缓冲；Consumer 禁止自动提交 Offset，只有 Inbox 本地事务提交后才手工提交。

### 6. 重试、死信与重放

1. 临时故障使用有上限的分级 Retry Topic，永久契约错误、权限/租户边界错误、Payload 损坏和超过最大尝试次数进入 DLQ。
2. DLQ 保留原始 Envelope、Topic、Partition、Offset、ConsumerName、首次/末次失败时间、尝试次数、稳定原因码和脱敏错误摘要。
3. 重放只能由受保护 Host 运维能力按精确 `ConsumerName + MessageId` 或受控范围执行，必须记录操作者、原因、来源 Offset、目标 Topic 和 Domain Audit。
4. 重放仍经过 Inbox；不得删除 Inbox 唯一记录来强制重复业务副作用。需要业务重执行时必须由专用补偿命令表达。

### 7. 安全与租户

1. Kafka 生产使用 TLS 与 SASL/最小 ACL；Producer、Connector 和 Consumer 使用独立凭据，Secret 不进入仓库、日志、消息头或管理响应。
2. `TenantId` 只能来自原事务的可信租户上下文；Consumer 恢复租户上下文并验证订阅允许的 Host/Tenant 作用域。
3. Topic ACL 不能替代应用租户隔离；不得创建每租户 Topic 作为默认隔离方式。
4. Payload 按现有数据分类、最小化和保留政策治理；禁止把密码、令牌、连接串或无必要 PII 放入事件。

### 8. 可观测性与运维

必须提供低基数指标和 Trace：Outbox commit-to-capture 延迟、SQL Server LSN/MySQL Binlog Lag、Relay publish 延迟、Connector 错误、Broker Produce 错误、Consumer Lag、最老未消费年龄、Inbox 重复命中、Retry/DLQ 数量、重放结果和 Outbox/Inbox/Broker 存储增长。

API 只写 Outbox，因此短时 Broker 故障不阻塞请求；但 Outbox、事务日志、CDC 变更表或 Binlog 接近保留/容量边界时必须告警、停止切流或执行已批准的流量保护，禁止静默丢弃可靠事件。

### 9. 分阶段实施与切换

1. **F0 设计与基线**：完成规则、ADR、Spec、计划和现有轮询基准。
2. **F1 契约与存储**：实现 Envelope V2、追加式 Outbox V2 和双库迁移，仍由旧 Worker 承担生产业务语义。
3. **F2 Broker 与 Inbox**：实现 Kafka Provider、订阅目录、Inbox、消费事务、Retry/DLQ 和故障测试。
4. **F3 影子 CDC**：SQL Server/MySQL Connector 只发布到无业务消费者的 Shadow Topic，比对数量、摘要、顺序、延迟和恢复。
5. **F4 单流试点**：选择一个可回退、幂等、低风险的真实业务事件；停止该流旧发布所有权并排空后，启用唯一 CDC Relay 与正式 Consumer。
6. **F5 扩展与退役**：逐流迁移；稳定窗口和生产等价恢复验收通过后，才收缩旧轮询字段、索引和 Worker 路径。

任何阶段失败都停止进入下一阶段。Shadow Topic 不得存在业务消费者；同一正式事件流不得由旧 Worker 与 CDC Relay 同时发布。回退必须先停止 CDC Relay/Consumer、记录位点并排空或隔离 Broker 消息，再从明确边界恢复旧 Worker，不能通过双开争抢实现回退。

## 后果

- Full.NET 获得不依赖 CAP/MassTransit 的可靠 Broker 发布订阅、跨进程扇出、独立消费者积压和重放能力。
- 数据库仍承担业务事务与追加式 Outbox 写放大，但不再承担应用层队列领取、租约续期和处理状态热点写入。
- 新增 Kafka、Kafka Connect/Debezium、CDC、Topic/ACL、位点、保留和灾备运维成本；未形成值班与恢复能力前不得生产切流。
- SQL Server 和 MySQL 的 CDC 机制不同，必须分别测试和运维，不以抽象层掩盖 Provider 差异。
- 该决策不授权全面微服务化，不改变模块数据所有权，也不授权分片、多数据库双写或 Event Sourcing。

## 验收与复核

生产切流至少满足：

1. SQL Server/MySQL 业务事务 + Outbox + CDC + Kafka + Inbox 全链路真实集成通过；
2. 四个关键崩溃点重复测试证明不丢事件且无重复业务副作用；
3. Shadow 事件数量、Payload 摘要和顺序核对无缺口；
4. 单一发布所有权、排空和回退演练通过；
5. CDC/Connector/Broker/Consumer Lag、存储、保留和告警已接入；
6. 依赖漏洞、生产 Connector 构建来源、镜像签名/SBOM/摘要、Apache-2.0/MIT 许可及 Notice 复核通过；测试用 Quay 镜像不得直接提升为生产镜像；
7. 能力矩阵只在相应证据完成后从 `Designing` 更新为 `Build-verified`，生产等价恢复和负载认证后才可标记 `Production-verified`。

## 参考资料

- [Microsoft SQL Server Change Data Capture](https://learn.microsoft.com/en-us/sql/relational-databases/track-changes/about-change-data-capture-sql-server)
- [MySQL Binary Log](https://dev.mysql.com/doc/refman/8.4/en/binlog-replication-configuration-overview.html)
- [Debezium SQL Server Connector](https://debezium.io/documentation/reference/connectors/sqlserver.html)
- [Debezium MySQL Connector](https://debezium.io/documentation/reference/3.4/connectors/mysql.html)
- [Debezium Outbox Event Router](https://debezium.io/documentation/reference/stable/transformations/outbox-event-router.html)
- [Apache Kafka Producer Configuration](https://kafka.apache.org/40/configuration/producer-configs/)
- [Wolverine Durable Messaging](https://wolverinefx.net/guide/durability/)
- [Wolverine Kafka Transport](https://wolverinefx.net/guide/messaging/transports/kafka)
