# Full.NET 日志模块说明

> 更新：2026-09-28，已纳入方案审查、性能边界和采集器/应用后台直发 Kafka 两路线比较与配置目标。初次阅读基线为 `006ca78f1fff66a6bfe37627264ee527e3d9c673`，两路线文档更新基线为 `32fc0330757dc74b614e8c1b54e57e8824c0537b`。本文区分当前实现与目标；用户已授权分阶段实施，但未通过的投递或容量不得标为已验证。

## 1. 阅读入口与状态

- 架构边界：[总体架构 §16](../superpowers/specs/2026-07-17-fullnet-architecture-design.md#16-可观测性与高并发日志)。
- 本次决策：[ADR-0012 可配置日志采集与 Kafka](../architecture/adr/ADR-0012-configurable-log-delivery.md)。
- 唯一活动计划：[LG00—LG08](../superpowers/plans/2026-09-28-configurable-log-delivery.md)，先完成消费者与确认边界的准入实验。
- 当前应用双通道故障处置：[日志降级说明](logging-degraded-mode.md)。

| 能力 | 当前源码事实 | 本次开发目标 |
| --- | --- | --- |
| `ILogger<T>` / Serilog | 已有结构化日志、Console Compact JSON、独立条数/字节有界普通与高优先级快照队列；生产容量尚未认证 | 保留；高频固定模板使用 LoggerMessage，补齐分类和字段 |
| HTTP 汇总 | B2 Summary、采样、显式请求投影入口；操作日志列表已有数值分页返回摘要 | 补齐其他受控投影与 B1 详情 |
| Auditing | B1 Operation/Exception/Outbound 微批；默认关闭的 B2 Access 入库 | 保持可靠性；修正操作结果、权限字段和页面分页，扩展受控详情 |
| Elasticsearch | 核心 Hosting 直接引用旧 Sink；开关默认关闭 | 集中日志以采集/消费层写入；核心移出旧写入依赖 |
| Fluent Bit | 已有 filesystem 缓冲示例；使用 emptyDir，筛选/字段映射有静态风险 | 实际解析、分流、持久存储和故障恢复验证 |
| 日志 Kafka | 未接入；业务 Messaging 已有独立 Kafka 能力 | 正式比较 Collector/ApplicationKafka 入口，验证后按配置选择；不复用业务 Outbox/Inbox 或 Producer 预算 |
| 容量/投递保障 | 有局部测试和历史证据，不等于全链生产认证 | 保持 `Capacity-not-verified`，按最终拓扑验证 |

不要将旧 Sink Enabled、ES 集群健康、配置文件存在或 Kafka ACK 解释为日志已成功可查询。

## 2. 所有权与可靠性

| 类别 | 所有者/路径 | 失败语义 |
| --- | --- | --- |
| B0 领域审计 | 各业务模块自有表，与业务状态同事务 | fail-closed，不进入日志队列或 Outbox |
| B1 重要 HTTP 审计 | Auditing 有界跨请求微批，调用方等待写入尝试 | 默认 fail-open + 指标/告警，不能以 Kafka 替换提交语义 |
| B2 访问、HTTP 汇总、诊断 | Hosting/可选 Access 队列/平台采集 | 可采样、可过载丢弃；Error/Critical 独立容量也不构成不可丢承诺 |
| 认证事件 | Identity 自有审计及独立查询/保留 | 沿认证事务边界；不复制到第二套认证日志表 |

默认应用队列仍为 10000/1000 条，运行时禁止 BlockWhenFull；共享停机排空预算默认 5 秒。HTTP Priority 是发射闸门，慢请求 Warning 最终仍进入普通 Serilog 队列。两队列共用标准输出不能证明下游资源完全隔离。

### 2.1 当前配置与读写边界

以下为当前源码默认值；日志字节预算是 LG02 新增配置，部署覆盖值以实际环境为准：

| 配置节 | 当前默认与作用 |
| --- | --- |
| FullNet:Logging | 普通/高优先级容量 10000/1000 条与 64/8 MiB、单条 UTF-8 JSON 16384 字节、非阻塞、停机总预算 5 秒；详细示例见降级说明 |
| Observability:HttpOperation | Enabled=true、Summary、XL；成功采样初始 1%，AlwaysRecordErrors=true，慢请求阈值 1 秒；IncludePathPrefixes=/api，排除 /health、/openapi、/scalar |
| 同上捕获/发射 | PriorityCapacity=2048、BestEffortCapacity=8192 是并发发射闸门；请求预算 2048 字节、返回预算 0、PayloadRouteAllowList 为空。列表端点已调用请求和返回分页投影，但默认配置不会产生 Payload |
| Auditing:MicroBatch | B1 共用容量 4096 条、计费 64 MiB；每批最多 64 行、计费 262144 字节、等待凑批 20ms；入队等待 100ms、停机排空 5 秒。100ms 不是请求等待数据库的总上限 |
| Auditing:AccessLogCapture | 默认关闭；开启后 B2 队列容量 8192、每批最多 100 行、凑批 100ms、退出 5 秒 |
| Auditing:Retention | 默认关闭，只在 Worker 清理；Access 30 天、Operation 365 天、Exception/Outbound 90 天，每批 200、每轮最多 15 批、轮询 3600 秒 |
| Auditing:DetailsRetention | Worker 独立运行的到期详情清理：每批 200、每轮最多 15 批；末批满额时立即继续清理积压，非满额或失败后按配置轮询（默认 60 秒）；不受普通 Retention.Enabled 关闭影响。Worker 已在成功清理与检查积压后写入共享检查点 |
| Auditing:DetailsCapture | 默认关闭且静态路由白名单为空；API 后台每 30 秒读权威检查点，请求仅读本地快照。缓存最长 90 秒、清理成功检查点最长 180 秒、最早积压滞后最多 300 秒；未知、读取失败、未来时间或超限一律拒绝资格。显式启用并列出完整静态 API 路由后，健康资格允许 B1 操作行保存固定网络上下文，默认保留 24 小时且不得超过操作摘要保留期。操作日志导出成功时可采集固定请求摘要（默认上限 2048 UTF-8 字节）；固定结果摘要默认关闭，须显式设置 `MaxResponsePayloadBytes`（上限 2048）。不读取原始请求/响应 Body |
| Auditing:Query | contains 时间窗 1 天；趋势 7 天/96 桶；导出 31 天/5000 行；不是无界全文检索 |

六档初始成功采样为 S=100%、M=25%、L=5%、XL=1%、XXL=0.1%、Ultra=0.01%；不依据瞬时在途数自动切档。5xx、已识别异常和慢请求绕过成功采样，普通 4xx 并非全部自动进入 Priority；关闭 B2 不关闭独立异常日志、Metrics 或 B0/B1。

现有 Host 审计查询按 access/operations/exceptions/outbound_calls 的精确 Read 权限控制；操作受限详情另要求 `auditing.operations.details.read`，且须同时具备普通 Read。Access 使用游标；Operation/Exception/Outbound Vue 页面已改用服务端页码、总数和筛选。三页可在详情摘要中展示列表已有的安全元数据；外呼详情不提供领域变更差异。原始 IP、端口及受控请求/返回摘要只允许经独立受限详情接口获取；该接口已进入双库运行时 OpenAPI 与生成客户端。服务端查询与 Vue API 均核对详情响应 ID 与请求 ID 一致，错配失败关闭。操作日志抽屉仅在当前会话同时具备两项权限时显示请求/返回页签，打开页签才按需读取受限详情，切换记录取消旧请求。现有 XLSX 导出覆盖 Access/Operation/Exception，新增 ContextJson 不进入列表/导出。Trace 关联跨模块由 Reader Port 获取，不跨模块 JOIN。

### 2.2 源码证据与优先缺口

- 应用日志：[ServiceDefaultsExtensions](../../src/BuildingBlocks/Full.NET.Hosting/Observability/ServiceDefaultsExtensions.cs)、[双通道 Sink](../../src/BuildingBlocks/Full.NET.Hosting/Observability/FullNetLoggingPipelineSink.cs)。默认 Info、Microsoft.AspNetCore Warning；请求侧生成有界 Compact JSON 快照，后台 Console 直接写快照（时间 @t、模板 @mt、安全异常类型 @x，Info 可省略 @l）。旧 ES Sink 启用时附带计费的安全事件拷贝，由后台保留日期格式和字典等 Serilog 语义后投递。
- HTTP：[Middleware](../../src/BuildingBlocks/Full.NET.Hosting/Observability/HttpOperationLogMiddleware.cs)、[Options](../../src/BuildingBlocks/Full.NET.Hosting/Observability/HttpOperationLogOptions.cs)、[Emitter](../../src/BuildingBlocks/Full.NET.Hosting/Observability/HttpOperationLogEmitter.cs)。B2 已区分 `Outcome=Exception/HttpError/HttpCompleted`：异常映射后的原始路由和异常事实可保留，`HttpCompleted` 只表示 HTTP 结束，不证明业务事务成功。LG02 部分字段已由静态 Endpoint 与受信连接生成；无 Activity 时 `TraceId` 留空、`RequestId` 单列，B2 `url` 只输出路由模板，来源站点与认证 ClientId 仅保留指纹，HTTP Method、UA 和语言只输出受控类别。普通事件现有默认 `diagnostic` 分类与管道生成的 UUID v7 `LogEventId`。进程启动时新增一次 Resource 记录，普通事件仅关联受控 `Instance`；Serilog 解构输入、顶层字符串和整事件快照已有局部限长/字节预算。B2 同步热路径已读取进程内诊断策略快照并按端点、诊断组和可信租户匹配采样；Trace 定向规则仅匹配本地生成且父链没有远端来源的 Activity，入站 traceparent/父 ID 不能扩大采样。过期规则在使用时失效，请求内已缓存的决定在关联规则到期时重算。实例级 BestEffort 容量仅接受全局 HTTP 类别/组规则，忽略租户、端点、Trace 容量覆盖。权威回源失败时进程内快照撤销临时规则并回退安全默认；API 节点启动时及每 30 秒后台直接读取权威配置，跨实例更新或恢复默认在下一轮成功读取后收敛；传播耗时还包括数据库读取与串行等待，不承诺严格 30 秒。轮询不触发缓存/Backplane 失效，策略快照 accessor 与普通请求日志热路径均不回源；管理端详情接口仍查询配置行取得版本。诊断策略键已从通用 Host 配置项的列表、详情和写入入口排除，需使用专用权限接口管理；普通字段秘密处理仍待后续切片。
- B1：[Operation 中间件](../../src/Modules/Full.NET.Modules.Auditing/Middleware/OperationLogMiddleware.cs)、[微批配置](../../src/Modules/Full.NET.Modules.Auditing/Features/WriteAuditBatch/AuditMicroBatchOptions.cs)。LG01 已开始：端点单一要求权限由授权 metadata 提取，复合要求不再伪写第一条 Claim，完整集合目前仅进入内存写入模型；操作摘要的最终状态改由异常映射外层的协调器刷新，异常事实独立标为失败。微批信封对每个字段取 UTF-8/UTF-16 较高字节估算并加基础费用；单条超过 MaxBatchBytes 时 fail-open 拒绝，相邻两条不能合批越过上限，停机延后项也要完成或明确失败。新增 `QueueMaxBytes` 对等待入队、Channel 内及整批写库中的信封合计计费；超过条数或字节容量均 fail-open，热更新收紧字节上限时仅拒绝新预留，不丢弃已接受记录。计费不包含生产者在预留前临时构造的模型、SQL/驱动缓冲和 CLR 实际 RSS，必须用容量与负载试验核对。取消及响应已开始场景已有 Unit 验证，仍须完成真实 Host.Api 与双库验收，不能据局部 Unit 宣称 LG01/LG04 全部完成。
- LG04 详情存储增量：双库 239 迁移增加可空 `ContextJson`、`DetailsExpiresAtUtc`、到期索引和 Auditing 自有的单行清理检查点表，不回填旧行；Worker 已独立按有界批次清空到期详情并保留摘要，成功读取剩余过期积压后刷新共享检查点，不受普通 Retention.Enabled 控制。API 已接入后台检查点读取及失败关闭的本地资格缓存，请求不查库。B1 操作写入现已将固定网络上下文与首次捕获确定的绝对到期时间同批写入；只有显式开启、完整静态路由白名单命中、检查点健康且保留期合规时才构造，超限详情降级为摘要。操作日志导出成功路径已增加仅含时间范围/状态码/成功筛选的请求摘要，以及仅含行数/截断/敏感列标记的可选结果摘要；不读取筛选文本、文件名和文件字节，其他端点仍只采固定网络上下文。资格在请求捕获时判断，已获准并排队的行可能在热配置关闭后完成写入；停用不是对在途行的同步撤销。默认配置保持零详情写入；列表和旧详情 API 不读取 ContextJson。独立的 `GET /api/v1/auditing/operation-logs/{operationLogId}/details` 同时校验普通 Read 与受限详情 Read，只读取未到期固定版本上下文，到期及非法内容返回 404，不返回原始 JSON。迁移恢复与双库清理/检查点/HTTP 权限断言已编写，真实双库运行尚待 Actions。
- 采集：[Fluent Bit 候选配置](../../deploy/observability/fluent-bit-values.yaml)。现已改为单 Tail、Pod 标签准入、必需封套字段筛选和缺分类的普通 ILogger 默认 BestEffort；`@t`、CRI/Docker 解析、字段筛选、优先级分流与脱敏键仍须固定版真实样本验证，emptyDir、重试和输出确认仍须故障验证，不以静态配置测试代替真实交付。
- 安全：[ExceptionHandler](../../src/BuildingBlocks/Full.NET.Hosting/Api/FullNetExceptionHandler.cs)。当前快照的 @x 只保留异常类型，不写原始消息/栈；已知敏感键与显式赋值格式会在入队前替换，任意未标记秘密仍无法自动识别，详见 §2.3。

### 2.3 当前快照预算与待验证边界

HTTP Payload 上限不能替代整个 ILogger 管道的保护。所有应用日志，包括普通字符串、解构集合和异常，均须受下列初始预算约束；这些是开发起始值，不是生产容量认证：

| 当前配置/约束 | 默认值 | 计量范围 |
| --- | ---: | --- |
| MaxEventBytes | 16384 字节 | 整条输出 JSON 的 UTF-8，含模板、属性、资源和安全异常摘要 |
| GeneralQueueMaxBytes | 67108864 字节 | 普通通道待消费与 Sink 在途快照的计费容量 |
| HighPriorityQueueMaxBytes | 8388608 字节 | 高优先级独立计费容量，不借用普通通道 |
| 属性/对象深度/集合项 | 64 / 4 / 16 | 消息属性及嵌套诊断结构，遍历须提前停止 |
| 字符串上限 | 2048 字符 | 普通标量与消息文本；UA、语言等使用更小的专属上限 |

两通道同时检查条数和字节预算，达到任一上限即非阻塞拒绝，并以固定通道标签记录丢弃、超大事件和字节预算拒绝。全局 Serilog 解构限制在事件构造阶段生效；入队前生成脱离原始异常/业务对象引用的 UTF-8 LogEnvelope。默认 Console 路线的队列仅持有快照字节；旧 ES Sink 启用时封套另持有限深、限量的安全事件拷贝并计费，不保留原始业务对象或异常。文件出口仍属于后续配置目标。

当前字节计费为 UTF-8 数组长度加每事件 128 字节保守封套开销；旧 ES 开启时另按安全事件的有界对象图估算费用。构造前先预留单事件最大费用，成功后退还差额；出列进入 Sink 后仍预留，到 Sink 调用结束才释放。构造期间的临时字符串/对象图、实际 CLR/GC 开销和外部 SDK/native 缓冲仍非精确计费，必须通过 LG02/LG08 的分配、RSS 和在途量实测核对；此预算不是进程总内存上限。格式化写入受单事件预算约束；超限先移除诊断属性，关键属性仍不能容纳则拒绝。LG03 的请求/返回投影 JSON 必须整块保留或移除，不按普通字符串裁成无效 JSON。

预算预留、释放已覆盖入队拒绝、正常消费、失败和退出时放弃待消费事件；阻塞 Sink 未返回不会回收其快照。并发/真实宿主故障和性能场景仍须验证，调用方预先构造的参数、GC 和兼容适配额外分配仍需单独测量。热路径继续使用 LoggerMessage/IsEnabled，不能为精确计费先无限格式化再截断。

普通结构化日志的入队快照现对已知敏感属性名、嵌套成员与字典键做整值替换；原始 IP、端口、地址、请求/返回 Body、Header、Query 等已知受限详情字段在顶层及嵌套层移除。HTTP Operation 的可选属性只接受中间件固定摘要字段名单，其他属性不复制；名单中的受控 RequestPayload/ResponsePayload 仍须经过两阶段投影许可，单靠属性名不能证明来源可信。自由文本中出现 Bearer/Basic、密码、Token、API Key、私钥、连接串或签名等明确赋值格式（包括 JSON、带点或索引式字段名）时整块替换，危险属性名和类型标签也不保留原文。模板本身含敏感语境时，通用占位符值一并丢弃；只有未被模板引用且通过固定值或格式校验的分类、流、可靠性、Trace/Span ID、状态码及 LogEventId 可以保留。模板过长或 token 过多时只保留经校验的关联/路由元数据，尤其保持已生成的 LogEventId，不复制任意属性。普通可选属性最多扫描 256 个，先按固定键取必要属性，避免大量无效属性拖长请求线程。Console 快照与旧 ES 兼容事件使用同一受限副本，出口字节负例已覆盖已知字段；这属于已知格式防护，无法识别没有标记的任意秘密，也不能替代调用方禁止记录凭据的约束。每条字符串先受既有长度上限，再做匹配；新增扫描成本仍需与请求 P99/分配基线比较。

`RequestPayload`/`ResponsePayload` 的封套出口现额外校验当前分页投影的精确数值 JSON 结构、范围和 2048 字节上限；其他同名属性及嵌套副本移除。自由文本若含已知受限详情键的赋值形态，包括 JSON 字符串或模板字面量，会整块移除。该检查阻止这些已知形态进入输出，但不证明调用者取得了投影许可，来源和预算仍由两阶段入口负责。新增解析只在字段实际出现时运行；启用后的 P99/分配成本仍待测量。

普通 `diagnostic` 等非 HTTP Operation 事件不复制 `http.route`、`url`、`Route`、`DiagnosticGroup` 等 HTTP 专属字段别名，避免以其他分类携带原始路径。HTTP Middleware 现经宿主内类型化入口提交固定摘要到同一双有界后台队列；快照器仅从该入口的值快照读取 HTTP 专属字段。普通 `ILogger` 事件即使手工伪造 `log.class=http.operation`，也会降级为无 HTTP 详情的诊断事件。此边界防止靠可重放的 Scope/模板冒充 HTTP 来源；它不改变已采集摘要字段自身的脱敏和目的地许可要求，仍须通过真实宿主与负载测试确认开销。

HTTP Middleware 的构造现要求 `HttpOperationLogIngress`；定制宿主必须将同一入口实例接入日志管道，漏注册时 DI 激活失败。已注册但日志管道尚未挂接或正在退出时，入口拒绝发射并计 `missing_ingress` 跳过，不能算作 B2 成功发射。

本地组合测试已让中间件与普通 `ILogger` 共用同一个 Serilog 双通道管道，并检查入队快照 UTF-8 字节及旧 ES 兼容 Sink 收到的安全事件拷贝：中间件输出静态路由模板，实际路径段不出现；普通日志伪造 HTTP 分类时，伪造路由与同名载荷也不出现。此证据尚未覆盖实际 Console、采集器、旧 ES 网络投递、OTLP 或 Kafka 的逐跳出口字节与负载。

类型化 HTTP 摘要按中间件给出的 `reliability.class` 分流：慢请求即使只有 Warning 级别，标为 `Priority` 时也进入高优先队列。默认 `AlwaysRecordErrors=true` 使 5xx/异常进入 Priority；显式设为 `false` 且请求未达到慢请求阈值时，错误摘要按成功采样与 BestEffort 容量规则进入普通队列，即使日志级别仍为 Error。普通 `ILogger` 的 Error 事件仍按级别进入高优先队列，不能借伪造分类占用 HTTP 专用入口。

本地阻塞 Sink 故障注入已验证：General 消费者被阻塞且队列开始丢弃时，类型化慢请求仍由独立 Priority Sink 接收，请求不会等待 General 队列空位。该单实例短时测试不代表持续故障、优先队列满载或生产 P99/吞吐认证。

类型化入口现区分未挂接、宿主内存队列接受和有界队列拒绝。`fullnet.http_operation_log.emitted` 只统计已入宿主队列的 B2 摘要；Priority/BestEffort 队列拒绝分别计入 `fullnet.http_operation_log.dropped` 的固定 `priority_queue`/`best_effort_queue` 标签，底层通道丢弃指标仍单独记录。这里的“已入队”不代表 Console、采集器、Broker 或 ES 已持久接收；崩溃与下游故障仍可能丢失 B2。

最大事件字节数的等值边界已加入回归测试：快照恰好占满预留额度时不调用 `Release(0)`，可正常入队并在消费后释放费用。超限、队列满和关闭竞态仍走拒绝与计数路径。

### 2.4 有界双通道机制与性能边界

当前每个宿主进程各有普通和高优先级两个 `BlockingCollection<LogEnvelope>`，底层为 `ConcurrentQueue<LogEnvelope>`，分别由一个专属后台线程消费。低于 Error 的事件进入普通队列，Error/Fatal 进入高优先级；这不是整个集群共用一个队列，也不是 10000 个并发请求的容量声明。HTTP 发射闸门、B1 审计微批和这里的 Serilog 队列是不同机制。

生产者先非阻塞预留最大封套费用，再生成有界快照、退还差额并使用 `TryAdd`；容量满时拒绝当前事件、归还预留并增加丢弃计数，不等待空位。消费者逐条写 Console 或兼容 Sink，这一层不自行凑批，下游 SDK 是否另有批量必须分别核对。非阻塞只表示不为队列空位/下游交付主动等待，不表示零分配、无同步竞争或严格无等待算法。事件参数计算、模板绑定、Scope、解构和 Compact JSON 快照格式化都在调用线程，后台 Console 不重复序列化。

独立队列和线程隔离普通日志对错误日志缓冲容量的占用，但两路最终共用 stdout，仍受共享输出吞吐和节点资源影响，不提供严格抢占或错误日志不可丢承诺。可按下列路径判断瓶颈，不凭“异步”认定请求零成本：

| 环节 | 可能限制 | 首先需要观察的表现 |
| --- | --- | --- |
| 调用线程 | 元数据/投影提取、脱敏、对象解构、事件构造及 GC | 请求 P99、CPU、分配和日志调用耗时 |
| 并发入队 | 容量检查、预算预留与内部同步竞争 | 入队耗时、拒绝原因和并发争用 |
| 后台消费 | 逐条输出、stdout 或兼容 Sink 重建/发送速度 | 深度、在途量、最老事件年龄及丢弃 |
| 共用节点资源 | CPU、运行时日志文件、磁盘和网络竞争 | 请求延迟与采集积压同时升高 |

当前 LogEnvelope 已在入队前完成有界 UTF-8 快照，代价是序列化进入调用线程。LG02/LG08 必须与此前后台格式化基线同环境比较请求尾延迟和分配；只有结果达标才能确认这一取舍适合生产。Console 消费已有快照；旧 ES 开启时携带安全事件拷贝，其额外成本需纳入比较。没有证据前不以替换队列容器、增加线程或增大缓冲作为吞吐结论；不得为优化取消字节/对象边界。

对单个通道，设日志输入速率为 λ、持续处理速率为 μ，剩余条数容量为 Q。当 λ > μ 时，理想化填满时间约为 `Q / (λ - μ)`；字节预算可能更早触顶。假设空普通队列 Q=10000，输入 20000 条/秒、处理 15000 条/秒，则约 2 秒填满。这只是说明机制的算例，不是项目实测。扩大队列延长缓冲时间，不增加持续处理能力，且可能增加内存、日志延迟和停机排空成本。

### 2.5 采集器机制、部署与过载

目标 Fluent Bit 作为独立进程/容器，默认优先评估节点 DaemonSet：读取该节点容器运行时生成的日志文件，解析/分类一次后分流，使用有限内存和磁盘缓冲、批量发送与重试。应用请求不需要同步调用采集器；Logstash 则是目标 Kafka 路线中的消费写入端，不是同一个组件。Sidecar 仅在实例隔离确有需求时比较其资源与运维成本，不在本轮变更部署方式。

节点采集器容量按该节点所有实例日志的总事件/字节速率核算。JSON 解析、过滤、压缩/TLS、文件数量、日志读取及缓冲读写、网络和目的地速度都可能限流；不能假设默认线程模型充分利用所有 CPU。通常先表现为采集延迟和缓冲增长，但共享节点 CPU/磁盘/网络仍可能间接推高业务 P99；stdout 变慢还可能使应用后台队列积压并丢弃。

旧示例曾有两个 Tail 输入读取同一批 `*fullnet*.log` 文件；当前候选改为单 Tail 加重标记分类，但尚未通过固定版采集器真实测试。CPU request=100m、limit=500m 不能作为高并发容量依据。`emptyDir: 2Gi` 是本地临时缓冲，且不等于每个输出都可独占 2Gi；输出另有队列限额，归档缓冲也需计费。这些是当前配置事实和待验证风险，不是已观测到的生产瓶颈。LG06 仍须按节点总量验证资源、解析正确性和输出隔离。

下游停止/变慢时，文件系统缓冲不是无限等待区：达到内存阈值是否暂停输入取决于缓冲模式及设置；达到输出 `storage.total_limit_size` 时 Fluent Bit 会丢弃该逻辑输出队列最旧的 chunk。暂停/落后期间源文件若被轮转清理，仍可能出现缺口；重试次数耗尽和节点失效也须分别对账。目标平台必须显式选择并验证满盘/满队列策略，不能把“有磁盘缓冲”写成绝不丢失。

设有效剩余磁盘预算为 B、输入/发送速度分别为 Rin/Rout（字节/秒），Rin > Rout 时，理想化可缓冲时间为 `B / (Rin - Rout)`；下游停止时为 `B / Rin`。例如 20GiB 缓冲、20MiB/秒输入、下游停止，仅约 17 分钟，尚未计入封装、文件和安全余量。恢复发送能力必须超过持续输入才能消除积压；事件条数、平均/最大字节及保留时间要同时核算。

Kafka 只缓冲已成功交付到 Broker 的记录，不能补救应用入队前、stdout 或采集器阶段的缺口，也不能消除请求端构造成本或 ES 长期处理不足。是否启用依集中缓冲、故障窗口、多消费者和实际容量证据决定；保留双通道和进程外采集方向不代表所有部署必须启用 Kafka。

## 3. 目标拓扑与配置

```text
ILogger -> Serilog -> 有界双通道/字节与在途预算
 -> Local: Console/可选滚动文件
 -> Collector: Console/可选文件 -> Fluent Bit（规范化、分类、可恢复缓冲）
    -> Direct: 查询/归档存储
    -> Kafka: 日志专用 Topic -> 独立消费写入端 -> 查询/归档存储
 -> ApplicationKafka: 后台 Kafka Producer -> 同类日志专用 Topic
    -> 独立消费写入端 -> 查询/归档存储

已纳入 B1 的请求 -> Restricted 安全详情 -> Auditing 数据库（独立权限/到期）

OTel Trace/Metrics -> Collector -> 相应后端（不重复导出同一份日志）
```

| 部署档 | 应用 DeliveryMode | 采集器 | Kafka | Elasticsearch | 启动/降级边界 |
| --- | --- | --- | --- | --- | --- |
| Local | Local | 关闭该日志流采集 | 关闭 | 关闭 | 无远程日志依赖 |
| CentralDirect | Collector | 开启 | 关闭 | 可选开启 | ES 故障在采集层缓冲；不反向等待业务请求 |
| CentralKafka | Collector | 开启 | 开启 | 独立可选 | Broker 故障缓冲；采集/消费积压告警 |
| ApplicationKafka | ApplicationKafka | 不采集同一应用日志流 | 开启 | 独立可选 | 仅适配已准入的构建可选；Broker 故障时有界拒绝/超时 |

两路线均为待实现、待验证能力；采集器路线保留为参考默认，后台直发成为正式简化候选。部署先比较同环境结果，再从已准入路线中配置选择，不根据瞬时负载自动切换。Kafka 档的 ES 可关闭，但必须有其他有效消费目的地。

应用 `FullNet:Logging` 管理 DeliveryMode、等级、条数/字节预算、Console/File 与退出预算；HTTP 捕获和 Auditing 详情边界不变。平台 `logPipeline.ingress` 区分 Collector/ApplicationKafka，`transport` 的 Direct/Kafka 仅表示是否经过 Broker，Direct 不表示应用直发。发布清单将同一档位渲染为 Pod 的不可变路由标识与应用预期模式；应用只校验自身配置和注入预期值，不能在启动时核实采集器真实规则。发布模板校验加固定版本采集器的行为测试证明端到端匹配。Collector 档应用不持有平台凭据；ApplicationKafka 档只注入日志 Producer 最小写权限的连接/TLS/认证配置，不获得消费、ES 或集群管理凭据。

下列为**计划配置，不是当前可直接使用的配置**：

```yaml
logPipeline:
  enabled: true
  ingress: Collector # Collector | ApplicationKafka
  transport: Kafka # Direct | Kafka
  collector:
    enabled: true
    diskBuffer:
      enabled: true
      persistence: persistentVolume # nodeLocal 必须披露节点丢失风险
      capacity: 20Gi # 示例；实际按字节速率与故障时长核算
  kafka:
    enabled: true
    connectionSecretRef: fullnet-log-kafka
    generalTopic: fullnet.logs.general.v1
    priorityTopic: fullnet.logs.priority.v1
  elasticsearch:
    enabled: true
    connectionSecretRef: fullnet-log-elasticsearch
    indexStrategy: EventDate # 固定事件日期索引；不使用 rollover/data stream
    indexRouteVersion: v1
  consumer:
    implementation: Logstash
    ackBoundary: PersistentQueue # SinkConfirmed 只对准入消费者开放
    persistentQueue:
      enabled: true
      maxBytesPerPipeline: 1073741824 # 示例；总容量还须包含所有流水线及 DLQ
      checkpointWrites: 1 # 持久化优先起点，吞吐必须实测
  archive:
    enabled: false
```

上例为 Collector/CentralKafka。ApplicationKafka 档的应用计划配置为 `FullNet:Logging:DeliveryMode=ApplicationKafka`，平台改为 `ingress: ApplicationKafka`、`transport: Kafka`、`collector.enabled: false`；Kafka 仍启用，单独向具备适配能力的应用注入 Producer Secret。采集器可采集其他工作负载，但必须排除同一应用日志流。每个 Pod 按其发布时的路由标识独立筛选；旧版 Collector Pod 与新版 ApplicationKafka Pod 混合存在时仍应各走一条入口。当前 Fluent Bit 候选已添加标签准入，但固定版真实路由、元数据缺失、滚动发布和 Console 镜像负例未取得 CI 证据前，不开启直发。具体 Kafka Options/适配程序集和部署注入键由 LG00/LG05 固定，不假设运行时能动态加载客户端。

旧配置迁移优先级须先落地并测试：

| 新 DeliveryMode | 旧 Elasticsearch.Enabled | 生效行为/处理 |
| --- | --- | --- |
| 未指定 | false/未指定 | 保持既有 Console 与已有平台采集行为；显示 `legacy-console` 状态，不暗示 Local 零远程 |
| 未指定 | true | 保留旧 ES 直写并告警，显示 `legacy-direct`；迁移完成前不删除依赖 |
| 显式 Local | false | 应用内仅写 Console；部署采集器必须排除该流，当前尚未验证远程零投递 |
| 显式 Collector | false | 应用内仅写 Console；实际采集入口、路由和目的地须由平台交付并验证 |
| 显式 ApplicationKafka | false | 当前产物缺少静态适配器，启动拒绝；适配器与平台单一入口通过准入后才可启用 |
| 任意显式模式 | true | 启动拒绝，要求先迁移或关闭旧 Sink；不能静默忽略或同时投递 |

当前 LG05 首批代码已加入强类型 `FullNet:Logging:DeliveryMode` 与启动校验：未设置时保持旧 Console/ES 分支，旧 ES 直写会在宿主启动时输出一次固定迁移告警；显式模式与旧 ES 开启冲突时拒绝启动。显式模式还要求同值的 `FullNet:Logging:ExpectedDeliveryMode`，后者由发布清单注入；缺少、孤立或不一致时拒绝启动。注册服务与构建 Serilog 之间若切换任一模式值或旧 ES 开关也拒绝启动，避免校验、告警和 Sink 分叉；运行中切换仍需重启。应用的同值校验只覆盖本地配置，不能证明平台采集器行为。

Helm Chart 的 `logging.ingress` 默认 `Legacy`，不注入两个新模式键，保留旧行为；非生产预览选择 `Collector` 时，同一值渲染成 API/Worker Pod 的 `fullnet.io/log-ingress=collector` 标签以及 ConfigMap 的 `DeliveryMode`、`ExpectedDeliveryMode`。当前 Chart 拒绝 `Local`（尚未验证采集器排除规则）、`ApplicationKafka`（缺少静态适配器）以及**生产** `Collector`（尚无已验证的采集路由）。显式 `Local`/`Collector` 在应用内均继续写现有 Console 双通道，旧 ES Sink 不启用。Fluent Bit 候选已改为单 Tail 和标签准入，但静态配置与非生产 Helm 渲染不证明单一入口或最终投递。固定版采集器的真实路由、缺失元数据失败关闭、混合滚动 Pod 和输出故障验证通过后，才可解除生产门禁；Kafka 适配器仍属于后续 LG05/LG06 工作。

现有受 `observability.elasticsearch.read` 保护的 Elasticsearch 管道健康响应已追加 `deliveryStatus` 与 `deliveryConfirmationBoundary`，不改变原字段和路由。状态从宿主启动时冻结的配置计算：未指定新模式且旧 ES 关闭为 `legacy-console`，旧 ES 开启为 `legacy-direct`，显式 Local 为 `disabled`，Collector 为 `external-collector`；`application-kafka` 只预留机器码，当前产物会在启动时拒绝该档。确认边界当前只有 `configuration-only`，或旧 ES Sink 确实注册时的 `sink-registered-only`，两者都不表示远端已收到、持久化或可查询。ES 关闭时管理查询不连接 ES；Vue 在非兼容模式只显示入口状态和确认边界，不把旧版 ES Sink 未注册、索引或集群诊断呈现为当前路线故障。旧版服务未提供新增字段时，Vue 保留兼容诊断并显示“旧版服务未提供”。运行时管理状态只反映当前宿主，不聚合所有 Pod，也不替代平台采集器与消费端验证。

关闭组件不创建客户端、不连接、不探测，不暴露秘密。显式新模式与旧直写冲突、发布预期模式不匹配、ApplicationKafka 配 Direct、缺少静态适配或必需配置、Kafka 传输但 kafka.enabled=false、集中档没有目的地、缓冲无容量等组合须拒绝。日志与业务 Kafka 使用独立配置、Producer、ACL、Topic、配额和消费组；不自动启用 Messaging HybridKafka/CDC。当前宿主已移除 `ReadFrom.Services` 根级隐式 Sink 注册；未来新增第三方出口必须显式进入同一管道并核验单一所有权。

核心不强制引用 Kafka/ES 写入客户端；现有业务 Kafka 仍归 Messaging。ApplicationKafka 由可选静态适配提供，LG00 明确 Host.Api/Worker 的真实消费者、依赖方向、构建组合与隔离收益后再确定程序集/项目，不反射加载或提前制造空抽象。缺少能力的产物不能仅靠配置开启。关闭不创建 Producer，但不等于已引用的托管/native 包退出发布闭包；分别验证默认产物和直发产物的维护、许可、体积、裁剪及 Native AOT。

平台 Kafka 消费写入端不复用业务 Worker，当前准入基线为 Logstash/PersistentQueue。平台拥有独立部署、凭据、Consumer Group、CPU/线程/连接/批量预算，普通/优先及查询/归档分开容量。该基线使用可恢复独占卷，不共写或删除非空 PQ。LG00 可比较 SinkConfirmed 简化路线，但只有实际消费者证明逐项存储/可靠隔离完成后推进连续 Offset，才允许省去重复 PQ；固定实现、版本、摘要及许可后开放对应 Kafka 档。

### 3.1 后台直发的处理与所有权

候选路径保留双通道和统一快照预算，后台向长生命周期日志专用 Producer 连续提交；以 DeliveryReport 或有界在途 ProduceAsync 跟踪，不逐条串行 await ACK、每事件 Flush 或无界 Task.WhenAll。客户端批量、压缩、消息/字节与在途上限独立设置。分别计量应用缓冲与 SDK/native 副本，定义所有权转移、QueueFull、回调/超时、失败、停机及释放时点，不能只统计应用 Count。

可选适配项目现已定义 `FullNet:Logging:Kafka` 的强类型 Producer 配置和独立应用待确认双维预算。配置分别指定普通/优先 Topic、TLS/SASL、SDK 队列条数/KiB、单消息尺寸与交付/停机超时；`acks=all`、幂等和至多 5 个在途请求为不可降低的基线。单通道原型在 SDK 入队前预约，按投递回调或同步失败释放，并区分 SDK `QueueFull` 与应用预算耗尽；事件 ID 的 key 字节也计入容量和单消息尺寸。双通道组合器分别持有普通和优先 Producer/队列，先停止两路提交，再以同一截止时间分配 Flush 剩余预算。Hosting 的最小快照契约只向可选出口提供安全 UTF-8、同一 JSON 中的管道事件 ID 和优先级；缺少可信 ID 时拒绝外发。Host.Api/Worker 尚未引用该适配、绑定该节或创建 Producer，`ApplicationKafka` 仍在启动时拒绝；假客户端测试不能证明真实 Broker ACK、SDK/native 峰值或包括 native Dispose 的总停机耗时。

Producer 本地接收不等于 Broker 确认。普通/优先 Topic 不自动隔离同一 Producer 的共享队列；必须验证独立 Producer 预算或可证明的独立预留与调度，普通洪峰不能用尽错误日志发送容量。失败仅使用安全指标/独立诊断，不递归写回同一 Kafka Sink。

直发默认无应用磁盘 Spool，进程崩溃可能丢失未获 Broker 确认的记录；Broker 不可用时在有限预算内重试后拒绝/超时，至少一次从已验证 Broker 确认边界起算。若要求 Broker 故障期间跨应用重启恢复，优先选已验证 Collector 持久缓冲，不隐式增加应用 Spool 或日志 Outbox。

同一 LogEventId 同一时段只由一个入口负责送入日志 Kafka；直发模式的 Console/File 本地诊断不再被 Collector 转发同一事件。采集器以 Kubernetes Pod 路由标签/注解筛选 Collector 流，元数据缺失或不匹配必须失败关闭，不能退回当前容器文件名通配发送。模式切换走受控发布：旧 Pod 继续旧路由直至停止接收并限时排空，新 Pod 才用新路由；记录残留，保留原 ID/时间/路由，并按采集器真实输出及 Broker ID 对账。不默认双写或自动故障切路，也不承诺跨进程切换无损；恢复时对账重复与缺口。

### 3.2 两路线比较与配置准入

比较固定相同 Release/硬件、Schema、事件大小分布、采样/投影与安全策略、Kafka 分区/acks/副本/压缩，以及相同下游消费者和确认边界。先只改变应用到 Broker 的入口，再单独评估 PQ/SinkConfirmed；不以降低可靠性或取消脱敏换来的吞吐判定路线胜出。

| 对比项 | Collector | ApplicationKafka |
| --- | --- | --- |
| 路径代价 | stdout/文件、读取和再次解析，成本分布于应用与节点 | 省去文件采集，应用承担客户端/序列化/网络 |
| 必报性能 | 请求 P99、每实例事件/字节吞吐、节点汇总、采集延迟 | 请求 P99、每实例事件/字节吞吐、提交/确认延迟 |
| 资源与过载 | 应用+采集器 CPU/内存/磁盘/网络、缓冲与丢弃 | 应用+SDK CPU/托管及 native 内存/网络、缓冲与丢弃/超时 |
| 恢复 | 源文件/卷、轮转、进程/Pod/节点故障及 ACK | 应用/Producer 崩溃、ACK 前后、Broker 故障及停机；披露无 Spool 窗口 |
| 适用判断 | 多语言统一采集、应用外缓冲或依赖隔离收益明确 | 精简收益满足请求/资源预算，且依赖和损失窗口可接受 |

LG00 定义可接受的 P99 增量、资源、丢弃/缺口预算及恢复目标；LG08 提供对比证据和适用范围后批准部署选择。允许两种已准入模式用于不同部署，不要求每个环境启用全部组件。不得以 ConcurrentQueue 微基准或 Kafka 集群总吞吐外推单宿主能力；未满足目标的模式继续关闭，生产容量未测保持 Capacity-not-verified。

## 4. 截图字段采集清单

以下字段均为目标；现有少量同名字段不表示完整覆盖。目标逻辑字段采用稳定 PascalCase，采集层显式映射到 ECS。现有输出含 log.class、reliability.class、http.method 等带点键；字段格式升级需先让采集端支持旧/新 SchemaVersion，再切换发射，不直接改名导致旧路由漏采，也不维护含义不一致的别名。

| 截图/关联项 | 目标机器字段 | 来源及采集边界 |
| --- | --- | --- |
| 控制器 | Controller | MVC 受信 Endpoint metadata；Minimal API 不伪造控制器，显示“不适用” |
| 操作名 | Action / EndpointName | MVC Action 或稳定 WithName；不反射扫描方法，不使用动态 URL 当名称 |
| 显示名 | EndpointDisplayName / DisplayNameKey | 静态元数据与本地化键；UI 可翻译，业务不依赖译文 |
| 路由/Area | RouteTemplate / Area | Endpoint 路由模板与可用 metadata；Area 缺失标记不适用 |
| 方法 | HttpMethod | 请求行属于不可信输入；B2 只输出 GET/HEAD/POST/PUT/PATCH/DELETE/OPTIONS/TRACE/CONNECT 固定类别，其他一律 OTHER |
| 地址 | Scheme / Host / RequestPath / Url | 当前 B2 `url` 只输出路由模板、未匹配端点输出固定占位符；受控 Host 与路径/Query 详情仍待目的地许可，禁止任意查询值或路径中的秘密 |
| 协议 | HttpProtocol | Request.Protocol，例如 HTTP/1.1、HTTP/2 |
| 来源 | SourceOriginFingerprint | Origin/Referer 先校验 HTTP(S) 站点、去除凭据/路径/Query/Fragment，再将站点 SHA-256 指纹写入 B2；不输出请求方可填写的原始主机，不用于认证 |
| 客户端 | ClientKind / ClientIdFingerprint | 仅认证 Principal 的 ClientId 可以生成 SHA-256 指纹；不保存明文 ClientId，不凭任意 Header 推断可信身份；ClientKind 为静态类别 |
| 浏览器 | UserAgentSummary | UA 是不可信文本；B2 只输出 Edge/Firefox/Chrome/Safari/curl/Other 固定类别，不保存原文或任意子串 |
| 语言 | Culture / AcceptLanguageSummary | 仅输出资源目录支持的规范语言标签；请求 Header 的首个标签不受支持或异常时留空，不保存任意 Header 文本 |
| 客户端 IP/端口 | ClientIpFingerprint / ClientIp / ClientPort | 仅规范化 Connection 信息；IP 指纹默认，原 IP/端口为默认关闭的 Restricted 详情，只进 B1 审计库，独立权限/短保留；可用时采集，不推测缺失值 |
| 服务端 IP/端口 | ServerAddress / ServerPort | Connection.LocalIpAddress/LocalPort 可用时采集；内部拓扑属于 Restricted，只进受保护 B1 详情，不作为指标标签 |
| TraceID | TraceId / SpanId / RequestId | Activity.TraceId/SpanId；TraceIdentifier 独立保存为 RequestId；完整 traceparent 不是 TraceId |
| 线程 ID | CaptureThreadId | 仅可选诊断捕获点的托管线程号；异步请求跨线程，禁止称为请求全生命周期线程，不作关联键 |
| 耗时 | ElapsedMs | Stopwatch；区分 Endpoint/HTTP 总耗时，汇总在最终响应状态可知后记录 |
| 请求参数页签 | RequestSummary / RequestCaptureState | 先取得采集许可，再执行 Endpoint RequestProjection；默认 Restricted 详情只进审计库，B2 仅允许另行审定的 Internal 摘要；不读原始 Body/Header |
| 返回内容页签 | ResponseSummary / ResponseCaptureState | 先许可再在结果映射边界执行 ResponseProjection；Restricted 详情只进审计库，B2 可用安全结果码；不复制响应流 |
| 用户/租户/权限 | UserId / TenantId / RequiredPermissions | 可信上下文和 Endpoint 权限 metadata；不能取第一个 Permission Claim 冒充实际要求 |
| 结果 | StatusCode / Outcome / BusinessResultCode / ExceptionType | HTTP 最终状态与安全机器码；异常不允许记为成功；业务成功不能仅从 2xx 推断 |
| 系统/框架/环境 | Application / Instance / HostRole / Environment / RuntimeVersion / OSDescription / FrameworkVersion | 进程启动时记录一次 Resource 元数据；逐条只携稳定资源引用，不每请求重复大块系统信息 |
| 唯一记录/分类 | LogEventId / SchemaVersion / OccurredAtUtc / ExpiresAtUtc / IndexRouteVersion / Level / LogClass / LogStream / ReliabilityClass / DataClassification / EventId / EventName / DiagnosticGroup / SourceContext | LogEventId 在采集前生成；事件时间、绝对到期与索引路由版本一同冻结，重试/重放不变；EventId 只表示类型 |

线程、原 IP、服务端地址、Payload 采集不得为了“字段齐全”绕过数据和容量边界。截图未圈中的明文 Cookie/Token 只作为禁止采集的反例；不复制截图中的值或 Admin.NET.Pro 代码/资源。

## 5. 请求/返回投影与详情存储

生产默认 Summary；SanitizedPayload 必须同时满足模式、Endpoint 白名单、字段白名单、采集目的地与预算。白名单不存在、解析失败、预算耗尽时只降为摘要，不能影响业务响应。投影注册表静态指定数据分类，调用方不能把 Restricted 参数标成 Internal 来绕过出口。

旧版 `CaptureRequestJson(HttpContext, string)` 已停用：为保留调用签名，它只清除旧 Items 值，不留存或发射原始 JSON。旧调用方可能在进入该方法前仍已构造原文，须迁移为先许可后执行工厂的静态投影。`ProjectJsonPayload` 保留为兼容辅助函数，不作为普通日志写入入口；字段名白名单不能证明字段值没有凭据。

已注册 B2 两阶段捕获的静态投影 `http.pagination.v1`，仅含经业务验证的数值 `page`/`pageSize`；`http.pagination.result.v1` 仅含 `page`/`pageSize`/`totalCount`/`itemCount`。它们先检查开关、完整静态 Endpoint 路由白名单、Middleware 的路径包含/排除规则、Info 日志级别、请求内固定的成功采样键与决定、共享的每秒事件/字节预算，取得各自租约后才执行工厂；路由截断只用于日志显示和采样标签；许可和最终发射均以完整静态模板检查白名单，不能由截断前缀授予许可或丢弃合法投影。Info 被关闭时此成功查询投影不捕获，后续 Warning/Error 仍可输出安全摘要。未执行工厂的取消和未使用租约退还预算，工厂一旦开始，异常或超限仍消耗一次事件及预留字节额度，防止失败循环绕过 CPU 限流。默认每实例每秒 1000 次、2 MiB，请求和返回各受独立的单条字节上限，返回上限默认 0（关闭）。操作日志列表 Endpoint 已在查询成功后从规范化分页结果调用这两个入口；默认路由白名单为空，因此须显式配置模式、路由和对应字节上限才会采集。返回摘要取查询结果中的计数，不读取列表项或实际 HTTP 响应流；请求和返回写入独立的 B2 属性。投影及源生成元数据失败均不得影响列表响应。异常处理器只向 ILogger 传入异常类型及路由模板或固定未匹配占位符，不传原始 Exception、消息或实际路径段。B1 Restricted 目标始终拒绝。该切片不能作为完整请求/返回详情能力对外启用。

- 请求与返回分别限 2048 UTF-8 字节、深度 4、集合 16 项、字符串 128 字符；返回默认 0 字节（关闭），开启后使用显式预算。不要截断字节数组后追加省略号造成无效 JSON/UTF-8。
- 采用两阶段入口：TryBeginCapture 先检查启用、Endpoint/投影白名单、TTL、目的地资格、详情保留和速率/字节预算；许可成功后才调用受限投影工厂及 JsonTypeInfo 源生成序列化。关闭/未采样/超预算时工厂不得执行；不提供接收预先生成 string JSON 的公共捕获重载。
- 投影工厂自身限制字段、字符串和集合，再由有界 UTF-8 writer 限制包装后的输出；不先生成无上限 JSON。许可作用域负责提交或回收预留。B2/B1 只复用已批准的 Internal 输入，不复制 Restricted 详情到 B2。禁止响应流替换、Multipart/文件/流式/SSE/WebSocket 捕获、上传二进制和完整对象图。
- Password、Authorization、Cookie/Set-Cookie、Token、API Key、签名、原始 nonce、连接串及私钥永不持久化；nonce 如需关联只能显式 HMAC 摘要。普通字段中的凭据也需负例。直接异常 `@x` 亦须经过安全异常投影，数据库异常保持安全摘要，不让另一个 Sink 泄漏原文。
- CaptureState 固定为 `captured/not_enabled/not_allowed/not_applicable/redacted/truncated/failed/budget_exceeded`。未采集不是空 JSON，也不是错误；失败原因采用安全机器码，清理健康拒绝用 not_allowed，不写入原始异常消息。
- B1 Operation 详情规划新增可空、版本化 `ContextJson` 和 `DetailsExpiresAtUtc`：只存安全上下文和显式允许的投影，总 UTF-8 上限 8192 字节，计入微批实际字节预算。到期以首次捕获时间确定，重试/再次展示不得续期。历史行不回填未知内容。SQL Server/MySQL 成对增量迁移；不新建通用日志表、不把全部请求改为 B1。
- 摘要字段沿既有 Read 权限；投影/Restricted 详情须新精确权限 `auditing.operations.details.read`。API 无权限时不返回这些字段，UI 隐藏不代替授权；下载/导出同样受控，不自动把新增详情加到旧导出。
- Vue 详情三个页签“日志消息/请求参数/返回内容”，提供状态说明和安全 JSON 展示；真实服务端分页。关闭采集或旧行仍可读，不自动把新的 GET 详情查看变成 B1 写操作。

共享 HttpOperationContext 只承载 Internal 上下文；Restricted 上下文/投影由 B1 专属许可生成并单独保存，不注入通用 ILogger Scope/Enricher。结果带受控目的地，B2 发射只读取内部许可存储中的 B2InternalSummary，不能通过外部构造 Result 改写分类。

原 IP、服务端地址、请求/返回详情默认只进入 B1 ContextJson，禁止通过普通 Console/File、SelfLog、Kafka、ES、归档、Trace/Metrics 或异常输出产生副本。出口测试直接检查实际字节，不只检查 DTO；没有纳入 B1 的请求保持 B2 摘要，不为字段齐全新增审计行。将来若需要外部 Restricted 导出，须另行明确授权并制定完整访问/删除/重放方案，本计划不添加该开关。

详情 API 到期过滤立即生效，Worker 独立的 DetailsRetention 清理计划移除整个 ContextJson 并保留摘要；不受 Auditing:Retention.Enabled=false 影响。启用详情要求配置最大保留、最大清理滞后和实际 Worker 运行条件；清理过期积压超过预算时拒绝新详情许可并告警，摘要/业务语义不变。首次清理状态未知时不能扩大采集。保留天数不得超过审计行保留期；删除备份/恢复副本的物理期限按部署备份策略披露，恢复后先清理过期详情再开放读取，不能承诺瞬时擦除全部备份。

## 6. Kafka/ES 性能与恢复

Kafka Producer 长生命周期，批量、压缩、有界消息数/字节/在途量；`acks=all`、幂等和副本策略成对配置。逐条串行 await ACK 限制吞吐；应用直连实验只能使用受管理的后台发送，DeliveryReport 或有界 ProduceAsync，不能无界 Task.WhenAll。

普通/优先 Topic 分开；消费组按查询、归档分开。Topic 不按每个用户/租户生成；分区数按热点和实际消费者核定。保留策略选择时间/容量删除，不用 compaction 丢失日志历史。模板引导与写入使用最小权限，不由 API 启动创建平台资源。

### 6.1 消费确认与坏记录

确认方式独立于 Collector/ApplicationKafka 入口。当前候选基线为 `ackBoundary=PersistentQueue`；`SinkConfirmed` 仅在 LG00 为具体消费者证明逐项存储/可靠隔离后提交连续 Offset 时准入。后者可由 Kafka 保留待处理记录而省去重复 PQ，但必须验证保留余量、重平衡/重启、部分成功与持久隔离。Logstash 的 enable_auto_commit=false 在写入内部队列后提交，不能仅将 queue.type 改为 memory 就宣称 SinkConfirmed；未准入组合拒绝启动。

Logstash Kafka 输入目标为 enable_auto_commit=false、queue.type=persisted、queue.checkpoint.writes=1、有限 queue.max_bytes 和可恢复卷。其手动提交语义是在写入队列后推进 Offset，并不是 ES 索引成功；只有 LG00 证明 PQ checkpoint/fsync 先于 Offset 提交，才能把该 PQ 作为新的交付边界。每分区只提交连续已持久接收记录之后的下一 Offset，不得因后续记录先成功越过缺口。PQ 不复制数据，卷损坏/永久丢失风险不能由 Broker 中已有提交位点兜底；对应 RPO 与 Kafka 保留范围内受控回放必须单独验证。

PQ 事件完成条件为各目标逐项成功，或坏记录已可靠隔离。HTTP 200 不能代表整批 Bulk 成功；400/404 映射失败、429/5xx、超大记录、未知 schema 和 DLQ 满分别处理。核验所选插件默认的丢弃/重试行为，不能直接宣称 DLQ 开启即可靠隔离。不可证明隔离持久性的插件不得准入该可靠档；隔离失败时停止接收/处理并告警，不越过未完成记录。隔离存储有容量、保留和访问控制，故障内容不回写原 Topic。

checkpointWrites=1 在每事件写入后强制检查点，官方提示可能严重影响性能；它是当前可靠性准入实验的配置，不是通用高吞吐默认值。LG00/LG08 必须记录 PQ 写入/检查点耗时、持久存储延迟、持续消费和恢复追赶能力，分别验证应用、采集和消费瓶颈后再扩容/分区；不能为了吞吐静默扩大刷盘丢失窗口。LG00 任一入口/消费者/确认组合未通过只关闭该组合；已验证的其他组合可继续使用，不自动切换消费者组件或新增自研消费项目。

### 6.2 ES 重放去重

本阶段选择固定 UTC 事件日期索引，例如 fn-logs-{routeVersion}-{class}-{yyyy.MM.dd}，索引名由受控路由表、首次冻结的 OccurredAtUtc/IndexRouteVersion 生成；不接收业务提供的任意索引名，不按重放时间或当前路由版本重新分桶。显式关闭 data_stream 和 ILM rollover 写入模式，保留固定日期索引的生命周期清理。

LogEventId 映射 document_id，使用 index 动作，使同一事件重试始终写同一索引/ID；默认 ES 自动 ID 不满足该契约。去重承诺只覆盖当前路由版本/日期索引的保留窗口，不是全链 Exactly-Once。受控重放必须保留原始时间、路由版本与 ExpiresAtUtc，到期记录不再写入，不能重建已过期索引。索引迁移需要旧路由重放映射及单一写入所有权；变更路由不直接复制活动事件到另一个索引。无稳定 ID 的历史日志只按旧查询/尽力收集路径兼容，不用内容哈希合并两条真实事件，不授予同等去重承诺。

测试覆盖跨午夜、跨月、消费者重启、部分 Bulk 重试、写成功但 Offset 未提交和历史重放。data stream/rollover 作为后续单独评审能力，必须补充跨 backing index 去重方案，不能只依赖相同 ID。固定日期索引的分片、查询与写入成本在 LG08 用真实数据校准。

可靠阶段是“应用入队 -> 可恢复缓冲 -> Broker 确认 -> 最终存储确认”。至少一次承诺只从已验证的持久边界开始；采集插件若仅确认本地 SDK 入队，必须先证明 ACK/检查点语义或替换该发送边界，不得宣称 Broker 已持久化。Kafka ACK 不代表已索引。消费者可在下游确认或自身持久队列确认后提交 Offset；后一种必须验证持久队列恢复/刷盘语义。

应用内存崩溃、磁盘满、节点丢失、源文件轮转、保留超期都可能形成缺口；用 LogEventId 和故障窗口对账。节点本地磁盘只能承诺该节点可恢复，PV 的节点/区域失败能力也须实测。应用无限故障、资源有界、非阻塞和绝不丢失不能同时保证；B0/B1 不因外部日志系统失败降低语义。

## 7. 告警与验收

记录应用深度/容量/丢弃、采集缓冲字节/最老年龄、Producer 成功/失败、Kafka Lag/最老年龄/保留余量、ES 每项写入失败/延迟、隔离数、恢复追赶速度。标签不含原 URL、用户、租户、异常消息或 LogEventId；关联字段可在受保护日志中查询。

实际容量至少报告事件/秒、字节/秒、请求 P50/P95/P99、CPU、分配、磁盘/网络、重复/丢失/可查询延迟。故障矩阵覆盖 SIGKILL、采集器重启、Pod 重建、节点故障、磁盘满、Broker 不可用、ES 429/5xx/部分 Bulk 失败、消费者重平衡和写入后未提交 Offset。没有证据不能升级为生产容量或端到端 Verified。

容量验证分别记录调用线程、两路后台输出、节点采集器和消费写入端的持续处理能力；普通洪峰、错误洪峰和两者混合时同时检查优先日志年龄/丢弃与请求 P99。比较日志等级关闭、当前管道、目标快照 Summary、获许可投影、Direct/Kafka 的同环境差异。请求无明显变慢但持续丢日志、采集延迟增长或消费无法追赶，均不能作为容量通过。

## 8. 官方参考（2026-09-28 查阅）

- [微软高性能日志](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/high-performance-logging)：LoggerMessage 源生成。
- [微软同步日志边界](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-10.0#no-asynchronous-logger-methods)：慢存储后台化。
- [旧 Elasticsearch Sink 归档说明](https://github.com/serilog-contrib/serilog-sinks-elasticsearch)：不再作为长期扩展主线。
- [Elastic 官方 Sink](https://www.elastic.co/docs/reference/ecs/logging/dotnet/serilog-data-shipper)：官方 Sink 也不提供 durable mode。
- [Confluent .NET 客户端](https://docs.confluent.io/kafka-clients/dotnet/current/overview.html)：批量与投递报告。
- [Kafka Producer](https://kafka.apache.org/41/configuration/producer-configs/)、[Topic](https://kafka.apache.org/41/configuration/topic-configs/)：采用与项目 Broker 基线对应的文档，不凭 Java 参数默认值推断 librdkafka。
- [Fluent Bit Kafka](https://docs.fluentbit.io/manual/data-pipeline/outputs/kafka.md)、[Grep](https://docs.fluentbit.io/manual/data-pipeline/filters/grep.md)、[Kubernetes emptyDir](https://kubernetes.io/docs/concepts/storage/volumes/#emptydir)：插件与存储边界需真实测试。
- [Fluent Bit 性能](https://docs.fluentbit.io/manual/administration/performance)、[背压](https://docs.fluentbit.io/manual/administration/backpressure)：输入线程、缓冲模式和输出限额的行为，不能替代本项目版本与环境的容量实测。
- [Logstash Kafka 输入](https://www.elastic.co/docs/reference/logstash/plugins/plugins-inputs-kafka)、[PQ](https://www.elastic.co/docs/reference/logstash/persistent-queues)、[ES 输出](https://www.elastic.co/docs/reference/logstash/plugins/plugins-outputs-elasticsearch)、[DLQ](https://www.elastic.co/docs/reference/logstash/dead-letter-queues)：队列确认、逐项失败与刷盘代价；采用前固定版本并验证。
- [Elastic 重复事件与 rollover](https://www.elastic.co/blog/efficient-duplicate-prevention-for-event-based-data-in-elasticsearch)：同一 ID 不自动保证跨索引唯一。
