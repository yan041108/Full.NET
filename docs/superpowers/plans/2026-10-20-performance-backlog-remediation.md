# 80 项性能问题批量修复

## 授权与输入

- 用户要求批量修复，最后统一测试验证；范围为 [性能清单](../../verification/performance-review-backlog-2026-10-20.md) PERF-01～PERF-80。
- 基线：main，HEAD `49345c3c9823360071433adb4928e09691a35d62`，开始时 286 项既有工作区变更。
- 任务差异边界：`performance-backlog-20261020`，使用 Git 元数据与文件哈希快照，不复制仓库。
- 优先修复 P1；实现和回归用例连续推进，最后冻结输入并集中验收。不安排逐项提交、逐项全测或审批。
- 始终保留授权、可信租户、会话撤销、租约所有权、可靠事件、双库及 Native AOT 约束。

## 执行顺序与状态

| 批次 | 问题 | 工作与验收场景 | 状态 |
| --- | --- | --- | --- |
| 1 | 01、06、07、13、22、25、32、47 | 扫描跨轮推进、消费组并行启动、独立心跳、Git 截止时间、图预算、认证后限流；覆盖失败前缀、取消和边界 | 已实现，受影响集中验收完成 |
| 2 | 02、12、17、41、57 | 有界目录读取、支付证书事务外获取、撤销提交后广播、公告数据库筛选分页、区域分块删除；覆盖隔离、回滚和参数预算 | 已实现，受影响集中验收完成 |
| 3 | 04、08、23、24、28、29、34、45、55、67、76、77、78、79 | 后台任务调度、清理及租约生命周期；覆盖并发、失租停止、有界历史及取消 | 已实现，受影响集中验收完成 |
| 4 | 09、19、21、26、33、37、40、48、51、52、56、61、62、65、68、71、72、73、75 | 有界集合、批量端口、SQL 投影与访问路径；双库和参数边界 | 已实现，受影响集中验收完成 |
| 5 | 10、11、15、18、30、35、36、38、39、43、44、46、50、53、54、63、66、69、70 | 网络、进程、文件和 CPU 预算及连接复用；取消、字节上限、异常资源释放 | 已实现，受影响集中验收完成 |
| 6 | 03、05、14、16、20、27、31、42、49、58、59、60、64、74、80 | 列表与明细分离、服务端分页、实时合并、工作流监听；契约与界面回归 | 已实现，受影响集中验收完成 |
| 7 | 全部 | 独立只读风险复核、集中编译及受影响测试、双库与 AOT 验收、前端验证、差异检查 | 本地受影响集中验收完成 |

## 实施与验证记录

- 已连续实施后台游标、独立控制面、租约维持、请求/响应/磁盘预算、连接复用、批量授权和目录、集合写入、目录摘要与分页等关联变更；已进入集中验证期，实现不能据此标记通过。
- 已补齐 PERF-02 候选分页衔接、PERF-03 字段权限筛选与自有隶属投影、PERF-60 管理页服务端分页、PERF-64 固定创建操作幂等凭据和轮询；正在集中编译、对齐契约与测试并处理验证发现。
- PERF-34 对账变为每请求最多 100 个租户的游标批次，Identity/Files 通过各自批量端口返回权威用量；调用方须根据 NextAfterTenantId 连续推进。
- PERF-48 引入每租户事件流 500 个订阅的原子容量保留，正文仅传一次集合入队；既有超过预算的配置明确拒绝入队，需在部署前校正，不能静默丢弃目标。预算表与订阅保留同一事务。
- PERF-51 配置批量更新/删除使用快照版本集合 CAS，任何项竞争或失败整体回滚；原始集合上限为 100。
- 实施时不据静态代码宣称效果；后续局部双库实测见性能清单，不能外推为容量达标。
- 回归测试随实现编写；定位确需最小复现时可提前运行，其余集中执行。
- 最终依据任务快照生成影响集，合并重叠检查并复用有效构建。未运行、失败或跳过必须明确记录。
- 容量结论保持 `Capacity-not-verified`；没有负载实测不声称吞吐或延迟提升比例。

- 集中验证影响计划：inner 预计 58 分钟，slice 预计 122 分钟；两份规划命令均成功。按功能风险覆盖双库及受影响集，先完成构建和契约对齐，复用构建执行回归。

- 集中验证阶段已取得：Release 构建 0 警告、0 错误；治理 85/85、性能治理 67/67、SQL 安全 5/5、命名 35/35。
- 首轮完整 Unit 实际发现 8978 项：成功 8886、失败 91、Linux 专用跳过 1，用时 5m11s。失败正在定位修复，不能标记验收通过。主要根因为新增事件名格式和批量读取/授权对应的旧夹具；保持撤销和隔离断言。
- 独立只读复核发现的候选页并发授权覆盖已修复：用户侧赋权与角色侧成员编辑统一角色版本及角色→用户锁顺序；加入双 Provider 回归场景。源隶属版本标记与 Outbox 同事务，读投影只用于管理筛选，不能授权。
- 资源预算：使用项目 run-build-with-resources 入口以 CPU=2、内存=3 GiB 执行后端构建/测试；前端默认并发过高的运行已中止，后续限制 worker 数。系统临时日志保留失败定位记录，不创建源码副本或根 artifacts。

- 后续集中证据：Unit 聚焦 370/370；迁移命名与 SQL 安全合并 37/37；契约演进检测用例 22/22。完整 Unit 重跑、前端用例及双库行为仍在执行，不能升级验收状态。
- 检查发现两个既有日志时间线契约覆盖缺口（API 模块数与 manifest 绑定）；该功能在任务开始前已存在，尚未修改其业务实现。
- 独立复核新增的 274 派生计数重建与 275 索引中断恢复已成对修正，并加入真实双库重放断言，待执行。

- 最新 Integration Release 构建通过（0 警告/0 错误），包含双库 273–275 的计数、索引与版本投影恢复断言，以及角色用户入口/候选页冲突与未见成员保护场景。尚未执行这些新行为用例。
- 完整 Unit 重跑：8982 项，8980 成功、1 失败、1 Linux 专用跳过；剩余失败为沙箱子进程启动标记等待，正在单 worker 独立复查，未修改生产超时或取消语义。
- 前端聚焦 31/31；修复 Vitest 默认 exclude 被覆盖而重复发现依赖工作区测试的问题，重新发现本项目 214 个测试文件（原发现 286）。共享包仍单独验收。

- 沙箱解析观测组在未改代码、单 worker 复查中 62/62 通过；完整运行的单项启动时序失败保留原始记录，不将平台跳过计为通过。
- Architecture 检出 Kafka Task.Run 违反同步边界；改为每消费组专用后台线程，总数最多 64，补独立启动、关闭结果与取消测试，待集中重建验证。
- 补充真实双库摘要分页断言：GoView/Printing 各 21 条含 32 KiB 正文的记录及版本，验证无重叠分页、摘要载荷上限和明细保真；仍由现有双 Provider 性能 API 场景执行。

- 内存释放后继续集中验收。Vue 完整用例 214 文件 / 986 项通过；共享客户端契约 66 文件 / 290 项通过。最终 Vite 构建与全部包预算通过：首屏静态 JS 1,494,856 bytes、gzip 390,387 bytes，未调整预算基线。
- Kafka 专用线程与组故障联动取消 4/4 通过；最新角色候选与双入口授权回归 8/8 通过。完整 Architecture 为 238/239，剩余全局 SQL 登记与静态声明已修正，定向复核正在运行，尚不能标记整个架构集通过。
- OpenAPI 守卫重用校验及错误码前缀以满足既有包预算；保留整数归一、失败关闭与原错误码。生成器断言改验真实读取器抛出的错误码，定向 12/12 通过；完整离线检查重新执行。
- 真实双库迁移、受影响模块、浏览器与 Linux Native 验证仍待完成；当前不升级 80 项验收状态。

- 全局 SQL 目录定向 2/2 通过，最新完整 OpenAPI 离线 213/213 通过，生成产物 `--check` 零漂移。
- 浏览器首轮 97 成功、1 失败、4 项 Layui 专用跳过；失败为用户流程旧组织引用模拟响应缺少总数字段，补齐后该完整流程定向 1/1 通过。没有改弱生产响应守卫。
- 独立只读复核发现 GoView/Printing 草稿与版本、Reporting 定义与版本缺少 Native Dapper 物化器；已补 7 种行的 ordinal 物化及静态注册，并补 Reporting 分组列表 JSON 元数据。新增同一原生双库 HTTP 生命周期中的非空摘要/版本/明细断言，以及候选部分保存后的数据库回读与旧版本 409；列序及双库类型已独立复核，实际 Native 验证待执行。
- 273–275 恢复回归已实际双库通过 2/2，0 跳过。首轮夹具顺序及同步释放 DbSession 的错误已修正，不降低计数、索引、单调版本或隔离断言。原始日志保留，接受的 TRX 另存系统临时目录。
- 模块影响集使用 `--phase merge --execution-group modules --reuse-build` 验证：保留受影响双库并追加 Smoke，Windows 不以 Native skip 冒充原生运行；Messaging-heavy 和 Linux Native 分开独占执行，不涉及 Git 合并。已确认 Integration 输入及产物一致并复用成功构建。
- 真实浏览器启动器固定 Redis 256 MiB / MySQL 1.5 GiB / SQL Server 3 GiB 的容器限额；SQL Server 内部缓冲预算 2048 MiB。相关测试工具 43/43 通过；只作用于新建测试容器，不改变既有共享资源。
- 新增两项迁移回归后，canonical 发现数更新为 1344、迁移分片为 562；实际分片与登记完全一致（250 legacy / 312 current），无遗漏/重复。工具链最新 164/164、治理 85/85 通过。
- 审计 B1 的真实 SQL→OTLP→Prometheus 正式告警触发/恢复 1/1 通过（12m13s），保留生产五分钟窗口。后续模块按 UID 合并为 513 项同场景双库用例，正在执行。
- 沙箱 Unit 偶发失败已进一步定位为夹具自身预算冲突：启动标记最多观察 8 秒，原 deadline 分支仅给沙箱 1 秒。统一为夹具既有 10 秒预算，仍断言实际超时、取消和子进程清理先于观测结束；生产配置与实现未改。待后续完整 Unit 集中复测，不能复用此前 62 项通过覆盖这一修改。
- 追加独立覆盖复核发现 PERF-50/77/64 的既有用例不足以证明新持久计划、真实续租 SQL 与匿名预览凭据链。已在现有双库 PerformanceApi 场景补真实 Organization 三批恢复、检查点重放、后续正文零读取及任务/源文件/能力绑定拒绝；ImportExport/Reporting 各用真实 30 秒租约、独立观察/争抢作用域、替换租约后取消与旧执行不得完成。DocumentShareSecurity 场景补并发创建幂等、访问计数一次、固定期限、凭据用途隔离和分享停用/文档软删除撤销。新增断言尚未构建运行；当前 513 项使用先前冻结程序集，结束后须定向运行这四个更新后的现有用例，不能将本轮运行视为已覆盖新增断言。
- 当前模块运行实际发现双库 Host_document_items 旧夹具仍把分页版本接口读成数组；已改为 PagedResult，并继续断言总数、剩余版本及版本标识。受影响的六个场景（PerformanceApi、DocumentShareSecurity、Host_document_items，各双库）将在本轮完成后集中重建、定向复测；不重复全量模块集。Linux Native 补充域按当前矩阵实际为核心 44 + 通知 2 + Settings/Jobs 4 + OIDC 16 + Kafka Replay 2，最低 68 项，以实际 UID 发现及完整 TRX 为准。
- 模块运行另确认角色管理与 API Key 撤权夹具复用了用户赋角色前的版本；新的统一角色 CAS 正确返回 409。已通过真实 GET 获取当前角色快照并断言成员编辑确实提升版本，再提交权限撤销，保留后续令牌拒绝断言；生产 CAS 未放宽。定向复测集合因此为上述三组加角色管理/API Key，共五组双库 10 项，尚待运行。
- 同类夹具复核还发现机构数据范围更新复用赋权前版本，租户邀请和订阅历史按旧数组反序列化；均已对齐当前角色快照/PagedResult 并保留隔离和目标记录断言。当前定向复测为八组双库 16 项。岗位计划回归明确以权限撤销方向检查旧计划拒绝；该组先于故意保留不可执行租户排队项的游标夹具运行，避免不同验收场景互相污染。尚未执行这些更新后的断言。
- 本轮模块运行定位到 PERF-34 的真实缺陷：分页查询以 Guid.Empty 作为缺失指标和首批游标参数，被全局 AssignedGuidTypeHandler 正确拒绝并导致 HTTP 500。已改为独立 nullable Id 投影、双库带类型 NULL、可空首批游标及对应静态 AOT 物化器；显式空 UUID 游标在查询前返回 400，不放宽全局标识约束。Unit 保留用量和 CAS 断言，并补首次读取参数/无效游标零额外查询；Native critical flow 补缺失指标、至少两页真实游标、应用后幂等及 400。六个原有配额双库场景加入定向集合，合计 22 项；新增修正尚待集中构建和运行。
- 消息验收准备补齐新建容器预算：普通与 Native 路径的 Kafka/Connect/Elasticsearch 均为 2 GiB，Collector 为 512 MiB，CPU 保持既有有界档；独享标签仍只由相应模式添加，既有共享数据库配置不变。相关差异已独立只读复核，尚待实际运行。80 项 messaging-heavy 按 regular/CDC/logging 依赖拆为同批顺序进程，逐组与 canonical UID 集精确比对并核对完整 XML；只沿用矩阵既有 74 个实际通过下限，原始 Inconclusive 单列，不充当通过。每组执行前单独检查 Docker guest 余量。
- 当前模块运行另有 MCP 冷调用双库用例返回 cancelled（统一执行器固定 10 秒预算）；管理 API 先发现再批准的调用场景尚未见同类失败。未据此放宽生产期限，先补断言错误码与执行耗时诊断；当前模块及 22 项复测结束后，用同场景双库最小复查区分冷初始化/资源时序和实现问题。用户选择自行释放 Docker 内存，未授权重启共享容器，后续只重新探测资源门禁。
- 独立复核另确认 PERF-39 的 POST SSE 完整缓冲会扣住反向 JSON-RPC 请求，服务端等待客户端回复时互相等待；该生产缺陷不在替换 PrimaryHandler 的 Interop 失败链中。已改为 SSE 按读取累计 4 MiB 与 30 秒总期限，普通 JSON 仍有界缓冲。补真实 HttpListener/生产 HTTP 策略/SDK 的反向 ping→客户端另 POST pong→原响应完成场景，并覆盖未知或伪造长度、超限后重复读取、deadline、caller 取消与流释放。未延长生产期限；新增五个 Unit 场景尚待执行。为避免边编辑边编译，已仅停止两个仍排队的任务自有 Node 入口（23860、107428），未停止当前模块或任何共享资源；代码就绪后重新排队。
- 模块影响集本轮实际结束：513 项，491 成功、20 失败、2 NotExecuted（缺少阿里云短信/SMTP 外部配置），3h14m03s，进程退出非零。完整原始 TRX 已另存系统临时目录 fullnet-performance-20261020-modules-513.trx。18 项已修正待复测，2 项 MCP 取消待独立复现；不能将该命令记为通过。SSE 预算/双向 RPC 修正与五项新 Unit 已独立只读复核，待实际验证。宿主可用内存回升至约13.2GiB，Docker guest仍约5.7GiB；没有重启共享容器。
- 冷调用最小复现最新实际为双库 2 项：MySQL 成功、SQL Server 失败（ai.tool.cancelled，约 10.46 秒）；首次出站 POST 在执行预算取消后才以 499 结束，不能据此断言数据库慢或 SSE 缓冲导致。保留生产 10 秒预算，追加仅记录协议方法/状态/耗时的测试诊断，待与真实管理 API 调用对照。
- 22 项定向复测新增匿名预览链在双库创建返回 500。沿写入链确认匿名 RequestedByUserId=Guid.Empty 与已分配 UUID 约束冲突；已改为显式 nullable 请求人、双库 276 可重复迁移和 AOT nullable 读取。匿名派生 PDF 归属当前文档所有者，任务仍保留匿名发起事实；生成客户端将在真实 OpenAPI 再导出后统一生成。现有双库恢复场景补列恢复重放，文档链断言请求人为 null，客户端补混合列表/非法请求人两项守卫用例。只读复核未发现归属/幂等/迁移具体缺陷，指出的生成客户端遗漏已列为待执行项；上述新修正尚未构建或运行。
- 22 项定向复测实际为 16 成功、6 失败、0 跳过（8m56s）：ImportExport 持久计划及 ImportExport/Reporting 真实续租、租约替换后的取消/终态隔离均已覆盖通过；剩余为文档匿名预览 2 项和配额物化 4 项。真实 Dapper 六项最小复现 3 成功、3 失败，精确暴露 COUNT(Int32)、SUM(Decimal) 与 UNION Version(Int64) 和位置构造器不匹配；已为三个内部投影补可按属性转换的无参构造，保留原逻辑类型与 Native 显式 reader，待双库/Unit 验证。首轮复现构建另检出已有新断言的 nullable 警告作为错误，已加实际 null 防护；不把构建失败当成行为红灯。
- 新增文档领取→真实 Files 上传→匿名凭据读取 PDF 的双库断言，仅替换外部转换器，核对匿名请求人为空且派生文件继承文档归属；Native 现有文档生命周期追加 Office 上传、匿名创建重试及 NULL 请求人读回。当前 14 项仍使用此前冻结程序集，不覆盖这两份新断言，后续须定向重建复测文档组。
- MCP 机制复核确认夹具没有先发 HTTP，第一次 MCP 请求触发 ASP.NET 路由/端点装配；SDK 默认还有独立 5 秒 discover 探测，不能直接把首 POST 的取消等同于外层 10 秒取消。已为后续最小复查补真实 /health/ready 前置与耗时记录，保留生产和 SDK 预算、审批、协议策略、执行时权威授权与 schema 检查；当前仍待带协议日志的对照和 readiness 后双库实跑。
- 后续 14 项双库复测全部通过（4m03s，0 跳过，完整 XML/UID 一致）：配额对账 6、273～276 恢复 2、匿名创建/凭据链 2、MCP Interop/管理 API 各 2。该轮带协议诊断的 server/discover 为 2.37～2.57 秒，tools/list/call 为约 48～98ms；不能把这一混合进程结果当成冷启动失败已修复的证据。
- 已移除临时协议诊断，真实 HTTP readiness 前置后的独立 MCP 双库进程实际通过 2/2（2m19s，0 跳过，完整 XML/UID 一致），没有预连 MCP 或延长生产/SDK 预算。新增匿名 PDF 执行链与既有登录用户预览契约共 4 项正在执行；OpenAPI 双库再导出和客户端生成待其通过后执行。
- 最新 SQL/命名/UUID 门禁合并 40/40，治理 85/85 通过。最小映射回归的修正等待完整 Unit 批次覆盖，不重复运行已通过的 14 项。
- 最新匿名预览实际执行链与既有登录用户契约双库 4/4 通过（2m27s），独立 MCP readiness 双库 2/2 通过。随后真实 SQL Server/MySQL OpenAPI 各 1/1 导出成功，客户端统一生成并以 --check 验证零漂移。最新共享契约 292/292、OpenAPI 213/213、Vue typecheck/Vite 构建及全部包预算通过；静态 JS 1,494,886 bytes、gzip 390,379 bytes，预算基线未改变。
- 模块证据按原始 513 个 UID 与后续定向结果严格合并：511 Passed、2 NotExecuted（外部短信/SMTP 未配置），原始 491/20/2 和非零命令退出记录仍保留，不把证据合并改写为原命令通过。
- 最新完整 Unit 原始结果为 8997 项、8996 Passed、1 Linux 专用 NotExecuted，新增 SSE 五项和配额物化六项全部 Passed；但入口误将最低实际执行数设为8997，MTP 以退出9/Failed summary 拒绝该运行。已保留原始证据，修正入口为8996实际执行，同时严格核对全部8997发现UID、仅该Linux用例可未执行、XML summary须Completed/Passed，重跑正在排队，不能提前记为通过。
- 临时消息验收入口保留全部80个canonical UID，regular/CDC/logging分组逐项去重；只接受既有六个SQL Server CDC债务UID且核对本结果行的明确Agent-gap原因（regular 2、CDC 4、logging 0），全部MySQL及其余用例必须Passed。Scope_C含糊前置条件跳过还须同批另五个Agent-gap结果且未配置外部SQLServer CDC实例，不接受Connect超时、捕获超时或新跳过。新建Elasticsearch普通启动路径也补齐既定2GiB硬限额，待实际编译和消息执行。
- 2026-10-21补跑测试工具链153/153通过（node --test --test-concurrency=2 tests/testing/*.test.mjs）。任务已结束513运行创建的数据库按快照、精确创建时间、命名和无活动连接筛选清理；不触碰基线数据库或重启共享容器。Linux Native/消息/真实浏览器仍待Docker guest内存门槛及实际运行，不升级验收状态。
- 完整 Unit 修正执行入口后实际通过：8997发现UID，8996 Passed、0 Failed、仅1个明确Linux专用NotExecuted，5m57s，退出0；完整XML/UID、程序集SHA与Completed/Passed summary均验证。关联Architecture实际74/74通过（10s），另以精确发现集合验证XML/UID，无跳过。API/Worker分析器尚在执行。
- 已结束模块运行的任务数据库清理实际完成：SQL Server249/MySQL247，均dropped且0 skipped，随后查询清单剩余均0。用户明确授权仅重启fullnet-it-mssql后，再次核对精确容器ID、无Integration进程/业务连接/活动用户请求，仅有内部CEIP空闲连接；完成该容器重启并SELECT 1就绪成功，数据与配置保留。Docker guest可用回升至约12.6GiB，重型验证可按门槛继续，未操作其他共享容器。
- API AOT分析构建0警告/0错误（3m43s），默认JIT还原成功；Worker AOT分析0警告/0错误（4m10s），默认JIT强制Rebuild亦0警告/0错误（2m27s）。整个fast-remainder入口退出0。随后Integration Release再构建0警告/0错误（1m27s），消息regular组41项于00:21:42开始，尚未取得结果。
- SQL Server重启后缓存随启动/真实读取回升，原regular/CDC/logging入口仍按原预算执行。独立只读复核确认可采用新的日志逐class进程入口：每class最多TLS Kafka2GiB+ES2GiB+Collector0.5GiB，或静态Kafka2GiB+局部broker2GiB且无ES，故8.5GiB含4GiB余量；CDC仅同一Kafka2GiB+Connect2GiB，8GiB含余量，regular维持8GiB。未改变任何容器硬限额或四GiB保留。旧入口已开始构建，停止保护检测到子进程后拒绝操作，未终止/热改。新v2仅在旧入口后续因资源门禁退出、regular真实通过时使用，先后核对源码fingerprint及338个DLL/JSON哈希，重验旧41项完整XML/UID后只跑余项，完整80项及既有六项CDC原因门禁不变；尚未启动v2。
- Messaging regular实际41项：39 Passed、2既有SQLServer Agent-gap NotExecuted、0 Failed，18m13s，完整XML/UID和原因门禁通过。旧入口在后续CDC的10.5GiB资源门禁退出1，未启动该组；原始命令保留非零事实。无活动测试/业务连接后，沿用户已授权的同一SQLServer容器重启范围完成资源释放与就绪；v2先核对冻结源及338个DLL/JSON，再重新验证并复用这41项，CDC16已开始，不重跑regular。
- Windows System日志确认00:43～05:00间多次Modern Standby（Kernel-Power 506/507），CDC初始化被暂停；05:00恢复后才创建实际Kafka/Connect。已给当前任务Node进程绑定临时SYSTEM_REQUIRED防自动休眠请求，所有者退出自动清除，不改系统电源方案/屏幕配置，也不阻止用户主动休眠；接口依据 https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate 。该墙钟间隔不作为性能证据。恢复后Scope_C MySQL实际失败connect_position_not_ready，后续MySQL CDC也有失败；Connect日志显示初始化在遍历大量历史数据库schema，正在独立只读定位，未延长期限或新增跳过。
- Native范围补查确认S3适配器/AWSSDK本批未改，PERF11为独立HttpOssBlobClient、PERF70为支付/通知HTTP预算；但现有68项遗漏共享HostFiles列表新时间窗/LIKE筛选路径。将在消息冻结批次退出后，扩展现有双库critical flow中的NativeAnonymousPreviewAssertions，复用已上传native.docx验证默认/显式/排除窗口、四种LIKE字面字符、400与无身份401；不新增容器/TestMethod/UID，Linux发布与Integration构建前统一纳入。
- 已定位并停止唯一自有失败CDC测试进程，保留其失败输出/Connect原始日志；该轮中断无完整TRX，不能计通过。Ryuk自动清理本轮自有Kafka/Connect/网络，未操作三个共享数据库容器。根因为MySQL模板默认采集非捕获库/表schema；源码与官方3.4文档、先DELETE再InterruptedException的日志顺序均已独立核对，休眠并不能解释恢复后的位点门禁失败。
- 两份MySQL Shadow/Capacity模板均显式限制captured database/table的DDL history，保持no_data、捕获Outbox、120s位点期限及完整投递断言不变；同步运行说明。部署契约新增两项先实际失败（缺少配置），修改后完整8/8通过。Native共享文件列表的现有双库流程增加默认/显式/排除时间窗、四种LIKE字面输入、非法窗400和无Bearer401，继续使用每请求Bearer保持后续分享真实匿名；不新增Native TestMethod/UID。
- 修改前7295个源文件逐文件SHA与原冻结aggregate吻合；修改后精确5个源文件变化（2模板、部署契约测试、2个Native helper），其余代码/依赖未变。独立复核确认regular9类及helpers不读取这两模板，故保留原39Pass+2Agent-gap证据；未将旧338输出manifest改成新DLL。统一Integration Release重建0警告/0错误（1m12s），第一次Shell将-m:2拆分导致命令语法失败的日志另存；引用参数后成功。
- 新v3输入另存当前338个Integration DLL/JSON和真实外部Benchmark目录292个DLL/JSON，源及两目录哈希在前后核对；旧regular41再次按当前UID/原因/summary验证，CDC16和日志逐class23实际运行。当前live Connect已确认两项限制均true、connector/task RUNNING且有有效MySQL binlog file/pos；不能提前据此认定整组通过。实际Kafka/Connect资源回读均2GiB memory=swap、1核，四GiB余量/期限未降低。新增修正已独立只读复核，Native Linux实测仍待执行。
- 后续独立只读覆盖复核发现两个前端边界尚缺定向验证：匿名Office预览的凭据用途/固定截止/429/路由取消，及角色已加载候选并集、跨页选择与成员快照版本提交。测试草稿暂存系统临时目录，消息冻结结束后纳入，不重复已通过的后端全集。沿旧轮询返回成功的路径进一步确认取消后仍可能进入useBlobPreview.load并先清掉新页面URL；待最小回归实际复现后修正异步返回的所有权检查，不能提前计为修复。
- CDC新模板实际整组16项完成：12 Passed、4个严格既有SQLServer Agent-gap NotExecuted、0 Failed，28m53s，完整XML/UID通过；全部MySQL用例实际Passed。入口随后在日志组8.5GiB资源门禁退出1，未启动日志组；保留原始非零记录。退出后另行核对v3冻结源及338+292输出哈希未变，保存完成CDC后的输入校验记录。
- 匿名预览最小回归实际RED：路由切换abort后旧成功轮询仍调用旧内容接口；修正task创建与poll返回后的当前代际/code/取消检查，阻止旧结果清理或替换新URL。新增两组前端边界及邻近用例最终6文件26/26通过（含新URL不得revoke断言），先前角色夹具因懒加载操作/错误teleport stub导致的失败保留，未改生产成员逻辑。类型检查、Vite及全部预算再次通过，静态JS1,494,886 bytes/gzip390,378，预算基线未变；Compatibility正式入口实际12/12、0跳过，Release构建0警告/0错误。
- 续跑v4另存新输入：只新增两份Vue测试与修正一个Vue组件。将唯一Vue组件按精确差异反向还原并匹配原逐文件SHA、剔除两新增测试后，完整aggregate与v3冻结hash吻合；两个输出目录338+292个DLL/JSON亦全部未变。独立只读复核确认regular41与CDC16可按当前精确UID/原因重新验原TRX，v4仅实际运行剩余日志23，并保留全80 UID、六项明确Agent-gap和当前源/二进制前后哈希门禁；没有改写旧manifest或将资源门禁失败改为成功。
- v4日志前五组17项实际全部Passed、0跳过；请求延迟组三项为2 Passed/1 Failed，失败在in-process八条一批的ES写入/同步偏移提交收据链，TaskCanceledException。时间线84.9s包含约37s ES就绪+7s Kafka+10s请求档+30s收据期；Runner与测试循环均有30s收据预算，fixture单HTTP10s、sink单操作30s、父期限5分钟，正在最小诊断取消来源，尚未调整期限/批量。Standalone同样配置batch8但与发送并行且收据120s，其通过不等于当前串行预算足够；Host生产默认batch1、有界实验上限64，不能称默认64。RouteComparison/Secret独立场景继续执行，保留失败TRX，当前不升级整组状态。
- 此次Latency测试仍向root artifacts/logging-request-{process,es,kafka}写入；三个目录CreationTime分别精确吻合22:04:32/22:06:27/22:07:52 UTC三个用例start，可证明本任务归属。消息冻结结束后将该测试改用统一TEMP，并只对这三个已结束目录作有界清理，保留失败原始记录及必要报告，不清理其他既有artifacts。
- v4全部结束：73 Passed、1 Failed、6精确既有NotExecuted；RouteComparison正反序四场景1/1（7m57s）和Secret边界2/2（2m23s）均通过。出口保留失败1，另行核对当前源码及338+292个DLL/JSON仍与v4冻结输入完全一致，保存after-logging校验记录；不能将原失败命令报告通过。之后仅Latency测试增加有界计数/计时/取消来源诊断与TEMP输出，不改8条批量、HTTP/收据/总期限。Integration Release重建0警告/0错误（50.60s），单项真实栈诊断已开始，尚无结论。
- 单项诊断实际再次失败并确认取消源：expected=5206、consumed=3512、committed=3504、438个已完成八条批次，receipt elapsed=29,952.69ms，当前flush=10.56ms，receipt_cancelled=True/parent_cancelled=False。因此为整批30s收据预算，单HTTP10s与父5min均未触发。随后串行功能对账采用协调器既有64条硬上限减少ES/同步Commit往返，保留5000请求/目标500每秒、30s收据/循环、HTTP/sink/父期限、所有文档/ID/投影和最终offset/DLQ断言，另补总提交数==Broker高位点总数；独立消费者仍为原8条并行配置/原120s。README与结果proof明确64范围，不把新配置证据外推到8条串行或生产默认1条。只读复核未发现语义弱化。
- 64配置Integration重建0警告/0错误（1m07s）；延迟整类实际3/3通过、0跳过（3m31s），完整XML/UID与前后源/二进制哈希核对通过，三份原始result.json已在入口回收TEMP前保存。串行实际5206条全部消费/确认/提交，82批、6,105.37ms，原30s预算未变。
- 消息证据严格合并完成：当前全80个canonical UID重新发现，与旧集合完全一致；除Latency测试一个源文件外全部源文件未变，338+292输出仅IntegrationTests.dll变化。重新验证regular41、CDC16和各日志组的完整XML/UID/summary/本行原因，Latency组采用最新3/3。合并为74 Passed、6个明确Agent-gap NotExecuted、0 Failed，原v4与单项诊断的非零事实和原始TRX均保留。独立结果见TEMP/backlog-messaging-accepted-union.json，不宣称原失败命令变为通过。真实浏览器双Provider流程已开始，Native仍待实际执行。
- 真实浏览器完成：browser-backlog.mjs入口退出0，SQLServer16/16（7.6m含首次完整独立构建）、MySQL16/16（3.5m）通过。verify-browser-results.mjs逐case核对project/expectedStatus/result仅一次Passed、两Provider精确同一16个case、stats失败/跳过/flaky全0，共32；前后含项目user覆盖的源码fingerprint一致（398549bcc5e5305eb616ff6b95b50cb19dd753d6982b1058d567f26b6ee9d07c）。本轮自有浏览器栈自动清理，三个共享数据库容器保留。Linux API/Worker真实Native发布已开始，尚不称通过。
- Linux Native 首次 4 GiB 发布在 ILCompiler NativeAotNameMangler/ObjectWriter 阶段实际抛出托管 OutOfMemoryException，容器 exit1/OOMKilled=false；原始日志、资源状态及失败入口保留，不计通过。按实测峰值将本任务编译容器调整为 6 GiB memory=swap、CPU2，预检同步为 10 GiB（含既定4 GiB余量），未调整编译、裁剪或警告门禁。沿既有明确授权，仅在无测试及业务连接时重启精确同一SQLServer容器并确认SELECT 1、420个数据库全部ONLINE，数据配置保留；06:58开始6 GiB真实发布，尚无通过结论。
- 6 GiB 发布实际再次 exit1/OOMKilled=false，失败仍为目标文件写入时的 String.Concat/EETypeNode 内存不足，无新源码/AOT告警。资源回读6 GiB memory=swap且cgroup无OOM事件，观测到进程RSS约4.17 GiB、容器峰值约5 GiB；官方说明容器GC默认堆预算为限额75%（https://learn.microsoft.com/dotnet/core/runtime-config/garbage-collector#heap-hard-limit-percent），与现象相符，但不把未采集到的堆值当实测。保留4/6 GiB失败原始记录，仅调整编译容器为8 GiB（默认GC预算6 GiB）、预检12 GiB含4 GiB余量，其他编译参数不变。按原授权在无活动测试/业务连接时仅重启同一SQLServer容器，就绪与资源门禁通过；07:13开始真实8 GiB发布，仍未验收。
- 清理只读复核进一步确认：较早249/247模块库已全部清理；剩余新旧基线差异不能单独证明本任务归属。仅原TRX直接关联的后续双库各一个可用于精确清理，不明库保留。Native新增库将结合既有基线及本任务容器中实际原生进程的数据库名记录确认，不记录连接凭据。
- 默认GC的8 GiB轮次也实际失败（13m42s，exit1/OOMKilled=false）：已从名称拼接推进至StringTableBuilder.FlushReservedStrings→MemoryStream.set_Capacity，生成.o变为0字节。失败日志/容器资源/状态均另存，尚不能认定ELF或单数组2 GiB上限。依据NuGet真实repository commit 4099f83977f4d29604a9aeb971fb0a7bc1d5f699逐行核对官方StringTableBuilder/ElfObjectWriter及MemoryStream扩容：新旧缓冲在复制前同时存活，而当前栈为分配失败，单纯容器尚有余量不等于GC堆足够。独立只读复核同意最小单变量实验，禁止关闭-g/业务功能或削弱验收。
- 固定8 GiB memory=swap/CPU2及外部4 GiB余量，仅通过现有targets的_IlcEnvironmentVariables向ILC Exec设置DOTNET_GCHeapHardLimitPercent=55（十六进制85%）；不修改Host运行配置。按授权仅重启同一SQLServer并确认就绪，删除本任务失败的精确0字节.o后于07:31开始真实发布。07:32:58已读取实际ILC进程GC白名单，仅该55设置，当前API rsp SHA 82930b6b13e51e94769dcf790fc2d1a730479c38f1aab898d29b9cd6eea44df2与默认GC失败轮次完全一致。小型环形资源记录持续保留，结果尚未取得。
- 同rsp/8 GiB/ILC85%实验实际仍失败（15m07s，exit1/OOMKilled=false），仍为StringTableBuilder→MemoryStream容量分配；cgroup最后观测peak8,153,509,888 bytes且max/oom均0。小型采样与实际GC白名单另存failed-8g-gc85记录；不能认定已修复、ELF2 GiB硬限或85%配置足够。停止单纯调大内存/比例，进入有界名称诊断，不关闭-g/功能或减少Native测试。
- 有界诊断diagnose-ilc-map.mjs直接使用同一rsp及--map FIFO，只统计XML映射名称数量/总编码字节/最长长度、最多20个160字符前缀及最多150类；Node堆256 MiB且不保存完整map。已用跨chunk夹具验证计数与String前缀隐藏。独立只读复核后补NaN拒绝、FIFO reader非零协调失败、35分钟实际期限、容器40分钟上限及精确双标签ID的停止/排空/清理；诊断结果不作Native发布验收。
- 首次诊断因guest13 GiB检查退出，未创建容器。核对实际执行位置后拆分预算：host为Linux编译容器8+Windows Node预留1+固定余量4=13 GiB，guest只计容器8+余量4=12 GiB，未减少任何余量；buildBudget9 GiB/CPU3、容器8 GiB/CPU2不变。按既有授权核对无测试/业务请求后仅重启同一SQLServer，07:54开始有界诊断；Native发布/68项运行仍未通过。
- 有界map诊断实际结束（17m54s，exit1/OOMKilled=false，同StringTableBuilder容量分配栈），完整map未落盘；仅留下1,790,894个Name属性、389,633,463 XML编码bytes、最长2,784 bytes及top20/类别小记录。GCInfo重复MethodCode名称且XML转义膨胀，所以不是.strtab实测；最长为GCStaticEEType位图，其后Polly嵌套Func/ValueTuple，未找到单个巨大名称或本批新增递归泛型的证据。独立只读复核确认Native roots、Polly标准HTTP韧性入口未在本任务变动，不据OOM删除功能/root。
- 找到本任务开始前的有效资源对照：同cache的performance-followup-publish.log及旧native-build日志实际API/Worker/Linux测试构建成功，API139,760,352B/Worker88,794,344B、9/7批准警告，同SDK10.0.402/runtime10.0.13/image；旧controller为10 GiB/CPU4且未显式限制swap，不能当本批源码验收或证明10 GiB无swap足够，但说明当前8 GiB无swap档更紧。结合本轮峰值/名称统计，下一受控档固定9.5 GiB memory=swap、CPU2，仅ILC GC85%（8.075 GiB堆/1.425 GiB非GC）；host与guest即时准入均为13.5 GiB，外部4 GiB余量未减。
- 持同一root/buildLease回调内直接调用原授权的restart-authorized-sql.ps1（固定完整ID、无测试/业务连接、SELECT1），未嵌套withTestRun；重启后立即测guest，避免跨入口启动间隙消耗可用缓存。08:16的9.5 GiB实际容器已创建并通过资源回读，API/Worker发布及Linux测试构建正在执行，未取得最终结果。
- 9.5 GiB/ILC85%实际越过原失败点：API对象文件成功生成1,006,353,679 bytes（08:34:43），随后ld.bfd真实链接；cgroup观测peak9,334,648,832 bytes（约8.69 GiB），max/oom为0，说明原8 GiB档不足以承载本次实际编译峰值。08:37日志明确NATIVE_PUBLISH_PASSED Api及/out/publish；仅该角色publish阶段成功，Worker/Linux测试构建、最终双角色警告/ELF/输入门禁和68项原生运行仍待完成，不提前升级Aot-published或全部80状态。
- Linux Native 双角色与测试程序集构建最终实际通过（入口 exit0）：API ELF 141,148,608 bytes、Worker 89,727,112 bytes，分别仅9/7项既有批准告警；Linux测试构建0警告/0错误。实际9.5 GiB memory=swap/CPU2、ILC GC85%，编译峰值9,334,648,832 bytes，OOM事件0；源码fingerprint仍为398549bcc5e5305eb616ff6b95b50cb19dd753d6982b1058d567f26b6ee9d07c，API编译rsp与先前失败轮次完全相同。构建自有容器已退出并删除，失败轮次原始证据保留。随后启动同一68 UID的真实Native运行，核心66/Replay2分进程顺序执行；当前只确认发布成功，运行验收尚未完成。
- 运行期清理只读复核发现原归属观察器有2400轮提前退出上限，已在本任务临时目录新增独立coverage观察器：以已采集记录为seed，继续采集至实际Native owner退出，250min总期限到期明确失败；最终prepare/verify均要求coverage的started/ownerEnded完成标记，并在执行清理前核该观察器实际exit0。当前运行未中断，双观察器使用不同输出；不扩大任何可删除数据库集合。只读复核确认这处门禁修正及原精确容器/基线/创建时间/无连接保护有效。
- Native核心66运行期间实际发现MySQL critical_http_flow失败：成员部分保存后不带userIds的GET总数预期1实为0（NativeApiE2EAssertions.cs419）。候选页与PUT saved.UserIds此前断言已通过；对精确自有失败库只读查询确认native-members角色实际保留1条关联，proof保存TEMP/native-role-persisted-readonly-proof.txt。根因范围缩到Endpoint可选Guid[]缺省绑定为空数组、服务将显式空候选早返0；拟按Query.ContainsKey区分未提供参数，不修改写入/CAS/撤销。当前不热改冻结源码，核心其余场景继续收集完整TRX。另保存7442文件SHA（无源码复制），与发布fingerprint完全吻合，供后续精确差异/影响集复测使用；当前尚未验收。
- 独立只读沿真实dotnet/dotnet commit 4099f83977f4d29604a9aeb971fb0a7bc1d5f699的EndpointParameterEmitter确认RDG数组缺省确实ToArray/new Guid[0]，null声明不能恢复缺省语义。另确认客户端host-role-members.ts以零UUID编码显式空候选；仅恢复null还不足以处理此编码。拟Endpoint仅按Query.ContainsKey恢复缺省null，Service保留原始Count>100拒绝后再移除零标记，沿原空集合早返，避免101个空标记绕过预算；不修改全局AssignedGuid约束。回归将覆盖未提供筛选、有效/空/混合候选、非法非空GUID、101空标记400、部分保存未见成员及旧版本409，Native双critical+JIT双库实际Endpoint+Unit定向批次；当前核心仍运行，源码尚未变动。
- SQL Server critical原生流程随后在完全相同cs419断言实际失败（47.842s，预期1实为0），与MySQL同一缺省数组根因。两条完整原生流程都必须在修正后重跑，不能以已通过的前半段或JIT结果替代；其余核心场景继续执行。
- 原生核心66最终实际完成：64 Passed、2 Failed、0 NotExecuted，1h19m21s；容器退出2、外层入口退出1，OOMKilled=false。完整XML/UID与Failed summary/counters已严格核对，两个失败均为双库critical的同一cs419断言；Replay2未启动。核心容器已移除；补充coverage观察器实际exit0并写ownerEnded=true，共捕获SQL32/MySQL34个隔离库名，原始记录均保留。
- 结束后重新核对7442文件aggregate仍完全等于原发布398549...，归档旧双角色manifest/日志、资源/状态与coverage（不复制源码/二进制）。随后按已复核限定修正5文件6处：Endpoint区分缺省键，Service原始100项预算后解码空UUID，Unit及JIT/Native实际HTTP边界断言。精确逆向差异逐文件与原aggregate均吻合，其余源不变；新fingerprint为9ca06d088059d9432f059797e9831864d31d74be37403fc08ddb07a0f3525fcd。
- 定向Windows验证已实际取得角色成员Service7/7通过、0跳过（2.314s，完整XML/UID）；包含新空标记/原始101项预算与零SQL断言。双库真实HTTP新增生命周期仍在执行，未提前计通过。最新完整Unit仍为先前8997发现/8996 Passed/1 Linux专用NotExecuted；本轮新增Unit方法只由这次定向7项覆盖，不称完整8998重跑通过。
- 新角色查询实际HTTP双库2/2通过（2m35.956s，0跳过/失败，完整XML/UID）；Unit7/7、HTTP2/2的Windows集中入口整体exit0，两个Release构建均0警告/0错误。最终source fingerprint与输入一致（9ca06d...）；新两库只记录经验证库名，已从各自Passed TRX行精确核出SQL fullnet_it_a717124d9c12491f831c282a032125db / MySQL fullnet_it_2d13fa94deb045a0b5461d4b4e063d04，不输出连接凭据。
- Native续跑入口已独立只读复核：要求新成功publish与当前5文件/6精确逆向差异、原7442源aggregate/旧manifest相符，原66完整Failed结果严格核对后仅复用64 Passed；新critical2与未跑Replay2补齐同一canonical68 UID并集，原非零命令保持失败。新观察器以原66归属为seed且持续到新owner实际退出，预期70个Native隔离库；wrapper强制observer/power实际exit0、PID及ownerEnded完成。
- 最终清理v2已独立只读复核：精确containerId区分归档原core（只接受已核64/2/0的exit2）与新core/replay（exit0），保留stopped/Pid0/noOOM、基线/创建身份/无活动连接与固定目录保护；新增JIT两库的直接TRX证明，最终逐名查询零剩余并拒绝skipped，未知库保留。当前未执行任何最终删除。
- 修正后Native发布已排队，但宿主可用内存约8.8 GiB，低于本批已实测9.5 GiB编译+固定4 GiB余量；官方入口尚未取得构建名额，未创建编译容器或重启共享SQL。已请求用户释放宿主至至少14 GiB；不下调门槛，不操作既有Windows SQL/Oracle等共享进程。Native运行与80项最终验收仍未通过。
- 用户释放宿主内存后，实际可用22.6 GiB，官方构建入口自动通过9.5+4 GiB门禁；10:31:54沿原明确授权检查无活动测试/业务连接后仅重启同一完整ID的fullnet-it-mssql，SELECT1就绪通过。新9.5 GiB memory=swap/CPU2容器6145baff...于10:32:27启动，SDK image与原发布完全一致；不降低预算/告警门禁。临时SYSTEM_REQUIRED绑定实际build owner，ILC GC白名单/小型资源环形采样另行记录；实际新发布仍未完成。

## 最终收尾（2026-10-21）

- 80项（P1 13项）实施与本地受影响集中验收完成；上方各阶段失败和等待为历史记录，原始结果未覆盖。最终事实以[性能修复记录](../../verification/performance-review-backlog-2026-10-20.md)为准。
- 成员查询缺省/空候选修正：Service 7/7、真实JIT双库HTTP 2/2；重新Native发布与Linux测试构建退出0后，critical双库2/2、Kafka Replay双库2/2均通过，零跳过/失败。原核心66项仍为64/2/0，按精确UID仅复用64个Passed并合并新4个Passed，完整覆盖68项，不宣称原失败命令已重跑通过。
- Native最终输入SHA256为`9ca06d088059d9432f059797e9831864d31d74be37403fc08ddb07a0f3525fcd`；API/Worker既有批准告警9/7，ELF及源哈希核对通过，Linux测试构建0警告/0错误。防休眠和数据库观察器均实际退出0。
- 收尾清理入口退出0；直接证明的36个SQL Server、38个MySQL临时库全部删除、零跳过、独立查询零剩余。回收规定的生成目录，保留最小原始证据。
- 用户授权提交全部工作区代码；在所有验证与清理退出后统一暂存/提交，保持原发布HEAD和源字节证据，提交不触发重复全测；不推送。环境依赖NotExecuted和`Capacity-not-verified`继续保留。

- 提交前`git diff --cached --check`发现新增braces/stringify文件的末尾空行，仅移除一个CRLF并通过静态语法检查。验收后无功能变化；提交证明单独记录该格式差异和固定7442路径，保留原Native输入9ca06d…及发布HEAD。
