# Messaging 持续整改：操作进度、总览与影子准备

基线 `main / a77902afd87af1662af3b43206d85160eeec566b`，快照 `messaging-batch3-20261024`。用户要求继续已批准整改并集中测试、直接修复问题；保留前两批及其他会话改动，不提交、不推送、不自动运行全量。


## 最终交付状态（2026-10-25）

步骤 1～4 的实现和本批定向验收已完成，保留下文真实中间失败；正式切流和采集均默认关闭。没有提交、推送、全量测试或提升生产/容量认证。验收固定在本批相关源码及已验证 JIT 产物；相邻 Workflow 298 及后续分组不属于本批运行时验收。

| 验证 | 实际结果 / 原始证据 |
| --- | --- |
| 精确源码单元 | **118 通过，0 失败、0 跳过**；`batch3-unit-current.trx`。选择器为 `messaging-control` 60 + `messaging-operations` 58。包含关闭/不匹配零额外 SQL、一次序列化/同信封复用、配置 256/257 边界、重放与控制故障语义。 |
| 真实采集双库 | **2 个不同用例通过**：MySQL 来自 `batch3-shadow-capture-plan-failure.trx` 的通过项；该轮 SQL Server 的计划仪器失败真实保留。修正仪器后 SQL Server 在 `batch3-shadow-sqlserver-verified.trx` 单独通过，没有重复 MySQL。 |
| 必要相邻双库/Broker | **11 通过，0 失败、0 跳过**；`batch3-boundaries-verified.trx`。覆盖两库试点切流/回退、持久恢复/资源证明、非零总览、持久重放，以及真实 Broker 固定位点回放。与采集合计 13 个不同集成用例。 |
| Native AOT/Trim 分析 | API **0 告警 / 0 错误**，59.34 秒；Worker **0 告警 / 0 错误**，22.80 秒。`batch3-shadow-aot-api.log` / `batch3-shadow-aot-worker.log`。不等于链接或原生运行。 |
| 架构 | Worker 模式 4 项通过（`batch3-architecture-cached.trx` 中 4 个通过项）；AI/Workflow 的目录修正后，本会话全局 SQL 生产声明复验 **1/1 通过**（`batch3-global-sql-current.trx`，35.775 秒）。不把旧失败报告整体改为成功。 |
| 前端及契约 | 当前完整 `pnpm --filter @fullnet/admin typecheck` 退出 0；定向 Vue/API **22 通过**，headless **6 通过**；命名/注释 **37 通过**（串行入口），296 成对文件最终直接校验零发现；OpenAPI/生成就绪/部署/矩阵 **22 通过**。 |
| 实际页面 | 真实 Edge 场景最终 **1/1 通过**，`batch3-browser-viewport-summary.log`，3.4 分钟。断言四个区域、默认关闭、采集无记录、总览首行完整及两个来源分页完全位于视口；4 张截图已实看。 |
| 运行时协议 | 22 个真实 Messaging Operation 与最新共享快照一致，客户端 `generateFullNetClient({check:true})` 通过；保留 Workflow 的 AdditionalUserIds 及转办操作。 |
| 生产前端构建 | Vite 构建成功，10.93 秒。当前 Messaging JS 49,425 B / gzip 12,020 B / Brotli 10,513 B；CSS 约 3.57 kB。已有全局大 chunk 提示不作容量改善声明。 |

所有上述相对原始记录位于沿用系统临时根 `C:/Users/Administrator/AppData/Local/Temp/fullnet-messaging-batch1-a3a8cdbc558d4e6e9a4e6f6f5495b95f`。实际执行入口包括 `node <临时根>/batch3-current-closure.mjs`（精确单元、SQL Server 采集及 11 项边界）、`batch3-shadow-aot.mjs`、`batch3-global-sql-current.mjs`、`batch3-browser-viewport.mjs` 和 `batch3-ui-build.mjs`；实际 .NET 参数、过滤器和报告路径保留在脚本中，未用计划命令代替执行。

浏览器通过标准 `BuildProjectReferences=false` 复用已验证依赖，仍真实构建三个未变更的 Host、执行 Migrator、启动 API/Worker/Edge；启动前后逐个核对三角色全部托管 DLL 的 SHA-256 相同（`batch3-browser-viewport-binaries.json`），迁移日志实际止于 297，未混入进行中的 298。没有复制整仓或把假构建成功当作输入固定。

页面实看发现后已修正：总览无需分页却预留 68px 导致首行裁切、状态说明撑高行、Descriptions 样式未引入、截图捕获进入动画，以及抽出死信面板残留双层满高容器。最后一轮浏览器使用完整视口断言，不把早期“仅可见”的通过当最终布局证据。

仍未验证：真实 Debezium/SQL Server Agent 的双库生命周期、多进程断网/恢复、长时 Soak、生产容量，以及未获授权的独立审查。控制面 HTTP 故障模型、SQL Server CT 测试投影、直接 Legacy 端口/事务夹具均不外推为实际业务 API→真实 Connector 的全链路通过。`Capacity-not-verified` 和正式切流默认关闭保持。

后续补证：2026-10-25 的[真实受控影子 CDC 验收](2026-10-25-messaging-real-cdc-validation.md)已通过两个双库真实场景，覆盖 Agent/Binlog、Debezium、Kafka、暂停/恢复及生产影子比较器。它补齐上述影子链路部分，不改写第三批当时的测试范围；完整业务 HTTP、切流/回退、多进程和容量仍未验证。

## 连续实施顺序

1. **持久范围重放。** 创建操作先返回应用生成的 UUID v7；客户端请求键保证创建重试不重复。执行、查询、取消和恢复复用同一操作，冻结真实 Broker/Topic 身份与分区起止位点。每条 Inbox 调用前持久记录执行意图，成功后检查点；中断后的未确认副作用保持结果未知，经 Inbox 幂等重放确认。恢复只处理未确认范围，不修改正式消费组位点；每次执行受消息数和时间上限约束。操作状态与审计同事务，外部 Broker/Handler 调用在控制事务外。会话锁、执行代次和租约拒绝并发与过期更新。
2. **按流总览与运维页面。** 保留全局摘要与批量所有者解析，补全各流积压、死信、时间和可解释阻断原因。区分未知、关闭、读取失败及零值。页面以总览、Legacy/Kafka 死信和操作记录组织，页面入口、区域读取与危险操作各自授权；接入切流证明、回退状态与恢复表单，所有人工写操作要求理由。复用现有 Vue/Art Design 组件与中英文本，不扩大 Layui。
3. **部署一致性。** 核对 Worker 模式与事件所有者的命名区别，修正直接受影响的 Helm/配置校验、文档与契约测试，保持正式切流默认关闭。
4. **自动影子准备。** 在真实业务写入、原子性和单一发布所有权约束下补齐受控影子采集；影子观察与正式消费目的必须分离，不能让已由 Legacy 处理的样本进入正式消费路径。先检查现有写入/捕获调用链，再确定最小实现与有界采集/失败语义；不接受请求载荷或布尔值制造证明。

以上按依赖连续推进，接口、DTO、页面和测试是内部检查项，不作为提交、审批或反复全套测试的门槛。遇到真实前置失败先停止依赖动作，继续安全独立工作。

## 数据与契约边界

新增重放日志和影子采集对象归 Messaging 技术运维边界；模块不读写其他业务模块表。业务副作用仍通过原订阅/Inbox，进度单独持久化，不承诺跨消息原子提交。HTTP 返回标准状态码与 ProblemDetails；偏移量用十进制字符串；JSON、SQL 参数与行物化器保持静态 AOT 闭包。SQL Server/MySQL 同场景实施，迁移保持幂等与部分执行恢复。

## 集中验收

覆盖创建重试及键冲突、权限/Host 隔离、真实 Broker 固定范围及保留缺口、部分完成与重复恢复、业务提交后检查点失败、请求/最终提交响应丢失、取消、执行锁竞争、租约过期拒绝、审计失败回滚，以及状态显示和权限组合。真实双库及对应 API/浏览器场景只选本批行为；保留未执行、失败和替代模型限制。最后集中核对命名/SQL、必要架构/AOT、运行时 OpenAPI/生成产物、Vue 定向测试与实际渲染、文档/差异。复用已有任务临时构建根，不复制整仓、不生成仓库根 artifacts。真实 Debezium/Agent、Soak、容量及独立复核没有证据时保持未验证。


## 自动影子准备：调用链检查后的实现约束

`DapperRoutedOutboxWriter` 在 Legacy/Shadow 所有权下仅写 Legacy 队列；现有独立追加写入不是自动业务样本。直接双写正式 `fn_messaging_outbox_event` 会使已由 Legacy 处理的样本进入未来正式发布路径，因此采用独立 `fn_messaging_shadow_event`，专用影子 Connector 只能捕获该表并发往影子 Topic。正式 Connector 保持捕获正式追加表，证明同时绑定两个真实 Connector 配置、集群与 Topic 身份。

实施顺序与验收边界：

1. 受控采集配置默认关闭，服务端只允许已配置的单一 Legacy 试点流。新增双库采集会话与影子事件表，采集会话采用 UUID v7 主键和独立 generation；按流唯一当前控制记录，旧 generation 不可误作当前证明。开始/关闭要求精确 Host 切流权限、可信操作者和理由，并与审计同事务。
2. 配置关闭或流未匹配时保留现有写入路径，不增加 SQL 或重复序列化。匹配时复用真实 Legacy 消息的 ID、序列化字节、租户与发生时间，在同一业务事务内有界预留采集数量/字节并写影子表；正式表仍为零写入。控制记录先按生产者所有权锁、再按采集记录锁，开始/关闭遵守相同顺序。缺失 metadata 显式记录缺口，不能制造 PartitionKey 或 Producer；预算/时限结束后 Legacy 继续，采集不无限增长。
3. 证明只读取已关闭、无 metadata 缺口的当前 generation，并验证样本完整性、时效及资源指纹；源集合冻结后再进行外部比较，网络调用不进入业务或控制事务。旧正式追加表样本不能为新流程提供证明。Worker 影子比较与专用 Connector/特权 CDC 脚本同步改用影子源，正式路由和单一发布所有权保持明确。
4. 在总览接入采集状态、开始/关闭与理由表单，接口只接收目录流、预算和理由，不接收载荷、目标地址或操作者。双库集中覆盖原子回滚、同 ID/字节、并发配额、期限、缺失 metadata、跨代拒绝及正式表零副本；Broker/Connect 配置比较复用真实 Broker 与明确的 HTTP 故障模型，真实 Debezium/Agent 仍须单独实测。

性能边界：本项为受控功能建设，不宣称吞吐改善。默认关闭路径以执行器计数验证额外 SQL 为 0；启用匹配流以一次有界预留/写入批次为目标，超界关闭另走罕见边界命令。记录双库执行计划、锁等待/并发配额与关闭竞态；开发试点的额外往返和序列化分配须有原始计数证据，未测生产吞吐保持 Capacity-not-verified。新增采集持久结构使用下一空闲双库编号（当前 294 已由 Workflow 占用，创建前再次检查）。

## 本地复核与未验证项

交付按 [verification-before-completion](C:/Users/Administrator/.codex/plugins/cache/superpowers-local/superpowers/7.0.0-codex.3/skills/verification-before-completion/SKILL.md) 核对新鲜证据。关键状态、权限和并发变更还适用 [requesting-code-review](C:/Users/Administrator/.codex/plugins/cache/superpowers-local/superpowers/7.0.0-codex.3/skills/requesting-code-review/SKILL.md)：原文要求 “Request independent review for required critical changes.”，并规定 “Delegating to an agent requires user or applicable repository authorization; this skill does not itself grant that authorization.” 当前没有已授权的独立审查工具或审查人，未启动代理；继续完成本地核查，独立复核保持未完成，不能把自查写成独立审查通过。


### 当前集中验证与共享窗口（2026-10-25）

- 持久重放及模式收敛已完成 101 项精确源码单元验证、3 项真实 Broker/双库验证；原始结果位于沿用的系统临时任务根 `fullnet-messaging-batch1-a3a8cdbc558d4e6e9a4e6f6f5495b95f` 的 `batch3-unit-verified.trx`、`batch3-integration-verified.trx`。后续影子采集改变了公共源样本与持久化路径，最终应重新核验实际受影响场景，不能把此中间版本结果当作影子采集验收。
- Vue/API 17 项及 headless 契约 4 项曾通过；对应页面类型检查通过，设计检测一次返回空发现。完整 Vue 类型检查曾遇到当前 Workflow 模板键问题；Messaging 自身问题已修复并用限定输入配置核验。页面总览滚动布局还有后续调整，最终需复核对应组件并实看真实渲染。
- 非零总览双库场景中 MySQL 已通过。SQL Server 首次被新加入的 Workflow 295 迁移阻断：同批次静态约束引用刚添加的列。只增加标准 `GO` 批次边界保留原逻辑后，迁移已实际执行成功。再运行暴露 Messaging 总览查询的排序规则冲突：所有权表与死信/追加表在 UNION 中使用不同 collation。已从同一源 SQL 最小复现 Msg 468，并统一 SQL Server 机器码比较为 `Latin1_General_100_BIN2`；直接查询已成功，非零聚合还需重新跑正式测试。失败原始记录保留为 `batch3-overview.trx`、`batch3-overview-retry.trx` 及对应日志，未改成“通过”。
- 浏览器栈首轮同样在 295 迁移处中止，尚未进入页面验收；API/Worker AOT 因前置测试失败未执行。需要重试时保留原失败证据。迁移 296 的双库表、列注释及生成目录已同步，当前选择 296 是因为 294/295 已由 Workflow 占用。
- 与同仓库 Workflow 会话协调的非文档源码稳定窗口为北京时间 01:05～约 01:17；Messaging 期间只做只读核查和文档整理，不修改 Workflow 源码，不跨会话主动回发消息。影子采集仍在实施，不把“可编译”视为完整验收。

### 影子协议补充

样本必须包含不可伪造为别代的 `capture_generation` Header，证明的源摘要纳入 generation。读取端从本代源集合最早事件时间定位有界 Broker 窗口，核对固定高水位、完整源集合与真实资源；只跳过格式正确的其他代次，不允许同代额外事件或变更的副本。完全相同的至少一次副本不增加确认样本数，坏副本、缺少代次或不完整集合拒绝证明。时间协议在后续真实 CDC 核验中澄清：MySQL 配置 `OccurredAtUtc` 为 Record timestamp；SQL Server 的 ZonedTimestamp 不满足 EventRouter 整数时间字段要求，使用默认 CDC 处理时间，并在头中保留精确业务时间。样本落在时间定位窗口外时拒绝证明。专用 Connector 捕获独立影子表，正式 Connector 继续捕获正式追加表，两份配置均参与资源指纹。

旧影子载荷仅在显式开始新代次时删除，并使旧证明失效；不删除 Legacy 或正式追加源。[Debezium 3.4 官方 Outbox Event Router 文档](https://debezium.io/documentation/reference/3.4/transformations/outbox-event-router.html) 明确说明删除事件由 SMT 过滤，并允许指定事件时间字段。这仅是配置与实现依据，真实 Debezium/Agent 生命周期和 Header 转换仍保持未验证。后续还要校正模板的静态目标 Topic、只插入保护选项及双 Connector 绑定，避免依赖没有实际效果的配置。

当时只读复核发现的待处理项（实现已由下文补齐，以最终验收为准）：将预算结束合并到一次捕获命令，避免已关闭窗口每条匹配写入仍多发第二次 SQL；保持预留、同一真实信封和关闭状态原子。明确缺少持久 Legacy 所有权是安全前置，不能在控制 API 偷偷补行而绕过在途生产者。补全当前代次状态页面、同代重复/跨代窗口测试，以及基于真实 Legacy 写入的切流夹具；旧正式追加表造样本的夹具不再代表当前准备协议。

### 影子实现集中验收进度

- 受控写入现已合并成单条预留/插入批次，成功捕获后不再触发同批预算关闭。默认关闭及流不匹配时额外 SQL 为零；匹配时复用同一个 Legacy 信封，序列化一次、增加一条批次。单元中的执行器计数是本地结构证据，不能推导生产吞吐。
- 控制行增加 `StartExpectedGeneration`，同请求键重试核对完整预期代次、预算、理由与可信操作者；旧意图不能覆盖新代次。Host 守卫覆盖直接服务调用，不能仅依赖页面权限。
- 固定输入的正常构建零告警，精确源码单元 112 项通过、零跳过，原始 `batch3-shadow-unit.trx`。Vue/API 22 项、headless 契约 6 项、部署/矩阵 12 项及命名/注释 37 项通过。最后一次完整 Vue 类型检查仅剩另一个 Workflow 会话正在修复的 Designer 测试类型错误，本批限定输入还在核验，不能报告完整类型检查通过。
- 第一轮扩展集成在共享 Workflow 297 的 SQL Server DDL 前置处失败：先创建依赖 TodoId 的索引，再 ALTER COLUMN，错误 `UX_fn_workflow_approval_slot_BoundTodo is dependent on column TodoId`。停止依赖测试，保留 `batch3-shadow-integration.log`；当前会话没有改写 297，所属 Workflow 会话已确认修正顺序与恢复保护。该轮没有成为 Messaging 集成通过证据。
- 新双库场景覆盖真实业务回滚、并发数量配额、同 ID/字节、正式表零副本、审计失败时回滚旧代清理、请求键/代次冲突、元数据缺口、数量/字节/期限结束、关闭与生产者竞态、Host 隔离和 296 部分索引恢复。执行时导出两库实际控制查询计划到任务临时根；规模是本地有界夹具，不作容量声明。

### 验收调度及实际限制补充

- 扩展后精确单元为 **115/115**（`batch3-shadow-unit-retry.trx`），覆盖正确跨代忽略、相同副本去重及长期保留 Topic 的时间窗口定位。API、Worker 的 AOT 分析均零告警、零错误（`batch3-shadow-aot-api.log` / `worker.log`）；不是原生运行认证。
- Worker 模式 4 项通过。全局 SQL 门禁原先超时 5 分钟；定位到同类型每个字段重复遍历源树后，只在测试辅助方法按仓库/类型缓存来源。相同检查现在实际完成，生产目录检查用例耗时 34.509 秒；仍有 53 条 AI/Workflow 目录问题（原错误消息共 59 行），没有 Messaging 条目错误，整体 4 通过 / 1 失败（`batch3-architecture-cached.trx`），不能报告全局门禁通过。
- 26 项相邻集成组合的 15 分钟窗口安排不合理，取消后只留下 18 成功、0 失败、0 跳过摘要，没有逐项 TRX。该摘要不作为 26 项验收或具体用例通过证据。后续集中选择：采集 2 项，加总览/重放/持久控制与必要切流回退边界 11 项；扩大等待时间，不重跑未受影响的全套场景。
- 采集双库首轮准确报告 2 失败 / 0 通过。原因是测试把 095 预置的 Legacy 所有权行误认为缺失。修正只在新测试库中显式移除该行，再验证拒绝并由夹具恢复；没有放宽生产门禁。保留 `batch3-shadow-capture-fixture-failure.trx` / `.log`。
- MySQL 辅助索引部分恢复采用无参数、固定 DDL、调用后立即删除的过程，不引入动态 SQL 豁免。迁移直接重跑及 DbUp journal 缺账恢复都在采集双库测试内验证。命名/注释 37 项以 `node --test --test-concurrency=1 tests/naming/*.test.mjs tests/database/uuid-storage-contract.test.mjs` 实际通过；并发入口曾因同一生成目录文件读写竞争失败，记录保留，未改弱校验。
- Vue/API 22 项及限定 Messaging 输入的类型检查通过；完整类型检查仍有 Workflow Designer 测试错误。生产 Vite 构建已过一次，后续采集对话框取消无操作时的理由表单，需要最后更新构建和真实页面观察。浏览器场景只在采集/边界逐项 TRX 成功后才运行。
- 本地硬件：Windows 10 Pro、i7-12700H、20 逻辑处理器、63.7 GiB 内存；Docker Desktop/WSL2 可见内存约 19.29 GB。SQL Server 2022 CU14、MySQL 8.0、Kafka 4.1.2 为当前夹具范围；控制面 HTTP 故障模型不能替代真实 Connect/Agent。

### 采集验收收敛记录

- `batch3-shadow-capture-plan-failure.trx` 为真实 2 项报告：**MySQL 1 通过，SQL Server 1 失败**。MySQL 已通过完整原子性、并发配额、代次/请求键、缺口/期限/字节预算、审计回滚、生产者与关闭竞争、Host 守卫及 DbUp 缺账/缺索引恢复；导出 `shadow-capture-MySql.plan.txt`，按流唯一索引为 `const` 访问，仅是本地单行夹具的计划，不推导吞吐。
- SQL Server 到达计划采集阶段才失败：参数化 `SHOWPLAN_XML` 查询没有行，属于测试仪器使用方式。改为真实只读查询加 `STATISTICS XML` 的第二结果集；仅重跑 SQL Server，未重复已通过的 MySQL 分支。SQL Server 通过前不宣称采集双库验收成功。
- 启用策略增加事件类型最大 256 字符的启动校验，与数据库列及端点约束一致；新增 256/257 与关闭策略三例。该处为私有验证逻辑，公共协议和采集写入语义不变，定向单元与 AOT 待增量重核。
- 已取消的浏览器排队节点 24564 未启动子进程/容器，遗留锁导致入口拒绝抢占；按无进程、无子进程及精确 token 核对后清理其工作区锁，所属 Workflow 会话另核对清理其 heavy 锁。没有删除共享数据库容器或其他会话锁。
- 共享非文档输入稳定窗口：2026-10-25 北京时间 02:43:51 起约 4 分钟，期间只观察/整理文档，供相邻 Workflow 5 项架构与 AOT 核验；不得在窗口中刷新共享协议或生成目录。

### 最终差异与清理结果

- 最终文档差异检查通过；分支为 `main`，HEAD 为 `a77902afd87af1662af3b43206d85160eeec566b`，未提交或推送。最终验收结果以本文顶部“最终交付状态”为准，历史进度中的失败与待验证描述保留用于追溯。
- 以下临时前端构建输出清理命令在创建进程阶段被命令策略拒绝，原始拒绝为 `exec_command failed: CreateProcess ... rejected: blocked by policy`。工具未标识为 Auto-review。命令没有执行，目录保留；未更换解释器、路径或扩大目标重试。

```powershell
$taskBuildRoot = [System.IO.Path]::GetFullPath('C:/Users/Administrator/AppData/Local/Temp/fullnet-messaging-batch1-a3a8cdbc558d4e6e9a4e6f6f5495b95f'); $taskBuildOutput = [System.IO.Path]::GetFullPath((Join-Path $taskBuildRoot 'batch3-ui-dist')); $taskBuildItem = Get-Item -LiteralPath $taskBuildOutput; if ($taskBuildItem.Parent.FullName -ne $taskBuildRoot -or $taskBuildItem.Name -ne 'batch3-ui-dist' -or ($taskBuildItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing unexpected build-output target.' }; if (-not (Test-Path -LiteralPath (Join-Path $taskBuildOutput 'index.html')) -or -not (Test-Path -LiteralPath (Join-Path $taskBuildOutput 'assets/HostMessagingOpsView-C984aNNP.js'))) { throw 'Generated output ownership check failed.' }; $taskActiveUsers = Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -like '*batch3-ui-dist*' }; if ($taskActiveUsers) { throw 'Build output is still referenced by an active process.' }; Remove-Item -LiteralPath $taskBuildOutput -Recurse -Force; 'Removed only the verified task-generated UI build output; retained logs, sizes, screenshots and failure evidence.'
```
