# 全产品 Native 本机续验（2026-10-30）

## 当前结论

冻结后端矩阵为初始 Native 124 项加独立 Windows 控制器的 Workflow 服务 9 项，共133个唯一UID；当前 **133/133通过、0项尚未通过**，此前已通过项没有未收口的较新回归；校对SQL并发死锁及FAQ检索夹具领取问题均已修复并通过原双库复验；FAQ传输同预算双库正式复验通过，原阶段取消仍保留且根因未唯一定位。桌面手动1项另列，用户已明确当前不方便操作，先完成后端。

这是本机指定 Native 剩余范围，未执行仓库全量测试。并行新增 K05 Operations 等能力不自动纳入这份冻结矩阵。历史成功、失败、未执行和队列等待分别记录；较新失败覆盖较早成功，复验成功后才消除回归。

基线为 main / 75be6fc0e17433f9c59c42553093dacd479ef9d1；任务快照 full-product-native-local-20261030。其他模块WIP与两份Messaging stash保留，没有提交、推送或生产切流。源码未复制整仓。原始材料在 %TEMP%/fullnet-product-native-local-20261030，下文路径均相对该目录。

## 校对并发死锁修复与原生复验（23:38）

校对并发SQL原UID19a9c630-b3ca-81fa-94f8-9dbb2f316f69在extended-2a1a8afe实际0/1、零跳过（54.173s），4轮并发编辑原断言200+409遇到200+500。隔离JIT首轮通过，原Native重现后仅在本任务独占SQL容器启用trace1222，捕获correction-race-1793372418125原始死锁图：事务外HeadTenant SELECT持document S等head S，保存事务持head X等document X，读取成为victim。诊断运行未计入133，通过的一次重现也未代替修复。

修复仅AiKnowledgeCorrectionStore.ReadCurrentAsync的NoTransaction只读快照：明确Deadlock返回null，由编辑入口重新授权并核CAS版本；其他异常、取消、保存、幂等回放及提交后复核不被吞掉。新单测11/11、零跳过（286ms），构建0告警/错误（53.13s），实际DLL/PDB与源码已独立核对。该方法只有Edit与Workspace API调用点，故只重发API，Worker098f保持。

新API ELF为8ffd4e1b148e6a16e8467032ffd4699346289f5bfd7a2914cf2c533577c7bec3（171752608字节），四门禁及sourceUnchanged=true；3576源码项无变化、1527托管输入与真实ILC/发布清单独立复核，9条告警与前轮已批准告警相同，托管阶段0告警/错误（3m38.76s）。原纠错SQL extended-ee5b39e7（54.360s）和MySQL extended-093ec0fd（61.493s）各1/1、零跳过；Host/Tenant每范围4轮200+409，共16对竞争，原断言未放宽。两轮1136冻结输入、同次UID及资源回收均独立核对。新API的core-4670e7c5（50.597s）与core-2c86d0b0（39.113s）各1/1、零跳过，完整OpenAPI/部署通过；后续32项也已按原UID完成补验，见最终汇总。

原失败结束后，资源上下文脚本在14:43:52 UTC被并行任务更新；旧aff926摘要已从Git快照恢复到evidence/orchestration-at-correction-failure，未回滚repo，也不把当前文件称为未漂移。99项原通过证据在各自当时已核对；失败仅做原始记录，不伪造完整当前闭包proof。另仅归档已退出PID17288、无子进程/活动预算持有者的失效准入门，未操作其他会话容器或源码。

## 已完成变更与直接验证

FAQ search原SQL UID116ea204-2076-8e2f-82e5-f62976cf5379在extended-9f1603c0为0/1、零跳过（4m27.967s）：replaceParent回调的Generate手工Acquire为空，未到旧来源拒绝断言。仍运行的FAQ索引Worker会扫描人工纠错任务，与夹具手工Acquire竞争；日志没有同任务先领者的唯一记录，关闭时SQL3980也不是先领证据。夹具现在先StopAsync，并硬断言AiKnowledgeIndexHostedProcessor.ExecuteTask完成，再执行原Workspace/Edit/Generate/Publish；生产租约、原Acquire非空及后续拒绝断言未放宽。原失败及1136项partial-proof保留为0通过/1失败。

重编遇到并行Calendar/AI契约与旧normal准备产物不一致。没有采用摘要不符的旧助手推导源码；普通Host.Api准备图明确以Jit稳定构建，0告警/错误（7m21.85s），4327输入前后一致。76程序集DLL/PDB、3350源码摘要、152份producer/运行文件独立核对；72项更新保留70项旧字节，新增Calendar.Contracts DLL/PDB。另为新增租户拒绝聊天用例补必需Title=null，原403不变；这不是新增个人日程验收。

控制器最终稳定构建0告警/错误（2m59.67s），DLL9b4fc13a…及PDB/CodeView、1102文档已核，包含Stop完成断言及Title参数。原SQL extended-da73a35b（10m39.823s）和MySQL extended-3ca483d8（6m6.755s）各1/1、零跳过；Host/Tenant完整后续拒绝、历史隐藏及预算守卫均完成。新运行1138输入，Native API8ffd/Worker098保持；原失败和编译失败不改写，后续漂移源码不冒充已重编。准备刷新及控制器证明见evidence/faq-search-normal-preparation-build.json、updates.json和linux-controller-faq-search-worker-fixed-proof.json。

| 变更 | 实际验证 |
| --- | --- |
| 完整OpenAPI静态闭包：Hosting文件/流3根、6模块接口集合15根、Document可空枚举2根，共20个精确根 | 关闭反射回退的定向单测2/2、零跳过（182ms）；完整Native双库core 2/2、零跳过，实际完整OpenAPI200及最终ready通过。没有通配root或新增suppression |
| DataApproval两个恢复处理器识别正常停机时的SqlException3980等取消相关异常，运行期错误处理保留 | 定向4/4、零跳过（1.210s）；此前新Native审批恢复/生成4/4、零跳过（4m42.059s） |
| Auditing正常关闭Channel的竞态：只在Reader正常完成时进入原排空路径，异常关闭与运行期Error保留 | 最终6/6、零跳过（413ms）；新Native双库core及两份API停机日志独立核对，无原B1 loop/drain错误 |
| Native夹具补归档Root、受控转换配置、正式发布/独立审核、证据保留及租约清理边界；日志和证书资产进入本任务临时目录 | 修复对应的core/Worker定向复验均通过；没有放宽生产失败关闭、保留期或租户/所有权断言 |
| Windows资源锁EPERM/EACCES/EBUSY按原期限等待；Native输入策略精确排除4个纯工具索引 | 对应工具回归5/5、零跳过（1.871s）及6/6、零跳过（48.5ms）。运行SQL、配置和实际ILC输入继续受守卫保护 |
| FAQ长测正式续取操作者，并恢复相同租户；历史准备每次读取当前readerToken，避免旧Claims引用已撤销会话 | 原SQL FAQ精确UID复验1/1、零跳过（11m47.862s），Host/Tenant全部断言完成；未延长生产10分钟TTL、未用403自动重试、未放宽409或计费断言 |

## 有效后端证据

所有通过项均来自原始TRX的同次UID、冻结字节及资源回收记录。不同组按各自冻结产物和未变路径复用，不将旧产物证据外推为最新全仓源码已验证。

2026-10-31补充历史Messaging复用边界：原预取8/TTL测试源码清单87/89项不含生产源码。另直接重哈希原API/Worker Native source-input内197/208个名称含Messaging/Outbox/Inbox的src路径，观察到旧枚举内8个生产文件后续变化（绑定、监控、回退就绪、lag及切流）；新增KafkaCdcMonitoringWorker.Streams.cs不在旧清单，不能说生产仅有8项变化。独立复核初始任务快照/HEAD字节后确认这8项均为任务初始后的多流/配置演进，不属于本轮OpenAPI、DataApproval、Auditing或Correction修复，不能仅凭diff判定会话作者。因此原长稳/TTL仍为当时冻结ELF、单流拓扑的有效历史证据，不认证当前多流控制、监控及配置路径已重跑。原始摘要和差异见evidence/messaging-source-reuse-check.json；此检查不等于完整传递闭包或新的运行验收。

| 范围 | 当前有效证据 |
| --- | --- |
| 双库核心/完整OpenAPI | 先前core-83ade89a为2/2、零跳过（1m54.295s）；API8ffd更新后core-4670e7c5/2c86d0b0各1/1、零跳过（50.597s/39.113s），1136输入与正常关闭已独立核对 |
| S3、Kafka范围重放、缓存、Kafka及本地日志Provider | providers-92e5e5be，10/10、零跳过（6m17.932s） |
| Notifications、Settings、Jobs HTTP/JSON | business-33128dbe，6/6、零跳过（3m45.080s） |
| OIDC/BrowserSession | 首轮16通过/2失败；保留默认300秒轮转窗口，补真实节点ready与持续续租后，精确2项复验2/2、零跳过（11m38.761s）。累计18个唯一UID |
| Worker | 累计35个唯一UID：双库Worker业务路径、审批恢复、知识生成/退休、PDF/Prompt、租户权威、日志；原部分失败保留，必要复验补齐 |
| 当前Worker双库完整生成及发布 | worker-38f047f7，2/2、零跳过（5m23.010s）；每库TXT/CSV/DOCX/PDF/PNG五格式，TXT/CSV恢复后模型调用不增加，1132冻结项及回收通过 |
| FAQ草稿与生命周期补验 | 草稿SQL extended-30ca0f4e（3m20.443s）、MySQL extended-2a2aec5c（2m7.798s）各1/1、零跳过；生命周期SQL extended-c8e73a26（9m51.635s）、MySQL extended-d1219300（5m55.620s）各1/1、零跳过。四轮实际TRX、1136冻结输入及清理均独立核对。结论绑定实际冻结控制器；共享NativeHuman测试源码后续改变，不认证为当前后续源码也已通过 |
| FAQ绑定文档普通路由边界 | SQL extended-f9c63dc3（3m19.935s）、MySQL extended-c5365cc2（1m49.365s）各1/1、零跳过；原UID、1136输入、控制器/Native身份与回收独立核对 |
| FAQ父子图 | SQL extended-64b30bd4（13m48.288s）、MySQL extended-dd3ca6bf（8m35.030s）各1/1、零跳过；结论绑定冻结原控制器，原UID、1136输入及回收按轮核对 |
| FAQ检索、问答与历史来源 | SQL extended-da73a35b（10m39.823s）、MySQL extended-3ca483d8（6m6.755s）各1/1、零跳过；1138输入，保持Host/Tenant旧来源拒绝、历史隐藏及预算不增断言 |
| FAQ传输与防降级 | SQL extended-23954ead（4m52.845s）、MySQL extended-04dc43d0（2m47.200s）各1/1、零跳过；同预算正式运行，保留原取消失败及独立诊断边界 |
| 发布目录、拒绝草稿与清理竞争 | SQL extended-83ac092b（1m39.801s）、MySQL extended-95b7a8b8（1m28.441s）各1/1、零跳过；1138冻结输入，按实际原UID核对 |
| 双库归档证明与历史4项 | SQL PDF(false) dd9c8d91（3m25.520s）、DOCX(true) 85c104b9（3m27.038s）；MySQL PDF ff8b1244（2m44.121s）、DOCX 9316f2c4（2m39.215s）。各1/1、零跳过；合成已结算manifest/vector及JIT coordinator恢复，不代表真实解析、转换或Native Worker消费 |
| 双库检索问答与历史复核 | SQL cf25567b（2m36.572s）、MySQL b413d2a0（1m57.411s）各1/1、零跳过；原Host-only正向问答范围，不外推租户正向模型派发 |
| 双库比较 | SQL 1a82a334（5m23.036s）、MySQL 3c4dfbcc（2m52.414s）各1/1、零跳过；原一次计费、范围与独立评分守卫，结论绑定原编译方法 |
| 双库上下文窗口 | SQL 484585c6（4m40.251s）、MySQL 3948588e（2m25.951s）各1/1、零跳过；范围、位置和冻结来源守卫，结论绑定原编译方法 |
| 双库发布检索 | SQL d4da1e98（4m28.531s）、MySQL 4497a747（2m28.542s）各1/1、零跳过；原范围、来源版本和无派发方法，1138输入 |
| 双库Host策略拒绝 | SQL b3b0f418（2m40.320s）、MySQL f7dd8267（1m41.897s）各1/1、零跳过；原拒绝、无预算和无载荷分支，1138输入及回收已独立复核 |
| 双库精炼 | SQL dab5c00a（11m11.804s）、MySQL 74095244（5m10.931s）各1/1、零跳过；原范围、独立结算阶段及拒绝守卫，1138输入与回收已独立复核 |
| 双库关系与权威校验 | SQL 02b00634（5m58.165s）、MySQL 9e55b6d4（3m15.402s）各1/1、零跳过；原内嵌父来源及完整权威校验入口，1138输入与回收按轮核对 |
| 9项独立Native补验 | 双库AI基本链、增量索引、SignalR、VForm及单项Smoke，共9/9、零跳过；阶段记录 native-independent-stages.json |
| 初始Native中的Workflow审批2项及独立Workflow服务9项 | 原冻结UID均通过；服务证据 workflow-runtime/services-0dbc25b5。Flutter桌面1项不在这9项中 |
| 私有反馈FAQ | MySQL原独立成功有效；SQL最终 extended-40ea843b，1/1、零跳过（11m47.862s），旧SQL回归消除。Host/Tenant各起始与负向前续取4次，两类根锁拒写共4次，费用/模型不增及未受影响来源正向对照均完成 |
| 新增来源/历史补验 | SQL/MySQL人工生成/发布/v4历史均已通过：extended-8bc86ecd 1/1、零跳过（2m50.423s），extended-dbff30d9 1/1、零跳过（1m52.378s）；其余原缺失UID已补齐，以最终汇总为准 |

CSV/PNG结构化来源新增双库4/4、零跳过：5db928f8、b96cb371、4e6dda3d、c10cc3e1；实际来源编辑、索引、正式发布、历史位置断言完成。每个真实解析沙箱独立3GiB/2CPU，保持原独占入口，不外推文本FAQ的6GiB预算。

新Worker的14份Native Worker日志及2份Native API日志所核关闭错误为零；原TRX stdout仍有旧JIT准备宿主的Auditing日志，不能宣称整批JIT与Native日志零Error。Native知识库API用例中的JIT索引/历史准备与真实Native Worker覆盖分开声明。受控模型不是付费供应商质量验收，合成解析证明不能代替真实解析；真实五格式Worker证据只按上表范围声明。

Messaging预取8双库双Worker Native60分钟长稳、自动TTL及迟到REST/分片发现整改保留原冻结包历史证据，见[Messaging续验报告](2026-10-28-messaging-reliability-continuation.md)，不在本轮重复执行或加进133分母。后续多流源码变化不由这些旧包结论认证，范围见前文复用边界。

## 当前产物与输入证明

- 当前API ELF：8ffd4e1b148e6a16e8467032ffd4699346289f5bfd7a2914cf2c533577c7bec3，171752608字节；sourceUnchanged及四门禁通过。先前a7881875…作为旧组的冻结输入保留。
- Worker ELF：098fdc2bd0ba7243d66b26daf866e288e282b22ed10cdfa173f2d097793bb687，108287720字节。
- API/Worker实际ILC rsp、发布清单及9条既有第三方告警已核。旧API a788的两个AI C#与Worker的RunAccess在各自托管编译后变化，旧manifest的sourceUnchanged=false原样保留；不外推为当前全仓源码认证。
- 当前Linux控制器DLL：9b4fc13a83d32ef1b65f014bd7ad90a7aa4c439c18633534c43a0e82cfdf8fbc；稳定构建0告警/错误（2m59.67s）。1102个实际PDB文档为1093仓库源（Shared74）和9生成文档，CodeView关联已核；先前dae022…控制器及旧证明保留，后续NativeHuman源码变化不冒充已重编。当前正常准备图和1138输入按本轮冻结记录区分。
- 原控制器采集器漏记Shared的缺口已由实际PDB证明补齐，后续采集器加入Shared；运行输入纳入实际PDB及证明文件，前后复验。没有改写旧before/after来承认已变化输入。
- 349双库迁移准备已刷新，DbUp DLL为1b754ee468663b2f60eb2bc773e36bb270abb52cb0898d2ab0df14179a228e95；736嵌入资源及两份349 SQL摘要匹配冻结源，旧348 DLL/PDB保留。这里只声明准备资源与真实core部署就绪，不代替349业务的独立恢复测试。

环境：i9-14900K（24核/32逻辑处理器）、约64GiB内存；Docker VM32CPU/约31.19GiB；SDK10.0.402、Runtime10.0.13、Docker Engine29.9.1。本轮是本机功能证据，保持 Capacity-not-verified。

## 原失败与收口记录

2026-10-31 02:44新增SQL传输UID1bfa91c4-4386-8d53-b606-4723c7a4cdce原运行extended-eef4de2a为0/1、零跳过（2m13.664s）。异常位于初始Generate的ExecuteBatch→VectorStore.ReadAsync→ReadCurrentModel→末次CurrentAsync的authority.Read等待，并未进入传输断言；linked25秒为外层剩余期限，不能说该末次调用单独耗时25秒。1138闭包及回收已核。静态读取链有多次完整证明，但未据此修改授权、缓存或预算。

仅该原UID的MeterListener隔离诊断（diagnostic-transfer-runtime/extended-13d8ab46）为1/1、零跳过（4m15.725s），不计正式133矩阵。hook只绑定managed测试入口；Dapper总/命令ms、连接与HTTP秒转ms，37轮资源采样。SQL容器35次样本最大CPU54.04%、对应1.228/3GiB；driver37次样本最大CPU78.05%（4CPU配额）、对应242.1MiB/6GiB。未采Native API容器CPU、数据库锁/执行计划；不能外推全部资源无压力。部分查询P95约40–50ms，滚动30秒队列cap12000会截断，只能报告有界观察，不能把SQL累计ms等同阶段wall/DB CPU或宣称优化收益。根因未唯一定位；原预算、生产实现和测试断言不变，其后仅作一次不带hook的原双库正式复验，结果见下文。证据为evidence/transfer-stage-diagnostic-contract.json、transfer-stage-diagnostic.log及该目录资源NDJSON。

补充观测边界：driver末次停机0B行不作性能样本；17个窗口达到cap且每窗仅输出累计耗时前24组，重叠窗口不可相加，采样不能排除间隙峰值。正式无hook复验SQL extended-23954ead（4m52.845s）、MySQL extended-04dc43d0（2m47.200s）各1/1、零跳过；SQL1138输入数组逐字等于原eef4失败数组。没有循环到通过、延长预算或改生产实现；这只证明同预算该次功能复验，不证明原取消已修复或性能提高。

| 原失败 | 处理与最终边界 |
| --- | --- |
| 完整OpenAPI500 | 精确补20根后真实双库Native200；原失败TRX保留 |
| 归档Root缺失、未启用转换Provider导致core失败 | 只修隔离夹具配置；原生core通过，未声明真实Office转换成功 |
| 旧JIT迁移准备/AI依赖、K01发布清理旧断言造成失败 | normal JIT依赖及迁移准备按来源刷新；夹具走正式发布/审核/租约和保留期，不直接修改业务状态冒充通过 |
| SQL FAQ25秒索引阶段 ai.knowledge.index_unavailable | AI模块复用已证明manifest所携model，实际新Native及normal JIT来源已核，最终SQL整项通过；未扩大阶段预算 |
| extended-5d265052，0/1、零跳过（10m43.769s） | 索引已过，随后同会话在10分钟整数秒边界前409、边界后403；结合真实授权代码定位长测过期。没有直接读取该令牌actual exp，保留此证据限制 |
| extended-fbffcdd4，0/1、零跳过（8m12.470s） | Host完成相关断言，Tenant SaveHistory仍缓存已撤销会话的readClaims；改为当前readerToken。最终40ea843b完整通过 |
| 源码并行变化、编译错误或排队撤销 | 拒绝的托管/控制器记录保留，稳定重编后才验收。仅撤销本任务未启动driver的队列并核PID/token归档锁，未删除其他会话进程或资源锁 |

原始分类和阶段过程保留于 evidence/report-before-consolidation.md 及各次TRX；当前结论以 native-result-audit.json 为准，不采用控制台摘要替代原结果。

## 本机剩余项

冻结后端133个唯一UID已全部通过，missing=[]、latestRegressions=[]；没有把诊断专用运行、跳过或未执行项计入分子。机器事实源为evidence/native-result-audit.json，最后一项于2026-10-31 04:33本机时间结束。结论限定各组实际冻结API/Worker/控制器及当时验证的路径，不认证随后并行演进的全仓源码。

桌面手动1项未通过：17:40那轮夹具900秒内未提交，实际0/1、零跳过（15m54.091s），资源已回收、账号及路径过期。自动化持续GetCursorPos拒绝访问，用户已接受手动但当前明确不便，故未再启动窗口。新Windows349准备已独立核798源/829运行文件，唯一运行DLL变化为DbUp；Flutter55源/12发布文件匹配。桌面入口独立冻结为API a788/Worker098，并非本轮后端8ffd API；准备就绪不计实际桌面操作通过。

## 最终检查与本轮清理

最终定向文本检查和工作区结果见evidence/focused-source-quality.json、final-working-tree-check.json；分支main、HEAD仍为75be6fc0e17433f9c59c42553093dacd479ef9d1，无索引冲突，两份原stash仍保留。并行WIP及6个其他任务既有删除/移动不做恢复，没有提交或推送，也没有运行仓库全量测试。

全部本轮验收进程退出、无活动任务挂载后，核绝对路径和每级非reparse祖先，仅删除旧API/Worker的2个编译.o对象，合计1,947,339,933逻辑字节；不把逻辑长度当作物理释放量。SHA256及精确清理结果在evidence/compiler-object-cleanup.json。发布包、托管证明、待验桌面夹具、可复用构建及原始失败记录保留，未操作其他任务目录。旧Messaging根的递归编译目录清理此前两次被自动安全策略以blocked by policy拒绝，未给具体规则；仍保留且未重试或绕过，详见原Messaging报告资源与清理结果。

## 外部环境与未实现角色

- 生产等价容量、1万同时在途、跨物理节点/可用区故障域需要对应环境；本机Docker不能替代，Capacity-not-verified。
- 真实AWS角色/Workload凭据、外部消息厂商送达、付费AI供应商质量及真实Office转换未由本地受控服务证明。
- Migrator、LogConsumer当前仍是JIT发布角色，尚无相应Native实现/门禁；这是独立实现剩余项，不是外部环境缺失。API/Worker的Kafka日志不能替代这两个角色的Native验收。
- 默认JIT/LegacyPolling、shadow关闭与生产切流状态未改变。
