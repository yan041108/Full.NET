# 可配置日志交付与请求详情开发计划

> 执行约束：按仓库 AGENTS.md 逐任务实施；不自动创建工作树、派发子代理或切换独立执行流程。用户已授权修复方案并开始执行；仅按实际验证勾选任务，生产部署/切流仍须单独批准。

**Goal:** 保留 ILogger/Serilog 和审计边界，补齐截图字段及安全请求/返回详情，提供可选采集缓冲、Kafka 和 Elasticsearch 的纵向闭环。

**Architecture:** 应用快照以 Local/Collector/ApplicationKafka 选择入口；Collector 经 Console/文件与平台缓冲，ApplicationKafka 经可选静态适配有界后台直发。两路线同环境比较后开放配置，平台 Direct/Kafka 与 ES/归档独立选择。消费确认基线为 Logstash/PQ，SinkConfirmed 简化候选须独立准入；固定事件日期索引以 LogEventId 去重。Restricted 仅存 B1，独立授权、到期/清理；Vue 三页签。

**Tech Stack:** .NET 10、ILogger/LoggerMessage、Serilog、System.Text.Json 源生成、现有 Dapper 执行器、SQL Server/MySQL、Fluent Bit、可选 Apache Kafka/Logstash/Elasticsearch、Vue 3/TypeScript。版本沿集中依赖与受信平台基线，新平台版本由 LG00 固定镜像/插件及摘要，不自动升级。

## 批准依据与关联

- 当前用户明确要求按已讨论建议更新日志文档、制定开发计划，并尽量采集截图圈出的字段。
- [ADR-0012](../../architecture/adr/ADR-0012-configurable-log-delivery.md)、[总体架构 §16](../specs/2026-07-17-fullnet-architecture-design.md#16-可观测性与高并发日志)、[日志模块说明](../../operations/logging-module.md)定义目标。
- 本计划接管历史 Task 8B 的尚未完成部分与本次字段/可选链路；旧双通道、退出、单事件隔离计划和 Verification 保留历史状态，不改写历史通过证据。本主题后续只更新这一份活动计划。
- 执行基线需重新读取 git rev-parse HEAD / status；文档编写基线为 006ca78f1fff66a6bfe37627264ee527e3d9c673。已有 contracts/database/object-comments.json 用户改动不属于本计划。
- 本次方案审查修订核对基线为 44ad78833a80cdd4823d2863a999e18d7c94fff7；五项意见映射到模块说明 §2.3/§5/§6、LG00/LG02—LG06。文档修订不等于实验或实现已通过。
- 双通道/采集器性能分析补充见模块说明 §2.4/§2.5；调用线程快照成本、共享 stdout、节点汇总采集和逐事件 PQ 检查点分别纳入 LG02/LG06/LG00/LG08，未增加实施完成状态。
- 用户确认后台直发 Kafka 成为正式简化候选，分析验证后可采用或配置选择；更新基线为 32fc0330757dc74b614e8c1b54e57e8824c0537b。最新授权为修复方案并开始分阶段执行；路线准入与配置边界见模块说明 §3.1/§3.2。

## Global Constraints

- B0 同事务 fail-closed；B1 请求等待直接微批写入尝试且默认 fail-open；B2 可采样/可过载丢弃；Audit/日志不使用 Outbox。
- 核心不新增 Kafka/ES 写入强依赖；业务 Kafka 与日志 Kafka 配置、凭据、队列、Topic 和配额分离。
- 请求/返回默认 Summary；投影请求 2048 字节、返回默认 0（显式启用最多初始 2048）、深度 4、集合 16、字符串 128；ContextJson 总 8192 UTF-8 字节。字节预算包括包装结构，不在截断后产生无效 UTF-8/JSON。
- 原 IP/端口、服务端地址与请求/返回详情默认 Restricted，只存 B1 数据库；普通日志/文件/平台副本禁止。B2 只能输出另行审定的 Internal 摘要；秘密任何级别不保存，IP 默认指纹；本阶段不增加 Restricted 外部导出。
- 整事件 MaxEventBytes 初始 16384，普通/优先独立计费上限 67108864/8388608 字节，同时保留 10000/1000 条数上限；64 属性、深度 4、集合 16、普通字符串 2048 字符。这些是起始保护值，非 RSS/吞吐认证。
- 先取得采集许可再执行工厂和有界源生成序列化；无许可零工厂执行。B1 资格/独立预算不由 B2 采样决定，关闭 B2 不阻止既有 B1 摘要。
- 消费确认按准入组合冻结：PersistentQueue 的连续持久接收，或 SinkConfirmed 的逐项最终成功/可靠隔离；都只推进连续 nextOffset。不能把 Logstash 改为内存队列代替 SinkConfirmed。ES 固定事件日期索引/ID/到期和 index 动作不变。
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
| R05 消费者与确认机制未选定 | Logstash/PQ 基线；SinkConfirmed 独立比较；连续 Offset/Bulk/隔离准入 | LG00/LG06：固定实现/版本及故障证据；失败组合关闭，不外推其他组合 |

表中为已修订设计和待取得证据，不是运行验证结果。

### 执行依赖与交付顺序

LG00 定义两入口及消费确认候选、依赖/AOT 可行性和可比实验预算，可与应用正确性切片推进；应用依次 LG01 → LG02 → LG03 → LG04。LG05 实现模式及准入的可选直发适配，移除旧 Sink 等待 LG06 替代链验证。LG06 依赖 LG00/LG02/LG05 并验证单入口；LG07 依赖 LG04/LG05，LG08 对比和封板后才批准目标部署路线。采集映射仍先于 Collector wire 切换。执行状态由下方检查项和实际证据表示。

## LG00：入口路线、消费者与确认边界准入实验

**文件：**读取当前平台配置/供应链约束；实验代码拟放 `eng/observability/verify-log-delivery.mjs`、`tests/deployment/log-pipeline-routing.test.mjs`；固定选型及证据回写本计划/模块说明，不另建第二份活动计划。仅真实实验完成后保存对应日期的 Verification。

**选择与归属：**首选 Logstash 作为独立平台 Kafka 消费者；Kafka 输入 + 每流水线有界持久 PQ + ES/归档输出。平台团队负责运行、升级、PQ/DLQ、资源和凭据；不新增应用 .NET 日志消费者、不复用业务 Worker。实验需提供实际镜像摘要、输入/输出插件版本、配置、许可和适用存储。

**两入口：**Collector 与 ApplicationKafka 为正式候选。冻结直发适配归属/程序集、Host.Api/Worker 静态消费者及发布组合，依赖方向为宿主 → 可选适配 → Hosting 最小稳定口，Hosting 不反向引用 Confluent 实现；第三方类型不进入公共契约。只有真实复用与依赖隔离证据支持时拆项目，不为目录完整增加抽象。Collector 默认产物与直发产物分别核对闭包，不能凭 Enabled=false 推断 native 包已排除。

- [ ] 定义同环境入口比较：固定安全/采样/事件大小、Broker 分区/acks/压缩和相同消费者，记录可接受请求 P99 增量、每实例字节吞吐、CPU/托管及 native 内存、丢弃/缺口与恢复目标。先比较入口，再单独比较消费确认，实验不代替 LG08 生产容量证据。
- [ ] 原型验证有界后台直发的连续 Produce/回调或有界 ProduceAsync、双优先预算、QueueFull/超时/停机；不在请求等待 ACK，不逐条串行 await、不无界任务。验证 Broker ACK 前后 SIGKILL 的缺口、来源 ID、重试及消费者可查询，不以 SDK 接收视为持久确认。
- [ ] 验证可选适配的版本/许可/体积、DI/源生成/裁剪与 Host.Api Linux Native AOT 原生发送、不可用/认证失败及退出；既有 kafka-replay 证据不能外推日志 Producer。能力未通过的构建禁止配置 ApplicationKafka。
- [ ] 冻结发布清单的每 Pod 路由标签/注解和应用预期模式注入；针对旧 Collector Pod 与新 ApplicationKafka Pod 同时存在、缺失 Kubernetes 元数据、Console/File 镜像和当前双 Tail 通配规则建立真实路由负例。没有失败关闭的采集隔离证据不准入 ApplicationKafka。
- [ ] 比较 PersistentQueue 与候选 SinkConfirmed：只有真实实现支持逐项最终写入/持久隔离后推进连续 Offset，才可省 PQ；验证重平衡、乱序部分成功、写成功未提交及坏记录。冻结实现/版本/确认配置，未找到准入实现则保留 PQ，不把 Logstash 内存队列当等价替代。

- [ ] 固定 Logstash/插件/ES/采集器版本；检查文档对应版本、许可与漏洞，给出每流水线 JVM/批次/连接/PQ/DLQ 上限和可恢复独占卷。镜像固定后才能登记实际测试命令。
- [ ] 验证 enable_auto_commit=false、queue.type=persisted、queue.checkpoint.writes=1 的真实先后：PQ 写入/fsync 前后分别 SIGKILL，再查 Broker Offset 与恢复记录。先写后续快记录、前序慢/失败记录，证明不能越过连续缺口。
- [ ] 验证 ES HTTP 200 的部分 400/404/429/5xx、超大记录、未知 schema、DLQ 失败/满盘；逐条跟踪最终成功或可靠隔离，不接受插件默认丢弃。隔离无法证明则停止该可靠档准入；不靠启动成功勾选。
- [ ] 验证固定日期索引：显式 data_stream=false、ilm_enabled=false、document_id=LogEventId、action=index；索引由冻结事件 UTC 日期/路由版本决定。跨午夜/跨月重放只有一个目标记录，过期/已清理索引不可被重建。
- [ ] 验证正常重启、Pod 重建、永久卷丢失、消费者重平衡和非空 PQ 扩缩容；明确 PQ 不复制数据，RPO、Broker 保留内回放、单一重放所有权与恢复成本。
- [ ] 报告 checkpointWrites=1 的吞吐/磁盘成本；PQ 组合失败只关闭该组合，保留其他已验证路线。SinkConfirmed 必须独立准入；固定替代实现/确认配置并更新同一 ADR/计划，不静默回退或自动更换组件。
- [ ] 测量每事件检查点配置下的 PQ 写入/检查点耗时、存储延迟、稳定消费与故障后追赶；准入要求不仅确认正确，还能在目标持续输入下排空积压。吞吐不足先查持久存储/流水线瓶颈，不以扩大 checkpoint 间隔绕过确认门禁。

**完成标准：**分别记录入口/构建/确认组合的可行性、持久起点、Bulk/DLQ/Offset 与去重证据；失败组合保持关闭，成功原型仍须 LG05/LG06/LG08 纵向交付才能启用目标部署。不能因一条路线通过就放行另一条或删除旧 Sink。

## LG01：结果与字段正确性基线

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

## LG02：上下文、分类与高频生成

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

## LG03：安全请求/返回投影与独立预算

**本轮增量证据：**通用 Serilog 入队封套补充签名键、带点/索引式赋值、嵌套 JSON 凭据与属性洪峰回归；敏感模板删除所有被引用的占位符值，仅保留未被引用且经过严格校验的路由/关联字段，过长模板不保留属性。`SignInCount`/`SignOutTime` 不误判为签名材料。Console UTF-8 和旧 ES 兼容事件同测；`logging-delivery` 本地选择集 134/134、API Native AOT 架构集 73/73、AOT 分析已通过。仍须按 LG03/LG08 验证其他出口及真实负载性能，不能据此勾选整个 LG03。

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

## LG04：B1 详情双库扩展、查询与授权

**先行预算修正（2026-09-28）：**现有 B1 信封以 UTF-8/UTF-16 字节较大值计入文本及固定开销；单条超过 `MaxBatchBytes` 即 fail-open 拒绝，当前批次无法容纳下一条时延后到下一批，停机时延后项也必须完成或标记失败。已用 RED/GREEN 覆盖多字节低估、ASCII 内存费用、两条合批越限、单条超限和停机取消。后续增量新增 `QueueMaxBytes` 对等待、排队和整批写库中的信封预留总字节，条数和字节任一超限均 fail-open；入队超时、通道关闭、热更新后的单条超限及停机排空释放额度。独立审查发现 writer 解析、停机排空、运行中热配置失效可能使请求永久等待，均以故障注入测试复现并修正为 fail-open：已接受的排队/延后信封在配置持续失效时终结，新请求读取无效配置也立即拒绝，消费循环限速。最终本地 `logging-delivery` 149/149、API Native AOT 架构 73/73、AOT 分析 0 警告/错误、治理 55/55、工具链 52/52；热配置故障测试额外重复三轮。完整 Architecture 232/232 运行于本切片最终异常修正前。真实 Host.Api、双库与容量认证仍待执行。该预算修正结束时详情字段、双库详情与独立清理仍未实施，LG04 不勾选完成。

**详情存储基座增量（2026-09-28）：**已新增双 Provider 239 可空 `ContextJson`/`DetailsExpiresAtUtc` 迁移和中文对象说明；MySQL 使用固定字面量的逐列 `PREPARE` 适配非事务 DDL，并按命名规则登记精确债务。新增双库恢复测试模拟旧行与第一列已提交但 DbUp 未记账，检查空值及重复重放。当前仅准备存储结构：应用写入仍为摘要，列表及既有详情响应不读取新列；捕获、绝对到期写入、独立清理、详情权限和真实双库测试仍未完成，LG04 不勾选完成。

**独立清理增量（2026-09-28）：**双库 239 增加到期索引及恢复断言；Worker 新增独立 `Auditing:DetailsRetention` 预算和有界清理循环，SQL Server 单语句批量清空、MySQL 短事务领取后按 Id 清空，均保留操作摘要。该清理不读取普通 `Auditing:Retention.Enabled`；末批满额时立即继续下一轮，短批或失败才进入配置轮询，避免积压受固定间隔限速。Unit 已覆盖双 Provider SQL 形状、批次与配置边界；双库 API 集成夹具已增加过期/未过期详情的分批清理与摘要保留断言，但尚未在真实数据库运行。架构边界精确登记此 Worker Host 作用域，相关测试通过；共享清理检查点、健康资格和详情采集仍未实施，真实双库清理/索引效率仍待 Actions 和执行计划验证，不将 LG04 标为完成。

**共享检查点增量（2026-09-28）：**尚未发布的双库 239 同步增加 `fn_auditing_details_cleanup_state` 单行表和中文注释；Worker 仅在清理与最早过期积压读取均成功后写入成功时间及积压时间。SQL Server 事务内更新/首次插入，MySQL 单语句 Upsert；多实例较旧的观察时间不得回退较新的状态。迁移重复重放、非空积压到追平后的状态转换、两实例并发首次写入及旧观察不回退断言已编写，但真实数据库尚未运行。API 后台读取、TTL/滞后健康资格及详情捕获尚未接入，因此检查点本身不授予采集许可。

**API 资格缓存增量（2026-09-28）：**Auditing 已提供双库检查点读取与 API Native AOT 显式行物化；API 后台独立 Host 作用域刷新，默认禁用不查库，刷新失败立即清空本地资格。请求路径只读内存，按本地缓存有效期、Worker 最近成功时间、最早积压滞后及未来时间失败关闭；配置默认 30/90/180/300 秒，均可验证边界。Unit 覆盖缺失、过期、未来、积压、禁用、热配置失败与刷新读取失败。审查发现 MySQL Worker 的 `DateTime?` 最早过期时间查询在 Native AOT 下缺少标量识别，已补齐 `DateTime` 类型并用先失败的架构测试防回归。此时 B1 目的地许可尚未消费该资格；后续增量接入见下文。

**B1 固定网络上下文增量（2026-09-28）：**B1 Operation 的静态路由详情入口现在消费 API 本地清理健康资格；默认开关关闭且白名单为空。启用后仍须完整静态路由命中、保留小时数不超过操作摘要保留期，才序列化固定版本的客户端/服务端 IP 与端口；不读取任意 Header、Query、Body 或响应流。详情与首次捕获确定的绝对到期时间同批写入双库可空列，并计入 B1 字节/SQL 参数预算；超大详情降级为摘要。SQL 写入故障诊断改为固定信息，不带可能包含受限参数的异常文本。Unit 覆盖拒绝与通过路径、Middleware 注入、列参数、计费及超限降级；真实双库写入与请求/响应受控投影仍待验证/实施，LG04 不勾选完成。

**独立详情读取增量（2026-09-28）：**新增 `GET /api/v1/auditing/operation-logs/{operationLogId}/details`，同时要求 `auditing.operations.read` 与 `auditing.operations.details.read`。查询只在 Host 作用域选择未到期的可空详情，响应前再次核对到期和版本化固定字段；缺失、过期、非法详情统一返回 404。响应只含白名单网络上下文，不透出原始 ContextJson；旧列表、普通详情和导出继续只返回摘要。已增加 SQL Server/MySQL 查询选择的 Unit、双 Provider HTTP/数据库集成夹具和 OpenAPI 冻结夹具。现有客户端 manifest 未登记这个尚无 UI 消费者的新 Operation，离线快照不需改变；待 UI 绑定时由运行时生成器更新客户端。真实双库 HTTP 与运行时 OpenAPI 仍待 CI 验证；请求/响应受控投影尚未实施，LG04 不勾选完成。

**导出端点受控投影增量（2026-09-28）：**已为成功的操作日志导出请求接入 B1 专属固定字段投影。详情资格和精确静态路由先通过，随后才运行请求/结果工厂；请求摘要只含规范化时间、状态码和成功筛选，结果摘要只含行数、截断及敏感列标记，不包含 PathContains、文件名、文件字节、Body 或 Header。请求和结果分别按 UTF-8 上限计费，结果默认关闭；失败、未许可或超限只留下安全 CaptureState 或不采集，不替换导出响应。该切片不表示其他 Endpoint 的请求/返回已覆盖，真实双库及 API Native AOT 仍需验证，LG04 不勾选完成。

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

## LG05：可选配置与核心依赖迁移

**直发预算基础增量（2026-09-29）：**新增独立 `Full.NET.Logging.Kafka` 适配程序集中的 `KafkaLogProducerBudget`，对待投递终态的消息做条数和估算 UTF-8 字节双重非阻塞预约；令牌可在投递回调与失败清理竞态下幂等释放。聚焦 Release Unit 通过条数、字节、并发预约及同一令牌并发重复释放四项用例。该类尚未连接 Producer，也不含 SDK/native 缓冲计费；当前 `ApplicationKafka` 启动门禁保持关闭。后续接入必须在 SDK 入队前预约、Broker 投递终态后释放，并同时配置 SDK 自身队列/在途上限，不得把本测试视为直发吞吐或可靠性交付证据。

**直发配置基础增量（2026-09-29）：**可选适配程序集增加独立的 `FullNet:Logging:Kafka` 强类型配置与校验，分别固定普通/优先 Topic、TLS/SASL、应用待确认消息数/估算字节、SDK 队列消息数/KiB、单消息尺寸、幂等在途请求数、交付/停机超时。禁用明文协议、缺失凭据、重复 Topic 和无法容纳一条最大消息的预算；异常与 `ToString()` 不回显 Secret。`BuildProducerConfig` 仅构造 `acks=all`、幂等、有界的 SDK 配置对象；宿主尚未绑定该配置或创建 Producer，也未验证 native 缓冲，不能因此解除 `ApplicationKafka` 启动门禁。后续必须测量 SDK/native/RSS 峰值与普通洪峰下优先日志容量，不能以两类 Topic 代替隔离。

**投递生命周期原型增量（2026-09-29）：**可选适配项目增加单通道 `KafkaLogDeliveryLane`，每个实例独立持有长生命周期 Producer、应用条数/字节预约和 SDK 队列配置，并显式启用 DeliveryReport、只保留必要的错误报告字段。SDK 入队前预约，异步投递报告、同步异常或 `Local_QueueFull` 释放；停机 Flush 后将仍未确认者计作 abandoned，Flush/Dispose 异常计数且不阻止归还预约。事件 ID 限制为短 ASCII key，key 字节计入消息尺寸与待确认预算，阻止超长/多字节 key 绕过。`KafkaLogProducerPair` 在两个通道各建独立 Producer，先关闭两个入口，再优先排空优先通道并按单一单调时钟截止时间传递剩余 Flush 预算；第二个 Producer 创建失败时清理第一个。单元测试验证两路应用预算隔离与共享 Flush 时限。`Accepted` 仅表示 SDK 接收；假客户端测试不等于真实 Broker ACK、Native AOT 或实际优先通道性能证据。Native Dispose 可能额外耗时，尚无 Host 静态装配、总停机耗时实测、告警/指标与混合路由验证，`ApplicationKafka` 门禁保持关闭。

**最小快照契约增量（2026-09-29）：**Hosting 新增只含预格式化 UTF-8、管道生成的 `LogEventId` 和优先级标记的 `HostLogSnapshot`。双通道后台消费者在 Console/兼容 Sink 之外独立调用可选外部回调；事件 ID 必须来自与 JSON 同一份安全快照，缺失时拒绝外发，不生成第二个 ID。可选 Kafka 组合器已能消费该契约，完整数组路径不再额外复制。Unit 验证普通/优先路由、JSON/ID 一致和缺失 ID 失败关闭；宿主尚未注册回调或解除启动门禁，受限详情与真实 SDK 字节仍待 LG03/LG05/LG06 门禁。

**发布门禁增量（2026-09-29）：**生产 Helm 渲染现拒绝 `Collector`；非生产可预览模式与标签注入。旧 Fluent Bit 示例曾按文件名双 Tail 且未读取 Pod 标签，现已改成单 Tail、Kubernetes 标签准入、普通/优先级重标记的候选配置，并新增固定 Fluent Bit 4.1.1 镜像的 CRI/Docker 样本专项 CI。样本包含 Local/直发排除、元数据缺失、Priority 与 Error 重叠，以及保留 Tail checkpoint 的优雅重启。当前仅完成静态配置检查与样本夹具生成；镜像行为、元数据失败关闭、崩溃/输出故障恢复没有 CI 终态，生产门禁保持。

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

## LG06：采集缓冲、Kafka 与 ES 纵向交付

**文件：**修改 `deploy/observability/fluent-bit-values.yaml`、`otel-collector-values.yaml`（仅核对不重复日志采集）、`prometheus-rules.yaml`、`grafana-dashboard.json`；新增 `deploy/observability/log-pipeline-values.yaml`、`log-consumer-values.yaml`、`logstash-pipelines.conf`、`logstash-settings.yaml` 和独立消费者工作负载模板（例如 log-consumer-statefulset.yaml，非应用 Chart）；修改 `tests/deployment/observability-contract.test.mjs`；扩展 LG00 的真实路由/故障测试。

**消费/输出：**消费版本化 JSON 与 LogEventId；输出 Collector-Direct/Collector-Kafka/ApplicationKafka 的可查询/归档证据。应用只注入直发所需最小写权限，Broker/ES/独立消费者仍不放进应用 Helm。

- [ ] RED：真实日志样本包括 @t、缺失 @l 的 Information、带点键、无显式 Reliability 的 Error、HTTP Priority 与 BestEffort；启动固定版采集器验证解析/路由，不以字符串匹配代替行为。
- [ ] 建立一次读取/分类后分流，修正跨字段 OR、字段名和 CRI/Docker parser；持久存储保存 tail checkpoint 和 buffer，磁盘满/轮转/节点失效分别定义损失预算。
- [ ] 按节点所有实例合计事件/字节速率核算 DaemonSet 资源；记录当前双 Tail 重复读取、100m/500m CPU 和 2Gi emptyDir 的参考限制。固定版本下验证输入/输出线程、文件数量、解析/过滤、TLS/压缩、源文件与缓冲磁盘 I/O，不能仅凭 Pod 数量推断采集能力。
- [ ] 分目的地验证内存/磁盘限额、输入暂停、最旧 chunk 丢弃与重试耗尽；区分卷总容量和输出预算。测普通洪峰对优先流的延迟/丢弃以及共享节点资源对业务 P99 的影响，明确告警与安全降级。
- [ ] 先发布支持旧带点键/新规范字段的采集映射，再按 SchemaVersion 切应用输出；旧采集端回退仍有受控映射。B2/优先 Topic ACL 与资源隔离必须实测，不能只依靠应用两队列。
- [ ] 验证两入口 × 可选 ES/归档及显式 Local 全关；Collector 可 Direct/Kafka，ApplicationKafka 仅 Kafka 且排除同流采集。使用固定版本采集器跑旧/新 Pod 混合、元数据缺失和诊断镜像样本；发布模板将同一档位渲染到 Pod 路由和应用预期模式，应用启动检查只覆盖本地配置，不替代平台运行验证。验证最小 Producer ACL/Secret、TLS/Topic 预置与消费者只读凭据，不允许应用获取 ES/管理权限。
- [ ] 消费者实现/确认边界沿 LG00 准入记录，不复用业务 Worker。Logstash 基线使用独立流水线、Consumer Group、JVM/线程/连接/批量、PQ/DLQ 和独占卷，非空 PQ 不因更新删除；SinkConfirmed 使用其已证明的提交/持久隔离与恢复配置。普通/优先和查询/归档分开预算，关闭目的地不装配流水线。
- [ ] 日志 Producer 与消费预算独立；批量压缩、acks=all/幂等、副本策略、时间/容量保留、普通/优先 Topic。测定分区热点，不按用户/租户建 Topic。
- [ ] **先验证发送 ACK 边界**：杀死采集器于 SDK 入队后/ACK 前，重启核对 LogEventId。若插件提前释放 chunk/推进检查点导致不可接受缺口，停止生产承诺，替换发送适配或采用可证明持久交付机制；不靠延长 Retry_Limit 伪装修复。
- [ ] 按 LG00 组合实现 PersistentQueue 的 checkpoint/fsync 后或 SinkConfirmed 的逐项最终成功/持久隔离后推进连续 nextOffset；内存入队/HTTP 200 不能替代。重放保持原位点/ID/时间/路由/到期，PQ 卷丢失或 Kafka 保留不足分别按对应 RPO 恢复。
- [ ] ES 受控固定日期索引，data_stream=false、ilm_enabled=false、action=index、document_id=LogEventId；逐项 Bulk 失败分类、429/5xx 重试与经证实可靠的坏记录隔离。固定映射避免动态字段爆炸；过期消息不写入，旧无 ID 日志不宣称同等去重。
- [ ] 针对插件默认 400/404/409/超大请求处理验证，不把失败事件当作完成；DLQ 满/写失败停止接收或处理并告警，不无声丢弃。持久隔离与恢复有独立保留/权限，故障内容不回写原流。
- [ ] 固定版本平台专项 CI 执行故障矩阵；不要把 Kafka/Docker 长测加入 Smoke。同步测试矩阵/分片与供应链证据后提交。

## LG07：Vue 三页签、分页与配置状态

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

## LG08：容量、恢复与交付封板

**文件：**扩展 `eng/load/k6/scenarios/audit-logging.js` 和 LG06 专项脚本；仅达到基准/恢复门槛后保存 `docs/verification/2026-09-28-configurable-log-delivery.md`（实际执行晚于该日期时用真实日期）；更新模块说明、降级 Runbook 和对应里程碑能力状态。

- [ ] 记录相同 Release 环境下 Local/Summary、投影开启、Direct、Kafka 档的事件/字节速率、请求 P50/P95/P99、CPU/分配、磁盘/网络、可查询延迟、重复/丢弃；单条/总队列超大与优先洪峰分别测。
- [ ] 执行模块说明 §3.2 的 Collector-Kafka/ApplicationKafka 对比，保持相同消费者/确认配置；计入应用+采集器或应用+SDK/native 的总成本及每实例字节吞吐，确认无双路采集。依据 LG00 预算记录胜出条件/适用部署，批准一条或多条已准入模式，不只报 Kafka 集群峰值或 ConcurrentQueue 微基准。
- [ ] 增加日志等级关闭、当前双通道和目标快照 Summary 的可比基线；分别记录调用线程、后台输出、节点采集器和 Logstash/ES 的处理速率及最老事件年龄。普通/错误/混合洪峰与节点多实例聚合分别测试；请求无明显变慢但持续丢弃或延迟增长不算容量通过。
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
