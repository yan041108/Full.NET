# 可配置日志交付与请求详情开发计划

> 执行约束：按仓库 AGENTS.md 逐任务实施；不自动创建工作树、派发子代理或切换独立执行流程。用户已授权修复方案并开始执行；仅按实际验证勾选任务，生产部署/切流仍须单独批准。

**Goal:** 保留 ILogger/Serilog 和审计边界，补齐截图字段及安全请求/返回详情，提供可选采集缓冲、Kafka 和 Elasticsearch 的纵向闭环。

**Architecture:** 应用快照以 Local/Collector/ApplicationKafka 选择入口；Collector 经 Console 与平台缓冲，ApplicationKafka 经可选静态适配有界后台直发。Collector/ApplicationKafka 入口已按本地测试准入，默认仍为 Legacy。Kafka 下游采用独立 Host.LogConsumer、逐项 ES Bulk、可靠 DLQ 确认及分区连续 Offset 提交；原 Logstash/PQ 不作为正式确认组合。固定事件日期索引以 LogEventId 去重。Restricted 仅存 B1，独立授权、到期/清理；Vue 三页签。归档及更大容量范围列为后续可选扩展。

**Tech Stack:** .NET 10、ILogger/LoggerMessage、Serilog、System.Text.Json 源生成、现有 Dapper 执行器、SQL Server/MySQL、Fluent Bit、可选 Apache Kafka/Logstash/Elasticsearch、Vue 3/TypeScript。版本沿集中依赖与受信平台基线，新平台版本由 LG00 固定镜像/插件及摘要，不自动升级。

## 本期收尾清单（2026-10-02，唯一活动清单）

**本期状态：已完成本地验收，可结束本期开发。**最终代码、浏览器范围和回归证据见[收尾验收](../../verification/2026-10-02-logging-module-closeout.md)。

本期按 R-20260930-local-acceptance 的本地实际测试验收；下文按日期保留的演进说明与 LG00—LG08 原始设计不是重复待办。原始勾选仅反映当时记录，未覆盖全部候选项的部分在下表明确缩限，不自动宣称全部历史矩阵通过。

| 范围 | 收尾状态与证据 |
| --- | --- |
| LG00/LG05/LG06 入口和交付 | 已完成本地准入：Collector、ApplicationKafka 可配置，独立消费者、HTTPS ES、可靠 DLQ 和连续 Offset 已接通；见本文件各实际运行切片与模块说明 |
| LG01—LG04 字段、安全投影和 B1 | 已完成本期受控字段、双库详情授权/到期、微批条数/字节预算；B2 不承载原始受限详情，任意请求/响应正文不自动采集 |
| LG07 Vue 页面 | 已完成：三页签/按需加载、服务端分页、到期/历史记录、撤权后新会话及 API 403、三页低视口布局；SQL Server 4/4、MySQL 含页面回归 8/8，组件 23/23、构建/包体通过 |
| LG08 实测与恢复 | 本地请求 P99、正反序两路线比较、链路对账、局部容量及故障恢复已完成记录；仅对报告中的负载与故障范围有效 |
| 最终交付 | 独立审查、受影响回归和文档核对完成；日志改动随本期交付提交，验收规则已单独提交 `91176bfd`，既有无关文件保留 |

后续可选扩展：对象存储归档/独立 File Sink 全副本秘密审计；专用安全事件入口；扩大到 1 万在途、长时间 Soak、节点/磁盘永久丢失及全部跨拓扑切流矩阵。它们不阻断本期本地验收；无相应实测继续保留 `Capacity-not-verified`，不能承诺跨重启零丢失或指定生产最大吞吐。生产实际部署和切流仍需明确授权。

## 批准依据与关联（历史验证证据）

**2026-10-01 秘密跨出口验证切片：**新增少量诊断事件经 Collector Console/测试文件与采集器，或正式 ApplicationKafka 出口，接 TLS Broker、正式独立消费者和私有 CA/受限 API Key HTTPS ES。原始日志含秘密字段、嵌套凭据、异常消息、原 IP/服务端地址及受限请求/返回内容。来源与 Broker 逐 ID/字段比较；全部 ES 文档对账、最终 Offset/DLQ 和资源清理通过后写独立报告。使用每次新生成的秘密哨兵，原始值不保存为证据；字节断言先用原始哨兵证明会拒绝，防止空集合/失效断言通过。不扩展本次结论到旧 Sink、独立 File Sink、归档、B1 数据库或完整安全审计。首次实跑 Collector 因脱敏模板丢失 Instance 只输出 2/3，已用失败单测定位，保留严格 UUID 格式的 Instance 修复；任意/凭据字符串仍拒绝，补做 AOT 分析与原生验证。

- [x] 建立三条诊断事件与非空哨兵检查、来源身份和实际双 Topic 收据验证。
- [x] 正式独立消费者至 HTTPS ES、全部字段/最终 Offset/DLQ/清理实跑通过。
- [x] 接入聚焦分片与影响选择，完成复核、相关验证与文档。

修正版两路线 2/2、每路线三条 ES 文档全部字段/最终 Offset/DLQ/清理对账通过；日志单测 318/318、双库宿主回归 2/2、AOT 分析无警告/错误、API AOT 架构 73/73、新鲜 Linux 原生发布及原生日志运行 1/1 通过。工具 54/54、治理 57/57，1106 项集成发现闭合。独立复核的共享夹具漏选已按失败测试修复。详见[秘密跨出口验证](../../verification/2026-10-01-logging-secret-boundary.md)，不升级旧 Sink/归档/B1 库/Worker 原生或容量范围。

**2026-10-01 两路线进程隔离正反序比较：**真实请求基准以同一个独立 Benchmark 子进程入口运行 Collector/ApplicationKafka，父进程承担实时 CRI 桥接和同一 Broker 观察，不进入被测进程 CPU/GC 统计。每档 200 预热/5000 实测/500 次每秒、Projected、批次 8、TLS Kafka/HTTPS ES/相同受限 API Key；按 C→K、K→C 四档各用独立夹具与原始目录。记录请求 P50/P95/P99/节奏、测量进程 CPU/GC/发送末工作集、实际日志字节和排空时间；每档 5200 HTTP/520 预期 500、全部字段、镜像输入排除、最终 Offset/ES/DLQ/退出清理必须通过。子进程只测量，外部收据验证显式延期由父进程完成，不单独写链路通过。共享 Docker 主机/测试 CRI 桥接成本仍披露，不称为应用独占资源或生产最大容量，不因单轮值改变默认路线。

- [x] 比较用例实际 RED；统一独立请求入口及有界进程/观察管理。
- [x] 四档正反序真实链路运行，原始样本/逐项交付和资源清理。
- [x] 日志验证、独立审查、工具/发现与比较记录、配置选择建议。

最终修正版四档通过（分片 1/1、0 跳过），每档恰有 5000 实测日志、5206 文档全部字段/最终 Offset/DLQ/清理对账。独立复核的构建配置与预热跨阶段问题已修复并重跑。Collector P99=2.5027/2.1733ms，ApplicationKafka=2.2878/2.2369ms，顺序变化未形成稳定优势，继续配置选择。日志单测 315/315、原 TLS Kafka 请求回归 1/1、Local 六档、工具 54/54、治理 57/57；1104 项集成发现闭合，构建无警告/错误。见[两路线比较](../../verification/2026-10-01-logging-route-comparison.md)，不扩大业务 API 或最大容量结论。

**2026-10-01 Collector 实时采集切片：**延续相同 200 预热/5000 实测/500 次每秒安全投影档，Kafka、HTTPS ES、正式独立消费者和固定 Fluent Bit 全部在请求前启动。测试桥接器持续读取正式 Collector Console 文件，将完整 JSON 行写成新增 CRI 行及独立 ID 镜像，Fluent Bit 实际 tail 绑定目录，不能预装请求来源。Broker 独立读取器在请求期间保存收据，并必须证明 HTTP 日志已收到、消费者提交大于零；请求结束后排空桥接器，全部来源/镜像指标/位点/ES 文档按既有标准验证。桥接内存与诊断有界、取消和任务释放贯通。报告明确本地 CRI 桥接与共享进程开销；本切片不单凭不同轮次延迟决定路线优劣。

- [x] 新实时用例 RED/GREEN；绑定目录、受控实时桥接与在途消费证据。
- [x] 原回放/实时全链及受影响日志验证，独立审查与记录。

最终实跑五项请求分片 5/5、0 跳过、275.061 秒。实时 Collector 观察到实测期间 108 条 HTTP 收据，具体 Priority offset=20 在窗口内 committed=56，排除预热和结束后确认；ES 5206 文档、10412 输入、5206 镜像排除、最终位点 General=4686/Priority=520、DLQ=0，清理完成。请求档约 499.56 次/秒、P99 3.1380ms；同轮 ApplicationKafka P99 2.5557ms，仅单轮观测且桥接成本不同，不决定路线优劣。日志单测 315/315、工具 54/54、治理 57/57、1103 项发现闭合、独立审查修复已复核。见[实时采集记录](../../verification/2026-10-01-collector-live-stream.md)。

**2026-10-01 Collector Kafka 全链回放：**真实 Collector Kestrel 安全投影档为 200 预热/5000 实测/500 次每秒；宿主释放后将其全部原始快照写成 CRI 文件，固定 Fluent Bit 4.1.1 使用现有可信 Pod 标签及分类过滤、候选 Kafka 双出口，LogEventId 作为 Key，TLS/主机名校验与有界 Producer 缓冲。相同来源另写 ApplicationKafka Pod 镜像并伪造正文标签，必须全部排除。启动正式批次 8 独立消费者、HTTPS ES 私有 CA/受限 API Key；Broker 按完整唯一 ID 与允许的采集元字段核对来源，等待每分区 committed=最终高位点，ES 全字段一致、DLQ=0。仅验证有限回放，不称为持续采集或同轮路线性能比较；不改变 Forward 默认配置。五分钟总预算，资源释放后才写通过报告。

- [x] 新集成用例实际 RED；最小 Collector 请求入口与共享夹具扩展。
- [x] 固定 Fluent Bit 实际 Kafka 插件全链运行、镜像排除与逐字段/位点对账。
- [x] 影响集、独立审查和真实范围文档；保留原 HTTPS/Broker 验证。

最终加固版本四项请求分片 4/4 通过、0 跳过，229.238 秒。Collector 实际输入 10412、Kafka B2=4686/Priority=520，5206 条不同 ID 镜像排除；正常退出后最终 Broker 位点与独立消费者 Commit 一致，ES 5206 文档/两索引全字段一致、DLQ=0，资源清理已确认。HTTP 请求窗口约 499.89 次/秒、P99 2.6101ms，仅有限回放范围。日志单测 315/315、工具 54/54、治理 57/57、1102 项发现闭合，独立审查两项修复已复核。见[Collector 全链验证](../../verification/2026-10-01-collector-kafka-chain.md)。

**2026-10-01 独立消费者 HTTPS 请求闭环：**真实 Kestrel/ApplicationKafka/TLS Broker/5000 次安全投影请求，启动正式消费者子进程使用批次 8、ES 私有 CA 与 fn-logs-1-* 范围的 write/auto_configure API Key。子进程在生产请求前启动，可与请求并发消费；排空最多 120 秒，来源双 Topic 全部记录和 ES 文档/投影逐 ID 对账，实际 committed=最终高位点，DLQ 高位点为零。进程必须仍存活且输出启动声明；排空后强制结束仅本任务子进程并确认退出，CA/容器清理后写独立通过报告。Development 临时 CA 使用 NoCheck，不扩大 Production 吊销策略，不称为优雅停机或最大容量证据。

- [x] 新真实用例 RED/GREEN；受控私有 CA/API Key ES 夹具和有界独立进程。
- [x] 全链实际本地运行、全部文档/最终位点/DLQ/进程退出与夹具清理对账。
- [x] 相关日志/工具/治理/发现验证、独立审查与实际范围说明。

最终修复后真实请求分片 3/3 通过、0 跳过：独立进程至 HTTPS ES 共 5206 文档/两个索引逐字段一致，General=4686、Priority=520 均等于最终高位点，DLQ=0，进程退出和资源清理完成；HTTP P99 2.7113ms、约 499.63 次/秒，仅限固定局部 HTTP 档。日志单测 315/315、工具 54/54、治理 57/57、1101 项发现无遗漏/重复，构建 0 警告/错误。已修复审查指出的 Kafka 启动/建 Topic 期限和 HTTPS 宿主 SAN 问题。见[独立进程 HTTPS 验证](../../verification/2026-10-01-logging-request-process.md)，不改变实验开关或长期容量结论。


**2026-10-01 HTTP→Kafka→ES 本地闭环切片：**沿用真实 Kestrel/ApplicationKafka/TLS Broker 的安全投影 200 预热/5000 实测、500 次/秒档。固定真实 ES 镜像使用本地明文夹具及已有仅测试 HTTPS 地址改写边界，不外推 ES TLS；Broker 双 Topic 记录使用正式批次协调器/最多 8 条、正式 ES Bulk Sink 与实际 Offset Commit。读至冻结高位点后，查询 ES 全部 LogEventId 的固定索引、来源字段及投影，对账每分区已提交高位点，隔离或重试不得通过。单项 5 分钟，资源释放后写独立结果，非独立消费者进程/并发持续消费/容量证据。

- [x] 建立新闭环失败测试，复用正式协调器并从真实 Broker 记录写 ES。
- [x] 本地实测及全部文档/来源字段/最终 Offset 对账，保留结果和实际范围。
- [x] 日志测试、发现分片、工具/治理、独立审查与文档同步。


最终加固后真实闭环通过：5206 条 ES 文档/两个索引全部字段一致，含 5200 HTTP 日志/唯一 RequestId/安全投影和 520 个预期 500；General committed=4686、Priority=520，分别等于冻结 Broker 高位点，隔离为零。原两项分片 2/2 通过，最终独立索引名/位点保存增强后单项 1/1 再通过；工具 54/54、治理 57/57、1100 项集成发现闭合，最终构建无警告/错误。见[请求至 ES 验证](../../verification/2026-10-01-logging-request-es.md)，不扩大独立进程、ES TLS 或容量结论。

**2026-10-01 ApplicationKafka 真实请求切片：**复用现有 TLS Kafka 4.1.2 夹具、正式静态 `KafkaLogSnapshotExporter.Create` 和 Kestrel B2 中间件，每档 200 预热/5000 实测、目标 500 次/秒/最多 8 循环，Summary 与安全投影各一档。冻结索引路由 1/30 天，请求不等待 ACK；宿主释放排空后读取双 Topic 最终高位点，检查 Key/LogEventId、优先路由、5200 唯一 RequestId/520 预期 500 与投影。保留原始 Broker 字节、HTTP/调度样本；TLS 夹具清理后才生成通过报告，不作为 Collector 同轮对比、ES 写入或最大容量证据。

- [x] 真实测试 RED：缺少出口实现不能通过；实现固定可控 Kafka 请求入口和最终收据验证。
- [x] 本地真实 TLS Broker Summary/Projected 两档、原短档兼容与日志单测。
- [x] 影响集/发现分片、相关工具/治理与独立审查、文档和证据。


最终正式 ApplicationKafka TLS 请求实测通过：Summary/Projected 各 5000 次实测、约 499 次/秒，HTTP P99 2.246/1.947ms；两档从排空后的 Broker 收据各确认 5200 唯一 RequestId/520 预期 500，Projected 5200 组投影，Console 镜像 0 字节。日志单测 315/315、工具 54/54、治理 57/57、1099 项集成发现闭合；原短档六组兼容重跑通过。见[Kafka 请求验证](../../verification/2026-10-01-logging-request-kafka.md)，单轮两档不代替同轮 Collector/Kafka、ES、长期或业务容量比较。

**2026-10-01 Collector 真实请求回放切片：**沿用固定 Fluent Bit 镜像、正式 CRI Tail/可信 Kubernetes 元数据及路由过滤，导入持续请求档 Projected 的 5200 个真实快照，并加入 5200 个不同合法 UUID v7、正文伪造 Collector 标签的 ApplicationKafka Pod 镜像。文件出口替代 Forward，仅验证真实字段、投影、Priority/B2 分流与防重复；固定 15 秒采集窗口，最多 8MiB 输入，失败恢复/清理随机任务容器与临时文件，不作为 Kafka/ES/ACK 或持续吞吐证据。

- [x] 建立逐 ID/路由/内容验收 RED/GREEN，拒绝漏投、重复、错误路由和投影篡改。
- [x] 新增显式真实请求回放 CLI，复用现有采集夹具和可信元数据，保留实际输出与源摘要。
- [x] 本地 Docker 实测、相关部署/工具/治理测试与审查，更新实际范围和说明。


最终 Docker 回放通过：5200 条真实 HTTP 快照/5200 组投影，Priority 520、B2 4680，5200 个不同合法 UUID v7 的 ApplicationKafka 镜像被排除，正文伪造入口标签无效；逐 ID/字段对账完整，容器与临时目录清理确认。部署/判定 12/12、工具 53/53、治理 57/57；独立审查的源摘要错配与清理证据问题修复后重跑。见[真实请求回放验证](../../verification/2026-10-01-collector-request-replay.md)，仅为固定 15 秒有限预装回放，不标为持续吞吐或 Kafka/ES/ACK 证据。

**2026-10-01 固定节奏请求切片：**复用同一 Kestrel/日志端点，新增显式 `--sustained`，并发最多 8、每档 5000 次实测、目标 500 次/秒，首末计划发送跨度 9998ms，最长发送窗口 30 秒。每档仍预热 200 次，三档正反序共六组；保留 HTTP 延迟与单独的调度落后延迟、全部原始样本、最终 5200 个唯一日志/520 个预期 500 与安全投影对账。CPU/分配仅共享驱动进程发送窗口；不改变生产配置，不升级完整业务 API P99 或长期容量结论。

- [x] 固定节奏/时长判定 RED/GREEN，拒绝短跑、非法输入和超过 30 秒的发送。
- [x] CLI 接线、独立输出目录及原短样本兼容；单调时钟与可取消等待，不创建每请求任务。
- [x] Release 实测六档、受影响测试与审查、实际范围和验证记录。


六档持续实测通过：每档约 10 秒，实际 499.4–499.7 请求/秒，Summary HTTP P99 1.707–1.715ms，Projected 1.680–2.008ms；开启日志每档 5200 个不同 RequestId/520 条预期 500，两档各 5200 组投影。调度落后 P99 约 15.5ms，实际发送有延后/追赶，HTTP P99 不含调度等待；不外推平滑请求流或完整业务容量。日志单测 315/315、工具 53/53、治理 57/57、1098 项集成发现无缺漏；原短档六组兼容重跑通过。见[持续请求验证](../../verification/2026-10-01-logging-request-sustained.md)。

**2026-10-01 日志请求延迟切片：**在现有 Benchmarks 项目通过真实 Kestrel/`AddFullNetServiceDefaults`/`UseFullNetRequestLogging` 运行相同测试端点：关闭 B2、100% Summary、安全分页请求/返回投影三档，均含 10% 预期 500。固定并发 8，每档预热 200、实测 2000 请求，正序/逆序各一轮；Console JSON 接入独立本地文件，释放后核对预期日志/投影数量、唯一 RequestId 与预期 500；释放前丢弃计数须为零，不声明停机全过程无其他日志丢弃。报告完整 HTTP 客户端耗时 P50/P95/P99、实际吞吐与整个驱动进程 CPU/分配，保留原始样本。没有认证/数据库/Kafka，不标为完整业务 API P99 或生产容量。

- [x] 验收判定 RED/GREEN：无效延迟、异常响应、漏日志/投影或丢弃不得通过。
- [x] 实现有界可重跑的 `logging-request-latency` CLI，复用真实中间件，失败不留 passed 报告。
- [x] 实际 Release 对照、相关测试与审查、同步说明和实际范围。

**2026-10-01 持续输入与恢复追赶切片：**沿用本地 kind、固定批次镜像/8 条上限、500m/256Mi。单连接有界节奏发送 3000 条、每条 2048 字节 JSON、约 50 条/秒且至少持续 59 秒；第 20 秒暂停隔离 ES 10 秒，恢复后必须在仍有输入时观察到 lag=0，最终位点 3003、逐 ID/内容对账、DLQ 不新增、Pod 不更换/额外重启。采样积压上限 1000 条，发送阶段最多 90 秒、发送结束排空最多 30 秒；只验收本场景，不外推长期容量或请求 P99。

- [x] 结果判定 RED：拒绝缺少真实积压/持续时长、提前跳 Offset、未在输入期间追平与超预算。
- [x] 新增显式 `--sustained --batch-records=8`，持久单 Producer 连接、有限发送/采样/恢复时间，失败时恢复暂停的任务容器并清理；默认模式兼容。
- [x] 本地真实实验、相关工具测试与差异检查，记录资源、积压和恢复时间并更新说明。

最终期限修正后完整真实重跑通过：3000 条/6144000 JSON 字节逐 ID 对账，持续发送 61.366 秒、实际 48.89 条每秒，ES 暂停 10.354 秒，采样最大积压 462 条，恢复后 14.135 秒观察追平且输入继续，最终 Offset=3003/lag=0。发送结束后 2.283 秒确认完毕，绝对期限 30 秒；内存峰值约 60.28 MiB，DLQ 不新增、无额外重启，清理通过。相关测试 10/10、工具 53/53、治理 57/57；独立审查发现并修复排空期限绕过，负例 RED/GREEN 与最终重跑完成。见[持续输入验收](../../verification/2026-10-01-log-consumer-sustained.md)，不覆盖请求 P99、长期 Soak 或更高规模。

**2026-10-01 有界批次协调切片：**最多 8 条，默认 1 条保持原单条模式；显式配置后由单 Poll 循环收集已可用记录，不等待凑批。批量 ES 回执和可靠 DLQ 决定连续完成前缀，每分区提交一次；收集/交付/隔离/提交边界验证分配代数，过期批次失败关闭，未确认记录由监督器重放。

- [x] 建立部分 Retry/隔离失败、成功前缀、跨分区、数字空洞、所有权变更和取消的失败测试；实现 `BatchLogDeliveryProcessor`。
- [x] 宿主可选 `FULLNET_LOG_CONSUMER_BATCH_MAX_RECORDS`（1..8），默认 1；有限条数/合法单记录收集，异常大记录沿原单条隔离，SDK 无界并行和请求线程等待均不引入。
- [x] 验证 Helm 可配置、日志单测、真实 Kafka/ES 故障与重平衡，以及相同 1000×2048 字节/相同资源的吞吐对账；审查、文档与清理完成后记录实际范围。

本轮日志单测 310/310、真实 Linux Kafka/ES 回归 15/15、部署/对账 8/8、工具 53/53、治理 57/57，构建无警告/错误，独立增量审查无阻塞。同镜像同资源 1000×2048 字节突发对账均通过：单条 30.213 秒/33.10 条每秒，最多 8 条 8.456 秒/118.26 条每秒，约 3.57 倍单次观察速率；零缺口、新增 DLQ、额外重启，任务资源已清理。默认仍为 1，不外推持续容量或请求 P99。见[批次协调验证](../../verification/2026-10-01-log-consumer-batch-coordinator.md)。

**2026-10-01 有界 Bulk 传输切片：**扩展 `ElasticsearchLogDocumentSink` 的逐项 Bulk 传输，最多 64 条/1 MiB，请求和响应保持有限超时/字节；原单条 API 复用相同路径。此层不提交 Offset，不确认 DLQ；宿主批次协调与重平衡所有权另行接入后才重跑吞吐对比。

- [x] 单测先失败：混合 201/429/400、目标错配、条数/字节超限、响应超限及取消。
- [x] 实现固定 index/ID 的单次 NDJSON 请求，完整逐项解析，默认单条运行路径兼容。
- [x] 本地测试/构建与真实 ES 批量写入对账、影响集及文档完成；不开启生产消费者门禁。

日志聚焦 302/302、最终 Bulk 传输聚焦 10/10、Linux Kafka/ES 回归 14/14、治理 57/57；构建 0 警告/错误。真实 HTTPS ES 的两文档批量写入、固定 ID 重放与读回通过，独立审查无阻塞问题。详见[Bulk 传输验证](../../verification/2026-10-01-log-consumer-bulk-transport.md)。下一步接有界批次协调、连续 Offset 与分配所有权，再以相同 1000 条场景对比；本次未改变宿主逐条消费模式。

**2026-10-01 消费者本地吞吐基线切片：**复用隔离 kind/Kafka/HTTPS ES 与 Production/Online 配置；正式单消费者实现不变。显式发送 1000 条、每条精确 2048 字节 UTF-8 JSON，120 秒内完成提交；逐 ID `_mget` 核对内容，要求零缺口、DLQ 不新增。记录从发送启动至提交完成的含工具开销耗时、字节吞吐、cgroup CPU/节流及内存峰值；不是应用 P99 或持续输入容量认证。

- [x] `tests/deployment/log-consumer-throughput-proof.test.mjs` 先验证缺失/重复/内容错误拒绝；`eng/testing/log-consumer-throughput-probe.mjs` 实现有界生成与真实对账。
- [x] `log-consumer-kubernetes-smoke.mjs` 增加独立 `--throughput` 与结果目录；保留既有 TLS 吊销/恢复验证，完成后运行基线，检查默认模式兼容。
- [x] 运行本地实验、影响集与完成检查，更新实际证据和日志说明；未测规模不升级。

实际 `pnpm test:log-consumer:throughput:live` 退出 0：1000 条逐 ID/内容对账通过，新增 DLQ=0、最终 Offset=1003/lag=0，无额外重启。单次观察耗时 25.924 秒，38.57 条/秒、78999.59 JSON 字节/秒；消费者 CPU 2.744 秒、节流计数增量 1.263 秒、Pod 生命周期内存峰值 65.47 MiB。对账测试 2/2、工具 64/64、治理 57/57，清理通过，见[吞吐基线](../../verification/2026-10-01-log-consumer-throughput.md)。这是含 CLI/观察开销的突发基线；下一步批量优化须保持逐项确认与连续 Offset，不能将该结果当作持续容量或应用 P99。

**2026-10-01 通知持续故障切片：**从首次有效请求开始用单调时钟计时，持续 60 秒 HTTP 503；故障期间不确认接收，恢复后验证同指纹 firing 的真实交付，再完成四阶段恢复。默认关闭，故障模式互斥，尝试记录保持有界；不推断任意时长故障或 HA。

- [x] 时间窗口、非法配置及失败不确认的用例先失败，再实现显式实验。
- [x] 本地实际持续故障、同指纹恢复、原配置恢复与清理验收。
- [x] 影响集、文档与完成检查。

实际 `pnpm test:log-consumer:notification-outage:live` 退出 0：持续 60000 毫秒故障期间同指纹 firing 拒绝 37 次，首次成功于 60000 毫秒；四阶段交付、原通知字段恢复及清理通过。部署测试 16/16、工具 64/64、治理 57/57，详见[持续故障记录](../../verification/2026-10-01-log-consumer-notification-outage.md)。仅验收这段实测窗口，任意时长故障、持久通知恢复、HA、容量/P99 不升级。

**2026-10-01 通知 Pod 重建切片：**首个 firing 实际交付后删除并重建隔离 Alertmanager，证明 UID 改变、新 Pod 就绪、同指纹 firing 重新送达及后续恢复通知。临时 emptyDir 丢失，范围为 Prometheus 重发后的重新接收，不证明持久通知队列或 HA。

- [x] 建立未重建/未就绪/跨任务拒绝测试，先失败再实现独立实验入口。
- [x] 本地真实 Pod 重建、同指纹重新送达及恢复/清理验收。
- [x] 影响集、文档与完成检查。

实际 `pnpm test:log-consumer:notification-restart:live` 退出 0，新旧 UID 不同、新 Pod Ready，同指纹交付顺序为 firing/firing/resolved/firing/resolved；配置恢复和清理通过。部署测试 15/15、工具 64/64、治理 57/57，详见[通知 Pod 重建记录](../../verification/2026-10-01-log-consumer-notification-restart.md)。通知持久恢复、HA、长期故障及容量/P99 不在本次通过范围。

**2026-10-01 通知 503 重试切片：**默认关闭故障注入，新增显式本地实验；接收端拒绝前两次有效通知且不确认接收，记录有界白名单尝试。验证真实 Alertmanager 对同一 firing 指纹产生 503/503/200，再完成四阶段恢复通知，不改变正式规则阈值或生产配置。

- [x] 先建立失败用例（实际 200≠503），实现有界故障预算及尝试证明。
- [x] 真实本地重试、同指纹通知、原配置恢复和清理验收。
- [x] 影响集、文档和审查完成；通知进程重启、HA、长期故障与容量/P99 分别验收。

实际 `pnpm test:log-consumer:notification-retry:live` 退出 0：同一 firing 的首次三次有效请求为 503/503/200，随后四阶段同指纹交付；原通知字段恢复、任务资源清理通过。部署/接收端 14/14、受影响工具 64/64、治理 57/57，独立增量审查无阻塞问题。详见[通知重试记录](../../verification/2026-10-01-log-consumer-notification-retry.md)。仅关闭短暂 HTTP 503 重试验证缺口。

**2026-10-01 本地通知闭环切片：**复用正式重启规则和真实消费者故障，增加隔离 Alertmanager 与仅接收本次任务事件的 Webhook。只临时配置专用本地 Prometheus 的通知端点，退出按字段比较恢复；不配置外部邮件/聊天接收器。

- [x] 建立接收端的范围/载荷/状态验证与失败测试，增加显式通知实验入口。
- [x] 实际完成 Prometheus → Alertmanager → 接收端的 firing/resolved 交付，验证清理和原配置恢复。
- [x] 按影响集验证、审查并更新日志说明与证据，容量/P99 不因通知测试升级。

最终 `pnpm test:log-consumer:notifications:live` 退出 0，四阶段 firing/resolved/firing/resolved 实际交付且同指纹；清理完成、Prometheus 原通知字段恢复。部署/接收端测试 13/13、工具测试 64/64、治理 57/57。详见[本地通知记录](../../verification/2026-10-01-log-consumer-local-notifications.md)。关闭本地 Webhook 通知链路缺口；具体外部接收器、通知 HA/故障重试与容量/P99 不在此验收范围。

**2026-09-30 重启告警接线切片：**复用本地 Operator，为 kube-state-metrics 仅启用 Pod 采集、两个必需指标和一个应用标签；保留工作负载 namespace。使用真实消费者缺配置失败产生 Kubernetes 重启，验证正式规则触发、标签排除/恢复与删除测试 Pod 后解除，不重跑未变化的 Kafka/ES 链路。

- [x] 增加可选本地监控 Overlay 与独立失败关闭的随机命名空间实验脚本。
- [x] 本地真实 kube-state-metrics 与 Operator Prometheus 验证，检查影响集、文档和清理结果。

最终跨日运行于 2026-10-01 本地完成，`pnpm test:log-consumer:restart-alert:live` 退出 0；真实重启 3 次、工作负载 namespace 与应用标签保留、正式规则触发、标签排除/恢复、删除 Pod 后解除均通过，清理完成后发布工件。工具测试 64/64、部署检查 9/9、治理 57/57；详见[重启监控验证](../../verification/2026-10-01-log-consumer-restart-monitoring.md)。关闭本地 kube-state-metrics 接线缺口，通知送达、健康服务恢复和容量不在本次结论范围。

**2026-09-30 Operator 发现切片：**沿用户“继续”授权，补独立消费者的真实 PodMonitor 自动发现。部署固定版本的本地轻量 Operator/Prometheus 工具；不修改业务 release，不安装外部通知，不把监控发现当作容量验收。

- [x] 增加只供本地 kind 的双层选择器、有限资源监控配置，以及显式 `--operator` 实验入口；默认链路和消费者 experimental 保持不变。
- [x] 实际验证命名空间排除、PodMonitor 标签排除、双匹配后 Pod IP 直接抓取及消费者指标，并重跑 ES/DLQ/Offset 和吊销恢复。
- [x] 记录实际结果、监控工具保留范围与任务资源清理，完成部署检查和独立审查。

最终修订版 `pnpm test:log-consumer:operator:live` 退出 0；先证明实际 up，再分别排除、观察目标消失并恢复。3 条来源记录 → 2 条 ES 文档 + 1 条 DLQ，Offset=3、lag=0；工具套件 64/64、Helm 26/26、部署检查 9/9、治理 57/57。详见[Operator 本地发现验证](../../verification/2026-09-30-log-consumer-operator-discovery.md)。本地监控工具/CRD 保留复用，任务资源清理；kube-state-metrics 接线、通知与容量未在本切片验收。


- 当前用户明确要求按已讨论建议更新日志文档、制定开发计划，并尽量采集截图圈出的字段。
- [ADR-0012](../../architecture/adr/ADR-0012-configurable-log-delivery.md)、[总体架构 §16](../specs/2026-07-17-fullnet-architecture-design.md#16-可观测性与高并发日志)、[日志模块说明](../../operations/logging-module.md)定义目标。
- 本计划接管历史 Task 8B 的尚未完成部分与本次字段/可选链路；旧双通道、退出、单事件隔离计划和 Verification 保留历史状态，不改写历史通过证据。本主题后续只更新这一份活动计划。
- 执行基线需重新读取 git rev-parse HEAD / status；文档编写基线为 006ca78f1fff66a6bfe37627264ee527e3d9c673。已有 contracts/database/object-comments.json 用户改动不属于本计划。
- 本次方案审查修订核对基线为 44ad78833a80cdd4823d2863a999e18d7c94fff7；五项意见映射到模块说明 §2.3/§5/§6、LG00/LG02—LG06。文档修订不等于实验或实现已通过。
- 双通道/采集器性能分析补充见模块说明 §2.4/§2.5；调用线程快照成本、共享 stdout、节点汇总采集和逐事件 PQ 检查点分别纳入 LG02/LG06/LG00/LG08，未增加实施完成状态。
- 用户确认后台直发 Kafka 成为正式简化候选，分析验证后可采用或配置选择；更新基线为 32fc0330757dc74b614e8c1b54e57e8824c0537b。最新授权为修复方案并开始分阶段执行；路线准入与配置边界见模块说明 §3.1/§3.2。

## Global Constraints
**2026-09-30 独立消费者指标与告警切片：**统计回调不增加消费循环网络查询，固定 10 秒采样；按实际分区分配聚合已提交位点的可见积压，未知/过期不报 0。计数仅使用固定 ES/DLQ/Offset 类别；短命进程失败由 Kubernetes 重启告警兜底。

- [x] 先建立 Prometheus 规则失败证据及指标边界测试；补充统计 JSON 长度/深度、分区数量与采样年龄上限。Broker DOWN 刷新缓存零值的回归先失败，保护后通过。
- [x] 接入只读统计回调及固定投递结果计数，不改变确认、提交、异常退出和取消语义；配置可选 PodMonitor。
- [x] 实际 promtool 验证积压、未知、故障、目标不可达及重启的触发/恢复；重跑 Kafka/ES 与本地 Helm 部署指标采集，记录真实范围。

最终本地验收：日志单测 297/297、Linux Kafka/ES 14/14、Helm 26/26；真实 Prometheus 从 Pod 抓取已提交积压与 ES/DLQ/Offset 确认计数，正式 TargetDown 规则在测试采集桥中断后触发、恢复后解除，测试资源清理完成。详见[指标与告警评估验证](../../verification/2026-09-30-log-consumer-alerts.md)。Operator 自动发现、目标 kube-state-metrics 接线及 Alertmanager 通知送达未在此实验验证；独立消费者 experimental 开关和容量状态不变。

**2026-09-30 Kubernetes 消费链路切片：**复用独立 Helm 与镜像，在本地 kind 部署消费者，连接隔离的真实 Kafka/ES 测试容器；只创建任务专用命名空间、Secret 和短期证书，不改业务部署。

- [x] 增加可重复的本地链路脚本，覆盖正常事件 ES 读回、毒消息 DLQ 读回和消费组已提交 Offset。
- [x] 临时 CA 提供实际 CRL 地址，沿 Helm 的 Production/Online 配置验证，不注入 NoCheck 或跳过主机名验证；吊销时保留 Offset 2，换新序列号证书后重放至 Offset 3。
- [x] 运行本地真实实验，记录结果并清理测试资源；失败关闭，未运行项不计通过，独立消费者仍保持 experimental。

`pnpm test:log-consumer:kubernetes:live` 最终退出 0，3 条来源记录 → 2 条 ES 文档 + 1 条 DLQ，lag=0，CRL 请求 4 次，清理完成后才发布通过工件。范围为 kind 消费者连接外部隔离单节点 Kafka/ES，详情见[本地链路与 CRL 验证](../../verification/2026-09-30-log-consumer-kubernetes-delivery.md)。Lag/故障分类告警、OCSP、集群内 Kafka/ES HA 和容量未在本切片验收。

**2026-09-30 独立消费者运维交付切片：**沿用户“继续”授权，先补交付载体与可观测生命周期，不更改逐项确认后提交的规则。本切片使用已有 JIT 独立宿主，应用 API/Worker 与双库业务路径不变。

- [x] 真实独立进程建立健康端点失败用例：Broker 未分配分区不得 ready；已隔离并提交后指标不包含来源/载荷/秘密。
- [x] 使用独立轻量 HTTP 运维面及 Host 生命周期处理 SIGTERM；消费者仍在单个循环中串行确认与提交。健康就绪只表示实际取得 Kafka 分区，不冒充 ES 写入确认。
- [x] 增加独立镜像与默认关闭的 Helm 交付配置：显式实验启用、固定 Secret 键、只读 CA、非 root、资源限额及探针，禁止公共 Ingress。
- [x] 本地构建、Helm 渲染和真实 TLS Kafka 进程验证；Linux 实际 SIGTERM 验证退出 0，记录未确认 Offset 保留。生产 CA/CRL/OCSP 和完整端到端门禁仍单独验收。

本切片本地验收通过：Linux Kafka/ES 集成 14/14（0 跳过）、Helm 合约 25/25；kind 实际启动后 live=200、未分配分区 ready=503、计数为 0。完整命令、范围和故障修正见[独立消费者运维验证](../../verification/2026-09-30-log-consumer-operations.md)。独立消费者仍为 experimental，未将探针通过等同于可靠消费组合准入。

**2026-09-30 ApplicationKafka 生产入口准入：**用户要求继续推进此门禁，沿全项目本地验收标准执行。本切片开放 API/Worker 的生产入口选择，确认范围止于应用受限 Producer 的 Broker 投递报告；不自动开放 LogConsumer 试验开关，不改变 ES/DLQ 确认规则，不承诺入队即持久或未测容量。原“同环境完整容量比较后才能打开入口”改为“入口按本地功能/安全/故障测试验收，容量比较决定部署资源与规模”；此前生产关闭段落保留历史事实，以本次结果为准。

- [x] `HighPriorityLoggingTests` 为 Production 出口生命周期/无适配器行为建立失败证据，Helm 扩展双角色 Production Secret/路由正反例。
- [x] `ServiceDefaultsExtensions` 和 Helm helpers 解除 ApplicationKafka 环境硬拦截；保留 Local、旧 ES 冲突、预期模式、冻结路由及专用 Secret 校验。
- [x] 真实 Kafka 的 TLS/SASL/ACL 与 Broker 中断恢复回归，Production Host 双 Topic 读回，预算/关闭测试。
- [x] API/Worker 新 linux-x64 Native AOT 发布，在 Production 环境以真实测试密钥配置运行 Kafka 外部进程用例；API 验证普通/优先、Worker 验证资源日志和正常退出。2/2，0 失败/跳过，发布及运行均退出 0。
- [x] 同步 ADR/运维/README、记录范围和未验证项、完成独立审查与 `git diff --check`。生产入口配置门禁开放；详见[本次验证记录](../../verification/2026-09-30-application-kafka-production-admission.md)，不自动切流或开放独立消费者生产门禁。

**2026-09-30 验收变更及当前执行计划：**用户将本项目全部验收改为本地测试实际通过即可，权威为 rules/development-quality.md §11（R-20260930-local-acceptance）。本次先为 Host/Helm 的 Production Collector 接受行为及本地验收规则建立失败测试，再移除 Collector 的环境拦截，同步 ADR/Spec/运维说明并运行受影响本地门禁。Collector 应用配置开关与可选下游可靠组合分别验证；Local/ApplicationKafka 未在本次扩大生产准入。以下历史增量中的“生产 Collector 关闭/待 CI/专用环境”保留当时结论，当前配置开关以本修订为准；未执行的端到端、P99、容量和恢复任务继续开放，不因修改标准勾选完成。
**本次本地验证记录：**基线 `c609759576c4bf54ff9e743161ecb829ca8d4d4a` 加任务 `logging-collector-local-admission` 工作区增量，Windows/.NET 10、Docker Desktop；先以生产 Collector 与本地验收规则的新断言取得失败证据，再修改实现。`node --test tests/governance/integration-test-feedback.test.mjs tests/performance/load-profile-contract.test.mjs tests/deployment/helm-contract.test.mjs` 34/34；`pnpm test:dotnet:unit -- --filter FullyQualifiedName~HighPriorityLoggingTests --minimum-expected-tests 1` 50/50；`pnpm test:integration:affected -- --snapshot logging-collector-local-admission --phase slice` 工具契约 64/64，首次 Smoke 因 Docker 未运行失败，启动 Docker 后 `pnpm test:integration:smoke` SQL Server/MySQL 合计 8/8；`pnpm test:governance` 57/57；`pnpm test:skills` 两技能 79/48 项契约通过；`pnpm test:load-profiles` 6/6；`pnpm test:observability-deploy` 9/9；`pnpm test:dotnet:architecture -- --selection api-native-aot` 73/73；`pnpm test:aot:analyzers` 与 `pnpm test:aot:worker:analyzers` 均退出 0、0 警告/错误。通过项均无 skip/失败；独立只读审查未发现本次范围确切问题。未在本次重跑全项目矩阵、Linux 原生发布/运行或容量压测；不声明这些范围已通过，也未部署/切流。

- B0 同事务 fail-closed；B1 请求等待直接微批写入尝试且默认 fail-open；B2 可采样/可过载丢弃；Audit/日志不使用 Outbox。
- 核心不新增 Kafka/ES 写入强依赖；业务 Kafka 与日志 Kafka 配置、凭据、队列、Topic 和配额分离。
- 请求/返回默认 Summary；投影请求 2048 字节、返回默认 0（显式启用最多初始 2048）、深度 4、集合 16、字符串 128；ContextJson 总 8192 UTF-8 字节。字节预算包括包装结构，不在截断后产生无效 UTF-8/JSON。
- 原 IP/端口、服务端地址与请求/返回详情默认 Restricted，只存 B1 数据库；普通日志/文件/平台副本禁止。B2 只能输出另行审定的 Internal 摘要；秘密任何级别不保存，IP 默认指纹；本阶段不增加 Restricted 外部导出。
- 整事件 MaxEventBytes 初始 16384，普通/优先独立计费上限 67108864/8388608 字节，同时保留 10000/1000 条数上限；64 属性、深度 4、集合 16、普通字符串 2048 字符。这些是起始保护值，非 RSS/吞吐认证。
- 先取得采集许可再执行工厂和有界源生成序列化；无许可零工厂执行。B1 资格/独立预算不由 B2 采样决定，关闭 B2 不阻止既有 B1 摘要。
- 消费确认按准入组合冻结：PersistentQueue 的连续持久接收，或 SinkConfirmed 的逐项最终成功/可靠隔离；都只推进连续 nextOffset。Elastic 官方不保证 Logstash Kafka 输入的 Offset 只在事件安全进入 PQ 后提交，因此该具体组合暂不满足 PersistentQueue 准入；不能把 Logstash 改为内存队列代替 SinkConfirmed。ES 固定事件日期索引/ID/到期和 index 动作不变。
- 显式 DeliveryMode 与发布清单的 ingress 一致，直发必须走 Kafka；每个 Pod 的不可变路由标识决定 Collector 是否采集，元数据缺失失败关闭。应用启动只校验注入预期值，不宣称能核实运行中采集器；发布模板和真实路由测试负责平台一致性。同一事件只有一个集中入口，不双写/自动故障切路。SDK 缓冲另受消息/字节/在途预算，普通/优先 Topic 不代替 Producer 容量隔离。直发默认无应用 Spool，披露未获 Broker 确认的崩溃缺口。
- 新模式未指定时保留现有 Console/平台采集行为；旧 ES Enabled=true 时保持直写并告警。显式新模式与旧 ES Enabled=true 冲突必须拒绝；不能将 legacy-console 当成显式 Local 零远程。替代链与配置迁移验证前不删除旧 Sink。根级 `ReadFrom.Services` 隐式 Sink 入口已移除；未来所有 Serilog 集中出口须显式纳入单入口约束。
- 只能 Vue 新增页面；开放契约、SQL、源生成、权限变更按相关规则验证。Schema 增量迁移成对、历史行保持可读，不复制现有未知内容。
- 测试命令/执行位置唯一权威为 rules/development-quality.md §11；每任务先失败证据再实现，通过不得仅凭注册或文本断言；容量保持 Capacity-not-verified。
- Secret 仅引用；平台依赖固定版本/摘要、许可/漏洞检查。不得将 Admin.NET.Pro 截图、Token 或代码作为发布资源。

## 文件职责与接口

已有 Hosting/Observability 承担生成、分类和汇总；Auditing 承担 B1 DTO/持久化；ObservabilityAdmin 承担管道状态；Settings 保持限时诊断；deploy/observability 承担平台资源。实现类型保持 internal；下列跨 Hosting/Auditing 的实际消费契约及最小读取访问器公开，不用 InternalsVisibleTo 绕过边界，不新增无消费者的 Abstractions 项目。

当前 LG02 `HttpOperationContext` 为 Hosting 内部的 Internal 摘要，由 `HttpOperationMetadataBuilder` 从受信来源构造；LG03 对外只暴露 `HttpLogCaptureTarget`、`HttpLogCaptureState` 和 `HttpLogCaptureResult.State`，投影 JSON、所属请求及目标保持内部可见。LG04 接入前须建立跨 Hosting/Auditing 的最小只读访问器及版本化 `ContextJson` 契约，再以实际消费者验证字段和 AOT 闭包；不把现有内部对象误称为已交付的公共契约，也不以 Dictionary 任意接受 Header/对象。

完整上下文字段以模块说明 §4 为准。B2/B1 只共享已审定的 Internal 输入和 RequestId/TraceId，Restricted 结果留在 B1；各自记录创建一次独立 UUID v7 LogEventId，重试不变。记录事件时间、绝对到期与索引路由版本同样冻结，LogEventId 不代替业务 EventId。Restricted 上下文由 B1 许可单独生成，不进入通用 Scope；B2 发射不得接受调用方手工构造的 Result 或分类，读取目标必须匹配。

### 五项审查意见落实表

| 意见 | 修订决策 | 实施与必须取得的证据 |
| --- | --- | --- |
| R01 权限/保留不覆盖外部副本 | Restricted 只存 B1；独立详情清理及健康门禁 | LG03/LG04：所有出口无 Restricted；普通清理关闭仍清理；过期/无权限不可读 |
| R02 先序列化后检查预算 | 两阶段许可 + 受限工厂/有界源生成 | LG03：许可拒绝时工厂零执行；未开始工厂则回收预留，已开始的失败尝试仍计费以限制 CPU |
| R03 条数不能限制日志总内存 | 整事件/双通道字节与对象规模限制 | LG02/LG08：超大普通 ILogger/异常/集合和并发退出，计费/在途不超限 |
| R04 rollover 不能仅凭 ID 去重 | 固定事件日期索引 + 冻结路由/ID/到期 | LG00/LG06：跨午夜/月、重试/重放同索引/ID，过期不重建 |
| R05 消费者与确认机制未选定 | 原 Logstash/PQ 基线阻塞；重新选择能证明顺序的持久接收或 SinkConfirmed 实现 | LG00/LG06：固定实现/版本及连续 Offset/Bulk/隔离故障证据；失败组合关闭 |

表中为已修订设计和待取得证据，不是运行验证结果。

### 执行依赖与交付顺序

LG00 定义两入口及消费确认候选、依赖/AOT 可行性和可比实验预算，可与应用正确性切片推进；应用依次 LG01 → LG02 → LG03 → LG04。LG05 实现模式及准入的可选直发适配，移除旧 Sink 等待 LG06 替代链验证。LG06 依赖 LG00/LG02/LG05 并验证单入口；LG07 依赖 LG04/LG05，LG08 对比和封板后才批准目标部署路线。采集映射仍先于 Collector wire 切换。执行状态由下方检查项和实际证据表示。

## LG00：入口路线、消费者与确认边界准入实验（历史设计）

**文件：**读取当前平台配置/供应链约束；实验代码拟放 `eng/observability/verify-log-delivery.mjs`、`tests/deployment/log-pipeline-routing.test.mjs`；固定选型及证据回写本计划/模块说明，不另建第二份活动计划。仅真实实验完成后保存对应日期的 Verification。

**选择与归属：**原首选 Logstash Kafka 输入 + 每流水线有界持久 PQ 的可靠确认前提已被[官方 Kafka/PQ 限制](https://www.elastic.co/docs/reference/logstash/tips-best-practices)否定，LG00 暂停将其作为可靠档基线的后续准入测试。平台团队重新评估消费者与确认边界；不新增应用 .NET 日志消费者、不复用业务 Worker。候选必须提供实际镜像/依赖摘要、版本、配置、许可、适用存储及提交顺序证据。

**两入口：**Collector 与 ApplicationKafka 为正式候选。冻结直发适配归属/程序集、Host.Api/Worker 静态消费者及发布组合，依赖方向为宿主 → 可选适配 → Hosting 最小稳定口，Hosting 不反向引用 Confluent 实现；第三方类型不进入公共契约。只有真实复用与依赖隔离证据支持时拆项目，不为目录完整增加抽象。Collector 默认产物与直发产物分别核对闭包，不能凭 Enabled=false 推断 native 包已排除。

- [ ] 定义同环境入口比较：固定安全/采样/事件大小、Broker 分区/acks/压缩和相同消费者，记录可接受请求 P99 增量、每实例字节吞吐、CPU/托管及 native 内存、丢弃/缺口与恢复目标。先比较入口，再单独比较消费确认，实验不代替 LG08 生产容量证据。
- [ ] 原型验证有界后台直发的连续 Produce/回调或有界 ProduceAsync、双优先预算、QueueFull/超时/停机；不在请求等待 ACK，不逐条串行 await、不无界任务。验证 Broker ACK 前后 SIGKILL 的缺口、来源 ID、重试及消费者可查询，不以 SDK 接收视为持久确认。
- [ ] 验证可选适配的版本/许可/体积、DI/源生成/裁剪与 Host.Api Linux Native AOT 原生发送、不可用/认证失败及退出；既有 kafka-replay 证据不能外推日志 Producer。能力未通过的构建禁止配置 ApplicationKafka。
- [ ] 冻结发布清单的每 Pod 路由标签/注解和应用预期模式注入；针对旧 Collector Pod 与新 ApplicationKafka Pod 同时存在、缺失 Kubernetes 元数据、Console/File 镜像和当前双 Tail 通配规则建立真实路由负例。没有失败关闭的采集隔离证据不准入 ApplicationKafka。
- [ ] 重新选择消费者确认实现：先证明它可控制每分区连续 Offset 在安全持久接收或逐项最终写入/可靠隔离之后提交，再比较 PersistentQueue 与 SinkConfirmed 的成本。验证重平衡、乱序部分成功、写成功未提交及坏记录；未找到实现则该 Kafka 消费可靠档保持关闭，不以 Logstash/PQ 或内存队列自动替代。

- [ ] 固定新选定消费者、ES、采集器及插件版本；检查许可与漏洞，给出每流水线 CPU/内存/批次/连接/队列/DLQ 上限和适用持久存储。Logstash 9.5.4 仅为已完成的局部恢复实验版本，不作为可靠档默认实现。
- [ ] 对新选定实现验证安全持久接收或逐项最终完成与 Offset 提交的真实先后：关键窗口分别 SIGKILL，再查 Broker Offset 与恢复记录。先写后续快记录、前序慢/失败记录，证明不能越过连续缺口。旧 Logstash/PQ 配置的单事件恢复实验只保留为局部证据，不能勾选本项。
- [ ] 验证 ES HTTP 200 的部分 400/404/429/5xx、超大记录、未知 schema、DLQ 失败/满盘；逐条跟踪最终成功或可靠隔离，不接受插件默认丢弃。隔离无法证明则停止该可靠档准入；不靠启动成功勾选。
- [ ] 验证固定日期索引：显式 data_stream=false、ilm_enabled=false、document_id=LogEventId、action=index；索引由冻结事件 UTC 日期/路由版本决定。跨午夜/跨月重放只有一个目标记录，过期/已清理索引不可被重建。
- [ ] 验证正常重启、Pod 重建、永久卷丢失、消费者重平衡和非空 PQ 扩缩容；明确 PQ 不复制数据，RPO、Broker 保留内回放、单一重放所有权与恢复成本。
- [ ] 若新实现采用逐事件 PQ 检查点，报告刷盘吞吐/磁盘成本；失败组合保持关闭，保留其他已验证路线。SinkConfirmed 必须独立准入；固定实现/确认配置并更新同一 ADR/计划，不静默回退或自动更换组件。
- [ ] 测量每事件检查点配置下的 PQ 写入/检查点耗时、存储延迟、稳定消费与故障后追赶；准入要求不仅确认正确，还能在目标持续输入下排空积压。吞吐不足先查持久存储/流水线瓶颈，不以扩大 checkpoint 间隔绕过确认门禁。

**完成标准：**分别记录入口/构建/确认组合的可行性、持久起点、Bulk/DLQ/Offset 与去重证据；失败组合保持关闭，成功原型仍须 LG05/LG06/LG08 纵向交付才能启用目标部署。不能因一条路线通过就放行另一条或删除旧 Sink。

## LG01：结果与字段正确性基线（历史设计）

**执行状态（2026-09-28）：**已开始。权限旧单值列只在 Endpoint 有唯一 Full.NET 权限要求时填写；无要求/复合要求留空，标准与 OpenAccess 策略的完整 `RequiredPermissions` 已进入 B1 内存写入模型，数据库详情与查询仍待 LG04。已用 RED/GREEN 单元测试验证第一条 Claim 错报、异常映射后 400/500/503 状态刷新、B2 映射异常结果及原始路由、已停止协调器 fail-open、取消请求和响应已开始时的 B1 结果；原有公开中间件阶段数值已冻结。真实 Host.Api/双库集成尚未通过；下方 LG01 检查项暂不标完成。

**本地证据：**LG01 首轮 `logging-delivery` 选择集 20/20；LG02 字段与分类切片扩大为 31/31，完整 Unit 3475 通过/1 项 Linux 专用跳过、Architecture 232/232、`pnpm test:integration:tooling` 52/52、`pnpm test:governance` 55/55、`pnpm test:aot:analyzers` 退出码 0。分类切片另运行 `HighPriorityLoggingTests` 12/12。代码审查指出禁用级别仍构造字段、超长 Header 先全量解析两个问题，已用 RED/GREEN 修复并重跑上述本地门禁。`pnpm test:integration:affected:plan -- --snapshot configurable-log-delivery --phase inner` 选择 `integration-matrix, smoke`；Docker daemon 此后已可用，但双库 Integration/Smoke 仍未执行，按 GitHub Actions-first 门禁保持待验证。

**文件：**修改 `src/Modules/Full.NET.Modules.Auditing/Middleware/OperationLogMiddleware.cs`、`Middleware/AuditWriteCoordinatorMiddleware.cs`、`src/BuildingBlocks/Full.NET.Hosting/Observability/HttpOperationLogMiddleware.cs`、`tests/Full.NET.UnitTests/Hosting/HttpOperationLogTests.cs`；根据异常完成边界扩展 Hosting/Api 的既有结果映射及 Auditing WriteModel/Buffer；新建 `tests/Full.NET.UnitTests/Auditing/OperationLogMiddlewareTests.cs`；修改 `eng/testing/test-matrix.json` 登记 `logging-delivery` Unit 选择集。

**输出：**HTTP 异常不误报成功；RequiredPermissions 来自 Endpoint 授权 metadata，旧 PermissionCode 不再冒充命中权限。不使用 HTTP 状态证明业务事务成功。

- [ ] 建立异常 Endpoint 仍为默认 200 的失败用例：Operation Outcome 必须为 failed/exception，最终可确认状态为 500，不能 Succeeded=true。
- [ ] 建立有多 Permission Claim 的失败用例：要求权限来自 Endpoint；复合策略保留要求集合，不取 claims.First。
- [ ] 在业务执行、响应最终状态与异常边界间建立明确记录时点；已开始响应的异常标记 Outcome，不伪造“客户端收到 500”。保留原异常、取消和 B1 等待语义。
- [ ] 覆盖已映射的业务异常、未处理 500、取消及响应已开始；最终 HTTP 状态只能由实际映射结果/完成边界提供，不能将所有异常硬编码为 500。B1 捕获与刷新次序必须通过真实中间件组合测试，不能在已刷新后补状态。
- [ ] 运行下方 `logging-delivery` Unit 选择集，保存 RED/GREEN；共享管道变化由 affected selector 确认双库 slice 影响集。
- [ ] 审查差异后单独提交本任务，不能夹带上下文扩展或平台变更。

## LG02：上下文、分类与高频生成（历史设计）

**执行状态（2026-09-28）：**已开始请求摘要与普通事件分类切片。`HttpOperationMetadataBuilder` 从静态 Endpoint、`Activity`、可信 Connection、认证 Principal 和已解析 Culture 生成 Internal 摘要；Minimal API 的 MVC 字段留空，未匹配路由用固定占位符，B2 `url` 不再带实际路径段/Query。`TraceId` 与 `RequestId` 分列、`SourceOriginFingerprint` 仅保留已校验 HTTP(S) 站点的 SHA-256 指纹，`ClientIdFingerprint` 仅保留认证 Claim 指纹，UA 输出固定浏览器族、语言仅输出资源目录支持的标签，可选线程 ID 仅表示捕获点。日志级别禁用时跳过元数据构造，超长来源拒绝，UA/语言先限输入再分类。普通事件在进入双通道前得到默认 `diagnostic` 分类及管道生成的 UUID v7 `LogEventId`，调用方同名 ID 不可信。后续切片已加入进程启动一次的 Resource 记录，逐事件仅关联受控 Instance；Serilog 解构深度/集合/字符串限制和普通顶层字符串入队前限长已通过聚焦 RED/GREEN。原子 `LogQueueByteBudget` 已接入两个 Sink：先预留最大封套费用，再构造并退还差额；默认队列只持有有界 UTF-8 快照，Console 直接输出；旧 ES 启用时附带独立计费的安全事件拷贝，保留日期格式、字典和枚举等语义；异常 `@x` 仅保留类型。移除根级 `ReadFrom.Services` 隐式 Sink 入口，防止绕过管道。单事件 16384 字节、普通/优先 64/8 MiB 以及条数容量已接入，阻塞 Sink 在途预算不提前归还，超大/字节拒绝有独立低基数指标，高优先级字节占用也参与 ready 降级。`logging-delivery` 选择矩阵已登记并经本地验证。已新增公开宿主入口的日志热路径基准并记录[探索性结果](../../verification/2026-09-28-logging-hot-path.md)；采样波动大，旧实现同环境对照、请求 P99、安全事件对象的真实 RSS、更广泛的普通字段秘密、受控投影及真实采集 wire 仍待验证，不能据此宣称 LG02 或容量已交付。

**文件：**新增 `src/BuildingBlocks/Full.NET.Hosting/Observability/HttpOperationMetadataBuilder.cs`、`HttpOperationContext.cs`、`LogEventMetadataEnricher.cs`（均在同目录）；修改 `HttpOperationLogMiddleware.cs`、`HostingLog.cs`、`FullNetLoggingPipeline.cs`、`ServiceDefaultsExtensions.cs`；新增 `tests/Full.NET.UnitTests/Hosting/HttpOperationMetadataTests.cs`，扩展 `HighPriorityLoggingTests.cs`。

**统一预算文件：**新增同目录 `LogEnvelope.cs`、`LogEnvelopeBuilder.cs`、`LogEnvelopeConsoleWriter.cs`、`LogSnapshotException.cs`、`LogQueueByteBudget.cs`；修改 `FullNetBoundedAsyncSink.cs`、`FullNetLoggingPipelineSink.cs`、`LoggingOptions.cs` 及现有监控/校验；新增 `tests/Full.NET.UnitTests/Hosting/LogEnvelopeBudgetTests.cs`、`LogQueueByteBudgetTests.cs`。

**消费/输出：**消费可信 Endpoint/Connection/Activity/Identity 上下文；输出稳定 Context 与统一 LogEventId/分类，供 LG03/LG04 使用。

- [ ] 用 DefaultHttpContext、RouteEndpoint 和 Activity 构造截图每字段的存在/缺失用例；验证 Minimal API Controller/Area 不适用、traceparent 不写入 TraceId、缺 Activity 时 RequestId 不伪装为 TraceId。
- [ ] 覆盖伪造 Forwarded Header、Host/Referer 控制字符、UA 超长、BCP 47、ClientId 不可信 Header、异步线程切换；可信代理集成复用已有夹具。
- [ ] 静态 metadata 获取控制器/操作/显示键/路由；可选 CaptureThreadId 只在捕获点记录；系统资源启动一次、逐条只关联 Instance。
- [ ] 普通 ILogger 事件统一默认分类；错误分类不能依赖调用方手工填字段。源生成固定模板，昂贵投影在 IsEnabled/采样/预算之后计算。
- [ ] RED：普通 ILogger 的巨大字符串、集合、属性数量和异常对象均受整事件约束；合法多字节/投影 JSON 不被剪坏；验证 queue 条数/字节任一耗尽拒绝，普通与优先不能互借预算。
- [ ] 设置事件构造阶段的全局 Serilog 解构边界；通过有限遍历/有界 writer 生成 owned UTF-8 LogEnvelope，队列不得持有原始业务对象或原始 Exception。旧 Sink 兼容封套只可持有有限安全事件拷贝及仅含异常类型的合成摘要，并另行计费。未知标量不调用任意业务 ToString 兜底。Console/File 从快照输出，旧 Sink 在后台使用受限适配。
- [ ] 保留当前调用线程构造、后台 Compact JSON 格式化的同环境基线；分别测目标入队前快照构造/序列化和入队耗时、分配、请求 P99。Console/File 不重复生成相同 JSON；没有容量证据不替换队列容器或靠增加线程/队列容量宣称高性能。
- [ ] 用实际缓冲 Capacity 加封套开销计费，同时限制构造中/在途量；不能先完整 RenderMessage/序列化再裁剪。超限先移除诊断字段，仍不能满足则拒绝并记录安全原因，不能递归写回原事件。
- [ ] 并发验证预算预留/回收、失败、丢弃、消费完成和退出竞态；阻塞 Sink 未返回不能回收其缓冲。新增独立 queue.bytes/bytes.capacity/oversize 指标；保留计费预算不等于进程 RSS 的边界。
- [ ] 明确逻辑字段到当前带点键的输出映射；采集端未完成 LG06 双版本读取前不切换 wire 字段。格式切换随 SchemaVersion 滚动发布，不靠重复别名维持不明语义。
- [ ] 扩展本地 Unit/Architecture 与 AOT 分析；不得通过运行时程序集扫描补齐 metadata。
- [ ] 提交字段模型与生成，逐行核对模块清单没有遗漏。

## LG03：安全请求/返回投影与独立预算（历史设计）

**本轮增量证据：**通用 Serilog 入队封套补充签名键、带点/索引式赋值、嵌套 JSON 凭据与属性洪峰回归；敏感模板删除所有被引用的占位符值，仅保留未被引用且经过严格校验的路由/关联字段，过长模板不保留属性。`SignInCount`/`SignOutTime` 不误判为签名材料。Console UTF-8 和旧 ES 兼容事件同测；`logging-delivery` 本地选择集 134/134、API Native AOT 架构集 73/73、AOT 分析已通过。仍须按 LG03/LG08 验证其他出口及真实负载性能，不能据此勾选整个 LG03。

**编码键防护增量（2026-09-29）：**通用字符串中的 JSON Unicode 转义可拆开已知凭据键，使明文扫描漏检。RED 用例确认 `pass\u0077ord` 值进入 Console 快照；封套现保守移除含合法 `\u` 四位十六进制转义与赋值分隔符的整块文本，Console 与旧 ES 兼容事件共用此结果。独立审查发现初版不区分大小写的 `\u` 检测误删 Windows 路径，RED 用例确认后收紧为合法四位十六进制转义。`logging-delivery` 本地选择集 232/232、API Native AOT 架构集 73/73 与 AOT 分析（0 警告、0 错误）通过。该规则可能移除无秘密的编码赋值文本，仍需比较请求 P99 与分配，且不承诺识别任意编码或无标记秘密。

**执行状态（2026-09-28）：**旧字符串入口已停用并清除旧 Items 值；因无法先许可后构造原文，不再发射到 B2。已新增并注册 B2 `http.pagination.v1` 数值 DTO 的两阶段入口：路由/模式/请求内固定采样键与决定/每秒事件与字节许可在工厂前；未执行工厂的取消与未用租约归还预算，工厂开始后的失败或超限仍计费，避免 CPU 限流被反复失败绕过。目的地只接受 B2，B1 明确 `not_applicable`。中间件只读取绑定当前请求的许可结果。操作日志列表 Endpoint 在查询成功后仅以规范化分页数值调用新入口；新增 `http.pagination.result.v1` 只投影 `page/pageSize/totalCount/itemCount` 四个数值，写入独立的 B2 ResponsePayload，默认响应上限为 0 因而关闭，不读取列表项或响应流；请求与返回共用每秒事件/字节预算，但有各自的单条上限和请求内结果槽。默认路由白名单为空，启用时需显式配置，投影及源生成元数据失败不影响查询响应。异常处理器只记录异常类型和安全路由模板/固定占位符，不把原始 Exception 交给 ILogger；HTTP Operation 发射失败的 Debug 路径也只记录异常类型。请求方填写的 HTTP Method、UA、Accept-Language、来源站点及认证 ClientId 均改为固定分类、受控标签或指纹，不把原文放入 B2。日志配置热更新失败或诊断 provider 再次抛错时跳过 B2，不改变业务响应。RED/GREEN、热更新采样、失败降级、跨请求隔离及并发预算测试覆盖这一切片；本轮 logging-delivery 选择集已在本地通过。审查发现的长路由截断白名单碰撞及未发射路径提前占用预算均已用失败用例复现并修复：许可与最终发射均使用完整静态路由检查白名单，先核对路径包含/排除规则与 Info 级别，再预留预算；只有日志显示和采样标签使用有界路由。进程内诊断快照已接入 B2 同步采样，过期规则不再生效，请求内缓存决定在规则到期时重算；默认快照复用单例，热路径不因默认存储反复分配；实例级容量覆盖只接受全局 HTTP 类别/组规则，Trace 定向采样拒绝远端父链（含无 listener 的父 ID 回退）；权威回源双重失败或配置恢复默认时已撤销旧的扩大采样快照；API 节点启动及每 30 秒后台权威回源已接入，管理端快照读取不再回源，轮询不广播缓存失效，跨实例更新与恢复默认在下一次成功读取后收敛（另计数据库读取与串行等待）；通用 Host 配置项入口已阻断保留策略键的读写并在列表 SQL 中排除；普通日志封套已在入队前对已知敏感键、嵌套字典及自由文本凭据格式做整值/整块移除，Console 和旧 ES 兼容事件的实际 JSON 字节负例覆盖已知格式；无标记任意秘密、其他出口、B1 资格/详情仍未覆盖，LG03 不勾选完成。

**文件：**修改 Hosting/Observability `HttpOperationLogOptions.cs`、`HttpOperationLogSanitizer.cs`、`HttpOperationLogMiddleware.cs`、`HttpOperationLogEmitter.cs`；新增同目录 `HttpLogCaptureResult.cs`、`HttpOperationPayloadProjection.cs`、`HttpLogCaptureLease.cs`、`HttpLogCaptureTarget.cs`、`HttpLogCaptureBudget.cs`；新增 `tests/Full.NET.UnitTests/Hosting/HttpOperationPayloadProjectionTests.cs`、`HttpLogCaptureBudgetTests.cs`；修改 `src/BuildingBlocks/Full.NET.Hosting/Api/FullNetExceptionHandler.cs` 的安全异常输出边界。

**接口：**两阶段 `TryBeginCapture(HttpContext, HttpLogCaptureTarget, projectionKey, out lease)` → 许可作用域内 `lease.Capture<TProjection>(Func<TProjection> boundedProjectionFactory, JsonTypeInfo<TProjection>)`。目的地仅 B2InternalSummary/B1RestrictedDetail，投影键与分类由静态注册表决定，不能由调用方任意提升。先许可后构造/序列化，不接受 string safeJson 或任意原始业务对象反射。取消或未提交且工厂未开始时回收预留；工厂开始后的异常/超限保留计费并给出安全 CaptureState。

- [ ] RED：关闭/非白名单零捕获，JSON 输出有效；多字节截断、深度/数组上限和普通字段内秘密都不泄漏。覆盖 Password/Token/Cookie/签名/nonce/证件/银行卡负例及异常消息秘密。
- [ ] RED：流式响应、文件、Multipart、WebSocket 不改动业务流；巨大 payload 在序列化前预算拒绝；没有捕获时返回明确 State。
- [ ] RED：关闭、未采样、过期诊断、非白名单、目的地不合法、预算耗尽/清理状态不健康时工厂调用次数为 0；许可后未开始工厂的取消/弃用回收预算，工厂开始后的异常/超限计费且不泄漏秘密。B1 资格独立验证，不能以 B2 未命中采样拒绝 B1 摘要。
- [ ] 在显式 DTO/结果映射边界实现投影；不用 Body 缓冲或响应 MemoryStream 替换，不完整解析无上限 JSON 后才做字节裁剪。
- [ ] 投影工厂提前限制字段/字符串/集合，再以源生成 JsonTypeInfo + 有界 UTF-8 writer 计入整个投影包装；禁止调用方在 TryBeginCapture 前创建投影 DTO/JSON。记录超限状态，退出时归还未用预留。
- [ ] 以命名配置约束每秒事件/字节和每记录上限；校验正数、最大值与冲突。B2 过载只收缩 B2，B1 详情按自身预算控制，摘要可靠性不变。
- [ ] 将限时诊断采样与 scoped 匹配接入实际路径；热路径剔除过期规则，跨实例刷新不依赖只读 Current 快照；回源失败不能保留过期扩大采集策略。
- [ ] 使用独立保留与授权的原 IP/内部地址；B2 的 SourceOriginFingerprint 只由去凭据/路径/Query/Fragment 的站点计算；安全异常投影取代原始 @x 泄漏。运行 Unit、AOT 与相关 Settings slice。
- [ ] 出口负例检查 Console/File/旧 ES Sink/SelfLog/OTLP 及 Kafka Producer 提交字节、Broker 记录、失败诊断和 DLQ 的实际字节：Restricted 请求/返回、原 IP/端口、服务端地址与秘密不得出现。LG03 用可捕获传输封套测试；LG05 检查 Producer 实际序列化缓冲；LG06 在真实 Broker/消费者逐跳复核。公共 Context/Scope 不携带 B1 sidecar，伪造 Result 不能绕过目的地验证。B2 仅写批准的 Internal 摘要及状态；没有 B1 资格时 not_applicable，不偷偷写详情。
- [ ] 提交；以安全投影结果作为后续共享输入，避免重复序列化。

## LG04：B1 详情双库扩展、查询与授权（历史设计）

**先行预算修正（2026-09-28）：**现有 B1 信封以 UTF-8/UTF-16 字节较大值计入文本及固定开销；单条超过 `MaxBatchBytes` 即 fail-open 拒绝，当前批次无法容纳下一条时延后到下一批，停机时延后项也必须完成或标记失败。已用 RED/GREEN 覆盖多字节低估、ASCII 内存费用、两条合批越限、单条超限和停机取消。后续增量新增 `QueueMaxBytes` 对等待、排队和整批写库中的信封预留总字节，条数和字节任一超限均 fail-open；入队超时、通道关闭、热更新后的单条超限及停机排空释放额度。独立审查发现 writer 解析、停机排空、运行中热配置失效可能使请求永久等待，均以故障注入测试复现并修正为 fail-open：已接受的排队/延后信封在配置持续失效时终结，新请求读取无效配置也立即拒绝，消费循环限速。最终本地 `logging-delivery` 149/149、API Native AOT 架构 73/73、AOT 分析 0 警告/错误、治理 55/55、工具链 52/52；热配置故障测试额外重复三轮。完整 Architecture 232/232 运行于本切片最终异常修正前。真实 Host.Api、双库与容量认证仍待执行。该预算修正结束时详情字段、双库详情与独立清理仍未实施，LG04 不勾选完成。

**详情存储基座增量（2026-09-28）：**已新增双 Provider 239 可空 `ContextJson`/`DetailsExpiresAtUtc` 迁移和中文对象说明；MySQL 使用固定字面量的逐列 `PREPARE` 适配非事务 DDL，并按命名规则登记精确债务。新增双库恢复测试模拟旧行与第一列已提交但 DbUp 未记账，检查空值及重复重放。当前仅准备存储结构：应用写入仍为摘要，列表及既有详情响应不读取新列；捕获、绝对到期写入、独立清理、详情权限和真实双库测试仍未完成，LG04 不勾选完成。

**独立清理增量（2026-09-28）：**双库 239 增加到期索引及恢复断言；Worker 新增独立 `Auditing:DetailsRetention` 预算和有界清理循环，SQL Server 单语句批量清空、MySQL 短事务领取后按 Id 清空，均保留操作摘要。该清理不读取普通 `Auditing:Retention.Enabled`；末批满额时立即继续下一轮，短批或失败才进入配置轮询，避免积压受固定间隔限速。Unit 已覆盖双 Provider SQL 形状、批次与配置边界；双库 API 集成夹具已增加过期/未过期详情的分批清理与摘要保留断言，但尚未在真实数据库运行。架构边界精确登记此 Worker Host 作用域，相关测试通过；共享清理检查点、健康资格和详情采集仍未实施，真实双库清理/索引效率仍待 Actions 和执行计划验证，不将 LG04 标为完成。

**共享检查点增量（2026-09-28）：**尚未发布的双库 239 同步增加 `fn_auditing_details_cleanup_state` 单行表和中文注释；Worker 仅在清理与最早过期积压读取均成功后写入成功时间及积压时间。SQL Server 事务内更新/首次插入，MySQL 单语句 Upsert；多实例较旧的观察时间不得回退较新的状态。迁移重复重放、非空积压到追平后的状态转换、两实例并发首次写入及旧观察不回退断言已编写，但真实数据库尚未运行。API 后台读取、TTL/滞后健康资格及详情捕获尚未接入，因此检查点本身不授予采集许可。

**API 资格缓存增量（2026-09-28）：**Auditing 已提供双库检查点读取与 API Native AOT 显式行物化；API 后台独立 Host 作用域刷新，默认禁用不查库，刷新失败立即清空本地资格。请求路径只读内存，按本地缓存有效期、Worker 最近成功时间、最早积压滞后及未来时间失败关闭；配置默认 30/90/180/300 秒，均可验证边界。Unit 覆盖缺失、过期、未来、积压、禁用、热配置失败与刷新读取失败。审查发现 MySQL Worker 的 `DateTime?` 最早过期时间查询在 Native AOT 下缺少标量识别，已补齐 `DateTime` 类型并用先失败的架构测试防回归。此时 B1 目的地许可尚未消费该资格；后续增量接入见下文。

**B1 固定网络上下文增量（2026-09-28）：**B1 Operation 的静态路由详情入口现在消费 API 本地清理健康资格；默认开关关闭且白名单为空。启用后仍须完整静态路由命中、保留小时数不超过操作摘要保留期，才序列化固定版本的客户端/服务端 IP 与端口；不读取任意 Header、Query、Body 或响应流。详情与首次捕获确定的绝对到期时间同批写入双库可空列，并计入 B1 字节/SQL 参数预算；超大详情降级为摘要。SQL 写入故障诊断改为固定信息，不带可能包含受限参数的异常文本。Unit 覆盖拒绝与通过路径、Middleware 注入、列参数、计费及超限降级；真实双库写入与请求/响应受控投影仍待验证/实施，LG04 不勾选完成。

**独立详情读取增量（2026-09-28）：**新增 `GET /api/v1/auditing/operation-logs/{operationLogId}/details`，同时要求 `auditing.operations.read` 与 `auditing.operations.details.read`。查询只在 Host 作用域选择未到期的可空详情，响应前再次核对到期和版本化固定字段；缺失、过期、非法详情统一返回 404。响应只含白名单网络上下文，不透出原始 ContextJson；旧列表、普通详情和导出继续只返回摘要。已增加 SQL Server/MySQL 查询选择的 Unit、双 Provider HTTP/数据库集成夹具和 OpenAPI 冻结夹具。现有客户端 manifest 未登记这个尚无 UI 消费者的新 Operation，离线快照不需改变；待 UI 绑定时由运行时生成器更新客户端。真实双库 HTTP 与运行时 OpenAPI 仍待 CI 验证；请求/响应受控投影尚未实施，LG04 不勾选完成。

**导出端点受控投影增量（2026-09-28）：**已为成功的操作日志导出请求接入 B1 专属固定字段投影。详情资格和精确静态路由先通过，随后才运行请求/结果工厂；请求摘要只含规范化时间、状态码和成功筛选，结果摘要只含行数、截断及敏感列标记，不包含 PathContains、文件名、文件字节、Body 或 Header。请求和结果分别按 UTF-8 上限计费，结果默认关闭；失败、未许可或超限只留下安全 CaptureState 或不采集，不替换导出响应。该切片不表示其他 Endpoint 的请求/返回已覆盖，真实双库及 API Native AOT 仍需验证，LG04 不勾选完成。

**双库聚焦验收增量（2026-09-29）：**`AuditingOperationLogAssertions` 已在同一真实 Host.Api 夹具中补齐受限详情的匿名拒绝、仅普通读取/仅详情读取拒绝、双权限成功、非法版本/到期/缺失返回 404，以及普通详情不携带受限上下文。当前源码 Release 构建零警告/错误；SQL Server 与 MySQL 的 `Host_operation_log_query_follows_contract` 各通过 1/1，239 迁移双库恢复通过 2/2，已有保留集成夹具中的详情到期清理与共享检查点双库用例通过 2/2。此证据只覆盖聚焦用例；Worker 实际宿主生命周期、真实浏览器、API Native AOT 原生进程及全链路恢复仍未验收，LG04 保持开放。

**文件：**修改 Auditing `Features/WriteOperationLogs/OperationLogWriter.cs`、`Features/WriteAuditBatch/AuditWriteEnvelope.cs`、`AuditWriteBatchSql.cs`、`AuditWriteBatchWriter.cs`、`Contracts/OperationLogContracts.cs`、`AuditingAuthorizationContributor.cs`、`Serialization/AuditingJsonSerializerContext.cs`、`Persistence/OperationLogSql.cs`、`AuditingDapperAotMaterializerContributor.cs`、`Features/QueryHostOperationLogs/Endpoint.cs`、`HostOperationLogQueryService.cs`；扩展 Auditing Retention。新增双 Provider 下一可用编号 `*_AuditingOperationLogContext.sql`；编号必须按执行时最高迁移分配，不硬编码当前号。

**测试：**扩展 `tests/Full.NET.UnitTests/Auditing/AuditingWritePathTests.cs`、`AuditingRetentionTests.cs`、`tests/Full.NET.IntegrationTests/Auditing/AuditingOperationLogAssertions.cs`；受影响 API 双库复用既有测试宿主。

**独立清理：**新增 Auditing `Retention/AuditDetailsRetentionOptions.cs`、`AuditDetailsRetentionRunner.cs`、`AuditDetailsRetentionHostedService.cs`、`AuditDetailsCleanupCheckpointStore.cs`、`AuditDetailsCapturePolicyCache.cs` 和 Persistence 下对应 SQL。由 Auditing 自有 `fn_auditing_details_cleanup_state` 保存清理成功时间/过期积压最老时间的最小共享检查点；遵循 UUID v7、Naming Profile、双库迁移与物化规则。API 通过有界后台缓存刷新，不在每次投影同步查库。Hosting 的 B1 目的地许可只消费 Auditing 注册的最小资格/清理健康策略；模块未启用或检查点不存在时拒绝详情。

**消费/输出：**消费 LG02 Context/LG03 CaptureResult；输出带独立权限的详情 DTO，默认列表不带 Payload。

- [ ] RED：ContextJson 可空历史行、非 ASCII 大小计量、单条超过预算、微批参数预算、详情到期查询不可见；2x 大字段不能绕过 MaxBatchBytes。
- [ ] 增量迁移添加可空 ContextJson 与详情到期字段，源生成、AOT 物化、INSERT/SELECT 双库贯通；一次事务的批写语义保持。历史行不回填猜测值，不新增跨模块表访问。
- [ ] RED：普通 Auditing:Retention.Enabled=false 时独立详情仍清理；到期 API 不可见；Worker 缺失、检查点过期/未知、最大清理滞后超限均拒绝新详情。无详情权限的响应不包含嵌套 Restricted 值。
- [ ] ContextJson/DetailsExpiresAtUtc 原子写入，首次捕获决定绝对期限，不因重试续期。独立 Worker 有限批清除整块详情并更新自身检查点，失败不刷新成功时间；后台策略缓存自身 TTL 到期立即收缩采集，网络/缓存失败不沿用旧的扩大许可。
- [ ] 注册 `auditing.operations.details.read`，受保护详情服务独立鉴权；匿名、租户、只有列表权限均不能获取 Restricted/Payload。未知权限失败关闭。
- [ ] 限制实际 UTF-8 总字节，详情降级/预算拒绝保留摘要，数据库约束失败仍遵循 B1；Worker 分批清除到期详情而保留摘要。列表/旧导出不自动扩展敏感内容。
- [ ] 双库验证旧结构升级、半完成未记账恢复、历史读、事务、查询、权限、清理；同步 OpenAPI snapshot 与生成客户端，不手改生成物。
- [ ] 配置最大保留、清理滞后/检查点有效期和 Worker 条件；检查点不走 Settings 表、业务 Outbox 或跨模块 SQL。备份恢复后先清理到期 ContextJson，再开放详情查询/采集，披露备份物理删除期限。
- [ ] 提交；未完成双库/权限验证不交付为可用详情。

## LG05：可选配置与核心依赖迁移（历史设计）

**Worker Native AOT 原生发送增量（2026-09-30）：**沿用 Worker 外部进程夹具，以隔离 MySQL 和带最小 Producer ACL 的 SASL_SSL Kafka 启动非生产 `ApplicationKafka` Worker；旧原生产物的相同用例未能在普通 Topic 读回资源日志，当前 Worker 原生发布后读回日志且 Kafka key 与 JSON `LogEventId` 相等，并在限定时间内以退出码 0 停止。Worker Native AOT CI 发现阶段明确要求此用例。该证据仅覆盖普通通道与本地 Docker Desktop，未覆盖 Worker 的优先事件触发、正式 Linux CI、目标平台凭据轮换、消费者、崩溃对账与容量；详见[验证记录](../../verification/2026-09-30-log-kafka-native-worker.md)，生产门禁不变。

**直发预算基础增量（2026-09-29）：**新增独立 `Full.NET.Logging.Kafka` 适配程序集中的 `KafkaLogProducerBudget`，对待投递终态的消息做条数和估算 UTF-8 字节双重非阻塞预约；令牌可在投递回调与失败清理竞态下幂等释放。聚焦 Release Unit 通过条数、字节、并发预约及同一令牌并发重复释放四项用例。该类尚未连接 Producer，也不含 SDK/native 缓冲计费；当前 `ApplicationKafka` 启动门禁保持关闭。后续接入必须在 SDK 入队前预约、Broker 投递终态后释放，并同时配置 SDK 自身队列/在途上限，不得把本测试视为直发吞吐或可靠性交付证据。

**宿主静态接线增量（2026-09-29）：**Host.Api/Worker 已显式引用日志 Kafka 适配器并将静态工厂交给 Hosting；仅在非生产显式选择 `ApplicationKafka` 且部署预期匹配时绑定 Kafka 节与创建双 Producer，普通模式不调用工厂。工厂返回空值时 Host 构建失败，避免选中直发后静默丢日志。后台安全快照仅提交日志 Kafka，旧 Console/ES 出口关闭；宿主双通道结束有界排空后释放 Producer。提交拒绝、Broker 投递成功/失败、停机未确认及 SDK 清理失败分别按固定通道/结果低基数计数；异步失败和停机遗留测试经过 RED→GREEN。非生产 Helm 现要求独立配置 Secret，以显式键映射注入 API/Worker；模式和预期模式与 Pod 路由标签固定在同一 Pod 模板，避免滚动切换时共享 ConfigMap 漂移。生产 Chart/Host 直发门禁仍关闭。聚焦宿主测试、本机 AOT analyzer 及 Helm 合约已通过。首次 Linux Native AOT 发布尝试在生成原生代码阶段因 Docker `unexpected EOF` 中断；后续成功复测与原生发送证据见下方增量记录，关闭模式的发布闭包体积及容量对比仍未验证，LG05 与 LG00 保持开放。

**真实 Broker 投递增量（2026-09-29）：**Kafka 4.1.2 Testcontainers 夹具新增日志专用集成用例：普通/优先 Topic 的真实 Broker ACK、读回的事件 ID/载荷及零遗留预约；暂停 Broker 后 SDK 异步失败释放预约，恢复后同一 Producer 再次 ACK 并可读回；非生产 Host 的 `ILogger` 事件经双有界管道与测试注入的真实 Producer 送达双 Topic，并核对快照事件 ID。纳入 `messaging-heavy` 分片并提供 `logging-kafka` 聚焦选择。该组故障夹具明文仅供测试，不改变生产 TLS 门禁；Host.Api 原生进程、崩溃前未确认消息、最终 ES/归档及容量仍待验收，生产门禁不变。

**私有 CA 配置增量（2026-09-29）：**非生产 `ApplicationKafka` Chart 可选引用独立 CA Secret 的固定 `ca.crt` 键，只读挂载到 API/Worker，并把固定路径注入日志 Producer 的 `SslCaLocation`；未配置时使用镜像系统信任根，其他日志模式不注入证书。Helm 合约覆盖两角色和关闭模式。该配置链不等于 TLS/SASL 真实握手或生产准入。

**TLS Broker 增量（2026-09-29）：**独立 Kafka 4.1.2 SSL 容器使用临时 CA/服务端证书，非生产 Host 经正式 `KafkaLogSnapshotExporter.Create`、`SslCaLocation` 与双 Producer 发送 `ILogger` 普通/优先事件，消费者通过 TLS 读回两个 Topic 的同一事件 ID。证书仅用于测试并在夹具退出时删除。此证据覆盖本地 TLS 握手及静态工厂，不覆盖 ACL、Host.Api Native AOT、真实 Helm Secret 投影、证书轮换、原生发布及最终检索；LG05 仍开放。

**SASL_SSL 认证增量（2026-09-29）：**同一独立 SSL 夹具可启用 PLAIN 测试账号；正式 `KafkaLogSnapshotExporter.Create` 用正确凭据完成 Host→Broker→消费者投递，错误密码由真实 SDK 异步回调报告失败，未获 ACK 且归还全部预约。测试账号仅在临时容器 JAAS 文件中，固定字符串不可作为生产凭据。此证据不证明 ACL 最小 Topic 写权限、SCRAM、凭据轮换或原生进程，生产门禁不变。

**Topic ACL 增量（2026-09-29）：**独立 SASL_SSL 测试 Broker 启用 KRaft StandardAuthorizer 并默认拒绝；仅测试管理账号可预置 Topic/ACL，日志 Producer 账号只获指定 Topic 的 Write/Describe。真实幂等 Producer 对授权 Topic 获 ACK，未授权 Topic 无 ACK、异步失败并释放预约；同一账号消费因无 Group 权限被拒绝。此为本地权限行为证据，不等于目标平台 Secret/ACL、SCRAM/轮换、Host.Api Native AOT 或消费端验证；生产门禁不变。

**Host.Api Native AOT 原生发送增量（2026-09-29）：**重跑正式 `linux-x64` 发布后生成原生可执行文件，告警清单通过；新增核心原生外部进程用例，以非生产直发配置启动 Host.Api、连接带最小 Producer ACL 的真实 SASL_SSL Kafka，普通 Topic 读回启动资源日志，优先 Topic 读回按零慢请求阈值归类的 HTTP 操作日志，两者均含管道事件 ID；健康检查与正常退出通过。本地 Linux SDK 容器运行 1/1，测试矩阵已登记；嵌套 Docker 通过显式宿主网关和临时证书 SAN 解决映射端口可达性，未改变生产配置。详细命令、环境和边界见[验证记录](../../verification/2026-09-29-log-kafka-native-aot.md)。该证据尚未覆盖正式 Linux CI、目标平台 Secret/轮换、总停机预算、消费者或容量，生产门禁继续关闭。

**直发配置基础增量（2026-09-29）：**可选适配程序集增加独立的 `FullNet:Logging:Kafka` 强类型配置与校验，分别固定普通/优先 Topic、TLS/SASL、应用待确认消息数/估算字节、SDK 队列消息数/KiB、单消息尺寸、幂等在途请求数、交付/停机超时。禁用明文协议、缺失凭据、重复 Topic 和无法容纳一条最大消息的预算；异常与 `ToString()` 不回显 Secret。`BuildProducerConfig` 仅构造 `acks=all`、幂等、有界的 SDK 配置对象；宿主尚未绑定该配置或创建 Producer，也未验证 native 缓冲，不能因此解除 `ApplicationKafka` 启动门禁。后续必须测量 SDK/native/RSS 峰值与普通洪峰下优先日志容量，不能以两类 Topic 代替隔离。

**投递生命周期原型增量（2026-09-29）：**可选适配项目增加单通道 `KafkaLogDeliveryLane`，每个实例独立持有长生命周期 Producer、应用条数/字节预约和 SDK 队列配置，并显式启用 DeliveryReport、只保留必要的错误报告字段。SDK 入队前预约，异步投递报告、同步异常或 `Local_QueueFull` 释放；停机 Flush 后将仍未确认者计作 abandoned，Flush/Dispose 异常计数且不阻止归还预约。事件 ID 限制为短 ASCII key，key 字节计入消息尺寸与待确认预算，阻止超长/多字节 key 绕过。`KafkaLogProducerPair` 在两个通道各建独立 Producer，先关闭两个入口，再优先排空优先通道并按单一单调时钟截止时间传递剩余 Flush 预算；第二个 Producer 创建失败时清理第一个。单元测试验证两路应用预算隔离与共享 Flush 时限。`Accepted` 仅表示 SDK 接收；假客户端测试不等于真实 Broker ACK、Native AOT 或实际优先通道性能证据。Native Dispose 可能额外耗时，尚无 Host 静态装配、总停机耗时实测、告警/指标与混合路由验证，`ApplicationKafka` 门禁保持关闭。

**最小快照契约增量（2026-09-29）：**Hosting 新增只含预格式化 UTF-8、管道生成的 `LogEventId` 和优先级标记的 `HostLogSnapshot`。双通道后台消费者在 Console/兼容 Sink 之外独立调用可选外部回调；事件 ID 必须来自与 JSON 同一份安全快照，缺失时拒绝外发，不生成第二个 ID。可选 Kafka 组合器已能消费该契约，完整数组路径不再额外复制。Unit 验证普通/优先路由、JSON/ID 一致和缺失 ID 失败关闭；宿主尚未注册回调或解除启动门禁，受限详情与真实 SDK 字节仍待 LG03/LG05/LG06 门禁。

**发布门禁增量（2026-09-29）：**生产 Helm 渲染现拒绝 `Collector`；非生产可预览模式与标签注入。旧 Fluent Bit 示例曾按文件名双 Tail 且未读取 Pod 标签，现已改成单 Tail、Kubernetes 标签准入、普通/优先级重标记的候选配置，并新增固定 Fluent Bit 4.1.1 镜像的 CRI/Docker 样本专项 CI。样本包含 Local/直发排除、元数据缺失、Priority 与 Error 重叠，以及保留 Tail checkpoint 的优雅重启。镜像行为的本地证据见下文；输出故障恢复没有 CI 终态，生产门禁保持。

**固定镜像本地路由增量（2026-09-29）：**Docker Desktop 运行固定摘要的 Fluent Bit 4.1.1 样本时发现，`grep` 直接读取 `@t`/`@mt`/`log.class` 会静默过滤全部记录；现先复制这些字段到临时键验证，再于普通/优先输出前移除。静态契约锁定复制、校验与移除；`node eng/testing/fluent-bit-collector-route-smoke.mjs --docker-desktop` 的真实镜像运行通过，覆盖单 Tail、CRI/Docker、Pod 标签准入、元数据缺失排除、优先级分类、优雅重启及静稳输出后 SIGKILL 再启动的 Tail checkpoint。不可达 Forward、无限重试及同一磁盘缓冲目录在删除源日志后恢复全部事件；改用当前有限重试、持续故障 130 秒的可复核探针显示 B2 Forward 路径两个事件缺失。原始 ID、命令和边界见[验证记录](../../verification/2026-09-29-fluent-bit-collector-route-fault.md)。有限重试候选是有损 B2 策略，不提供跨故障无丢失保证。GitHub Actions 终态、原下游持久 ACK、满盘/节点丢失与容量仍缺证据；生产 `Collector` 门禁保持关闭。

**输出丢失告警增量（2026-09-29）：**候选 Forward/归档输出已设置固定指标别名，并按 Priority 与 B2 分别增加 Fluent Bit 输出丢弃/重试耗尽告警，覆盖此前仅监控应用双通道丢弃而漏掉的采集器下游缺口。固定镜像故障探针现从采集器自身 HTTP 指标端点读取两路别名，复测中 Priority/B2 各丢弃 3/2 条，与删除源日志后的缺失 ID 数一致；重试耗尽各计 1 个 chunk，详见[验证记录](../../verification/2026-09-29-fluent-bit-collector-route-fault.md)。Helm 生产拒绝文案改为当前真实准入条件，不再声称采集器只按文件名选择。采集器本地指标不等于 Prometheus 已抓取或告警通知会触发；这两项仍归 LG06 真实平台验收。

**Prometheus 规则局部验收（2026-09-29）：**固定 Prometheus 3.15.0 镜像的 `promtool check rules` 解析全部 25 条规则，`promtool test rules` 用合成计数覆盖 Priority/B2 丢弃触发、单纯重试不触发、归档别名和故障前无告警；专用命令 `pnpm test:observability-alerts` 已进入固定镜像专项工作流。此规则评估不包含实际 Prometheus 抓取、Alertmanager 通知或目标集群的标签/网络权限，生产门禁不变。

**Priority 无进展告警增量（2026-09-30）：**Priority 无限重试不会触发丢弃/重试耗尽告警；现新增 `FullNetFluentBitPriorityOutputRetryWithoutProgress`，在五分钟窗口内有重试但无成功计数且持续两分钟时告警。固定 Prometheus 3.15.0 的规则检查现解析 26 条，`promtool test rules` 覆盖持续无进展触发与恢复后不误报。该规则只判断输出整体进展，不能识别其他记录成功时某个 ID 仍卡住；目标集群真实抓取与通知仍归 LG06，生产门禁保持关闭。

**DaemonSet 可用性告警增量（2026-09-30）：**采集器目标从服务发现消失时，原 `up=0` 规则可能没有时间序列；现增加 `FullNetFluentBitDaemonSetUnavailable`，以 kube-state-metrics 的 `kube_daemonset_status_number_unavailable` 监控 `fullnet-fluent-bit` 存在时的 Pod 不可用。固定 Prometheus 规则检查现解析 27 条，合成规则测试覆盖持续一分钟触发及恢复清除。目标平台还需确认 kube-state-metrics 抓取、DaemonSet 名称、告警通知，并另行监控整个 DaemonSet 被删除或期望副本变为零；生产门禁不变。

**采集缓冲指标修正（2026-09-30）：**固定 Fluent Bit 4.1.1 探针表明旧 `FullNetFluentBitSpoolHigh` 所引指标在 v1 抓取端点没有时间序列；v2 端点提供 `fluentbit_output_chunk_available_capacity_percent`。测试抓取改为 v2，告警改名 `FullNetFluentBitForwardQueueCapacityLow`，仅报告两路 Forward 逻辑队列剩余容量低于 20% 持续五分钟。固定镜像 `64KB` 队列溢出时，输出丢弃已增加而容量仍为 93.6% 可用；该条件不能作为丢失前预警，独立输出丢弃告警负责已发生损失。旧 `FullNetFluentBitDiskFull` 引用 PVC 指标，却无法覆盖候选的 `emptyDir`，现替换为明确的 `FullNetNodeDiskPressure`；它只报告节点压力，不能证明 Pod 的 2 GiB 限额未满。`promtool` 覆盖低容量与恢复、DiskPressure 与恢复；真实采集器指标及原丢弃告警的 v2 抓取另由专项烟测验证。额外固定镜像 ENOSPC 特征测试确认输入 chunk 写失败时进程仍运行且输出丢弃计数为零，见[缓冲压力记录](../../verification/2026-09-30-fluent-bit-storage-pressure.md)。目标平台 `emptyDir` 用量、驱逐/ENOSPC、恢复与告警仍为 LG06 阻塞项。

**输入写盘故障可观测性增量（2026-09-30）：**固定镜像在 ENOSPC 与输入 chunk 写失败时，`fluentbit_input_ingestion_paused`、`fluentbit_input_storage_overlimit` 及输出丢弃计数仍为零；输入接收计数在首次错误后可能继续增加。专项脚本固定可重复的零值反例，不以单次计数停滞判定损失。生产规则暂不把这些指标伪装成满盘告警；目标集群须补独立错误观测、日志 ID 缺口及恢复对账。

**Prometheus 抓取烟测本地实测（2026-09-29）：**新增 `pnpm test:observability-alerts:live` 命令，专项工作流直接运行同一脚本：固定版本的 Prometheus 抓取固定版本 Fluent Bit 的 HTTP 指标，先等待两路零丢弃基线，再注入不可达 Forward 并查询丢弃计数与 Priority/B2 firing 告警。抓取配置已固定为共享测试夹具，`promtool check config` 成功解析规则文件及目标；脚本语法和工作流接线也已通过静态检查。Windows Docker Desktop 固定镜像实测退出码 0：零丢弃基线、真实抓取、两路丢弃 firing 告警及停止采集器后的 `up=0` 目标失联告警均通过。目标失联规则只覆盖仍在服务发现中的 `job=fluent-bit` 目标；消失目标与 DaemonSet 可用性另需平台门禁。精确提交的 CI 终态仍待取得。该本地结果不替代目标集群抓取、Alertmanager 通知和下游交付确认。生产 `Collector` 门禁保持关闭。

**Priority 重试策略增量（2026-09-29）：**既有双路有限重试探针曾观测到 Priority/B2 均可在重试耗尽后丢弃。现将 Priority Forward 改为 `Retry_Limit=False`，B2 保留 3 次及丢弃告警；RED/GREEN 静态契约与固定镜像 130 秒故障探针确认 Priority 3/3 条从原缓冲恢复、丢弃/重试耗尽均为零，B2 2 条按现有有损预算丢弃。证据见[故障记录](../../verification/2026-09-29-fluent-bit-collector-route-fault.md)。这不解决缓冲满、节点丢失和真实下游 ACK，LG06 生产门禁继续关闭。

**Forward ACK 配置增量（2026-09-29）：**两路 Forward 已要求 `Require_ack_response On`，防止插件在未收到协议 ACK 时直接将发送视为完成；静态 RED/GREEN 和固定镜像路由/不可达输出烟测通过。实际接收端兼容、ACK 对应持久化边界及 ACK 前后 SIGKILL 对账尚未完成，不能据该配置解除生产门禁。

**Forward ACK 行为增量（2026-09-29）：**固定镜像双容器烟测从候选提取两路真实输出选项，验证正常接收端各收到一个 ID 且发送端成功计数各为 1；替换为读取数据但不回 ACK 的接收端后，新 Priority 记录未增加成功计数。脚本与专项 CI 已接线，记录见[故障验证](../../verification/2026-09-29-fluent-bit-collector-route-fault.md)。后续接收端进程崩溃和 TLS 局部验证见下文；目标平台完整交付确认仍缺。

**Forward TLS 增量（2026-09-30）：**候选双输出显式开启服务端证书链及主机名校验，并从只读 `fullnet-forward-ca` Secret 的 `ca.crt` 键加载同一 CA 路径；Secret 名可在平台候选值文件中配置。独立固定镜像脚本保留 ACK/重试/TLS/CA 路径输出选项，使用临时 CA 与匹配 SAN 的接收端验证两路 TLS ACK，再以错误 CA 与不匹配主机名验证两路拒绝。已接入专项 CI，本地 Docker Desktop 样本通过，见[验证记录](../../verification/2026-09-30-fluent-bit-forward-tls.md)。尚缺精确提交 CI 终态、目标平台 Secret 投影、证书轮换及实际下游持久边界；生产 Collector 门禁仍关闭。

**ACK 后接收端进程崩溃增量（2026-09-29）：**同一固定镜像烟测已把待发 ID 送入 `storage.sync full` 的文件系统缓冲接收端，并使其下游不可达；发送端计入 ACK 成功后，SIGKILL 接收端并在保留缓冲目录的条件下重启，原 ID 恢复一次。该单样本收窄了进程崩溃缺口，不能外推到卷/节点丢失、满盘、ACK 与缓冲写入之间全部竞争窗口或真实持久消费者。生产门禁维持关闭。

**无 ACK 空闲连接修正（2026-09-29）：**独立审查要求跨越 ACK 等待期再判断未成功；强化测试发现固定镜像面对保持连接但不回 ACK 的接收端，60 秒仍不重试。两路 Forward 现显式设置 `net.io_timeout 5s`，RED/GREEN 静态与固定镜像实测确认无 ACK 后进入重试且不计成功；后续缓冲 ACK 与 SIGKILL 恢复仍通过。专项 CI 串行探针超时提高到 15 分钟，准确 Actions 终态待验。此修正防止单个不回应连接无限占位，不等于无限时长无丢失。

**Local Helm 与 Host 预览增量（2026-09-29）：**非生产 API/Worker 现在可渲染 `logging.ingress=Local`，Pod 标签和应用 `DeliveryMode`/`ExpectedDeliveryMode` 同值，且不注入日志 Kafka 配置。RED/GREEN Helm 契约覆盖两角色；生产 Local 仍因目标平台采集器排除未验证而拒绝，ApplicationKafka 仍因适配器未装配而全环境拒绝。Host 启动现同步拒绝 Production 中显式 Local/Collector，即使不经 Chart 直接注入同值模式；独立审查指出 Helm 的 `production=false` 原本没有同步 .NET 环境，默认 Production 的 Pod 会被 Host 拒绝。现 Chart 明确注入 `DOTNET_ENVIRONMENT` 与 `ASPNETCORE_ENVIRONMENT`，非生产显式入口须另设 `dotnetEnvironment=Staging` 或 `Development`，生产只允许 `Production`；不匹配在渲染期拒绝。非生产两档保持可用，未指定新模式的旧行为不变。RED/GREEN Unit 与 Helm 渲染契约覆盖该边界。此预览不代表生产 Local 已准入。

**文件：**修改 `src/BuildingBlocks/Full.NET.Hosting/Full.NET.Hosting.csproj`、`Observability/LoggingOptions.cs`、`ServiceDefaultsExtensions.cs`、`ElasticsearchLoggingOptions.cs`、`ElasticsearchSerilogSinkConfigurator.cs`、`Directory.Packages.props`；修改 Api/Worker 配置示例；修改 ObservabilityAdmin `Features/MonitorElasticsearchLogPipeline/ElasticsearchLogPipelineHealthService.cs`、`ElasticsearchLogPipelineContracts.cs` 与相关客户端契约。

**测试：**修改 `tests/Full.NET.UnitTests/Hosting/ElasticsearchLoggingOptionsValidatorTests.cs`；新增 `LoggingConfigurationTests.cs`（同目录）；扩展 `tests/Full.NET.ArchitectureTests` 内现有依赖门禁，新增精确日志依赖用例。

**直发文件：**依据 LG00 冻结的可选适配程序集新增 `KafkaLogSink.cs`、`KafkaLogProducerOptions.cs`、`KafkaLogDeliveryMonitor.cs`、`KafkaLogProducerBudget.cs`；修改 Api/Worker 的静态装配/构建组合及 Secret 注入。Hosting 仅扩展真实适配消费者所需的最小快照消费/注册口，禁止第三方类型泄露与反向依赖。新增对应适配 Unit 和 `tests/Full.NET.UnitTests/Hosting/KafkaLoggingTransportTests.cs`；native 验证按现有专项入口登记，不凭空承诺已有选择集。

**快照审查增量（2026-09-29）：**审查发现普通可选属性可将原始 IP、Body 等受限字段带入 Console/外部快照，长模板回退也会丢失 LogEventId。已补封套负例，收紧 HTTP Operation 可选字段名单及已知受限详情键，回退只保留已校验元数据。普通无标记秘密、受控载荷的来源证明和实际 Kafka/采集出口字节仍须 LG03/LG05 验证；ApplicationKafka 启动门禁保持关闭。

**载荷封套增量（2026-09-29）：**普通日志可以伪造 `RequestPayload`/`ResponsePayload` 属性，原封套会按同名白名单复制。已补失败用例并在最终封套只接受当前分页 DTO 的精确数值 JSON 形态及范围；多余、重复、嵌套、越界或非数字字段均拒绝，同名嵌套属性也移除。该形态验证不提供来源证明，后续 LG03 仍须验证 HTTP 发射点与两阶段许可的一致性，并测量启用投影后的解析成本。

**二次审查修正（2026-09-29）：**受限字段还可藏在普通 `Context` 字符串、嵌套成员或短模板字面量中。现对已知受限详情键的赋值形态执行整块移除，并增加 Console/兼容事件双出口负例；未标记的任意秘密仍不是可自动识别的内容，LG03 出口验证继续开放。

**来源边界审查（2026-09-29）：**试验性的线程局部 HTTP 发射标记因 `ILogger` Provider 可观察并重放 Scope/模板，无法严格证明原事件身份，已撤回。非 HTTP 分类现拒绝复制 HTTP 专属字段别名；伪造 `log.class=http.operation` 的严格来源问题保持开放。后续 LG03 应设计中间件拥有的类型化封套写入路径，并保持现有单入口、队列预算与 Console/兼容出口一致性，不能用时间窗口或属性名充当安全凭据。

**类型化入口增量（2026-09-29）：**已确认 `ILogger`→Serilog 桥接会将自定义 Scope 对象转成字符串，因此不再通过可观察 Scope 传递来源凭据。HTTP Middleware 现直接调用宿主内 `HttpOperationLogIngress`，把固定摘要交给原有双有界 Sink；Sink 为该记录生成事件 ID/资源标识，并仍使用统一快照预算及 Console/兼容出口。普通 `ILogger` 伪造 HTTP 分类时降级为无 HTTP 详情的诊断事件。已用中间件、入口和伪造负例覆盖局部路径；真实宿主、AOT、出口字节与负载验证仍按 LG03 门禁继续。

**入口装配收紧（2026-09-29）：**HTTP Middleware 不再提供将 B2 摘要写回普通 `ILogger` 的兼容分支；缺少 `HttpOperationLogIngress` 时宿主 DI 激活失败，入口已注册但未挂接管道时计 `missing_ingress`。中间件单测使用显式测试入口观察摘要。生产宿主继续复用 ServiceDefaults 已注册的入口实例；真实宿主与停机竞态需继续验证。

**组合字节验证（2026-09-29）：**已将真实 HTTP Middleware、`ILogger`→Serilog 桥接、类型化入口和双通道 Sink 放在同一单测内，直接检查两个入队 UTF-8 快照及旧 ES 兼容 Sink 收到的安全事件拷贝。静态路由模板保留，实际路径段与伪造 HTTP 路由/载荷均不出现。此测试不代替 Console、采集器、旧 ES 网络投递、OTLP、Kafka 的实际出口字节检查，也不构成容量认证。

**Priority 路由修正（2026-09-29）：**失败测试发现慢请求 Warning 虽被 HTTP 闸门认定为 Priority，类型化 Sink 却仅按 Error 级别分流，实际落入 General。现记录在构造时冻结 `reliability.class=Priority`，类型化 Sink 按冻结分类进入高优先队列；组合测试覆盖真实中间件慢请求与伪造普通日志的分流。普通 `ILogger` Error 仍按级别优先；显式 `AlwaysRecordErrors=false` 且未超时的类型化 Error 则留在 BestEffort。仍须在故障洪峰和负载场景验证优先队列年龄、丢弃与请求 P99。

**普通通道故障注入（2026-09-29）：**本地将 General Sink 阻塞并打满普通队列，确认类型化慢请求仍能由独立 Priority Sink 接收，且请求不等待 General 队列空位。测试使用单实例短时阻塞；持续故障、Priority 自身满载、内存/CPU 和生产等价 P99 仍待 LG08 专项矩阵。

**入队统计修正（2026-09-29）：**失败测试证明 Priority 队列拒绝第三条记录时类型化入口仍返回成功，导致 B2 `emitted` 误计。现入口返回未挂接/已入队/被拒绝三态；两路有界 Sink 在实际 `TryAdd` 后返回结果，中间件仅对已入宿主内存队列者计 `emitted`，拒绝者按固定通道标签计 `dropped`。这不是端到端交付确认；真实下游、崩溃及多实例故障仍需门禁验证。

**等值预算边界修正（2026-09-29）：**独立审查发现无旧 ES 拷贝时，恰好 `MaxEventBytes` 的快照预留差额为零，原代码调用 `Release(0)` 并在请求侧抛异常。精确长度 RED 用例已复现，现仅在差额大于零时释放；GREEN 用例覆盖成功入队与最终预算归零。

**错误优先级配置修正（2026-09-29）：**失败测试发现 `AlwaysRecordErrors=false` 时中间件给未超时的 5xx 贴 `BestEffort`，类型化 Sink 却按 Error 级别再次送入 Priority，导致配置的采样与容量语义不一致。现类型化 HTTP 摘要只按冻结的 `reliability.class` 选通道；普通 `ILogger` 的 Error 分流保持原规则。中间件和 Sink 各有回归测试，真实混合负载仍待验证。

- [ ] RED：显式 Local/Collector/ApplicationKafka 模式、缺适配、应用/注入预期模式不一致、直发配 Direct、缺最小 Producer 配置、全关零连接/零创建。逐行覆盖迁移矩阵：无新模式且旧 ES 关闭保留 Console，旧 ES 开启保留直写并告警；任一显式模式与旧 ES 开启冲突拒绝；显式 Local 零远程。不得将新模式默认开启 Kafka。
- [ ] 实现强类型 DeliveryMode/平台 ingress 发布映射和可选静态适配；冻结 Kafka Options 与消息/字节/在途上限。Producer 长生命周期，SDK/native 计费、投递回调、失败/超时、ShutdownFlushTimeout 总预算和安全诊断贯通；释放竞态与普通洪峰保护优先通道必须行为验证。
- [ ] RED：同 ID 只从选定集中入口进入 Kafka；固定发布模板同时生成 Pod 路由标识和应用预期模式，应用启动校验本地一致性，采集器行为测试覆盖混合滚动 Pod、缺失元数据失败关闭、直发 Console/File 镜像不被采集及第三方注册 Sink。切换限时排空、残留/原 ID、排除规则和单一所有权。禁止自动故障切到 Collector 产生双写；Broker 失败不改变 B0/B1。

- [ ] RED：显式 Local 零远程连接；组件关闭不要求 Secret、不探测；错误启用配置给出安全启动错误；旧 ES 配置不能静默失效。未指定新模式只验证旧行为兼容，不能误报显式 Local。
- [ ] 使用强类型 Options/静态注册，实施等级与 Console/File 开关；文件路径、容量、轮转与保留均有上限，写入在后台。File 作为可选构建能力，不能配置引用任意程序集。
- [ ] 贯通 LG02 字节预算及 LG03 目的地许可；日志平台配置不提供 Restricted 导出开关。新预算/清理配置是计划能力，旧配置不得被静默解释为已开启详情。
- [ ] 将旧 ES Sink 迁移为明确兼容阶段：先按迁移矩阵警告/保留/冲突拒绝，平台替代链与存量配置迁移均验证后移出核心写入包。保留历史索引读取方式，不自动删除旧数据；Core 无日志 Kafka 客户端。
- [ ] 管理面区分 disabled/legacy-direct/external-collector/application-kafka 状态及当前确认边界；无 Broker/ES 不阻断业务 readiness。Secret 不进入响应和 Options.ToString。
- [ ] 验证引用图、disabled 注册与 native 产物；Confluent native 现有业务引用不能被误删。可选新依赖先进行集中版本、许可/漏洞和 AOT 分析。
- [ ] 提交并同步配置迁移说明。没有平台替代验证前不删除旧入口。

## LG06：采集缓冲、Kafka 与 ES 纵向交付（历史设计）

**Logstash/PQ 重启局部增量（2026-09-30）：**已固定官方 Logstash 9.5.4 镜像及 Kafka/ES 插件版本；本地 Kafka 4.1.2 单分区、Logstash `enable_auto_commit=false`、持久 PQ、`queue.checkpoint.writes=1` 下，ES 不可达时记录进入 PQ 且 Group Offset 推进至 1。SIGKILL 并保留 PQ 卷后停止 Broker，Logstash 在无法重读 Kafka 的情况下重新输出原 LogEventId 一次，队列清空；Broker 重启后 Offset 仍为 1。专项脚本接入独立 GitHub Actions 工作流，精确提交的 CI 终态仍缺。该单事件窗口不证明 checkpoint/fsync 与 Offset 的全部先后、坏记录/DLQ 可靠隔离、最终 ES 写入、生产凭据或吞吐；见[验证记录](../../verification/2026-09-30-logstash-pq-restart.md)，生产消费组合与 Collector 门禁不变。

**Logstash/PQ 准入阻塞（2026-09-30）：**[Elastic 官方说明](https://www.elastic.co/docs/reference/logstash/tips-best-practices)明确指出 Kafka 输入可能在事件安全写入 PQ 前提交 Offset；这与本计划持久接收先于连续提交的可靠档条件冲突。`enable_auto_commit=false`、`queue.checkpoint.writes=1` 和上述单事件重启通过均不能解除该限制。停止把 Logstash/PQ 当作可由补充故障样本直接准入的基线；LG00 先选定并证明其他确认实现，同一 ADR/规格同步重评，生产 Kafka 消费组合及 Collector 门禁维持关闭。

**Vector SinkConfirmed 候选负例（2026-09-30）：**固定 Vector 0.58.0、Kafka 4.1.2、端到端 ACK 与 ES 部分失败重试的真实镜像实验显示：单项 429 阶段 Offset 不提交，201 后提交至 1；随后单项 400 被 Vector 丢弃，后一条 201 又把连续 Offset 推进到 3，越过失败记录。该配置不满足可靠档准入，不能因 Vector 的 Source/Sink 均标称 at-least-once 而替代 Logstash/PQ。可重复脚本与限制见[验证记录](../../verification/2026-09-30-vector-kafka-es-boundary.md)；LG00 继续寻找能持久隔离坏记录或在缺口处停止提交的实现，生产门禁不变。

**下一候选筛查：**[Apache Kafka Connect `SinkTask.preCommit`](https://kafka.apache.org/37/javadoc/org/apache/kafka/connect/sink/SinkTask.html)允许连接器在提交前刷新/限制安全 Offset；这只是接口能力，不证明某个插件的 Bulk/DLQ 实现。[Confluent Elasticsearch Sink 文档](https://docs.confluent.io/kafka-connectors/elasticsearch/current/overview.html)声明至少一次交付，并说明单项 429 会尝试报告到 DLQ 后使任务失败，仍须实测 DLQ 持久 ACK、连续提交与部分成功。其[代码许可证为 Confluent Community License](https://github.com/confluentinc/kafka-connect-elasticsearch)，不能未经许可评估就并入 Full.NET 的 MIT 发行物。此项只是 LG00 的研究候选，尚未选型、引入依赖或批准部署。

**Kafka Connect 插件源码筛查（2026-09-30）：**IBM 的 [Apache-2.0 Elasticsearch Sink `276c755`](https://github.com/ibm-messaging/kafka-connect-elastic-sink/blob/276c755442f68824716c88adeb2aa02754a52da7/src/main/java/com/ibm/eventstreams/connect/elasticsink/ElasticWriter.java#L327-L335) 在 Bulk HTTP 2xx 且 `errors=true` 时仅记录错误，仍重置失败计数并使 `commit()` 返回；[`ElasticSinkTask.flush()`](https://github.com/ibm-messaging/kafka-connect-elastic-sink/blob/276c755442f68824716c88adeb2aa02754a52da7/src/main/java/com/ibm/eventstreams/connect/elasticsink/ElasticSinkTask.java#L104-L115) 随之成功。按该版本源码，单项失败可能随成功的 flush 推进 Kafka Offset，故不能作为本项目的可靠档消费者。此项为固定提交的静态审查，不代表所有版本或所有 Kafka Connect 插件的结论。Confluent 插件仍因发行许可与实际确认顺序未验证而保持研究状态。

**独立平台消费者研发候选：**上述两项排除不足以宣称所有现成插件不可用；但为了推进 LG00，允许在独立于 Host.Api、Host.Worker、业务模块和业务 Outbox 的实验性平台进程中实现最小 SinkConfirmed 候选。先在单分区、单线程、固定有界批次中证明确认状态机，再扩展分区；不可直接复用业务消息处理器或把客户端加入默认应用发布闭包。实验代码不等于替换 ADR 第 7 条的生产消费者决策，只有下面各步和真实故障证据通过后才冻结实现。

**首个代码切片：**`src/Platform/Full.NET.LogConsumer/ElasticsearchBulkOutcome.cs` 已建立独立类库，`tests/Full.NET.UnitTests/Logging/ElasticsearchBulkOutcomeTests.cs` 先 RED 后 GREEN 覆盖 HTTP 200 混合 201/429/400、缺项、无效 JSON、字符串状态码、顶层矛盾以及同项多动作/成功项带错误。解析器只返回“成功/重试/需要隔离”的建议，不负责 DLQ ACK、连续水位或 Kafka Commit；`Isolate` **不是**已隔离。类库目前只被测试引用，没有部署进程或生产装配，不能据此勾选下面的消费准入项。`logging-delivery` 聚焦选择集已包含这 5 项测试。

**连续水位代码切片：**`src/Platform/Full.NET.LogConsumer/PartitionDeliveryWatermark.cs` 在单分区分配代数下有界记录 Consumer 实际交付顺序。ES 逐项成功或独立 DLQ ACK 才能推进队首水位；数字 Offset 空洞不被误判为缺口，Broker Commit 失败保留安全水位，撤销分区后旧完成回调与水位失效。`tests/Full.NET.UnitTests/Logging/PartitionDeliveryWatermarkTests.cs` 先 RED 后 GREEN 覆盖前慢后快、坏记录待隔离、Commit 重试、撤销代数、容量/顺序及边界 Offset。审查补充的失败用例要求：容量拒绝一条已交付记录后只能重试该 Offset，若仍交付更晚记录或出现倒序，本次分配失败关闭且水位不可提交。当前类型不线程安全，只供单一 Poll 循环串行调用；它不执行 Broker Commit，也无法自行证明调用方确实收到 DLQ ACK。真实消费者、重平衡与 SIGKILL 证据仍未完成，LG00 准入项不勾选。

**输入校验与冻结路由代码切片：**`src/Platform/Full.NET.LogConsumer/KafkaLogRecordParser.cs` 有界解析 Compact JSON，要求 Kafka key 与规范 `LogEventId` 一致，`@t` 带显式时区，分类属于固定集合，冻结的 `OccurredAtUtc` 与 `@t` 为同一瞬间，到期晚于发生且不超过 3650 天，版本在 1..9999；缺失、重复、矛盾或超限均拒绝。已解析记录只以 `ReadOnlySpan<byte>` 暴露私有载荷；写入资格还须按受控“版本→保留天数”映射核对**精确到期时间**，再检查未过期并构造固定 UTC 日期索引名。宿主 `LogEnvelopeBuilder` 剔除调用方提供的三项路由字段，成对显式配置时从原事件时间注入可信值；普通 `ILogger` 伪造的 `security` 分类降为 `diagnostic`，可信安全事件入口待实现。Helm 成对配置，ApplicationKafka 预览入口要求正值，Collector 不配置时仅可用于采集预览而不能进入新 ES 消费路径。RED/GREEN 覆盖路由/分类伪造、跨时区、缺失、到期延长、版本和宿主到消费者契约。此切片尚无 Kafka 消费进程、ES/DLQ 投递或 Offset Commit，输入校验不能解释为完整脱敏审计或生产准入。生产门禁不变。

1. 在独立消费者程序集建立最小输入契约：Kafka `TopicPartitionOffset`、原始受限 B2 日志 JSON、`LogEventId`、冻结的事件 UTC 日期/索引路由版本。开启 `EnableAutoCommit=false` 与 `EnableAutoOffsetStore=false`，固定 Consumer Group、Topic、最大记录字节、批次条数/字节和并行度；禁用未评估的自动跳过与隐式 DLQ。输入校验、固定路由与逐项 Bulk 判定已有 Unit 证据；`Full.NET.LogConsumer` 仍只是类库，尚无 Kafka 运行输入、消费配置或持久投递。
2. 对 ES `_bulk` 逐项解析响应：HTTP 200 只说明 Bulk 请求被接收，必须要求 `items` 条数和顺序与输入一致；每项 2xx 才算成功。429/5xx、HTTP 超时、无效或缺项响应保持原 Offset 未完成，按有界重试/退避处理；400/404 等确定性失败只有在受限日志 DLQ 的 Kafka Producer 收到 `acks=all` 投递报告后才算隔离完成。DLQ 拒绝、超时、容量耗尽或 ACK 未知时停止该分区推进；不要把错误正文、Secret 或 Restricted 详情写进指标/通用日志。先写单项混合状态、响应缺项、DLQ 失败的 RED 测试，再实现解析和判定。
3. 只在所属分区所有较早已交付记录完成后提交其连续 `nextOffset`；记录 Offset 数字空洞不能视作消费缺口。处理写入成功但提交失败、DLQ ACK 后提交失败的重复，使用固定日期索引与 `LogEventId` 文档 ID 覆盖式重放；DLQ 使用稳定事件/来源位点键并记录重放所有权。Unit 覆盖前慢后快、坏记录、乱序完成、撤销分区/epoch、重复提交与取消；Broker + 假 ES 实测 SIGKILL 与重平衡的真实 Offset。
4. 固定镜像/包版本、许可、漏洞、CPU/RSS、ES 连接池、请求超时、各 Topic 分区/副本/保留和 DLQ 访问权限；Broker 故障、ES 429/400、DLQ 失败/满、超限/未知 schema、跨午夜重放和关停分别保存原始 Offset/文档/DLQ 证据。仅把通过的组合回写 ADR、配置与 LG06；容量和生产切流继续受 LG08 与独立批准约束。任一场景越过未完成记录则撤回该实现，不以扩大内存队列或重试次数补救。

**Forward ACK 前采集器 SIGKILL 增量（2026-09-30）：**固定镜像专项脚本现于接收端读到 ID、拒绝回复 ACK 且发送端已重试的窗口杀死采集器；删除源文件后保留发送缓冲与 Tail DB 重启，原 ID 从缓冲获得 ACK。随后杀死已 ACK 且下游不可达的持久缓冲接收端，原 ID 从接收缓冲恢复一次。本地命令退出码 0；真实顺序和边界见[验证记录](../../verification/2026-09-30-fluent-bit-pre-ack-crash.md)。正式 Linux CI、fsync 竞争窗口、满盘/节点失效、TLS 和真实下游确认仍未验证，生产门禁不变。

**文件：**修改 `deploy/observability/fluent-bit-values.yaml`、`otel-collector-values.yaml`（仅核对不重复日志采集）、`prometheus-rules.yaml`、`grafana-dashboard.json`；按 LG00 新选定实现新增 `deploy/observability/log-pipeline-values.yaml`、消费者配置与独立工作负载模板（非应用 Chart），不预设 Logstash 配置文件；修改 `tests/deployment/observability-contract.test.mjs`；扩展 LG00 的真实路由/故障测试。

**消费/输出：**消费版本化 JSON 与 LogEventId；输出 Collector-Direct/Collector-Kafka/ApplicationKafka 的可查询/归档证据。应用只注入直发所需最小写权限，Broker/ES/独立消费者仍不放进应用 Helm。

- [ ] RED：真实日志样本包括 @t、缺失 @l 的 Information、带点键、无显式 Reliability 的 Error、HTTP Priority 与 BestEffort；启动固定版采集器验证解析/路由，不以字符串匹配代替行为。
- [ ] 建立一次读取/分类后分流，修正跨字段 OR、字段名和 CRI/Docker parser；持久存储保存 tail checkpoint 和 buffer，磁盘满/轮转/节点失效分别定义损失预算。
- [ ] 按节点所有实例合计事件/字节速率核算 DaemonSet 资源；记录当前双 Tail 重复读取、100m/500m CPU 和 2Gi emptyDir 的参考限制。固定版本下验证输入/输出线程、文件数量、解析/过滤、TLS/压缩、源文件与缓冲磁盘 I/O，不能仅凭 Pod 数量推断采集能力。
- [ ] 分目的地验证内存/磁盘限额、输入暂停、最旧 chunk 丢弃与重试耗尽；区分卷总容量和输出预算。测普通洪峰对优先流的延迟/丢弃以及共享节点资源对业务 P99 的影响，明确告警与安全降级。
- [ ] 先发布支持旧带点键/新规范字段的采集映射，再按 SchemaVersion 切应用输出；旧采集端回退仍有受控映射。B2/优先 Topic ACL 与资源隔离必须实测，不能只依靠应用两队列。
- [ ] 验证两入口 × 可选 ES/归档及显式 Local 全关；Collector 可 Direct/Kafka，ApplicationKafka 仅 Kafka 且排除同流采集。使用固定版本采集器跑旧/新 Pod 混合、元数据缺失和诊断镜像样本；发布模板将同一档位渲染到 Pod 路由和应用预期模式，应用启动检查只覆盖本地配置，不替代平台运行验证。验证最小 Producer ACL/Secret、TLS/Topic 预置与消费者只读凭据，不允许应用获取 ES/管理权限。
- [ ] 消费者实现/确认边界沿 LG00 重新准入，不复用业务 Worker。新消费者使用独立流水线、Consumer Group、CPU/内存/线程/连接/批量、队列/DLQ 和适用的独占存储；已有非空 PQ 不因更新删除。SinkConfirmed 使用其已证明的提交/持久隔离与恢复配置。普通/优先和查询/归档分开预算，关闭目的地不装配流水线。
- [ ] 日志 Producer 与消费预算独立；批量压缩、acks=all/幂等、副本策略、时间/容量保留、普通/优先 Topic。测定分区热点，不按用户/租户建 Topic。
- [ ] **先验证发送 ACK 边界**：杀死采集器于 SDK 入队后/ACK 前，重启核对 LogEventId。若插件提前释放 chunk/推进检查点导致不可接受缺口，停止生产承诺，替换发送适配或采用可证明持久交付机制；不靠延长 Retry_Limit 伪装修复。
- [ ] 按 LG00 组合实现 PersistentQueue 的 checkpoint/fsync 后或 SinkConfirmed 的逐项最终成功/持久隔离后推进连续 nextOffset；内存入队/HTTP 200 不能替代。重放保持原位点/ID/时间/路由/到期，PQ 卷丢失或 Kafka 保留不足分别按对应 RPO 恢复。
- [ ] ES 受控固定日期索引，data_stream=false、ilm_enabled=false、action=index、document_id=LogEventId；逐项 Bulk 失败分类、429/5xx 重试与经证实可靠的坏记录隔离。固定映射避免动态字段爆炸；过期消息不写入，旧无 ID 日志不宣称同等去重。
- [ ] 针对插件默认 400/404/409/超大请求处理验证，不把失败事件当作完成；DLQ 满/写失败停止接收或处理并告警，不无声丢弃。持久隔离与恢复有独立保留/权限，故障内容不回写原流。
- [ ] 固定版本平台专项 CI 执行故障矩阵；不要把 Kafka/Docker 长测加入 Smoke。同步测试矩阵/分片与供应链证据后提交。

**顺序消费者切片（2026-09-30）：**已新增独立 JIT 进程，禁用 Kafka 自动提交，单记录经校验后写 ES Bulk，逐项成功或受限 Kafka DLQ `acks=all` 持久确认后同步提交 nextOffset。无效/过期/未知路由走 DLQ，暂态 ES 错误保留位点并退出等待监督器退避重启；固定日期索引和 LogEventId 作重放幂等。真实 TLS Kafka 测试已验证正常与坏记录顺序提交、DLQ 原始记录可读；固定镜像 ES 9.5.4 与真实 Kafka 测试已验证写成功但提交失败后重建同组 Consumer 的原位点重放，同一 ID 最终一份可查询文档。另一真实 ES 映射拒绝测试验证逐项 400、DLQ 未确认保留位点、重建同组 Consumer 原位点重放，以及受限 Kafka DLQ Broker 确认后才提交 nextOffset。前两项 ES 测试通过受控传输改写访问容器 HTTP；新增固定版 ES HTTPS 测试验证私有 CA、无效/最小写权限 API Key 的 Bulk 行为，并修复宿主读取仅有公钥的 CA PEM 时误要求私钥的问题。测试自签 CA 无 CRL，测试客户端关闭吊销检查；独立进程的真实 TLS Kafka 故障/重启用例已验证 DLQ Topic 不存在时保留 Offset、修复后原记录进入 DLQ 并提交 nextOffset；它不写 ES。独立进程已在 Development 以无 CRL 的测试 CA 显式关闭吊销检查，经过真实 TLS Kafka、私有 CA、最小写权限 API Key 完成 ES 写入与 Offset 提交；Production 和未指定环境均对关闭吊销检查的配置拒绝启动。无效 ES API Key 使独立进程延期投递且不提交 Offset；修正凭据后重启，同组重放原记录并完成 ES 写入及提交。真实 ES 成功写入但代理暂扣回执时 SIGKILL 独立进程，来源 Offset 未推进；会话失效后重启重放至 Offset 2，同一 ID 最终一份文档。另有 TLS Kafka 成员正常离组后的接管，以及测试专用短 MaxPoll 导致处理中成员退出 Group、新成员接管原 Offset、旧成员迟到提交被 Broker 拒绝的证据。生产在线吊销检查与正式 CA/CRL/OCSP、生产 Poll 预算下全部撤销时序、吞吐或 Native AOT 证据仍缺。生产门禁继续关闭，LG06 故障矩阵和 LG08 容量验收仍开放。局部证据见[故障与交接验证](../../verification/2026-09-30-log-consumer-crash-handoff.md)，详细配置见[日志运维说明](../../operations/logging-module.md)。

## LG07：Vue 三页签、分页与配置状态（历史设计）

**文件：**修改 `ui/admin/src/views/OperationLogsView.vue`、`ExceptionLogsView.vue`、`OutboundCallLogsView.vue`、`components/AuditLogDetailDrawer.vue`（同 views 目录）、`src/api/operation-logs.ts`、`views/ObservabilityElasticsearchHealthView.vue`、i18n 资源；修改 `packages/client-contracts/src/auditing-operation-logs.ts` 的手写验证层，生成接口通过现有 OpenAPI 流程更新。

**分页、筛选与安全摘要进度（2026-09-29）：**操作、异常、外呼三页已改为服务端页码/页大小/总数，并在新请求及卸载时取消旧请求；页面单测覆盖第二页、页大小重置和旧响应覆盖。三页均经生成 Operation 提交各自已有的筛选字段，contains 无时间时补 24 小时 UTC 范围并回显，手动时间无效时阻止请求；共同时间策略有单测。三页分页布局已统一使用 `useArtPagedTableInCard`，分页放在卡片表格容器底部，加载结束后重新测量表高；页面测试验证对应分页布局类，真实浏览器布局仍待验收。操作与异常详情摘要已展示列表返回的用户、租户、IP 指纹等元数据；外呼新增摘要抽屉，展示目标类别、重试次数和安全错误码，不提供不适用的领域差异查询。异常堆栈未额外放入摘要。受限详情三页签与真实栈验收尚未完成。受限详情 Endpoint 已存在，但其 Operation 尚未进入运行时 OpenAPI 快照/生成客户端，须先由双库快照流程接线再做权限门和按需请求；不得在页面手写路径绕过生成契约。本切片不代表 LG07 通过。

**配置状态增量（2026-09-29）：**Elasticsearch 日志健康页在 Collector、ApplicationKafka、Local 模式下只展示入口状态和确认边界，隐藏旧版 ES Sink/索引/集群诊断，避免“未注册即当前路线故障”的误读；兼容直写和旧版未提供模式仍保留原诊断。组件测试覆盖三种非兼容模式与直写模式。页面不证明平台采集器、Kafka 或 ES 的实际投递，真实栈状态验收仍待完成。

**详情差异切换增量（2026-09-29）：**共用详情抽屉按记录 ID、TraceId、可用性和页签变更撤销旧领域差异查询，并在新查询前清空旧结果；迟到响应、取消错误均不得覆盖当前记录。组件测试覆盖“旧请求晚于新请求完成”的竞争条件。

**受限详情契约接线（2026-09-29）：**`auditingGetHostOperationLogDetails` 已进入客户端 manifest、SQL Server/MySQL 同一运行时 OpenAPI 快照和生成的模型/操作/守卫；Vue API 层提供按 ID、可取消的受限详情读取。导出过程中发现 SQL Server 239 迁移同批次提前绑定新列导致首次建库失败，已按仓库既有模式在列添加后分批编译；双库运行时 OpenAPI 与 239 重放恢复测试通过。操作日志抽屉现按普通 Read 与受限详情 Read 双重权限增加“日志消息/请求参数/返回内容”，保留已有变更差异；仅打开受限页签时请求详情，切换记录取消旧请求；缺失、过期、关闭及未采集状态有安全说明。组件测试覆盖无权限不请求、迟到响应以及真实会话权限撤销时移除已展示的 IP、取消在途请求；同一记录切换请求/返回页签复用已取得的详情。真实页面和过期数据的端到端验收仍待执行。

**详情身份核对（2026-09-29）：**受限详情即使结构合法，也必须与查询 ID 相同；后端独立查询服务与 Vue API 入口均新增失败关闭核对。单元测试用错误查询物化行和错配 HTTP 响应复现先前可接受另一条详情的行为，再验证统一拒绝。正常 SQL 仍按 ID 过滤，此核对覆盖映射或缓存异常路径，不改变现有权限与到期策略。

**测试：**新增 `ui/admin/src/views/OperationLogsView.test.ts`；扩展 `tests/e2e/admin-real-stack/tests/host-operation-logs.spec.mjs` 与日志健康页用例。

- [ ] RED：服务端 total 大于首页时翻页请求页码/页大小，不对首页 20 行做伪分页；过滤变更重置页码，取消旧请求避免覆盖新数据。
- [ ] 日志消息显示字段表；请求参数/返回内容显示安全 JSON 和 CaptureState，未开启/不适用/旧行/截断/已脱敏均有中英文解释；JSON 文本不得执行 HTML。
- [ ] 没有 details 权限不请求、不渲染敏感字段；直接详情 API 负例由 LG04 验证。显示原 IP/服务端地址遵守权限，线程号注明捕获点。
- [ ] ES/Kafka disabled、Collector/ApplicationKafka 及确认模式可理解，不显示误导的“应用 Sink 未注册即故障”，客户端不含平台凭据。
- [ ] 运行受影响 Vue Unit/构建/包体；真实栈逐页验收按规则安排，未通过仍保持 Implemented/Build-verified。
- [ ] 提交，不扩展冻结 Layui。

## LG08：容量、恢复与交付封板（历史设计）

**自由文本封套微基准尝试（2026-09-29）：**既有 Host 日志热路径基准新增 Windows 路径和 JSON 转义赋值文本场景，Release BenchmarkDotNet 3/3 场景完成且队列未报丢弃；初次默认构建超时无结果，放大调用次数后仍有迭代时长警告与宽于均值的误差，不能判断扫描开销是否可接受。环境、命令和原始汇总见[验证记录](../../verification/2026-09-29-log-escaped-text-benchmark.md)。LG08 请求 P99、资源及同载荷 A/B 未完成，容量状态不变。

**文件：**扩展 `eng/load/k6/scenarios/audit-logging.js` 和 LG06 专项脚本；仅达到基准/恢复门槛后保存 `docs/verification/2026-09-28-configurable-log-delivery.md`（实际执行晚于该日期时用真实日期）；更新模块说明、降级 Runbook 和对应里程碑能力状态。

- [ ] 记录相同 Release 环境下 Local/Summary、投影开启、Direct、Kafka 档的事件/字节速率、请求 P50/P95/P99、CPU/分配、磁盘/网络、可查询延迟、重复/丢弃；单条/总队列超大与优先洪峰分别测。
- [ ] 执行模块说明 §3.2 的 Collector-Kafka/ApplicationKafka 对比，保持相同消费者/确认配置；计入应用+采集器或应用+SDK/native 的总成本及每实例字节吞吐，确认无双路采集。依据 LG00 预算记录胜出条件/适用部署，批准一条或多条已准入模式，不只报 Kafka 集群峰值或 ConcurrentQueue 微基准。
- [ ] 增加日志等级关闭、当前双通道和目标快照 Summary 的可比基线；分别记录调用线程、后台输出、节点采集器和经 LG00 准入的消费者/ES 处理速率及最老事件年龄。普通/错误/混合洪峰与节点多实例聚合分别测试；请求无明显变慢但持续丢弃或延迟增长不算容量通过。
- [ ] 按允许故障窗口核算缓冲与 Kafka 保留：峰值字节/秒 × 故障秒数 × 安全余量，副本与压缩分开计费；消费者恢复吞吐必须高于持续输入。
- [ ] 用各阶段实际剩余条数/字节预算和净积压速度计算到满时间，记录缓冲保持时间、恢复追赶时间与保留余量；排除 SDK/在途/封套开销后核算有效预算，不能以应用队列 10000 条或示例磁盘容量承诺固定 RPS/故障窗口。
- [ ] 执行进程 SIGKILL、采集器容器重启、Pod 重建、节点故障、满盘、Broker/ES 不可用、部分 Bulk 失败、写成功但 Offset 未提交、重平衡和坏记录矩阵；按 LogEventId 对账缺失/重复。
- [ ] 对直发单独验证 Producer SDK 接收与 Broker ACK 前后崩溃、内存预算耗尽、认证失败及停机；披露无 Spool 的未确认缺口。演练 Collector↔ApplicationKafka 的受控发布/回退、旧入口残留与同 ID 对账，不以临时双写通过恢复验收。
- [ ] 增加三项安全/资源对账：受保护详情和秘密在 Console/File/旧 ES/Kafka Producer/Broker/消费者/DLQ/归档等所有外部副本中不存在；普通清理关闭时详情按期独立清理；超大普通 ILogger/异常/集合下单事件和两队列字节不超计费上限，记录在途/临时/SDK/RSS 峰值。
- [ ] 跨午夜/月、重试、重启/历史重放保持同索引/ID；记录在过期后不能重建索引。演练 PQ checkpoint 前后、输入提交位点前后、DLQ 满、永久卷丢失与非空 PQ 扩缩容；对账连续 Offset 与隔离记录。
- [ ] 检查日志灾难不改变 B0/B1 与认证语义，关闭配置没有外部网络调用；受影响 AOT 原生成功/负例不能由分析通过替代。
- [ ] 架构/安全/恢复评审完成后，独立记录可证明的持久交付起点、RPO/恢复时间和未验证故障。没有目标硬件生产证据保持 Capacity-not-verified。
- [ ] 同步活动计划的真实勾选、能力状态与可回退发布步骤，提交交付；生产部署/切流由后续明确授权控制。

## 命令与执行门禁

以下命令均已有仓库入口；新 `logging-delivery` selection 必须先在 LG01 登记实际测试 ID，不降低发现门槛：

```powershell
pnpm test:task:start -- configurable-log-delivery
pnpm test:integration:affected:plan -- --snapshot configurable-log-delivery --phase inner
pnpm test:dotnet:unit -- --selection logging-delivery
pnpm test:dotnet:architecture
pnpm test:aot:analyzers
pnpm test:observability-deploy
pnpm test:naming
pnpm test:sql-safety
pnpm test:openapi
pnpm test:integration:partitions
pnpm --filter @fullnet/admin test
pnpm --filter @fullnet/admin build
pnpm test:bundle-budgets
pnpm test:integration:affected:plan -- --snapshot configurable-log-delivery --phase slice
git diff --check
git status --short
```

按任务选取命令，不每任务全跑；已编译同源码可按规则使用 --no-build。预期为非零发现、退出 0、无未解释跳过；RED 应因行为缺失失败，环境/编译失败不是有效 RED。slice/merge 真实双库、平台 Kafka、native publish 与真实栈优先 GitHub Actions；仅有本地 Unit 不能勾选环境验证。性能与恢复脚本实施后要先提供 --help/配置校验，再登记准确专项命令。

## 停止条件与回退

- 未找到可信来源的字段只能标记缺失；秘密/无界流无法安全投影时保持 Summary，不以完整 Body 兜底。
- 双库迁移/权限/AOT 闭包不完整时停止该切片交付；不能以停用安全检查通过测试。
- 采集 ACK 或持久恢复无法证明、磁盘满拖垮业务、优先日志挤占业务 Kafka 时停止生产接入；回退为 Local/已验证 Direct，保留待恢复缓冲不删除。
- Restricted 出现在普通输出、无许可仍执行投影、详情清理门禁失效或队列字节不可控时停止该详情/管道切片交付；保留摘要，不能靠 UI 隐藏或扩大内存通过验收。
- 任一入口/消费者/确认组合的依赖/AOT、单一发布所有权、连续 Offset、恢复或去重未证明时禁止该组合；SinkConfirmed 不由关闭 PQ 自动获得。保留已验证模式，不删除旧 Sink/非空 PQ/隔离存储，不自动改变确认语义或消费者实现。
- 关闭 Payload 不改变摘要或审计结果；关闭日志 Kafka 不触发业务 Kafka 切流；移除旧 Sink 前保留旧配置兼容与历史读。
- 方案文档修订不自动勾选 LG00—LG08；执行前确认代码基线与现有无关改动，每个切片凭实际测试更新状态。
