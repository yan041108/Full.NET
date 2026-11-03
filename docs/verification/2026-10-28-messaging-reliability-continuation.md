# Messaging 连续整改本机记录（2026-10-28）

## 当前范围与同步保护

续办基线为 `main / 75be6fc0e17433f9c59c42553093dacd479ef9d1`，快照 `messaging-resume-20261028`。同步及冲突处理已完成，索引无未解决冲突。本任务 16 个新增文件在恢复时与 stash `b74742b9a31f9c5db7bb6e60498345a5653ac531` 的未跟踪文件树归一化比对一致；随后按本任务继续修改。没有应用或删除整个 stash，没有重置索引。其他模块保存独立摘要；已观察到 AI、Workflow 与命名契约等并行输入变化，未覆盖或回滚这些内容。

此前定向验收已完成；本轮续办以最新小节为准。生产容量及未覆盖运行边界仍未认证。以下通过项均为本机实际结果，历史机器的临时 TRX 未在此重新核验。正式切流、影子采集、CDC 观测默认关闭，Worker 默认 `LegacyPolling`，保持受约束 Pilot / `Capacity-not-verified`。没有提交、推送、合并或修改生产配置，没有执行仓库全量测试。2026-10-29 的新增实现、独立静态复核和最终验收见 2026-10-29 节；其余日期段落保留各自历史输入。

2026-10-28 收尾快照：`main / 75be6fc0e17433f9c59c42553093dacd479ef9d1`，无未解决冲突，原stash仍保留。本任务已跟踪差异的`git diff --check`及26个新增文件的空白检查通过；部署/矩阵18/18再次通过。早期967个文件摘要中无缺失，4项为本任务确认的共享文件变化，另观察到28项其他文件变化，未回滚或覆盖。完整状态和摘要保存在`working-tree-check.json`。

## 2026-10-30：预取8长稳与自动TTL续办

快照 `messaging-prefetch8-native-soak-ttl-20261030`，继续使用原 `main / 75be6fc0e17433f9c59c42553093dacd479ef9d1` 和既有两份 Linux Native 应用，原 stash 保留。仅扩展 Messaging 专属测试、验收入口和相关文档，没有修改生产默认、重发应用或运行全量测试。

| 本轮实际验收 | 结果 |
| --- | --- |
| 预取8 Native 短测 | `component-native-prefetch8-smoke-sozj3U` **2/2、零跳过，6m19.011s**；每库60秒、60条，原两类物理故障和精确排空检查通过 |
| 预取8 Native 长稳 | `component-native-prefetch8-soak-FZqVJS` **2/2、零失败/跳过，2h03m51.716s**；每库满3600秒、3600条，最终全部处理、零积压/持久DLQ/重复业务行/新增死锁 |
| 短测准入及配置守卫 | 长跑前入口检查 **8/8**；短测与长跑实际899项运行输入相同，准入明确绑定上述短测，不能用默认1或5/10档短测替代 |
| TTL 聚焦编译与入口 | Integration 聚焦编译 **30.43s、0警告/错误**，编译前后源码一致；补充拒绝缺失任一Native根后入口检查 **9/9、零跳过** |
| 正式 Topic 自动TTL与旧offset首次执行 | `component-native-auto-ttl-formal-qLiY08` **2/2、零跳过，5m30.221s**；实际Low0→1、原资源/fence保留、无resume，旧offset请求首次执行409且持久blocked |
| 首个重试 Topic 自动TTL | `component-native-auto-ttl-retry-Ezsuk7` **2/2、零跳过，5m32.673s**；实际Low0→1，原资源/fence保留、无resume、正式组位点/投影/Outbox不变 |
| 部分执行重放快照的自动TTL恢复 | `component-native-auto-ttl-formal-pQ78ma` **2/2、零失败/跳过，5m35.172s**；实际partial确认1条、Next=1，TTL使Low0→2后恢复409/blocked，原SnapshotJson/RequestJson、Next与确认计数均未改变 |

本轮预取8配置严格为高水位8、低水位0、单槽1、PerMessage，代理NoDelay及SingleWrite均开启，负载固定每秒一次真实组织单元HTTP写入。每库第15分钟强杀原Worker并启动实际替换容器，第30分钟暂停独享Broker10秒并恢复；两个API持续存活，排空后两个存活Worker均SIGTERM退出0，累计第三个Worker是替换实例。Broker短暂保留被强杀的组成员不等于存在第三个活跃Worker，最终均恢复两个成员。实际认证/租户会话各更新11次，没有延长JWT。

| 预取8长稳实测 | MySQL | SQL Server |
| --- | ---: | ---: |
| 实际测量秒数 | 3600.0073423 | 3600.0016401 |
| 最终写入 / processed Inbox / 未处理 | 3600 / 3600 / 0 | 3600 / 3600 / 0 |
| HTTP P95 / P99（ms） | 34.097 / 45.809 | 65.488 / 72.904 |
| Outbox→Inbox P95 / P99（ms） | 547.838 / 8695.015 | 5465.961 / 10003.238 |
| 停载后排空（秒） | 1.235 | 3.605 |
| 采样最大积压 / 最老年龄（秒） | 41 / 40.290 | 14 / 13.729 |
| 单API / 单Worker最大采样RSS（MiB） | 268.66 / 188.67 | 267.95 / 186.16 |
| 积压 / 基础设施资源采样轮数 | 112 / 111 | 112 / 111 |

正式组最终Committed=High=3601（含原切流探针），三个未写入重试Topic仍Low=High=0；只在该真实空窗口接受未知commit。硬件、单租户/单正式分区/RF1、容器预算和离散采样限制沿用下节登记，不外推生产容量或连续峰值。旧默认1的10档失败及其他测试闭包保持原记录，不能把不同代理/配置版本的延迟差异归因于单一变量。

长稳前后87项源码和899项运行输入无漂移，测试程序集SHA256为 `533377ee6a87a28bf43c0ab97d32b45b46b4f6b9f4f1dff9f820037c2746a242`；测试结束后才接入并编译TTL场景。TTL首次执行/重试阶段另行冻结89项源码，测试程序集SHA256为 `a5b27a83bf15c4589beedb6edfb46b43db2f4dace47304c537560f9aadf31b90`；该轮结束后仅补正式Topic的partial快照边界，聚焦编译26.71s、0警告/错误，89项源码再次独立冻结，测试程序集SHA256为 `e3122de42484a0d8401f4c9e05ce015b8531efb10f6117c2314964b258a2be71`。当前89源码/899运行输入最终重哈希无漂移，公共依赖及两份Native应用不变。两次仅归档被替换的测试DLL/PDB及两个描述文件，合计约5MiB，未复制整仓或公共依赖；不把不同测试程序集称为同一闭包。

重现使用 `component-native-prefetch8-smoke` → `component-native-prefetch8-soak`；自动TTL使用独立 `component-native-auto-ttl-formal|retry`。各入口均需实际构建根、系统临时证据根和明确的API/Worker Native发布根。TTL复用原双库恢复方法的独立分支，不能把该分支通过冒充完整故障矩阵重跑。原始结果位于既有系统临时任务根，长稳审计为 `evidence/component-native-prefetch8-soak-FZqVJS-audit.json`。

TTL独立场景只对本轮专属Broker设置1秒清理周期，并实际读取生效值；只对目标Topic设置delete、初始retention.ms=-1、retention.bytes=-1、segment.ms=1000及LogAppendTime。真实切流与CDC业务已完成、原资源已持久化且实际Connector暂停后，才把目标保留期降为2000ms，等待同TopicId/分区的Low覆盖原末条位点，High不倒退。没有调用DeleteRecords、删除重建、伪造消息时间、修改正式组commit或重认领新基线。重试场景在第一个1s重试Topic写入真实已处理信封副本，并核对投影无新增；它不是新的业务故障路由验收。

两库回退自动补偿和显式恢复均返回500，原代次保持Recovering、CDC owner、fence=1、原资源JSON和切流激活未变、Connector仍PAUSED，实际resume次数未增；跨过12秒Worker巡检时间窗后再次核对。真实业务POST返回503且无组织/Outbox新行，正式组位点与投影不变。正式Topic的精确旧offset请求返回409、持久blocked/resource_or_window_changed。过期前事件已被处理，本例不证明未消费事件在TTL后不丢，也不证明所有消费者自动停机；控制窗口丢失后仍需源事件与Inbox/DLQ独立对账，不能直接清fence。

补充partial轮使用两条真实CDC业务事件，正式组已确认到High=2；原重放范围为[0,2)，单次预算1，Native API先返回partial、Confirmed=1、AlreadyProcessed=1、Processed=0、HasUnconfirmed=false、Next=1，并实际持久SnapshotJson。自动TTL推进Low=2后，再执行原操作返回409、持久blocked/resource_or_window_changed；Confirmed仍1、Next仍1、SnapshotJson/RequestJson原字节相同，无新业务效果或正式组commit。两库均通过，未把AttemptCount、租约或更新时间的正常变化当作应保持不变。三组TTL均复用原两个方法的明确分支，共六次场景执行，不是新增六个canonical用例或完整故障矩阵重跑。

从降低retention到观察Low推进的等待，正式首次轮MySQL/SQL Server为1.325/0.259秒，重试轮1.297/1.287秒，partial轮1.324/0.783秒；旧消息在调低前已存在，这些数值不是从追加消息开始计算的TTL寿命。该轮只分别过期正式及第一个1s重试Topic，另两个重试阶段未单独演练。

本轮五组明确运行共44个累计Native宿主、30个DB/Broker/Connect均已退出，20次存活Worker正常SIGTERM退出0；刻意强杀的Worker不算优雅停止。`evidence/native-prefetch8-ttl-container-exit-audit.json`按精确容器身份核对，其他模块资源未接管。最终工作区与独立只读复核记录见本节收尾。

最终工作区核对 `native-prefetch8-ttl-final-working-tree-check.json`：仍为原main/HEAD，索引无未解决冲突，原stash保留；本任务已跟踪差异检查及42个新增文件空白检查通过。早期967项保护摘要无缺失，5项为已确认本任务共享文件变化，另观察到62项其他并行变化，没有覆盖或回滚。`evidence/native-prefetch8-ttl-process-exit-audit.json`确认该任务目录下残留宿主进程为0。未提交、推送、合并或启用生产切流，未执行全量测试；既有编译目录继续复用，没有重新尝试此前被策略拒绝的递归清理。

授权的独立只读评审最终通过，未发现阻断项：原始TRX、短测准入、三版源码/运行闭包、归档DLL、Native身份、TTL前后保护/快照断言及Worker真实shutdown日志均核对一致。评审者没有执行构建、测试或Docker操作；容器实时无残留由主执行者的精确Docker查询核对，不冒称由评审者独立查询。

当前剩余：生产等价容量、1万同时在途目标、独立物理故障域和全产品Native矩阵未认证。保持Pilot / `Capacity-not-verified`、Worker默认LegacyPolling及正式切流/影子采集关闭；Connect有限迟到动作纠偏不等于任意延迟下的远端CAS或线性化fencing。

## 2026-10-30：本机 Native 双 Worker 长稳与有界负载

按用户确认先完成本机范围。快照 `messaging-native-soak-20261029`，基线仍为 `main / 75be6fc0e17433f9c59c42553093dacd479ef9d1`，原 stash 保留。复用前节实际 API/Worker Linux ELF、SDK 镜像及生产实现；本轮只补定向测试、代理、入口和相关文档，其他模块继续独立开发。未跑仓库全量测试、提交、推送、合并或生产切流。

| 本机验收输入 | 实际结果 |
| --- | --- |
| Native 双 Worker 短测 | `component-native-dual-worker-smoke-O2VCmp` **2/2、零跳过，6m55.009s**；两库分别60.0160042/60.0002828秒，各60条全部对账 |
| Native 双 Worker 长稳 | `component-native-dual-worker-soak-pNwFuU` **2/2、零跳过，2h03m41.687s**；每库满3600秒、3600条，最终3600 Outbox/processed Inbox、精确投影，未处理/持久DLQ/重复业务行/新增死锁均0 |
| 原预取1，5次HTTP/秒 | `component-native-capacity-5-0Lu6aB` **2/2**；每库60秒300次写入，全部排空，但停载后仍需156.644/159.019秒 |
| 原预取1，10次HTTP/秒 | `component-native-capacity-10-tuq6JR` **0/2、零跳过，11m18.927s**；两库均600次写入，180秒内未完成业务投影，失败及最后积压原样保留，不能写成通过 |
| 仅NoDelay、预取1对照 | `component-native-capacity-5-QQZpqp` **2/2**；MySQL排空仍152.600秒，未解决主要串行等待 |
| NoDelay、预取8，5/10次HTTP/秒 | `component-native-capacity-5-PqAWxB` / `component-native-capacity-10-Hjx8KH` 均 **2/2、零跳过**；每库300/600条完整对账，原故障、事务、Inbox、Broker排空及180秒门槛保留 |
| 调度/反压/位点与代理回归 | **28/28、零跳过，911ms**；含精确8项上限、混合Key单槽串行、拒绝第9项、排空后Resume、安全水位，以及真实loopback原帧/响应字节、OffsetCommit Gate阻断和Drop；入口检查 **7/7** |
| 最终单帧版本10档复验 | `component-native-capacity-10-2vErl5` **2/2、零跳过，6m48.506s**；每库600条完整对账，精确8/0/1、PerMessage、SingleWrite及Inbox处理时间非倒退均核对，独立只读复核通过 |

长稳实测如下，HTTP响应和Outbox→Inbox分别计时：

| 每库3600条实测 | MySQL | SQL Server |
| --- | ---: | ---: |
| 实际测量秒数 | 3600.0106023 | 3600.0129497 |
| HTTP P95 / P99（ms） | 25.463 / 36.886 | 58.918 / 69.265 |
| Outbox→Inbox P95 / P99（ms） | 786.193 / 27912.665 | 5659.944 / 32571.814 |
| 停载后排空（秒） | 1.195 | 4.283 |
| 采样最大积压 / 最老年龄（秒） | 43 / 42.358 | 45 / 44.347 |
| 单API / 单Worker采样最大RSS（MiB） | 265.34 / 187.94 | 266.50 / 188.43 |
| 积压 / 基础设施资源采样轮数 | 112 / 111 | 112 / 111 |

真实链路为可信租户HTTP创建组织单元→事务Outbox→Debezium→Kafka→两个实际Native Worker→Inbox/组织投影；两个Native API持续存活，业务负载固定发往其中一个API。每库原Worker在15分钟处SIGKILL后以新容器替换，30分钟处暂停独享Broker10秒并恢复；各11次真实登录/租户会话续建，不延长JWT。正式组最终Committed=High=3601（含1条切流探针），三个从未写入重试Topic均Low=High=0，未知提交仅在该实际空窗口接受。各两个存活Worker在排空后SIGTERM退出0；原被强杀Worker不称优雅停止。

短测首次 `component-native-dual-worker-smoke-0uiSlA` 为 **1/2、零跳过，7m23.656s**。MySQL已排空，但单次Windows `Task.Delay`提前约0.57ms返回，实际59.9994269秒未满足原60秒下限；现按Stopwatch反复补足剩余时长，保留原断言。重跑O2VCmp通过后才准入长稳，准入文件指向该真实Native短测；JIT、旧ELF或旧程序集短测不能替代。

预取1在本轮单有序分区和三个空重试Topic下，积压期实测约1.7～1.8条/秒。专用消费线程和5ms完成轮询已存在，未见CPU饱和；逐项Pause/Resume、同步安全水位提交与默认Fetch等待构成候选等待链，未把推测写成唯一根因。新增显式 `--partition-buffer 1|8` 只允许Native的60秒5/10档；长稳和其他入口保持1。预取8使用现有受支持的8/0/1配置（高水位8、低水位0、单槽）及PerMessage安全水位模式，扩大有界待处理深度，单分区Handler仍串行。未改生产默认配置或重发ELF。

小帧代理另有独立证据：同一独享Broker的20次只读ApiVersions v0，直连/Native Listener P50约0.35/0.32ms，经原分两Write代理约44ms；单设NoDelay未消除此开销。现使用一个4+size缓冲区、保留相对帧体的解析偏移、Gate及Drop，并一次Write完整线格式。单帧修正后20次实测直连0.4046ms、Native Listener0.3053ms、代理0.3885ms（P95 0.4618ms）。该结果仅证明这组小帧路径的额外等待改善，不代表全部Fetch/Commit或业务延迟。

原对照字段 `OrderedProjectionVerified` 实际只检查按Outbox事件时间/Id排序的Inbox.ProcessedAtUtc非倒退；旧原始结果保留，当前字段校准为 `InboxProcessedTimeNondecreasingVerified`。处理时间不等于Kafka offset或数据库commit时间；单槽顺序和反压依赖上述精确配置单测与真实配置，不能把该SQL单独写成事务提交顺序证明。

全部阶段复用单租户、1个正式有序分区、RF=1及原3个重试Topic；真实变更事件不是旧Scope C的固定256B负载。短时每库60秒，5/10档必须实际完成300/600次HTTP、发送率至少目标95%，并在原180秒等待门槛内完成业务及真实Broker排空；该阈值是开发验收参数。短时故障固定15秒处Worker替换、30秒处Broker暂停10秒；各两个API和两个存活Worker，累计第三个Worker是替换实例。

资源来自同一owner容器内 `/proc/1/stat`、CLK_TCK、VmRSS，记录PID1/RSS/累计CPU，单独记录容器名；不再以Docker CLI的Windows工作集当Native资源。只对本轮独享DB/Broker/Connect采集Docker原值，单Host限1核/1GiB，MySQL1核/1.5GiB、SQL Server1核/3GiB、Broker/Connect各1核/2GiB。主机i9-14900K、32逻辑CPU、约63.69GiB，Docker31.19GiB、Engine29.9.1/WSL2，存在其他任务负载。约33秒离散采样不是连续峰值；Native API/Worker后段未见明显加速增长，Broker内存有所增长，不外推长期无泄漏。

原长稳闭包899项、源码84项前后及独立复核均一致；优化对照源码85项、最后帧修正87项另行冻结。三版仅替换测试程序集（原b41eac…、对照027c1f…、最终601ea3…），Native ELF/manifest/原生依赖及其他公共运行输入全部保持一致。只额外保留被替换的测试DLL/PDB和两个描述文件，没有复制整仓或公共依赖闭包。JIT工厂仅用于隔离库迁移/播种。完整当前AI源码、Migrator Native及全产品Native不在本轮认证范围。

有界测量分别报告HTTP输入和包含停载排空的完整产消周期速率；后者不含初始化和优雅停止。5档为分帧版本B，10档为最终单帧版本C，两者Native应用相同、测试程序集不同，不混称同一测试闭包：

| 档位 / 数据库 | HTTP实测速率（次/秒） | 包含排空速率（条/秒） | 停载排空（秒） | HTTP P99（ms） | Outbox→Inbox P99（秒） |
| --- | ---: | ---: | ---: | ---: | ---: |
| B预取8、5档 / MySQL | 4.9998 | 3.814 | 18.663 | 94.912 | 45.351 |
| B预取8、5档 / SQL Server | 4.9995 | 3.773 | 19.502 | 76.848 | 50.495 |
| C预取8、10档 / MySQL | 9.9988 | 6.732 | 29.123 | 32.134 | 42.424 |
| C预取8、10档 / SQL Server | 9.9976 | 5.110 | 57.410 | 62.263 | 71.564 |

最终C正式组Committed=High=601，原重试窗口仍0/0；每库两个Worker在排空后正常退出0。C两库最大采样积压230/458、最老年龄35.887/62.053秒，单API最大RSS266.18/264.75MiB，单Worker181.25/183.02MiB。原始TRX、NDJSON、资源曲线、各版输入清单、元数据RPC诊断及 `native-load-summary.json` 位于前述系统临时任务根。最终87源码及899运行输入独立重哈希无漂移，公共依赖与两ELF仍未变化。

最终收尾：九轮本任务记录中的90个Native宿主容器、54个DB/Broker/Connect容器均已退出，30次成功Worker SIGTERM退出0有记录；失败轮及刻意强杀不计为优雅停止。Windows侧本任务构建/Native根下未发现残留API/Worker宿主进程，未接管其他模块资源。`native-load-final-working-tree-check.json`确认分支/HEAD不变、索引无未解决冲突、原stash仍保留；本任务差异检查及41个新增文件空白检查通过，早期967项保护摘要无缺失。5项共享文件含本任务定向变化（Unit项目仅增加原代理源码链接），另观察到61项并行变化，未覆盖或回滚。没有仓库根artifacts或整仓复制。既有编译目录递归清理此前被自动审批以`blocked by policy`拒绝，未绕过，继续复用保留。

剩余边界：预取8仅有明确60秒档位测量，未做该配置的60分钟长稳；生产等价容量、1万同时在途目标、独立物理故障域、自动TTL过期及全产品Native矩阵未验证。Connect无远端CAS，有限迟到动作纠偏不等于任意延迟下线性化fencing。保持Pilot / `Capacity-not-verified`、Worker默认LegacyPolling、正式切流/影子采集关闭。

## 2026-10-29：分片发现与完整 Native 控制链续办

快照 `messaging-native-control-worker-20261029`；用户授权的分片整改、Messaging 完整 Native 控制面和 Worker CDC 定向验收已完成。本轮没有运行全量测试，没有提交、推送、合并、生产切流或覆盖其他模块。前序日期及输入的结果继续保留，当前范围以本节为准。

| 本轮验收 | 实际结果 |
| --- | --- |
| canonical 分片发现 | **1666项、五分片UID互斥并集完整**：api-sqlserver206、api-mysql206、migrations580、infrastructure565、messaging-heavy109；迁移分组250/330完整互斥。仅执行发现，不是运行1666项测试 |
| 分片/发布工具 | **21/21、0跳过**，`native-tools-final.log`；普通JIT入口拒绝继承Native根，实际宿主配置漂移及缺失配置均失败关闭 |
| API 实际 Native 发布 | **167445072 bytes**，SHA256 `889f78d6376f1a093af821f02c70021355e2f8c9bb97153af06dec53faab1e81`，`native/api-JlHRav`；完整发布31m43.117s，托管编译14m36.29、0警告/错误；机器码9条既有第三方告警通过精确门禁 |
| Worker 实际 Native 发布 | **106118520 bytes**，SHA256 `e910a5d4e6a2920abd257036789623ab1d96d6d58fa11bc2d233ad5995188912`，`native/worker-LzMSqF`；完整发布18m08.360s，托管编译10m57.22、0警告/错误；机器码9条既有第三方告警通过精确门禁 |
| 完整 Messaging Native 双库故障链 | **2/2、0失败/跳过**，`component-native-process-Yb1sFw`；入口摘要22m43.045s。TRX单例：MySQL11m02.1744085s、SQL Server11m40.5914071s；不把二者求和与入口耗时混用 |
| 原生进程及纠偏 | 每库累计5次API、8次Worker启动，共26次实际Linux启动；不是13并发。每库Event1480实际纠偏2次，各2个Worker接受SIGTERM后退出0；故意SIGKILL不称优雅停止 |
| 最终冻结 | `assemblyUnchanged/binariesUnchanged=true`；测试依赖、两份ELF、manifest、配置及原生依赖前后完全一致；14项本任务fixture/tool源码最终无漂移 |

分片根因与修复：旧默认程序集发现1572，旧矩阵1477；当前源码首次Release构建5m01.63、0警告/错误，后续夹具构建1m20.44/1m18.88均0警告/错误。首个新鲜集合1664，后续新鲜构建增加两项后以1666为准；没有直接降低门槛迁就旧数字。入口先验证完整UID集合再统一报告计数漂移，保留原六项共享CDC环境债务额度，重型最低通过门槛同步提高。迁移342只选两个恢复方法、最低数2，不混入Soak。canonical/affected入口复用已构建的两个真实宿主，显式供给输出和独享临时证据，冻结实际运行配置；Native根只能通过专属入口使用。治理断言同时识别枚举双库和具有真实双库创建分支的true/false用例，未修改AI测试实现。

本轮实际启动两个原生API和两个原生Worker，复用原双库恢复方法及完整故障断言：真实鉴权与租户业务HTTP、事务Outbox、影子采集、切流证据/切流、回退与恢复、存活旧执行者真实KILL专属会话锁后的迟到pause/resume、Connector配置漂移/任务失败、API到Connect和API/Worker到DB及Worker到Kafka断连、Inbox提交/OffsetCommit间崩溃、重放提交/检查点间崩溃与自然租约接管、DLQ提交/位点间崩溃、最终提交后HTTP响应丢失、业务投影与可信Actor/B0精确对账。两个切流后持久终态的迟到动作均由实际Native Worker纠正，owner/fence/代次保持约束。Linux镜像内实际Ubuntu24.04.5/.NET10.0.13日志逐条对应Native宿主身份；JIT工厂仅负责独享库迁移/播种，不冒充Native Migrator。

原生发布各用独立目录，`--host api|worker`选择目标ILC/ELF；普通resume拒绝跨宿主目录。两者均为SDK10.0.402、linux-x64/runtime10.0.13，SDK镜像ID `sha256:2b9cf51b78cffcbbae976c2260c58dcf0831647d8afec572aa6766085ff2052b`。独立只读核对API1527项、Worker1526项冻结托管输入与实际磁盘相等，同次ILC/80项发布清单与API82个、Worker81个最终文件一致。三条工具索引的排除仍由实际ILC及发布内容证明。两宿主实际输出中7项Messaging/数据/契约实现DLL摘要相同。Worker机器码阶段4个非任务AI C#变化精确登记在manifest，托管产物未变；不认证AI的最新源码，也没有覆盖或回滚它们。独立评审只读核查发布与最终TRX/日志，没有冒称执行测试。

测试拓扑将Windows观察端保留为EXTERNAL回环直连，Native API/Worker经原故障代理进入同一Broker的独立NATIVE Listener，Connect内部9092不变。每库实际ClusterId与Native切流持久基线相等，记录于`native-routing.ndjson`；故障代理原有OffsetCommit/断连Gate及Inbox/位点/重读断言保留。原生宿主只读挂载本轮发布目录，启动前校验manifest/ELF头/SHA/四项guard，停止前核对随机容器名及owner标签；Docker CLI资源不当作ELF资源。成员数早于rebalance完成可见，测试现等待Stable、双成员和正式/三个重试Topic完整且无重复分配。

失败事实均保留：

- `native-process-P1Cgd9` **0/2、0跳过，4m38.776s**。错误地把容器网关名用作Windows观察端地址，解析到非本机网段，创建Topic前握手超时，尚未启动Native Host；改为上述双Listener，没有修改系统DNS/hosts、生产实现或两份ELF。
- `native-process-QDyMT3` **1/2、0跳过，13m15.485s**。SQL Server原生全链和两次SIGTERM退出0通过；MySQL切流准备422。旧轮未保留具体拒绝分支，不能冒称唯一根因。发现并修复仅等成员数的前置缺口，添加失败时状态/分配快照，诊断自身失败不会吞掉原始异常，不输出原始Connect trace。第三轮双库均通过。
- 第三轮准备时，完整Integration `NoDeps`重编译因并行AI测试依赖新字段、冻结旧DLL产生13个AI编译错误（51.69s），未修改AI或降低断言。复用原有`MessagingProcessFocused`，只链接仓库原Messaging源码和实际旧依赖；诊断首次误用私有方法的2个编译错误已改为公开状态接口并只提取状态字段。最终聚焦编译**15.18s、0警告/错误**，前后源码摘要相同；不冒称当前完整AI或完整工作区Integration编译通过。
- 两次重跑按正式资源池等待其他模块Native构建完成，未抢占、清锁或终止其他任务；等待时长不计为用例执行时长。

重现入口：常规新鲜Integration构建使用 `run-messaging-reliability.mjs --stage native-process`；本机为避开非任务AI混合版本使用 `--stage component-native-process`，两者沿用相同的原双库恢复过滤器、最低数2和40分钟上限。均显式提供 `--build-root`、系统临时`--output`及`--native-api-root`/`--native-worker-root`；缺任一Native根不能混入JIT。现有项目未被拆为新业务模块，也没有复制整仓源码。

原始TRX、Native宿主/退出/路由NDJSON、Windows迁移日志、源码及发布摘要保存在 `C:/Users/dai27/AppData/Local/Temp/fullnet-messaging-reliability-20261028`；最终汇总 `evidence/native-control-final-summary.json`，容器核对 `evidence/native-final-container-audit.json`。三轮测试累计21个Testcontainers资源及43个Native宿主容器均已退出，其他模块资源未接管。当前分支/HEAD、stash与差异检查见本节最终收尾记录。

最终收尾：两个本轮SDK发布容器已退出，`native-final-process-audit.json`确认本任务构建/Native根下无残留宿主进程。`native-final-working-tree-check.json`确认仍为`main / 75be6fc0e17433f9c59c42553093dacd479ef9d1`，索引无未解决冲突，原stash `b74742b9a31f9c5db7bb6e60498345a5653ac531`保留，本任务已跟踪差异及40个新增文件空白检查通过。早期967项保护摘要无缺失，4项为已确认本任务共享文件变化，另观察到61项其他并行变化，未覆盖或回滚。此前编译目录递归清理被自动审批以`blocked by policy`拒绝，未通过其他工具绕过，目录继续复用保留；没有在仓库根生成artifacts或复制整仓。

剩余边界：未执行全量测试、当前AI最新源码验收、全产品/所有Provider Native矩阵、Native60分钟Soak、生产等价容量、独立物理故障域和自动TTL过期演练。前轮60分钟长稳是原JIT Host.Worker输入；本轮Native控制面与HybridKafka/Debezium交付通过不能改写为Native长稳或容量证明。Connect没有远端CAS，只证明原资源前提下有限迟到动作后的纠偏；不声明任意延迟的REST线性化fencing。保持Pilot / `Capacity-not-verified`，默认LegacyPolling、正式切流与影子采集关闭。

## 2026-10-29：迟到 REST 与双真实 Worker 续办

本轮快照 `messaging-late-rest-dual-worker-20261029`，基线仍为 `main / 75be6fc0e17433f9c59c42553093dacd479ef9d1`，原 stash 保留。仅修改 Messaging 及直接相关数据锁、测试、SQL 目录和文档，未覆盖其他模块。本轮整改及双库真实验收已完成；生产容量与完整 Native 控制路径仍未认证。

原专属数据库连接在外部动作和终态提交前验证锁仍由自己持有，失锁不再补偿或重新获取锁延续旧动作。Worker 仅在锁外发现物理偏差时竞争控制锁，锁内重验当前激活与终态关联、显式 fence、冻结端点/环境、Kafka 身份/窗口/delete-only 策略及恢复源位置；不写 owner、不清 fence。当前代次直接关联所有权行，不按机器时钟排序。新切流生成 ActivationId 并冻结控制端点，防止既有同名拓扑认领旧操作。Connect 不支持远端 CAS，本实现证明原资源前提下有限迟到动作后的收敛，不声明线性化 fencing 或固定恢复 SLA。需要持续手工暂停时，先停止或禁用相关控制 Worker，见部署与回退手册。

真实验收发现 SQL Server 错误 468：所有权 MessageType 使用二进制排序规则，操作日志 EventType 继承库默认规则，直接文本 JOIN 冲突。改为 UUID 和 Schema 关联，并分别用相同 EventType 参数匹配两个表，保持隔离，无新增 DDL 或任意 COLLATE。纠偏失败输出低基数 Event1481 ReasonCode，不暴露原始 SQL、URI 或秘密。

| 本轮边界 | 最终实际结果 |
| --- | --- |
| 控制组件 | **46/46、0 跳过**，976ms，`late-rest-reason-control-tests.log`；含原连接失锁、迟到动作、当前激活/代次、显式 fence、资源漂移、健康只读探测与稳定原因码 |
| 真实双 API/双 Worker 故障矩阵 | **2/2、0 跳过，22m10.393s**，`component-process-pujyJT`。两库真实 KILL 原专属锁连接且保留 API 存活；迟到 pause 在 Recovered 后由 Worker 恢复 RUNNING，迟到 resume 在后续 Completed 后恢复 PAUSED；owner/fence/代次、非空 B0 与可信 Actor 审计及精确业务投影均通过，Legacy 新写入不再进入 CDC |
| 部署目录双 Worker 短测 | **2/2、0 跳过，6m19.221s**，`component-dual-worker-smoke-kjpsOu`；每库实际60秒、60次业务写入、精确对账、Worker 替换及 Broker 暂停恢复 |
| 双真实 Worker 长稳 | **2/2、0 跳过，2h04m20.730s**，`component-dual-worker-soak-33urrp`；两库各满3600秒、各3600次真实 HTTP 业务写入。最终3600 Outbox/3600 processed Inbox、精确投影、重复行/死信/未处理均0，正式/重试 Topic 位点排空，新死锁0；两库各执行15分钟处真实 Worker 崩溃替换、30分钟处独享 Broker 暂停10秒并恢复 |
| 准入工具及命名 | 工具 **5/5**，`dual-worker-admission-closure.log`；命名 **37/37**，`late-rest-naming.log`；本类4项的恢复2项/长跑2项聚焦分区发现完整且互斥 |
| 最新 API/Worker AOT 分析 | **均0警告/错误**，分别13.76s/57.83s，`late-rest-collation-api-aot.log` / `late-rest-collation-worker-aot.log`；这是分析构建，不能称机器码运行 |
| 新增 JSON 的实际 Native | 关闭反射的真实 Linux ELF 完成 ActivationId、控制端点/环境及双库源坐标 JSON 往返，`native-json-result.json`；2584384 bytes，SHA256 `b083a17f53a2cff39d9f3748a9424fcf723ecf084db9344f6e0ec0ae540da9c7`，仅认证此序列化范围 |

双真实 Worker 长跑使用同租户单有序分区、每秒一次创建组织单元；两个成员不等于双倍并行吞吐。两库各11次真实认证会话续建，未延长默认短期 JWT 或关闭鉴权。Broker 正式 Topic High/Committed 均为3601（含1条切流探针），三个未写入重试 Topic Low=High=0，未知提交位点只在该实际空窗口下接受。

| 长跑实测 | MySQL | SQL Server |
| --- | ---: | ---: |
| 测量时长（秒） | 3600.007 | 3600.014 |
| HTTP P95 / P99（ms） | 22.218 / 33.177 | 17.322 / 25.426 |
| Outbox→Inbox P95 / P99（ms） | 505.894 / 28695.045 | 5436.542 / 31749.615 |
| 停载后排空（秒） | 4.038 | 7.455 |
| 采样最大积压 / 最老年龄（秒） | 37 / 35.999 | 41 / 40.155 |
| Worker 采样峰值工作集（MiB） | 165.31 | 169.15 |
| 积压 / 三容器资源采样轮数 | 113 / 112 | 113 / 112 |

运行机器为 Windows x64、i9-14900K、32逻辑CPU、约63.69GiB内存，存在其他任务负载；数据库/Broker/Connect 均维持原有界容器预算。资源值是离散采样，不称连续峰值。测试 ContentRoot 指向实际构建目录，短测与长跑 **1380项** DLL/配置/契约/native 依赖身份完全一致；整轮 `assemblyUnchanged/binariesUnchanged=true`。最终核对1078项相关构建源输入无漂移，其中237项 Messaging/直接运行路径亦无漂移；不是对其他模块完整工作区源码的认证。独立只读评审已复核实现、短测及准入闭包，无新增阻断项，未冒称评审者执行测试。

失败与修复保留：`component-process-kkoLKf` 未到故障点，前置响应未保留，不能认定唯一原因；同时发现健康采样竞争控制锁，修复锁外只读探测、有界冲突重试及提前响应诊断。该轮精确停止自己的测试进程树，无 TRX，不计通过。`component-process-X9WVtX` 为0/2、6m37.195s，修正活动日志 FileShare 及 SQL Server 实际锁描述筛选，代理析构释放在途 Gate。`component-process-w1Btu2` 为1/2、16m15.758s，暴露并修复上述真实排序规则错误，随后 pujyJT 两库通过。首次 API AOT 参数拆分失败，改正式参数数组后重跑通过。上述旧轮不计通过。

首轮双 Worker 短测 `component-dual-worker-smoke-cwCkrT` 为1/2、5m08.651s；MySQL 不存在所查 Global Status 行，现读取已启用的 `INNODB_METRICS.lock_deadlocks` 实际计数，缺失/禁用仍失败，未假造0。修正后 `component-dual-worker-smoke-akGCpO` 2/2、6m11.730s；补齐实际 ContentRoot 和静态配置/native 冻结后再取得 kjpsOu 2/2，作为本次长跑唯一准入。周期/最终采样串行化，避免 NDJSON/集合竞争。

原始 TRX、每库 result.json、faults/backlog/resources.ndjson 与汇总 `evidence/dual-worker-final-summary.json` 位于 `C:/Users/dai27/AppData/Local/Temp/fullnet-messaging-reliability-20261028`。两库各三个独享容器最终均已退出；最终 `evidence/late-rest-final-process-audit.json` 确认该构建根下实际 API/Worker 进程数为0。保留先前失败证据；此前递归清理编译目录被自动审批拒绝（`blocked by policy`），未绕过，继续复用并保留这些目录。

最终工作区核对 `late-rest-final-working-tree-check.json`：分支/HEAD未变化，索引无未解决冲突，原stash保留，本任务已跟踪差异 `git diff --check` 和35个新增文件空白检查通过。早期967项保护摘要无缺失，4项为已确认本任务共享文件变化，另观察到44项其他变化；没有将这些并行内容覆盖或回滚。未提交、推送或合并。

剩余未通过/未覆盖：仓库完整程序集分片仅做发现，1572与 canonical1477不一致，此门禁未通过；未修改阈值迁就其他模块漂移，也未执行全量测试。当前新增路径仅有上述 AOT 分析和 JSON Native 运行；此前275a…1218完整 API ELF/双库原生 HTTP 是旧输入历史证据，不替代本轮完整 Native 控制故障矩阵或 Worker CDC Native，二者仍未验证。生产等价容量、独立物理故障域、任意远端延迟的线性化 fencing 未认证，保持 Pilot / `Capacity-not-verified`。正式切流与影子采集仍默认关闭，Worker 默认 LegacyPolling；没有提交、推送、合并或生产切流。

## 2026-10-29：时间观测与不可变回滚基线续办

续办快照为 `messaging-observability-remaining-20261029`，HEAD 未变化。独立只读评审经用户授权启动；发现的 Topic 同名重建、重试阶段减少和保留窗口前移三个 P1 反例已修复，复核未再发现阻断项。静态复核不替代以下实际运行。最终实际结果见本节末汇总；未覆盖边界继续保留。独立评审只读检查源码及证据边界，上述运行测试均由主执行者完成，未冒称评审者执行了测试。

实现范围：MySQL 读取 Connector 已完成 GTID 前缀与真实 Performance Schema 提交计时，只在同一 server UUID、完整前缀、可用且有界历史等前提下输出时间差；空闲已追平为真实零，历史不足或权限不足保持 unknown。Kafka 时间保留比按真实 Broker 索引、消费组提交点和 retention.ms 计算，要求操作者确认自创建以来 LogAppendTime 并绑定不可变 Topic ID，delete-only、无字节保留；时间探测失败撤下旧比值，不吞掉独立消息数。每节点时钟误差预算采用保守四倍年龄修正、两倍未来时间检查，不把 offset 差称为时间。

切流在独立 `CdcKafkaBindingJson` 列保存 Cluster/Connector/配置指纹、原正式组和完整重试阶段、Topic ID/分区及真实 Low/High；不依赖首条正式 CDC 提交坐标。迁移 342 成对实现并可在 DDL 成功但登记中断后重放，禁止替旧记录从当前 Broker 补造原基线。暂停前、排空中、最终提交与恢复均核对原资源；Low 前移、Topic 重建、阶段减少、未知提交位点等失败关闭。自动回退仅覆盖原未截断窗口，保留回收后须独立源/Inbox 对账，不能直接重设基线。恢复使用原持久 Connector；活着的旧执行者丢失数据库会话锁后的迟到 REST 可用性边界尚未实测。

| 新增范围 | 已取得的本机证据 |
| --- | --- |
| 观测组件 | 38/38，`component-unit-nyCJfj`，386ms |
| 控制组件 | 27/27，`component-control-Rj7Dnk`，657ms；覆盖正式/重试 Topic 在准备前后变化及阶段减少 |
| 真实时间来源 | 3/3，`component-time-8o3OqP`，31.062s；MySQL 历史环 10000/20000 行、缺权限/计时关闭/截断与 Kafka 支持/拒绝条件 |
| 真实 Kafka 资源保护 | 2/2，`component-topology-NHeCjG`，15.125s；正式/重试 Topic 实际 DeleteRecords 和同名删除重建，实际组位点不能掩盖缺口 |
| 部署与矩阵 | 43/43，`time-deployment-helm.log`，8.234s；本轮后续入口调整另需结构复验 |
| 新列命名/注释 | 37/37，`topology-naming-2.log`，2.489s |
| 新增 JSON 的真实 Native | `native-json-result.json`：关闭反射的 Linux ELF 实际完成 MySQL/SQL Server 源坐标、不可变拓扑 JSON 往返；2571984 bytes，SHA256 `80a2a6edf2f42efe8b21d71b48814c3dec8872978c1c31fc6c4a6bbfdbaab937`；仅此序列化范围 |

失败事实保留：最初把拓扑附在正式源坐标上，真实双库暴露切流前尚无首条正式提交坐标，现改为独立列；旧测试对 Topic 数量的预期未含第三个自定义重试阶段，修正为四个真实 Topic，并冻结编译前后源码。`component-process-oHE9jw` 双库均运行到最后，先截断原日志再要求成功回滚导致预期失败（0/2，19m15.330s）；最初尝试移到成功回退之后，独立复核指出这会先命中 Legacy 所有权门禁、无法进入窗口检查。现将整组断言移入独立的真实 Kafka＋双库 Inbox/操作日志场景，要求 HTTP 409、持久 blocked、实际消费组位点不变、业务副作用仍为 8 行；该夹具显式预置 CDC owner 并产生测试信封，不称 Debezium/业务 HTTP 端到端。多进程批次保留真正 HTTP 切流和最终回退，严格窗口保护不放宽。一次构建因本任务测试在编译期间调整而被源摘要门禁拒绝；后续稳定重建零警告/错误且前后摘要相同。失败日志与原始 TRX 均保留。


排队修正：取消尚无测试子进程/容器的旧场景请求后，资源入口正确拒绝其遗留锁；核对进程已退出、无子进程及精确锁归属后只删除该请求的三个单文件锁，再由正式入口重新排队，未抢占其他任务。此前排队中止没有 TRX，不算已执行。Helm 首次结构复验使用了错误的本任务工具路径并暴露旧矩阵数 63；修正工具路径和实际新增用例对应的 70 后，部署/Helm/矩阵 39/39、0 跳过通过（9.776s），迁移映射追加后矩阵 4/4 再次通过。


Native 补充：首次完整 ELF 实际发布完成，但 `attempt-icsCjP` 因链接期间三个工具索引和其他模块源码变化而被旧源码门禁拒绝；托管闭包及 HEAD 实际未变，不计通过。经独立只读核查，只精确排除未进入实际 ILC 参数/发布源文件候选集的 `global-sql-statements.json`、`client-generation-manifest-v1.json`、`fullnet-client-v1.openapi.json`，仍保存全量源码变化；Workflow/命名等真实契约资源继续冻结。新增 `--resume-managed` 只复用同归属直接子目录、无符号链接、最后成功托管快照、原完整镜像/SDK/RID/编译命令与 HEAD、全部冻结 DLL/依赖摘要一致的阶段，原拒绝记录不修改，重新执行 `publish --no-build`。独立复核还发现辅助 MSBuild 后不得重设托管基线，已改为原 frozen 全程唯一权威；本轮运行结束需再独立核对原清单/新清单/实际磁盘三者一致。实际发布候选清单含 NativeBinary（apphost 会在 SDK Publish 后被覆盖），不是 JIT 运行。精确索引守卫反例 4/4 已通过，包含 Link 改名、大小写别名、缺失证据和其它真实契约仍冻结。

告警新增首轮 `component-time-alerts-qDRvAH` 0/1、0 跳过，6m48.507s：某次真实采样返回 unknown，断言立即失败并撤下旧比值，没有用消息数冒充时间。原记录不足以区分保留/时钟/查询异常，未宣称原根因已经证实。已增加失败时 Broker 实际 Low/High/时间索引及有界日志记录；独享 Topic 参数从 1 秒保留改为 30 秒，启动时实测宿主/追加时间差符合 1 秒预算，保持原告警表达式和 for=10m、不接受中途 unknown，待完整复验。Worker AOT 的一次排队超时未开始编译，现只延长本任务排队预算，CPU/内存限额不变。

完整 Integration 构建曾受并行 AI SearchKnowledge 缺失阻断，未修改 AI 模块；组件与多进程桥接只链接原源码、复用非任务依赖，不能冒充当时完整工作区 Integration 通过。当前 Native 重新读取完整 API 闭包，结果以实际发布清单为准。正式切流和影子采集仍默认关闭，Worker 默认 LegacyPolling；既有 Soak 不改写成新协议长稳或生产容量证明。

### 2026-10-29 最终集中验收结果

后续复核补齐第四类实际准入缺口：compact 可删除同 key 的内部事件而不改变 Low。正式及 retry 的共同拓扑入口现在于窗口读取前后批量检查有效策略，只接受纯 delete，含 Broker 继承值；缺权限/未知失败关闭。Topic 从创建起保持 delete 是部署契约，不允许 compact→delete 后重新认证丢失历史；Connect 内部 compact Topic 不受此规则影响。无需增加可切换策略或新 DDL。依据 [Kafka 4.1 cleanup.policy](https://kafka.apache.org/41/configuration/topic-configs/#cleanup.policy)。

| 边界 | 本机实际结果 |
| --- | --- |
| 双 API/双 Worker 故障矩阵＋迁移 342 | **2/2、0 跳过，18m56.030s**，`component-process-bDEnyW`。两库实际创建列，并重放未登记 DDL 两次，持久基线 JSON 完全不变；完整故障恢复、最后提交响应丢失、Completed、业务投影和可信 Actor 审计通过 |
| 固定窗口重放 | **2/2、0 跳过，1m34.465s**，`component-replay-atnhBq`。真实 Kafka＋双库 Inbox/操作日志，截断后 HTTP409、持久 blocked、正式组位点不变、八行业务副作用不增加；不是 Debezium 端到端 |
| 最新策略/真实 ACL/资源身份 | **2/2、0 跳过，19.139s**，`component-topology-0AhIVi`。正式/重试分别验证 Broker 继承 compact、混合拒绝、显式 delete 接受；实际 StandardAuthorizer 拒绝 DESCRIBE_CONFIGS，但 DESCRIBE 仍成功，空 Topic 也不能视为排空，权限恢复后重新读取成功；原 DeleteRecords/同名重建断言保留 |
| 最新控制组件 | **27/27、0 跳过，686ms**，`component-control-WAMEk7`。原观测组件 38/38 和真实时间来源 3/3 的未改算法证据保留 |
| 原十分钟时间风险告警 | **1/1、0 跳过，10m36.814s**，`component-time-alerts-tNuWkz`。真实采集驱动原表达式及 for=600s firing，停止负载后等待已接受发送确认，1190 的真实高水位与最终确认一致，实际组提交后计数/比值为零、告警解除；30s 独享保留、1s 已检查时间预算，摘要采样段628.924s。未向 exporter 注入比例 |
| 最新 API/Worker AOT 分析 | API 稳定托管 Native 构建 **4m49.72s、0 警告/错误**；Worker **1m12.19s、0 警告/错误**，`native-delete-policy-final.log` / `delete-policy-worker-aot.log` |
| 最新完整 API Native 产物 | `attempt-rXUnbg`，**164753984 bytes**，SHA256 `275a5c4bd034b11bd53f438c4802856edadb43f4248c364927e76904b8741218`。单次实际 Publish 的源清单80项、最终文件82项及 ILC 参数均核对；9条既有第三方白名单告警，不称零告警机器码发布；无自有代码新增 suppression |
| 最新 Native 实际双库 HTTP | **2/2、0 跳过，1m44.208s**，`component-native-api-iPtdne`。上述 ELF 配置装配、401、真实 Host 登录、受保护重放查询/JSON、错误 Topic400及总览所有权读取通过 |
| 部署/矩阵/输入门禁 | **43/43、0 跳过，10.855s**，`final-deployment-native-contracts.log`。Compose 显式默认 delete，Helm及元数据守卫反例通过；迁移列的命名/注释37项既有通过输入未变 |

`native-delete-policy-final-audit.json` 再次独立核对最新清单与实际磁盘 **1527/1527 托管项完全相同**、ELF摘要相同、207项相关运行时源码无漂移；四个发布输入守卫均为 true。SDK镜像仍为 `sha256:2b9cf51b78cffcbbae976c2260c58dcf0831647d8afec572aa6766085ff2052b` / SDK10.0.402、runtime10.0.13。其他模块后续八项 C# 变化仅记录在 manifest，未覆盖或声称其当前源码版本已认证。旧7cd产物及原拒绝/三份闭包一致证明保留为历史，不替代当前275a产物。

Native 范围仅配置、认证、运维只读和新增列真实物化。Legacy断言能抓到读取异常后降级unknown，不单独证明内部JSON逐字一致；由静态ordinal17、双库持久基线对账和独立关闭反射的Native JSON往返共同覆盖。没有扩展完整控制面、Kafka传输或Worker CDC Native支持范围。

验收复用按改变的边界执行：双库完整矩阵之后新增的 delete-only 条件只有共同 Kafka 元数据入口变化，没有改 SQL/状态机/JSON；以实际 Kafka 策略和 ACL 2/2、控制组件27/27及最新 AOT/Native覆盖新增边界，没有自动再跑整套故障矩阵或Soak。

失败保留：告警前一轮 `component-time-alerts-qDRvAH` 6m48出现unknown并失败；下一轮已真实firing，但10m31时取消 ProduceAsync 等待后仍有一条在途写入，最后归零断言失败。最终发送不再用取消等待冒充撤销写入，等待真实确认并核对高水位后提交；原阈值、零积压及解除断言未删除。新增 delete-only 源码使排队中的旧 `--resume-managed` 正确拒绝复用，随后从最新源码重新稳定构建并实际发布。

最终 Git/资源：刷新远端成功；`main / 75be6fc0e17433f9c59c42553093dacd479ef9d1`，领先4/落后0，无未解决冲突，stash `b74742b9a31f9c5db7bb6e60498345a5653ac531` 仍保留。未提交、推送、合并或启用生产开关。所有本轮测试均为明确聚焦选择；全量、前端/浏览器及客户端重生成未执行，HTTP契约未改。最终差异检查见任务临时目录 `final-working-tree-check.json`。

剩余边界：存活旧执行者失锁后迟到 REST 的可用性演练/远端fencing尚未认证；完整Native控制面、Worker CDC Native、两个实际Host.Worker长期Soak、生产等价容量矩阵、独立物理故障域和自动TTL过期均不外推为通过。既有每库60分钟WorkerParity长跑仅保留其原输入范围。部署仍须先执行迁移342，并实际配置监控权限、GTID计时、不可变Topic ID及外部时钟保证；不满足前提时保持unknown。正式切流、影子采集及CDC观测默认关闭，Worker默认LegacyPolling，保持Pilot / Capacity-not-verified。

自动安全审查此前拒绝本任务构建目录的递归清理，只返回 blocked by policy；未改用其它解释器绕过，build/focused-build/aot/native等可重建目录仍保留。成功夹具与精确归属Native容器自行退出清理；`final-resource-check.json` 对TRX记录的219个创建ID核对，当前运行数为0，精确Native发布容器也为0，本任务API/Worker子进程无残留；共享或其它任务资源未接管。原始TRX、命令、输入/源码摘要、告警firing/解除和失败证据保留。

## 实际实现与修复

- A：核对现行调用链，修正持久范围重放、BinaryDataConverter + JSON delegate、API 控制面、权限 `messaging.kafka.range_replay`、独立影子表和真实双库历史记录之间的矛盾，保留历史验收边界。
- D：移除 Capture Job=1、Binlog=168h 的假健康默认值；经 Full.NET 数据执行边界读取真实 Agent/Capture Job、Binlog 自动过期配置、producer fence。Worker 默认关闭，先校验专用 Connector 源和 EventRouter 路由，再读取真实任务、正式组消息 lag 与提交源坐标。采集串行有界，标签为固定组件/资源码。
- 新样本区分 healthy/unhealthy/unknown/error/disabled/stale，失败撤下旧健康值，单调时间控制有效期，额外观测时间戳/年龄预算供平台识别缓存的旧 OTLP 样本。未采集的 lag 或时间保留比不输出假零。
- 源窗口：SQL Server 固定 Outbox capture instance min/max LSN，零 min LSN 可能缺权限，返回 unknown；源 lag 用同库提交时间差。MySQL 真实 `SHOW BINARY LOGS` 检查文件与坐标；加密日志或异常规模不猜测。MySQL 时间 lag/Kafka 时间保留比仍 unknown，不能凭其余健康值批准切流。
- B/C 真实进程发现选项重复绑定会追加数组，现共用选项仅绑定一次、显式配置重试数组替换默认值；Connect 客户端原承诺更新却只用 POST，现用配置 PUT 创建/更新，错误不泄露凭据正文。
- 增加独立 API/Worker、HTTP/Kafka/数据库故障代理、重放检查点门和双库恢复入口，故障机制仅在测试代码和独享资源内，无生产故障接口。
- E 前置短测发现 Connect 默认 Xmx2G 与 2 GiB 容器冲突，现 Kafka 堆 512 MiB、Connect 堆 768 MiB，硬限额不变。后续定位隔离输出时模板固定向上五级寻址失败，改为将既定两份部署 JSON 嵌入 Runner，避免运行目录依赖；最新双库短测和各60分钟长跑均已通过。
- 长跑使用既有 Scope C/WorkerParity，独享数据库和管线；真实 Broker 暂停、资源采样与失败取消均有界。另补真实三 Broker、RF=3、minISR=2/3 的单节点中断/恢复，不将单 Broker 暂停称为 N+1。

## 本机实际证据（13:52 快照；后续新增见下节）

任务临时根：`C:/Users/dai27/AppData/Local/Temp/fullnet-messaging-reliability-20261028`。每轮独立目录保存输入、命令、托管闭包摘要、TRX、告警快照及失败日志，拒绝零发现、跳过、无 TRX、非零退出或运行二进制变化。未复制整仓、未创建根 artifacts。

| 范围 | 本机实际结果与证据 |
| --- | --- |
| 组件单元 | **28/28 通过、0 跳过**，`evidence/component-unit-k9QuLL`；包括真实选项组合、配置 PUT、未知/陈旧/失效语义 |
| 双库平台/源窗口 | **2/2 通过、0 跳过，29.057s**，`evidence/component-platform-AVraxq`；实际 Capture Job 启停、受限凭据、保留配置、CDC cleanup、Binlog FLUSH/PURGE、旧/新坐标 |
| 实际 Prometheus 闭环 | **1/1 通过、0 跳过，12m50.133s**，`evidence/component-alerts-RFiHGK`；原 5 分钟 Job、原 10 分钟 Binlog 告警，旧样本、exporter 503/恢复；不包含新增源窗口告警 |
| 两 API + 两 Worker 故障恢复 | **2/2 通过、0 失败、0 跳过，18m06.892s**，`evidence/process-W1tx43/process.trx`；本次固定输入的完整双库 B/C 场景 |
| API/Worker AOT 分析 | `FullNetAotAnalysis=true`、独立编译器均零警告/错误；`resume-source-api-aot.log` 5m26.45s、`resume-source-worker-aot.log` 1m22.33s；不是 Native 运行 |
| 最新关联 Integration Release 构建 | 零警告/错误，2m22.43s，`resume-alert-quorum-native-build.log`；嵌入模板已确认进入程序集；后续三 Broker 轮询修正尚未重编译 |
| 部署/矩阵 | `node --test tests/deployment/messaging-cdc-contract.test.mjs tests/testing/messaging-control-matrix.test.mjs`，**18/18 通过、0 跳过** |
| 源窗口实际告警/补强审计 | 已补场景，资源池等待中；尚未通过，不复用此前 2/2 冒充新增断言通过 |
| 独享 Scope C 短测 | 最近 **0/2 通过、2 失败、0 跳过**，`evidence/capacity-smoke-nsRUkI`；模板 IO 根因已修，原场景待复验；未启动依赖它的 Soak |
| 三 Broker | 首轮 **1 失败、0 跳过**，`evidence/quorum-apIxxV`；真实暂停后 Admin 元数据连接先超时，已改为在原两分钟内等待实际 ISR，待复验；没有计 N+1 通过 |
| 真实 Native 发布/运行 | 首轮 20 分钟超时退出 124；次轮 6 GiB 编译在目标文件生成阶段 OOM；本轮 12 GiB/2 核、45 分钟上限正在生成机器码。尚无通过 ELF/双库运行结果 |

B/C 通过范围：暂停意图持久化前后和实际暂停后崩溃、并发/重复恢复、旧代次拒绝、最终提交后 HTTP 响应丢失和 Completed 不恢复旧发布者；Worker Inbox 提交/OffsetCommit 间崩溃接管与重平衡；重放 Inbox/检查点间崩溃后原 OperationId/自然租约接管；DLQ 提交/位点间崩溃与 ReplayedAt 保持；API→Connect、Worker→Kafka、API/Worker→数据库和 Connector→数据库断连/恢复；同名 Connector 路由漂移拒绝、真实失败任务重启、固定 Broker 日志截断后 blocked 且正式组位点不变。数据库断连先被既有 JWT 权威会话校验以 401 拒绝，没有放宽认证策略。完成后又增加非空 B0 审计和精确宿主管理员 Actor 断言，须以后续源窗口批次的实际结果为准。

Native 新入口核对 SDK 镜像身份、真实 ELF、官方 warning 门禁、HEAD/源输入和 SHA；发布期间输入变化即拒绝登记当前固定输入。首轮到第二轮前有 30 项并行输入变化，第二轮期间又有 4 项变化，均未覆盖；第三轮重新登记 3,405 项实际编译输入。只验证 API 受保护运维查询/JSON/配置组合，不扩大 Worker CDC Native 或全部控制面边界。

## 执行边界与后续验收

### 最终 Native 运行结果

`native-api-KzYqcf/native-api.trx` **2/2通过、0失败、0跳过，1m25.502s**，整个测试托管闭包前后摘要一致。两库均实际启动上述Linux ELF：自定义1s/2s/3s重试配置通过装配，匿名运维请求401，真实宿主管理员登录后查询200且JSON含items数组，未登记Topic在访问Broker前400拒绝。使用独享数据库、实际迁移/开发测试种子和每个1核/1GiB的Native容器；未执行完整控制面故障矩阵、Kafka传输或Worker CDC Native，不外推这些范围。

### 18:35 Native 发布完成、运行中

两阶段 `attempt-YJp8ow` 已生成真实linux-x64 ELF，**162536960字节**，SHA-256 `f49ab6a079d49abd0f5ce67fc9b51db9b2d09f2aa3f64094e8b39f3750b5109c`。SDK10.0.402、目标runtime10.0.13、原12GiB/2核限额。首轮托管构建5m26.07s零警告错误但四个AI/Workflow文件变化而拒绝；第二轮2m06.52s零警告错误并取得稳定窗口。随后`publish --no-build`完成机器码，完整发布入口24m08.955s，退出0；仅9条ADR-0008已登记第三方ILC告警，无自有或未登记告警通过门禁。

`managedInputsVerified/nativeInputsUnchanged/managedArtifactsUnchanged/headUnchanged`均true，冻结托管输入SHA `a9b8f212916f7c01493cb747bd32f27ec02fb85f1fb7ce1c1120a44105e8758d`。机器码阶段有三个AI/Workflow C#文件变化，`sourceUnchanged=false`及具体路径如实写入manifest；该阶段不重新编译C#，实际DLL/依赖元数据、本任务源码与项目/内容输入均未变化，不认证这些其他模块的最新源码。没有复制源码或覆盖其改动。

对应Native fixture构建零警告错误、5m11.60s；此时间点启动`native-api-KzYqcf`，最终结果见上节。范围限于SQL Server/MySQL上的真实ELF配置装配、认证、受保护重放记录查询/JSON和错误Topic前置拒绝，不外推整个控制面、Kafka传输或Worker CDC Native。

### 18:11 双库长跑完成

`soak-VqO8EC/soak.trx` **2/2通过、0失败、0跳过，2h05m05.132s**；冻结程序集 SHA-256 `b5b3872c75df3d86657d18816e163281a16194dbe02027464f64ffbc03391611`，运行前后整个托管闭包摘要一致。每库60分钟、2条/秒、256B、7200条；1个 WorkerParity 流水线、2分区/RF=1。两库最终均为7200 Outbox/7200 processed Inbox/0积压，lost/duplicate/corrupted/outOfOrder/unflushed/invalidSequence 均0。

| 实测 | MySQL | SQL Server |
| --- | ---: | ---: |
| 消费吞吐，含排空（条/秒） | 2.00016 | 1.99845 |
| 端到端 P50/P95/P99（ms） | 273.079 / 501.063 / 526.622 | 4255.861 / 5457.852 / 5736.257 |
| 端到端最大（ms） | 10110.640 | 14923.322 |
| 停载后排空（ms） | 208 | 3296 |
| 采样最大积压 / 最老年龄（秒） | 11 / 5.048 | 20 / 9.859 |
| Broker 暂停/解除控制耗时（秒） | 10.069 | 10.056 |

故障定时从 Runner 命令调用前启动，30分钟后暂停本轮独享 Broker。恢复后的首个零积压采样分别距解除暂停81.139s/1856.139s；SQL Server 在持续到达期间保持批次积压，首个零样本是停载后的最终对账。该值仅是离散采样时点，不能写成精确故障恢复耗时；精确业务恢复时刻未独立测量。实际停载排空及所有消息的最大端到端耗时见上表。

每库保存115轮容器采样；每轮结束后等待30秒，实际间隔约32秒。采样最大 CPU / RSS：MySQL库3.92% / 522.7MiB，Broker15.54% / 487.6MiB，Connect94.38% / 824.4MiB；SQL Server库38.18% / 1909.76MiB，Broker15.47% / 484MiB，Connect67.16% / 634.6MiB。包含启动阶段，均在原1核及内存限额内；不是连续cgroup峰值。原始镜像ID、参数、115轮资源和数据库快照、故障时点、samples/checkpoint均保留，汇总为 `evidence/soak-summary.json`。本机有其他会话负载，不作专用硬件或生产容量认证。

Soak 复用本轮生产实现的冻结程序集；Native 验收清单格式的测试调整不在 E 选择中，长跑结束后单独构建。Native 两阶段发布与对应 fixture 构建已排队，实际发布/运行尚未通过；不改写 E 已完成的输入或结果。

### 17:08 长跑中间结果

`soak-VqO8EC` 的 MySQL 原始样本已完成60分钟：7200/7200，lost/duplicate/corrupted/outOfOrder/unflushed/invalidSequence 全为0、完整性为 true，最终数据库7200 Outbox/7200 processed Inbox/0积压。实测消费2.00016条/秒，端到端 P50/P95/P99 为273.079/501.063/526.622ms，最大10110.640ms，最终排空208ms。Broker 暂停/解除暂停控制记录10.069s；采到的最大积压11条、最老5.05s，恢复后首个零积压样本距解除暂停81.14s（持续到达及离散采样的上界，不是精确业务恢复耗时）。SQL Server 正继续执行，双库整轮 TRX 尚未关闭，不提前报告2/2通过。

### 16:04 更新

补齐既有四项指标/标签回归，组件选择 **35/35 通过、0 跳过，327ms**（`component-unit-hNudgY`），桥接项目链接仓库原测试与 `OutboxBacklogTelemetry` 原源码，不加载被系统阻止的完整 Unit AI 闭包；不计完整 Unit 通过。组件构建零警告错误，4.47s；部署/矩阵再次 **18/18 通过**。

当前生产代码冻结程序集的独享短测 **2/2 通过、零跳过，5m22.902s**（`capacity-smoke-kRlDVe`）。每库 40/40，MySQL/SQL Server 消费速率 **7.032/2.050 条/秒**；SQL Server 排空17.557s、端到端 P99 17.658s。原始完整性无失败码，数据库最终对账零积压。按同一程序集 SHA 和两库实测速率准入，已启动双库各60分钟/2条每秒长跑；当前只有启动和准入记录，尚无长跑通过结论。

### 15:48 更新

最新 B/C 多进程完整双库 **2/2 通过、0 失败、0 跳过，18m39.974s**（`process-4FBh0D/process.trx`）。包含本轮新增恢复源窗口门禁后的真实暂停/恢复路径，以及非空 B0 审计与精确宿主管理员 Actor 断言；两库各使用两个独立 API 和两个独立 Worker。保留原自然租约、故障代理、最终提交响应丢失、正式组位点与真实投影对账断言。Native 两阶段托管构建已启动，当前程序集短测在资源池等待；Soak 未启动。

15:59：两阶段 Native `attempt-h0eujJ` 三次托管构建均零警告/错误（4m25.37s、4m03.35s、2m10.44s），但前后摘要均有并行输入变化；第二至三次登记间包括 Workflow SQL 源码变化。按上限结束，没有生成机器码或登记 Native 通过，不修改其他模块。先继续当前固定程序集短测与依赖它的双库长跑；长跑后再尝试稳定窗口。

### 15:26 更新

独立源窗口实际告警 **2/2 通过、0 失败、0 跳过，11m26.991s**（`source-alerts-dTu4y9`）。MySQL 真实 FLUSH/PURGE、SQL Server 真实后续 CT 提交及 cleanup，使原 Connector 提交坐标失去恢复窗口；原两分钟告警均 firing。新建隔离 no_data 诊断基线后真实指标为 0、告警解除，原 Connector 保持暂停。四组源指标/告警快照分别保留两库丢失与恢复状态。这是采集和告警闭环，不把诊断新基线当成原业务缺口恢复。

第四轮 Native `attempt-f7sIpj` 在托管编译期间检测到三个 AI 模块文件变化，按精确容器标签停止，保存变化摘要，不计通过。入口改为两阶段：同一只读挂载源码的托管 build 前后摘要必须一致（最多三次有界增量重试）；随后 `publish --no-build` 从冻结 IL 生成机器码，并核对全部托管 DLL/依赖元数据、项目/配置/内容、本任务源码及 HEAD。其他模块 C# 后续变化单列 `sourceChangesDuringNative`，`sourceUnchanged` 如实为 false，不能冒充其当前版本；不复制源码、不放宽 ILC warning/ELF/实际运行门禁。依据 [.NET CLI no-build 文档](https://learn.microsoft.com/dotnet/core/tools/dotnet-publish#options)及本机 10.0.13 Native Publish targets，实际两阶段结果仍待运行。

### 15:20 更新

恢复路径补充真实源窗口门禁：在原配置身份匹配后，resume 前及全部任务 RUNNING 后都确认原 Connector 已提交源坐标仍可恢复；未知、权限不足或日志丢失时保留 producer fence。组件控制面 **22/22 通过、0 跳过，840ms**（`component-control-CWucMn`），新增恢复前 false/unknown 及恢复期间日志丢失的三项断言。最新 API/Worker AOT 分析分别 **13.36s/11.62s，零警告、零错误**（`source-guard-api-aot.log`、`source-guard-worker-aot.log`），不是 Native 运行。

独立源窗口告警前一轮 `source-alerts-oC8NL2` **0/2 通过、零跳过，9m35.769s**：两库均实际触发原两分钟告警，但隔离新基线的配置复制保留了旧 `name`，Connect 正确拒绝 PUT，整轮失败。现明确写入新名称，正在按原断言复验；旧 Connector 始终暂停，新基线只验证观测恢复，不能算旧业务数据恢复。随后连续复验补强审计/恢复保护的 B/C 与同程序集短测，未提前启动 Soak。

### 14:54 更新

Windows 于 14:26:22 重启（System 41/6008），最后两项编译被中断，部分日志/资源锁出现空字节。源码、HEAD 与 stash 核对完整；没有把中断构建计通过。核对旧 PID 均已退出、相关独享容器无活动后，将六份重启前失效锁归档到 `reboot-20261028-1426/` 再清理；未触碰重启后新锁、其他模块源码或共享容器。Docker Desktop 恢复后用 `--no-incremental` 重建关联程序集，**零警告/错误，5m52.34s**，`reboot-full-integration-build.log`。

当前组件 **31/31 通过、0 跳过**（`component-unit-yjNXMF`），新增在途刷新保留新鲜源告警/lag 样本、失败撤下和抓取退避配置回归。原源窗口混合场景 `source-alerts-LuoMg2` **0/2、零跳过，23m35.090s**；两库均执行完补强审计和 B/C 核心断言，但整批失败，不能报告新增整批通过。MySQL 已观测真实不可恢复状态，却因采样前撤下新鲜值造成告警连续性问题；现改为采样完成后替换、已确认失败时撤下，保留原 TTL。SQL Server 场景缺少后续实际 Outbox 捕获行，未满足清理前置；已改为独立双库诊断事件专项，使用真实后续 CT 提交 LSN，不伪造值或改低告警阈值。

三 Broker 专项 **1/1 通过、0 跳过，42.765s**（`component-quorum-F5jLnL`）。RF=3/minISR=2 在暂停一个节点时确认并消费 20 条；minISR=3 副本不足拒绝确认，恢复后原 2 条消息与实际尾部对齐；故障起点至恢复确认 30.652s。仅证明同一 Docker 主机的单节点暂停/恢复，保持 `Capacity-not-verified`。

10ms 抓取退避候选的原双库短测 **2/2 通过、0 跳过，5m13.846s**（`capacity-smoke-xXXFMJ`）。每库 40/40 对账；MySQL/SQL Server 实测消费速率 5.981/4.150 条/秒、排空 4.724s/7.674s、P99 4.655s/7.655s；最终数据库分别为 40 条正式负载 Outbox、40 条 processed Inbox、0 积压。前后均为相同本机/镜像/资源限额/负载，但期间发生重启和并行负载变化，不作为严格隔离的生产性能倍率认证。准入函数已用旧低吞吐基线验证拒绝、用新基线验证通过；60 分钟 Soak 尚未启动。

本次只进行自查：核对 producer 事务共享锁/fence 与变更排他锁、generation/CAS、终态及 B0 审计事务、提交未知后的新连接权威读取、Completed 不 resume、重放 lease/意图 CAS 与正式组位点分离、Host 精确权限和可信 actor、资源摘要/源/路由/集群守卫、默认关闭及双库只读查询。未充当独立评审；待执行结果以各原始报告为准。

### 14:05 新增真实基线与停止原因

嵌入模板后的 Scope C 双库短测 **2/2 通过、0 跳过，6m07.982s**，`evidence/capacity-smoke-sFOGkY`，每库 40 条均完整确认和消费，无丢失/重复/未知结果。但 MySQL/SQL Server 排空分别 38.512s/38.943s，消费实测速率 0.988/0.978 条/秒，P99 约 38.76s/39.14s，不能支撑登记的 2 条/秒长跑。自动 Soak 在取得任何资源/启动子进程前停止，`soak-admission-stopped.json`；新准入守卫已用该真实基线验证拒绝。未降低长跑负载或放宽完整性断言。

已定位消费配置 `QueuedMinMessages=1` 与底层默认 1000ms 队列抓取退避组合，新增显式 10ms 退避候选，保留串行处理、队列内存上限、幂等与提交语义；实际改善须用相同双库场景复验，尚未宣称性能提升。配置依据 [librdkafka 官方配置](https://github.com/confluentinc/librdkafka/blob/master/CONFIGURATION.md?plain=1)，本机 Confluent.Kafka 2.15.0 提供对应属性。

第三轮 Native 在发布期间又检测到 4 项并行 AI/Workflow/命名输入变化。按精确标签确认本任务容器后停止本轮，`native/api-on0Wyy/attempt-Loaa0a/input-change-stop.json` 保存原因和变化路径；无 ELF，不计通过，也不将此轮当成 12 GiB 足够的证明。后续只在新的稳定输入上复验，不改写其他模块或放宽输入门禁。

主机 Intel Core i9-14900K、32 逻辑处理器、约 63.69 GiB；Docker Engine 29.9.1 上限约 31.19 GiB。SDK 10.0.402/runtime 10.0.13、Node 24.20.0、pnpm 10.26.0。构建使用资源池、2 核/3 GiB、`-m:1`、`UseSharedCompilation=false` 和独立输出；重型验收等待所有构建名额。SQL Server 独享 3 GiB/1 核、MySQL 1.5 GiB/1 核，Kafka/Connect 各 2 GiB/1 核，Prometheus 512 MiB/1 核；三 Broker 每台 1536 MiB/1 核。Native 原 6 GiB 失败后改为资源池预留 12 GiB/2 核，未修改共享预算或其他任务资源。

```powershell
$taskRoot = Join-Path $env:TEMP 'fullnet-messaging-reliability-20261028'
node scripts/testing/run-build-with-resources.mjs --cpu 2 --memory-gib 3 --isolated-output "$taskRoot/build" -- dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --no-restore --nologo '-m:1' '-p:UseSharedCompilation=false' '-v:quiet'
node scripts/testing/run-messaging-reliability.mjs --stage process --build-root "$taskRoot/build" --output "$taskRoot/evidence"
```

后续各批使用同一入口的 `source-alerts`、`capacity-smoke`、`soak`、`quorum`、`native-api`；Native 发布使用 `run-messaging-native-publish.mjs`，实际命令分别保存。桥接组件只链接仓库原测试和真实生产项目，不复制源码。

Soak 预登记每库 60 分钟、2 条/秒、256B、7200 条、1 WorkerParity 流水线、2 分区/RF=1，第 30 分钟暂停独享 Broker 10 秒，排空 180 秒，每 30 秒采样三个独享容器；参数、停止条件、阈值来源和剩余边界见[Runner 文档](../operations/kafka-capacity-runner.md)。只有真实运行通过才能记录时长、吞吐、分位数、积压、恢复和资源峰值。多进程恢复结果不证明两个 Worker 长稳，Broker 截断不证明 TTL 自动过期，Docker 三 Broker 不证明独立物理故障域。

## 保留的较早失败事实

同步前基线 `2fe0d52bab8441c528b251ff28fa652b8dd76c7f`、快照 `messaging-operations-continuation-20261028`。完整 Unit/Integration 当时已构建；完整 Unit 程序集聚焦发现被 Windows 应用程序控制以 `0x800711C7` 拒绝 AI DLL，无 TRX，不计通过。原组件 21 项/双库 2 项通过，后来被上表新证据替代。Docker 后来停止导致首次真实告警无法开始；用户恢复 Engine 后按原规则实际通过。Roslyn 共享编译器出现 NullReferenceException/进程崩溃，独立编译器复验通过，未关闭分析器或系统策略。

同步前分片发现1543，与当时canonical 1455不一致；没有运行全量或改门槛迁就发现值。后续为新增三Broker 1个、独立源窗口告警2个用例精确维护对应分片/全局最低值，未声称解决其他会话带来的整体发现漂移。早期多进程失败还包括数组追加、POST更新409、Debezium在注册时直接拒绝错误密码；现用真实数据库断网/任务重启覆盖目标故障，不删除断言或改成跳过。

剩余门禁影响正式切流：独立复核，以及尚无可信来源的MySQL时间lag/Kafka时间保留比。适用Native配置/认证/运维只读片段、双库长跑、最新B/C、源窗口告警和三Broker已有上列实际通过记录；完整Native控制面故障矩阵、Worker CDC Native、生产等价容量矩阵、独立物理故障域、两个Host.Worker进程长稳及自动TTL过期均未认证。全量测试、前端构建/浏览器验收及HTTP客户端重生成未执行：本轮只选择直接相关验证，未改变前端或HTTP契约。原完整Unit发现受应用控制阻止的事实保留，组件57项不替代完整Unit通过。

## 资源与清理结果

成功测试的独享数据库/管线/Native容器均已随夹具或精确归属清理退出；未删除其他会话的容器、共享镜像或源码。TRX、metrics/backlog/resources、参数、命令、源/托管闭包摘要、镜像ID、失败记录与小型重建recipe保留在本任务系统临时根。

可再生编译目录的递归清理被自动安全策略两次拒绝：先是经过路径与使用检查的批量PowerShell命令，再是单独核对后、使用完整字面路径的`aot`目录清理。工具仅返回`blocked by policy`，未给具体规则。删除均未执行，未换解释器或工具绕过。因此仍保留本任务根下`build/`、`focused-build/`、`aot/`、`native/api-on0Wyy/build/`和`native/api-on0Wyy/publish/`；精确路径/大小/拒绝事实见`cleanup-result.json`。清理前检查无相关进程或活动挂载引用，但策略拒绝优先；不报告已清理二进制。
