# Full.NET 日志模块说明

> 更新：2026-10-02，已纳入本地验收标准与 Collector/ApplicationKafka 生产入口准入。初次阅读基线为 `006ca78f1fff66a6bfe37627264ee527e3d9c673`，两路线文档更新基线为 `32fc0330757dc74b614e8c1b54e57e8824c0537b`。本文区分当前实现与后续扩展；未通过的投递或容量不得标为已验证。

## 1. 阅读入口与状态

本期日志开发已按本地标准完成收尾。真实浏览器验收覆盖三页签、按需读取、服务端分页、到期/历史数据、撤权后新会话与 API 403；SQL Server 4/4、MySQL 含访问/异常页面回归 8/8。实测修复了小视口分页遮住详情按钮的布局问题。最终范围、审查与回归见[收尾验收](../verification/2026-10-02-logging-module-closeout.md)。归档、旧兼容包迁移及扩大容量等后续扩展不阻断本期，未测规模保持 `Capacity-not-verified`。

秘密跨出口专项已覆盖 Collector 与 ApplicationKafka 的少量真实诊断事件：来源、TLS Broker、HTTPS ES、最终 Offset/DLQ 与清理诊断逐项核对。实跑修复了凭据模板脱敏时丢失必填 Instance、导致 Collector 丢弃日志的问题；仅保留严格 UUID 实例标识。范围与原生验证见[秘密边界回归](../verification/2026-10-01-logging-secret-boundary.md)。

Collector 与 ApplicationKafka 的同负载比较已有独立执行入口 `pnpm test:logging:route-comparison:live`。四档正反序、请求子进程隔离、实测请求身份、全部文档与最终 Offset 的核对方法见[两路线比较记录](../verification/2026-10-01-logging-route-comparison.md)。配置选择依据局部性能与故障边界，不自动改变默认路线。

- 架构边界：[总体架构 §16](../superpowers/specs/2026-07-17-fullnet-architecture-design.md#16-可观测性与高并发日志)。
- 本次决策：[ADR-0012 可配置日志采集与 Kafka](../architecture/adr/ADR-0012-configurable-log-delivery.md)。
- 唯一活动清单：[本期收尾](../superpowers/plans/2026-09-28-configurable-log-delivery.md#本期收尾清单2026-10-02唯一活动清单)。LG00—LG08 保留历史设计，消费者与确认边界已有本地运行证据。
- 当前应用双通道故障处置：[日志降级说明](logging-degraded-mode.md)。

| 能力 | 当前源码事实 | 保持约束与后续扩展 |
| --- | --- | --- |
| `ILogger<T>` / Serilog | 已有结构化日志、Console Compact JSON、独立条数/字节有界普通与高优先级快照队列；生产容量尚未认证 | 保留；高频固定模板使用 LoggerMessage，补齐分类和字段 |
| HTTP 汇总 | B2 Summary、采样、显式请求投影入口；列表服务端分页与操作日志 B1 受限详情已接通 | 其他业务路由的受控投影可按需要扩展 |
| Auditing | B1 Operation/Exception/Outbound 微批，独立条数/字节预算；双库详情授权/到期；默认关闭的 B2 Access 入库 | 保持 B0/B1/B2 边界及配置默认关闭的原始详情策略 |
| Elasticsearch | 核心 Hosting 仍引用兼容旧 Sink；开关默认关闭；新集中链路由独立消费者写入 | 旧包移出核心须单独做兼容迁移，不阻断新入口本地验收 |
| Fluent Bit | 固定镜像的解析、标签准入、普通/优先分流、缓冲与局部故障已实际验证；Collector Kafka 实时链路已接通 | 生产部署按故障窗口选择存储，emptyDir 不承诺节点丢失后恢复 |
| 日志 Kafka | API/Worker 与 Helm 已允许显式生产 ApplicationKafka 配置；本地真实 Broker 覆盖 TLS/SASL/ACL，生产原生运行结果见[准入验证](../verification/2026-09-30-application-kafka-production-admission.md) | 两入口可配置选择，容量与下游确认按实际范围验证；不复用业务 Outbox/Inbox 或 Producer 预算 |
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
| FullNet:Logging | 普通/高优先级容量 10000/1000 条与 64/8 MiB、单条 UTF-8 JSON 16384 字节、非阻塞、停机总预算 5 秒；`IndexRouteVersion`/`IndexRetentionDays` 默认均为 0（关闭），须成对显式配置为 1..9999/1..3650，ApplicationKafka 预览模式还要求两者为正值；详细示例见降级说明 |
| Observability:HttpOperation | Enabled=true、Summary、XL；成功采样初始 1%，AlwaysRecordErrors=true，慢请求阈值 1 秒；IncludePathPrefixes=/api，排除 /health、/openapi、/scalar |
| 同上捕获/发射 | PriorityCapacity=2048、BestEffortCapacity=8192 是并发发射闸门；请求预算 2048 字节、返回预算 0、PayloadRouteAllowList 为空。列表端点已调用请求和返回分页投影，但默认配置不会产生 Payload |
| Auditing:MicroBatch | B1 共用容量 4096 条、计费 64 MiB；每批最多 64 行、计费 262144 字节、等待凑批 20ms；入队等待 100ms、停机排空 5 秒。100ms 不是请求等待数据库的总上限 |
| Auditing:AccessLogCapture | 默认关闭；Development 配置显式开启供本地日志页面联调；开启后 B2 队列容量 8192、每批最多 100 行、凑批 100ms、退出 5 秒 |
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
- LG04 详情存储增量：双库 239 迁移增加可空 `ContextJson`、`DetailsExpiresAtUtc`、到期索引和 Auditing 自有的单行清理检查点表，不回填旧行；Worker 已独立按有界批次清空到期详情并保留摘要，成功读取剩余过期积压后刷新共享检查点，不受普通 Retention.Enabled 控制。API 已接入后台检查点读取及失败关闭的本地资格缓存，请求不查库。B1 操作写入现已将固定网络上下文与首次捕获确定的绝对到期时间同批写入；只有显式开启、完整静态路由白名单命中、检查点健康且保留期合规时才构造，超限详情降级为摘要。操作日志导出成功路径已增加仅含时间范围/状态码/成功筛选的请求摘要，以及仅含行数/截断/敏感列标记的可选结果摘要；不读取筛选文本、文件名和文件字节，其他端点仍只采固定网络上下文。资格在请求捕获时判断，已获准并排队的行可能在热配置关闭后完成写入；停用不是对在途行的同步撤销。默认配置保持零详情写入；列表和旧详情 API 不读取 ContextJson。独立的 `GET /api/v1/auditing/operation-logs/{operationLogId}/details` 同时校验普通 Read 与受限详情 Read，只读取未到期固定版本上下文，到期及非法内容返回 404，不返回原始 JSON。2026-09-29 本地真实 SQL Server/MySQL 聚焦 Host.Api 详情权限、过期及普通详情隔离各通过 1/1，239 迁移恢复通过 2/2，详情到期清理/共享检查点所在的保留集成用例通过 2/2；Worker 实际宿主生命周期、原生 AOT 与平台端到端仍待验收。
- 采集：[Fluent Bit 候选配置](../../deploy/observability/fluent-bit-values.yaml)。现已改为单 Tail、Pod 标签准入、必需封套字段筛选和缺分类的普通 ILogger 默认 BestEffort；`@t`、CRI/Docker 解析、字段筛选、优先级分流与脱敏键已由固定版样本局部验证，emptyDir、重试和输出确认仍须故障验证，不以路由样本代替真实交付。
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

普通结构化日志的入队快照现对已知敏感属性名、嵌套成员与字典键做整值替换；原始 IP、端口、地址、请求/返回 Body、Header、Query 等已知受限详情字段在顶层及嵌套层移除。HTTP Operation 的可选属性只接受中间件固定摘要字段名单，其他属性不复制；名单中的受控 RequestPayload/ResponsePayload 仍须经过两阶段投影许可，单靠属性名不能证明来源可信。自由文本中出现 Bearer/Basic、密码、Token、API Key、私钥、连接串或签名等明确赋值格式（包括 JSON、带点或索引式字段名）时整块替换；包含合法 JSON Unicode 转义与赋值分隔符的字符串也整块移除，以防转义键绕过明文匹配，因此可能移除无秘密的编码文本；普通 Windows 路径的 `\U` 不属于合法转义。危险属性名和类型标签也不保留原文。模板本身含敏感语境时，通用占位符值一并丢弃；只有未被模板引用且通过固定值或格式校验的分类、流、可靠性、Trace/Span ID、状态码及 LogEventId 可以保留。模板过长或 token 过多时只保留经校验的关联/路由元数据，尤其保持已生成的 LogEventId，不复制任意属性。普通可选属性最多扫描 256 个，先按固定键取必要属性，避免大量无效属性拖长请求线程。Console 快照与旧 ES 兼容事件使用同一受限副本，出口字节负例已覆盖已知字段；这属于已知格式防护，无法识别没有标记的任意秘密，也不能替代调用方禁止记录凭据的约束。每条字符串先受既有长度上限，再做匹配；新增扫描成本仍需与请求 P99/分配基线比较。

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

旧示例曾有两个 Tail 输入读取同一批 `*fullnet*.log` 文件；当前候选改为单 Tail 加重标记分类。2026-09-29 固定 Fluent Bit 4.1.1 镜像的本地 Docker Desktop 样本通过路由、分类、缺失元数据排除、优雅重启及进程 SIGKILL 后追加记录再重启的 Tail 检查点测试；执行中发现并修复 `grep` 对 `@t`、`@mt`、`log.class` 直接读取时静默丢弃全部记录的问题。不可达 Forward 加无限重试的故障夹具在删除源日志后从原缓冲目录重放全部事件；改用当时双路有限重试并维持 130 秒故障时，B2 Forward 路径的两个普通事件缺失。详情见[本地验证记录](../verification/2026-09-29-fluent-bit-collector-route-fault.md)。这说明磁盘缓冲可重放与有限重试下允许丢失同时成立，不能外推到原下游恢复后的 ACK、满盘或 Pod/节点丢失。该证据未覆盖 GitHub Actions 终态、节点聚合流量或持久 ACK。CPU request=100m、limit=500m 不能作为高并发容量依据。`emptyDir: 2Gi` 是本地临时缓冲，且不等于每个输出都可独占 2Gi；输出另有队列限额，归档缓冲也需计费。这些是当前配置事实和待验证风险，不是已观测到的生产瓶颈。LG06 仍须按节点总量验证资源、解析正确性和输出隔离。

Priority Forward 当前改为持续重试，B2 Forward 保留有限重试。固定镜像的 130 秒不可达样本中，Priority 三条从缓冲恢复且无输出丢弃，B2 两条按预算丢弃；详见[故障记录](../verification/2026-09-29-fluent-bit-collector-route-fault.md)。两路 Forward 均要求 `Require_ack_response On`，因为 [Fluent Bit 4.1 Forward 输出文档](https://docs.fluentbit.io/manual/4.1/data-pipeline/outputs/forward)说明默认不等待接收端 ACK。候选下游必须支持该协议；ACK 仅说明接收端按协议回应，仍须用真实组件证明其持久化和最终存储边界。这不是无限故障或真实下游确认保证。

固定镜像的双容器 Forward 烟测已验证两路正常 ACK 后各计一次成功、无 ACK 时已发送数据不计成功；`pnpm test:observability-forward-ack:live` 与专项 CI 复用该脚本。附加故障场景在接收端读到数据但尚未回 ACK 时 SIGKILL 采集器、删除源日志并保留发送缓冲，重启后原 ID 获得 ACK；然后以 `storage.sync full` 的接收端缓冲接收并 ACK，在下游不可达时 SIGKILL 接收端，保留目录重启后原 ID 恢复一次。它只验证单样本的进程崩溃恢复；卷/节点故障、满盘、生产接收端持久边界和最终下游仍待纵向验收，详见[故障记录](../verification/2026-09-30-fluent-bit-pre-ack-crash.md)。候选输出已显式启用 `tls.verify_hostname On`，两路共用只读挂载的 `/fluent-bit/tls/ca.crt`；平台须预置 `fullnet-forward-ca` Secret 的 `ca.crt` 键，可在候选 `extraVolumes` 中改名，接收端证书 SAN 须匹配两个 `Host`。独立固定镜像 TLS 烟测直接使用该路径，验证临时 CA 和匹配主机名下两路 ACK、错误 CA 与主机名不匹配时的拒绝，见[TLS 记录](../verification/2026-09-30-fluent-bit-forward-tls.md)。CA 轮换后先滚动重启采集器，直到目标平台证明连接重载行为；Secret 投影与实际接收端仍待验证。

两路 Forward 均显式设置 `net.io_timeout 5s`。固定镜像实验显示，在默认无 I/O 超时的长连接上，接收端读到数据但持续不回 ACK 时，60 秒内没有重试；设置超时后，原 ID 进入重试而未计成功，避免单个无响应连接长期占住发送工作。超时和重试仍受各路容量预算约束，B2 重试耗尽可丢弃，Priority 缓冲满也可丢弃。

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

Collector 与 ApplicationKafka 两入口均已按本地测试准入；默认仍为 Legacy，采集器与后台直发可显式配置选择。相同负载的正反序比较未形成稳定性能胜者，不根据瞬时负载自动切换。Kafka 档的 ES 可关闭，但必须有其他有效消费目的地。独立消费者的实验部署开关与应用入口准入分别管理。

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
  consumer: # 原实验配置，可靠档准入已阻塞，不可用于生产
    implementation: Logstash
    ackBoundary: PersistentQueue # 该 Logstash/PQ 组合未满足确认顺序要求
    persistentQueue:
      enabled: true
      maxBytesPerPipeline: 1073741824 # 示例；总容量还须包含所有流水线及 DLQ
      checkpointWrites: 1 # 持久化优先起点，吞吐必须实测
  archive:
    enabled: false
```

上例为 Collector/CentralKafka。ApplicationKafka 档的应用配置为 `FullNet:Logging:DeliveryMode=ApplicationKafka`，平台改为 `ingress: ApplicationKafka`、`transport: Kafka`、`collector.enabled: false`；Kafka 仍启用，单独向具备适配能力的应用注入 Producer Secret。采集器可采集其他工作负载，但必须排除同一应用日志流。每个 Pod 按其发布时的路由标识独立筛选；旧版 Collector Pod 与新版 ApplicationKafka Pod 混合存在时仍应各走一条入口。当前 Fluent Bit 候选已添加标签准入，非生产 Helm 可预览直发；目标平台的真实路由、元数据缺失、滚动发布与 Console 镜像负例、Broker ACK 和下游恢复证据齐全前不开放对应确认档；Collector/ApplicationKafka 的生产入口配置准入按下方本地验收执行。

旧配置迁移优先级须先落地并测试：

| 新 DeliveryMode | 旧 Elasticsearch.Enabled | 生效行为/处理 |
| --- | --- | --- |
| 未指定 | false/未指定 | 保持既有 Console 与已有平台采集行为；显示 `legacy-console` 状态，不暗示 Local 零远程 |
| 未指定 | true | 保留旧 ES 直写并告警，显示 `legacy-direct`；迁移完成前不删除依赖 |
| 显式 Local | false | 应用内仅写 Console；部署采集器必须排除该流，当前尚未验证远程零投递 |
| 显式 Collector | false | 应用内仅写 Console；实际采集入口、路由和目的地须由平台交付并验证 |
| 显式 ApplicationKafka | false | 宿主注册静态适配器且 TLS/双 Topic/冻结路由配置完整时可在 Production 启动；缺少适配器或必需配置时拒绝 |
| 任意显式模式 | true | 启动拒绝，要求先迁移或关闭旧 Sink；不能静默忽略或同时投递 |

当前 LG05 首批代码已加入强类型 `FullNet:Logging:DeliveryMode` 与启动校验：未设置时保持旧 Console/ES 分支，旧 ES 直写会在宿主启动时输出一次固定迁移告警；显式模式与旧 ES 开启冲突时拒绝启动。显式模式还要求同值的 `FullNet:Logging:ExpectedDeliveryMode`，后者由发布清单注入；缺少、孤立或不一致时拒绝启动。注册服务与构建 Serilog 之间若切换任一模式值或旧 ES 开关也拒绝启动，避免校验、告警和 Sink 分叉；运行中切换仍需重启。应用的同值校验只覆盖本地配置，不能证明平台采集器行为。

Helm Chart 的 `logging.ingress` 默认 `Legacy`，不注入两个新模式键，保留旧行为。2026-09-30 按用户批准的全项目本地验收标准，**生产 API/Worker 可显式选择 `Collector` 或 `ApplicationKafka`**，并保持 `production=true`、`dotnetEnvironment=Production`；非生产仍可预览 `Local`、`Collector` 或 `ApplicationKafka`，使用 `production=false` 与 `dotnetEnvironment=Staging`（或 `Development`）。Chart 将环境写入 ConfigMap，把入口模式、同值预期模式及 `fullnet.io/log-ingress` 标签固定在同一 Pod 模板；旧 Pod 重启不会因共享 ConfigMap 更新而改用新路由。生产 Local 继续拒绝；ApplicationKafka 必须注册静态适配器、配置双 Topic/TLS 和冻结路由，Helm 要求专用 Producer Secret，可选只读 CA Secret；Collector 应用不注入 Kafka/ES 凭据。

直发仍须指定 `logging.kafka.configurationSecretName` 的固定 `bootstrapServers`、`generalTopic`、`priorityTopic`、`securityProtocol` 键，SASL 模式提供 `saslMechanism`、`saslUsername`、`saslPassword`；私有 CA 可设 `logging.kafka.caSecretName` 的 `ca.crt`。Chart 只在直发模式挂入证书并显式映射固定配置键，不通过 `envFrom` 覆盖模式。凭据与 CA 轮换后滚动重启；TLS/SASL Secret 不进入 Chart/ConfigMap。

生产直发入口示例（与对应角色、Provider 和现有生产安全 values 合并；两个角色分别发布）：

```yaml
production: true
dotnetEnvironment: Production
logging:
  ingress: ApplicationKafka
  indexRouteVersion: 2
  indexRetentionDays: 30
  kafka:
    configurationSecretName: fullnet-log-producer
    caSecretName: fullnet-log-broker-ca
```

版本/保留天数须与下游受控映射一致。Secret 在目标命名空间预置，Producer 仅授予指定日志 Topic 的 Write/Describe 权限；CA 只包含公钥证书。普通 Kafka Producer、日志配置 Secret 和消费者凭据不共用；此示例不安装 Broker/ES，不触发自动切流。
ApplicationKafka 入口在本地通过生产 Host/Helm 配置、TLS/SASL/ACL Broker 投递/拒绝与中断恢复、预算/关闭以及 API/Worker 原生运行后准入；不以完整容量矩阵或独立消费进程投产为启动前置。普通与优先使用两个长生命周期 Producer，`acks=all`、幂等及条数/字节/SDK 在途预算保持启用。请求不等 Broker ACK，无应用磁盘 Spool；入队 Accepted 不等于 Broker 确认，崩溃会丢未确认事件，重启不会自动重放。该入口用于 B2/运行诊断，不替代 B0/B1 审计。Broker 确认也不表示 ES 已索引；可选下游消费者按自己的确认/恢复契约验收。默认依旧 Legacy，不自动切流，容量和 P99 不由入口开关推导。
Collector 开关的准入验证在本地执行 Host/Helm、固定镜像路由、TLS/ACK 与故障场景，不再要求 CI 终态、专用生产等价集群或先完成完整 10K 容量矩阵。应用内继续写 Console 双通道，旧 ES Sink 不启用；平台采集与下游须另行配置，`configuration-only` 不表示端到端已确认。当前 kind 路由实测使用 stdout 测试出口；Forward/Kafka/ES、持久确认及恢复组合按自身的本地实际测试验收。已知有限重试/满盘/Pod 临时卷丢失属于日志损失边界，B0/B1 审计不依赖该链。未测 P99/规模保持 `Capacity-not-verified`，不对真实生产硬件外推。

现有受 `observability.elasticsearch.read` 保护的 Elasticsearch 管道健康响应已追加 `deliveryStatus` 与 `deliveryConfirmationBoundary`，不改变原字段和路由。状态从宿主启动时冻结的配置计算：未指定新模式且旧 ES 关闭为 `legacy-console`，旧 ES 开启为 `legacy-direct`，显式 Local 为 `disabled`，Collector 为 `external-collector`；直发注册后为 `application-kafka`。确认边界当前只有 `configuration-only`，或旧 ES Sink 确实注册时的 `sink-registered-only`，两者都不表示远端已收到、持久化或可查询。ES 关闭时管理查询不连接 ES；Vue 在非兼容模式只显示入口状态和确认边界，不把旧版 ES Sink 未注册、索引或集群诊断呈现为当前路线故障。旧版服务未提供新增字段时，Vue 保留兼容诊断并显示“旧版服务未提供”。运行时管理状态只反映当前宿主，不聚合所有 Pod，也不替代平台采集器与消费端验证。

关闭组件不创建客户端、不连接、不探测，不暴露秘密。显式新模式与旧直写冲突、发布预期模式不匹配、ApplicationKafka 配 Direct、缺少静态适配或必需配置、Kafka 传输但 kafka.enabled=false、集中档没有目的地、缓冲无容量等组合须拒绝。日志与业务 Kafka 使用独立配置、Producer、ACL、Topic、配额和消费组；不自动启用 Messaging HybridKafka/CDC。当前宿主已移除 `ReadFrom.Services` 根级隐式 Sink 注册；未来新增第三方出口必须显式进入同一管道并核验单一所有权。

Hosting 核心不引用日志 Kafka 客户端，但仍引用 `Serilog.Sinks.Elasticsearch` 支持旧版兼容 Sink；移出旧 ES 包是后续兼容性迁移，不能宣称依赖已排除。现有业务 Kafka 仍归 Messaging。Host.Api/Worker 已静态引用独立 `Full.NET.Logging.Kafka` 适配项目，不通过反射加载；未选中直发时不创建日志 Producer，但引用的托管/native 包仍进入宿主发布闭包。Host.Api 的本地 Linux Native AOT 直发普通和优先日志已通过真实 Broker 测试；Worker 本地原生进程的普通资源日志也已通过受限 Broker 读回和正常退出验证。默认与直发路径的许可、体积、裁剪差异、Worker 优先事件仍须本地实际验证，Linux CI 可选，不能仅凭关闭配置推断依赖已排除。

本地真实 Kafka 4.1.2 集成测试已验证普通/优先独立 Topic 的 Broker 投递回调、消息可消费、事件 ID 与 JSON 内容一致及预约归还；暂停 Broker 时异步失败归还预约，恢复后同一 Producer 可再次获得 ACK。非生产 Host 的 `ILogger` 也已通过双有界管道送达测试注入的真实 Producer，再从两个 Topic 读回同一事件 ID。独立 TLS 夹具临时生成 CA 与服务端证书，正式 `KafkaLogSnapshotExporter.Create` 经 `SslCaLocation` 连接 Broker，双 Topic 均读回同一事件 ID；SASL_SSL/PLAIN 测试账号也可通过正式工厂投递，错误密码只产生异步失败、没有 ACK，并归还预约。另以启用 StandardAuthorizer 的测试 Broker 验证受限 Producer 凭指定 Topic 的 Write/Describe ACL 获得 ACK，对未授权 Topic 无 ACK、异步失败并归还预约；读取因无消费组权限被拒绝。本地 Linux Native Host.Api 进程在非生产模式下经同类 TLS/SASL/ACL Broker 投递普通资源日志和按测试慢请求阈值归入 Priority 的 HTTP 操作日志，两个 Topic 读回的消息均含管道事件 ID，健康检查与正常退出均通过，证据见[原生验收记录](../verification/2026-09-29-log-kafka-native-aot.md)。上述为此前非生产证据。本次生产入口准入以 Production Host/Helm 与新原生测试为准；跨进程崩溃重放、目标平台 Secret/证书轮换及端到端 ES 可查询仍按各自实际验证范围披露，容量保持未验证。

平台 Kafka 消费写入端不复用业务 Worker。原 Logstash/PersistentQueue 可靠档基线现因 Kafka 输入 Offset 可能领先安全 PQ 写入而准入阻塞，详见 §5 与[ADR-0012](../architecture/adr/ADR-0012-configurable-log-delivery.md)。平台消费者仍须独立部署、凭据、Consumer Group、CPU/线程/连接/批量预算，普通/优先及查询/归档分开容量。LG00 重新评估可证明安全持久接收或逐项最终存储/可靠隔离后推进连续 Offset 的实现；固定版本、摘要、许可及故障证据后才开放对应 Kafka 档。

**独立消费进程试验切片（2026-09-30）：**`Full.NET.Host.LogConsumer` 已实现顺序 `Kafka Consume → 线格式/冻结路由校验 → 单项 ES Bulk 或受限 Kafka DLQ → 同步提交 nextOffset`。消费组关闭自动提交/自动存储位点；ES 只有逐项状态成功且回执 `_index`/`_id` 与本次目标完全一致才算确认；缺失或错配保留位点，400/409/413/422 逐项确定性失败需 DLQ `acks=all` 且投递报告为 `Persisted`，404/429/5xx 与未知响应保留位点。无效、过期、未知路由记录也只在 DLQ 确认后提交。ES 与 DLQ 失败或提交异常退出进程，监督器退避重启后从原位点重放；固定 `_id=LogEventId` 保持同一日期索引内幂等。ES 禁止跨主机自动重定向，整次 Bulk 请求及响应读取有 30 秒截止。真实 TLS Kafka 测试已验证正常记录、无效 UTF-8 key 和 DLQ 记录及连续提交至 Offset 3；固定镜像 Elasticsearch 9.5.4 与真实 Kafka 的集成测试又验证了 ES 写入成功而 Offset 提交失败时，重建同组 Consumer 后同一 ID 只有一份可查询文档，并最终提交 Offset 1。另一固定镜像用例使用真实 ES 映射拒绝（逐项 400）：隔离未确认时重建同组 Consumer 仍从 Offset 0 重放，受限 Kafka DLQ 收到原始记录后才提交 Offset 1，坏文档在 ES 中计数为零。前两项测试在传输层把受控 HTTPS 测试地址转到容器 HTTP 端口。新增固定版 ES HTTPS 测试验证：无受信 CA 时握手失败、无效 API Key 不确认写入、有效的最小写权限 API Key 可完成 Bulk 并查询文档；测试自签 CA 不发布 CRL；独立消费进程在 Development 显式配置 NoCheck 后，从真实 TLS Kafka 取记录，以私有 CA 和最小写权限 API Key 写入 ES 并提交 Offset 1。Production 或未指定 DOTNET_ENVIRONMENT 时对 NoCheck 配置拒绝启动。独立进程使用无效 ES API Key 时延期投递，来源 Offset 未提交且目标文档不存在；修正凭据后同组重启，原记录写入并提交 Offset 1。生产在线吊销检查和正式 CA/CRL/OCSP 仍未验证。独立进程的真实 TLS Kafka 用例已验证：DLQ Topic 不存在时进程退出且来源 Offset 未提交；修复 Topic 后重启，原始坏记录进入受限 DLQ 并提交 Offset 1。该用例不触发 ES 写入，也不是 SIGKILL 窗口验证。新增受控 HTTPS 代理只在真实 ES 逐项写入成功后暂扣回执：此时 SIGKILL 独立 Consumer，来源 Offset 仍为 1；等待 Kafka Group 会话失效并重启后原记录重放，Offset 到 2，同一 LogEventId 在 ES 仅保留一份文档。另以 TLS Kafka 验证成员离组后的分区交接：旧成员的迟到提交失败，新成员从原 Offset 处理并提交。另以测试专用 9 秒 MaxPoll 迫使处理中旧成员退出 Group：新成员重取原 Offset，旧成员迟到提交被 Broker 拒绝，新成员确认后提交。上述证据详见[故障与交接验证](../verification/2026-09-30-log-consumer-crash-handoff.md)；它不覆盖生产默认 Poll 预算下的全部撤销时序、Kubernetes 部署或吞吐预算，不能宣称生产准入。

试验进程默认拒绝启动，须显式设 `FULLNET_LOG_CONSUMER_EXPERIMENTAL=true`。必填配置：`FULLNET_LOG_CONSUMER_BOOTSTRAP_SERVERS`、`TOPICS`（逗号分隔，不能含 DLQ）、`GROUP_ID`、`DLQ_TOPIC`、`ROUTES`（例如 `1:30,2:30`，版本到保留天数的不可变映射）、`MAX_EVENT_BYTES`（256–65536）、`ES_URL`（HTTPS 根地址）、`ES_API_KEY`（已编码的 API Key）、`SECURITY_PROTOCOL`（`Ssl` 或 `SaslSsl`）。这些键均带共同前缀 `FULLNET_LOG_CONSUMER_`；可选 `SSL_CA_LOCATION`、`ES_CA_PATH`（仅包含公钥证书的 PEM/DER 文件；自定义 CA 默认执行在线吊销检查，证书链及 CRL/OCSP 必须可用）、`ES_REVOCATION_MODE`（仅 Online 或 NoCheck，须同时配置 ES_CA_PATH；NoCheck 仅供 DOTNET_ENVIRONMENT=Development 测试，其他环境拒绝启动），SASL 模式额外必填 `SASL_MECHANISM`、`SASL_USERNAME`、`SASL_PASSWORD`。生产凭据应以独立 Secret 提供；Consumer 只读来源 Topic 和提交所属 Group，DLQ Producer 只写隔离 Topic，ES 凭据只允许固定日志索引写入。DLQ 保存原始坏记录，可能含敏感内容，必须有独立 ACL、加密、保留与回放操作。ES 输出新增 `WriteBatchAsync`，最多 64 条、完整 NDJSON 请求 1 MiB、响应 64 KiB，返回严格对应 index/ID 的逐项结果；混合成功/重试/隔离和真实 HTTPS ES 批量重放已验证，见[Bulk 传输记录](../verification/2026-10-01-log-consumer-bulk-transport.md)。该传输 API 不提交 Offset 或确认 DLQ。宿主新增可选 `FULLNET_LOG_CONSUMER_BATCH_MAX_RECORDS`（1..8，默认 1）；Helm 对应 `batchMaxRecords`。大于 1 时单 Poll 循环只收集已可用记录，不等待凑批、不并行调用 Consumer。`BatchLogDeliveryProcessor` 在完整逐项回执后按交付顺序等待必要的可靠 DLQ ACK，首个未确认项之后全部留待重放；每分区只提交此前已确认前缀的末条下一 Offset。收集及异步阶段检查分配代数，Broker 拒绝失去成员资格的迟到提交作为最终兜底。超大载荷或超过 36 字节的原始 Key 走原单条隔离路径。本批合法原始载荷最多 8×65536 字节，ES 仍受 1 MiB 请求/64 KiB 响应预算；这不限制 SDK 缓冲或整个进程 RSS。ES/DLQ 按逐项计数，Offset confirmed 指标按提交操作计数，不能再当作事件条数；提交异常可能使健康摘要暂时少计已成功提交的部分分区，权威进度仍以 Broker Offset 为准。默认仍为单条和关闭实验部署。见[批次协调验证](../verification/2026-10-01-log-consumer-batch-coordinator.md)。另以批次 8、500m/256Mi 在本地持续发送 3000 条、每条 2048 字节 JSON：实际 48.89 条/秒、61.366 秒；ES 暂停 10.354 秒后观测积压并在输入继续时追平，最终 Offset=3003/lag=0、逐 ID 内容一致、DLQ 不新增且无额外重启。这个有限场景可按本地标准验收，不代表长期容量或请求 P99，见[持续输入验收](../verification/2026-10-01-log-consumer-sustained.md)。2026-10-01 本地单分区突发基线已对账 1000 条、每条 2048 字节 JSON，耗时 25.924 秒、观察速率 38.57 条/秒、最终 Offset=1003/lag=0、DLQ 未新增；计时包含 CLI 和提交观察开销，不代表持续容量或请求 P99，见[吞吐基线](../verification/2026-10-01-log-consumer-throughput.md)。满额/超大且无法写入 DLQ 会保留原 Offset 并停止推进，而非丢弃。退出码 2 表示门禁关闭，3 表示可重试的投递延期，1 表示其他错误；监督器必须设置退避与告警。

### 独立消费者运维与部署

独立 JIT 消费者现在通过 Host 生命周期接收 SIGTERM/SIGINT；停机取消正在等待的操作，不把未知投递当作成功。250ms Poll 让空闲进程也能响应停机，仍保持单循环逐项确认后提交。`FULLNET_LOG_CONSUMER_HEALTH_PORT` 默认 0（关闭 HTTP），显式配置范围 1024..65535。

运维端点只有 `/health/live`、`/health/ready`、`/metrics`：live 不因下游故障直接触发重启；ready 必须实际取得 Kafka 分区且未停机，撤销/丢失分区立即撤销就绪。它表示分区分配资格，不表示 ES 可写、没有积压或某条日志已索引。指标包括无标签的分区数、就绪、已确认且提交条数及延期条数，以及采样积压和固定 ES/DLQ/Offset 结果计数。

消费者使用 10 秒 SDK Statistics 回调，不额外向消费循环增加网络查询。`fullnet_log_consumer_lag_records` 只聚合当前实际分配分区的 `consumer_lag`，基于已提交 Offset（read_committed 使用 last stable offset），不是 stored/app Offset，也不是整个消费组总积压。统计仍是 SDK 的缓存快照，不提供强实时 Broker 状态保证。没有完整分区样本、负值、对应 Broker 非 UP、超出 262144 字符/深度 16/128 分区预算、原始统计时间超过 30 秒或分区发生变化时，输出 `lag_known=0`、`lag_records=-1`；禁止显示为 0。`lag_sample_age_seconds` 表示原始 SDK 统计年龄，无样本时为 -1。语义来源见[官方 librdkafka Statistics](https://github.com/confluentinc/librdkafka/blob/master/STATISTICS.md)。

`fullnet_log_consumer_delivery_results_total` 只有九条固定序列：ES confirmed/retry/isolated/error，DLQ confirmed/retry/error，Offset confirmed/error。ES/DLQ/Offset 成功计数各在对应确认后更新；异常原样传播，停机取消不计 ES/DLQ failure。它们是进程内计数，快速失败可能在抓取前消失，不能当作持久故障账本；TargetDown 与 Kubernetes 重启告警作为补充。

交付载体为 `deploy/containers/log-consumer.Dockerfile` 与独立 `deploy/helm/fullnet-log-consumer`，不装入业务 Worker。Chart 默认 `enabled=false`，启用须同时显式设 `experimental=true`、固定 `image.tag`（禁止 latest）和 `configurationSecretName`；这些参数不解除可靠消费生产门禁。Pod 使用非 root、只读根文件系统、资源限额、90 秒终止余量及真实 HTTP 探针；只提供 ClusterIP，入站 NetworkPolicy 仅允许配置的监控命名空间。网络策略是否生效取决于集群 CNI，渲染成功不证明执行了隔离。

配置 Secret 固定必填键：`bootstrapServers`、`topics`、`groupId`、`dlqTopic`、`routes`、`maxEventBytes`、`esUrl`、`esApiKey`、`securityProtocol`；SaslSsl 另需 `saslMechanism`、`saslUsername`、`saslPassword`。不使用 envFrom。可选 `caSecretName` 必须提供 `kafka-ca.crt`、`es-ca.crt` 两个公钥键，只读挂载；Chart 固定 Online 吊销检查，不提供 NoCheck 配置。Secret 更新后须显式重启，本切片不实现自动凭据轮换。

部署采用 Recreate：分区不足时新旧实例并存可能让新 Pod 永远没有分区，阻塞滚动更新。先停旧实例再启动新实例，期间 Kafka 保留记录；副本数应不超过来源分区分配能力，更新窗口须包含在 Broker 保留预算中。默认资源只为参考起点，不是吞吐认证。

可选 `podMonitor.enabled` 默认 false，需要平台已有 Prometheus Operator CRD；`podMonitor.labels` 必须匹配实际 Prometheus 的 PodMonitor 选择器，其命名空间选择器也要覆盖消费者 release namespace。抓取命名空间须匹配 `monitoringNamespace`；Job 标签固定来自 `app.kubernetes.io/name`，规则匹配 `job="fullnet-log-consumer"`。五项规则覆盖持续超过 1000 的已提交积压、已分配但未知的样本、固定 retry/error、目标不可达及反复重启。阈值是运维参考，不是容量认证。重启规则要求 kube-state-metrics 导出 `--metric-labels-allowlist=pods=[app.kubernetes.io/name]` 及必需指标，见[平台接线说明](../../deploy/observability/README.md#independent-log-consumer-monitoring)。本项目不自动安装监控栈或发送外部通知，Alertmanager 接收器由部署平台配置。

2026-09-30 本地指标与规则评估验收通过：日志聚焦单测 297/297、Linux Kafka/ES 14/14；实际 Prometheus 读取 Pod 指标，测试采集桥中断和恢复触发并解除正式 TargetDown 规则。其余四项告警以实际 promtool 时序用例验证，见[指标记录](../verification/2026-09-30-log-consumer-alerts.md)。后续 `pnpm test:log-consumer:operator:live` 已在本地真实 Operator/Prometheus 验证 PodMonitor 与命名空间的双层选择器：先取得 up，再分别排除、等待目标消失及恢复；最终实际 Pod IP:8080 直接抓取成功，见[发现验证](../verification/2026-09-30-log-consumer-operator-discovery.md)。2026-10-01 的 `pnpm test:log-consumer:restart-alert:live` 已验证实际 kube-state-metrics 两个指标与应用标签、工作负载命名空间保留，以及正式重启告警的触发、标签排除/恢复和删除测试 Pod 后解除，见[重启监控记录](../verification/2026-10-01-log-consumer-restart-monitoring.md)。后续 `pnpm test:log-consumer:notifications:live` 已实际验证 Prometheus→隔离 Alertmanager→本地 Webhook 的四阶段同指纹通知，并恢复原通知配置字段，见[本地通知记录](../verification/2026-10-01-log-consumer-local-notifications.md)。后续 `pnpm test:log-consumer:notification-retry:live` 已验证同一 firing 的 HTTP `503 → 503 → 200` 重试、四阶段交付及配置恢复，见[重试记录](../verification/2026-10-01-log-consumer-notification-retry.md)。后续 `pnpm test:log-consumer:notification-restart:live` 已验证 Alertmanager Pod 重建后同指纹重新送达及后续恢复，见[重建记录](../verification/2026-10-01-log-consumer-notification-restart.md)。重建丢弃 emptyDir，依赖 Prometheus 重发，不证明持久通知队列恢复。后续 `pnpm test:log-consumer:notification-outage:live` 已验证从首次有效请求起连续 60 秒 HTTP 503，同指纹拒绝 37 次后于 60000 毫秒恢复交付，四阶段通知与配置恢复通过，见[持续故障记录](../verification/2026-10-01-log-consumer-notification-outage.md)。具体外部接收器、持久通知恢复、HA、任意时长故障和容量/P99 仍未验收；删除 Pod 的解除不代表健康服务恢复，本地监控配置不作为生产监控模板。

```powershell
docker build -f deploy/containers/log-consumer.Dockerfile -t fullnet-log-consumer:<fixed-tag> .
helm template fullnet-log-consumer deploy/helm/fullnet-log-consumer --set enabled=true --set experimental=true --set image.tag=<fixed-tag> --set configurationSecretName=<consumer-secret> --set caSecretName=<public-ca-secret>
```

可重复的本地部署实验为 `pnpm test:log-consumer:kubernetes:live`：先构建 `fullnet-log-consumer:local-20260930` 镜像，脚本在固定 `kind-fullnet-local` 集群创建随机独立命名空间，连接隔离的真实单节点 Kafka/ES 测试容器。证书包含实际 CRL 分发地址，沿 Helm 的 Production/Online 配置运行；检查正常事件读回、毒消息 DLQ、提交 Offset、证书吊销时保留位点与换证后的重放。运行结束清理测试 Secret、证书、命名空间与下游容器，结果存于 `artifacts/log-consumer-kubernetes/result.json`。测试不会修改业务部署；它覆盖 Kubernetes 消费者到外部 Broker/ES 的功能边界，不代表 Kafka/ES 集群内 HA、网络策略执行或容量已验证。

2026-09-30 此实验本地通过：3 条来源记录对应 2 条 ES 文档和 1 条 DLQ，最终 Offset=3、lag=0；吊销 ES 证书后宿主以脱敏 HttpRequestException 退出 1，Offset 保留 2、有效事件不进入 DLQ，换证后自动重放。实际 CRL 请求 4 次，测试资源清理完成后才写通过工件，见[验证记录](../verification/2026-09-30-log-consumer-kubernetes-delivery.md)。这是 ES 私有 CA/CRL 的本地正反与恢复证据，不包含 OCSP 或部署目标实际 PKI；独立消费者的 experimental 开关不因单项实验自动解除。

### 3.1 后台直发的处理与所有权

候选路径保留双通道和统一快照预算，后台向长生命周期日志专用 Producer 连续提交；以 DeliveryReport 或有界在途 ProduceAsync 跟踪，不逐条串行 await ACK、每事件 Flush 或无界 Task.WhenAll。客户端批量、压缩、消息/字节与在途上限独立设置。分别计量应用缓冲与 SDK/native 副本，定义所有权转移、QueueFull、回调/超时、失败、停机及释放时点，不能只统计应用 Count。

可选适配项目现已定义 `FullNet:Logging:Kafka` 的强类型 Producer 配置和独立应用待确认双维预算。配置分别指定普通/优先 Topic、TLS/SASL、SDK 队列条数/KiB、单消息尺寸与交付/停机超时；`acks=all`、幂等和至多 5 个在途请求为不可降低的基线。单通道原型在 SDK 入队前预约，按投递回调或同步失败释放，并区分 SDK `QueueFull` 与应用预算耗尽；事件 ID 的 key 字节也计入消息尺寸与待确认预算。双通道组合器分别持有普通和优先 Producer/队列，先停止两路提交，再以同一截止时间分配 Flush 剩余预算。Hosting 的最小快照契约只向可选出口提供安全 UTF-8、同一 JSON 中的管道事件 ID 和优先级；缺少可信 ID 时拒绝外发。Host.Api/Worker 在非生产选中直发时绑定该节并创建双 Producer；后台快照仅走 Kafka，不同步写 Console/旧 ES，停机按双通道排空预算后释放 Producer。本地真实 Broker ACK 与 Host.Api Native AOT 双通道路径已验证；SDK/native 峰值及包括 native Dispose 的总停机耗时仍待测量。

Producer 本地接收不等于 Broker 确认。普通/优先 Topic 不自动隔离同一 Producer 的共享队列；必须验证独立 Producer 预算或可证明的独立预留与调度，普通洪峰不能用尽错误日志发送容量。当前分别记录本地提交拒绝、Broker 投递成功/失败、停机未确认及 SDK 清理失败的低基数指标，标签只有普通/优先与固定结果；不把失败递归写回同一 Kafka Sink。指标可见不代表重放能力，真实 Broker 失败恢复和告警尚须验证。

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

固定 Kafka 4.1.2 与 Logstash 9.5.4 的本地单事件实验已使用独立持久卷、`enable_auto_commit=false`、`queue.checkpoint.writes=1`：ES 不可达时 PQ 中存在事件且 Broker Offset 已提交；SIGKILL 后保留 PQ 卷并停止 Broker，重启 Logstash 后原 LogEventId 从队列恢复一次，随后 Broker 重启的 Offset 仍为 1。该实验没有覆盖 checkpoint/fsync 与 Offset 提交之间的所有崩溃窗口，也没有完成 ES Bulk、DLQ 或目标平台验收，详见[PQ 重启记录](../verification/2026-09-30-logstash-pq-restart.md)。

[Elastic 官方 Kafka/PQ 说明](https://www.elastic.co/docs/reference/logstash/tips-best-practices)进一步明确：Logstash Kafka 输入不能保证事件安全写入 PQ 后才提交 Offset，PQ 写入慢或阻塞时可能提前提交。输入插件对 `enable_auto_commit=false` 的“写入队列后提交”说明不能解释成安全落盘顺序保证。因此上述单事件恢复实验不能支持原 `ackBoundary=PersistentQueue` 可靠档准入；该 Logstash/PQ 组合当前被阻塞，不再仅以补充 SIGKILL 样本或调整检查点参数作为放行路径。

另一个 SinkConfirmed 候选也已实测排除：Vector 0.58.0 Kafka Source → Elasticsearch Sink 开启端到端 ACK 与部分失败重试后，永久 Bulk 单项 400 被丢弃，后续成功事件使 Broker Offset 越过该坏记录。429 重试期间未提交的局部正确性不能掩盖连续提交缺口；详见[Vector 消费确认实验](../verification/2026-09-30-vector-kafka-es-boundary.md)。该结论只覆盖所测版本/配置，不代表所有 Vector 部署，但本项目可靠档不可使用这套组合。

[Kafka Connect 插件源码筛查](../verification/2026-09-30-kafka-connect-sink-screen.md)还排除了 IBM Elasticsearch Sink 固定提交 `276c755`：Bulk 单项错误只记录日志，`flush()` 仍成功；Confluent 插件的许可及实际确认顺序仍待核对。LG00 现纳入独立平台进程的最小 SinkConfirmed 研发候选，先证明逐项结果、可靠 DLQ 与每分区连续 Offset，再做故障和容量实验。实验不使 Kafka 消费路径可用于生产，也不把消费者装入 API/Worker 或业务 Outbox。

确认方式独立于 Collector/ApplicationKafka 入口。`ackBoundary=PersistentQueue` 只对能证明安全持久接收先于连续 Offset 提交的实现准入，现有 Logstash Kafka 输入/PQ 不满足这一证明条件；`SinkConfirmed` 只对能证明逐项存储/可靠隔离后提交连续 Offset 的具体消费者准入。后者可由 Kafka 保留待处理记录而省去重复 PQ，但必须验证保留余量、重平衡/重启、部分成功与持久隔离。不能仅将 Logstash `queue.type` 改为 memory 就宣称 SinkConfirmed；未准入组合拒绝启动。

原 Logstash Kafka 输入实验配置为 enable_auto_commit=false、queue.type=persisted、queue.checkpoint.writes=1、有限 queue.max_bytes 和可恢复卷；其 Offset 不代表 ES 索引成功，官方也不保证它只在 PQ 安全落盘后推进。它不能作为本项目要求的可靠持久交付边界。重新选择的消费者每分区只能在连续已持久接收，或最终写入/可靠隔离记录之后提交下一 Offset，不得因后续记录先成功越过缺口。若选择 PQ，队列不复制数据，卷损坏/永久丢失风险及受控回放 RPO 仍须单独验证。

PQ 事件完成条件为各目标逐项成功，或坏记录已可靠隔离。HTTP 200 不能代表整批 Bulk 成功；400/404 映射失败、429/5xx、超大记录、未知 schema 和 DLQ 满分别处理。核验所选插件默认的丢弃/重试行为，不能直接宣称 DLQ 开启即可靠隔离。不可证明隔离持久性的插件不得准入该可靠档；隔离失败时停止接收/处理并告警，不越过未完成记录。隔离存储有容量、保留和访问控制，故障内容不回写原 Topic。

checkpointWrites=1 在每事件写入后强制检查点，官方提示可能严重影响性能；它是原 Logstash/PQ 实验配置，不修复输入插件的 Offset 顺序限制。若其他准入实现使用 PQ，LG00/LG08 必须记录写入/检查点耗时、持久存储延迟、持续消费和恢复追赶能力，分别验证应用、采集和消费瓶颈；不能为了吞吐静默扩大刷盘丢失窗口。LG00 任一入口/消费者/确认组合未通过只关闭该组合；已验证的其他组合可继续使用，不自动切换消费者组件或新增自研消费项目。

### 6.2 ES 重放去重

本阶段选择固定 UTC 事件日期索引，例如 fn-logs-{routeVersion}-{class}-{yyyy.MM.dd}，索引名由受控路由表、首次冻结的 OccurredAtUtc/IndexRouteVersion 生成；不接收业务提供的任意索引名，不按重放时间或当前路由版本重新分桶。显式关闭 data_stream 和 ILM rollover 写入模式，保留固定日期索引的生命周期清理。

索引路由配置默认关闭；成对显式配置 `FullNet:Logging:IndexRouteVersion` 与 `IndexRetentionDays` 后，宿主在最终受限快照中覆盖调用方同名字段，按原事件 UTC 时间冻结 `OccurredAtUtc`、`ExpiresAtUtc` 与版本。Helm `logging.indexRouteVersion`/`indexRetentionDays` 同样成对设置；ApplicationKafka 入口缺少正值时拒绝启动，Collector 仍可不带索引路由运行采集预览，但该输出不能进入新消费者的 ES 写入路径。两入口在配置相同时使用同一宿主快照。独立消费者类库要求 Kafka key/JSON ID、带时区事件时间和冻结路由字段一致；写入前还必须用受控的“版本→保留天数”映射核对精确到期时间，只对未过期事件构造 `fn-logs-{version}-{class}-{UTC 日期}`。缺失、矛盾、未知版本、延长到期或过期均不获得索引名。普通 `ILogger` 提供的 `security` 分类在最终快照降为 `diagnostic`；专用可信安全事件入口尚未建立。独立顺序消费者已实现试验链路；应用入口生产配置按本地验收准入，完整端到端、容量及消费者生产准入仍按各自实际测试范围验证。

LogEventId 映射 document_id，使用 index 动作，使同一事件重试始终写同一索引/ID；默认 ES 自动 ID 不满足该契约。去重承诺只覆盖当前路由版本/日期索引的保留窗口，不是全链 Exactly-Once。受控重放必须保留原始时间、路由版本与 ExpiresAtUtc，到期记录不再写入，不能重建已过期索引。索引迁移需要旧路由重放映射及单一写入所有权；变更路由不直接复制活动事件到另一个索引。无稳定 ID 的历史日志只按旧查询/尽力收集路径兼容，不用内容哈希合并两条真实事件，不授予同等去重承诺。

测试覆盖跨午夜、跨月、消费者重启、部分 Bulk 重试、写成功但 Offset 未提交和历史重放。data stream/rollover 作为后续单独评审能力，必须补充跨 backing index 去重方案，不能只依赖相同 ID。固定日期索引的分片、查询与写入成本在 LG08 用真实数据校准。

可靠阶段是“应用入队 -> 可恢复缓冲 -> Broker 确认 -> 最终存储确认”。至少一次承诺只从已验证的持久边界开始；采集插件若仅确认本地 SDK 入队，必须先证明 ACK/检查点语义或替换该发送边界，不得宣称 Broker 已持久化。Kafka ACK 不代表已索引。消费者可在下游确认或自身持久队列确认后提交 Offset；后一种必须验证持久队列恢复/刷盘语义。

应用内存崩溃、磁盘满、节点丢失、源文件轮转、保留超期都可能形成缺口；用 LogEventId 和故障窗口对账。节点本地磁盘只能承诺该节点可恢复，PV 的节点/区域失败能力也须实测。应用无限故障、资源有界、非阻塞和绝不丢失不能同时保证；B0/B1 不因外部日志系统失败降低语义。

## 7. 告警与验收

记录应用深度/容量/丢弃、采集缓冲字节/最老年龄、Producer 成功/失败、Kafka Lag/最老年龄/保留余量、ES 每项写入失败/延迟、隔离数、恢复追赶速度。Fluent Bit 候选的三路输出已设稳定别名，Priority 与 B2 分别以 `fluentbit_output_dropped_records_total` 和 `fluentbit_output_retries_failed_total` 建立丢失告警；这两个指标分别计记录和重试耗尽的 chunk，不能相加解释为事件数。Priority 无限重试可能长期没有这两类丢失计数，因此另以五分钟窗口内重试增加且成功计数不增加、持续两分钟的条件建立 `FullNetFluentBitPriorityOutputRetryWithoutProgress`。该规则识别输出整体没有进展，若其他 Priority 记录仍成功则不能证明某条记录没有卡住；也不替代缓冲最老年龄、磁盘使用和事件 ID 对账。采集器的 `up=0` 只能发现仍被服务发现保留的目标；`FullNetFluentBitDaemonSetUnavailable` 另以 kube-state-metrics 的不可用副本数发现 DaemonSet 存在时的 Pod 缺失。DaemonSet 整体被删除、期望副本为零及 kube-state-metrics 自身不可用仍需平台独立监控。固定镜像故障探针已从采集器本地指标端点读取两路丢弃与重试耗尽计数，并与删除源日志后的缺失 ID 对账；固定 Prometheus 的 `promtool` 规则测试覆盖丢弃触发、Priority 仅重试不触发丢失告警、持续无进展触发新告警、成功后不误报、DaemonSet 不可用与恢复以及归档别名。专项 CI 烟测以真实 Prometheus 抓取不可达 Forward 的两路输出指标并检查丢失 firing 告警，新无进展及 DaemonSet 规则仍待目标平台抓取与通知实测。即使烟测通过，目标平台抓取、标签和通知仍须故障注入验收。标签不含原 URL、用户、租户、异常消息或 LogEventId；关联字段可在受保护日志中查询。

固定 Fluent Bit 4.1.1 的本地探针确认：旧的 `FullNetFluentBitSpoolHigh` 表达式所需 `fluentbit_storage_chunks_up/total` 在 v1 指标端点没有对应时间序列；v2 端点存在 `fluentbit_output_chunk_available_capacity_percent`。采集器 Prometheus 抓取使用 `/api/v2/metrics/prometheus`；`FullNetFluentBitForwardQueueCapacityLow` 仅在两路 Forward 输出队列可用 chunk 容量低于 20% 持续五分钟时报告。它不是 `emptyDir` 字节占用或丢失前预警：固定镜像的 `64KB` 逻辑队列已出现输出丢弃，指标仍为 93.6% 可用，须依靠独立输出丢弃告警检测已发生的损失。原 `FullNetFluentBitDiskFull` 只读取 PVC 可用字节，但候选为 `emptyDir`，因此已移除；`FullNetNodeDiskPressure` 仅表示节点层磁盘压力。固定镜像在受限 tmpfs 上报 ENOSPC 和输入 chunk 写入失败时仍保持运行，输出丢弃计数为零，详见[缓冲压力记录](../verification/2026-09-30-fluent-bit-storage-pressure.md)。这不能模拟 Kubernetes 2 GiB 限额的驱逐时序；目标平台须补齐 `emptyDir` 使用量、满额故障和恢复证据，未完成前不得宣称采集磁盘满可被及时告警。

同一 ENOSPC 探针中，输入的 `fluentbit_input_ingestion_paused` 和 `fluentbit_input_storage_overlimit` 均为零；输入接收计数也可能在首次写失败后继续短暂增加。现有输出丢弃、输入暂停和超限指标不能单独判定该写盘故障，目标平台需另建独立错误观测并按 LogEventId 对账。

实际容量至少报告事件/秒、字节/秒、请求 P50/P95/P99、CPU、分配、磁盘/网络、重复/丢失/可查询延迟。故障矩阵覆盖 SIGKILL、采集器重启、Pod 重建、节点故障、磁盘满、Broker 不可用、ES 429/5xx/部分 Bulk 失败、消费者重平衡和写入后未提交 Offset。没有证据不能升级为生产容量或端到端 Verified。

容量验证分别记录调用线程、两路后台输出、节点采集器和消费写入端的持续处理能力；普通洪峰、错误洪峰和两者混合时同时检查优先日志年龄/丢弃与请求 P99。比较日志等级关闭、当前管道、目标快照 Summary、获许可投影、Direct/Kafka 的同环境差异。请求无明显变慢但持续丢日志、采集延迟增长或消费无法追赶，均不能作为容量通过。

本地请求延迟已有显式命令 `pnpm test:logging:request-latency:live`：真实 loopback Kestrel 复用正式 B2 中间件，固定并发 8、每档 200 次预热/2000 次实测，比较 Disabled、100% Summary、安全分页投影，正反序各一轮。最终输出核对唯一 RequestId、预期 500 和投影；释放前丢弃快照不能证明停机全过程无其他日志丢弃。2026-10-01 实测 P99 分别为 0.620–0.843ms、2.982–3.698ms、3.315–3.880ms，每档发送窗口仅 108–217ms；这证明本地测试端点的局部特征，不代表长期容量或完整业务 API P99。客户端/服务端共享进程的 CPU/分配只计发送窗口，不能当作完整日志成本。详见[请求延迟验证记录](../verification/2026-10-01-logging-request-latency.md)。
新增 `pnpm test:logging:request-latency:sustained`：同一端点每档 5000 次实测、目标 500 次/秒、最多 8 个发送循环，约 10 秒，六档正反序。发送超过 30 秒取消，HTTP 延迟与调度落后分别保留。2026-10-01 实际速率 499.4–499.7 次/秒，Summary HTTP P99 1.707–1.715ms，安全投影 1.680–2.008ms，最终日志和投影全部对账。调度落后 P99 约 15.5ms，存在延后/追赶，HTTP 延迟不含这部分等待；该有限场景不代表均匀负载、长期容量或完整业务 API。详见[持续请求验证](../verification/2026-10-01-logging-request-sustained.md)。
Collector 真实 HTTP 投影回放已有本地入口 `pnpm test:observability:request-replay:live`（先生成持续请求档）。固定 Fluent Bit/正式 CRI 与可信 Pod 路由过滤对 5200 条真实投影快照实际回放，Priority 520、B2 4680，逐 ID/字段对账，5200 个正文伪造 Collector 标签的 ApplicationKafka Pod 镜像全部被排除。该实验使用文件出口替代 Forward，固定 15 秒预装回放，不能当作 Kafka/ES/ACK 或持续吞吐证据。详见[真实请求回放验证](../verification/2026-10-01-collector-request-replay.md)。
ApplicationKafka 真实 HTTP 入口已有本地验收 `pnpm test:logging:request-kafka:live`（先 Release 构建 IntegrationTests）。正式静态出口通过 TLS Kafka 4.1.2 交付 Summary/Projected，各 5000 次实测、目标 500 次/秒；最终双 Topic 高位点收据各确认 5200 唯一 RequestId/520 预期 500，投影完整，Console 镜像 0 字节。2026-10-01 HTTP P99 为 2.246/1.947ms，调度落后约 15.5ms 单独记录；这是单轮局部测试，不可与不同轮次的 Collector 文件回放比较后宣称路线优劣。见[Kafka 请求验证](../verification/2026-10-01-logging-request-kafka.md)。
真实 HTTP→ApplicationKafka→ES 的本地闭环已加入 `test:logging:request-kafka:live` 五项分片：第二项 Projected 请求记录在宿主排空后由正式最多 8 条批次协调器消费，正式 Bulk Sink 写固定日期索引，实际 Kafka Committer 推进连续确认位点。独立的 UTC 日期/版本/分类索引名检查与 ES count/全部 `_mget` 来源深比较共同验收，最终每分区 committed 必须等于冻结高位点；隔离或重试不能通过。该项 ES 使用已有本地明文夹具/仅测试地址改写，范围见[请求至 ES 验证](../verification/2026-10-01-logging-request-es.md)。第三项在请求前启动正式独立消费进程，使用真实 HTTPS ES、私有 CA 与受限 API Key；父进程只读取 Broker 收据，等待子进程提交最终高位点并核对全部文档、DLQ 为零。排空后结束本测试子进程并清理夹具才写通过报告；Development 临时 CA 的 NoCheck 不改变 Production 吊销策略，也不代表优雅停机、Collector 同轮比较或长期容量。详见[独立消费者 HTTPS 请求闭环](../verification/2026-10-01-logging-request-process.md)。

第四项已补齐 Collector 真实 HTTP 快照的有限 CRI 回放至 TLS Kafka、独立消费者、HTTPS ES。固定 Fluent Bit 使用现有可信 Pod 标签过滤和候选 Kafka 出口；独立镜像 ID、完整输入指标、正常退出后的最终高位点共同防止替代或尾部误投假通过，ES 全文档字段对账。该 Kafka 出口仍只在测试夹具中，不改变参考 Forward 部署。结果和边界见[Collector Kafka 全链回放](../verification/2026-10-01-collector-kafka-chain.md)，没有持续实时采集或两路线同轮性能结论。

第五项验证请求期间的 Collector 实时采集：初始空 CRI 目录、测试 Console 桥接与固定 Fluent Bit 在请求前启动，必须在实测发送窗口内收到具体实测 HTTP 记录并确认其同分区提交位点，结束后再核对全部文档、镜像排除和最终 Offset。此证明不只依靠结束后的回放或排空；桥接包含本地测试开销，不是 Kubernetes 节点运行时采集或路线性能比较。详见[Collector 实时采集验证](../verification/2026-10-01-collector-live-stream.md)。

## 8. 官方参考（2026-09-28 查阅）

- [微软高性能日志](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/high-performance-logging)：LoggerMessage 源生成。
- [微软同步日志边界](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-10.0#no-asynchronous-logger-methods)：慢存储后台化。
- [旧 Elasticsearch Sink 归档说明](https://github.com/serilog-contrib/serilog-sinks-elasticsearch)：不再作为长期扩展主线。
- [Elastic 官方 Sink](https://www.elastic.co/docs/reference/ecs/logging/dotnet/serilog-data-shipper)：官方 Sink 也不提供 durable mode。
- [Confluent .NET 客户端](https://docs.confluent.io/kafka-clients/dotnet/current/overview.html)：批量与投递报告。
- [Kafka Producer](https://kafka.apache.org/41/configuration/producer-configs/)、[Topic](https://kafka.apache.org/41/configuration/topic-configs/)：采用与项目 Broker 基线对应的文档，不凭 Java 参数默认值推断 librdkafka。
- [Fluent Bit Kafka](https://docs.fluentbit.io/manual/data-pipeline/outputs/kafka.md)、[Grep](https://docs.fluentbit.io/manual/data-pipeline/filters/grep.md)、[Kubernetes emptyDir](https://kubernetes.io/docs/concepts/storage/volumes/#emptydir)：插件与存储边界需真实测试。
- [Fluent Bit 性能](https://docs.fluentbit.io/manual/administration/performance)、[背压](https://docs.fluentbit.io/manual/administration/backpressure)：输入线程、缓冲模式和输出限额的行为，不能替代本项目版本与环境的容量实测。
- [Fluent Bit 4.1 监控指标](https://docs.fluentbit.io/manual/4.1/administration/monitoring)：输出别名、记录丢弃和重试耗尽计数的定义；实际抓取与告警仍须在目标平台验证。
- [Logstash Kafka/PQ Offset 限制](https://www.elastic.co/docs/reference/logstash/tips-best-practices)、[Kafka 输入](https://www.elastic.co/docs/reference/logstash/plugins/plugins-inputs-kafka)、[PQ](https://www.elastic.co/docs/reference/logstash/persistent-queues)、[ES 输出](https://www.elastic.co/docs/reference/logstash/plugins/plugins-outputs-elasticsearch)、[DLQ](https://www.elastic.co/docs/reference/logstash/dead-letter-queues)：队列确认、逐项失败与刷盘代价；不能用单事件恢复代替安全落盘与提交顺序保证。
- [Elastic 重复事件与 rollover](https://www.elastic.co/blog/efficient-duplicate-prevention-for-event-based-data-in-elasticsearch)：同一 ID 不自动保证跨索引唯一。
