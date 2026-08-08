# ADR-0005：高并发模块化单体多实例生产基线

- 状态：已批准
- 日期：2026-08-01
- 决策者：项目所有者在本轮设计封板中逐项确认
- 适用范围：Full.NET 1.0 的 API、Worker、Migrator、数据库、Redis、缓存、Outbox、Audit、日志、Realtime、文件、Kubernetes 发布和容量认证
- 正式规格：[Full.NET 总体架构设计规格](../../superpowers/specs/2026-07-17-fullnet-architecture-design.md)
- 评估证据：[高并发模块化单体多实例改造评估](../../verification/high-concurrency-modular-monolith-multi-instance-assessment-2026-08-01.md)
- 后续决策：Kafka/CDC 的实施时机与事件交付边界已由 [`ADR-0006`](ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md) 部分替代；本 ADR 的模块化单体、容量与生产拓扑基线继续有效

## 背景

Full.NET 的目标包括“单体承载 1 万个同时在途动态请求”。这里的 1 万指同时尚未完成的动态 HTTP 请求，不是固定 QPS，也不能脱离请求耗时、数据库容量、外部依赖和硬件规格承诺。

开发阶段没有目标测试硬件，因此需要先冻结一套以万级在途为目标的可横向扩展框架设计，再在专用生产等价环境认证容量。架构必须同时解决多实例正确性、缓存一致性、日志洪峰、Audit 可靠性、滚动发布、灾备与过载自保，不能只通过增加线程、连接或 Pod 数量制造表面吞吐。

## 候选方案

### 方案一：保持单进程单实例

部署简单，但任一进程、节点或滚动发布都会中断服务，无法提供 99.9% SLO 所需的基本冗余，也无法安全承载正式容量认证。

### 方案二：强化型模块化单体、多实例运行（采用）

保持业务模块同进程调用和本地事务优势，将 API、Worker、Migrator 按运行角色分离，在 Kubernetes 上水平扩展无状态 API，并通过共享数据库、Redis、对象存储、Data Protection 和集中式可观测性解决多实例状态问题。

### 方案三：立即全面微服务化

会提前引入网络边界、分布式事务、服务治理、更多发布单元和故障模式，但当前没有独立伸缩、团队或 SLA 证据证明收益高于成本。模块仍可在以后满足 ADR-0002 门禁时按证据拆分。

## 决策

### 1. 生产拓扑与 SLO

1. 成熟生产参考为 Kubernetes + Helm，至少 3 个 Worker Node；API 最少 2 副本并使用滚动发布、PDB、拓扑分散、探针、排空、资源限制和网络策略。
2. Worker 独立部署且至少 2 副本用于故障接管，Outbox/Jobs 默认每副本 `MaxConcurrency=1`；Migrator 是发布管线控制的一次性 Job。
3. 月度可用性 SLO 为 `99.9%`，系统 5xx、过载 429、网关/应用超时和超 Endpoint 预算的响应计为坏事件，客户端认证/权限/验证和明确业务拒绝不计；默认月度错误预算约 43 分 50 秒并采用多窗口 Burn Rate 告警。Ingress/Gateway 与应用共同实施限流、连接/Body/超时限制和过载保护。
4. 应用 Helm Chart 不安装生产数据库、Redis、对象存储和可观测性后端；这些状态服务由独立平台能力提供高可用、备份和容量保障。

### 2. 数据、缓存与 Outbox

1. 关系数据库是业务事实权威源，SQL Server 与 MySQL 都是正式 Provider；Pod 扩容受数据库总连接预算硬约束。
2. FusionCache 是唯一缓存实现。缓存按 C0 权威强一致、S0-L2 共享即时、S1 重要业务、S2 可降级展示和 N0 不缓存分类；S1/S2 可使用 L1 + Redis L2 + Redis Backplane，C0/S0-L2 禁用 L1，N0 直接读取权威源。
3. 缓存失效不使用 Outbox。事务提交后当前实例直接清理 L1/L2，再由 Backplane 快速通知其他实例，TTL、缓存版本和权威源读取负责最终收敛。重复删除必须幂等且不得主动触发数据库回填。
4. 只有昂贵热点回填存在并发击穿证据时才增加分布式锁；锁获得者双检 L2 后回源，其他请求等待后重读或按缓存类别回退。
5. Outbox 只承载需要与业务事务原子提交、可靠重试的重要业务 Integration Event，交付语义为至少一次并要求消费者幂等。缓存失效、日志、Trace、Metrics、普通 HTTP Operation Log 和 Audit 均不进入 Outbox。

### 3. Audit、HTTP Operation Log 与日志

1. B0 Domain Audit 与业务状态在同一数据库事务直接写入，承担 fail-closed 语义。
2. B1 重要 HTTP Operation/Exception Audit 采用有界跨请求微批直接写审计库；请求等待所属批次写入尝试，默认 fail-open 并告警。若业务要求“无审计不成功”，必须提升为 B0。
3. B2 普通 HTTP Operation Log、Access 和 Diagnostic 通过异步有界日志管道进入日志平台，可按策略采样；每请求最多一条 HTTP 汇总记录，生产默认只采集 Summary。
4. 程序员诊断日志通过 `ILogger<T>`、`EventId/EventName`、`DiagnosticGroup` 和 Trace 上下文逻辑分组，不在代码中指定文件或 Sink。动态诊断按范围临时开启并强制 TTL、速率/字节上限、操作者、原因和 Audit；配置经当前实例刷新 + Redis Backplane + 版本/TTL 传播，不使用 Outbox。
5. 生产参考管道为 JSON stdout -> Fluent Bit 磁盘缓冲 -> Loki/对象存储；OTLP -> OpenTelemetry Collector -> Tempo/Prometheus -> Grafana。平台可替换，应用字段和可靠性语义不变。
6. 所有高频写入都必须批量化并有界，禁止每条日志/Audit 各开一次数据库连接；过载状态只能收缩 B2/Best Effort，不得改变 B0/B1 语义。

### 4. 多实例共享状态

1. Data Protection 使用稳定 `ApplicationName`、专用共享 RWX Key Ring 和 X.509 静态加密；历史证书、私钥与 Key Ring 一并备份和恢复。禁止使用可驱逐缓存 Redis 或 Pod 本地卷保存 Key Ring。
2. 生产文件使用集群外 S3 兼容对象存储。
3. `Cache/Backplane` Redis 与 `Realtime` Redis 暴露独立连接边界，生产默认物理隔离；开发可共用，生产同机例外必须有容量与故障域证据。
4. 多实例 SignalR 使用 Redis Backplane。除 WebSockets-only + `SkipNegotiation` 外，入口保持连接亲和；离线业务事实不依赖 Realtime Redis。

### 5. 发布、恢复与容量声明

1. 发布采用消费者优先与 Expand/Contract：配置预检 -> Expand 迁移 -> 兼容 Worker -> API 滚动部署 -> 观察/排空 -> 后续独立 Contract 迁移。
2. 业务数据库/Domain Audit 同城高可用目标 RPO 0，备份恢复 RPO 不超过 5 分钟、RTO 30 分钟；Data Protection RPO 0/RTO 15 分钟；已确认对象 RPO 0/RTO 30 分钟。Redis Cache 可重建，Realtime 不保存离线事实；其余详细目标以正式 Spec 第 21.3 节为准。
3. 开发阶段不要求证明 1 万同时在途，只要求高并发设计、正确性、资源上限和轻量回归。
4. 设计同步、多实例正确性、资源治理、Kubernetes 部署、双库、恢复和回滚门禁完成后，可受控上线保守流量，但必须标记 `Capacity-not-verified`，不得宣传 10K。
5. 10K 声明必须在专用生产等价环境完成 2K/5K/10K 台阶、Soak、N+1、故障注入和 SQL Server/MySQL 分 Provider 认证，并保留吞吐、错误率、P50/P95/P99、在途、连接池、队列、数据库、Redis、GC、CPU 和内存证据。

## 明确不在本决策范围

- 全面微服务化、服务网格；Kafka/CDC 事件交付已移交 ADR-0006 独立治理；
- 跨地域双活、数据库读副本、分片；
- 99.99% 默认 SLO；
- 为压测修改业务一致性、安全、Audit 或 Outbox 语义；
- 把开发机 10K 测试设为功能交付门禁；
- 在应用 Chart 内部署生产状态服务。

## 后果

- 框架实施必须先完成权威文档和规则同步，再依次完成多实例正确性、缓存/Audit/日志/资源治理、Kubernetes 工程化，最后在专用硬件做容量认证。
- 运维复杂度高于单实例，但获得无状态 API 扩容、滚动升级、故障接管和可审计容量结论；业务代码仍保持模块化单体，不承担微服务网络复杂度。
- Redis Backplane 是快速通知，不是强一致事务总线；需要强一致的缓存类别必须禁用 L1或回到权威数据源。
- Outbox 写放大只为重要业务事件支付，不再承担缓存、日志或 Audit 的通用可靠队列职责。
- 未取得正式容量证据时，任何文档、发布说明或市场材料都必须保持 `Capacity-not-verified`。

## 替代与复核条件

只有出现经测量的模块独立伸缩/SLA/故障隔离需求，才进入 ADR-0002 模块拆分门禁。事务 Outbox 的 Kafka/CDC 演进已由 ADR-0006 批准提前实施，但生产切流仍需满足双库、影子核对、单一发布所有权、排空和回退门禁。任何后续替代方案都必须新增或修订 ADR，并保持租户、安全、事务、Audit、双库和恢复语义不降级。
