# 日志模块整改计划

## 后续批次：B1 与集中投递容量（2026-10-19，本地验收完成）

按项目所有者要求，将剩余本地容量合并为连续批次。沿用已通过的 Local `.txt` 验收，不重复 Native 发布；生产 B0/B1/B2 语义、认证、租户、日志默认配置保持原约束。任务快照为 `logging-remaining-capacity-20261019`。

- [x] 补齐统一有界持续档与必要测试入口。显式 `FullNetLoggingCapacity=true` 才编译容量类，日常默认发现不加入长时压力，也不以跳过冒充通过。Collector/ApplicationKafka 使用同一真实 Kestrel 请求档，500→1000 请求/秒、每档 30 秒、200 次预热，正反序各一轮；保留短档回归。TLS/私有 CA、可信 Collector 路由、独立消费者和真实 ES 不用替身；容量容器在创建时声明内存/CPU/零额外 swap、独享资源会话及清理归属。
- [x] B1 使用真实 Host 协调器与正式 Dapper/事务，SQL Server/MySQL 分别运行 1、8、32 并发、每档 30 秒，复测 32 并发一次；Operation 每调用一条，10% 另带 Exception。核对逐条标识/内容、数据库行数、实际接收/失败/批次及队列预算，等待指标与队列水位完整记录。实际 2/2 通过、八组完整，调用 P99 最大 88.8923ms，队列预约归零。报告的是 B1 调用等待 P50/P95/P99，不冒充 HTTP 请求 P99。
- [x] 集中构建和执行受影响定向回归、B1 双库及八组集中投递样本。预设健康预算：非预期响应/丢弃/写库失败为零，实际持续窗口足够；B1 调用 P99 <500ms、排队年龄 <2 秒、最终预约归零；投递路线实际速率至少目标 95%、HTTP P99 <100ms、计划完成 P99 <250ms，发送窗口内有明确实测记录得到下游提交，结束时 ES/Offset/DLQ 全量对账。资源/正确性失败停止依赖场景，超预算保留原结果，不放宽阈值追认通过。接受配置下各样本通过，按未变闭包复用和定向复验对账，不宣称同次全部绿色。
- [x] 以实际硬件、并发、字节吞吐、CPU/内存、资源归还及失败边界更新日志模块说明与本计划，见[本批验证记录](../../verification/2026-10-19-logging-remaining-capacity.md)。最小原始时序/收据保留，自有容器与匿名卷全部归还；两个旧 Temp 目录的递归清理被命令策略拒绝，保留失败数据且未绕过，不声称磁盘全部清空。多实例混合业务、长期 Soak、2K/5K/10K 仍按各自实测范围报告，不从本批外推。

最终收尾：B1 八组共 98,410 条 Operation、9,838 条 Exception；集中投递八组共 180,000 实测请求及 181,648 个最终 ES 文档，位点、内容及零 DLQ 全部对账。接受样本 HTTP P99 为 2.6971–4.4718ms、计划完成 P99 为 15.8261–116.3962ms，最低窗口确认 96.98%。Consumer 显式批次 64/原始 Value＋Key 合计 512KiB；Collector 使用可选 200ms 低延迟配置。500/s 两路线为 ES 一核/测试树两核；1000/s Collector 为 ES 两核/测试树两核，ApplicationKafka 为 ES 两核/测试树四核，树内存均 6GiB。这些是实际接受配置，不是最低硬件保证；默认批次仍 1、默认采集周期和部署开关均未改。

相关单测 53/53、Helm 6/6、真实 Consumer 9/9、短回归四轮按原始完整结果成立；最终默认 Release 0 警告/错误，正式五分片 1,332 项完整互斥，父进程退出 0/Job0/无强杀。按 UID、源/构建守卫、原始分位数及 12 个压缩收据哈希完成独立对账；只读核实五阶段 57 个自有容器、79 个匿名卷均消失，共享资源窗口已归还。本批容量余项关闭，未测业务、平台与规模保持 `Capacity-not-verified`。

以下保留本批历史执行与决策，其中“下一轮”“待实测”均为当时状态，最终状态以上述接受配置和验证记录为准。

执行诊断：首次编译缺少三处 `PullPolicy` 导入，修复后构建通过；随后持续 CRI 桥的固定 10K 测试上限触发，改为按档显式有限预算（全局最多 60K），默认短档仍 10K。下一轮边界测试通过，但首个 Collector 持续样本计划完成 P99=428.7413ms 超过既定 250ms，虽然全部 15,206 个文档及位点最终一致也不能计容量通过；独立短回归遇到冷启动 `Not coordinator` 观察查询失败。原失败与真实退出/清理记录保留。当前补充预热 200 条的真实下游位点确认后才开始测量，只对三种明确的协调器未就绪查询错误继续有界观察，原发送窗口内提交断言和容量阈值不变；拆分采集排空、收据读取、内容校验及位点追赶计时，避免把测试校验时间当作消费延迟。根据已观察每组运行时长，容量测试执行期限在下一次启动前定为 45 分钟，允许独立预算失败的剩余样本继续采集，最终仍按全部预算判定，正确性/资源失败立即停止依赖项。

真实瓶颈与追加实现：全链预热后 Collector 的计划 P99 已降至 17.423ms，但逐条内容校验仅 5.562 秒、最终位点追平为 86.916 秒；同环境 ApplicationKafka 内容校验 0.918 秒、追平 88.432 秒。两个 500/s 完整样本均逐条对账，却证明现有批次 8 跟不上持续输入。已主动停止后续依赖压力，保留两组完整基线与取消记录，不能把未结束 MTP 当通过。下一批先验证 64 条、跨分区前缀确认的回归，再将可配置批次上限扩至 64，同时以 512KiB 原始载荷及 Key 总预算保留内存/ES 请求余量；默认批次仍 1，容量候选显式 64，短回归保留 8。Host、协调器、Helm Schema、测试和说明同步更新；独立 Consumer 为 JIT 且没有 Host.Api 生产项目引用，不重发未变 API/Worker Native。容量增加窗口内每 500ms 的真实提交进度，要求末次窗口内观测距结束不超过 1.5 秒、至少 95% 实测记录已确认；最后位点追赶等待须小于 5 秒，并继续完整 ES/Offset/DLQ 对账，禁止只看请求 P99 和最终可追平。资源、记录大小、HTTP/计划 P99 及其他原阈值保持。

追加执行与采集配置：64 条旧实现 RED 已证实；新实现相关单测 53/53、Helm 契约 6/6、真实消费者可靠性 9/9 均通过。独享容器删除时 Docker stats 的单行/连续多行 EOF 曾被临时监督器误当致命错误，保留真实 Job 中止与未完成矩阵，不当作生产失败或通过；采样仅在证明 Docker 守护进程仍可用后将纯 EOF 记为缺样，归属/限额/OOM/Ryuk 仍严格停止。首轮批次 64 的 Collector 500/s 位点追赶降至 3.523ms，但窗口末只确认 13,516/15,000，95% 门槛仍未过；ApplicationKafka 为 14,950/15,000，追赶 1.725ms。固定 [Fluent Bit 4.1.1 源码](https://github.com/fluent/fluent-bit/blob/v4.1.1/plugins/in_tail/tail.c#L568) 定义 Tail 进度检查默认两秒，并支持秒/纳秒组合；本机跨系统绑定文件的检查与默认一秒输出周期可能叠加，这是待实测的定位线索。新增可选 `fluent-bit-low-latency-values.yaml`，仅把输出与进度检查均设为 200ms，原默认、可信路由、分片拼接、缓冲和输出策略不变；静态契约和真实完整八样本复验后才准入。不降低原 95% 或延迟预算，不把更高文件系统调用频率外推为大文件数环境能力。

资源分档复验决策：低延迟 Collector 配置下，ES 1CPU 的完整八样本已全部完成内容/位点对账，500/s 四组均达到全部预算；1000/s 三组窗口确认仅 83.51%–92.17%，一组 97.59%，ES 观测接近满核，不能把高档判为通过，也不推断 1CPU 永远不能达到该速率。当前先完成不变短回归与默认程序集归还，随后仅对 1000/s 四组使用预先声明的 ES 2CPU/2GiB 配置复验，Consumer 仍 64 条/512KiB，Kafka/Collector/Windows Job 预算不变，95%/P99/尾等待等阈值不变。500/s、可靠性 9 项及单测/Helm 原有效证据按实际未变范围复用；文档分别记录一核失败与两核结果，不将环境扩容写成同资源软件提速或最低资源保证。

高档进程树分离预算：ES 两核后四组后端进度均超过 95%、尾等待均小于 5ms，但 ApplicationKafka 的计划完成 P99 为 560.695/782.954ms，完整 MTP 仍失败（两个预算失败，非请求错误）。请求、负载发生器、独立消费者和父观察器原先共同受 Windows 进程树两核硬配额约束；请求进程单独消耗约 32.8/34.7 核秒，新增 ES 吞吐也增加消费端 CPU，两核共享不能冒充独立应用两核配置。下一次只复测不通过的 ApplicationKafka 1000/s 两轮，测试进程树硬限预先声明为四核/6GiB，仍独占重型及全部构建名额、每 .NET 进程 `DOTNET_PROCESSOR_COUNT=2`，ES 仍两核、Kafka/Consumer 批次/内存及所有阈值不变；应用 CPU 指标继续明确是两核归一值，并不称为应用进程独享两核硬限。Collector 高档、500 档、原短回归、53/6/9 有效证据按未变闭包复用，原两核失败完整保留。

## 后续批次：日志本地容量扩展（2026-10-19，本地验收通过）

本批按项目所有者要求继续容量验收，独立于已闭合的 F01—F09。`cache-capacity` 仅覆盖受保护 Grid 热键，不能代替日志逐条接收、丢弃与投递对账。前序容量批次实际失败并归还资源后，本批使用正式资源池与真实 Windows Job 接续执行；不重复 Native 发布或已通过的无关用例。

- [x] 复用真实 Kestrel/B2 请求探针，增加 `logging-local-capacity --output <新的系统临时子目录>`。显式启用正式 Local `.txt`，统计器支持有界 60,000 样本与显式发送期限；保留 HTTP、调度落后、逐条相加的计划完成时序及文件摘要。每档预热 200 次，固定 8 个发送循环，以 500→1000 请求/秒、每档 30 秒测量 Disabled/Summary/Projected，正反序各一轮；不把发送循环或目标速率当作实际在途。
- [x] 正常 Release 构建及定向回归完成。首轮 NU1900 是官方主索引 TLS 失败，未运行测试，不计 RED；任务临时配置改用官方独立漏洞数据源，保留包源、locked restore、全依赖审计及告警即错误。恢复后真实 RED 为精确 6 项 5 通过/1 失败（旧万条上限）；GREEN 6/6、0 失败/跳过，正常增量构建 1m22.11、Benchmark 构建 11.67 秒，均 0 警告/错误。
- [x] 按预设预算执行全部 12 个样本，270,000 实测请求通过；180,000 实测 HTTP 日志及 1,600 预热日志完整落盘，非预期响应与丢弃均为 0。开启日志的 HTTP P99 2.5552–3.4427ms，计划完成 P99 15.9104–17.6353ms；两核 CPU 最高 14.66%、进程寿命工作集峰值 275.17MiB。实际服务端在途峰值 3–6，日志档均为 3。独立重算原始数组、日志统计/文件摘要、TRX/UID、预算及输入/终态通过；父进程真实退出 0、Job0、无强制停止，重型锁及成功临时日志已实际清理。
- [x] 已更新运维说明与[本批验收记录](../../verification/2026-10-19-logging-local-capacity.md)。最终 consumer `ae04189c45d38049f6d8d0a08f0ed3affcc444bee4ad22917b52990e15464ea6` 前后相同，生产 Native 输入仍为 `9bea866fedd21a184f37867dfca16089cfa53d0ebf90f768fd85054d921782b8`；与前序 `6cb7bad…` 相比只有五项基准/定向测试输入变化，未修改生产路径。完整来源映射追加保存，原 RED/GREEN 阶段收据及失败记录保留。

此 Local 批次自身仅关闭 Local/B2 有限容量验收，不代替 B1 或集中投递容量。后续 B1 持续写库及两路线分档扩容已按上节单独验收；多实例混合业务与平台 2K/5K/10K/Soak 仍未执行，保持相应 `Capacity-not-verified`，不是本批勾选完成项。

以下为原整改批次记录，其已验证范围不因新增容量批次而撤销。

状态：2026-10-19 本轮 F01—F09 日志功能整改及受影响本地验证已收尾。原核心 Native 完整 44 项为 43 通过、1 失败、0 跳过；修正正向夹具后，受影响退休 4 项补验 4/4、0 失败/跳过。不变 40 项原通过证据与补验 4 项按精确 UID 对账覆盖 44 项，不伪称原会话 44 项全绿；原失败与未知早退阶段保留。准入、请求 P99 特征、持续配额、Linux 日志 404 项、正式 Profile40 及既有发布保持各自已验证范围；未测业务规模与容量仍为 Capacity-not-verified，归档等后续扩展不属于本轮交付。

历史续验（2026-10-18 20:25，上海时间）：当时 Integration 已完成正常构建、产物复制及正式身份登记，准备阶段通过；Core 发现 44 项，实际执行的前 2 项因测试日志父目录被只读挂载而失败，随后取消，其余未验。该问题属于本轮测试环境配置，不能计为原生通过。资源已实际归还；修正后的候选增加发布目录只读与同级日志目录可写的实际探针，29 项控制检查通过。下述历史准备失败记录保留，最终完成状态仍以 Core 实际结果为准。

历史状态（2026-10-18 22:29，上海时间）：调度 v2 准备通过，Core 发现 44 项、实际完成 11 项均通过；SQL Server 缺少内部内存预算且识别 15,806MB，实际逼近 3GiB 容器硬限额，迁库停滞后有序取消。未延长原截止或修改冻结输入，不计整组通过。actor 退出 1、Job 活跃数为 0，自有 SDK、三个 session 容器及四个卷均实际清理，自有锁释放。修复仅涉及 Native 测试夹具及回归；等待共享 helper 稳定后合并进行一次正式构建与重验，已有必要 API/Worker 发布产物继续保留给实际消费者。

历史进展（2026-10-18）：五个主分片完整互斥共 1,323，正式检查通过；矩阵 Node 7/7、治理 84/84。准入矩阵 24/24、Kestrel HTTP 六组及持续配额八组均完成对账；Summary/Projected 请求 P99 2.35–2.92ms，仅为约 500/s 的受控探针。配额默认 20% 四轮均保留 100/100 优先事件，关闭时为 2/100，代价是最终少保留 99–105 条普通事件；普通丢弃和文件出口 P99 的不利结果保留。Linux 日志修订后 404/404、零失败/跳过；API/Worker ELF 为 140,009,728 / 88,879,224 字节，9/7 条现有允许依赖告警通过正式检查。只余核心 Native 44 项重新验收，不能据发布或局部通过宣布模块收尾、全业务请求 P99 或万级容量。

## 受影响 Native 补验与收尾（2026-10-19，本地验收通过）

统一写者集中应用容量五项、退休夹具及稳定 Wiki 七项真实 broad 输入，另将运维说明与本计划作为两项独立 docs 映射；九个文件逐一核对 before/after，聚焦守卫 41/41、零失败/跳过。实际 consumer 源为 `301c6414ba6a5c72eb0b3805c7d9eb97960112a04657a1718534b1a2d7fbb3d4`，相对原 `7508654a…` 恰好七项已知变化；生产 Native 源仍为 `9bea866fedd21a184f37867dfca16089cfa53d0ebf90f768fd85054d921782b8`。不修改原 producer、原构建记录或发布来源。

使用独立 `/src/.tmp` 记录目录，经正式 verify 真实拒绝后正常全 ProjectReferences 增量构建 Integration，耗时 2 分 34.12 秒、0 警告/错误。新记录 identity=`b4af90f37b1e5e4f7d9b01d72bc29e6aa07df6764889638ec3da64ea8bca3b11`，output=`2b361534e11c2eb0646786f665be738eae2eb7849db8e7058e199f44e694df7e`，实际 DLL SHA256=`0a6fa00550f3b8a91ed64a2170b3753bfcb6d0e4ee390ddbf9ef21e9dc582d01`。准备阶段真实退出 0、Job0 后确认 SDK Stopped，再取得新的重型租约，没有覆盖旧记录或以缓存存在冒充可复用。

实际执行 `dotnet /work/jit/bin/Full.NET.IntegrationTests/release/Full.NET.IntegrationTests.dll --filter FullyQualifiedName~Full.NET.IntegrationTests.NativeAot.NativeWorkerKnowledgeRetirementE2ETests --minimum-expected-tests 4 --timeout 15m`，并保留顺序运行设置、独立结果目录与正式 TRX。发现及结果精确对应原 SQL Server/MySQL TXT/PDF 四个 UID，实际 4/4、0 失败/跳过，耗时 5 分 18.269 秒；TRX SHA256=`35c838fb88466330efbba5fc16144215c2c14080aa36abecc54809af87292c7b`，官方完整 TRX 校验通过。原 Main 全量 Native 矩阵仍为 44 项/90 分钟，未缩范围或断言。

本次四项原调用均正常完成，完整已存代次恢复分支没有实际触发；不得据此声称已完成该早退恢复分支的故障注入覆盖。夹具只对权威完整 stored proof 成立的状态支持原绑定恢复一次，并立即限制模型调用数不得增加；原计费、指针、载荷、隔离和 Native Worker 实际清理断言全部通过。原批失败不是已证实的生产日志故障，也仍不唯一归因租约余量。

原完整 TRX 的四个受影响 UID 全部以本次正式四项结果复验；其余 40 个 UID 在原批全部 Passed，生产 Native 闭包未变。`coverage-after-remediation.json` 逐 UID 核对两份发现及 TRX，合计 44 个唯一 UID 的最新相关结果通过，明确 `originalFullRunPassed=false`。保留原 43/1/0 TRX，不合成绿色 TRX、不把补验冒充同一会话全绿。无需重发未变 Native、重跑 Unit404 或重审不变依赖图。

补验监督父进程于北京时间 03:46:36.518 持柄退出 0、Job 活动进程归零，无强杀。正式构建记录前后校验，guest 的源/发布/控制 finally 末检通过；独立归还检查再核 consumer301c、Native9bea、30 项控制及 API102/Worker93 全发布文件和清单哈希不变。自有新 SDK、夹具及匿名卷严格清理完成，原 Cache SDK 保持 Stopped、共享 work 卷保留。最小证据位于系统临时 `fullnet-logging-closeout-20261018/retirement4` 的 TRX、正式记录、进程终态、`post-run-guards.json` 与 UID 覆盖对账；动态结果仅更新 docs，稳定 Wiki 不再改变 broad 输入。

- [x] 正常消费者构建、独立记录及源/产物末检完成。
- [x] SQL Server/MySQL TXT/PDF 退休 4 项通过，原不变 40 项与补验按 UID 完整对账。
- [x] 正式退出、严格资源清理、任务差异及文档结构检查完成。

## 原批完整执行记录（2026-10-19，失败原件保留）

Core 在开始前固定 90 分钟测试、93 分钟执行及 3 分钟清理预算，保留完整过滤、44 个唯一 UID 和全部断言。实际从北京时间 02:29 启动，完整运行 51 分 48.344 秒，正式 TRX 为 43 通过、1 失败、0 跳过，不再因旧 45 分钟时限中止。TRX SHA256 为 `aa601c49bf9ccd2c8f5f58d25439ddffe0b80e98350c31de64cda53df1267f5d`，结果 UID 与同次发现的 44 项完全一致。失败原件保留，不把后续补验写成原会话 44 项通过。

通过范围包括 API 17 项、API/Worker 的受限 Kafka Topic 日志、本地 `.txt` 写入及停止释放、双库 HTTP/SignalR/工作流、Worker 的后台处理、PDF 恢复及租户拒绝。唯一失败为 `SqlServer_native_worker_retires_superseded_generation (False)`，UID `1a5f46da-9ffc-8c3e-a7c3-fecc8e41b773`；对应 PDF 与 MySQL TXT/PDF 均通过。失败发生在真实 Native 退役 Worker 启动前的 JIT 生成准备，不能直接归因日志管道。

自有 SQL 只读诊断显示失败的租户版本 2 仍 running、原租约和会话有效，完整清单、批次与向量已持久，预算 known/succeeded、已结算 7 个输入 Token。30 秒租约未续租，返回 false 的时间接近需保留 25 秒安全余量的门槛；这是定位线索，具体早退阶段仍未知，不认定唯一根因，也不修改生产租期、权限或计费规则。

已审夹具候选只在原调用返回 false 且 `IsTextGenerationStoredAsync` 完整权威证明成立时，以相同原会话、任务及租约，经现有 generation 正常恢复最多一次；完整已存证明不代替激活授权。恢复后立即断言模型调用数不增加，原活动指针、两次计费、预算审计、载荷、隔离及真实 Native Worker 清理断言均保留；证明缺失、恢复失败或异常仍失败。候选须与容量五项和本轮稳定文档由唯一 Main 写者集中合并，再以独立记录目录正常全引用增量登记，执行受影响退休 4 项。当前尚未登记新消费者或通过补验。

环境为同机 Docker 的 Linux x64、SDK 10.0.402；测试 SDK 4GiB/CPU2/无额外 swap，SQL Server 容器 3GiB、内部 2048MiB。Windows 监督 Job 4GiB/CPU2，实际持柄父进程退出 1、Job 活动进程归零，没有强杀。新 SDK 与自有夹具/匿名卷完成 8 项严格清理，原 Cache 容器保持 Stopped、共享 work 卷保留。运行后独立核对 consumer 源 `7508654a…`、Native 源 `9bea866f…`、26 项控制及 API102/Worker93 全文件与清单哈希，均匹配。正式 Profile 新断言 40/40 的共享消费证据单独成立；无需重发未变 Native 或重复 Unit404。

最小原始证据位于系统临时 `fullnet-logging-closeout-20261018/core90` 的原批 TRX、发现/失败摘要、进程终态、`post-run-guards.json` 与严格清理收据。后续补验须分别记录新 source/identity/output、实际 4 UID、恢复分支是否发生及无新增模型调用的断言；只能对不变的 40 个 UID 复用原通过证据，不伪造旧记录或合成同轮 44 项绿色 TRX。

- [x] 原始完整 44 项、唯一失败及资源归还已核对。
- [x] 统一最终源码后，正常消费者登记及受影响退休 4 项补验已完成，结果见上节。
## 依据与目标

2026-10-19 00:25 累计预算定位与下一策略：只读核对现存99,267字节旁路文件，按[官方长度前缀格式](https://raw.githubusercontent.com/microsoft/testfx/main/src/Platform/Microsoft.Testing.Extensions.TrxReport/Streaming/TrxTestResultSerializer.cs)取得31条完整诊断记录，UID/显示名与同次发现的前31项逐一匹配，无未知、重复或残缺尾部。API17项累计1541.459秒、Worker14项累计1156.356秒，合计2697.815秒（约44分58秒），相邻记录最大间隙0.004秒；最长两项AI为338.095/291.026秒，最后SQL PDF为166.742秒、结束于取消点00:04:51.145。现有证据支持累计时限不足，无单项占用整段45分钟的迹象。13条无完成记录为MySQL PDF1、Prompt2、SQL Worker8和租户准入2；“无完成记录”不等于全部从未开始。这份诊断不转换成TRX、不恢复会话成功状态，不作31项正式验收包复用。

据此将正式Native矩阵最低发现数17修为实际44，会话上限在下一次启动前固定90分钟，候选执行93分钟、清理3分钟；过滤、各项断言、P99与完整UID对账不变。Native治理及矩阵检查19/19通过。此后矩阵整体SHA为88c01694，只有Native minimum/timeout两字段相对已通过的canonical1f20c4b4变化，`matrix.integration`部分SHA3043401b保持完全不变，原1332项正式分片证据按其相关输入复用，不伪改旧收据。新`core90`候选35项控制检查、六项JS语法及PowerShell AST通过，尚未冻结当前消费者或创建SDK；下一重型按协调顺序先给缓存短批，再一次正常构建/新Profile40/完整Native44，不为诊断再跑，也不重复已有发布或Unit。

2026-10-19 00:12 最新续验结论：本轮原 45 分钟会话时限取消，框架汇总 Aborted、31 通过、0 失败/跳过，时长 46 分 51.226 秒，未形成完整 TRX；其余 13 项未完成，44 项门禁仍未过。actor 61300 于 00:07:04.339 真实退出 1、Job0/98，无 Job 强杀；SDK、session 容器及自有匿名卷共 11 项清理确认，源 4547 项与 22 控制末检不变，18 项补充也全匹配。旧截止和旧快照未延长或改写。下一完整运行须在开始前依据实际范围声明足够预算，不再沿不足的 45 分钟反复取消；场景、断言、P99 标准与 44 UID 完整性不降低。

Core 完整物理清理后，本轮实际 DLL SHA `ad2c66f5cf671978c6f3c29c0869646a13acaecde97e275d73fd336539432798` 在 SDK10.0.402 下真实发现 full1332/infrastructure288，原始 JSON 保留。正式校验器仅增加可选 `--assembly`，默认路径和全部数量/UID/迁移双库检查保持；私有候选先 RED4→GREEN4，再在 Main 执行参数及既有矩阵测试 11/11。正式 CLI 使用该实际 DLL，五分片 202/202/560/288/80 合计1332、完整互斥，迁移子组250/310亦互斥完整；元数据和程序集前后哈希不变。没有为数量登记重新编译或手改旧构建记录。

同一安全边界补资源账本 `sqlServerMemoryLimitMb`：SQL 声明 2048、其他角色 null，原四角色用例增加断言但不增加 UID。轻量链接实际源码重新执行 40/40、无编译警告；此前正式 Profile40 对应补充前的188c928/209a397来源，不将其当成新增断言的正式执行。当前两文件为e394b366/e055307，下一必要共享消费者正常构建后执行新正式40及廉价UID复核；该观测字段不改变生产宿主逻辑或本轮已运行的业务场景。当前 CLI/登记、原始来源及清理证据保留在系统临时 `fullnet-logging-closeout-20261018/matrix-current` 和 `core-sql-budget`。

2026-10-18 23:21 当前运行：新准备阶段已实际通过，Integration 构建 11 分 22.18 秒、0 警告/错误，正式 Profile 40/40 且 TRX 与发现 UID 完全匹配；actor 57424 于 23:17:43.894 真实退出 0、Job0/36，SDK 已确认停止并降至 4GiB。Core 于 23:19:13.716 实际取得重型及全部构建名额，发现完整 44 项后运行；执行截止次日 00:07:13.716、清理截止 00:10:13.716，不延长。SQL 实机回读只有一个 `MSSQL_MEMORY_LIMIT_MB=2048`，启动日志实际识别 2048MB，容器仍是 3GiB 硬限额且无 swap，确认修复已作用于真实路径；这不代替 Core 通过。新增 Profile 用例影响全量/基础设施的数量登记，须待本轮完整退出及清理后按同一程序集的实际唯一 UID 更新并执行正式分片检查，当前不改冻结矩阵。

2026-10-18 23:04 续验：共享构建 helper 的单写者责任已明确交接并完成修复，`CODEX_TOOL_CALL_ID` 与 `npm_lifecycle_script` 只作为启动元数据，从构建环境和指纹同时精确排除；真实 MSBuild 环境属性、源码及产物守卫保留。正式 `node --test tests/testing/test-build-reuse.test.mjs` 11/11、无失败/跳过。新候选 `core-sql-budget` 已重新冻结实际修复后的输入及未变发布产物，22 项控制、35 项检查、六项 JavaScript 语法及 PowerShell AST 通过；4547 项主要输入与 18 项不可变补充共同覆盖当前运行闭包。准备阶段已在 23:04:20.335 实际取得正常资源池，执行截止 23:22:20.335、清理截止 23:25:20.335；正常构建 Integration 后须实际执行正式 Profile 40 项并核对 TRX/UID。Core 44 项尚未启动，不将本段准备或轻量通过计作原生通过。helper 最终 SHA `f45c70d868bb38d28359b2f8597729494afcb849495df1140485ed6310f40063`，对应回归文件 SHA `5cd0d3afe596805b5f207d8db69d7664588419381d50a9d7a59485451623343b`；旧冻结记录不改写。

2026-10-18 用户要求剩余三项快速统一批量执行。本次连续收尾顺序为：①在同次真实 discovery 中取得全量、五个主分片及迁移子分组 UID，验证完整互斥后修正登记，正常登记新构建身份并执行正式分片检查；②使用一次 JIT 构建集中运行 24 组准入矩阵、六轮 Kestrel 请求对照，以及 9,900 文件配额下保留额度 0/20 的四轮正反序对照（8 场景），核实普通丢弃与优先保留的取舍；③对当前源码实际发布 API/Worker 并执行 Linux 核心原生门禁、日志专用场景与文件安全回归。已通过的 Core Unit/Architecture、治理、Local 全矩阵及静态 AOT 证据按未变范围复用，不重复切换 AOT/JIT 编译。共享资源以协调方鲜查后的完整窗口为准，不后台排队；各阶段连续执行，出现失败只停止依赖项，安全独立项继续，不设每项提交或审批节点。

验收看实际结果：分片 UID 完整且不重复；日志接收/消费/丢弃和字节预约可对账、双线程停止与硬配额保持；请求 P99 与日志调用 P99 分别报告，保留普通丢弃的不利结果。保留额度对照仅改变该配置，不能替代旧算法同环境基线或推导全面提速。Linux 验收必须真实发布、启动、交互与停机，TRX 和本次发现 UID 完整对应、无失败/跳过。旧 AI 原生冻结闭包缺少本修订 Local 文件代码且条数准入 hash 不同，不能复用为当前日志原生通过；可复用 SDK、包缓存及有证据一致的构建。临时产物用自有系统临时目录与容器挂载，不复制整仓、不写入宿主根 artifacts；成功后回收可再生产物，失败只留定位记录。

以下为统一批次开始前的控制准备记录（不代表最新执行状态）：统一运行控制已在系统临时 `fullnet-logging-closeout-20261018` 准备，所有权/额度/完整预算/清理边界及共享容器状态检查实际 31/31、零失败/跳过（Node＋模拟 Docker/WSL 响应的 PowerShell），脚本语法检查通过；这不代表日志性能或 Linux 验收通过。初次轻量门槛误套 Native 构建配置及复制的测试缺少自身依赖/目录的失败记录均保留；仅修正纯 Node 的 1GiB/CPU1 配置与夹具准备，正式重型/Native 内存门槛不变。连续批次声明执行上限 159 分钟（Managed30＋当前 API/Worker 发布70＋Linux运行59）及清理3分钟，保留 Unit10m、Native45m 等原完整时限，实际提前结束即归还，不静默延长。资源顺序为当前 Root FA03 实际归还后进入日志完整组，不后台争锁；完整 admission 尚未创建。配额对照新增计时结束后的普通文件收据统计，分别报告未接收丢弃、消费后保留删除和最终普通/优先保留，避免把下游已删除的记录误当作仍可查询。新对照夹具未重新编译/运行，待统一批次实际执行。 引擎恢复后，两个原共享实例保留实际停止状态；控制器现先按精确 ID inspect，记录 Running/ExitCode/StartedAt 和原限额，只对运行实例读取工作集，停止实例计零且不启动。新增五种状态回归先实际失败、修订后全通过；不把引擎恢复等同共享库健康，不沿用停机前的运行状态结论。

依据当前源码复核及用户提供的 `Full.NET.DOC/logging-module-review-20260929.md`。附件是审查材料，其中逐阶段重新确认、旧实现兼容迁移要求不构成本次执行规则；执行以最新 `AGENTS.md`、`R-20261017-direct-evolution`、本地验收和集中验收规则为准。

目标：数据库或日志下游故障不扩大业务故障；配置与真实行为一致；运行诊断能说明接收、丢弃、投递与退出状态。保留 B0 同事务、B1 等待写入尝试且默认 fail-open、B2 有界可丢弃，以及可信关联字段和逐来源授权。

起始基线：`main / 42d6714ee48ed58447fe608b7122089f1e58bd55` 加既有工作区修改；任务快照 `logging-remediation-20261017`。保护此前时间线扩展和其他任务修改。此前时间线验收的等待进程已超时退出，保留原结果，不将其作为本整改修订的通过证据。

## 批次 A：故障保护与监控（F01、F02、F03、F08）

- [x] B1 以单调时间记录入队年龄，增加最大排队年龄、批次执行期限、毒记录隔离总尝试上限。消费者对过期记录 fail-open；执行预算覆盖隔离全部分支，取消传入真实事务/命令，不用请求侧 `WaitAsync` 留下后台写入。预约只在真实执行结束后归还，所有 Completion 必须终结。
- [x] 经自有 Data 边界分类：明确的数据约束/字段值错误才可二分；超时、连接、死锁、未知故障整批失败，不递归放大。提交结果不明时不自动重放。隔离次数耗尽时剩余记录结束，不伪称全部是毒记录。
- [x] 对齐应用发布端与 PromQL/Grafana 的名称、单位、标签；累计丢弃改为 Counter，补齐 B1 深度/字节/年龄及日志消费者状态、进展，停机保留累计故障。MeterListener 与 Promtool 合成时序已验证；真实 Host B2→OTel→Prometheus 及优先丢弃告警触发/恢复已在下述 0.163.1 集中复验通过，B1 实际指标闭环已由下述独立专项完成验收。
- [x] Hosting 到 Kafka 使用同一单调截止时间，优先/普通共享剩余预算；明确 SDK native 清理不可硬取消的边界。Exporter 接收结果与 Hosting 消费结果区分，Broker ACK 和 ES 成功仍为不同阶段。
- [x] 内部故障走有界、限频 stderr，禁止递归进入自身管道；未装配、运行、停止、故障状态可区分，不凭零深度宣称健康。

以上勾选表示实现落地，不代表完整链路验收完成。新增 17 项 Unit 用例，修正既有基础设施故障用例的隔离语义，并同步基准项目构造调用。冻结源码验证结果：

| 检查 | 本批结果 |
| --- | --- |
| `node scripts/testing/run-dotnet-test-suite.mjs unit --selection logging-remediation --reuse-build` | Release 0 警告/0 错误；121/121 通过，0 跳过 |
| 同入口 `--selection logging-delivery --reuse-build` | 校验后复用 Release；发现 381，380 通过，1 项 Linux FIFO 在 Windows 跳过，0 失败；与上组有重叠，不相加宣称独立用例数 |
| `node --test tests/deployment/observability-contract.test.mjs` | 10/10 通过 |
| `node eng/testing/prometheus-log-alerts-test.mjs` | 40 条规则及抓取配置检查通过；宿主日志、Fluent Bit、消费者、缓存的 Promtool 告警测试通过；不等于真实 Host 指标联调 |
| SQL Server/MySQL、真实 Kafka/ES、真实指标导出、Architecture 与 API/Worker AOT/原生 | 本整改修订未执行，保持待验收；不复用旧时间线结论 |

默认新增参数位于 `Auditing:MicroBatch`：`MaxQueueAge=00:00:02`、`BatchExecutionTimeout=00:00:05`、`MaxIsolationAttempts=15`。排队年龄是提交前丢弃条件，数据库期限是协作取消，不承诺请求的硬返回上限。配置与故障语义已同步 Wiki 和降级处置说明。详细过程日志、失败重试与输入清单保存在本地验证产物，不提升为永久设计约束。

主要实现：`src/Modules/Full.NET.Modules.Auditing/Features/WriteAuditBatch/`、`src/BuildingBlocks/Full.NET.Data.Abstractions/DataCommandException.cs`、`src/BuildingBlocks/Full.NET.Data.Dapper/DataCommandExceptionMapper.cs`、`src/BuildingBlocks/Full.NET.Hosting/Observability/`、`src/BuildingBlocks/Full.NET.Logging.Kafka/`、`deploy/observability/prometheus-rules.yaml` 与 `grafana-dashboard.json`。

回归场景：健康批一次提交；单毒记录保留健康项；整库失败不拆批；共享尝试预算；批次超时停止后续尝试；排队过期不调用数据库；取消/停机/配置错误全部解除等待和归还预约；消费者异常/出口拒绝/停止后累计计数仍可观察；两个通道与 Exporter 不重新领取整段停机预算。扩展现有 `AuditingWritePathTests`、`DataCommandExceptionMapperTests`、Hosting/Kafka 与部署契约测试。双库真实约束、故障与真实指标 firing/resolved 在批次集中验收，不以单元替身代替。

## 批次 B：配置、本地文件与依赖收敛（F04、F05、F09）

- [x] 诊断 Payload 字节覆盖接入实际受控投影：匹配且未过期的规则与全局取最小限制，范围 0—2048，0 关闭对应方向；不越过路由白名单、投影资格、敏感分类和全局速率/字节预算。同步管理 DTO、Vue、说明与测试，不保留成功保存却无效的字段。普通 Debug 开关不冒充现有 HTTP 诊断能力，不在本批扩展为任意日志动态过滤器。
- [x] Local 写入和管理共享应用目录定位、自有文件命名；列表、尾读、下载和时间线识别同一 `.txt` 文件。移除 Host.Api 默认 `LogRootPath=logs`：未配置时跟随已启用 Local 的应用子目录，显式目录优先；其他模式不自动扫描。配置禁用时不创建目录，路径/链接/非普通文件防护保留。API 只能读当前实例可见目录，不能自动获得其他服务器文件。
- [x] 普通文件管理增加全部目录项扫描上限（默认 1000）、取消及明确截断状态；返回条数上限不代替扫描上限。列表响应改为 `items/isTruncated/scannedEntries`，客户端契约、生成产物和页面同步更新，不保留旧数组响应。恰好用满扫描预算保守报告截断，只对已扫描候选排序；尾读/下载查找也遵守预算。
- [x] 删除旧 ES Sink、Legacy 专用安全事件副本及失效配置/依赖/测试分支；只保留明确的 Local、Collector、ApplicationKafka 路线。保留独立 ES 写入消费者和受权只读查询，不新增兼容 Provider 项目。更新 ADR-0012、Wiki、部署示例及相关源码注释。

主要实现：`HttpOperationPayloadProjection.cs`、`DiagnosticPolicySnapshot.cs`、Settings 诊断策略管理、`LocalFileLogStore.cs`、ObservabilityAdmin 文件控制面及时间线、Hosting/Kafka 配置与项目依赖，以及 `ui/admin` 对应页面和客户端契约。

以上四项勾选表示实现完成，不能据此宣称批次 B 整体验收通过。新增 6 项 .NET 回归与 1 项 Vue 截断提示回归；验证使用独立冻结输入，不覆盖批次 A 的证据。旧 ES 删除已同步收敛监控 API、出口配置、事件副本、测试及文档，追加修订验证见下表。

| 检查 | 本批结果 |
| --- | --- |
| `node scripts/testing/run-dotnet-test-suite.mjs unit --selection logging-delivery --reuse-build` | 最终冻结输入 Release 构建 0 警告/0 错误；发现 387，386 通过，1 项 Linux FIFO 在 Windows 跳过，0 失败；包含本批新增 6 项 .NET 场景 |
| `pnpm --filter @fullnet/admin test src/views/ObservabilityLogFilesView.test.ts` | 3/3 通过，包含扫描耗尽但列表为空时的截断提示；下载测试存在 jsdom 不支持页面导航的提示，未影响断言 |
| `pnpm --filter @fullnet/admin typecheck` | 退出码 0；覆盖日志列表新响应与诊断规则字段展示 |
| `node scripts/openapi/snapshot-client-openapi.mjs --check --offline`、`node scripts/openapi/generate-fullnet-client.mjs --check` | 离线快照与 manifest 一致，生成产物零漂移；初次换行/规范化格式漂移已通过仓库规范化入口修正 |
| `node --test tests/openapi/client-generation-readiness.test.mjs tests/openapi/client-generator.test.mjs tests/openapi/settings-diagnostic-policy-contract.test.mjs tests/openapi/platform-backup-crypto-mqtt-observability-contract.test.mjs` | 12/12 通过，0 跳过；属于静态/生成契约检查，不代替运行时 OpenAPI |
| 最终输入及差异检查 | 138 个相关源码/配置/契约文件与冻结输入一致；默认 Host.Api 不再覆盖自动目录；本任务范围 `git diff --check` 通过 |
| 双库 API、运行时 OpenAPI、Linux FIFO、安全句柄、真实日志链路及 AOT/原生 | 本修订仍待集中验收；未与共享环境已有重型验收叠加启动 |

测试计数、冻结输入清单和原始输出保存在本地验证产物；表内检查有重叠，不累计为互不重复的独立验收数量。

回归场景：规则保存后实际生效/过期/故障回退；非白名单不可采集；真实 Local 写入经列表、尾读、下载、时间线可见；大目录含非日志项也计入扫描预算；轮转、链接替换、FIFO、跨应用目录、撤权、取消；各模式只有一条有效出口，禁用不外连。依赖删除验证真实 API/Worker AOT 发布与对应原生运行。

### 批次 B 追加：旧 ES 出口删除

宿主及 Helm 默认 `Collector`，只保留 Local/Collector/ApplicationKafka；删除 Serilog ES Sink、失效选项、注册状态和迁移告警。显式模式必须配对且同值，显式 null/空不允许被绑定为枚举零值 Local；残留旧 ES 配置节拒绝启动。生产 Local 仍须启用文件。

后台仅持有受限 UTF-8 快照和事件 ID，删除 LegacyEvent 对象图及其预算；测试辅助反序列化仅用于既有断言，不进入生产路径。管理 API、客户端、Vue 和浏览器用例改为报告入口与独立 ES 查询配置，边界固定 `configuration-only`，刷新失败清空旧结果；保持原精确读权限，不探测集群或返回凭据。独立 LogConsumer、DLQ/Offset 和时间线 ES 查询不删除。

| 检查 | 追加修订结果 |
| --- | --- |
| 官方 Unit `--selection logging-delivery --reuse-build` | 最终冻结输入 Release 构建 0 警告/0 错误；发现 386，385 通过，1 项 Linux FIFO 在 Windows 跳过，0 失败 |
| Vue 页面与 API 客户端指定用例 | 6/6 通过；覆盖三个入口、独立 ES 查询配置、刷新失败移除旧状态 |
| client-contracts 指定日志配置响应测试 | 2/2 通过；拒绝旧状态和旧确认边界 |
| Vue `typecheck` | 退出码 0 |
| `node --test`：observability-contract、native-aot-publish、platform-backup-crypto-mqtt-observability-contract | 26/26 通过；只代表静态契约/告警白名单规则，不代表实际 AOT 发布 |
| Helm 完整契约组 | 当前工作树 `node --test tests/deployment/helm-contract.test.mjs` 最终 19/19 通过、0 跳过。此前 12 项失败源于连接预算 Schema/断言未同步，现已补齐 `apiAuthorizationReadReserve`，保留业务额度 37；新增反例验证负数预留及 38+2+1 超过连接池 40 时拒绝。包含 lint、双库角色渲染、反例及三种日志入口，不代表真实集群联调 |
| Architecture | 当前工作树 `node scripts/testing/run-dotnet-test-suite.mjs architecture --reuse-build`：Release 0 警告/0 错误，235/235 通过、0 跳过；使用 SDK 10.0.402，正式入口校验构建输入与产物 |
| 独立代码审查、差异与源码一致性 | 独立只读审查未发现可复现高价值问题；8115 项冻结清单核对仅整改计划的结果记录有差异，源码/配置/契约一致；本任务范围 `git diff --check` 通过 |
| 真实双库 API、浏览器、Kafka/ES、Linux 文件、AOT 发布/原生及性能 | 本修订未执行；浏览器用例已更新且语法检查通过，不能代替实际执行 |

本次删除 3 项旧 ES 选项验证与 1 项旧 Sink 析构场景，替换既有宿主状态场景，测试矩阵同步调减；不以旧用例数量充当新覆盖。前期 A/B/C 曾使用整仓冻结副本；现按 2026-10-17 本地产物规则使用当前工作树、Git 基线/差异和可复用构建，不再复制整仓或长期保留成功临时输出。历史结论保持原范围，旧副本回收尚待完成。Helm 阻塞已解除，后续完成本批双库、真实链路与 AOT 集中验收；性能批次 C 仍保持待实施。

### 双库控制面验收与临时产物入口修订

使用当前工作树，通过 `withTestRun(heavy: true)` 和 `prepareTestBuild` 执行双库聚焦集：`ObservabilityAdminApi`、`Host_log_timeline_correlates_all_sources`、`Host_diagnostic_policy_management`，最低发现数 6、超时 15 分钟。首次编译发现新增状态响应的局部变量重名，已修正。真实 MySQL 控制面随后通过日志新响应断言，在既有租户缓存失效断言处失败：当前权威读取策略不允许手动失效。测试已同步为不可失效、操作目录为空，并保留无权限 403 与有权限不支持操作的 400/机器码验证；不放宽生产缓存行为。

| 检查 | 当前结果 |
| --- | --- |
| Integration 编译 | 修正后 Release 0 警告/0 错误；仅代表构建通过 |
| 上述 6 项真实双库聚焦集 | 超时取消后未自然退出，已终止核对归属的测试子进程，外层退出并释放重型锁；无完整汇总，不计通过。SQL Server 镜像尚未缓存且本轮未见对应容器启动，不能宣称 SQL Server 通过；修正后的控制面断言仍待实际重跑 |
| `node --test tests/testing/test-run-context.test.mjs tests/testing/test-build-reuse.test.mjs` | 18/18 通过，覆盖并行临时环境、锁、取消、构建复用和成功清理 |
| Windows 真实编译临时目录回收实验 | SDK 10.0.402 最小项目构建 0 警告/0 错误；入口结束后目录不存在。此实验只验证工具清理，不是 API AOT 验收 |

统一测试入口改用独立系统临时目录，成功后回收，失败保留必要定位记录；子进程禁用跨轮常驻 C# 编译服务及 MSBuild 节点复用，防止 Windows 分析器 DLL 占用临时目录。基于文件内容的构建产物复用和资源锁仍保留。修订前已有被编译服务占用的临时目录及历史整仓副本不强删，后续须在资源空闲时回收；本轮没有新建整仓副本。下一次优先在镜像就绪后重跑受影响双库场景，之后再执行真实 Kafka/ES 与 AOT，不连续重启相同长测试。

### MySQL 控制面聚焦复验

2026-10-17 使用当前工作树、SDK 10.0.402，经 `withTestRun(heavy: true)` 与 `prepareTestBuild` 执行 MySQL 三项聚焦场景，最低发现数 3、执行上限 5 分钟。Release 构建 0 警告/0 错误；日志控制面与跨来源关联时间线通过，诊断策略更新失败，汇总为 2 通过、1 失败、0 跳过（2 分 33 秒）。

定位为集成测试仍给 Endpoint 定向规则设置实例级容量 50，与现行 `scoped_capacity` 校验冲突。更新成功场景将容量覆盖设为 null，保留采样与请求/响应载荷限制；新增 Endpoint 容量覆盖返回 400 和 `settings.diagnostic_policy.scoped_capacity` 的反例，不放宽生产规则。两种数据库共用该断言实现。

修改后重新构建 0 警告/0 错误，并按完整方法 `Host_diagnostic_policy_management_follows_contract_with_mysql` 复验：1/1 通过、0 跳过（1 分 46 秒），入口退出码 0。本轮三类 MySQL 场景均已有实际通过记录，但属于两次执行，不能表述为最终版本一次性 3/3。成功临时目录按统一入口清理；失败记录保留在系统临时目录 `fullnet-test-run-Fmtpoh`。SQL Server 镜像尚未就绪，本轮不计 SQL Server、真实 Kafka/ES、AOT 或容量验收通过。

### SQL Server 控制面聚焦验收

2026-10-17 指定镜像 `mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04` 已就绪，经 `withTestRun(heavy: true)` 与 `prepareTestBuild` 运行当前工作树。Release 构建 0 警告/0 错误；过滤以下三个完整方法，最低发现数 3、执行上限 5 分钟：

- `Log_control_plane_follows_contract_with_sql_server`
- `Host_log_timeline_correlates_all_sources_with_sqlserver`
- `Host_diagnostic_policy_management_follows_contract_with_sql_server`

实际结果：3/3 通过、0 跳过、0 失败，耗时 2 分 30 秒，入口退出码 0。临时 TRX 随成功运行目录按入口清理，复用数据库容器保留。上述三类场景已有 SQL Server/MySQL 实际通过记录，但 MySQL 是条数准入修订前的两次执行，不宣称当前最终输入一次性双库 6/6；后续影响集验收按输入身份判断。真实 Kafka/ES、实际 AOT/原生、Linux FIFO 及剩余故障/性能矩阵仍待完成，不因本组通过而标记整体收尾。

### B1 回滚、隔离与保留期双库验收

2026-10-17 当前工作树使用 SDK 10.0.402，经 `withTestRun(heavy: true)` 和 `prepareTestBuild` 执行 `Request_audit_batch_rolls_back_partial_write` 与 `Audit_retention_deletes_expired_rows_in_fair_batches` 的 SQL Server/MySQL 场景。最低发现数 4、执行上限 5 分钟；Release 构建 0 警告/0 错误，实际 4/4 通过、0 跳过、0 失败，耗时 2 分 15 秒，入口退出码 0。

本组覆盖：失败批次的毒记录隔离与健康行/详情落库；详情读取的 401/403/授权读取、白名单字段投影和到期 404；保留期清理的跨类型公平分批、详情积压检查点保持及清空。它不是数据库断网、长时间阻塞或全部失败分类的完整故障矩阵，不以四项通过代替剩余故障验收。

父测试进程自然退出、重型锁释放，成功临时目录已清理，复用 SQL Server/MySQL 容器保留。后续真实链路的 `apache/kafka:4.1.2` 镜像已缓存（摘要 `sha256:5cc2a2fd93fa2687b44015eee04fb2c3edd9e526bd64bf8bec5ff1e268772e0e`），本轮尚未启动 Broker 测试；按既有顺序让出共享资源，不把镜像就绪记为链路通过。

### B1 真实锁等待与取消补充（Writer 边界本地验收通过）

本轮补齐 B1 真实故障边界：SQL Server 与 MySQL 各增加批次期限、调用方取消、写入连接中断三种场景，均使用独立临时数据库和真实表锁。服务端锁等待查询确认 INSERT 确实被阻塞；连接中断只结束该临时库内被自有锁阻塞的写入会话。随后断言调用退出、所有信封终结且不标毒、只有一次命令尝试；释放自有锁后核对没有迟到写入，并验证同一写入边界恢复。通过自有执行器调用实际驱动，观察包装器只计数、不替换数据库行为。不停止共享数据库、不更改用户权限。这组只覆盖 Writer 边界，队列计费仍由既有单测和后续整队列故障验收覆盖。

故障目标先通过同一事务连接读取真实会话 ID，再按临时数据库、会话 ID、锁等待及 INSERT 正文交叉核对，避免把其他后台写入当作目标。双库 API 分片最低发现数各增加 3，完整分片最低数同步增加 6，分片总数一致性检查通过。本任务 `git diff --check` 通过；测试矩阵契约实际 6/7 通过，失败为共享工作区已有 AI 迁移恢复用例 272 尚未登记，不将其写成全通过，也不擅自修改 AI 范围。

首次排队因共享遗留锁结束，未启动测试；用户确认继续后，仅清理核对过持有者已退出且身份未更换的单个锁。随后八项首跑实际 6/8 通过、2 失败：MySQL 冷模板准备耗尽夹具两分钟预算；SQL Server 在调用方取消后返回 `SqlException(0)`，通用异常分支吞掉取消。失败证据保留于系统临时目录 `fullnet-test-run-xwltHg/temp/b1-faults.trx`，没有把准备失败记成 B1 故障场景通过。

修订：准备阶段使用外层测试组预算，故障阶段单独计时；B1 在令牌已取消时将驱动普通异常统一转为取消，再由外层区分调用方取消与内部批次期限。此分支先于毒记录分类，不会把取消后的错误标毒或继续隔离；真实 I/O 退出后才终结信封。新增四项 Unit 覆盖调用方/期限与普通/已分类错误组合。既定五秒批次期限、服务端锁等待、单次尝试和恢复断言不放宽。

最终使用 SDK 10.0.402、CPU 数 2，通过正式入口执行 `unit --selection logging-remediation --reuse-build`：Release 0 警告/0 错误，125/125 通过，0 跳过/失败，耗时 26 秒。此前两次编译虽均 0 警告/0 错误，但因共享源码在编译期间变化，入口拒绝运行，均不计通过。源码稳定后才登记并执行上述有效单测。

再经 `withTestRun(heavy: true)` / `prepareTestBuild` 复验 `Request_audit_blocked_batch|Request_audit_disconnected_batch|Request_audit_batch_rolls_back_partial_write`：Release 0 警告/0 错误，8/8 通过，0 跳过/失败，耗时 2 分 44 秒，入口退出码 0。原始 TRX 和汇总位于 `C:/Users/Administrator/AppData/Local/Temp/fullnet-b1-fault-evidence-8oVcaP`。成功临时目录、父测试进程和重型锁已释放，复用双库容器保留。本组不代表整队列全部故障矩阵、真实指标导出、Linux/AOT 或容量验收；新取消分支的 AOT 分析/Architecture/原生路径仍需按整体批次补验。

### Kafka 真实生产者投递验收

2026-10-17 当前工作树通过正式重型锁及构建输入校验，使用 SDK 10.0.402 / `apache/kafka:4.1.2` 执行完整 `KafkaLogDeliveryTests`（最低发现数 6，执行上限 10 分钟）。Release 0 警告/0 错误；实际 6/6 通过、0 跳过、0 失败，耗时 1 分 06 秒，入口退出码 0。

覆盖受限生产者 Topic ACL、SASL_SSL 正确凭据与错误密码拒绝、官方导出器 TLS 双优先级投递、宿主 ILogger 普通/优先快照与事件 ID 一致、Broker 中断后预留额度释放及同一通道恢复、两类日志 Broker ACK 后仍可读取。认证失败和 coordinator 启动重试输出属于本组反例/启动过程，结果按最终断言确认；不将 Broker ACK 等同 ES 可查询。

父/子测试进程已自然退出、重型锁释放，成功临时输出和测试 Kafka 容器已清理；复用双库容器保留。独立消费者、ES 写入、DLQ/Offset、重平衡与跨进程恢复仍需各自实际验收，本组不代替它们或原生运行。后续 ES 夹具固定镜像 `docker.elastic.co/elasticsearch/elasticsearch@sha256:82ac14f43fe701992e601f4cc81e1c0d7dbc5a2576d8cd736006452925df4026` 已下载完成，镜像准备不计为测试通过。

### 独立消费者、ES 与提交恢复验收

2026-10-17 在上一共享原生任务退出且资源释放后，通过正式 `withTestRun(heavy: true)` / `prepareTestBuild` 执行当前工作树 `KafkaLogConsumerDeliveryTests`、`KafkaLogConsumerElasticsearchTlsTests`、`KafkaLogConsumerElasticsearchReplayTests`。SDK 10.0.402，真实 Kafka 4.1.2 与源码固定摘要 ES；最低发现数 9，执行上限 10 分钟。Release 0 警告/0 错误；实际 9/9 通过、0 跳过、0 失败，耗时 6 分 16 秒，入口退出码 0。

覆盖未分配分区时不就绪；逐条/批量处理失去分区所有权后不能提交；旧成员退出后新成员重放；独立进程 DLQ 失败保留 Offset，重启后待 DLQ ACK 才推进；ES 写入确认与 DLQ ACK 后连续位点提交；真实 ES 已写入但提交失败时以相同文档 ID 重放；映射拒绝在持久 DLQ 确认前不提交；私有 CA/API Key、受限权限和实际 Bulk/文档读取。TLS 场景还验证真实 ES 写入后、回执尚未交给 Consumer 时强制结束独立进程，随后位点不提前推进、重启重放更新同一文档。

本轮是 Windows x64 .NET 进程加 Docker Kafka/ES，不是 Linux 原生 Consumer；强制结束使用 `Process.Kill`，不计 Linux SIGKILL、SIGTERM 或 AOT 验收通过。重平衡的 max-poll 超时为主动故障注入，不是忽略测试失败。父进程及测试/消费者进程退出、重型锁释放、成功临时目录回收，Kafka/ES 业务测试容器已清理；复用双库容器保留。剩余 Collector 实际采集链路、原生/Linux 与性能矩阵不由本组覆盖。

### Collector 当前修订链路与路线对比

2026-10-17 经统一重型锁和构建校验运行 `KafkaCollectorRequestReplayTests|KafkaLogRouteComparisonTests`，SDK 10.0.402，Release 0 警告/0 错误；实际 3 项中 2 通过、1 失败、0 跳过，耗时 10 分 58 秒。通过项为静态 CRI 全链回放及独立请求进程的四轮正反序路线对比；失败项为同进程实时场景的“请求结束前观察到实测 HTTP 日志并确认 Offset”断言。不能把整批记为通过，也不能用另一个通过场景覆盖该失败。

四轮每轮固定 200 次预热、5,000 次实测、目标 500 请求/秒，全部实测记录完成 ES/Offset/DLQ 对账，宿主丢弃数均为 0。顺序 Collector / ApplicationKafka / ApplicationKafka / Collector，请求 P99 分别为 6.0247 / 4.8840 / 3.8511 / 10.5434 ms；对应实测日志约 805,689 / 761,714 / 761,499 / 803,706 字节/秒。环境为 Windows 10、i7-12700H、SDK 10.0.402、进程可见 CPU 数 2、同机 Docker 与模拟 CRI 文件。该短档只有两轮，不构成稳定性能胜者、完整业务 API P99、真实 Kubernetes 或扩大容量结论。

测试输出已改为统一入口隔离的系统临时目录，停止在仓库根 `artifacts` 累积；成功由入口回收，失败保留定位记录。初次整批结果位于系统临时目录 `fullnet-test-run-7kgpsL/temp`，其中 `logging-collector-chain.trx` 记录失败和发现数，路线对比目录的 `result.json` 保留四轮原始分布与对账。实时失败已补充收据数、实测匹配数和提交位点诊断，保持原断言与请求规模不变，单独复现后再决定修复。

随后保持原请求档和断言，仅补充失败诊断，单独执行 `FullyQualifiedName~Collector_streams_http_logs_and_commits_before_requests_complete`：Release 0 警告/0 错误，实际 1/1 通过、0 跳过、0 失败，耗时 1 分 41 秒，入口退出 0，成功临时目录已回收。初次失败仍保留为未解释的间歇性实时观察问题，不将重跑通过写成根因修复；后续需捕获失败窗口的采集/消费时序，不能扩大请求窗口或放松确认断言掩盖它。过程中一次临时编排命令把绝对程序集路径传给要求相对路径的构建复用入口，导致构建成功后校验报 ENOENT、测试未启动；改正调用后执行上述有效复跑，该次入口失败不计为测试结果。

#### 实时观察端整改（本地链路验收通过）

继续排查发现，实时回放和路线比较的父进程都在每读一条 Kafka 收据后固定等待 25 ms，因此观察吞吐上限约 40 条/秒，低于 500 请求/秒的发送档。仅 200 条预热日志就至少占用约 5 秒观察时间，另有连接、采集与提交等待；测试可能因自己的积压错过实测窗口。这是已确认的测试观察端限速问题，但初次失败缺少窗口计数，不能认定它是首次失败的唯一根因。

两处观察循环已改为每轮最多读取 256 条：首条最长等待 20 ms，后续使用零等待消费，仅处理已就绪记录；轮间仍等待 25 ms，并保留取消、请求结束与 10,000 条总收据上限。确认逻辑、请求规模、发送节奏和请求结束前提交的断言均不放宽。实时测试增加 `live-observation.json`，记录测量起止、首条收据、首条实测收据、提交确认时刻与查询次数，不包含业务正文。

当前基线 HEAD 为 `053294fa5477881bc73dbf3386ecfe502ffbb67c`；复用原任务快照并保护共享工作区修改。SDK 10.0.402、CPU 数 2，经 `withTestRun(heavy: true)` 和 `prepareTestBuild` 构建 Release，0 警告/0 错误。集中运行 `KafkaCollectorRequestReplayTests|KafkaLogRouteComparisonTests`：3/3 通过、0 跳过、0 失败，耗时 10 分 03 秒；随后保持同一构建和请求档，单独实时场景再运行两次，各 1/1 通过，分别耗时 1 分 38 秒、1 分 37 秒，入口最终退出码 0。这是三个不同用例、五次执行，不能称为五个不同用例。

三次实时测量中，首条实测收据分别约在开始后 1.499 / 1.834 / 2.539 秒出现，具体实测记录的 Offset 在 2.583 / 2.650 / 6.185 秒确认，均早于 10 秒请求窗口结束。完整字段、可信 Pod 路由、镜像排除、ES 文档、DLQ 为零及最终提交位点仍按原断言对账。该证据验证观察端限速修订在规定本地场景下通过；首次失败缺少窗口记录，仍不追认它的唯一根因。

修订后四轮顺序 Collector / ApplicationKafka / ApplicationKafka / Collector，请求 P99 为 4.2191 / 3.9743 / 4.1958 / 4.4571 ms，实测日志吞吐约 804,738 / 760,727 / 761,097 / 804,953 字节/秒；每轮 5,000 条实测记录完成对账，宿主丢弃为 0。仍为同机 Docker、模拟 CRI 和短请求档，不推导完整业务 API、Linux/AOT、稳定路线胜者或未测规模容量。

最小原始证据保留在 `C:/Users/Administrator/AppData/Local/Temp/fullnet-collector-batch-evidence-iTaVnX`：三份 TRX、各场景结果、三份实时观察时序及汇总；未保留源码副本、构建产物或日志正文。成功测试临时目录已回收、父/消费者进程退出、重型锁释放、Kafka/ES/Fluent Bit 测试容器清理，复用双库容器保留；本任务差异检查通过。

## 批次 C：证据驱动优化（F06、F07）

### 统一收尾首轮实际结果（2026-10-18）

以下结果更新本节之前的“尚未运行”状态，历史失败记录保留原事实。使用当前工作树，Windows SDK 10.0.402、CPU2/Job4GiB；Linux SDK 容器发布阶段 12GiB/CPU1.9，运行阶段实际降至 4GiB/CPU1.9，swap 均不额外扩张。未复制整仓。

| 检查 | 首轮实际结果 |
| --- | --- |
| 正式分片检查 | 完整 discovery 和 UID 对账后通过：SQL Server 202、MySQL 202、迁移 560、基础设施 279、消息重型 80，共 1,323，无遗漏或重复；最低登记差额为全量 +35、迁移 +2、基础设施 +33 |
| 测试矩阵与治理 | `node --test tests/testing/test-matrix.test.mjs` 7/7；全局治理 84/84，均无失败或跳过 |
| `logging-admission-matrix` | 正式 Serilog 配置、快照与双后台线程，六场景×两档载荷×正反序共 24/24；条数满、字节满、洪峰、超限、禁用和混合优先级对账通过。CPU、分配、队列年龄及日志调用 P99 保存原始统计；除既有条数准入前后对照外，不把当前特征推断成旧算法提速 |
| `logging-request-latency --sustained` | 六组全部通过；500/s、并发 8、每组预热 200/测量 5,000。Disabled 两轮 P99 1.8593/1.9522ms；Summary 2.3531/2.8667ms；Projected 2.9242/2.8392ms，日志丢弃 0。仅 Kestrel 受控探针，不是完整业务 API 或万级容量 |
| `logging-local-maintenance --quota-comparison` | 四轮正反序共 8 组上限及收据对账通过，但每组 4,096 普通/100 优先全部保留，回收的是旧种子；不能据此验收新事件之间的长期保留权衡。已将仅此对照模式增至 16,384 普通事件，并要求实际发生新事件保留删除，待重跑；原默认 24 场景参数不变 |
| Linux 官方 API/Worker Native AOT 发布 | 两项均通过。实际 ELF 分别 140,009,728 / 88,879,224 字节，SDK/RID 与正式 manifest 一致；API 9 条、Worker 7 条既有第三方依赖允许告警通过正式检查，不能写成发布零告警 |
| Linux 日志 Unit | 实际发现 404，402 通过、2 失败、0 跳过。失败分别为剩余停机预算小于 1ms却要求精确 0，以及 Linux 的 `DirectoryNotFoundException` 被精确 `IOException` 断言拒绝；修订只容许有效剩余预算和 IOException 子类，保留启动失败及既有文件不变断言，待重跑 |
| 核心 Native 运行 | 实际发现 44，包含 API Local/API Kafka/Worker Kafka，尚未启动；发布成功不能替代运行结果 |

首轮失败日志、TRX、完整源码前后 hash、构建身份与产物 hash 位于系统临时 `fullnet-logging-closeout-20261018`。父进程实际退出 13、Job0；清理阶段 Docker stop 的 15 秒宽限与 CLI 同为 15 秒导致超时，SDK 实际已停止但尚未删除，不登记清理完成。重入只复用精确自有容器的缓存与未变 ELF；修复清理控制时保留原绝对截止，不能重新起算执行预算。共享测试基建另有所有者按新授权修改，须区分日志三项变更、外部调度变更与未变生产输入，稳定并核对后正常重登记，不默默替换旧源码摘要或重发已验产物。

已增加 `LoggingSaturatedQueueBenchmarks`，通过公开宿主入口固定普通队列容量为 1、字节预算为 1 MiB，阻塞首条后台输出并填满待处理队列；以 32/2048 字符载荷测量后续调用拒绝成本。每轮校验队列保持满载且丢弃数与调用数一致，清理时先释放出口再关闭宿主。基准框架的控制台输出不进入阻塞 Writer。

2026-10-17 在 Windows 10、i7-12700H、SDK 10.0.402 / Runtime 10.0.13、`DOTNET_PROCESSOR_COUNT=2` 下，经统一重型锁和构建校验运行 `Full.NET.Benchmarks.dll --filter *LoggingSaturatedQueueBenchmarks* --job Dry --inProcess --artifacts <系统临时目录>`：Release 0 警告/0 错误，两个场景完成、无夹具断言失败，退出码 0。此轮只验证夹具可执行，单次冷启动样本及迭代过短告警不构成正式性能基线，不据此报告延迟改善、请求 P99 或容量通过。成功临时输出已按入口清理；正式前后对比需保留规定的最小原始证据。当时生产准入逻辑尚未修改；后续条数准入实现及对比结果见本节末尾，完整批次仍未完成。

- [x] 记录当前实现六类场景、两档载荷与正反序的完整准入特征，以及独立 HTTP 请求 P99；保存分配、拒绝、CPU、最老事件年龄。条数准入另有旧/新同载荷对照，其余场景无旧版同参数证据时不声称提速。
- [x] 实现条数与字节同步准入，减少可证明的无效快照构建；保持不可变安全快照与在途计费，不把原始可变对象放入后台；完整准入矩阵及 Linux 日志回归通过。
- [x] 测量包含目录锁等待的维护时间、真实队列至文件出口延迟及持续配额保留取舍，实施有界维护和可配置优先保留。纯锁等待未独立拆分，共同磁盘仍是共同故障域，不宣称完全隔离或全面提速。

F06 前序执行安排（已按上述统一续验结果完成）：补充有限准入矩阵，直接使用正式 Serilog 配置、快照构建及双消费线程，覆盖条数满但字节未满、字节满但条数未满、小事件洪峰、必要字段仍超限、禁用级别和普通阻塞/优先继续消费。按相同载荷正反序测量调用 P50/P95/P99、进程 CPU/分配、队列最老年龄及逐通道接收/消费/丢弃；每场景释放出口后核对真实线程停止与预约归还。沿用既有条数满修改前后证据；新增场景是当前实现特征，缺少原版本同场景结果时不报告提速。HTTP P99 继续由独立 Kestrel 夹具测量，不能用日志调用 P99 替代。夹具与文档先集中完成，资源窗口内再集中编译、运行，未执行前保持待验收。

实现入口为 `Full.NET.Benchmarks.dll logging-admission-matrix --output <新的系统临时子目录>`，固定 4 个生产循环、每循环 4,096 次调用；6 类场景 × 32/2,048 字符 × 正反序两轮共 24 组，另有不计入结果的预热。使用 `FullNetLoggingPipeline.Configure`，记录必要字段超限与字节拒绝的实际 Meter 计数，并分别报告混合场景普通/优先调用分位数。控制脚本 `node --check`、本任务差异/空白检查及正式 Release 编译均通过；2026-10-18 最新基准构建耗时 4 分 56 秒，0 警告/0 错误，构建身份和前后内容守卫有效。编译结束已不足约定的完整三分钟运行预算，矩阵未启动，没有结果文件或通过结论。Core 日志实现、Unit、Architecture 和既有 Local 维护夹具未变化，原已通过组不重跑。

主要实现：`FullNetBoundedAsyncSink.cs`、`LogEnvelopeBuilder.cs`、`LocalFileLogStore.cs` 及对应日志基准/故障夹具。不因偏好将专用同步出口线程机械替换为 Channel，也不承诺未经实测的提速比例。

条数准入子项已实现：构建前 CAS 预留名额，字节拒绝/构建失败/未入队归还，出队或主动放弃归还；字节计费仍覆盖在途。新增回归先复现满队列继续构建超大快照，再在实现后通过。正式 `unit --selection logging-delivery --reuse-build`：Release 0 警告/0 错误，387 项中 386 通过、1 项 Windows 不支持的 Linux FIFO 跳过，0 失败。两种载荷修改前后的同参数短基准已完成，32/2048 字符调用分配由 5.04/19.37 KB 降为 1.42/1.42 KB；耗时置信区间宽且存在迭代过短告警，不认定稳定延迟收益，不计请求 P99、容量或整个 F06 完成。场景、原始证据位置与边界见[条数准入验证](../../verification/2026-10-17-logging-count-admission.md)。

### 统一收尾续验结论（2026-10-18）

Linux 日志当前源 Release 正规构建 0 警告/0 错误，SDK 10.0.402、容器实际 12GiB/CPU1.9；完整发现 404 个唯一 UID，TRX 404 通过、0 失败/跳过，前后输入及构建输出守卫一致。已修复首轮两处跨平台断言，FIFO 真实执行通过。该结果不代替核心 Native 运行。

持续配额使用 Windows CPU2/Job4GiB，9,900 个旧文件、每组 16,384 普通和 100 优先事件，0/20 四轮正反序共八组。每组均实际发生新事件保留删除、硬配额和队列/字节归还对账完成；计数不是 fsync 或端到端持久确认。下表先列普通/优先最终保留，再分别列普通接收前丢弃与真实队列至文件出口返回 P99，不把保留删除合并成未接收丢弃：

| 轮次 | 0% 普通/优先保留 | 20% 普通/优先保留 | 普通丢弃 0%/20% | 普通出口 P99 ms 0%/20% | 优先出口 P99 ms 0%/20% |
| --- | --- | --- | --- | --- | --- |
| 1 | 9694 / 2 | 9594 / 100 | 0 / 175 | 19.02 / 1019.97 | 9.53 / 249.76 |
| 2 | 9694 / 2 | 9589 / 100 | 0 / 0 | 19.80 / 51.09 | 10.29 / 23.11 |
| 3 | 9694 / 2 | 9589 / 100 | 0 / 0 | 93.34 / 19.81 | 42.81 / 10.51 |
| 4 | 9698 / 2 | 9599 / 100 | 956 / 830 | 396.77 / 624.98 | 7.54 / 6.14 |

保留默认 20% 的理由是优先诊断历史，代价为最终少保留 99–105 条普通事件；这是可配置策略取舍，不是全面性能收益。保留额 0 关闭该保护；磁盘、物理删除和调度仍有共同开销，四轮短测不能证明稳定的吞吐或丢弃改善。首次 4,096 普通事件的八组没有形成新日志保留竞争，只作为功能收据，不混入持续配额效果对比。

剩余唯一验收组为核心 Native 44 项。重入正式新鲜性校验和 discovery 已通过，实际运行因 `/work/jit/bin/Full.NET.Host.Migrator/release/Full.NET.Host.Migrator` 缺失而出现迁库前置失败；先请求取消、再有界结束该自有测试进程，不能登记本轮通过。后续已从上述成功 Linux 构建导出完整 JIT Migrator 运行目录，284 文件、70,955,600 字节，ELF apphost、托管 DLL、deps/runtimeconfig 及全目录 hash 预检通过。次轮在原预期路径提供目录，启动前核对四项运行文件、源/SDK/参数和消费者身份，再按当前 discovery/必需类型执行原完整 45 分钟门禁；缺前置即停止。预检不等同实际迁库或原生验收。不重发未变 API/Worker、不重跑已过 Unit、准入、HTTP 或配额。

### Core 续验准备超时（2026-10-18 19:41，上海时间）

本次在新的独立窗口实际启动，基线为 `main / 053294fa5477881bc73dbf3386ecfe502ffbb67c` 加既有工作区。API/Worker 两个 ELF 与原发布哈希相同，Migrator 284 个文件逐项匹配；生产源码、依赖和构建配置未变。相对上一轮通过证据，新增差异仅为知识库、前端 OpenAPI/生成 guard/整数契约测试及缓存 Integration 测试；因此复用未变的原生发布、Unit 与性能证据，Integration 仍按当前源码正常构建并登记，不能手写复用记录。

`dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release` 连同既定单节点、关闭共享编译及锁定还原参数实际退出 0，0 警告、0 错误，耗时 10 分 22.74 秒；但后续运行目录复制与正式构建身份登记未完成便达到 12 分钟准备预算，`linux-prepare` 实际退出 124。Core 和真实外部夹具均未启动，没有本轮 Core TRX，不计作原生通过。原始编译日志及阶段记录保留在自有临时控制目录 `core-completion` 对应结果中，不能将编译成功等同于整个准备阶段成功。

资源已经实际归还：SDK 容器已删除，actor 43472 的活进程句柄确认退出 1，Job 最终活跃进程数为 0，34 个自有进程已结束；物理清理记录为 confirmed，自有工作区/构建/重型锁均已释放，原两个外部数据库与旧工作区锁不动。旧窗口不延长或复用。

下一候选 `core-runtime` 依据实测调整为准备 18 分钟＋Core 原定 45 分钟＋核对 3 分钟＋清理 3 分钟，共 69 分钟；控制检查 26/26、语法检查 5 项通过。构建预留合计 4CPU/16GiB，宿主 Job 实际 2CPU/4GiB、SDK 构建 1.9CPU/12GiB，运行降为 4GiB；普通构建名额与重型运行分阶段申请。该候选尚未取得下一实际窗口，不能按预检宣称完成。共享资源下一窗口已交给 AI，等待后续物理交接后重新核验输入并启动，剩余验收仍为 Core 这一组。

### Core 实际运行的挂载失败与修正（2026-10-18 20:25，上海时间）

后续 `core-runtime` 在独立 69 分钟窗口内按时启动，当前源码 Integration 正规构建耗时 8 分 33.19 秒，0 警告/错误；产物复制、正式构建身份登记、完整 Migrator 运行目录及发布 ELF 校验均通过，准备阶段实际结束后停 SDK、释放构建名额，再申请重型资源并将 SDK 降至 4GiB。Core 正式发现 44 个唯一 UID，保留原 45 分钟测试时限，实际执行约 3 分 4 秒后取消。

实际结果为 2 项失败、0 成功，剩余未执行，运行摘要为 Aborted。失败为 `IOException: Read-only file system`，路径是 `/src/artifacts/native-aot/linux-x64/test-logs`：原生 API/Worker Runner 需要在发布目录旁写进程日志，本轮测试控制却将整个任务产物父目录挂为只读。该错误不证明生产功能失败或通过，也不能用本轮 discovery、迁库前置或构建成功代替 Core 运行。确认已创建夹具都有所有权记录后，仅向自有测试进程发送 SIGINT；SDK、3 个已识别夹具及所记自有匿名卷已删除，actor 37040 由活句柄确认退出 1，Job 最终活跃数为 0、总进程数 86，清理记录 confirmed，外来数据库和旧锁不动。

新候选 `core-recheck` 保留旧轮控制和原始失败记录，仅将本任务系统临时目录中的产物父目录挂为可写，API/Worker 两个 `publish` 子目录及两个 `publish-manifest.json` 文件分别挂为只读。新增全发布树和清单逐文件哈希；在 prepare/core 阶段、真实夹具创建前检查实际 mountinfo，确认 ELF/清单写打开返回 EROFS，并沿 Runner 实际 `test-logs` 路径创建和回读探针。父目录只读、发布/清单可写、内核仍允许写打开均会提前拒绝；新增三项回归与原控制检查合计 29/29，语法检查 5 项通过。这些控制检查不计作 Core 通过。

本轮已正规登记的 Integration 优先通过正式 `prepareTestBuild` 身份、当前输入/环境/参数及完整输出校验复用；漂移才正常重建，不手写构建记录，不重复未变原生发布、Unit 和性能。新候选继续保留独立 69 分钟上限及原 45 分钟 Core 时限，等待已就绪 AI 发布任务实际归还后再准入；旧窗口不延长或复用。

### 两阶段续验与实际输入补充（2026-10-18）

按最新资源规则，新候选 `core-phased` 不再使用人工短启动窗或将准备等待计入整组时钟。普通准备按实际输出隔离进入规范池，预留宿主与 SDK 合计 4CPU/16GiB，取得名额后才开始 18 分钟执行＋3 分钟异常清理预算。Main 源码只读，ProjectReference 输出在自有 SDK 的 `/work/jit`，Host 可写挂载只进入本任务既有 TEMP；不得借修改工作目录伪装隔离。准备成功必须实际确认 SDK 停止、限额降为 4GiB，并待前一宿主进程活句柄确认退出 0、Job 活跃数为 0、名额释放，才交给独立 Core 阶段。保留已停止 SDK 的工作目录，避免跨阶段丢失已经完成的构建和迁库前置。

Core 在实际取得重型锁及全部构建槽后独立计 48 分钟执行＋3 分钟清理，保留矩阵规定的原完整 45 分钟测试。启动前核对 SDK ID、owner、image、Created、Source/Controller 标签与前一阶段回执。发出启动操作前先撤销“已确认停止”资格；启动结果不明时，只有重新读回 Stopped 且交接状态持久化成功才能恢复保留资格。停止确认失败必须在仍持有租约的回调中等待严格物理清理；记录写失败也不能造成资源提前归还。session 或 ledger 任一存在均不能按无夹具恢复。独立复核已指出并修正该异常分支，定向失败回归及控制检查合计 35/35，六项 JavaScript 语法与 PowerShell AST 检查通过；不将这些模型或解析检查视为原生通过。

冻结证据共同覆盖 4,547 项主要输入和 18 项传递补充。逐级项目图核对共 86 个 ProjectReference，其中企业请求样例的 18 项输入（含隐式 `.csproj.user` 缺失状态）以不可变补充保存，逐项匹配原 Unit/发布生产者；Core 进入前已实际匹配，退出后还须重新核对，不能改快照接受漂移。正式 `prepareTestBuild` 的原全量输入、环境、参数和输出守卫保持不变，不手写接受旧记录。未被调用的前端或构建工具不作为自定义运行冻结范围；若原正式指纹因其变化失效，仍由正常入口判定复用或重建。

准备阶段实际在 21:44:04.787 取得名额，编译 0 警告/错误、耗时 8 分 35.92 秒；正式记录、完整 Migrator、全发布树/清单哈希、实际 RO 写打开拒绝与日志目录写入探针均通过。actor 71076 于 21:54:42.768 退出 0，Job 活跃数为 0、总进程数 36，SDK 实际 Stopped4GiB。Core 随后取得独占资源，在 21:57:08.056 固定阶段时钟，执行截止 22:45:08.056、清理截止 22:48:08.056，不重置；实际发现 44 项并开始运行。

并行准入还核对 Docker guest 总硬预留：全部运行容器 cap＋本次 SDK/夹具峰值＋2GiB 必须不超过 guest 总内存，同时保留原实际可用内存门槛，未知无界 cap 拒绝；不能仅看当前工作集让两个 12GiB SDK 挤入约 20GiB guest。新阶段没有修改正在运行的其他控制或共享构建 helper。本日志当前 Integration 依赖记录中 SignalR.Common 为 10.0.13，Client 为 10.0.10，Native API 发布清单声明 RuntimeFrameworkVersion10.0.13；这是本地实际图和清单事实，不等同新一轮安全审计通过，也不依据相同 SDK 外推其他任务。

本轮 Core 在 22:29 有序取消：运行器汇总 Aborted、11 通过、0 失败/跳过，耗时 31 分 06.256 秒，不能按发现的 44 项全部通过。SQL 容器硬限额 3GiB、无 swap，实际没有 `MSSQL_MEMORY_LIMIT_MB`，启动却识别 15,806MB；工作集接近硬限额、迁库长时间不前，只读诊断查询也未在 15 秒返回。源代码对照发现普通 `BoundedContainers` 已设置内部 2048MiB，而 Native lease 路径只设置 Docker 硬限额。已在创建参数中固定内部预算并保留其他环境项，启动回读拒绝缺失、错误及重复值，其他三类容器环境保持原样。

轻量复现项目只链接实际 `NativeContainerResourceProfile.cs` 及其测试、引用已有依赖，不复制源码或整仓，不连接 Docker；通过正式构建资源池以 CPU1/1GiB 执行。修复前 34 通过、4 个新增断言按预期失败；修复后扩展空环境及同值重复等边界，实际直接执行 40/40、无编译警告。它不是正式 Integration 运行结果，后续须合并共享 helper 修订后正常构建、执行 Profile 聚焦集与 Core。保留源代码前后哈希，不改旧冻结记录接受修复。

actor 52096 在 22:29:41.032 真实退出 1，活句柄确认 Job0/总进程82，无强制 Job 终止；SDK、三个 session 容器及四个自有匿名卷清理确认，外来资源保留，自有锁无匹配。退出后 4,547 项主输入与 22 项控制逐项不变；18 项不可变补充也全匹配。SQL 观察、取消汇总与物理清理收据保留在系统临时 `fullnet-logging-closeout-20261018/core-phased`；下一候选必须重新冻结修复后的实际输入，不续本轮截止。已有必要 API/Worker bundle、manifest 和来源凭证仍被缓存任务使用，继续保留。

原生首轮迁库失败、控制器路径失败、10 分钟准备超时与配额不利数据均保留。旧输入清单不改写接受变化；本任务三项测试/基准变更与外部调度六项变更分别登记，调度所有者工具链 145 项/治理 19 项仅作为调度证据，不计日志业务通过。运行细节、TRX/UID、输入/产物及导出 hash、退出与实际清理收据位于系统临时 `fullnet-logging-closeout-20261018`，现行功能说明同步到 Wiki/运维手册。

### F07 候选集中验证（2026-10-18）

以下为前序候选证据；最新状态以本节之前的“统一收尾续验结论”为准。

前序批次当时状态：Windows 托管功能、定向性能对照、全局治理及当前修订 API AOT 分析已通过；正式 Integration 编译已解阻，分片发现仍因实际 1,323/登记 1,288 失败，Linux 原生运行未完成。保持 F06/F07 整批未完成，不将已通过的局部结果扩大为整个模块或容量通过。

实现采用真实 `LocalFileLogStore` 的已解析日期/日内登记顺序与闭合文件有序索引；运行期每次清理最多删除 16 个文件，到期积压由后续写入或原 Writer 刷新周期继续推进。启动尚无并发 Writer，可在最多一万文件的既有上限内收敛存量。活跃文件不删除，自有文件识别、目录链接防护、租约与硬配额保留。限制的是 OS 删除次数，不是硬毫秒 SLA，也没有新增维护线程或持久重放队列。

新增 `LocalFile.PriorityReservePercent`，默认 20、范围 0–50、0 关闭，修改重启；配置绑定、漂移检查、Helm values/schema/env 与 Wiki 同步。普通写入回收时尽量保护优先历史，按完整文件允许一个文件的粒度余量，但保护数不得占满名额，且仍留出普通通道一个完整文件的空间。到期不受保护，优先写入可替换旧优先历史；配额或有限回收预算不足时丢弃当前 B2 项、继续后续回收，不临时突破目录上限。

八项新增真实文件回归覆盖优先历史保护、单次删除有界及不再推进时钟时的积压进展、关闭保护、到期覆盖保护与优先自我替换、文件名额留给普通滚动、有限回收不足后的恢复、配置漂移须重启、最大普通事件仍有空间。原目录租约、双通道滚动、脱敏与链接防护用例一并复验。

基准入口为 `Full.NET.Benchmarks.dll logging-local-maintenance --output <新的系统临时目录>`。固定 16/1,000/9,900 个 256 字节既有文件，稳态/分钟维护/到期/配额四种阶段，预热后正反序各一轮；每场 4,096 普通事件、100 优先事件、128 字符载荷、各通道队列 256/1MiB，优先生产器请求 2ms 延时。两条真实 `FullNetBoundedAsyncSink` 后台线程复用实际快照与文件出口，虚拟 UTC 只触发维护，队列年龄用真实单调时钟。场景文件在后台真实退出后删除，只保留有界统计。维护调用包含目录锁等待，纯锁等待未独立拆分；出口返回不等同 fsync 或端到端确认。

环境为 Windows 10.0.19045、i7-12700H、SDK10.0.402/Runtime10.0.13、CPU2/Job4GiB，同机存在其他开发负载；不认定稳定业务请求 P99 或未测规模容量。基线与候选的队列、载荷、目录/文件配额及运行环境字段逐项对账，均完成 24 场景。主要两轮观测如下（斜线分隔第一/第二轮）：

| 场景与指标 | 基线 | 候选 |
| --- | --- | --- |
| 9,900 文件到期，单次维护 ms | 6698.86 / 4786.20 | 3.88 / 4.06 |
| 同场景优先事件排队至出口返回 P99 ms | 6684.68 / 4778.36 | 143.26 / 155.20 |
| 同场景普通丢弃 / 4096 | 3839 / 3839 | 2144 / 2126 |
| 小目录配额，最终优先保留 / 100 | 2 / 2 | 16 / 16 |
| 1,000 文件配额，最终优先保留 / 100 | 16 / 16 | 58 / 72 |
| 9,900 文件配额，最终优先保留 / 100 | 16 / 16 | 100 / 100 |
| 9,900 文件配额，普通丢弃 / 4096 | 630 / 751 | 0 / 1182 |

结果支持削减到期批量清理的长暂停与改善优先历史保留，但第二轮大目录配额的普通丢弃由 751 增至 1182，部分小目录尾延迟也有波动；不宣称所有吞吐、丢弃或 P99 全面改善。日期/登记顺序的回收与优先保护改变了可回收文件集合，很多小文件的物理删除仍有成本；两轮短测与非独占机器也不足以把波动归为稳定回归或稳定收益。普通/优先都仍处于共同磁盘故障域。

已执行验证：

- [x] SDK402 Release：原依赖编译 8:01.48、0 警告/0 错误，因其他人类授权对话补全 `Full.NET.slnx` 三项漏登记而被守卫拒绝；不恢复该修复，不沿用失效签名。稳定后正规增量 24.02 秒、0 警告/0 错误并重新登记，未绕过守卫。首次准入瞬时内存不足未启动 actor，恢复后仍按原物理12GiB/提交16GiB门槛进入，无降门槛或重置截止。
- [x] 日志组实际发现 404 个唯一 UID，403 通过、0 失败，唯一未执行为 Windows 不支持的 Linux FIFO；Local 18 项全通过。TRX 与发现 UID、构建/运行前后守卫均核对；AOT Architecture 实际 73/73，0 失败或跳过。
- [x] 候选 24 场景队列/在途字节、文件硬上限及磁盘/逻辑计费对账；所有优先消费丢弃为 0，最终历史保留另列，不把消费成功当作仍被保留。
- [x] Local Helm 实际行为专项 1/1、当前测试矩阵 Node 7/7。三个旧治理断言要求已退役根 artifacts 路径，现保留原失败证据，并改为真实入口脚本的本地 TMP/CI 导出路径、filter、timeout 和专用 TRX 参数检查。只复制几个脚本/配置到有限夹具，外部 SDK 边界模拟；专项 12/12、全局 84/84，0 失败/跳过。首轮夹具的 Windows `--import` 路径错误已定位并改用 file URL，失败未覆盖。该组不代表 Native 发布或运行。
- [x] 共享 `SharedDatabaseFixture.cs` 的三处 `PullPolicy` 缺少 `DotNet.Testcontainers.Images` 导入已补，正式 Integration Release 重编通过，0 警告/0 错误；按原内容 hash 核对仅此导入及格式变化，原缓存镜像与 Native 覆盖逻辑保留。
- [x] 当前修订 API AOT analyzers 及默认 JIT 还原图通过；复用未受共享夹具导入影响的 Core Unit、Architecture 和 Local 维护基准结果。
- [x] 后续已实际补齐分片 UID 对账，按全量/迁移/基础设施真实差额修正最低登记，正式检查通过；本节当时 1,323/1,288 的失败记录保留，不改写为旧轮通过。
- [x] 后续已完成准入矩阵 24 组、HTTP 六组、持续配额八组及 Linux 日志 404 项；API/Worker 实际原生发布已完成。
- [x] 核心 Native 整改闭环：原完整 44 项实际 43/1/0，修正夹具后受影响退休 4 项补验 4/4；原不变 40 项与补验逐 UID 完整对账，原失败 TRX 保留，不冒充同轮全部通过。

本轮补验（北京时间 2026-10-18 12:30—12:46）：使用 SDK 10.0.402、Windows Job 实际 4GiB/CPU2 限额和官方 normal/heavy 双锁。Integration 构建约 1 分 44 秒，API 完整闭包 AOT 分析 7 分 45 秒，默认 JIT 还原与基准 Release 构建均通过，构建均 0 警告/0 错误。`node scripts/testing/verify-integration-shards.mjs` 因 1,323/1,288 不一致失败；独立编译/分析分组保留通过，整体入口退出 1。原执行截止 12:46、清理截止 12:49；最后构建在 12:45:50 完成，末尾内容守卫与退出在 12:46:04 完成，位于原清理预算内，未延长或启动额外组。实际 Job0、自有进程与双锁已归还，共享双库和两把外来旧锁保持；本轮失败 TEMP 仅保留 113 字节运行收据。原始日志、构建身份、完整输入前后清单、`summary.json`、`terminal.json` 与实际落盘的 `physical-receipt.json` 位于下述证据根的 `candidate/final-checks`；准入矩阵及分片 UID 补采未执行。

原始证据：系统临时 `fullnet-local-maintenance-20261018-b9d2e473` 保存基线；其 `candidate/retry` 保存候选/TRX/比较、构建身份及前后 7,332 条输入摘要（完全相同）。父进程持柄退出 1/Job0，失败原因是后继 Integration 编译，不抹去已通过分组；自有 actor/监督/末个编译进程消失、双锁自有所有权已释放、无自有 Docker/DB，共享双库 ID/StartedAt/预算保持。Root 之后合法持有的重型锁完整保留，不要求全局无锁来伪造收据。轻量治理证据在 `fullnet-local-config-20261018-c0a8e156/governance-repair/retry`，Job1GiB/CPU1、持柄退出0/Job0，限定输入前后 SHA 一致。

源码漂移、内存准入、收据竞态与清理策略拒绝的完整原记录保留在各控制目录；一次过早声称收据已写入已更正为实际所有权核对。最小失败收据、因命令策略拒绝删除而保留的约11KiB未采信逆向阅读参考均不是正式源码冻结或验收证据；不通过其他解释器/工具绕过拒绝，不把它们混称已清理。正式场景工作文件已实际删除，不复制整仓、依赖或二进制。

## B1 整队列故障验证批次（2026-10-18）

此前真实双库故障证据止于 `AuditWriteBatchWriter`。本批已在真实 Host 注册的 `AuditMicroBatchCoordinator` 上完成验证，不新增生产测试开关，不修改 B1 可靠性等级；所有关联场景写完后集中构建和执行。

- [x] 扩展 `tests/Full.NET.IntegrationTests/Auditing/AuditingBatchFaultAssertions.cs` 的私有表锁与会话观察能力，供同目录协调器故障断言复用；仍仅操作本用例临时数据库，并先确认实际 INSERT 锁等待再注入故障。
- [x] 新增 `tests/Full.NET.IntegrationTests/Auditing/AuditingCoordinatorFaultTests.cs` 的双库精确用例：批次期限、连接丢失、协调器停止。固定容量 2、单批 1 行，在首批真实阻塞期间填满两个排队位置并验证下一条入队超时；已接受调用的请求取消不得跳过写入尝试。
- [x] 验证接受任务全部终结、数据库实际调用已退出、在途及排队字节预约最终归零。所有故障场景让积压过期后跳过 SQL；期限及断连解除后原协调器恢复；停止场景验证关闭后拒绝与排空，不将取消等待等同取消数据库 I/O。失败清理仍须等待真实操作退出。
- [x] 同批监听实际 B1 Meter，检查 accepted/rejected/failed/flushed、等待与批次直方图、深度/容量/字节/年龄及受控标签；进程内仪器证据与后续 OTLP→Collector→Prometheus 证据分别登记，不相互替代。
- [x] 配对用例和最低发现数接入机器测试矩阵；SDK 10.0.402、Release、官方 normal/heavy 双锁、构建身份校验、Windows Job 4 GiB/CPU 2 下集中执行，核对精确用例与零跳过。资源窗口真实归还后才恢复源码输入写入，不因空锁提前改动。

本地验收：Release 构建 1 分 48 秒、0 警告/0 错误；`auditing-coordinator-faults` 分片实际发现 6 个唯一 UID（双库各三个故障场景），执行 6/6 通过、0 失败/跳过，耗时 3 分 43 秒，入口退出 0。测试前后及 Node 检查后的构建输入/产物校验通过。表锁、Writer 和协调器均为真实路径；本批未修改生产协调器算法。故障夹具的 10 秒批次期限用于留足锁等待观察窗口，不修改产品默认 5 秒设置。

六个临时库的创建/清理由各用例明确持有：Host 释放后校验随机库名再删除，MySQL 同时收回该库授权，TRX 保留六条完成收据。父进程持柄退出 0、Job 活动进程归零，自有双锁及临时运行目录已回收，共享双库容器和 foreign 锁保留。最小原始证据位于系统临时 `fullnet-b1-coordinator-5eaf87f1c577468db525ee68334c1c79`，仅保留 TRX、发现/汇总、构建身份及监督记录，不复制源码或二进制。

相关 Node 检查实际 51/52：新增选择器通过，矩阵仍缺共享 AI 迁移 272 的登记。按矩阵变更要求执行全局分片与治理：发现 1291、当前登记 1287；治理 81/84，Notifications、Settings/Jobs、OIDC 仍因旧 `artifacts` 路径断言失败。以上全局缺口未通过，不以本批六项通过覆盖，也不为通过检查改写无关计数。

B1 真实导出组复用已验证的固定 Collector/Prometheus 配置，独立核对正式 B1 告警原窗口的 firing/resolved；该组已按下节实际完成，证据与本批进程内指标检查分别登记。没有证据揭示生产缺陷前不修改协调器算法，已有 B2、Kafka/ES 场景不无条件重跑。

### B1 正式指标与告警闭环批次

状态：2026-10-18 本地真实链路验收通过。夹具、专项矩阵与影响路由已落地；`node --test tests/testing/run-affected-integration.test.mjs tests/testing/test-matrix.test.mjs` 实际 53/54 通过，新增路由均通过，唯一失败仍为共享 AI 迁移 272 未登记。分片最低数合计与 full 当前登记一致；全局发现及治理缺口单独列于下文，不把专项通过扩大为全局绿。

使用现有真实 API Host、SQL Server 与固定摘要 Collector/Prometheus，新建 `tests/Full.NET.IntegrationTests/Observability/AuditB1MetricsPipelineTests.cs`，不修改或重跑已通过的 B2 夹具与六项双库故障用例。双库 I/O 差异已由前组覆盖，本批聚焦同一 Auditing Meter 的实际注册、OTLP 导出及 PromQL，不将本组解释为 MySQL 指标链路独立实跑。长时告警放入独立分片；普通 Auditing 查询不自动包含它，微批/Meter 注册及共享导出配置变化则显式追加。

- [x] 直接加载实际部署 Collector 配置及正式规则，保持 WAL、五分钟统计与五分钟 `for`，固定镜像只用缓存；测试 OTLP 客户端直连且不产生被测日志，资源与地址仅指向自有容器。
- [x] 通过真实协调器提交健康项、数据库约束失败项及超大项，验证 accepted/flushed/failed/poisoned/rejected 与等待/批次直方图；以已产生的稳定初始样本建立基线，不能靠手工写入监控值制造零基线。
- [x] 固定队列容量 8、单批 1 行，九个固定异步调用循环，真实 SQL 命令前注入可取消两秒等待以持续施压；最大排队年龄显式设为一分钟，仅用于夹具。消费者单次取出后深度至少为 7，仍超过 75% 阈值；不增长并发数或挂起无限请求。另以单循环受控拒绝维持失败告警条件。
- [x] Prometheus 验证实际 job/instance、队列条数/字节/年龄、低基数标签及 `FullNetAuditB1QueuePressure`、`FullNetAuditB1WaitOrFailure` 均进入 firing；停止负载并完成所有已接受写入，核对队列/字节归零、数据库落行数与计数对账，原窗口滚动后两条告警均恢复。
- [x] 新专项分片登记与影响路由、定向 Node 检查、SDK 402 Release 正式构建、精确 UID/原始 TRX、前后输入/产物守卫及资源清理在一个完整批次验收。本批保留内部 18 分钟、MTP 20 分钟及独立清理预算，未挪用缓存依赖准备窗口。

正式执行 `auditing-metrics` 专项：SDK 10.0.402、单节点及禁共享编译/节点复用、locked restore；Release 构建 1 分 46.53 秒，0 警告/0 错误。实际发现 1 个唯一 UID，MTP 执行 1/1 通过、0 失败/跳过，耗时 12 分 17.561 秒，入口退出 0；TRX 与发现 UID 一致，构建后、测试后及 Node 检查后的输入/产物守卫均通过。

北京时间 08:43:22 基线就绪，08:44:22 队列及直方图导出就绪，08:49:24 两条正式告警均触发，08:49:24–29 实际预约归还及数据库/计数对账通过，08:54:26 原窗口滚动后恢复。最终接受 189 项，含 1 项约束失败/毒记录；188 个成功批次对应 188 条健康落库，受控超大项拒绝累计 182，队列深度/字节/年龄均归零。正式失败与压力规则没有缩窗；全程无 Remote Write 乱序拒绝、标签契约检查通过。

环境为 Windows x64 托管 API、共享本地 SQL Server 和同机 Docker；Windows 整树 Job 4 GiB/CPU 2 经真实回读和挂起分配后恢复，Collector/Prometheus 各 512 MiB/CPU 1、固定缓存摘要且禁止拉取。父进程持柄退出 0、Job 活动进程归零，自有随机库、两后端容器、Ryuk、匿名卷和网络均闭合，临时运行目录/双锁已释放；共享双库 ID、启动时间及限额与组前一致。证据仅保存在系统临时 `fullnet-b1-metrics-74edaec7a1e34c72a0df9b2c32912f6f` 的 TRX、发现/汇总、构建身份、限额/清理收据及有界 Prometheus 状态，不保留源码或二进制副本。本组为受控压力告警验证，不计请求 P99、容量、Tempo 或 Linux/AOT 验收。

按矩阵变更要求执行的全局检查仍未通过：实际发现 1292、登记 1288；治理 81/84，失败仍为三项 Native 脚本旧 `artifacts` 路径断言。相关共享缺口原样保留，不为通过改写无关计数或重跑已通过的其他日志组。

## 集中验收与交付

### Host 指标真实导出与告警（2026-10-18，配置通过、链路待验收）

下一批使用真实 Host / OpenTelemetry SDK → 固定摘要 Contrib Collector → 固定摘要 Prometheus，直接加载现行 Collector `config` 和正式告警规则，只替换隔离测试目的地。通过普通与优先后台出口阻塞，验证两个队列的深度、容量、在途字节、最老事件年龄及累计丢弃，恢复后核对字节归零；优先丢弃告警须经历实际触发和恢复，保持原五分钟 `increase` 与一分钟 `for`。

配置调查发现 `prometheusremotewrite` 使用 `remote_write_queue` 和独立 `wal`，现有 overlay 却复制了 OTLP 的 `sending_queue.storage`。须用固定版本 Collector 复现严格配置校验失败，再替换对应配置并执行真实链路；Trace 的 OTLP `sending_queue.storage: file_storage` 保留。`attributes` 官方元数据支持 Metrics，不将未经证实的处理器能力怀疑作为修改依据。参考 [v0.141.0 配置定义](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.141.0/exporter/prometheusremotewriteexporter/config.go)。

共享缓存/Data 批次释放资源后，使用统一重型锁和固定摘要 Contrib 0.141.0 二进制复现原配置的 `invalid keys: sending_queue`。已修订为专用队列（单消费者）和 WAL，并固定 overlay 镜像版本/摘要；Trace 队列不变。新增可复用 `eng/testing/otel-collector-config-test.mjs`，直接提取部署配置并模拟其 storage 挂载，真实 `validate` 退出码 0。裸容器未挂载目录时的校验失败不认定为部署缺陷；该目录由 Helm 的 emptyDir 提供。emptyDir 不覆盖 Pod 替换或节点丢失，WAL 不等于端到端投递确认。

新增真实指标集成场景 `HostLoggingMetricsPipelineTests` 和矩阵聚焦集 `logging-metrics`。首次 Release 构建因测试中的 `Host` 名称解析冲突失败（0 警告、1 错误），测试未启动；已改用完整类型名，并收紧缺失时间序列不能误通过的判断。该修订尚未重新编译，不登记 Build-verified 或链路通过。正式重跑需使用统一重型锁、构建输入校验和聚焦集最低发现数；必须实际完成原窗口内的告警触发/恢复。

部署契约 10/10 通过；测试矩阵契约 6/7，仍因共享 AI 迁移 272 未登记而失败；全分片发现 1284、当前登记 1281，包含其他未登记共享用例，不能报告全局矩阵通过。治理检查实际 81/84，Notifications、Settings/Jobs、OIDC 三项历史产物目录断言与共享脚本修订不一致；不扩大日志范围修复这些独立问题。

首轮构建进程自然退出并释放锁，失败目录为系统临时 `fullnet-test-run-RbbJEr`，未启动本组业务容器。后续资源核对确认缓存任务原验收窗口仍有效；空锁不代表窗口交接，因此暂不启动下一组，源码冻结让其继续，原顺序不变。只复用已缓存固定镜像；未删除任何新锁、共享数据库或其他任务资源。

2026-10-18 后续只读核对发现真实场景的等待预算还需与当前配置对齐：固定 v0.141.0 的 [WAL 实现](https://github.com/open-telemetry/opentelemetry-collector-contrib/blob/v0.141.0/exporter/prometheusremotewriteexporter/wal.go)在读取请求后检查 `buffer_size` 或 `truncate_frequency` 决定发送，当前为 100 和 1 分钟，并非每次 SDK ForceFlush 后立即发往 Prometheus。测试的 45 秒基线/40 秒队列观察窗口短于该配置周期；正式执行前须按真实配置推导预算并增加阶段诊断，不以 ForceFlush 成功代替下游收到数据，也不缩短正式告警的五分钟窗口。当前尚未运行该场景，不将此静态发现写成已复现运行失败。缓存新的集中窗口期间只更新本计划，保持源码/测试/配置/构建输入冻结；该窗口最晚于北京时间 2026-10-18 06:03 清理结束。

同轮调用链审查确认 `AddFullNetServiceDefaults` 已生成日志 `Instance`，但 OTel 未注册相同的服务资源标识，Helm 也未提供 `service.instance.id`。SDK 1.17.0 的 [默认 Resource](https://github.com/open-telemetry/opentelemetry-dotnet/blob/core-1.17.0/src/OpenTelemetry/Resources/ResourceBuilder.cs)只补默认 `service.name`，不会自动补实例 ID；因此存在多实例 Remote Write 时间序列合并风险，尚未以运行测试复现。下一实施批次将用资源断言先验证缺口，再统一 Trace/Metrics 与日志的实例标识；服务名默认采用宿主名并保留显式 `OTEL_SERVICE_NAME`，不添加租户、用户或 URL 标签。覆盖默认/显式服务名、同一 Host 的 Trace/Metrics/日志标识一致和不同 Host 实例 ID 不同；真实 Prometheus 场景同时核对 `job`/`instance`。该宿主路径变化须纳入 Unit、Architecture、AOT 分析与相应原生路径影响集，未执行不得报告通过。

同批补齐监控器所有权：原 `FullNetLoggingMonitors` 以预创建实例注册，DI 不负责释放；改为容器拥有的工厂实例，并在 Serilog 回调通过同一个 DI 服务解析，保持 Logger 停止后、Host 完全释放前的终态计数语义。增加真实 MeterListener 断言：Host 存活时可观测自有仪器，释放后不再回调，避免测试或宿主反复创建残留仪器。新增真实指标场景及相关部署配置/核心注册变化接入 `logging-metrics` 影响集，核心注册仍保留 Smoke；同步选择器回归。

缓存批次确认测试结束并归还进程/锁后，已将上述四项草稿应用到当前工作树：观察预算按 WAL 周期加 20 秒余量推导，正式告警窗口不变；实例资源统一、DI 所有权和影响集路由均已实现，真实测试清除运行机 OTEL 配置后仅连接自有容器。新增 4 个资源/生命周期用例，原有资源测试一起进入日志聚焦选择，计数只维护于机器矩阵。选择器与部署契约 `node --test tests/testing/run-affected-integration.test.mjs tests/deployment/observability-contract.test.mjs` 实际 54/54 通过，0 跳过/失败；本任务差异检查通过。修订前 .NET 回归未单独执行，不能把静态缺口核对写成 RED 运行证据。

后续窗口安排了不挂载源码的 SDK 工具镜像增量；本轮未抢占其重型资源。日志无容器回归使用官方快速套件及同工作区锁，SDK 10.0.402、CPU 数 1，执行 `unit --selection logging-delivery --reuse-build`，实际结果如下。真实 Collector/Prometheus、AOT 分析和原生范围仍待相应实际执行；不因代码已实现、Node 检查通过而登记通过。

该无容器组首次 Release 0 警告/0 错误，但构建期间两个解析器锁文件被还原更新、共享缓存集成测试被修改，入口拒绝登记和执行，测试未启动；失败目录为系统临时 `fullnet-test-run-SBeBIU`。核对变化来源后，通过同一官方入口增量重建（24 秒，0 警告/0 错误）并登记有效产物：共 396，395 通过、0 失败、1 项 Windows 不支持的 Linux FIFO 跳过，耗时 15 秒，入口退出码 0。包含资源标识与生命周期 5/5（其中 4 项新增），不将第一次未执行算通过。日志父/子进程自然退出、当前工作区锁归还；随后前端原准入窗口优先，暂不启动新的 .NET 或重型组。治理 81/84、矩阵契约 6/7 和分片 1284/1281 的共享问题仍原样保留，真实指标链路、AOT 分析及原生尚未通过本修订验收。

随后通过 SDK 10.0.402、CPU 数 1 的正式入口执行 `architecture --selection api-native-aot --reuse-build`：Release 构建 0 警告/0 错误，实际 73/73 通过、0 跳过/失败，入口退出 0。`node scripts/testing/run-api-aot-analyzers.mjs` 由同工作区锁保护执行，分析器构建 4 分 20 秒，0 警告/0 错误，默认 JIT 还原成功、入口退出 0；仅登记 AOT 分析检查，不登记 Native 发布或运行通过。

2026-10-18 用户明确要求“优先日志，并授权资源协调”，替代此前不发协调消息的安排。已通知资源协调任务：当前运行组自然结束，下一窗口优先完成日志真实指标验收；继续通过官方 normal/heavy 锁和构建身份校验，冻结源码、测试、配置及 code-wiki，不抢占其他进程。固定镜像已核对本地缓存，测试显式禁止拉取；本组不使用数据库、Kafka、ES 或 Redis。此安排不等于真实指标测试已经通过。

#### 真实指标首轮结果与后续修订（待重验）

2026-10-18 用户授权日志优先后，通过官方 `withTestRun(heavy: true)` / `prepareTestBuild` 运行 `HostLoggingMetricsPipelineTests`，SDK 10.0.402、`DOTNET_PROCESSOR_COUNT=2`；Release 构建 4 分 13 秒，0 警告/0 错误，输入校验通过。实际仅 1 项：0 通过、1 失败、0 跳过，测试入口退出 2、外层退出 1，耗时 2 分 39 秒。Prometheus 零丢弃基线在约 58 秒送达，真实 `job`/`instance` 断言通过；制造饱和后的指标条件在原 80 秒预算内未满足，告警触发与恢复未执行完成，整项不得登记通过。

同时捕获两类运行证据：OTLP 命名 HttpClient 的信息日志及 Polly 自日志进入容量仅两条的被测队列，观察动作会改变普通日志丢弃数；Collector Remote Write 收到 Prometheus `400 out of order sample`，部分日志指标可查询不能覆盖此错误。已核对固定 v0.141.0 的请求并行、WAL 和队列源码，尚未确定乱序拒绝的根因，不以调大接收窗口或丢掉相关指标掩盖问题。

测试仅对自有 OTLP 客户端移除日志及韧性处理器并使用直连，避免观测传输污染队列；增加饱和期间进程内丢弃数保持 1 的断言，以及完成时不能存在乱序拒绝的断言，失败时同时保留 Prometheus 日志。实际 Host、OTLP、Collector 部署配置、Prometheus 和原五分钟/一分钟告警断言均保留。此修订尚无有效重新编译和运行证据。

首轮容器实际限额经 inspect 核对为各 512 MiB、CPU 1；Windows 仅有 .NET CPU 数提示，没有原生 Job 硬限额。后续构建为补齐整树限额而中止，未开始第二次测试；适配既有监督脚本后，启动已超过保持完整 12 分钟预算的最晚准入时刻，入口按原截止拒绝运行，没有生成 Job 验收证据或延长窗口。测试进程、业务容器及自有 normal/heavy 锁已归还，共享双库和 foreign 锁保留。下一次正式执行需新鲜窗口，以真实 class9/class15 回读、挂起后分配 Job 再恢复、持柄退出及 Job0 核对限额和归还。

最小原始失败记录为系统临时目录 `fullnet-logging-metrics-evidence-TDCPP0` 中的 TRX 和摘要，未复制源码；正式状态保持“首轮失败，修订待重验”，不影响前述已完成的 Unit/Architecture/AOT 分析事实，也不扩大成 Linux/AOT 或容量验收通过。

继续核对上游后确认当前 0.141.0 不包含 [PR #44467](https://github.com/open-telemetry/opentelemetry-collector-contrib/pull/44467) 的 WAL 修复：原 `createWAL` 没有开启 `AllowEmpty`，截断时不能删空，读写索引重新同步后可能重发旧样本。该修复于 2026-01-07 合并，列入 0.144.0，关联 [乱序样本问题 #41785](https://github.com/open-telemetry/opentelemetry-collector-contrib/issues/41785)。当前部署确实启用此 WAL 路径，原始 400 与上游故障机理吻合；这是必须处理的依赖缺陷，但尚无升级后的本地对照证据，不认定为本次失败的唯一原因。下一步先升级到包含修复、可实际取得并校验摘要的稳定镜像，执行真实配置校验，再运行同一完整指标场景；不能仅移除测试干扰后继续把旧镜像作为合格候选。

2026-10-18 只读核对时官方最新发布页指向 0.163.0；本机 Docker Registry 对该标签返回 `not found`，对 0.144.0 元数据请求两次返回 EOF。尚未取得可验证的新镜像摘要，也未拉取或修改当前固定镜像版本；不写入猜测摘要。已向资源协调说明需要把下一窗口调整为有界依赖准备，当前没有启动新的重型组。升级目标、实际可用镜像及运行结果确认后再同步当前配置和知识库，历史 0.141.0 失败证据保留。

后续资源协调明确允许在有界准备窗口核对 0.163.0 的官方镜像，但不在同窗追加业务测试。按[官方安装说明](https://opentelemetry.io/zh/docs/collector/installation/)的明确 Docker Hub 与 GHCR 仓库查询：Docker Hub 经认证的 manifest HTTP 请求实际返回 404，GHCR 的 `0.163.0` 元数据查询同样返回 `not found`。因此未取得 linux/amd64 manifest 摘要，没有执行 pull、创建新 Job 或运行 validate；不把 GitHub 发布页存在等同镜像已可用，也不无界尝试第三方镜像站或临时编译 Collector。该准备窗口按依赖不可用提前归还；后续取得官方固定镜像后再集中完成配置升级与原 UID 重验。

2026-10-18 继续核对发行仓库后发现 [0.163.1](https://github.com/open-telemetry/opentelemetry-collector-releases/releases/tag/v0.163.1) 已专门修复 0.163.0 的发行问题，其清单中本项目使用的组件为 0.163.0。官方 Docker Hub 认证查询实际成功，取得多平台索引摘要 `sha256:1175b03003053962012f5a231f5567f66a3e8fdb960c7835c0e48f7aa1555806`；linux/amd64 清单摘要 `sha256:45c3ed5eb9eb71e3e6ade7fa67aed000e5f68bb22fe730a6fa66a2504077cfce` 已对原始内容计算 SHA256 核对，对应配置 `sha256:10b693a2a8318fbd8c62e2e35b49461bae18f7f7eae43741990cbf623b80c1ed` 确认为 linux/amd64。版本不可得的阻塞已解除，但尚未下载、升级或通过验收。

升级核对采用真实组件定义：Remote Write 的当前类型为 `prometheus_remote_write`，HTTP 设置转入 `http`，显式保留 `UnderscoreEscapingWithSuffixes` 与请求并行 1；原队列、WAL、Trace 的持久队列和文件挂载继续保留。OTLP、file_storage 的现用字段仍有对应定义。下一窗口完成这些相关实现后运行真实二进制 validate，随后集中复验原指标场景；缓存构建当前持有共享资源，正式输入保持冻结，不边构建边改代码。

本轮继续使用 `fullnet-performance-hardening` 和 `systematic-debugging` 约束，复用原任务快照及 HEAD `053294fa5477881bc73dbf3386ecfe502ffbb67c`。镜像准备控制仅保存在系统临时目录，不复制工作树；先核对固定平台缓存配置，再以禁止拉取、禁网络的自有容器校验实际部署配置，并记录真实退出及限额。该步骤只验证配置，不替代随后正式 Host 指标、饱和、告警触发/恢复或容量场景。

#### Collector 0.163.1 配置升级（配置验收通过，指标链路待重验）

部署 overlay、集成夹具常量和直接契约已统一为官方 GHCR 的 0.163.1 与索引摘要 `1175b030…1555806`，linux/amd64 清单为前述 `45c3ed5e…4077cfce`。Trace 改用当前 `otlp_grpc` 类型，Metrics 改用 `prometheus_remote_write` 和嵌套 `http`，显式保留指标名称转换与请求并行 1；队列、WAL 和存储挂载未移除。Wiki 与运维说明同步这些实际设置。

Docker Hub 首次获取在 daemon 认证阶段明确返回 EOF、CLI 自然退出 1；保留原失败及从初始不确定恢复为已闭合的记录，不计下载成功。随后在同一绝对预算内从官方 GHCR 获取相同版本、索引及平台清单，实际成功，缓存 RepoDigests 已核对。Docker 29 的本地 image ID 不用作 manifest/config 摘要的替代证明。

通过官方双锁、真实 Windows Job 4 GiB/CPU 2（设置后回读并挂起分配再恢复）执行配置准备。唯一自有 Collector 容器采用禁拉取、禁网络、512 MiB/CPU 1，加载当前 overlay 提取的原配置及其 storage 挂载；实际 `validate` 退出 0。随后 `node --test tests/testing/run-affected-integration.test.mjs tests/deployment/observability-contract.test.mjs` 实际 54/54 通过、0 失败/跳过。持柄父进程退出 0、Job 活动进程归零，容器及自有双锁已释放，共享数据库保留。原始最小记录位于系统临时 `fullnet-logging-metrics-supervision-11ad277b85b34ef5856f67aac80ddadb/collector-01631-ghcr`。

共享 Integration 构建另捕获旧夹具 `RemoveAllResilienceHandlers` 的实验性诊断 `EXTEXP0001`，该构建失败且未发现/运行用例；现已改用稳定 `ConfigureAdditionalHttpMessageHandlers` 仅隔离自有客户端，未设置全局 NoWarn。此修订仍须完整正式构建证明，旧单元测试和本次配置验收不覆盖其编译结果；随后必须完成原 Host 指标、双通道饱和及正式告警触发/恢复场景。

#### 原 Host B2 指标与告警集中复验（本地验收通过）

2026-10-18 使用 SDK 10.0.402、CPU 2、Windows 整树 Job 4 GiB，按正式 normal/heavy 双锁及 `prepareTestBuild` 在当前工作树执行 Release 构建。构建使用单节点、禁并行/共享编译、禁节点复用及 locked restore，实际 7 分 40 秒，0 警告/0 错误，输入及产物校验通过；夹具实验性 API 编译阻断已由稳定 API 修订后的真实构建消除，没有全局关闭诊断。

精确选择 `Full.NET.IntegrationTests.Observability.HostLoggingMetricsPipelineTests.Host_queue_metrics_reach_prometheus_and_priority_drop_alert_fires_then_resolves`，最低发现 1，保留 MTP 12 分钟及内部 10 分钟预算：实际 1/1 通过、0 失败/跳过，耗时 6 分 14 秒，入口和监督退出码均为 0。TRX 核对精确类名/方法名，测试后再次校验构建输入与产物，未补构建或追加重跑。

真实 Host 经 OTLP→当前部署 Collector→Prometheus，零丢弃基线约 59.5 秒可查询；两通道容量均为 2，分别产生 1 次丢弃，饱和深度、字节/容量及最老记录年龄均按原断言验证。释放出口后在途字节归零；正式 `FullNetPriorityLogDrop` 告警先触发，随后原五分钟 `increase` 窗口滚动到零并恢复，未缩短一分钟 `for`。资源 `job`/`instance` 与宿主标识一致；完成时 Collector 不存在 `out of order sample` 的断言通过。该组同时修订了 WAL 依赖和测试自干扰，因此证明修订后的链路通过，不将首次失败追认为仅有某一个根因。

本组为 Windows x64 .NET 进程及同机 Docker 的合成日志指标场景。Collector/Prometheus 各 512 MiB/CPU 1、只使用缓存固定镜像且禁自动拉取；不覆盖 B1 全部指标、Tempo 下游、Linux/AOT 运行或容量。持柄监督确认父进程退出 0、Job 活动进程归零；自有 Collector、Prometheus、Ryuk 及双锁已清理，共享数据库保留。成功测试临时输出由官方入口回收，仅在系统临时 `fullnet-logging-metrics-supervision-11ad277b85b34ef5856f67aac80ddadb/metrics-01631` 保留 TRX、摘要、构建日志和限额/退出收据。旧 0.141.0 失败证据保留，不覆盖或改写。

- [x] 每批关联实现及测试场景一起完成；先受影响编译、Unit/Architecture/契约，再同场景 SQL Server/MySQL、真实 Kafka/ES、Linux 文件与 AOT、故障/性能。共享资源使用正式入口，不抢其他任务锁；资源协调按用户最新授权执行。
- [x] 复用只限实际输入、产物和测试身份一致的范围；旧冻结清单不改写。分片按真实 UID 修正登记，失败、跳过及中止均不计通过；文档更新后的差异另存，不冒充旧构建身份仍有效。
- [x] 真实 Host 指标及正式告警已完成本地闭环，接收/Broker ACK/ES 可查询分别按既有专项对账。性能按实际 CPU/内存、载荷及规模报告，未测范围保持 `Capacity-not-verified`。
- [x] 已同步 Wiki、运维手册及活动计划，补齐保留权衡和不利 P99、区分发布与运行；清除运维手册中失效的 Legacy/宿主 ES Sink 默认及兼容状态说明。历史失败保留原事实。
- [x] 本轮任务差异、分支及工作区已检查；Markdown 编码 9/9、91 个相对文件链接及任务范围空白检查通过。仍在 main，未自动提交、推送、部署或切流。

受影响入口：`pnpm test:inner -- --snapshot logging-remediation-20261017 --plan`、`pnpm test:slice -- --snapshot logging-remediation-20261017`；AOT 与日志真实链路选择按测试矩阵和最终影响集执行。共享编译按 `--reuse-build` 身份校验复用，不随每个文件修改重复启动整套验收。
