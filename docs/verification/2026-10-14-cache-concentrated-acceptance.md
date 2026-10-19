# 缓存模块集中验收记录（2026-10-14）

约定的缓存功能及集中验收已完成，结果按各冻结来源和拆批复用边界保存。双库性能实验不证明所有场景稳定提速，完整当前main与SDK402仍独立验收，容量保持 `Capacity-not-verified`。

**2026-10-15 16:38:48（Asia/Shanghai）最终收口：14/14 主项已验收，0个必做待验收项。** 修复后的SQL Server/MySQL入口2项实际通过；与此前关闭1项、Native新24项及复用18个完整方法身份组成已核对的拆批并集。最终凭证为`cache-plan-expiry-finalization-completed-proof.json`。原失败父批及中断记录保留；这是既定冻结缓存范围的验收，不代表完整当前main或SDK402验收，容量仍为`Capacity-not-verified`。两个条件项保持No-Go，不新增代际键或分布式锁。

16:44最终复核补记：`node artifacts/cache-hardening/verify-cache-entry-expiry-type-windows.mjs --execute`实际完成两库2/2、零失败/跳过，TRX SHA `5e6b0fa363b3780a6298747e2c51eb4e29c4d885aabb06e4f5d4ad3b9476db02`，父退出0且signal为null，16:38:26整窗清理确认。独立复核追加发现旧失败回执尚未绑定其原始证据摘要；已补齐scope、来源、原主/监督、terminal及全部evidenceSha256核验，未修改或重跑.NET。`cache-expiry-old-receipt-binding.test.mjs`直接执行收集器实际门禁，先1通过/4指定失败，再5/5；连同原14项工具检查共19/19、零跳过。`node artifacts/cache-hardening/verify-cache-final-expiry-split-union.mjs`严格汇总重跑退出0，`node artifacts/cache-hardening/finalize-cache-expiry-plan-after-runtime.mjs --preflight`重核既有证据和232个当前输入退出0，二次只读复核无新增Important问题。原16:38完成代码及两份凭证另存`.before-receipt-binding-fix`快照，当前完成凭证刷新严格汇总SHA和复核记录，历史失败不改写。Markdown与任务范围diff检查均退出0；容量及当前main边界不变。

**2026-10-15 16:34 类型修复补测（替代下列等待状态）：** 原`d4298fda…`双库批次实际1通过（MySQL）/1失败（SQL Server），父退出1、运行退出2、零跳过；TRX SHA `cf256efa0413970d68145c31edc5a8f7886b7c7ef5b772207fdf1c27e3712204`。SQL Server在租约未变化比较之前因夹具用`QuerySingleAsync<DateTime>`读取`datetimeoffset(7)`而抛InvalidCast；MySQL物理列为`datetime(6)`。原批28秒增量构建零警告/错误，未直接命中严格构建复用；运行和清理实际完成，16:20:11父终态、16:20:28整窗回执。旧收集器和收口入口正确拒绝完成，原失败及源码完整保留。

修复仅将两次租约读取改为`ExecuteScalarAsync<object>`，首次断言非空，按同库原始boxed标量比较；不强制转换两库不同CLR时间类型，不放宽公开403、作用域、停用拒绝、权威故障或取消断言。独立只读复核未发现Important问题，实跑仍待结果。新来源`700cd86475a77b850c27ff39844c3eaf9b8f30b245bf3beb8c8662ed11e06093`、8,044输入，相对Native C4仅同一夹具变化，生产和发布物不变；原MySQL通过参数行不复用，双库2一起补跑。

AI页面18已实际完成、原演员/锁释放后，新正式helper18016/监督57652于16:28取得测试锁，目录`cache-entry-expiry-type-integration-runtime-114e8832`，仍在构建/执行流程。新实际终态`cache-entry-expiry-type-terminal.json`，资源回执`cache-entry-expiry-type-browser-boundary.json`，scope `cache-entry-expiry-type-terminal-resource-handoff`；最终收集器`verify-cache-final-expiry-split-union.mjs --wait`和收口入口`finalize-cache-expiry-plan-after-runtime.mjs --wait`已接续，状态`cache-plan-expiry-finalization-state.json`，只有实际成功才生成`cache-plan-expiry-finalization-completed-proof.json`。两份私有工具保留原门禁并额外绑定中间失败来源，新module的14项工具检查通过，不累加.NET通过数。当前仍 **12/14主项已验收、4个必做检查项待验收**。

**2026-10-15 16:06 收口接续：** 新增私有收口入口 `node artifacts/cache-hardening/finalize-cache-plan-after-runtime.mjs --wait`，只在修复后双库实际通过、严格拆批收集器重跑退出0及整窗清理齐备后，才关闭CACHE-02、14的四个剩余检查项并更新本记录；不启动.NET或容器、不占重型锁。真实预检重核232个当前拥有输入及Unit259、模型双库2、Architecture235和后台/控制面9的原始TRX/UID/计数/摘要，未把待运行的入口双库2计绿。

文档门禁先取得9项指定失败再9/9；独立只读复核发现严格收集器/依赖未封存及文档并发写入风险，追加真实临时文件反例先2通过/3失败，修正后合计14/14、零跳过，日志 `cache-final-plan-all-guards-green.log`。现按摘要封存收集器、依赖和关键manifest，逐文件写前核对旧内容、校验后核对预期新内容，异常只回滚仍属于自己的写入；二次复核未发现新Important问题。旧后台批次不具有后来新增的cleanupConfirmed字段，适配按其原始已释放终态、实际空容器差集及既有governance来源/路径/TRX绑定完成，首预检失败保留。这些属于工具级证据，不累加为.NET通过数。收口状态见 `cache-plan-finalization-state.json`；当前仍12/14已验收，新双库运行待资源接续。

**2026-10-15 15:43 减少串行等待：** 在已获准独立副本 `d4298fda…` 中提前完成普通构建及精确发现，不占重型锁或启动容器。命令 `node artifacts/cache-hardening/verify-cache-entry-fixture-repair-windows.mjs` 实际退出0，Release零警告/错误、6m18.95s；发现SQL Server/MySQL各1项，UID精确等于原失败两项。正式输入/输出守卫通过，状态 `prepared-and-lock-released`，普通工作区锁已释放；结果 `cache-entry-fixture-repair-integration-preparation-87d95cb7`，汇总 `cache-entry-preparation-overlap-completed-proof.json`。此前两次PowerShell启动包装失败均发生在正式helper运行前，日志保留，改用已观察实际退出的exec会话完成本次准备，不累计为测试运行。

补测主队列39756仍等待AI r12x真实终态及整窗资源回执，然后取得重型锁、核对并复用该构建。此处仅接受构建/发现，不增加测试Passed数；当前仍为 **12个主项已验收、2个待全部验收、4个未勾选检查项**。CACHE-02新增合同与证据对应表，明确Unit、后台双库与Native结果的复用范围，以及工具/Hub新连接/聊天续租仍待实际双库验证的边界。

**2026-10-15 15:28最新收口：** 本轮Native实际 **24/24通过、零失败/跳过**，三分区实际退出均0、signal均null，分别10/10（17m03.859s）、10/10（12m11.185s）、4/4（3m57.168s）。父进程33728实际退出0，live-handle终态15:23:03；整窗回执15:23:12，原主/监督/阶段演员均已退出，自有资源及锁已清理。`node artifacts/cache-hardening/verify-cache-native-reboot-completion.mjs`退出0，核对每个原始分区退出码、TRX哈希、计数、UID、生产来源与清理，再合并此前18个完整方法Passed身份，生成`cache-native-reboot-42-completed-proof.json`。这是 **24新跑+18复用=42唯一身份** 的分批并集，保留原失败父批，不称同次42全绿；不需要重发布API/Worker或重跑12分钟构建。

双库原生冷启动与首次真实L2读取均通过。下列单位为毫秒，各阶段/Provider仅一个样本；启动到`health/live`不含迁移，Grid计时不含登录但包含完整请求处理，冷Peer Grid不是隔离的Redis命令耗时。

| Provider | 主/次进程启动到live | 首次Grid | 预热Grid | 冷Peer首Grid（实际L2） | 写请求至Peer观测 |
| --- | --- | --- | --- | --- | --- |
| SQL Server | 635.47 / 571.23 | 185.89 | 23.40 | 375.01 | 183.26 |
| MySQL | 550.32 / 568.58 | 113.56 | 9.25 | 107.79 | 53.07 |

`cache-cold-start-performance-closure-proof.json`绑定这两项Passed及测量、Native42证明、既有真实断连/回填证据、65,689请求零错误的原来源配置对照，以及明确标注纯L1的微基准定义；据此收口CACHE-13。既有性能来源`6a13122f…`与本次冷启动来源`c4f5e175…`分别保留，不作历史升级前后比较，不从单样本声称P99或容量。当前 **14个主项12个已验收、2个待全部验收、4个未勾选检查项**，剩余CACHE-02、14；Root修复后的JIT双库2仍按队列等待AI r12x整窗之后运行。AI42376已接收Native实际清理回执并启动其SDK402验证，资源接续不借作缓存验收；完整当前main/SDK402与`Capacity-not-verified`边界不变。

**2026-10-15 15:15实际进展：** 恢复窗口`fullnet-cache-final-specialties-native-9011dead`首分区已实际 **10/10通过、零失败/跳过**，运行退出0、signal=null，17m03.859s；`partitions.json`与原始TRX哈希已核对。包含此前返回false的SQL Server TXT旧代次、同方法PDF参数及SQL Server停用租户Worker准入。TXT旧失败本轮未复现，原单次失败原因仍未知，不能声称失败诊断修复了业务问题。第二分区继续运行，其余14个新身份及修复后的JIT双库2仍待结果；本条不是完整Native42接受证明。

已完成重启工具独立只读复核及两项本组修正：最终收集器现在逐分区核对实际退出码0、信号null、TRX/UID/计数，防止“全Passed TRX之后文件校验失败”被计绿；两只资源观察器不再按全Docker daemon创建时间等待外组容器，Native按实际session/owner/volume/network核对，JIT要求已运行子批明确完成清理。`cache-final-collector-exit.test.mjs`实际调用收集器回调，两个指定反例先失败后通过，连同两观察器外组容器反例及所有权边界共 **10/10**，日志`cache-review-recovery-guards-green.log`；这些是工具级Node检查，不计入Unit259或Native结果。

仅按实际创建时间与命令行核对后，替换了三个只读观察器/收集器；审计`cache-review-readonly-replacement-audit.json`保留旧身份，Native、AI、后继夹具主runner均未停止。正在运行的17144使用的helper未改动；其内部失败分区汇总修正版另存`verify-cache-native-reboot-resume.next.mjs`，当前最终接受由严格的原始退出码门禁保护。复核未发现本组修正新增明确阻断或假绿。AI出队观察器修正另存`artifacts/r12z/watch-sdk402-handoff.mjs`：本组已独立核对PID45100、15:09:57的真实创建时间及该命令行，脚本实际使用CIM身份分类；其工具记录为7/0，未混入缓存测试数。原r12x观察器源码/metadata故意留档，资源回执仍写原r12x路径。当前仍为 **11个主项已验收、3个待全部验收、5个未勾选检查项**。

**2026-10-15 14:53重启恢复（优先于下列运行状态）：** 系统事件1074/6006记录14:18:55发起关机，实际LastBootUpTime为14:37:47。原Native父进程/监督器及锁外等待者均被中断，没有live-handle终态，原退出码未知，不能补造0/1或正常资源回执。`cache-20261015-reboot-interruption-audit.json`保留系统事实；中断首分区只有原始TRX流，没有完整TRX或分区退出码，不接受其中任何通过身份。此前已完成的JIT关闭1、Unit259等独立结果继续有效。

Docker Desktop恢复后，按原记录的三个session核对本组停止容器：仅清理3个SQL/MySQL/Redis容器，保留源码卷`fullnet-cache-final-specialties-native-da058620`及42份诊断日志（含卷内继承历史日志，数量不表示本轮测试结果）。逐token核对后只释放99608的重型/工作区两锁，未修改外组27640/80568锁。`cache-reboot-native-resource-handoff.json`绑定审计及清理SHA，scope `cache-reboot-native-interruption-resource-handoff`，明确`originalExitCode=null`、`terminalObserved=false`。

新Native24入口`cache-native-reboot-batch-7ea51acc`（Node33728/监督39748）已启动，来源仍为`c4f5e175…`；实际预检退出0，并核对源码、完整输出哈希、发布物及24个身份，`ownership-preflight.json`明确复用da058620成功测试构建，本次没有重新构建或发布。新运行目录`fullnet-cache-final-specialties-native-9011dead`首分区运行中，结果仍待收集。真实终态/整窗资源回执分别为`cache-native-reboot-terminal.json`、`cache-native-reboot-browser-boundary.json`，scope `cache-native-reboot-terminal-resource-handoff`；不得沿用原中断窗口的缺失回执。

租户夹具修复已安装到Root，哈希`7ab55e78…`，安装审计`cache-entry-fixture-root-installation.json`；保留公开403，使用内部服务准备真实租户会话并核对所有权。当前Native只绑定独立C4副本，安装没有改变其输入。修复后双库2仍待验证：新队列`cache-entry-reboot-batch-c9721d96`（Node39756/监督45152，排队截止21:00）等待新Native整窗和新AI r12x（Node42376/监督40224）整窗实际清理后执行，原顺序不变。补测真实终态/资源回执为`cache-entry-reboot-terminal.json`、`cache-entry-reboot-browser-boundary.json`，scope `cache-entry-reboot-terminal-resource-handoff`。新只读收集器`verify-cache-final-reboot-split-union.mjs --wait`只在实际双库2、关闭1及Native24+旧18完整方法身份和两窗清理满足时生成证明；尚未生成。当前仍为 **11个主项已验收、3个待全部验收、5个未勾选检查项**，不因恢复进程计绿。

**2026-10-15 14:02最新收口：** `node artifacts/cache-hardening/verify-cache-resource-closure.mjs`实际退出0，完成CACHE-08的分批证据复核：Unit259、Redis8、真实ACL两参数行、256目标批量、30秒断连与积压关闭共映射17个方法，原始TRX哈希/Passed UID核对通过；四份冻结来源与当前缓存生产输入共124次比较，仅有已登记XML注释差异，无需重复.NET构建或测试。SDK默认熔断0、恢复延迟5秒以及项目资源范围均已记录；选项通过绑定/非法值与故障路径核验，不声称默认参数性能最优。接受有限目标、短时故障资源合同，不新增严格队列/内存硬上限、可靠排空或容量承诺。单项关闭来源的失败父批与独立Native运行状态继续保留。当前 **14个主项11个已验收、3个待全部验收、5个未勾选检查项**；剩余CACHE-02、13、14。

14:04运行补充：Native专项Linux测试工程Release构建实际退出0、零警告/错误，12m10.87s，精确发现24个恢复身份，第一分区10项已开始运行；不是新原生发布或24项通过。`verify-cache-final-runtime-split-union.mjs --wait`已作为只读收集器接续：只有两份整窗清理、修复后两库2实际成功、原关闭1实际Passed以及Native新24与旧18完整方法身份均满足时，才写入`cache-final-runtime-split-union-completed-proof.json`。原失败父批/失败参数行保留，不能将拆批并集描述为同次全绿或当前SDK402验收。收集器当前只有语法检查通过，尚未生成完成证明。

**2026-10-15 13:58最新状态（优先于下列历史记录）：** `c4f5e175…`的正式Windows Release构建实际退出0、零警告/错误，7m44.12s；专项JIT3实际1通过、2失败、零跳过，运行退出2，子批容器差集为空。通过的是恢复积压关闭用例：关闭前46条待恢复项，关闭耗时15.34ms，自有Redis连接4→0，完成关闭后观察期无恢复重试；托管内存观测为5,820,024→5,892,624字节。`cache-shutdown-actual-result-proof.json`绑定实际UID、TRX SHA `074b1700a61c0412ada62f48d04d633d74cc0288b0205dc712d5b06c2d14ff04`及单项结果；不将失败父批称为通过，也不把耗时断言解释为强制关闭期限或内存硬上限。

两库失败均发生于新增夹具使用租户公开创建聊天接口，期望201却实际403，尚未进入权威准入断言。权限目录明确该写权限只属于Host；初次静态复核漏掉了此夹具前提。修复稿保留公开403断言，改在可信租户作用域调用内部服务准备真实会话，并断言租户与所有者；后续只读复核未发现该修复新增重要问题，尚无修复后运行结果。新副本`d4298fda…`逐项核对8,044输入，仅修改此测试文件，生产代码和发布物不变；准备时漏复制一份清单内历史`.tmp`脚本已按原哈希补齐，原失败记录保留，未触发.NET重试。

当前缓存整窗`cache-final-recovery2-batch-3ce2fea4`继续Native24，实际预检/42身份发现和分区核对退出0，Linux测试构建仍运行；两份已接受API/Worker发布不重复。夹具补测仅SQLServer/MySQL2，入口`cache-entry-fixture-repair-batch-f81412b9`（Node107772/监督110100），锁外等待当前缓存整窗及已排队AI r12w整窗实际清理，排队截止21:00。运行入口在这两个前置结束后才安装Root夹具，避免更改运行中绑定的输入；补测不会重复关闭1或未变Native。资源回执为`cache-entry-fixture-repair-browser-boundary.json`，scope `cache-entry-fixture-repair-terminal-resource-handoff`。当前仍为10个主项已验收、4个待全部验收、8个未勾选检查项；关闭单项证据不自动收口整个资源复合门禁，SDK402、容量和完整当前main仍不在本批验收范围。

2026-10-15 13:24最新状态：来源`4d4896c7…`的API/Worker原生发布实际退出0，分别141,818,560/91,322,504字节，完整核对为`fullnet-cache-native-current-cf258d5c`下两份发布证据。Native40已结束，实际19通过、1失败、20未执行；失败为SQL Server TXT旧代次夹具生成返回false，尚不能确认根因。`cache-model-scope-partial-native-proof.json`核对两个实际分区TRX、UID、运行退出0/2和整窗清理，不称整组通过。失败诊断只新增受控状态读取，不改权限、租约、原断言或重试行为。

最终关联测试现已集中编写：恢复积压关闭1、工具/Hub新连接/聊天续租双库权威准入2、双库Native冷启动与首次L2读取测量2。尚未.NET构建或执行。Native补测按完整方法保留参数行，计划复用18个旧完整方法身份，执行剩余22项和新冷启动2项；不重复发布API/Worker。旧最后专项队列在前置失败后零执行结束，原截止及回执保留；新批次在锁外等待AI SDK402整窗终态/清理后执行，截止18:00。初次新等待者仅零阶段结束，用于修正测试上下文只读属性及可空诊断后重新封存，未中断测试；全部旧未执行来源保留。静态治理36/36、矩阵7/7通过，不代替新.NET运行或容量证据。

当前执行指针为`cache-final-recovery2-batch-3ce2fea4`，来源`c4f5e175…`、8,044个冻结输入，Node96988/监督36680；等待r12v整窗后接续。资源回执只认`cache-final-recovery2-browser-boundary.json`及其对应scope，不沿用旧未执行等待者回执。此批复用原缓存SDK/运行时与发布物，不能验收共享main后来升级的SDK402来源。

同来源独立只读复核`cache_final_boundary_review`已完成，未发现新增Critical、Important或Minor问题：确认工具/Hub新连接/聊天续租的正向对照与两轮停用/恢复、独占Redis用户连接统计、实际L2值与权威库差异对照、仅失败时的受控诊断及生产租户/模型作用域边界。覆盖限制单独登记：请求预取消只运行工具入口；Hub的ConnectionAborted固定为None；聊天监视取消的是推理预算；Dispose五秒为完成后的观察断言，不是可强制中断的SDK关闭硬期限。审查未修改代码、未运行构建/测试或外部进程，不定位此前生成false的根因，也不升级Native或整体能力状态。

CACHE-01按既定“复现/结构证据”验收口径补齐六类风险映射；提交后进程终止明确仅有结构及等价无通知状态证据，没有实际kill窗口注入。两份运维文档进一步补齐Pub/Sub历史消息丢失、单实例工厂合并、项目与SDK默认资源值及恢复后新版本核对，文档子项已验收：Markdown388文件及任务范围diff检查退出0。发现项目没有覆盖SDK默认的两个熔断duration（均为0）与恢复延迟（5秒），明确记录而不擅自改变生产配置；实验2秒延迟不冒充默认值。当前10个主项已验收、4个待全部验收，剩余8个未勾选检查项（CACHE-02、08、13、14）；没有增加任何.NET运行通过项，以下较早计数按其时间保留。

最新接续状态（2026-10-15 10:05）：来源 `3bf6eb81…` 的聚焦 Unit **250/250**、真实 Redis 非零抖动与后台 L2 故障 **2/2** 已实际通过。Native13 实际 **9 通过、4 失败**：两项载荷用例的业务断言已完成，末尾错误地观察已脱敏的原异常文本；另两项租户 Worker 用例在创建 Agent 运行时返回 500。原始失败及清理记录保留。

载荷观察改为日志合同已有的 `ExceptionType=CacheDeserializationLimitException`，保留精确 1 MiB、多一字节、多字节 JSON、条目预算、权威回源及不回填断言；来源 `cb384983…` 的 Native 两项已实际 **2/2 通过、零跳过**。日志 `fullnet-cache-log-reason-native-b3b0231a/runtime-part-1.log`、`complete.json`（TRX SHA `6046e78f8cd36458347405a24498c088e46e21389307b5b909590fd4b975c493`）及整窗回执均已核对。此次复用旧生产发布物，只证明缓存载荷路径，不能证明后述生产修复。

租户运行 500 的根因为运行准入调用 Host 管理模型查询。已分离运行查询：租户仅可读取本租户或共享 Host 模型，Host 运行仅可读取 Host 模型，租户仍由数据执行器绑定；不存在/越界保持 404、禁用保持 422，拒绝发生在预算预留之前。三份生产文件、四行 Unit 回归和双库两个真实 API 回归已编写并经静态安全审查，无重要问题；**尚待本批运行验收**。新来源 `4d4896c7…`、8,042 个输入，`cache-model-scope-batch-457211c6` 已开始 Unit259，随后双库2、完整架构与 AOT 分析；`cache-model-scope-native-queue-9e0effa6` 只在前批实际通过且终态清理后，执行新发布与 Native40。旧 813 API/Worker 不包含此次生产修复，不能复用为修复后证据；主分支 SDK402/runtime13 和容量均未因此验收。原排队截止 13:01 保持不变。

本次另按既有实际证据集中收口 CACHE-03、06、10 及 CACHE-14 的 Vue 子项，未重复构建或运行。`node artifacts/cache-hardening/verify-cache-contract-closures.mjs` 核对 Unit245、真实 Redis8、双库 Grid2 的原始 TRX、74 次生产输入比对、六类 promtool 规则结果及两库各3个真实页面报告；唯一生产差异为恢复阈值 XML 注释。Grid 继续按已批准的 S2 迟到回填/TTL 合同验收，未引入版本拒绝协议或自提交起算的绝对陈旧 SLA；规则计算通过也不证明生产告警送达。新抖动/Native 边界、当前主分支 SDK 升级和容量不在此次收口范围。

Unit259 已实际全部通过、零跳过，构建0警告/错误；TRX SHA `b94d8a2ff14bbff24426bddb4befc8e770e80ee8127419425f6f552b0c2a6726`。`node artifacts/cache-hardening/verify-cache-payload-closure.mjs` 核对 Unit259、上述 Native2及95个不变缓存/Settings/依赖输入后，收口 CACHE-05；当前14个必做主项8个已验收、6个待全部验收，剩余12个未勾选检查项。新双库运行、架构/AOT与修复后完整Native尚待后续实际结果。

10:20更新：模型作用域双库API2/2、Architecture235/235、AOT分析退出0均已完成；三份原始TRX与零失败/跳过、正式锁释放由 `cache-model-scope-jit-completed-proof.json` 核对。双库TRX SHA `e9b7e3756b89940eca64f008a8ee602247f792412c3796b5e6dcc2a6f4d28d9d`，架构SHA `d3cf1ab6faf76ac216e7d5abe4eaf6219593deae51cd48ca50813196adc444f6`。Root治理修正三条旧断言后84/84，矩阵7/7；原81成功/3失败日志保留。

Normal寿命null以当前Unit依赖重验，两个指定反例与负缓存Fail-Safe拒绝均正确，Set/工厂两正例实际降级及保留期退出；构建小入口而未重建业务项目。`cache-edge-jit-completed-proof.json`将该范围与31个不变缓存文件、Unit250及非零抖动真实Redis2关联后收口CACHE-04。当前9个主项已验收、5个待全部验收，剩余10个未勾选检查项；不外推null全故障组合或容量。同一Integration构建已复用，1248身份分区完整；新API/Worker发布及完整Native40入口 `fullnet-cache-native-current-cf258d5c` 已开始API发布，尚未通过原生门禁。

## 已接受的功能与运行范围

| 范围 | 实际结果 | 证据 |
| --- | --- | --- |
| 当前安全依赖来源的缓存/后台授权 Unit | 245/245，零失败/跳过；来源 `0504174f…`，三项实际 NuGet 安全闭包固定 10.0.13 | `artifacts/cache-hardening/cache-security-unit-current-completed-proof.json` |
| 缓存 Unit | 225/225，零失败/跳过 | `artifacts/cache-hardening/concentrated-fast-completed-proof.json` |
| 架构与 AOT 静态分析 | Architecture 235/235；分析退出 0 | 同上；不替代原生运行 |
| 真实缓存影响集 | 28 个唯一 UID，零接受失败/跳过；原 20 项加当前 Redis 故障 8 项分批验收，两库各 4 项 | `artifacts/cache-hardening/redis-recovery-batch-completed-proof.json` |
| Grid 双库迟到回填与后台刷新故障 | Grid 2/2，后台 2/2；四个唯一 UID 分两来源验收，只差 Grid 夹具所有者配置 | `artifacts/cache-hardening/cache-short-window-completed-proof.json` |
| Redis 批量与断连专项 | 两批 256 目标、SDK 峰值四槽、远端删除 256；修订后的长断连1/1通过、恢复/实际远端失效成立；原失败保留，两来源分批接受 | 同上及 `artifacts/cache-hardening/cache-long-disconnect-completed-proof.json`；严格恢复容量未验收 |
| Redis 生产准入与命名空间定义 | 端点变体 17 项实际通过；同范围传播及跨应用/环境标签隔离 2 项实际通过，相关生产输入未变化；两阶段切换与回退门禁已定义 | `artifacts/cache-hardening/cache-isolation-closure-completed-proof.json`；不代表生产物理隔离或实际滚动切换通过 |
| API/Worker Native | 两个新原生发布物；完整核心 38/38，零失败/跳过 | `artifacts/cache-hardening/native-vue-completed-proof.json` |
| Vue 真实栈 | SQL Server/MySQL 各 3/3，总计 6/6，零跳过/意外失败/重试通过 | 同上 |

功能覆盖统一批量失效的整批预检、身份去重、字节/数量/并发/接纳预算、提交后独立清理；安全权威读取、S0 禁用 L1、显式 S2 降级及不支持版本复核协议的拒绝；载荷与解析输入保护；SDK 指标、管理目录及 Vue 状态/权限处理。未由这些结果覆盖的场景继续在[活动计划](../superpowers/plans/2026-10-03-cache-hardening.md)保留。

性能与当前 Native/Vue 来源为 `6a13122fd6c427d1945d5548d6f87b2259eb0b0ca7e630abe561dab091b73b28`，7,997 个冻结输入。Unit 原来源 `7aebeff2…` 与后续来源的复用边界已单独核对；不声称整个共享工作区都通过验收。本任务没有提交、推送或部署。

## 本地性能实验

入口为 `node artifacts/cache-hardening/run-cache-performance-owned.mjs`；父进程持有正式工作区/同机重型锁，子进程执行，退出后等待 Ryuk 自然回收并核对进程/容器。正式结果为 `artifacts/cache-hardening/cache-performance-runtime-3545c53b`，原始四模式/十样本全部结束，`childExitCode=0`、`cleanupConfirmed=true`，锁已释放。

采用 Windows Release JIT、SDK 10.0.401、两个 API 进程、真实 Redis 与数据库。硬件为 i7-12700H、14 核/20 逻辑处理器、物理内存 68,450,914,304 字节；负载生成器、数据库、Redis 和应用共享这台开发机。应用配置 `DOTNET_PROCESSOR_COUNT=2`、GC 预算 1 GiB，不能将逻辑处理器配置当成 Windows CPU 硬配额；k6/Collector 容器分别配置 1 CPU、512/384 MiB。

同一候选代码比较 C0 权威读取与 S2 缓存配置，非历史版本升级 A/B。单一已验证 Host 用户读取 `identity.users` Grid 偏好；8 VU、5 秒预热、每模式两个 20 秒样本；缓存模式另有 15 秒自有 Redis `CLIENT PAUSE 5000 ALL` 实验。所有样本均保留，没有剔除较差结果。

| 数据库 | 模式/样本 | 请求数 | P95 / P99（ms） | Grid SQL 次数 |
| --- | --- | ---: | ---: | ---: |
| SQL Server | C0 / 1 | 7,744 | 18.60 / 44.62 | 7,744 |
| SQL Server | C0 / 2 | 8,662 | 9.68 / 12.28 | 8,662 |
| SQL Server | S2 / 1 | 10,091 | 7.81 / 10.56 | 0 |
| SQL Server | S2 / 2 | 10,286 | 6.89 / 10.83 | 2 |
| SQL Server | S2 / Redis 延迟恢复 | 6,506 | 5.68 / 7.78 | 4 |
| MySQL | C0 / 1 | 8,512 | 11.17 / 15.36 | 8,512 |
| MySQL | C0 / 2 | 9,509 | 7.83 / 9.46 | 9,509 |
| MySQL | S2 / 1 | 7,277 | 52.34 / 111.03 | 0 |
| MySQL | S2 / 2 | 10,079 | 7.33 / 9.48 | 2 |
| MySQL | S2 / Redis 延迟恢复 | 5,886 | 7.65 / 29.86 | 4 |

合计 84,552 请求，HTTP/业务响应错误率均为零。C0 的稳定 Grid 查询计数与请求数一致；S2 两个稳态样本每库共两次 Grid 查询，证明本实验的回源减少。MySQL 首个缓存样本延迟明显恶化，不能据第二样本或 SQL Server 结果宣称普遍提速；需在遥测归属修正、预热/环境影响核对后再针对该问题复验，不重跑无关门禁。

写请求发起至第二节点观察到新版本分别为 SQL Server 70.01 ms、MySQL 65.09 ms，包含 PUT 写请求耗时；没有测得精确事务提交时间、全节点确认或生产失效 SLA。API 目录实际返回七个新增策略/运行字段，且四模式配置与观察生命周期核对通过；这不替代新增字段的 OpenAPI schema 验收。

`node artifacts/cache-hardening/summarize-cache-performance.mjs` 退出 0，核对十份原始 k6 摘要、前后指标摘要、全部冻结输入、同库 A/B API 程序集一致与实际响应字段；输出 `local-experiment-summary.json`。`node artifacts/cache-hardening/finalize-cache-acceptance.mjs` 退出 0，重新核对 Native/Vue/性能结果，不重跑上述测试，状态为 `queued-batches-completed-and-reverified`、`completePlan=false`。

## 指标和验收边界

- 实际服务标签为 `unknown_service:dotnet`，主 API 与 Worker 共享 `cache-node-one`；不能拆分的 API 总 SQL 和主节点 Runtime/连接资源记为未知。Grid 查询使用专属稳定 StatementName，Runtime/连接指标只采用没有 Worker 混入的第二 API 节点；原始指标保留。
- HTTP 在途值为异步导出的节点 gauge 采样和，不能当作同时发生的精确峰值，更不能以 VU 替代真实在途。数据库/Redis Docker 资源、P50/P95/P99、第二 API 分配与连接指标保留在原始样本及汇总中。
- Redis 命令延迟不能证明长断连、通知丢失重放、并发恢复队列硬上限、批量压力或 Soak；零目标请求错误也不能作为整个后台栈零错误的证明。
- 其他任务的 `artifacts/r11u` 双库 OpenAPI 使用较早缓存 DTO，只有原九字段，不能验收本轮七个新增字段；双方已确认保留当前缓存合同及生成客户端增量。
- 剩余范围为真实权威数据库故障/工具/后台/长连接准入，真实迟到回填收敛，尚未覆盖的后台/L2 组合和长断连资源/告警，实际操作审计/失败流程、当前字段 OpenAPI 及最终复核。按完整功能范围集中处理，见活动计划。

早期登录、清理、SDK 缺失和镜像拉取失败记录均保留，未累计为成功结果。代际键和分布式锁仍为条件评估；本记录没有 2K/5K/10K、容量、全项目验收或生产 SLO 结论。

## 后续修订状态

已在同一冻结源码和既有构建基础上修订实验夹具：为两个 API 与 Worker 设置各自遥测服务/实例身份，改用 30 秒同负载请求预热，并在负载前核对实际 OpenAPI 的三个缓存操作、五个 schema、新字段类型和兼容可省略性。集合顺序与说明文案不当成线协议差异；旧九字段文档、预算类型变化及新增必填均有拒绝用例。关联 Node 夹具 13/13、零失败/跳过，冻结输入/实际导入检查通过；未修改生产实现。

修订后的真实运行尚未验收。Docker 已按原授权恢复；此前正式入口拒绝另一任务 PID 83696 的遗留重型锁，缓存没有清理或抢占它。原拥有者恢复原组后确认失败终态、宿主退出、资源清理及锁释放；该失败仅作为资源交接边界，不计为缓存通过。修订实验继续经正式互斥入口执行，旧入口/清单另存 `artifacts/cache-hardening/performance-v1-preserved`，上述原始十样本与限制仍保留。

告警出口复核发现失效延迟查询的 `_ms_bucket` 与实际 `_milliseconds_bucket` 不匹配。回归先复现指定失败，修正规则后 `node --test tests/deployment/observability-contract.test.mjs` 10/10、零失败/跳过。运行说明同时补齐新指标、实际观察生命周期与 Realtime 连接配置名；六类告警的 promtool 持有/触发/恢复及不误报用例已经接入正式入口，将在后续同一重型批次中对独立冻结的告警配置执行。此处只有出口契约/文档检查通过，promtool、目标平台采集与通知送达仍未验收；.NET 已发布的 Instrument 名与已通过 Native/Vue 结果保持原范围。

首次修订运行 `cache-performance-runtime-b427e358` 在负载启动前被严格 OpenAPI 比对拒绝：实际三个 nullable double 字段包含数字字符串 `pattern`，手工追加的客户端快照缺少该约束。原始文档、失败和自然清理记录保留；没有预热或负载样本，不累计为性能通过。已将 `jitterSeconds`、`negativeDurationSeconds`、`failSafeMaxDurationSeconds` 的实际约束补入当前合同，并单独冻结期望投影，未修改 7,997 项生产/构建冻结输入。

回归进一步复现客户端生成器对 float/double 字符串忽略 `pattern`。最小修复执行 schema 模式及有限数值检查，保留合法字符串原表示，不转换金额或普通字符串；所有生成文件由正式入口重新生成。`pnpm --filter @fullnet/client-contracts test` 276/276、`pnpm test:openapi` 211/211，均零失败/跳过；客户端 TypeScript 构建退出 0，生成产物检查无漂移。实验夹具追加真实文档阳性与删约束反例后 14/14 通过。

修正后的批次 `cache-performance-runtime-16e6ee2e` 已完成，子进程退出 0、自有资源清理确认、两锁正常释放。集中收尾 `cache-acceptance-finalization-c8b25fa1` 于 21:09:50（Asia/Shanghai）重新核对对应来源的 Native/Vue、十份测量摘要及告警证据，状态 `queued-batches-completed-and-reverified`，`completePlan=false`。汇总 SHA 为 `2ddd682bb9240b1230429c9fa9d9857adea9108c5f743c7c60125cb02cd141db`；没有重新发布未变的缓存 Native 产物。

## 修订实验结果与当前增量

四组各执行 30 秒真实 8 VU 预热，合计 37,582 个预热请求单独保存，未混入下表。三个服务/实例组合在每份测量指标中实际出现并核对：`Full.NET.Host.Api/cache-node-one`、`Full.NET.Host.Api/cache-node-two`、`Full.NET.Host.Worker/cache-worker-one`。API SQL、资源和分配汇总明确排除 Worker；各组真实 OpenAPI 的三个缓存操作及五个 schema 全部匹配，包括七个新增字段与三个浮点时长模式。

| 数据库 | 模式/样本 | 请求数 | P95 / P99（ms） | Grid SQL 次数 |
| --- | --- | ---: | ---: | ---: |
| SQL Server | C0 / 1 | 5,630 | 28.38 / 43.96 | 5,630 |
| SQL Server | C0 / 2 | 5,720 | 27.60 / 47.71 | 5,720 |
| SQL Server | S2 / 1 | 6,657 | 23.09 / 35.90 | 1 |
| SQL Server | S2 / 2 | 6,234 | 24.76 / 36.17 | 2 |
| SQL Server | S2 / Redis 延迟恢复 | 4,546 | 17.29 / 24.09 | 4 |
| MySQL | C0 / 1 | 5,598 | 27.10 / 45.85 | 5,598 |
| MySQL | C0 / 2 | 5,016 | 35.91 / 51.34 | 5,016 |
| MySQL | S2 / 1 | 9,965 | 7.22 / 9.39 | 1 |
| MySQL | S2 / 2 | 10,086 | 6.87 / 10.23 | 1 |
| MySQL | S2 / Redis 延迟恢复 | 6,237 | 7.91 / 11.51 | 4 |

合计 65,689 个测量请求，HTTP/业务响应错误率均为零。C0 稳态 21,964 请求对应 21,964 次 Grid SQL；S2 稳态 32,942 请求对应 5 次 Grid SQL。本轮两库缓存稳态的尾延迟低于对应权威读取样本，没有重现旧实验的 MySQL 首样本异常；身份、预热及机器状态均有变化，不能据此确定旧异常根因或证明生产普遍提速。写请求开始至第二节点观测分别为 157.56 / 65.09 ms，仍包含写请求，不是精确提交后失效时间。

固定 Prometheus 镜像的 `promtool test rules tests/deployment/cache-alerts.promtest.yaml` 退出 0，覆盖六类告警的持有/触发/恢复，以及正常延迟、Fail-Safe 和关闭事件不误报；原日志 SHA `8289a4919870ab5b28f5e90a86b4af3cdbd8c244516c02fca90d6e3f289a6711`。告警配置独立冻结并核对摘要；这证明规则计算，未证明生产采集链或通知送达。8 VU、短暂停命令及采样 gauge 的原边界继续有效，容量仍为 `Capacity-not-verified`。

后续安全链路复核发现后台绑定只核对会话及作用域，没有读取租户当前活动状态。新增 12 条回归、类内共 18 项；独立旧实现 `background-authority-frozen-9fbcf11d` 的正式构建通过，运行 18 项中 10 通过、8 项指定失败，零跳过/中止，保留 `background-authority-unit-58cbd325`。反例覆盖 Refresh/OIDC 停用后继续接受、权威失败后继续接受、读前/读中取消未传播，不把指定失败当生产验收通过。

补丁在共用 `BackgroundSessionBindingValidator` 通过既有 `IIdentityActiveTenantDirectory` 重查活动状态，未知/停用拒绝、取消传播、Host 不查租户、会话无效时不查租户；不新增跨模块 SQL、缓存或迁移。独立副本 `background-authority-green-3c26289d` 的来源为 `e06044d994ad22c678e36137ad21010060d66cddfe4592447a0fbf831ec5a6ae`，在已结束的反例副本基础上仅替换当前生产补丁和矩阵，保留原副本完整。18/18 初验通过，继而集中缓存/后台授权 245/245、零失败/跳过，结果 `background-authority-unit-37ba596f`；Architecture 235/235、零失败/跳过，AOT 分析退出 0 并恢复 JIT 还原图。集中入口 `background-authority-impact-79d33196` 于 21:34:22（Asia/Shanghai）完成并释放正常资源。

`node artifacts/cache-hardening/verify-background-authority-proof.mjs` 退出 0，复核两副本完整输入、当前生产/测试文件、原始三份 TRX 的摘要及 18/18/245 唯一 UID、实际计数和架构/AOT 步骤退出码，输出 `background-authority-completed-proof.json`。安全专项只读复核未发现重要或关键问题，确认超级管理员权限分支不能绕过绑定校验，API/Worker 和官方 Minimal 的活动目录注册已存在。矩阵/Markdown 16 项结构检查通过，本任务差异检查无空白错误。

首轮集中包装 `background-authority-impact-43b192a9` 重复申请自有工作区锁，245 项尚未开始；核对仅有自有 Node 等待者、无 .NET 子进程后停止该等待者，父入口正常 finally 释放自有锁，未手工删除或抢占外来锁。修正为各正式子入口自行持锁后集中顺序执行；原零执行失败记录保留，不计入成功结果。

原 Native/Vue/性能记录继续绑定来源 `6a13122f…`。新增后台安全路径已有上述 Unit、架构和 AOT 分析证据，尚未通过对应原生运行及真实双库故障验收；不能将旧 Native 38/38 外推到本次增量。完整计划与生产容量状态仍未升级。

## 后台准入真实验证批次

任务快照 `cache-background-native-20261014`，开工 HEAD `caa3352f1604c0bf7237193819b6117ebbeca603`，分支 `main`。本批只新增测试及对应矩阵计数，不改生产行为。`CacheBackgroundAuthorityTests` 四项使用真实 Refresh/OIDC 会话、SQL Server/MySQL 及独立 API 实例，验证同一后台验证器两轮停用/恢复、租户权威表查询失败、恢复后重新接受，并以会话表仍正常的 Host 验证排除整体查询失败假阳性。租户表故障仅作用于独立测试库且在 finally 恢复；保留最后活动租户保护，不以撤销会话代替停用。

`NativeWorkerTenantAuthorityE2ETests` 两项通过实际原生 API 登录、创建租户运行及健康 Host 运行，提交停用后启动实际原生 Worker，检查 `authorization_required`、预算 `cancelled`、拒绝事件及真实模型端请求。健康 Host 任务须完成并结算成功，模型端仅收到该对照。用例已编译，尚未实跑，不能据预期断言称为通过。

独立副本 `background-runtime-frozen-ac52493b` 的来源为 `b1f0557bb99a84bdc0e316021397de92f3c5b235dbaff6b06fc75aea7dd493eb`，8,038 个冻结输入。正式 `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo` 退出 0，零警告/错误，耗时 3 分 3 秒；发现双库用例四项。完整 Native 发现复用同一构建，40 UID 精确由原核心 38 项和新增两项构成，方法完整分区无遗漏/重复。`node artifacts/cache-hardening/verify-background-runtime-preparation-proof.mjs` 退出 0，核对冻结输入、当前拥有文件、构建退出、实际发现摘要与分区；输出 `background-runtime-preparation-completed-proof.json`。矩阵/Markdown 结构检查与本任务空白差异检查另行记录实际结果。

集中运行队列 `background-runtime-batch-ae1fd0b7` 先等待 AI R11w 与 R11x 两组完整终态和资源释放，协调器不持有工作区或 heavy 锁；各正式子入口自行持锁，避免重复申请。计划先实跑四项新准入及三项既有租户审计提交/回滚，再用新源码发布 API/Worker 并执行完整 Native 40 项。既有三项租户审计不替代缓存管理操作审计的剩余范围。原 Native/Vue 完成凭证及指针保持独立；本次为 **Build-verified / runtime pending**，真实数据库与新 Native 均未升级验收状态。

缓存控制面另新增 `CachePolicyGovernanceTests` 双库两项，沿用真实 API 权限及 B1 数据库写入，健康执行调用当前批量失效实现，仅指定异常分支由测试装饰器注入。覆盖读写权限分离、租户上下文拒绝、三类非法请求零执行、四目标当前节点失效、实际成功/失败审计及失败后恢复；检查 ProblemDetails 和审计详情不泄漏令牌、私有键或注入错误。它不证明 Redis 网络故障或审计数据库不可用时的行为。

控制面独立副本 `cache-policy-governance-frozen-3fb22b36`，来源 `db06babb08962df0ddd3bb5db90741f1e33adc24f9c339520006e3ddcf14258e`。在已结束的后台候选构建基础上复制，只增加新测试和相应矩阵；8,039 个冻结输入、生产闭包与 `b1f0557b…` 完全一致。正式 `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo` 退出 0、零警告/错误，耗时 4 分 0 秒，双库发现两项。准备凭证由 `node artifacts/cache-hardening/verify-cache-policy-governance-preparation-proof.mjs` 核对，状态仅 **Build-verified / runtime pending**。

两项控制面原准备方案合计九项 JIT 加完整 Native 40 项，尚未取得实跑通过结果。下述新来源和集中运行状态替代旧队列的活动状态，旧来源保留为历史准备证据。

## 2026-10-15 安全依赖修订与集中实跑

完整发现确认 Integration 实际 1240 项、infrastructure 214 项，矩阵原少计两项已经校正，其余分片数量未改。后续还原被 PKCS 及 SignalR Common 安全告警拒绝，现固定两包为 10.0.13，同时将其最小 Options/DependencyInjection.Abstractions 依赖闭包固定到 10.0.13。依据微软 [PKCS 高危公告](https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx)、[PKCS 另一公告](https://github.com/dotnet/runtime/security/advisories/GHSA-2fmv-rv2c-59x7) 和 [SignalR Common 公告](https://github.com/dotnet/aspnetcore/security/advisories/GHSA-334f-vvp4-7g6p) 修订 NuGet 图；API/Worker 的 SignalR Common 来自共享框架，本批没有证明该框架或 SDK 已升级。

控制面测试已加强为精确比对四个实际目标的键/tag/作用域，并由装饰器调用现有批量实现。专项复核未发现新的重要问题。旧安全告警、构建内存异常和进程中断证据保留；中断没有退出回执，不推断为通过。只在核对自身原进程不在、无活动消费者及 PID/token 后归档自有普通锁，未处理外来重型锁。

最终来源 `813f86e57e9c9d06f2b94c14b2a358870f8fc407020c71c7f2a0781aa6f7550b`，副本 `cache-security-runtime-frozen-a35647b2`，8,039 个冻结输入。正式 Release 构建退出 0、零警告/错误、4m42.78s，普通 CLR 预算 2 GiB、最多两构建节点、禁用共享编译器。九项 JIT、1240 项完整分片及 Native 40 项身份检查均通过，Native 发现复用同次构建。`node artifacts/cache-hardening/verify-cache-security-preparation-proof.mjs` 退出 0，结果 `cache-security-preparation-completed-proof.json` 核对完整冻结输入及实际 NuGet 资产图：Integration 四个固定包均为 10.0.13，API/Worker PKCS 为 10.0.13；未声明数据库或 Native 实跑通过。

集中实跑 `node artifacts/cache-hardening/run-cache-security-runtime-after-ai.mjs` 的九项真实双库准入/控制面/审计已完成：正式运行退出 0、9/9、零失败/跳过，5m15.489s。结果 `cache-security-integration-runtime-dfad760f`，TRX SHA `1061d519e04b2a0d92a36de68e27f07a903487215f914694e5534260d67f9e84`；九个实际 UID 与发现集精确相等，正式前后输入/输出守卫通过，新增容器为空，自有锁正常释放。随后自动进入新鲜 API/Worker 发布和完整 Native 40 项，结果 `fullnet-cache-native-current-cb664032`，四个方法完整分区各十项，尚未声明原生运行通过。

整个批次为 `cache-security-runtime-batch-00c1cb6d`、指针 `cache-security-runtime-batch-path.json`；协调器不持锁，各正式子入口顺序持锁。前置 AI 原组的中断释放回执仅用于资源交接，原组没有完整通过声明。当前 **JIT runtime verified / Native running**；`node --test tests/testing/test-matrix.test.mjs` 7/7、Markdown 编码检查 9/9 通过，本任务 `git diff --check` 退出 0。容量及共享框架安全升级状态不变。

原生编译期间新增下一批四个回归用例，生产实现未变：真实双库 Grid 服务/数据库/Redis 的提交后迟到回填与无通知 TTL 收敛两项，以及后台工厂成功时 L2 写失败/读写同时失败的实际 ACL 对照两项。Root 矩阵净增四项，完整发现预算 1244、基础设施 218；当前 Native 仍按原 `813f86e5…` 冻结输入和 40 项计划执行，不变更在跑来源。

快照 `cache-real-redis-remaining-20261015`，开工 HEAD `2a470063f7965b7587be67a8a92b6cc24ca3291b`。独立来源 `e4c8f8df5e6b97896a832a04c7bde2947ea0aa972fcb7e905abf5411098e1af6`，8,040 个冻结输入，副本 `cache-remaining-frozen-04f0b6f9`，派生自上批且只替换这三个测试/矩阵文件。首次准备在编译前因 Windows PATH 别名处理错误拒绝（`spawn git ENOENT`），保留零编译失败记录；保存原 PATH 后修正入口。又发现仅复制清单文件的副本缺独立 Git，初次编译不作为合格复用凭证；`complete-cache-remaining-preparation.mjs` 在锁外等待自身初次构建退出，再补齐已封存副本的私有 Git 元数据、按完整输入指纹正式核验四项和全量分片。当前尚未声明这些门禁通过。

四项运行协调器 `cache-remaining-runtime-batch-056eb801`，指针 `cache-remaining-runtime-batch-path.json`，仅在最终准备凭证与当前 Native 整组完成/清理后启动正式 heavy 入口；此前没有执行进程或重型锁。另一任务已同意该固定短窗后移交 AI MySQL 补验，双方不争抢阶段空隙。运行失败同样等待自身进程/容器自然退出，并核对自有锁；清理不能确认时拒绝提供成功释放回执，外来资源不处理。四项实跑、实际 UID/TRX 和最终清理尚未声明通过，不另行重复 Native 发布。

初次冷构建 `cache-remaining-integration-preparation-787e7d94` 实际退出 1，`GenerateDepsFile` 的 `FileNotFoundException` 指向全局 NuGet `Elasticsearch.Net.dll`；后续只读确认整个 `C:/Users/Administrator/.nuget/packages` 目录不存在，原因未知，未归因其他任务。无新增四项发现/运行结果。原进程退出、自有普通锁释放后，副本已补齐独立 Git，正式入口及分片复用入口统一改为本任务私有 NuGet 目录、`RestoreForce=true`，同源恢复准备 `cache-remaining-sealed-preparation-7a522bb4` 重新还原/构建。此前失败完整保留，未禁用审计或降低警告合同；共享缓存、SDK、外来资源和 Linux Native 来源未修改。

## 四项首轮实际结果及原生缺包失败

安全依赖升级后的快速回归已补验：`node artifacts/cache-hardening/verify-cache-security-unit-current.mjs` 退出 0，来源 `0504174f…`，Unit Release 构建零警告/错误、1m38.87s，实际缓存/后台授权 245/245、零失败/跳过、54.138s；发现身份与原接受的 245 项精确相等，正式前后冻结输入和输出检查通过。TRX SHA `ef2581731303665d2e9adb8f0f99db62efb7fe1ed01de7600ba6db8e6d9b2573`。`node artifacts/cache-hardening/verify-cache-security-unit-current-proof.mjs` 退出 0，独立核对结果摘要、身份及 Unit 实际 PKCS/Options/DependencyInjection.Abstractions 三包均为 10.0.13，并确认已准备的 Integration 消费目录字节仍匹配原正式构建记录。未触及共享 SDK/全局缓存，不启动数据库/Redis或占重型锁，不宣称共享框架或 Native 升级通过；Grid2/Native40 自动接续仍在锁外等待约定整组边界。

Grid 修正后的准备已完成：正式 `dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo -maxcpucount:2 -p:UseSharedCompilation=false -p:RestoreForce=true` 退出 0、零警告/错误，4m59.74s；四项完整发现和单独双库 Grid 两项发现均退出 0，后者复用同一次构建；完整 1244 分区无遗漏/重复。`node artifacts/cache-hardening/verify-grid-refill-retry-proof.mjs` 退出 0，准备凭证 `grid-refill-retry-preparation-completed-proof.json` 核对所有输入、真实发现摘要和唯一身份、分区日志摘要、仅 `OwnerModule` 一行变化及当前拥有文件。它不证明实际回填收敛。自动接续入口 `run-cache-final-recovery-after-ai.mjs` 已通过语法检查并启动；各正式子入口自行持锁，协调器不持锁。仅在两前置整组实际终态、收集成功且容器退出后执行 Grid2 和私有 Linux 缓存 Native40，任一前置失败拒绝自动接管。后台两项与 Grid 两项最终以实际 UID 精确并集登记为两个来源的分批结果，不能改写首轮 2/4 失败结果。

来源 `e4c8f8df…` 正式 Release 构建退出 0、零警告/错误，27m28.04s；完整 1244 分片无遗漏/重复、四项唯一身份及全部 8,040 个冻结输入检查通过，准备 `cache-remaining-sealed-preparation-7a522bb4` 完成。直接正式运行 `node artifacts/cache-hardening/verify-cache-remaining-integration.mjs --execute` 的结果 `cache-remaining-integration-runtime-c3ef6cf5` 为 2/4 通过、2 失败、零跳过，4m13.787s；TRX SHA `c836082920747d76626c6c3956c3e61ff21d9cee7bdcabfc463b28a0ec4a3687`。两项后台工厂成功同时 L2 写失败/读写同时失败场景通过；Grid 双库因夹具缺所有者配置在启动期被拒绝，尚未验证回填收敛。Root 仅新增该所有者字段，原副本不改；新来源 `0504174f54c5f0ddb777391ba0f2e7a3a1086a7ed03b659a7430fd08a93ca92a`，暖副本 `grid-refill-retry-frozen-e4f4addc` 正在正式构建/发现及分片准备，后续只复验双库两项，不重复上述两项已通过范围。

原生 API 发布 `fullnet-cache-native-current-cb664032` 于 01:28:49（Asia/Shanghai）退出 1，错误为 `DirectoryNotFoundException`，缺失 `/root/.nuget/packages/microsoft.netcore.app.runtime.nativeaot.linux-x64/10.0.11/runtimes/linux-x64/lib/net10.0/System.ComponentModel.EventBasedAsync.dll`。Worker/40 项没有开始。本次并非 OOM 证据。实际挂载复用了宿主全局 NuGet，纠正此前将 Linux 发布视为独立包缓存的判断；尚未确认文件缺失的外部原因。派生发布/运行入口已改为每批私有 Linux 包目录，并在发布前校验和记录该运行时 DLL 摘要；语法检查退出 0，尚未复验发布。

`node artifacts/cache-hardening/record-cache-failed-window-release.mjs` 退出 0，回执 `cache-failed-window-release.json` 核对旧 Native/实际四项的所有已记录宿主和阶段进程不在、自有容器及锁均释放、两编译中间卷无活动消费者并保留，只存在三个常驻容器，外来资源未处理。该失败加清理边界已移交另一任务的 AI MySQL 补验，不要求其等待 Native 变绿，也不计失败组通过；后续 Grid/Native 重型恢复等待其完整窗口释放。容量状态仍未升级。

## 批量压力与持续断连专项准备

2026-10-15 既有控制面证据收口：`node artifacts/cache-hardening/verify-cache-governance-closure-proof.mjs` 退出 0。复核来源 `813f86e5…` 九项实际 TRX 中控制面双库两项的唯一身份与 Passed、原始摘要和清理状态；56 个后端拥有输入与当前工作区一致，缓存页面/API/测试和真实栈用例四个输入与来源 `6a13122f…` 一致，并重读两库各三项的原始 Playwright 报告、零跳过/意外失败/flaky 及摘要。关闭 CACHE-11 最后一项权限/参数/B1审计/失败提示合同，原清单未核销为 11 个主项、30 个子项；没有重跑用例，也没有将注入的执行器失败外推为网络或审计库故障。当前主仓库新版 SDK/runtime 的整体验收继续独立保留。

新增两项 `CacheRedisResourceBudgetTests`，开工 HEAD `f7e0632afabc29a5eb341f9f0e43fb36072b616a`、快照 `cache-redis-resource-batch-20261015`。独立来源 `0bee9fe3493eb055a2ee8d1c962674a8f4fbd27a1f842048c92a3423cdda258e`、8,041 个冻结输入，父来源 `0504174f…`；只增加新测试文件并将 infrastructure/full 数量各增加两项，生产文件未变。真实场景计划覆盖双批次 256 目标的共同四槽、满接纳拒绝、远端消息/L1/冷 L2，以及 128 目标的 30 秒独占用户断连/发布拒绝、TTL 收敛、恢复数量/内存观测和实际重新订阅。它没有数据库业务调用，不用提供程序名称冒充双库覆盖；测试进程内存观测及 SDK 恢复日志不能证明独占缓存内存或严格 16 项硬上限。

正式 Release 构建退出 0、零警告/错误，4m57.39s。首包装因错误使用 SQL Server/MySQL 发现门禁拒绝，两个发现命令本身退出 0，没有真实用例执行；失败 `cache-resource-preparation-40f2618b` 原样保留。修正派生入口后，`complete-cache-resource-preparation.mjs` 退出 0，复用成功构建核对两个精确类型/方法/UID，并完成 1246 项完整分区无遗漏/重复。`node artifacts/cache-hardening/verify-cache-resource-preparation-proof.mjs` 退出 0，核对全量输入、当前拥有文件、仅两项计数变化、实际构建/发现/分区及日志摘要；证明 `cache-resource-preparation-completed-proof.json` 明确 actualRedisExecuted=false。矩阵结构检查 7/7。

`run-cache-resource-after-core.mjs` 已通过语法检查并启动，指针 `cache-resource-runtime-batch-path.json`；只在现有缓存 Grid2/Native40 整组终态/资源释放后正常申请正式子入口锁，协调器保持锁外，前置失败则保留并停止接管。当前 **Build-verified / Redis runtime pending**，未宣称实际资源预算或容量验收通过。
