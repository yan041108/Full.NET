# 本机剩余 Native 角色与阶段取消整改（2026-10-31）

## 范围与状态

基线 main / `75be6fc0e17433f9c59c42553093dacd479ef9d1`，任务快照 `native-remaining-hosts-20261031`。用户要求先定位原25秒取消，再实现并验收 Migrator、LogConsumer Native；保护并行改动，不运行全仓测试。此前[冻结后端133项报告](2026-10-30-product-native-local.md)保留原范围，不把本轮新角色塞进旧分母。

本轮本机定向验收已完成：Migrator 4/4、LogConsumer 4/4、修复后的人工校对Worker双库2/2，均零跳过。原始证据位于系统临时目录 `%TEMP%/fullnet-product-native-local-20261030`，复用现有构建，不复制整仓。下文保留失败与重建过程，历史“待结果”不覆盖后续实测结果。

## 25秒取消定位

原 SQL UID `1bfa91c4-4386-8d53-b606-4723c7a4cdce` 的 `runtime/extended-eef4de2a/result.trx` 证明：失败在初始 Generate 的 ExecuteBatchAsync 第37行，即向量持久化后的最终 ReadAsync；最后 ReadCurrentModelAsync → ReadProvenCoreAsync → CurrentAsync → authority.Read 等待时取消。外层批次共享25秒剩余预算，不能说该末次查询独占25秒。

原实现一次成功向量恢复含5次完整清单/权威证明。新增7个封闭阶段观测：batch、restore、embedding、persist、proof、current、authority，沿用原令牌及25秒期限。观测不生成业务身份、不缓存授权，也不吞掉错误或取消。候选修复合并两组相邻证明与模型读取，降为3次完整证明，保留最后一次独立权威复核、两次向量SQL、载荷/哈希验证及前后行一致性；没有跨调用授权缓存。

诊断仅订阅 managed 控制器的阶段 Trace 和固定 Statement 耗时，API/Worker 仍直接运行既有 ELF；诊断结果不并入原133矩阵。普通 AI 准备构建0告警/错误（1m18.65s），首次诊断 `correction-stage-runtime-baseline/extended-370c1cd1` 为0/1、零跳过（41.321s），原因是 Calendar 新契约与旧实现的 TypeLoadException，未进入目标链路。完整普通API准备图随后稳定构建0告警/错误（4m04.79s），`sourceUnchanged=true`；没有修改 Calendar 源码。原UID新诊断 `extended-67c224d0` 为1/1、零跳过（4m31.234s）。其TRX流达到约1MiB上限，只解析出1121条完整阶段记录；后续丢失条数未知，不能把部分样本最大值说成完整用例最大值。记录内20次完整恢复均含5次直接证明，平均3142.72ms，最大4078.82ms。

受控慢权威服务（每次文件读取1500ms、原25秒预算）旧实现对照 `remaining-runtime/baseline-slow-675e4c40` 实际0/1、零跳过（1m12.814s），恢复跨度在25030.264ms取消；该跨度的向量SQL为44.238ms。候选 `candidate-slow-41931b04` 实际1/1、零跳过（1m05.909s），恢复19125.977ms，3次完整证明和2次向量SQL，闭包未变化。两次原始阶段NDJSON均完整保留，摘要 `evidence/correction-slow-comparison-result.json`。候选普通AI构建0告警/错误（56.72s），其所有闭包路径与原普通API图逐项比对仅两处证明合并源码不同，运行目录仅替换AI DLL/PDB，控制器和预算相同。这证明重复完整证明会放大权威服务延迟并耗尽预算；历史那次外部变慢的唯一触发因素仍未恢复，不能据此宣称已唯一确定。双库负面边界验证待结果。

## 两角色实施

按[ADR-0015](../architecture/adr/ADR-0015-migrator-log-consumer-native-aot.md)新增显式 Native 开关与独立发布契约，默认仍为JIT。Migrator的播种审计/租约改为显式DynamicParameters；Identity、Tenancy、Settings、Regions迁移入口注册实际播种物化闭包，保留最小Profile。LogConsumer固定bulk action改为Utf8JsonWriter，保留JSON转义；native binding只列出Confluent.Kafka实际Linux候选类型。

| 定向范围 | 状态 |
| --- | --- |
| Migrator：双库Baseline/Development幂等、Production拒绝Overlay、journal失败不播种、可选OIDC客户端/签名材料及保留管理策略 | 4/4、零跳过（1m31.373s），真实ELF运行，范围内 `Aot-published` |
| LogConsumer：真实TLS Kafka/HTTPS ES，单条与64上限模式、API Key拒绝、确认前崩溃重放、DLQ失败不提交及SIGTERM、实验门禁 | 4/4、零跳过（5m26.231s），真实ELF运行，范围内 `Aot-published` |
| 校对阶段观测：取消/错误原样传播、父跨度与有限标签 | 8/8、零跳过（206ms），构建0告警/错误 |
| 校对恢复双库：第二次向量SQL后撤权、清单实际丢失、载荷/来源一致性与不重复计费 | 4/4、零跳过（2m32.436s），candidate-boundaries-4f9151ba，运行闭包未变化 |
| 两角色默认JIT与显式Native开关 | 2/2、零跳过（1.759s），实际架构用例源码的独立小宿主，仅仓库路径定位作显式适配 |
| LogConsumer AOT分析器完整生产引用闭包 | 0告警/0错误（12.05s），源码前后稳定；独立Linux输出，非ELF运行证明 |
| 发布角色、告警及实际ILC输入守卫 | 原两个选定Node文件13/13（82.1981ms）；预算隔离新增后14/14、零跳过（107.4265ms），native-role-budget-contracts.log |
| 发布工具版本冻结 | 新增回归后选定文件4/4、零跳过（64.6728ms），含缺失文件和运行中策略变化拒绝；原始日志 native-independent-tooling-focused.log |

发现并修复发布证据风险：旧脚本进程结束时才读策略文件SHA，会把当时新文件摘要与旧加载实现混用。新入口在启动时冻结4个工具文件，准入后及链接后复核，变化时拒绝生成成功manifest。已在运行的旧进程不能由此追认新策略；用户随后明确授权风险同步，已向Workflow会话发送，并明确针对先前旧进程，不误指当前新进程。

独立只读审查已核对两角色直接ELF入口、最小播种闭包及原失败/重放断言复用，未发现新的静态启动阻断。审查指出初版Migrator用例关闭OIDC，随后已在相同4UID内补入真实可选客户端/签名材料播种及重跑保留策略断言，仍待运行。LogConsumer批次64是配置上限，排空探针32条，不称为实际64条满批；TLS场景使用Ssl，不代表全部SASL机制。真实发布及运行仍待验。

Migrator首次Native准备遇到并行AI变更：旧Contracts DLL缺少新加入的 `AiPersonalScheduleCompletionTarget`，导致CS0234/SYSLIB1030，当前源码已存在该类型。该次不记通过，不修改AI契约；保留失败日志，并在准备阶段结束后使用原目录增量重建。

首次准备实际13错误（10m14.93s），还包含并行Calendar新增类型；第二次增量准备6错误（3m33.18s），来自并行日程完成实现的可空Guid/参数字典类型。读取当前源码时这些行已由所属会话修正，本任务未覆盖。两次失败日志均保留，第三次在原目录排队。新独立角色控制器稳定构建0告警/0错误（1m27.71s），DLL `21fd436a505877329d5e81100b9ffebbe23eaefd8d1652cd7387ea79e6aa4bc7`；独立只读审查确认29个实际Compile与2个生成文档、PDB关联和OIDC断言均一致。

第三轮获得窗口后，准备编译0告警/0错误（3m58.72s），但期间AI日程服务/SQL及SQL索引变化，守卫拒绝混合输入并只重试增量准备；第二次准备0告警/0错误（2m05.46s），稳定输入已进入Native链接。双库运行已先排队：先核对排队控制器未变，待发布成功并释放编译预算后读取同次manifest/ELF，再冻结实际运行闭包。队列控制器快照与真正运行快照分开，不把尚未存在的产物记为已冻结。

该轮链接完成后，输入守卫实际拒绝登记成功：`attempt-DlPZHS/rejected-input.json` 证明HEAD及冻结托管IL未变，但链接期间双库 `351_PersonalScheduleCompletion.sql` 被并行更新（source-input/source-after-native保留两版摘要）。迁移SQL是实际嵌入资源，不能用旧IL追认新脚本；本任务没有放宽守卫或改写SQL。运行队列因没有成功manifest安全退出，没有启动旧产物。第四轮在原目录重新准备资源，未采用不合法的 `resume-managed`。向AI会话同步稳定窗口需求的新内容已请求用户授权，尚待回复。

第四轮仅排队未编译：无活动构建槽时Windows可提交内存约14.4GiB，旧12GiB预算加4GiB系统保留不能准入。已取消自有排队进程，核实PID41120退出、无子进程和Native编译容器后，仅归档它的精确token工作区锁。Migrator独立调整为2CPU/8GiB，资源池与容器硬限制同取角色契约；API/Worker/LogConsumer预算不变。相关Node回归14/14；第五轮在该限制内稳定准备0告警/0错误（2m19.21s）并完成真实发布。

Migrator ELF为107,645,544字节，SHA256 `c1f9ef70f0a9b6c4de74564a5b0db98de43c1c6c2a89c849a9d724c73780e2fb`，全部输入/HEAD/工具守卫通过，8条均为原精确登记告警，无DbUp或Kafka新例外。`remaining-runtime/migrator-35bb3c36` 实际4/4、零跳过（1m31.373s），原始 `migrator-d5b7bc6d-7d50-41a0-b550-278cb44dac77.trx` 和每个CLI进程日志保留；00:45:21 UTC `reaper-clean`记录容器/网络/卷均为空。按本轮双库迁移与播种范围登记 `Aot-published`。

## 修复后的Worker直接边界

只读复核确认原五格式Worker用例走普通上传/index，不进入CorrectionSource分支；其迁移前置为FullNetApiFactory进程内DbUp，并非虽被注入但实际未使用的JIT Migrator子进程。因此不借原35项或五格式2项认证本次proof合并。

新增双库 `NativeWorkerCorrectionGenerationE2ETests`：正式HTTP prepare/edit/index，Native Worker真实准备来源和生成；独享库激活触发器在结算/向量后阻断激活，移除后正式retry，模型拒绝前仍计数，核对调用数、完整账本（含费用、Token、结算时间）和payload均不变。明确核任务Correction三字段及correction格式、完整generation/active指针。TXT解析是进程内路径，已删除无效CLI替换及“由此证明未重解析”的错误措辞。用例只读审查通过，编译及运行待验。

普通API准备图稳定构建0告警/0错误（3m51.24s），刷新真正加载的DbUp及AI/Contracts/Calendar一致闭包。成功API `1fc0773a…`、Worker `7d672dd7…` 的三份校对源码均与当前实现匹配；只读复制运行必需publish文件及原始manifest/source/ILC证明至本任务冻结目录，不复制整仓、构建缓存或debug大符号。新增矩阵入口 `ai-knowledge-correction-worker-native` 独立2UID，原133分母保持冻结。首次聚焦控制器编译因四个未使用的夹具字段触发CS0649，已改等价默认属性，未抑制告警，重编待结果。

随后控制器稳定编译0告警/0错误（1m23.69s），33个实际Compile；`remaining-runtime/worker-correction-d172e861` 实际2/2、零跳过（2m27.743s），精确UID `1344f7bd-ad84-84c6-9555-3b28ecd70a9c`、`1851a9c3-8e48-81f5-8b62-10314699c38b`。双库人工修订真实由Native Worker首次派发并结算，故障后正式retry恢复，拒绝前模型计数仍各1，Token各1，账本及向量payload不变，运行闭包未变；00:57:33 UTC记录资源回收为空，parser前后均为空。该轮未配置价格，ChargedCost为NULL，只认证已知Token及费用字段不变；现补入操作前测试价格与0.000002非零费用断言，重编/复验待结果，不能把首轮称为已知金额验收。

最新两个Node文件15/15、零跳过（73.9638ms），包含角色预算隔离及新的双库Worker入口，不执行全量测试。用户已授权向AI会话同步SQL漂移失败证据，已发送并说明Migrator随后重编及4/4已通过，不请求回滚现行SQL。

金额补验控制器 `5f6aaceb37fd33e33e438941893fe781af8891bbaf54134dae8113ed7535d1f7` 稳定编译0告警/0错误（1m35.81s），33Compile/PDB/CodeView由独立只读审查核对。首个金额运行在排队后仅因共享test-matrix被并行更新而拒绝旧快照，未启动测试；重捕获当前输入后 `remaining-runtime/worker-correction-c3d6b447` 实际2/2、零跳过（2m24.676s）。双库正式HTTP人工修订、Native Worker准备/首次派发/结算、激活失败与正式retry均执行；每库calls=1、Tokens=1、Cost=0.00000200，恢复后的完整账本（含金额及结算时间）和向量原字节均不变，生成和active指针存在。实际UID与原双库范围相同，不将重跑累加。运行闭包前后未变，01:30:17 UTC `reaper-clean`容器/网络/卷为空，parser前后均为空。

旧控制器已归档；后续compile-proof及共享matrix发生了正常更新，历史“运行时闭包未变”只指对应轮次前后核对，不能理解成所有历史文件当前仍未变化。原冻结133项仍是独立历史范围。本轮没有提交、推送、合并、生产切流或全量测试；分支main、HEAD保持 `75be6fc0e17433f9c59c42553093dacd479ef9d1`，原两条stash保留，任务相关差异检查和未合并冲突检查通过。

最终独立只读审查通过：金额版原双库UID精确一致，1151项输入重哈希全部匹配，控制器/PDB与API/Worker身份一致；6个Worker关闭记录齐、所查fatal为0，同次parser/reaper资源为空且session归属一致，无新增阻断。该新增Native校对范围为双库Host；不扩大为所有租户/提供程序认证或完整产品当前树全量验收。

LogConsumer正式发布于本机07:23完成（58.792s），SDK10.0.402、Runtime10.0.13、linux-x64。ELF 12,797,688字节，SHA256 `0c7188c8ce0e3eebb516d36403b72e0fee0f643b7274b292dbe5621078645fd3`；源码/托管/原生输入、HEAD和工具版本全部守卫通过，只出现精确登记的 `Confluent.Kafka.dll / IL2104`。发布manifest位于 `native-remaining-roles/log-consumer-UlYAtm/manifest.json`；运行队列已启动，尚不登记该角色 `Aot-published`。

首轮独立Native运行 `remaining-runtime/log-consumer-8810e332` 为2/4、零跳过（41.270s），DLQ失败不提交/重启重放及SIGTERM、实验门禁已通过；两个TLS用例都在创建ES夹具时因缺少固定镜像摘要 `82ac14f43fe701992e601f4cc81e1c0d7dbc5a2576d8cd736006452925df4026` 而失败，未进入目标TLS链路。原始TRX、stdout和进程日志保留；正在拉取同一精确摘要，不更换镜像或弱化断言。

精确镜像随后拉取成功；同一ELF/控制器的第二轮 `remaining-runtime/log-consumer-31ad7470` 为4/4、零跳过（5m26.231s），精确UID集合、双层运行闭包检查均通过。原始 `log-consumer-bac2295a-019b-4124-b2fc-55b2608e7db8.trx`、Native进程日志及资源记录均在该目录。`reaper-clean` 于00:04:13 UTC记录容器/网络/卷均为空。LogConsumer现按Ssl TLS、当前批次配置及本轮失败/重放范围登记 `Aot-published`，不扩大到未执行SASL、实际64满批或生产容量。

同轮两个32条/9600字节排空探针分别为0.589s（54.3条/s、16286字节/s，Native CPU20ms、峰值工作集56,160,256字节）及0.865s（37.0条/s、11097字节/s，CPU10ms、峰值55,816,192字节）。测试驱动实际限制4CPU/6GiB，Native消费者在同一驱动内运行；这些是有界本机小样本，不能外推持续吞吐、实际64满批或生产容量，保持 `Capacity-not-verified`。

一次早期诊断覆盖了旧验收的全局输入索引副本，各轮冻结输入和TRX未被修改。现已将该索引按 `runtime/extended-9e55b6d4/frozen-runtime-inputs.json` 的原始字节恢复，覆盖前的诊断副本另存，恢复摘要记录在 `evidence/global-input-pointer-restored.json`。

## 继续单列

- 桌面手动1项仍待新有效夹具；旧账号和路径已过期，不在本轮重启。
- 生产等价容量、万级在途和独立物理故障域需要外部环境，保持 `Capacity-not-verified`；真实外部消息送达、AWS角色、付费模型质量及Office转换也未由本机受控服务证明。
- 原Messaging单流预取8长稳/TTL证据不认证后续多流源码；旧编译目录递归清理曾被自动策略拒绝，未重试或绕过。
- 本轮LogConsumer静态分析成功后，递归清理 `%TEMP%/fullnet-product-native-local-20261030/log-consumer-analysis/build` 被自动审批审查拒绝，仅返回“blocked by policy”；未重试或绕过，保留其可再生成编译输出，原始分析证据仍在。
- 没有提交、推送、部署或生产切流。最终差异、资源回收与新鲜验证在本批完成时补记。

## 2026-10-31 Native 满批与 SASL 补验

在同一分区预置64条记录后启动原生消费者，通过暂扣回执的代理转发至真实 Elasticsearch，直接断言同一个成功 Bulk 回执有64个 items；在回执返回前终止进程，确认来源位点仍为1，重启后重放并确认位点65，再验证32条探针及 ES 文档总数与重放版本。该64上限场景采用 Kafka SASL_SSL/PLAIN；单条场景仍为 SSL。原本的私有 CA、错误 ES API Key、DLQ 故障及关闭门禁继续保留。该验证不涵盖 SASL SCRAM 或错误 SASL 凭据。

定向控制器构建0警告/0错误，29个实际编译输入稳定，DLL SHA256 `7e3055cf0bff08a49afff09be8587a2617f973c92b444f3de68cec419703e7a7`。复用之前已发布的 LogConsumer ELF `0c7188c8ce0e3eebb516d36403b72e0fee0f643b7274b292dbe5621078645fd3`，`remaining-runtime/log-consumer-91f1b0ec` 实际4/4、零跳过，原始 TRX 分别记录 `native-log-consumer-observed-bulk-items=1 sasl=False` 与 `native-log-consumer-observed-bulk-items=64 sasl=True`；运行闭包前后相同，01:45:49 UTC 记录该测试会话容器、网络和卷为空。本次为本地有界功能验收，仍不认证持续生产容量或独立故障域。

## 2026-10-31 SASL 原生认证补验

在同一 Linux Native LogConsumer ELF 上分别使用真实 Kafka SASL_SSL 的 PLAIN、SCRAM-SHA-256、SCRAM-SHA-512；每种机制均以错误密码建立连接，确认 Broker 认证拒绝、实际查询到消费组位点未提交且 ES 不存在待投递文档，再使用正确凭据完成真实投递、64项 Bulk、回执前终止及位点重放。仅使用测试临时账号。定向范围 `remaining-runtime/log-consumer-6a657b1b` 为4/4、零跳过，原始 TRX 中逐机制记录 `native-log-consumer-invalid-sasl-rejected` 和64项 Bulk；运行闭包不变，03:40:20 UTC 本轮容器、网络及卷回收为空。控制器DLL SHA256 `1a28f1eb72c3007e521f8a5bcec6041923fcda7b03d6d6b185887b21e6701e9d`；ELF为前次正式发布产物。

前两次各3/4：第一次夹具遗漏 Kafka 私有CA导致 TLS 先失败，第二次错误密码已被 Broker 认证拒绝但新建消费组位点查询遇到 `NotCoordinatorForGroup`。已分别调整CA配置时序、对协调器迁移错误做有界重试；只在实际查询成功后断言未提交，不以查询失败代替通过。失败原始记录保留在 `remaining-runtime/log-consumer-e7734a06` 与 `remaining-runtime/log-consumer-453b4100`。

本轮另对当前多流监控相关源码与聚焦单元测试执行构建（0警告/0错误）和9项精确过滤：共享组 lag 汇总、错误路由隔离、来源位点失败、按 Topic 绑定 retention 身份、取消撤销观测及四种非法边界，9/9、零跳过（317ms）。测试使用替身，不证明真实 Broker 的多流长稳或 TTL；原始结果为系统临时目录 `fullnet-messaging-reliability-20261028/multistream-unit-focused.trx`。当前 API/Worker 最新可复用包各有6个 Messaging 源文件已漂移，不能用于当前源码的真实长稳/TTL认证；新 API 发布按正式12 GiB预算等待，但可用提交内存约14.3 GiB，低于构建预算加系统余量；本任务已停止排队并释放专属锁，未开始构建。真实多流夹具、当前源码的双库长稳和自动TTL仍待完成。

## 2026-10-31 当前源码多流续验（进行中）

本轮更新物理夹具以满足现行专属流过滤门槛：主流和第二个独立 Connector 在 EventRouter 前按事件类型及版本过滤。第二 Connector 使用隔离测试库中的合成信封，在短测／长稳前后及主流自动 TTL 暂停期间检查独立投递与类型、版本隔离；它不是第二条已切流正式业务流，不计入业务吞吐。长稳准入要求同闭包双库短测包含该探针。已构建实际流过滤插件，Java定向测试通过；验收入口冻结插件构建源码、JAR摘要、Connect镜像、控制器源码、Messaging源码及工具和测试矩阵。相关 Node 准入测试12/12，更新后的定向控制器构建0警告/0错误。真实双库短测、长稳及TTL尚未完成。

本轮 Native API ELF SHA256 `26c3a7356e1c4b8c52a434b0701609de64af338bd41c4275e103169120372f5d`，稳定托管输入、原生输入、ELF与工具守卫通过；编译期第一轮因两个 Messaging 回退文件变化被拒绝并增量重编。链接期另有一个 AI 源文件变化，故不能称其为全部产品当前源码的完整快照；API 对应的198个 Messaging/Outbox/Inbox源码文件集合与当前工作树一致。Worker发布进行中。现有 Workflow 四条真实业务流的双库 Native 功能测试准备独立运行，不将其功能证据或上述合成探针冒充两条正式业务流的60分钟长稳与TTL。

本轮双库预取8 Native 短测 `component-native-prefetch8-smoke-Kc39tX` 为2/2、零跳过（7m50.171s），每库60条业务消息排空，独立合成流在故障前后均真实隔离并投递；发布后的源码、插件与运行闭包一致。第一轮 `component-native-prefetch8-smoke-MZ8DdH` 为1/2：MySQL通过，SQL Server探针的ExecuteNonQuery受触发器NOCOUNT影响返回-1；已改为同事务按Id核查真实行数，第二轮双库通过。第一轮结束时并行修改的 `EventDeliveryRollbackTests.cs` 也被源码守卫检出；已重新编译控制器，不覆盖该文件。

现有 Workflow 四条真实业务流的双库 Native 功能验收 `workflow-four-stream-a15a0e8b` 为2/2、零跳过（7m09.652s）；实际执行逐流影子采集和切流、Outbox/Inbox、重复信封及一条流回退后其他流继续运行，运行闭包不变，10:35:49 UTC该会话容器、网络、卷回收为空。该结果证明四业务流功能，不证明这些业务流的60分钟长稳或自动TTL。

Worker ELF SHA256 `d7616d4df4b61687cb5bca1a52b8363414d49146c345bbd00246f64484e76881`，发布源码、托管、原生、HEAD及工具守卫均通过，9项已登记告警；当前209项 Messaging/Outbox/Inbox源码与其发布输入匹配。正式 Topic 自动TTL首轮已结束；长稳与重试Topic TTL仍待完成。

正式 Topic 自动 TTL 双库运行 `component-native-auto-ttl-formal-JG5EpW` 为1/2、零跳过：MySQL 实际触发 Broker 自动清理，Low 从0推进到2，原重放快照和 producer fence 保留，主 Connector 暂停时独立合成流仍可投递。SQL Server 在等待暂停请求到达代理时超时，未开始调整 retention，不能作为 SQL Server TTL 通过证据；正在独立复现并只读采集 CDC 与 Connector 位点，不降低安全门槛。真实两条及以上业务流的双库长稳和 TTL 仍待完成。

SQL Server 单项复现 `sqlserver-ttl-diagnostic-27baf687` 在 TTL 前失败：第二 Connector 健康但全部历史事件被流过滤时，尚无已提交来源位点；原夹具先等位点再写第一条匹配探针。已调整为先真实写入并验证匹配及不匹配事件，再等待提交位点，保留隔离断言。聚焦控制器重编 0 警告、0 错误，输入守卫通过；修复后单项复现进行中。

修复后 SQL Server 复现 `sqlserver-ttl-diagnostic-a938d835` 已进入原暂停阶段，30 秒及 60 秒采样均为源事件 2、CDC 捕获 2，源捕获最高 `0000002D000002880008` 与 Connector 提交 `0000002d:00000288:0008` 相等，回退仍为 Prepared。当前覆盖判断对 SQL Server 要求严格大于，因而空闲流在相等时可能无法推进；不能直接放宽为相等，否则同事务部分消费可能被误判。原生产问题仍未修复，正式双库 TTL 不通过；复现仅作诊断，非验收通过。

为验证推进机理，在两次相等采样后，仅向该隔离诊断库写入一条被主流过滤的合成第二流事件（介入已单独记录）。主 Connector 提交位点随后越过冻结边界，回退进入暂停并触发真实 TTL，SQL Server 原窗口 Low 从0到2、固定重放断点及 fence 得以保留。该次单项进程返回0、输入闭包未变，但因人工额外推进仅作为诊断，不计正式 TTL 验收；不得据此认为空闲流问题已修复。

## 2026-11-01 SQL Server 空闲流修复与验收

本轮保留提交 LSN 严格越过的原路径；相等时尝试以 SQL Server CDC 中唯一的 `(commit_lsn, change_lsn)` 记录、事件序号、活动回退代次以及目标流在两种 Connector 读取排序下无后续记录进行保守确认。同一命令内有多条目标事件、CDC 未捕齐或位置有歧义时仍拒绝。Connector 的跳过插入、事件处理容错跳过和 Connect 转换容错跳过配置被拒绝并纳入资源指纹。独立只读审查指出的同事务后续其他流位置也纳入回归场景。当前实现、定向单元及真实 SQL Server CDC 集成用例尚未构建或运行；资源预算正等待其他会话发布，不能登记修复已通过，也不能将本次代码用于生产。双库 Native 长稳和自动 TTL 尚未开始。

独立只读审查另外指出：从未产生目标 Outbox 事件且 Connector 没有真实位点的流仍无法通过原 readiness 合同；本轮实现仅处理存在历史事件后的空闲场景，零事件流需设计不伪造位点的独立证明并验收。相同事务中多行由同一 SQL 命令写入时，本轮仍保守拒绝，不能宣称覆盖所有事务形态。

聚焦单元控制器 `MessagingIdleFenceUnitFocused` 构建 0 警告、0 错误，执行 34/34、零跳过，测试输入前后未变；覆盖 Connector 位点解析、CDC 位点比较及双库 Connector 配置拒绝跳过插入、错误容忍和事件处理跳过。真实 SQL Server CDC 用例控制器正在构建，该单元结果不替代数据库或 Native 验证。

聚焦 SQL Server CDC 集成控制器构建 0 警告、0 错误，任务源码及迁移输入未漂移；真实 CDC 测试 `sqlserver-idle-cdc-485d3737` 已启动，尚无结果。独立只读审查在追加式源表和 no_data 快照合同内未发现新的已确认误放行，但未代替真实 Connect offset flush 验证。

首次 SQL Server CDC 定向运行 `sqlserver-idle-cdc-485d3737` 为0/1、零跳过：夹具同步释放包含仅支持异步释放的 DbSession 的 DI 作用域时抛出异常。已改为异步释放，正在重新构建并准备重跑；该失败不得计作生产修复通过，原始 TRX 保留。

第二次真实 SQL Server CDC 定向运行 `sqlserver-idle-cdc-02cc806b` 为0/1：已跨过异步释放问题，暴露测试误假定单条多行 INSERT 的各行必共享 command_id。已修改用例读取真实 command_id 与 seqval，只有在两种读取顺序都位于另一目标行之后时才预期通过；同 command 或顺序冲突仍预期拒绝。重新编译和复验待完成，原始失败保留。

真实 SQL Server CDC 定向运行 `sqlserver-idle-cdc-3975ec0a` 为1/1、零跳过，任务输入前后未变。两条独立命令同事务时中间位置拒绝、末位置接受；另一流后续位置可证明目标流覆盖；错误代次和序号拒绝。单语句两行实际 command_id 为1、2，change_lsn 分别为 `0000002C000072580002`、`0000002C000072580004`，前者拒绝、后者接受；不声称已物理覆盖同 command 歧义。审查建议补充两条ID和同提交结构断言，已添加，最终重编重跑待完成。

最终补充同提交与 ID 断言后的真实 CDC 运行 `sqlserver-idle-cdc-f2392797` 为1/1、零跳过，所冻结输入前后未变；实测 command_id 1/2、首条拒绝末条通过。该用例直接构造 Connector 位点，尚未验证真实 Kafka Connect offset flush 与 Native 回退，因此本次修复尚不登记端到端通过。当前 API/Worker Native 发布按资源预算排队；零事件流、同 command 歧义及真实多业务流长稳/TTL 仍待处理。

随后增加回退控制器同事务证明暂停前后重新核验的正反用例，四个相关测试类聚焦执行112/112、零跳过，编译0警告/0错误，所列输入前后未变。此轮脚本误复用了上次 TRX 路径并覆盖34项的原始报告；34项仅保留先前进程输出，112项报告包含其相关范围，不能将两个结果累加。后续脚本使用唯一报告目录。

新 Native Worker 已完成发布：`worker-TiIoQl`，ELF SHA256 `0973b314974792d8f56ee4a3b38f43b74b32483fbd861346c5e59a27b0786d91`，源码、托管、原生、HEAD和工具守卫通过，当前209项消息源码身份匹配。API发布因系统提交内存不足及其他会话预算排队。为提前诊断，独立系统临时目录构建了 JIT TTL 控制器，只有临时副本放开 Native 入口限制，0警告/0错误、任务输入不变；真实 SQL Server/Kafka 单项正在排队，不能作为 Native 验收。

隔离 JIT 诊断 `sqlserver-idle-jit-5a60be92` 无人工补写地到达暂停并记录 SQL Server formal Topic 实际 Low 0→2、fence 与重放断点保留；但整体0/1：临时 JIT 副本沿用仅支持 Native 容器的停止断言，且运行中 `MessagingKafkaFaultProxy.cs` 被并行会话更新，输入守卫拒绝了该轮。仅作为定位线索，不计通过。已只修改系统临时 JIT 副本的进程清理方式，当前以更新后的共享源码重编并准备重跑；正式 Native 夹具未放宽。

隔离 JIT 修订后 `sqlserver-idle-jit-082bcc31` 实际1/1、零跳过，输入守卫通过；SQL Server、真实 Kafka/Connect、双 JIT Worker 下 formal Topic 自动清理 Low 0→2，原重放窗口和 fence 保留，主流暂停时合成探针继续。该轮未记录暂停前必须相等的显式位点，不能单靠它证明走了新增相等分支，更不是 Native 或双真实业务流验收。现仅在临时 JIT 诊断副本加入回退前实测 CDC 与 Connector 提交 LSN 相等的前置断言，准备再跑。

加强前置条件的 JIT 诊断 `sqlserver-idle-jit-5e9e2750` 1/1、零跳过、输入不变；回退前源2条且CDC捕获2条，源和Connector提交LSN均为 `0000002d:000001f8:0008`、事务内位置 `0000002d:000001f8:0006`、序号1；之后自然进入暂停并实际完成TTL防护检查。为排除操作期间位点推进，已在临时夹具增加暂停后仍相等的断言，待资源重跑。此证据为 JIT，不是 Native，也非两条真实业务流。系统提交内存恢复后 Native API 发布已开始。

多流 TTL 设计复核发现 Workflow Relay 在某候选的 producer fence 异常上退出整批，未满页重扫时可能饿死后续独立流。已在候选自身事务及作用域退出后仅捕获该 fence 异常并继续其他候选，取消和其他错误仍传播；定向单测待运行。此生产修复使先前 `worker-TiIoQl` 的 Workflow 源码不再是当前输入，不能用于本轮真实多流验收。此前 API 发布使用旧 Workflow 托管输入，本任务核对容器归属后主动停止并保留失败记录 `fullnet-test-run-VfsFlz`，未登记成功；须在新源码验证后重发 API 和 Worker。

已新增显式开关下的真实 Workflow 四业务流双 Native Worker 夹具：60秒预检/3600秒长稳、预取8、中途重启一台Worker，逐次记录16个正式/重试分区及两个容器资源；尾部检查所有实际非空窗口排空。TTL 场景暂停 Completed 流、实际自动清理正式或重试 Topic，审批成功但后台 Completed 中继被 fence 限制，同时取消分支验证另外三条真实业务流；正式Topic额外检查部分重放固定窗口。默认 Workflow 测试不启用该场景。首次聚焦编译因客户端要求 List 而非数组失败，已修正并重新排队；尚未宣布构建或任何物理验收通过。

跨流 Relay 修复定向单测 `workflow-fence-unit-f3e87da4` 为3/3、零跳过，构建0警告/0错误。新 Native Worker `worker-SQtD35` 已发布，ELF SHA256 `e1f1eca86d159425e5e46704fc60dbf1ee065c97a8ea897d715b224649f3fa00`，源码、托管、原生、HEAD、工具守卫均通过；它包含该 Relay 修复，替代仅在历史轮次有效的旧Worker。加强前后相等位点断言的 SQL Server JIT 诊断 `sqlserver-idle-jit-681e014c` 正在运行；真实多流控制器仍待重编。

相等位点加强诊断 `sqlserver-idle-jit-681e014c` 1/1、零跳过且输入未变：源与Connector暂停前LSN均为 `0000002d:00000218:0008`，实际暂停后仍为该相同LSN，change_lsn `0000002d:00000218:0006`、序号1；未补写其他事件，随后 Broker 实际TTL与原窗口保护通过。该结果仅为JIT。真实业务多流控制器重新构建0警告/0错误，4593项任务输入前后不变；双库60秒预检及60分钟长稳仍未运行。

后续双库 Native 真实四业务流短测 `real-business-smoke-1c1256d9` 为 2/2、零跳过：MySQL 61.94 秒、SQL Server 63.69 秒，两个 Worker 实际分配分区、单 Worker 期间业务成功，正式 Topic 自动 TTL 后保持围栏并验证其他三条业务流继续。此结果使用下述并发关闭修复之前的产物，不能代替修复后的验证。

双库 60 分钟运行 `real-business-soak-b3d0b960` 为 0/2：MySQL 运行约 12 分钟后、SQL Server 较早出现协作请求确认失败，均记录 `workflow.collaboration.closed_cancelled`；不得登记为长稳通过。诊断显示授权有效、源步骤与代次活跃且目标已创建。只读审查发现两个 Worker 并发启动目标时，旧执行者可能在另一执行者创建目标后将输入准备失败作为拒绝，并通过通用关闭语句取消已创建的准入。现已增加基于原版本、绑定、目标作用域、requested 状态及空目标的专用关闭 CAS，通用撤销路径保留。该竞态与证据吻合，本轮具体失败的执行者级原因未直接捕获；修复后的双库回归和长稳仍须实际运行。

修复后的 Worker 和 API Native 分别为 `worker-Vp7itF` 与 `api-ih1HAc`，发布输入、源码、HEAD 和工具守卫通过。Worker 首次重发的 SerialNumbers 编译器退出 139，容器无 OOM，重试通过；该失败记录独立保留。修复后预检曾因等待期间共享 Workflow 测试文件发生变化而在运行前被输入守卫拒绝；未覆盖并行修改，夹具按现有源码重新构建（4594 项输入不变），真实验收仍在资源队列中。

随后预检在执行前再次拒绝：并行会话更新了测试锁工具 `test-run-context.mjs`，现有 Native 清单的发布工具摘要不再一致。旧产物并未损坏，但不能以新工具摘要登记旧进程的发布结果；本任务使用自己的已有目录重新发布，完成后才可继续验收。

Worker 已按新工具在自有目录增量重发；API 复用并行会话生成的只读 Native 产物，ELF SHA256 与本任务之前发布结果一致，按实际源码、工具和二进制摘要验证。过滤插件按当前工具重新构建并通过自身测试。首次修复后短测 `real-business-smoke-adc4b213` 为 0/2：新增事务夹具未先打开数据库连接，双库均在该处失败，已修正，原记录保留。

修复后重新运行 `real-business-smoke-7dcb06d5` 为 2/2、零跳过，运行前后输入一致；覆盖真实双库的启动失败关闭 CAS 正反条件、四条 Workflow 业务流、双 Worker 与单 Worker 业务切换，以及正式 Topic 自动 TTL 和其他流隔离。该 60 秒短测不代替每库 60 分钟长稳；后者已重新开始。

长稳 `real-business-soak-fdff60da` 中，并行修改使 SQL Server 心跳约束源码与冻结输入不同；运行无法代表当前源码。已停止本任务测试子进程并由其专用回收器确认容器、网络及卷均清理，未动其他会话资源。MySQL 最后采样为 2488 秒、345 次业务循环，完成单 Worker 故障切换，但未满 60 分钟且输入已漂移，不能登记为长稳通过。当前源码的 Worker 已重新发布，API 正重发；将以当前源码重新执行。

当前心跳约束源码的 API 与 Worker 均重新发布，发布输入和工具守卫通过。SQL Server Native 定向测试 `native-idle-heartbeat-bc2bd4e0` 为 1/1、零跳过，运行输入未漂移、隔离资源清理：目标流捕获表无新增行，另一真实 CDC 捕获实例提交事务后，Broker 收到真实心跳，目标 Connector 持久 LSN 从 `0000002d:00004b10:001c` 推进到 `0000002d:00004d58:0003`。该用例不证明完全无可证明位点的空流可以回退；其双库长稳和 TTL 仍须以当前源码重新验收。

当前源码的双库 Native 短测 `real-business-smoke-40566d0c` 为 2/2、零跳过：覆盖真实四业务流、双 Worker 和单 Worker 切换、启动失败关闭 CAS 正反条件与正式 Topic 自动 TTL；输入守卫通过。每库 60 分钟长稳已重新启动，短测结果不得替代长稳结论。

当前源码长稳的 MySQL 已记录 3601.5 秒、507 次业务循环及单 Worker 期间业务成功；结束时四流 Outbox/Inbox 为 513/513、512/512、255/255、255/255，失败 Inbox 为零。正式 Topic 自动 TTL 的低水位从 0 到 255，其余三条真实业务流继续。SQL Server 仍在运行，整个双库批次及最终输入守卫尚未结束，暂不登记双库长稳通过。

该批次的测试平台 `--timeout 90m` 实为全局会话超时，顺序执行两个各 60 分钟用例时会触发取消；MySQL 已完成后，SQL Server 仍在执行但整轮结果必须按最终报告判断。已准备固定相同源码、Native ELF、插件与工具输入的 SQL Server 单用例补跑入口，使用足够的会话超时；不以被取消或未完成的结果宣称通过。

原双库会话最终中止，平台汇总仅记录一项成功且未生成 TRX；SQL Server 虽实际生成 3614 秒采样及 TTL 数据，该取消轮不登记通过。SQL Server 单用例补跑 `sqlserver-business-soak-c83946b9` 已通过 1/1、零跳过，真实运行 3605.5 秒、175 次业务循环，单 Worker 期间业务成功且双成员恢复；正式 Topic 自动过期后低水位从 0 到 89，其他三条真实业务流继续，运行输入未漂移。MySQL 独立用例正在补跑，以取得不受全局取消影响的 TRX。

MySQL 单用例补跑 `mysql-business-soak-6e537de7` 已通过 1/1、零跳过，真实运行 3604.4 秒、505 次业务循环，单 Worker 期间业务成功且双成员恢复；正式 Topic 低水位自动从 0 到 254，其他三条真实业务流继续，运行输入未漂移。两库独立用例的实际源码、Native ELF、插件和工具输入均与先前同一批冻结输入逐项一致；SQL Server 最终四流 Outbox/Inbox 为 181/181、180/180、89/89、89/89，失败 Inbox 为零。重试 Topic 的自动 TTL 另行验证。

重试 Topic 自动 TTL 定向验收：MySQL `mysql-retry-ttl-6e049d75` 与 SQL Server `sqlserver-retry-ttl-524d746a` 各通过 1/1、零跳过，真实业务信封副本进入 retry.5s 后由 Broker 自动过期，低水位均从 0 到 1；围栏及原资源保持，其他三条真实业务流继续，运行输入未漂移。原双库长稳入口的全局超时已改为 180 分钟，避免两库顺序执行时重复触发 90 分钟会话取消。
