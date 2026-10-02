# ADR-0012：可配置集中日志、采集缓冲与 Kafka 传输

- 日期：2026-09-28
- 状态：方案已确认并获准开始分阶段实施；第 7 条 Logstash/PQ 消费确认基线因 2026-09-30 新证据进入重新评估，相关可靠档准入阻塞；不授权生产部署/切流
- 范围：B2/运行诊断日志、请求安全投影、可选采集与查询基础设施
- 不替代：ADR-0005 的容量与审计边界、ADR-0006 的可靠业务事件主链
- 规格：同步[总体架构 §16](../../superpowers/specs/2026-07-17-fullnet-architecture-design.md#16-可观测性与高并发日志)
- 详细说明：[日志模块](../../operations/logging-module.md)
- 唯一活动计划：[LG00—LG08](../../superpowers/plans/2026-09-28-configurable-log-delivery.md)，LG00 为消费者与持久边界准入实验

**验收修订（2026-09-30）：**项目所有者明确将本项目全部验收改为本地测试实际通过即可，执行位置统一遵守[开发质量 §11](../../../rules/development-quality.md#11-测试与验证)。Collector 的应用配置准入以本地 Host/Helm、固定版采集路由、TLS/ACK 和有界故障测试为依据，允许生产 API/Worker 显式选择 Collector，不再以专用环境、CI 终态或完整 10K 压测作为该开关的前置。该开关表示应用通过 stdout 交由外部采集，不证明平台下游已部署；可选 Kafka/ES 消费组合仍按本地实际测试验证自己的确认与恢复行为。容量保持 `Capacity-not-verified` 直到对应规模实测完成，B2 允许丢弃、已知满盘/缓冲损失及未验证范围按原始证据披露。历史段落中的旧环境门禁由本修订替代；生产部署/切流需当前用户授权。

**ApplicationKafka 准入修订（2026-09-30）：**用户继续授权推进生产入口，按本地 Host/Helm 配置、双通道预算和关闭、真实 TLS/SASL/ACL Broker 投递/故障恢复及 API/Worker 原生运行验收。API/Worker 可显式选 ApplicationKafka，必须保持 Production 环境、同值预期模式、静态适配、双 Topic、TLS、冻结索引路由和专用 Producer Secret；不启用旧 ES Sink，Collector 排除 applicationkafka Pod。入口准入止于 Broker 投递边界，Accepted 不代表 ACK，默认无跨重启 Spool；B2/运行诊断可过载丢弃，B0/B1 审计仍独立。原完整容量比较前禁止入口的前置由本修订替代，容量、最终存储及具体可靠消费组合仍分别取得实际本地证据。具体结果以活动计划/Verification 为准；未授权生产切流。
## 背景

现有 ILogger/Serilog 双通道保护请求线程，但应用内存队列没有跨崩溃恢复能力。部署示例已有 Fluent Bit 文件系统缓冲，emptyDir、字段解析及优先级筛选尚不能证明跨 Pod 恢复。核心 Hosting 引用已归档的 Serilog.Sinks.Elasticsearch；Enabled=false 不等于组件未进入发布闭包。

用户要求 Kafka/Elasticsearch 可配置，并要求截图圈出的请求/返回页签及请求上下文在可用时尽量采集。截图只作为字段需求，不作为复制 Admin.NET.Pro 实现、记录凭据或承诺全量原始 Body 的授权。

## 候选与取舍

| 方案 | 收益 | 代价 | 选择 |
| --- | --- | --- | --- |
| 应用直接同步写 Kafka/ES | 实现表面简单 | 慢网络进入同步 Log 边界、客户端缓冲与故障耦合 | 拒绝 |
| 应用有界后台直发 Kafka | 省 stdout/文件采集与再次解析，可能降低 CPU/I/O 和交付延迟 | 宿主客户端/native/凭据/预算与释放成本，默认无跨进程 Spool | 正式候选 ApplicationKafka；验证后可配置选择 |
| 应用后台直写 ES | 可省 Broker | 下游索引故障与应用发送预算耦合 | 本次不新增；旧 Sink 仅兼容迁移 |
| 有界日志 + 独立采集缓冲 + 可选 Kafka + 可选查询/归档 | 应用外恢复、统一采集、依赖隔离与独立扩容 | stdout/读取/解析及平台恢复运维成本 | Collector 参考默认；同环境比较后选择 |

2026-09-28 用户确认后台直发 Kafka 纳入正式比较，分析验证后可采用或按配置选择。本修订替代原“后台直连仅留待未来需求”的 Kafka 限制，不授权代码实施/生产切流，也不把两路线标为已实现。选择比较请求 P99、每实例日志字节吞吐、CPU/托管及 native 内存、丢弃与故障恢复；ConcurrentQueue 本地入队成本和 Kafka 集群容量均不能单独证明全链无瓶颈。

## 决策

1. 业务保留 ILogger<T>；Serilog 保留现有双通道，高频固定模板使用 LoggerMessage。不把 Kafka 当日志 API，不创建业务 Outbox 日志。每事件与两通道分别设置字节预算，配合条数、属性/深度/集合/字符串上限；队列只持有脱离原始对象引用的受限快照。
2. 应用 DeliveryMode 规划为 Local/Collector/ApplicationKafka。Collector 保留 Console/可选文件到 Fluent Bit 的参考路线并验证恢复；ApplicationKafka 使用有界后台 Producer，不让请求等待 Broker ACK，不逐条串行等待或每事件 Flush。统一字段、隐私和预算不随模式变化。
3. Kafka 与 Elasticsearch 独立可选；Local/CentralDirect/CentralKafka 加上 ApplicationKafka 为受控组合。平台 ingress 区分 Collector/ApplicationKafka，transport 的 Direct 只表示跳过 Broker；直发必须为 Kafka。日志 Topic/Producer/配额与业务事件隔离，应用 Chart 不承载 Broker/ES 集群。
4. 核心 Hosting 渐进移出旧 ES 写入依赖，不强制引用日志 Kafka 客户端。直发由可选静态适配和经验证的宿主构建提供，LG00 先定义 Host.Api/Worker 消费者、依赖方向与隔离收益，不提前建空项目。缺少适配不能靠配置开启；运行时关闭不等于依赖退出发布闭包，默认/直发产物分别验证许可、体积、裁剪和 Native AOT。直发应用只持有最小日志写权限，Collector 应用不持平台凭据。
5. 图示字段按日志模块清单实现“可用则采集”；Minimal API 不伪造 MVC 字段，线程 ID 是可选捕获点诊断。原 IP/端口、服务端地址、请求/返回详情默认为 Restricted，只进受保护 B1 审计库，不复制到 Console/File/Kafka/ES/归档。B2 只允许静态审定的 Internal 摘要；本计划不开放 Restricted 外部导出。
6. B1 操作详情使用可空版本化 ContextJson 与 DetailsExpiresAtUtc 的双库扩展，历史可读，精确详情权限与到期控制。独立详情清理不受普通 Auditing:Retention.Enabled=false 影响，健康/积压未知或超过预算时只拒绝新详情。采集先取得目的地、白名单和预算许可，再执行投影/源生成序列化；不接收预先生成的任意 JSON 字符串。不把 GET 提升为 B1，B0/B1 原事务/等待/失败语义保持。
7. Kafka 消费写入端原定以 Logstash/PersistentQueue 为准入基线，现将这一具体组合的可靠档准入标记为**阻塞并重新评估**。Elastic 官方明确说明 Kafka 输入不能保证事件安全写入 PQ 后才提交 Offset；`enable_auto_commit=false` 和 `queue.checkpoint.writes=1` 不能消除该风险。LG00 需重新选定可证明的消费者/确认边界：逐项最终写入或可靠隔离后推进连续 Offset 的 SinkConfirmed，或能证明持久接收先于连续提交的其他实现。固定实现、版本、确认配置与故障证据后才开放对应 Kafka 档；不把 Logstash 内存队列视为 SinkConfirmed，也不因本次重新评估自动引入自研消费者。
8. 配置关闭不连接/不探测；错误启用配置启动拒绝，运行中远程日志故障只在有界日志链内降级，不影响认证和业务事务。路由/凭据/持久队列不任意热切换。
9. ES 初始选择固定 UTC 事件日期索引，冻结 IndexRouteVersion/OccurredAtUtc/ExpiresAtUtc，LogEventId 映射 document_id，以 index 动作重试写同一索引/ID。关闭 data stream 与 ILM rollover；去重只覆盖该路由保留窗口。过期事件不重放、不重建过期索引；未来采用 rollover 另行设计跨索引去重。
10. 至少一次只从已验证的持久交付边界承诺；最终存储或可靠隔离逐项确认。PQ 不复制数据，永久卷丢失仍有 RPO/恢复预算。不能承诺应用内存入队零丢失、日志计费上限等于进程 RSS 或全链 Exactly-Once。
11. 同一事件同一时段只有一个集中投递入口。发布清单为每个 Pod 注入不可变的日志路由标识和预期模式；Collector 必须根据经验证的 Kubernetes 元数据只选择 Collector 流，缺失元数据时失败关闭，不按容器名通配符默认发送。滚动发布时旧 Pod 仍按旧路由、新 Pod 按新路由；本地诊断与其他 Serilog Sink 不得形成第二个集中出口。应用启动只验证注入值与自身配置，不能冒称已验证运行中的采集器配置；发布模板和真实路由测试再证明平台一致性。切换须定义旧入口限时排空/残留、新入口所有权和同 ID 对账，不自动双写或故障切路。Producer 内部队列也受消息/字节/在途预算，普通/优先 Topic 不能替代客户端容量隔离；投递回调、QueueFull、超时与停机纳入验证。默认直发无应用 Spool，未确认记录的崩溃损失窗口须披露。
12. 旧 `FullNet:Logging:Elasticsearch:Enabled=true` 在未指定新 DeliveryMode 时保持原直写行为并标为 `LegacyDirect`，给出弃用告警；没有新模式且旧开关关闭时保留既有 Console 行为，外部采集是否存在由部署清单决定。显式 Local/Collector/ApplicationKafka 与旧直写开关同时开启一律拒绝启动，避免静默失效或双写。`LegacyDirect` 只用于迁移，不能作为新的目标配置；替代链验证、配置迁移和发布核查完成后才能移除旧 Sink。

## 后果与迁移

- 旧 ES 配置按上述优先级兼容并提示迁移；固定日期索引使用新的受控路由，旧索引仍可读取，不自动复制、改名或删除历史数据。核心旧 Sink 依赖只有在替代路径及所有存量配置完成验证后退役。
- ES 健康页区分“应用直写已注册”和“外部采集链”，Broker/集群可达不等于记录可查询；关闭时有明确状态。
- 独立采集层增加平台运维成本，但使多数默认应用不承担远程日志客户端。Kafka 只在吞吐、故障窗口或多消费者需求明确时启用。
- 双通道只隔离应用缓冲和消费线程，仍共用 stdout；非阻塞入队不消除事件构造和同步竞争。入队前有界 UTF-8 快照引入调用线程序列化成本，节点采集器还承担所有实例的汇总流量与共享资源压力；按日志模块 §2.4/§2.5 和 LG02/LG06/LG08 分阶段验证，不以增加队列容量替代持续吞吐证据。
- 原 Logstash/PQ 基线的恢复实验只证明单个已入队事件可在 Broker 离线时恢复；它不推翻 Elastic 对 Offset 可能领先安全 PQ 写入的明确限制。该组合不能用于本 ADR 要求的可靠档准入；详见[验证记录](../../verification/2026-09-30-logstash-pq-restart.md)及[官方说明](https://www.elastic.co/docs/reference/logstash/tips-best-practices)。SinkConfirmed 是独立准入候选，不是放宽 PQ 刷盘参数的捷径。任一入口/消费者准入失败仅保持该组合关闭，保留已验证组合，不自动引入其他组件或自研 Worker。
- 后续固定版本筛查还发现 Vector 0.58.0 的 ES 单项 400 会被丢弃并推进后续 Offset；IBM Kafka Connect Elasticsearch Sink `276c755` 的 Bulk `errors=true` 仅记录错误且 `flush()` 成功；Confluent Elasticsearch Sink 尚有发行许可及真实确认顺序待核对。证据分别见 [Vector 故障实验](../../verification/2026-09-30-vector-kafka-es-boundary.md)与[插件源码审查](../../verification/2026-09-30-kafka-connect-sink-screen.md)。LG00 因此增加独立**平台**消费者的实验性 SinkConfirmed 候选，其研发不改变本 ADR 的生产选型、应用宿主边界或关闭门禁；需逐项确认、连续 Offset、可靠 DLQ 及故障恢复全部证明后再决定是否选用。
- 两路线比较固定事件分布、采样/安全、Broker 配置和下游确认边界，再单独比较消费者。满足请求/资源/丢弃/恢复预算后可按配置用于不同部署；Collector 的应用外缓冲与直发的精简收益不能被单项峰值吞吐覆盖。
- 采样、持久容量、磁盘满策略、保留和恢复追赶必须有测量；保留 Capacity-not-verified，未执行项不升级为 Verified。
- 当前授权已扩展到按活动计划分阶段实施；每一组合通过自身测试和准入后才能标为可用，生产切流仍需独立批准。
