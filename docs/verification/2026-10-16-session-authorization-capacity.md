# 会话权威读取容量异常契约修复

日期：2026-10-16。来源为[连接预算复测](2026-10-16-cache-connection-budget-recheck.md)发现的普通 JWT 会话读取容量不足被转换为 401 的问题。本报告记录修复与分阶段证据，不覆盖历史失败，也不构成容量认证。

当前结论：**r7 诊断采集已按停止规则完成，容量验收仍未通过。** C2 冻结候选功能门禁 74 Unit / 235 Architecture / 7 Integration / 20 Native 已通过；诊断保护测试 22/22、双库驱动生命周期 2/2 通过，后置证据核验实际退出 0。r7 汇总 15 个正式样本、807,312 请求（9 段及 572,914 请求复用），12 段满足本地预算、3 段因 SQL 新系列缺基线为 Incomplete；压力父 97120 实际退出 1、正式 run failed、acceptance=false 保留。MySQL 500 RPS 因 200 RPS 已触停止条件而未执行，不计通过。资源已交出，缓存无后续自动压力或等待程序；状态保持 Capacity-not-verified。

## SQL 总量观测补齐（2026-10-17，功能已实现、待 .NET/Native 验收）

r7 后续开发以提交 `42d6714ee48ed58447fe608b7122089f1e58bd55`、任务快照 `cache-sql-counter-baseline-20261017` 为边界，集中完成 SQL 总计数、采集选择、汇总、复用校验、回归与操作说明。现有 `executions` 把 outcome/failure_reason 纳入系列身份，首次失败会新增没有前置值的系列；新增 `fullnet.data.sql.completed_operations` 仅按语句、Provider、操作递增，成功/失败/取消共用真实基线。它是完成的操作尝试总量，包含准入阶段失败，不等于数据库端实际 SQL 执行量。原分类指标完整保留，不改变业务、认证、连接和排队配置。

容量配方显式选择 `fullnet_data_sql_completed_operations_total` 后，两个节点均须有真实前后基线；新语句、重置、系列消失、错误标签和缺失节点仍拒绝验收。分类增量缺基线继续为 null，仅在独立总量完整时降为可选缺口；新总量缺失禁止退回旧计数。旧配方默认维持原解析，新源码/API 不与旧样本合并为同一容量曲线。具体字段与边界见[驱动说明](../../eng/load/cache-capacity/README.md#sql-总量基线)。

独立轻量副本使用正式 normal 锁运行 `node artifacts/cache-hardening/cache-sql-counter-baseline-20261017/run-node.mjs red`，真实退出 1，22 通过/3 失败；关联实现完成后运行同入口 `green`，真实退出 0，25/25、零失败跳过。四个运行辅助脚本的 `node --check` 退出 0。`pnpm test:integration:affected:plan -- --snapshot cache-sql-counter-baseline-20261017 --phase inner` 实际退出 0，受共享 Dapper 影响，选择双库 Smoke；它只是规划，没有执行 Integration。现有三个 `DapperTelemetryTests` 已补成功后首次失败/取消保持同系列、双 Provider 标签断言，但尚未编译执行。

实现输入与测试日志的[冻结摘要](../../artifacts/cache-hardening/cache-sql-counter-baseline-20261017/implementation-proof.json)为 `538d9312037cff86c6ac0117ad595d2b05209a385e47de31d20e8f37e8a1f709`。当前只证明 Node 采集器回归通过；新增生产指标仍待 Release Unit/相关消费者、API/Worker AOT 分析、api-native-aot Architecture、影响集双库 Smoke 和匹配 Native 发布/运行。底座集中验收仍优先，缓存没有建立重型队列；恢复资源后在新冻结候选上集中执行，不用 C2 旧 ELF 冒充新实现，也不重复未变图的无关验收。

C3 集中验收输入现已准备，身份 `8d9d7d7e984403faad5c7c1ecef9e1503085b77f770bb1570121c5489b2669d9`：对 C2 的 8,045 个条目逐一核对，只覆盖 `DapperTelemetry.cs` 和对应单测两个文件，没有混入其他任务改动。容器内批次脚本集中安排 Unit、API/Worker 分析、api-native-aot Architecture、双库 Smoke、新 API 发布及双库核心 Native；Architecture 与 Smoke 选择取自冻结矩阵，Unit 与核心 Native 按本次影响显式聚焦；发现 UID 和正式 TRX 必须完全匹配。源码/辅助输入核验和 Node/Bash 语法检查实际退出 0，37 份辅助输入封存。执行前发现的脚本末尾 Windows 换行已另存 LF 版本，`execution-inputs.json` 明确选择 `pipeline-lf.sh`，原封存保持可追溯；这是执行前修正，不是构建或测试失败。

C3 [准备证明](../../artifacts/cache-hardening/sql-counter-20261017-c3/preparation-proof.json)摘要 `49cd676bbf03e03005811438f116df16026ccbfbbda7a35f100ef16271351a64`，只表示 `prepared-unarmed`。本机 Windows 未安装候选要求的 10.0.402 SDK，后续沿用已验证的 Linux 镜像、2 CPU/12 GiB 发布容器和编译器默认 GC，没有切换 SDK 或安装环境。底座明确要求继续保留完整内存窗口，缓存没有启动构建、.NET 测试、Docker、压力或后台排队，也未修改自有构建卷。后续以 `artifacts/foundation-freeze-20261016-resource-final.json` 的完整自然终态及实际资源释放为前置；宿主入口已经准备但未实际执行完整生命周期，收到并核验该回执后才能取得正式锁、测量内存并执行容器脚本。上述准备检查不能替代尚未执行的 .NET/Native 门禁。

宿主执行入口已补齐真实进程句柄监督、默认只检查模式、回执摘要与关联 state/run/terminal 核验、正式 normal/heavy 互斥、内存保护和自然清理确认。前序业务失败可以交资源，但缺少实际退出、资源仍占用、来源不匹配或旧回执都拒绝；Docker 状态不明或仍有新残留容器时保留锁，不删除其他任务容器。独立 normal 下 `node --max-old-space-size=128 --test gates.test.mjs` 实际退出 0，4/4、零失败跳过；这是资源门禁回归，不是生产 .NET 单测。实际 `run.mjs --check` 因缺最终回执退出 2，伪造摘要的 `--execute` 请求退出 1，均未创建功能 run 状态或启动 Docker。Node/PowerShell 语法检查退出 0，证明见[宿主准备记录](../../artifacts/cache-hardening/sql-counter-20261017-c3-host/preparation-proof.json)，摘要 `9c8137d50943d89eb062010c1a445b2705828aac7538f5da7a9eca32d58e707b`。

本批 Native 编译启动要求宿主物理/提交余量至少 4/12 GiB、WSL available 至少 14 GiB，运行保护线仍为 2/1/2 GiB；启动余量结合此前编译器约 9.58 GiB 峰值为整个编译批次保留预算，不改变原容量负载的保护门槛。当前没有自动接续程序，未绑定已退出的旧底座父进程；该任务仍在原窗口内修复，旧进程退出本身不视为整窗释放。

同轮仅使用已有 r7 日志/容器采样进一步归因：8 份日志共 477,954,163 字节，2,349 条容量异常按实例和 RequestId 在内存去重为 783 请求，每请求在三类来源出现；正式新段依次为 SQL Server open-500 的 106、MySQL closed-128 的 426、closed-64-repeat 的 1、open-200 的 250，逐段匹配 k6 全部服务端错误，全部关联 `identity.find_refresh_session_by_explicit_session_id`。写入和暂停恢复段无此异常；未导出去重身份或原始日志。只读[归因与资源证据](../../artifacts/cache-hardening/cache-sql-counter-baseline-20261017/failure-resource-analysis.json)摘要为 `881f934115ab39fd41d2985c07fd6ad9e7e3d2c29f58d6ca2dac06bd959b022d`。

采样中相关段的单节点准入在用峰值均为 37，SQL Server open-500 排队峰值 46、MySQL closed-128 峰值 64；两节点相加不是同时在途峰值。MySQL open-200 的离散排队 Gauge 没有采到等待，不能据此排除短等待。驱动与双 API 共用 4 CPU cgroup，MySQL closed-128 的 Docker CPU 采样均值约 413.87%、最大 430.90%，open-200 对应约 91.42%、130.00%；短时出口读数可超过 CPU 配额，且恢复场景也曾出现高读数。这些数据支持继续区分准入、查询、共享宿主与发生器开销，不能把某个平均 CPU 数字认定为唯一根因。没有据此放宽许可、队列、超时或权威会话安全语义。

进一步重算 r7 已有成功系列的前后直方图，仅使用真实同系列基线，不补失败系列的缺失值：

| 新正式段 | 成功权威会话操作均值 ms | 全部成功连接获取均值 ms | 全部连接持有均值 ms |
| --- | ---: | ---: | ---: |
| SQL Server open-500 | 51.42 | 13.60 | 37.75 |
| MySQL closed-128 | 72.07 | 49.57 | 22.33 |
| MySQL closed-64-repeat | 72.24 | 36.02 | 36.05 |
| MySQL open-200 | 105.10 | 57.67 | 47.35 |
| MySQL mixed-writes | 14.44 | 6.64 | 7.70 |
| MySQL redis-delay | 16.05 | 7.60 | 8.33 |

权威操作计时覆盖准入、Open、SQL/读取和连接释放，不含随后 Dapper 日志输出；连接获取包含准入等待与 Provider Open，连接指标还包含其他 SQL 和控制操作。因此表中均值不能精确相减为请求分段，也不能用于解释失败请求或 P99。MySQL 五个新正式窗口的 `Connection_errors_max_connections`、`Innodb_row_lock_waits` 与 `Innodb_row_lock_time` 增量均为零，SQL Server open-500 的已采集 LCK 等待任务与时间增量也为零；它们仅排除这些已记录计数的增长，不证明所有数据库等待均不存在。SQL IO 快照缺稳定文件 ID，未强算逐文件增量。下一次实测优先区分准入、连接 Open/持有及共享宿主调度；当前不据此修改池大小、排队或 SQL。上述[耗时与数据库证据](../../artifacts/cache-hardening/sql-counter-20261017-c3-host/timing-analysis.json)摘要为 `12363520756813020382764223d6b4df948cf11706fb408421a3727e41c37877`。

同时核对原 k6 `state.testRunDurationMs`：MySQL open-200 配置 60 秒，实际只运行约 41.973 秒，错误率和 P99 失败阈值设置了 abortOnFail，发生器实际退出 99；8,360 请求的原始实测速率约 199.17 请求/秒，不能把请求数除以配置 60 秒得到所谓稳定服务能力，也不能算作完整 60 秒通过。其余三个新压力档约 60 秒，恢复段约 45 秒。该[真实负载窗口证据](../../artifacts/cache-hardening/sql-counter-20261017-c3-host/load-window-analysis.json)摘要为 `681da93c7bd849b5b2b6664b862d1b63333f8056d064faf613aac96bb5f19ba1`，不修改任何原始分类、缺失 SQL 总量或失败终态。

## r7 结果与验收边界（2026-10-17）

本轮新增 6 段正式采集、234,398 请求，另有 9 段有效结果按同一 C2 API、场景、镜像、缓存策略和连接预算复用。下表只列新采集正式段；4 段预热不计容量样本，合计请求数也不是同一轮连续流量或生产 SLO。全部正式段的鉴权与载荷错误均为零。

| 上下文 | 新采集段 | 请求 | 服务端错误 | 调度丢弃 | P99 ms | 原始分类 |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| SqlServer-S2 | open-500 | 29,751 | 106 | 249 | 427.25 | Incomplete |
| MySql-S2 | closed-128 | 81,080 | 426 | 不适用 | 240.79 | Incomplete |
| MySql-S2 | closed-64-repeat | 44,493 | 1 | 不适用 | 347.23 | within-local-budget |
| MySql-S2 | open-200 | 8,360 | 250 | 22 | 1013.43 | Incomplete |
| MySql-S2 | mixed-writes | 36,855 | 0 | 不适用 | 48.49 | within-local-budget |
| MySql-S2 | redis-delay | 33,859 | 0 | 不适用 | 163.05 | within-local-budget |

SQL Server 500 RPS 出现 249 次丢弃；MySQL 200 RPS 出现约 2.99% 服务端错误、22 次丢弃及 P99 约 1,013 ms，均未满足本地预算。MySQL 128 并发虽未触及 HTTP 错误率/延迟阈值，必需 SQL 计数仍不完整，也不计通过。MySQL 64 并发重复段的 1 次服务端错误低于既定 1% 预算，保留原数值；没有把“满足预算”改称零错误。开放负载未建立通过档位，不承诺固定服务 QPS。MySQL 500 RPS 和稳定开环重复段按首档失败停止规则没有执行。

双库四个写入/Redis 暂停恢复段均满足本地预算、服务端/鉴权/载荷错误均为零；SQL Server 两段来源为 r6 并保留原证据，MySQL 两段为本轮新采集。受控写入传播观测最大值：SQL Server 约 409.65 ms、MySQL 约 225.65 ms。Redis 命令暂停不等于网络断连或消息丢失实验。MySQL 已测累计连接峰值最高 78，实验总预算 110、服务端上限 151；不外推未测场景。

本地硬件为 i7-12700H（14 核/20 逻辑处理器）、约 63.75 GiB Windows 共享宿主、Docker/WSL；两个 Native API、单个受保护 Grid 热键，Worker=0。SDK 10.0.402 / Runtime 10.0.13，API ELF 仍为 `5cec0c9da8ad32f96509471b91f957b1e2c9e43d82644a89d2347b5f581bf550`，源码 C2 `7e2f20419a78e1082a1f25e4101098c26cfd5a3cfcf0607b8a1f5d06009327c5`。沿用原 4 CPU/8 GiB 驱动与 API 容器、每 API 1 GiB GC 堆及有界发生器配置；不代表独立物理节点。新实跑段的宿主虚拟余量采样最小值约 5.18 GiB，未记录资源保护触发；这是离散采样，不能证明连续内存最低值。

采集完整性和容量通过分开记录：22 项保护测试实际退出 0，source-initial / reused-consumer / discovery / lifecycle / source-final / native-log-copy 均退出 0，真实双库生命周期 UID 各 1 个、2 Passed、零失败跳过。闭环新诊断分支实际保留 MySQL 128 并发的 Incomplete 并继续重复/开环/恢复，最终父进程按必需证据缺失故意失败。后置 `node …/verify-diagnostics.mjs <output> <terminal> <resource-receipt>` 实际退出 0，只证明全部采集与原始证据重算一致；未调用正常成功汇总入口产生验收通过证明。

- [诊断证明](../../artifacts/cache-hardening/fullnet-cache-capacity-auth-20261017-native-r7/diagnostic-proof.json)，SHA-256 `3638315182c5844972eaae7f771d703394a45b33566489cbeb337239aa328a97`。
- [独立完成核对](../../artifacts/cache-hardening/fullnet-cache-capacity-auth-20261017-native-r7/completion-audit.json)，SHA-256 `bd9f6ab7c026feb2ee421555785e71d3dfdfda8377719d153fa32c5f1b2aa0a2`。
- [最终纯资源回执](../../artifacts/cache-hardening/cache-capacity-auth-recheck-20261017-native-r7/resource-handoff-final.json)，SHA-256 `d21249d869a20e7deddf975aaddd7f11b036d8ae8efdbbc4b4e0196cd04a524c`；已通知 AI、底座与日志，仅交资源，不复用业务结果。

## 过程记录


04:03 更新：并行任务通报 MessagePack 2.5.302 的 Moderate 公告 `GHSA-qhrr-8q5h-9q3h`，当前主工作区中央版本已实际核对固定为 2.5.303。缓存 C1 冻结图尚未包含该调整，因此在尚未启动任何重型子进程、仍等待外部窗口时停止自有队列，不修改中央依赖或旧源码。旧 Unit/Architecture/AOT 分析证据保留其原图范围；重新入队前须形成新依赖图并独立审计，不能借用其他任务结果作为缓存功能验收。缓存尚未独立核验该公告或执行新图审计。

05:06 更新：正式交接回执摘要 `4092b35ea3ea17ea03d660ca1b9a88f4c6fffc8d198934334ee089c0c4cfaac3` 及其状态、run.json 摘要已独立核对，正式锁实际缺席后，C2 取得本任务 normal/heavy 锁开始集中验收。当前提交为 `69e282e6233256f64087c82328bf71bd67850f2e`；C2 只将 C1 加上当前中央 MessagePack 303 固定项，身份为 `7e2f20419a78e1082a1f25e4101098c26cfd5a3cfcf0607b8a1f5d06009327c5`，仍不能外推为整个当前 main 验收。

已独立核对[官方公告](https://github.com/advisories/GHSA-qhrr-8q5h-9q3h)所列 2.5.303 修复版本，并从官方 NuGet 下载 MessagePack 与 Annotations 两个包，包签名验证实际退出 0。C2 三个普通消费者恢复、API/Worker 两个 linux-x64 发布图恢复均退出 0；随后从官方 NuGet 重新读取公告数据并对五个包图独立审计，前置审计实际退出 0、零 NuGet 包发现，包含显式 RID 编译器覆盖核对，不包含框架运行时漏洞评估。原始证据位于 `artifacts/cache-hardening/auth-capacity-20261016-c2/` 的 `official-packages.json`、`package-signatures.log`、`early-package-audit.json` 与对应原始 CLI JSON。

C2 截至 05:31 已完成 API/Worker AOT 分析、普通引用和消费者构建，退出均为 0。Unit 74/74、Architecture 235/235、Integration 7/7（本批鉴权 2、Realtime 3、通知 2）实际全部通过，零失败、零跳过；发现 UID 与 TRX 已在容器及宿主分别严格核对。真实双库鉴权回归覆盖过载 503、恢复、来源故障及撤销拒绝。目前正在真实链接新 API，终态包审计及 Native 20（原计划核心/OIDC 18 加 JSON SignalR 双库 2）尚未取得结果，不能登记整批成功。父进程 136388、Docker CLI 子进程 138220，使用缓存自有原 C1 卷；不访问其他任务卷。后续监督进程 138224 已持有真实父进程句柄，仅在实际退出 0、完整 UID/TRX、清理、发布物和新图审计均独立核对成功后生成新压力配方，再重新申请正式资源锁；当前 `pressureStarted=false`。

05:16 资源协调：功能批后先交给另一任务一个集中验收窗口，再开展缓存压力。接续脚本会先生成本任务 `resource-handoff-functional.json`，核对真实父终态、完整功能证明、锁实际缺席、自有卷空闲和唯一正式 run.json，随后不持锁等待另一任务新的 `resource-handoff-intent-restore-final.json`。只接受时间不早于本批交接且状态/run 摘要匹配的整窗回执，不使用原有旧回执抢占空档。当前宿主余量允许另一任务在独立冻结目录和独立 normal 下运行 2 处理器、合计约 1–2 GiB 的 Node/Vue 检查；重型与 Docker 不并发，内存回落须先停止该轻量检查。

05:44 失败终态：C2 的 API 发布实际退出 1，ILCompiler 在 `StringTableBuilder.CreateIndex` / ELF 对象文件输出阶段抛出 `System.OutOfMemoryException`。父进程 136388 经 exec 会话 65592 实际退出 1，监督真实进程句柄也记录退出 1，Docker CLI 子进程 138220 退出 1。终态包审计及 Native 20 尚未执行，压力配方未生成、压力未启动。此前 Unit 74、Architecture 235、Integration 7 及前置包审计的实际成功保持其分项范围，不改写整批失败；源码最终校验及本次日志归档均退出 0。

05:47 已按约定交出资源窗口，独立确认父/子进程消失、自有容器为零、自有卷无占用、normal/heavy 锁实际缺席，且精确匹配本批正式 `failed` run.json。不可变 `resource-handoff-functional.json` 的摘要为 `a977a61650fb6b5a1a55d07f6d0cef8801577d65f099e5db2a4e463a829e95e7`，`acceptance=false`，只表示资源释放，原失败 state 保留。另一任务已接收并开始其集中窗口，缓存当前不启动编译、Native 或压力。

只读诊断发现发布资源配置偏差：此前已验收 SDK402 发布批 `fullnet-cache-sdk402-reviewed-ff31c37d/runtime-state.json` 记录容器为 12 GiB，且 `runtime.sh` 在发布前清除运行期 GC 堆限制、完成 API/Worker 发布；C2 容器被设为 8 GiB。C2 镜像不预设 GC 堆限制，脚本也在发布前清除了测试堆限制。优先恢复原发布预算并补录编译器 RSS、GC 环境及 cgroup 峰值/OOM 事件，再只重试新 API 发布、终态包审计及未执行 Native 20，复用同一冻结图下已通过的普通结果。本轮缺失 cgroup 峰值实测，暂不能将容器上限断言为唯一根因；不调整业务配置、测试阈值或告警允许项。

06:13 恢复批 r1 已排队，目录为 `artifacts/cache-hardening/auth-capacity-20261016-c2-native-r1/`，父进程 151248、监督进程 150828；当前 `waiting-ai-intent-restore-full-window`、`pressureStarted=false`，不占正式锁、不启动 Docker。源码及包图仍为 C2 身份 `7e2f...327c5`，普通结果已重新读取 UID/TRX 严格复核为 74/235/7，保留原父退出 1。收到新的 `resource-handoff-intent-restore-final.json` 并验证时序、真实终态和状态/run 摘要后才申请正式 normal/heavy。

r1 恢复已验收的 12 GiB / 2 CPU 发布容器、默认编译器 GC，原生测试阶段沿用原 SDK402 的 2 GiB 堆边界。新增发布期 cgroup `memory.max/current/peak/events`、编译器 RSS/高水位及宿主物理/虚拟/WSL 余量采样；只对失败目标的精确 `.o` 路径核对后另名保留，不清空依赖输出。新审计前后要求与 C2 原五个 assets 图摘要完全匹配，消费者也必须保持不变。仅重试新 API 发布、包审计及 Native 20；不重复已通过普通测试，不复用旧 ELF 或旧压力样本。新增流程已做脚本语法检查及只读审查，尚无恢复批执行结果。

06:40–06:46 等待队列恢复：r1 父 151248 的进程句柄记录退出 0，但生产状态仍为等待、未产生 childPid、容器日志、源码执行检查、ELF 或 Native 结果；不能将该退出码作为功能成功。原聚合门禁已阻止生成压力配方，原因未明，原始状态、终态及 `queue-interruption.json` 保留，未将其记为 Native 测试失败。r2 仅复制原恢复输入，改为由独立后台 PowerShell 直接创建并持有 Node 真实句柄，并额外要求匹配 PID、完整生产完成状态、acceptance 和全部证明后才接续压力。新父 166000、监督 80028，目录 `auth-capacity-20261016-c2-native-r2`；当前仍等待同一个新的 AI 整窗回执，不持锁、不启动 Docker，队列截止约 08:15:53。普通 74/235/7 已再次独立复核通过，未重新执行测试；恢复预算、源码和包图保持不变。

07:42 恢复批开始执行：新的整窗资源回执摘要为 `2bfb76fa690cac1ac201059a9afbf9f9bc34abc0544d0551a258b319ccb4a983`，实际父退出 0、清理与资源释放为 true；回执关联的状态及正式 run 摘要已独立核验。r2 取得正式锁，短暂等待宿主提交余量恢复到原启动门槛后，已启动自有 12 GiB / 2 CPU 容器，Docker CLI 子进程 134240，状态 `running-native-retry`。另一任务只剩轻量文档/Git 收尾，不占重型窗口；其功能结果不计入缓存验收。r2 仍使用同一 C2 冻结源码及包图，普通结果保持复用，发布和 Native 20 尚无通过终态，压力未启动。

08:03 发布恢复结果：r2 的 API 发布命令实际退出 0；新 ELF 长度 141,847,520 字节，魔数为 `7f454c46`，宿主独立计算的 SHA-256 与 Linux 记录一致，为 `5cec0c9da8ad32f96509471b91f957b1e2c9e43d82644a89d2347b5f581bf550`，区别于旧基线产物。发布后五图包审计实际退出 0、零 NuGet 包发现，全部 assets 未变化，图匹配退出 0。发布期采样记录容器峰值 10,289,238,016 字节（约 9.58 GiB），采样前后 cgroup OOM/OOM-kill 均为零，编译器 GC 环境覆盖未设置。新批实测峰值超过首次 8 GiB 预算，支持恢复 12 GiB；首次失败缺少同类采样，仍不把该差异断言为唯一原因。Native 20 已开始运行，尚未取得完整终态，不升级整批或容量状态。

08:27 功能恢复完成并接续压力：Native 20/20 全部通过，零失败、零跳过，发现 UID 与 TRX 精确一致。恢复批真实 Docker CLI 子进程与父 166000 均退出 0，父终态由监督 80028 的真实句柄确认；源码终检、消费者未变化及日志归档均退出 0，自有容器清理、卷空闲及正式锁释放已通过。监督运行聚合验证并生成 `functional-proof.json`，保留原 C2 父退出 1，同时汇总同源普通 74/235/7 和新 Native 20。本轮宿主只读复核重新验证了四组 UID/TRX、前后五图包审计、发布告警允许清单及新 ELF 摘要，未重写压力引用的 proof。随后启动压力父 182284，自有新配方和新输出目录均带 `native-r2` 后缀，使用新 ELF 与同一 C2 源码，不复用旧压力样本；已通过启动资源门槛并启动 SDK/采集器，正式样本尚待结果。

08:38 压力中止：运行保护在宿主空闲虚拟内存降至 0.8217 GiB 时触发，低于原 1 GiB 停止线；同期空闲物理内存 3.2511 GiB、Docker Linux 可用内存 15.2737 GiB，不能用 guest 余量代替宿主提交空间。压力真实父 182284 退出 1，发生器退出 99；源码终检、日志归档均退出 0，自有容器与卷已清理。独立资源回执 `auth-capacity-20261016-c2-native-r2/resource-handoff-final.json` 的 SHA-256 为 `0de94a64160853910ea2732c608af3102006306ea29e583e27f3918fb13600f6`，只证明资源释放；原压力 state 的 `acceptance=false`、`resourceReleaseConfirmed=false` 保留。

本段 500 RPS 部分样本共 16,671 次请求，服务端错误 519、业务错误率约 3.113%、调度丢弃 113，401/403 与坏载荷均为零。因此不能仅归为环境中止，也不能宣称 500 RPS 达标。服务端 after 指标存在累计 532 次 503，但 before 中没有该系列，不补零计算增量，也不与发生器 519 次强行等同。SQL Server/MySQL 两个生命周期的清理中止不表示两个提供程序均发现业务故障；本批尚无 MySQL 压力结果。

09:41 独立边界核对命令 `node artifacts/cache-hardening/verify-auth-pressure-r2-boundary-20261016.mjs` 实际退出 0，输出 `fullnet-cache-capacity-auth-20261016-native-r2/pressure-boundary-proof.json`。它重读真实父/发生器终态、原 summary 摘要、保护阈值与回执，并用捕获的原解析器重新核对以下 4 个有效样本。复用分项不改变失败父批结论，预热与中止段均排除。

| SQL Server 完整样本 | 请求数 | 成功 RPS | P99 ms | 401/403 | 结果 |
| --- | ---: | ---: | ---: | ---: | --- |
| C0 16 VU 第一次 | 66,810 | 1112.74 | 58.70 | 0 | 满足本地预算 |
| C0 16 VU 第二次 | 51,185 | 850.30 | 92.19 | 0 | 满足本地预算 |
| S2 64 VU | 98,291 | 1636.97 | 111.08 | 0 | 满足本地预算 |
| S2 64 VU 重复 | 103,613 | 1724.62 | 103.80 | 0 | 满足本地预算 |

09:40 用户释放内存后实测宿主空闲物理 11.22 GiB、虚拟 10.58 GiB。下一压力批准备复用同一 C2、新 ELF、消费者校验和及上述 4 个有效样本，不重跑已通过功能与 Native；实际启动仍须等待另一任务新的整窗资源回执并重新测量内存、取得正式锁，原保护阈值不变。

09:43 压力 r3 已由独立监督 191600、资源等待进程 192180 排队，配方位于 `cache-capacity-auth-recheck-20261016-native-r3/recipe.json`。已核对冻结输入、当前驱动摘要、消费者来源及 4 个原始有效样本；Node 语法检查、只读 `wait-resource.mjs --check` 与 PowerShell 解析均退出 0。等待唯一新回执 `artifacts/ai-incremental-auto-recovery-20261016/resource-handoff-final.json`，要求时序、状态/终态/正式 run 摘要及资源释放成立；等待期间不持正式锁、不启动 Docker。资源归还后重新要求宿主虚拟余量至少 8 GiB，并通过原启动保护线再申请正式测试资源；90 分钟未满足则保留未启动终态。当前 `pressureStarted=false`，尚无 r3 压力结果。

10:58 资源交接完成：已独立核对新整窗回执 SHA-256 `19dd8f0c3b9f3f54b9fb62fa647f49842f9ce08062f7ab46f068708b8baadc8d`，其绑定状态、真实父终态、正式 run 及 PID 复用观察摘要一致，normal/heavy 锁缺席，对方卷只读检查空闲。该回执的父退出 1、`acceptance=false`、`nativePending=true` 保留，仅采用资源释放事实，不采用对方功能证据；未触碰复用 PID 的外部进程。缓存等待进程已转为 `waiting-memory-headroom`，压力仍未启动：10:58 宿主空闲物理 7.85 GiB、虚拟 5.57 GiB，随后虚拟回升至约 6.68 GiB，尚未达到 8 GiB 重试余量。约 11:13 总排队截止前继续实际测量，不降低阈值。

11:01 r3 资源复测达到空闲虚拟 8.409 GiB，原启动门槛也通过，压力父 219484 已由监督 191600 创建并持有真实句柄，启动自有驱动；不再处于未启动等待。新 state 明确绑定失败 r2 父退出 1、`previousAcceptance=false` 和原始有效样本来源，不将复用提升为原父成功。独立资源观察进程仅在整个 r3 实际结束、全部本轮工作进程退出、正式锁和自有卷确认释放后，才向 `cache-capacity-auth-recheck-20261016-native-r3/resource-handoff-final.json` 写不可变资源回执；提前写入保护检查已实际拒绝，资源交接不要求压力验收成功。r3 当前结果仍待取得。

11:05 r3 终态失败：压力父 219484 真实退出 1，清理完成；不可变资源回执摘要为 `283499a0068159845005fa48b6415bc90fe2072927433e5eaf3fe412785a6e95`，已交给下一整窗，不继续占锁。源码终检、日志归档及消费者复用检查均退出 0。500 RPS 发生器真实退出 99，共 29,898 请求、56 次服务端错误（业务错误率约 0.1873%）、103 次调度丢弃，P99 303.23ms，401/403 及坏载荷为零。调度丢弃已超预算；总 SQL 增量又缺少前置系列，因此分类为 `Incomplete`，并中止余下恢复及 MySQL，不能登记本段或双库通过。

根因核对：两个 API 的 `identity.find_refresh_session_by_explicit_session_id` 在测段内首次导出 `outcome=failure`、`failure_reason=application_error` 系列，after 分别为 24/32，before 中均不存在。严格解析器不能将缺失系列补零。原解析器、summary、真实退出及资源回执已独立重读核对，证据为 `fullnet-cache-capacity-auth-20261016-native-r3/pressure-boundary-proof.json`；原 r3 失败不改写。

采集器新增默认关闭的 `continueIncompleteBoundaries` 诊断开关：仅放行“真实开环退出 99、明确超预算、唯一必需缺口为总 SQL 新系列缺基线、Grid SQL 及正确性完整”的独立后续采集，立即停止该组升压。该样本仍为 `Incomplete`、SQL 总增量仍为 null、不可复用；其他场景即使完成，正式 run 和父进程仍退出失败，`acceptance=false`。认证或载荷错误、计数重置/消失、其他必需缺口、资源停止线保持原中止规则。没有修改生产代码、ELF、连接预算或阈值。

采用任务快照 `cache-capacity-diagnostic-20261016`，基线为 `9f27f8ce4bc1e2a9c8808ea1bd14a73b57ee73eb`。独立轻量源码副本、独立 normal 中，新回归先实际退出 1（2 项失败），修复后 `node --test tests/performance/cache-capacity.test.mjs` 实际退出 0、21/21 通过，零跳过；驱动语法检查退出 0。以真实 r3 原始输入再解析仍为 `Incomplete`，仅显式诊断判断允许继续采集，默认拒绝。日志位于 `cache-capacity-diagnostic-unit-20261016/red.log`、`green.log`；新模式尚未进行真实压力验收，等待另一任务新整窗归还后补剩余矩阵，不重做 Native。

影响集规划命令 `pnpm test:integration:affected:plan -- --snapshot cache-capacity-diagnostic-20261016 --phase inner` 实际退出 0，识别本批 6 个变更文件，Integration 目标为 none；本批没有生产 .NET 变更，不为此重新构建或发布 API。源码、Markdown、UTF-8/空白和 `git diff --check` 已作相关检查，真实压力仍待补齐。

11:23 r4 已排队：独立监督 166508、等待进程 39072，配方为 `cache-capacity-auth-recheck-20261016-native-r4/recipe.json`。只等待新整窗路径 `artifacts/ai-incremental-auto-recovery-native-resume-20261016/resource-handoff-final.json`，不复用已消费的 19dd8f0 回执。当前 `waiting-peer-full-window`、`pressureStarted=false`，不持正式锁、不启 Docker。r4 显式开启诊断模式，仍使用 C2、新 ELF、自有原卷、已验收消费者和 4 个有效样本；相对 r2 仅运行器与解析器两份运行辅助文件改变，已与 21 项测试所用副本逐一核对摘要一致。新配方及全部输入已封存；Node/PowerShell 语法和只读来源检查通过，提前资源交接检查实际退出 1、回执仍缺席，符合拒绝预期。观察进程 190284 仅在本轮真实结束并实查资源释放后写唯一新回执 `cache-capacity-auth-recheck-20261016-native-r4/resource-handoff-final.json`。尚无 r4 真实压力结果。

11:35 利用既有证据补做只读请求链分析，未新增实验。捕获的原解析器逐条核对 4 个有效样本：C0 两次的 Grid SQL 分别为 66,810/51,185，会话权威 SQL 也分别为 66,810/51,185，总 SQL 约每请求 2 次；S2 两次请求为 98,291/103,613，Grid SQL 仅 4/3 次，会话权威 SQL 仍为 98,291/103,613，总 SQL 约每请求 1 次。由此确认本场景缓存确实消除了绝大多数 Grid 回源，但未消除每请求的权威会话查询，不能将压力结果单独归因于缓存命中率。

同窗口 GC 分配计数除以发生器请求数，C0 为约 106,087–106,191 字节/请求，S2 为约 113,017–113,028 字节/请求。这是两个 API 观测窗口的比值，包含认证、控制等工作，不等于单次缓存操作分配；不能据此认定缓存序列化是主要瓶颈，也不能因 Grid SQL 降低就直接声称吞吐提升。

对本轮归档的 4 个日志流（合计 69,795,988 字节）逐行只读核对，找到 56 条会话权威查询的 `ServiceCapacityExceededException`，均处于 r3 500 RPS 测段内，集中在北京时间 11:03:51.295–11:03:51.973。它支持“短时容量拒绝仍保持 503 而非无效凭据”的契约解释，不能解释为整分钟持续饱和或确定某一个 CPU/数据库锁是唯一根因。派生证据 `fullnet-cache-capacity-auth-20261016-native-r3/pressure-analysis.json` 绑定状态及逐文件摘要；原父退出 1、`Incomplete` 与未执行 MySQL 均不变。11:34 实测宿主空闲物理 10.89 GiB、虚拟 9.18 GiB，r4 仍等待另一任务整窗，不启动新负载。

11:45 r4 增加独立、只读的后置校验进程 222020，不修改封存的驱动或启动额外负载。它只等待本轮最终资源回执：若压力真实退出 0，调用原成功汇总门禁；若整矩阵采集结束但因诊断缺口退出 1，则逐项核对源码、新 ELF、真实父/子终态、双库生命周期 UID、各原始 summary/指标、复用来源、预算和完整场景集合，另外生成 `diagnostic-proof.json` / `diagnostic-report.md`，明确 `acceptance=false`、`diagnosticOnly=true`。完整采集不得替代压力验收；仍有缺口的 SQL 总数保持 null，原正式 run 和父退出 1 不改写。

后置脚本 `cache-capacity-auth-recheck-20261016-native-r4/verify-diagnostics.mjs`、`postflight.mjs` 已通过 Node 语法检查并封存摘要。使用真实未完成 r3 矩阵的负面检查实际退出 1，原因明确为“未完成所有上下文”，未生成诊断完整凭证，记录在 `postflight-guard-check.json`。完整矩阵的正向校验仍待 r4 实测，不将语法或负面检查当作通过。11:40 宿主虚拟余量 7.62 GiB，r4 仍为 `waiting-peer-full-window`；另一任务尚在 Worker Native 编译阶段，缓存不占其整窗。

11:51 新资源窗口已归还：回执 `artifacts/ai-incremental-auto-recovery-native-resume-20261016/resource-handoff-final.json` 的 SHA-256 为 `c9051e99981ae752e91f3c08ae19afbc297acdbb4967463a22ea3454bd3939ad`，已独立核对状态、终态、正式 run 摘要及实际父退出 0，仅采用资源释放事实。r4 预检已进入 `waiting-memory-headroom`，压力仍未启动；此时宿主空闲物理 6.68 GiB、虚拟 4.70 GiB，随后虚拟余量降至约 3.45 GiB，低于原 5 GiB 启动线及本轮额外 8 GiB 重试缓冲。已请求用户释放暂不用的应用或任务，队列持续实测；不停止未知归属进程、不修改阈值。尚无新的压力样本，MySQL 与恢复矩阵仍待执行。

11:55 用户明确回复“先保留进度，稍后压测”，因此停止自动等待、接续和后置校验。停止前逐一核对 39072/166508/190284/222020 的完整命令与所属目录并持有真实进程句柄；等待进程按请求终止为 -1，监督自然退出 1，两份轻量观察/后校验也已停止。`pause-request.json` 记录真实观察，原 `chain-state.json`、`preflight-terminal.json` 和最后等待状态保留；不存在压力输出目录、压力父进程或本轮正式测试，不把用户取消登记为压力业务失败。

实际确认全部本轮进程退出、自有卷无占用、容器为零及 normal/heavy 锁缺席后，生成约定唯一 r4 回执 `cache-capacity-auth-recheck-20261016-native-r4/resource-handoff-final.json`，SHA-256 `57f29201e0f509adcaa977a8328002d0d95fa73bf81e28febc5f5e251410fe92`。该暂停专用回执绑定真实监督终态、前置取消终态及原始观察，明确 `userRequestedPause=true`、`automaticResumeEnabled=false`、`pressureStarted=false`、`noFormalRunStarted=true`、`acceptance=false`，不伪造正式 run。已向另一任务交还资源意向；源码、C2、新 ELF、构建卷、21 项保护测试及原有样本/日志保留。后续用户明确恢复时须创建新接续目录、重新协调资源和实测内存，不能覆盖 r4 暂停记录或自动因内存回升而启动。

12:53 用户明确回复“继续”，恢复本任务。12:51 实测宿主空闲物理 17.26 GiB、虚拟 14.97 GiB；r4 两份封存清单逐项核对均无变化，新 ELF 摘要仍为 `5cec0c9d…`，无需重复功能测试、消费者构建或 Native。当前 main 为 `eb4c2d216a2072af3206f7025e01b60ce8dc78ee`，本批实验仍只覆盖 C2 冻结源码，不扩大为当前 main 验收。

已新建 `cache-capacity-auth-recheck-20261016-native-r5/`，输出目录独立，显式沿用受控诊断模式、原消费者与 4 个有效样本；r4 暂停清单及全部终态不改写。新监督 191388、等待进程 228872、资源观察 237672、后置校验 222616 已启动，仅等待另一任务确认的本轮唯一 `artifacts/ai-metadata-events-20261016/attempt2/resource-handoff-final.json`，不使用首次元数据失败回执或已消费的旧窗口。当前 `waiting-peer-full-window`、`pressureStarted=false`，不持正式锁、不启 Docker。全部复制脚本语法、来源复核及提前交接拒绝检查已完成；资源归还后重新核对内存门槛再由正式入口接续，本轮唯一最终资源回执将写 `cache-capacity-auth-recheck-20261016-native-r5/resource-handoff-final.json`。尚无 r5 压力结果。

13:38 资源协调确认：前序整窗仍在双库阶段，后续还包含 AOT 分析及 Native，很可能超过 r5 约 14:23 的外层等待截止。继续保留其整窗；若队列到期，保留真实未启动终态，不登记为压力业务失败或通过，也不覆盖原记录、反复建立竞争队列。对方完成后将发送约定新回执及摘要，收到资源通知再核对当时状态并恢复必要接续，保留已有有效结果。当前 `pressureStarted=false`，尚无新压力样本；该协调不采用对方功能验收证据。

13:49 前序任务明确因验收重试变更最终边界：旧 `attempt2/resource-handoff-final.json` 不会生成，新唯一回执改为 `artifacts/ai-metadata-events-20261016/attempt3/resource-handoff-final.json`，尚未释放其全局窗口。为防止旧队列等待失效路径，本任务先确认未有压力输出，再核对四个进程归属并持真实句柄结束旧等待：228872 实际 -1，监督 191388 自然退出 1，237672/222616 也已退出。原配方、封存清单、等待状态、真实终态全部保留，不在原目录改写新边界或伪造本轮正式 run。

`resource-boundary-update.json` 明确记录 `taskContinues=true`、`queueSuperseded=true`、`pressureStarted=false`、`noFormalRunStarted=true` 及新回执路径。独立 `resource-boundary-proof.json` 摘要为 `6619b74fa6d9cfb37cdc04334626657f14bfb40a30fbdf4461792d48f3ca3d33`，核对全部本轮后台进程退出、压力输出缺席、两份封存清单未变化。r5 从未取得该正式资源窗口，此记录不声明全局锁释放，也不将资源协调撤销计为压力业务失败；前序任务继续持有整窗。用户恢复授权仍有效，收到 attempt3 最终资源通知后再核对实际状态、内存并建立新批，继续复用已有有效结果。当前没有缓存等待/接续程序继续运行，不会误用旧回执放行。

14:51 前序任务通知再次变更最终资源边界为 `artifacts/ai-metadata-events-20261016/attempt4/resource-handoff-final.json`，attempt3 仅保留重试前置回执。新增不可变 `resource-boundary-update-attempt4.json`，摘要 `47369cc30ddec8e8c106ea80b7a02231c861ffc70ce132d9c228bc99448e16ef`，关联前一次边界记录；当前无缓存后台队列或压力，任务继续等最终通知。

同轮通过 `Get-CimInstance Win32_OperatingSystem` 独立核对宿主启动时间为北京时间 `2026-10-16T14:38:41.500+08:00`。缓存 r5 的撤销及真实句柄终态已于重启前记录，继续保留原证据；不能把其他未留终态进程的消失补成退出 0 或认作资源清理完成。前序任务通报正在恢复 Docker 并复核遗留资源；缓存不并发启动 Docker、不删除共享锁，不声明已释放其窗口。后续必须收到 attempt4 最终回执，核对资源归属、真实终态及启动门槛后再建立接续批；尚无新增缓存压力结果。

17:54 资源复核：前序边界经历 attempt5 接续后，AI 的唯一最终回执已落在 `artifacts/ai-metadata-events-20261016/attempt6/resource-handoff-final.json`，SHA-256 为 `98241644446e34e863f1c6e9787c571fe5057c9a67563bb9e5e0cb66ce1b4123`。独立核对绑定 state/terminal 摘要、真实父 29492 退出 0，以及正式 run 的时间与 `passed` 终态；只接收资源交接，不采用对方业务验收结果。全部 r5 封存输入逐项核对仍一致，缓存自有卷无容器占用，owner label 正确。

同轮实测共享 heavy 锁 PID 50868 的命令行为日志任务 `artifacts/logging-correlation-20261016/acceptance1/run.mjs`，宿主空闲物理 6.52 GiB、虚拟 2.66 GiB，未达到缓存重试的 8 GiB 余量。底座任务的浏览器短批资源回执摘要 `8aa55f28dce4033a0d4d31a4fcb28e384297fbda4cf1f1299e3effb99d61f14b` 已核对，但其双库入口已先申请共享锁，故不把短批清理或 AI 回执当作当前全机空闲。已协调先完成底座排队批；下一资源通知固定为 `G:/wwwroot/github_fork/Full.NET-printing-20261008-recovery/.tmp/foundation-freeze-20261016/resource-handoff-after-integration-attempt1.json`，收到后须核对真实执行/未执行终态、清理、实际锁和内存，再启动缓存新批。当前主分支为 `42d6714ee48ed58447fe608b7122089f1e58bd55`，后续缓存实验仍限定原 C2 冻结输入；没有新压力结果，也没有缓存自动接续进程运行。

20:19 r6 正式接续：底座在宿主 18:58 重启后结束了中断窗口，其纯资源回执摘要为 `407ce893fe295676b68e9f21bcfe6c64c2e37ab5c2c41c3991afaa6bfc87438a`；日志 `acceptance5/resource-handoff.json` 摘要为 `77f775aabcd1fbdd607168f83dcd27b645957e3f11977dfe09019aad4b196604`，状态 `released`、自有进程/容器/锁均释放。日志父退出码未提供，保持未知，不将资源释放补为测试成功。独立实查非复用 PID 消失、日志 Testcontainers session 无容器、缓存自有卷空闲及 owner 正确，normal/heavy 锁缺席；保留他方停止容器和 kind 节点。CIM 本轮宿主启动时间为 `2026-10-16T18:58:22.373558+08:00`，不替代对方保存的 wmic 时间。

新目录 `cache-capacity-auth-recheck-20261016-native-r6/` 封存原 r5 全部未变输入及本轮资源实查记录 `predecessor-resource-verified.json`，摘要 `724a6087ba5e555ea55ddd860600cf4cecdf5d957cbfd79bbd04211bcef8f8fd`。只读来源检查重算全部冻结文件、新 ELF 和 4 个原始有效样本通过，脚本语法检查通过。监督 51720、等待进程 15276、压力父 44080、资源观察 22992、只读后置核验 12280；前置实际测得虚拟余量 17,391,247,360 字节、物理 20,337,881,088 字节、客体 MemAvailable 18,288,607,232 字节。正式 heavy 锁已核对由压力父持有，运行前 21 项保护测试退出 0。此批不重建消费者或发布 Native，原失败父退出 1 与 `previousAcceptance=false` 原样绑定；仍需完成 SQL Server 恢复、MySQL 聚焦矩阵及受控诊断模式真实验证。唯一最终资源回执为 `cache-capacity-auth-recheck-20261016-native-r6/resource-handoff-final.json`，仅在真实整窗结束和资源实查后生成，当前尚不存在。

20:33 r6 终态：实际压力父 44080 退出 1、监督 51720 已持句柄记录，正式 run 为 `failed`，原 state/终态/原始指标不改写。逐样本使用封存解析器和真实 k6 退出记录重算 15 个完整样本（4 个预热、11 个正式）一致；正式样本中 4 个为复用、7 个为本轮新采集，新采集正式请求为 340,286。9 个有效正式样本已由 `reusableSamples` 独立核对可供下一批复用，保留本批父退出 1 和 `previousAcceptance=false`。

| 新采集正式样本 | 请求 | 服务端错误 | P99 ms | 原始判定 |
| --- | ---: | ---: | ---: | --- |
| SQL Server S2 / 500 RPS | 9,773 | 172 | 359.67 | Incomplete，另有 83 次丢弃 |
| SQL Server S2 / 混合写入 | 53,034 | 0 | 56.69 | within-local-budget |
| SQL Server S2 / Redis 暂停恢复 | 54,807 | 0 | 55.11 | within-local-budget |
| MySQL C0 / 16 并发对照 1 | 41,680 | 0 | 62.57 | within-local-budget |
| MySQL C0 / 16 并发对照 2 | 50,343 | 0 | 47.10 | within-local-budget |
| MySQL S2 / 64 并发 | 53,151 | 0 | 244.71 | within-local-budget |
| MySQL S2 / 128 并发 | 77,498 | 355 | 231.43 | Incomplete |

上述正式样本鉴权和载荷错误均为零。MySQL 已测各段的服务端连接峰值最高 75，低于实验总预算 110，服务端上限为 151；这只覆盖已完成段，不宣称后续开环、故障恢复或整个 MySQL 容量已通过。SQL Server 混合写入段的控制观测传播时间最大约 409.65 ms；MySQL 64 并发 Grid 回源为 4 次，对照组为每请求一次。它们不证明跨宿主容量或生产 SLO。

两个不完整段的 SQL 总量仍为 null。MySQL 128 并发实际 k6 退出 0、错误率约 0.458%，未触及 1% 错误预算；但新增会话 SQL 失败系列没有前置基线，仍不可验收。初版诊断模式要求开环且实际退出 99，因此停止了本轮，后置全矩阵核验也真实退出 1，未生成诊断完成证明。已补充受控闭环分支：仅在显式开关、完整请求/Grid/正确性证据、唯一 SQL 总量缺基线，且已观察服务端错误或性能预算越界时停止该组升压后继续其他场景；原 `Incomplete`、不可复用、整批失败和资源保护不变。新回归在独立副本先实际失败 1 项，修改后 22/22 通过，`test:integration:affected:plan -- --snapshot cache-capacity-diagnostic-closed-20261016 --phase inner` 退出 0、Integration 无影响。只修改 `metrics.mjs`、保护用例和说明，不修改生产 .NET 或 Native 产物；新边界尚待下一压力批真实验证。

本批不可变 [边界证明](../../artifacts/cache-hardening/fullnet-cache-capacity-auth-20261016-native-r6/pressure-boundary-proof.json) 摘要为 `95d27536e747e63a7588d4159d2c041907fea6d4a1cb293b6399edb6bcbaa1f3`，绑定全部原始重算、父失败、生命周期 1/1、9 个可复用样本及新旧诊断决策差异。最终 [纯资源回执](../../artifacts/cache-hardening/cache-capacity-auth-recheck-20261016-native-r6/resource-handoff-final.json) 摘要为 `3099bdc9b60cd7d5e2636e8ae7e8eb7266997674976f04a7f9534849862439ae`：自有进程、容器、卷占用和锁已释放，但日志 acceptance6 已接续取得 heavy PID 64432，因此明确 `ownedLocksAbsent=true`、`locksAbsent=false`，不宣称全机空闲。底座、AI 及日志均已收到资源协调通知；缓存目前没有压力或自动接续进程。下一批应复用 9 个有效样本，重测不可复用的不完整段并完成 MySQL 重复、开环和写入/Redis 恢复，重新取得完整双库生命周期及诊断后置证明；不重发 Native 或重做已验收功能。

2026-10-17 03:06 接续准备：宿主虚拟余量已恢复至约 15.66 GiB，但共享 heavy 文件仍记录旧父 55012；实际 PID、直接子进程及其句柄观察 50524 均缺席，运行容器只余三个保留的 kind 节点。原底座恢复 manifest 仍为 running，最终回执未生成，因此没有将进程消失补为退出或清理成功，也未删除他方锁。已通知所属窗口核对其原始终态/中断、自有资源和后续排队边界。

`cache-capacity-auth-recheck-20261017-native-r7/` 已准备新配方及独立监督、资源观察、后置核验脚本，尚无后台进程，输出目录不存在。新配方复用 r6 的 9 个有效样本及原 C2 ELF/消费者；只读核对全部 8,045 个冻结项、ELF 摘要、消费者三个构建输入、原失败父退出 1 和 9 段原始指标均成立，当前诊断代码/测试与 22/22 通过副本摘要一致，未重复运行测试。脚本语法检查通过；资源观察已区分本任务释放与随后其他任务持锁，避免把新的外部持锁者误认成本任务清理失败。详见 [r7 准备核验](../../artifacts/cache-hardening/cache-capacity-auth-recheck-20261017-native-r7/preparation-proof.json)。最终资源前置回执和运行输入封存尚未生成；收到新的资源证明、核对锁释放及启动余量后才可启动本批。

2026-10-17 03:40 r7 入队：原底座中断窗口的恢复回执摘要 `fb97206875bb2f2a24cc207967349f827a95df7078376824ce5687981dbc3d3c` 已核对，未知父退出码保持 null。随后 Docker Desktop 退出、Linux 引擎管道缺席；在没有相关重型进程的条件下按用户此前允许启动 Docker 的授权恢复 Desktop，未修改代理、DNS、内存或其他全局配置。恢复后引擎可访问，仅三个保留 kind 节点运行；日志 acceptance7 最终资源回执摘要 `68470dd751c5f1e1e99a4635be3bc8223537eef3969ddf2e6348905dd8f2b150` 已核对，相关旧进程、容器、私有卷活动挂载和捕获的 Testcontainers session 均为空。

本轮独立实查记录 [资源前置证明](../../artifacts/cache-hardening/cache-capacity-auth-recheck-20261017-native-r7/predecessor-resource-verified.json) 摘要 `0f415bec562a5f22c397efb0e046a7603cce8cbff9c799d65d6554f6c0aa4feb`，绑定底座、日志、AI 三份资源回执，不采用其业务结果或补全未知退出码。封存时新日志 acceptance8 已取得 heavy PID 81612，证明明确保留该外来锁；前置等待程序要求实际锁缺席、原 C2 输入及 9 段样本再次核对、宿主虚拟余量至少 8 GiB 并满足原主客体内存保护线后才启动压力。03:40 独立监督 42176、前置等待 25048、资源观察 54716、后置核验 85376 已建立，等待上限 90 分钟，等待阶段不持正式锁、不启动容器；来源检查实际退出 0。最终唯一纯资源回执为 `cache-capacity-auth-recheck-20261017-native-r7/resource-handoff-final.json`，当前尚不存在。已通知日志请求当前自然窗口结束后交接缓存这一有界余项批；不终止或删除他方进程、锁或容器。

r6 原始错误归因补充（10 月 17 日，只读分析）：对已复制的 8 份 Native 日志共 513,478,763 字节完成流式扫描并保存逐文件 SHA-256，未重新产生负载。含 `ServiceCapacityExceededException` 的 1,581 条记录，按实例与请求标识在内存中去重后恰为 527 个请求；每个请求均在 JWT Handler、DapperSqlExecutor、FullNetExceptionHandler 三个来源各记录一次，不能把 1,581 条日志当成 1,581 次失败。Dapper 记录的语句全部为 `identity.find_refresh_session_by_explicit_session_id`，分别精确匹配 SQL Server 500 RPS 段 172 次和 MySQL 128 并发段 355 次 k6 服务端错误。

SQL Server 异常时间范围为 `12:23:08.853Z–12:23:09.918Z`，约 1.065 秒；MySQL 为 `12:32:00.901Z–12:32:56.221Z`，约 55.32 秒。异常类型、语句和下述准入指标共同支持将这些已观测请求归入权威会话查询的容量异常路径，但不证明 CPU、SQL 锁、数据库 IO 或日志开销中哪项是唯一瓶颈，也不据此放宽权威读取、缓存安全或准入预算。原 SQL 总量 null、父失败、未测 MySQL 场景均保持不变。[失败归因证明](../../artifacts/cache-hardening/fullnet-cache-capacity-auth-20261016-native-r6/failure-attribution.json) 摘要为 `bb800d292b5cdb5c7718e229d248e95a8adec57dde07b060af2c5657c538fa20`，只导出汇总，不导出原始请求、Trace 或认证数据。r7 在分析期间仍由既有程序等待重型窗口，未新增压测样本。

同批稀疏准入指标进一步核对：SQL Server 后置累计快照为 rejected=11、timeout=161；MySQL 为 rejected=316、timeout=39，分别与 172/355 次观测错误数值相符。两段对应的 rejected/timeout 前置系列均缺席，故这些数值仅是后置累计值，不能当作本段增量；SQL 总量和准入增量继续为 null。调用栈成员名称匹配没有建立具体抛出成员的证据，未用于归因或验收。上述路径判断依据类型、稳定语句和低基数指标，不将未知前置值补零，也不据此给出逐请求拒绝/超时拆分。[准入指标证明](../../artifacts/cache-hardening/fullnet-cache-capacity-auth-20261016-native-r6/failure-admission-metrics.json) 摘要为 `10609bdc87e73c6a961225ecbb5affd0c211b58d4a36be70fb25b12252095897`，绑定原始 before/after 文件与日志归因证明；原失败与 r7 待验收范围保持不变。

2026-10-17 03:50 r7 自动接续：新日志窗口释放后，前置进程 25048 实测宿主空闲虚拟 13,401,329,664 字节、物理 17,900,122,112 字节、客体 MemAvailable 17,986,519,040 字节，输入与 9 个原始有效样本仍一致；监督 42176 创建压力父 97120 并持真实句柄，正式 heavy 锁确由该压力父持有。22 项运行前保护测试实际退出 0，Native 驱动已启动，不再处于未启动等待。已向日志、底座、AI 通知本轮实际资源占用和固定最终回执路径；不重建消费者或重发 Native，旧失败及未知计数继续保留，结果待取得。

## 实现与范围

普通 JWT 与 OIDC 会话校验统一保留 `ServiceCapacityExceededException`，交由既有异常管线转换为 HTTP 503、`DatabaseCapacityExhausted` ProblemDetails 和至少一秒的 `Retry-After`。移除两个校验器仅用于条件重抛的可选授权读取作用域参数，不新增错误码、SQL、迁移或缓存会话旁路。普通来源异常继续拒绝认证，请求取消继续传播，OIDC 临时租户上下文仍由 `finally` 恢复。

新增 SQL Server/MySQL 真实 JWT 集成场景，分别使用普通访问令牌和真实授权码流程取得的 OIDC 令牌，只在权威会话查询注入故障。场景覆盖 rejected/timeout 返回 503、普通来源故障仍为 401、同一令牌恢复后可用、正式 SQL 持久化撤销后仍为 401，以及错误响应不泄露内部异常和连接信息。C2 已实际通过新增双库 2 项，并与 Realtime 3 项、通知 2 项组成 Integration 7/7；新 API 的 Native 20 已通过。

C1 候选源码身份为 `98a7d8861cc3781d251a5179c393b5b85c9b379700311344cfb919d97e9c9f15`，由已验收父基线 `868b60c20bd10df07069ce4378868c4a62318447982a3816d0da2d0c6be7474c` 叠加本批六个文件形成，共 8,045 个清单项。C2 在 C1 上追加中央 MessagePack 2.5.303 固定项，身份为 `7e2f20419a78e1082a1f25e4101098c26cfd5a3cfcf0607b8a1f5d06009327c5`，r2 继续使用这一冻结输入。任务开始时 main 为 `6ee1771e29590d93c01371564e952c23771d4055`，07:07 核对为 `69e282e6233256f64087c82328bf71bd67850f2e`；工作区另有并行改动，不能将独立候选的验证扩大为整个当前 main 验收。Identity、Identity.Contracts、Hosting、Abstractions 和 Data.Abstractions 相关生产目录相对父基线仅有本批两个校验器差异。

## 已取得与待取得证据

环境为 Windows 共享宿主、Docker Desktop Linux、固定 SDK 10.0.402 / Runtime 10.0.13。C1 独立普通验证使用自有源码目录、自有卷、2 CPU / 4 GiB，不启动数据库、Native 程序或压力发生器；C2 集中批包含真实双库，发布使用 8 GiB 时发生 OOM，r2 将恢复原已验收的 2 CPU / 12 GiB 发布预算。下表汇总当前证据，首次失败按原始范围保留。

| 项目 | 当前事实 |
| --- | --- |
| Identity Release 与 Unit 消费者构建 | 实际退出 0，零警告、零错误 |
| 聚焦 Unit | 74/74 通过，零失败、零跳过；发现 UID 与 TRX 精确一致；真实子进程退出 0 |
| Integration 消费者构建 | 实际退出 0，零警告、零错误；发现新增双库 2 项 |
| API / Worker AOT 分析 | 分别实际退出 0，零警告、零错误；不等于新 Native 发布或运行已通过 |
| 首次架构运行 | 230/235 通过，5 失败，真实子进程退出 2，原始证据保留 |
| 普通模式架构恢复批 | 普通引用重建退出 0；235/235 通过，零失败、零跳过，真实子进程退出 0 |
| 双库真实 JWT 与相关 Integration | C2 实际 7/7 通过，包括新增鉴权双库 2 项；本轮只读复核源码身份、结果摘要及完整发现 UID/TRX，一致 |
| C2 包图与新 API Native 验证 | 首次 API 发布实际退出 1 保留；r2 新发布实际退出 0，新 ELF、发布告警、前后五图包审计均核对通过；Native 20/20 通过，真实父/子退出 0，清理及源码终检通过 |
| 修复后的压力复测 | r2/r3 父均退出 1；4 个完整 SQL Server 样本满足本地预算。r2 因宿主内存保护中止；r3 的 500 RPS 有 56 次服务端错误、103 次丢弃及 SQL 前置系列缺口，仍为 `Incomplete`。MySQL 未执行，保持 `Capacity-not-verified` |

首次架构失败由验证步骤将 AOT 分析输出作为普通架构测试引用引起，涉及 Dapper 依赖边界和模块选择测试。恢复方式为重建普通模式完整引用后重新运行架构测试；不修改业务或测试断言来掩盖失败。首次 `compile-state.json` 和 TRX 不改写。

Unit 复核命令为 `node artifacts/cache-hardening/verify-auth-capacity-unit-20261016.mjs`，实际退出 0。其核对源码清单、构建退出码、真实子进程退出、清理状态、七组测试发现与 74 个 TRX UID。证据位于 `artifacts/cache-hardening/auth-capacity-20261016-c1/` 的 `unit-proof.json`、`unit-state.json` 与 `unit-results/unit.trx`。

架构恢复后使用 `node artifacts/cache-hardening/verify-auth-capacity-compile-20261016.mjs` 独立核对 235 项 UID、源码前后校验及 API/Worker 分析结果，实际退出 0，输出 `compile-proof.json`。恢复批真实子进程 121600、父进程 122196 退出 0，独立普通锁释放、自有容器自然退出；首次失败不被覆盖。

## 复测请求链与指标边界

按 C2 冻结源码核对，受保护的 Grid GET 先经 `FullNetJwtBearerEvents` 调用 `AccessSessionValidator` 权威读取会话，再进入 `MyGridPreferenceService`。S2 命中缓存只省去 Grid 偏好回源，不会取消前面的会话查询；因此不能把全部 SQL 次数、数据库准入等待或端到端 RPS 直接解释为缓存自身表现。普通 JWT 路径未进入 `IDatabaseAuthorizationReadScope`，使用普通准入许可；预算中的独立授权预留 1 不能解释为对每次 JWT 查询另行保留了许可。本批保持这一预算和撤销语义。

后续同时核对 Grid 专属 Statement 的增量及每请求回源数、全部 SQL 与数据库准入指标、HTTP 错误和延迟。S2 热命中路径仍会为载荷上限检查执行 JSON 序列化，应结合分配/GC 实测解读，静态调用不能证明它是主要瓶颈。两节点活动请求 Gauge 的异步采样合计不等于全局同时在途峰值，连接累计峰值也不等于当前活动查询数。

发生器将 401/403 或坏载荷视为正确性失败，503 继续计入服务端错误及业务错误率；样本业务错误率达到 1%、P95 达到 500ms、P99 达到 1000ms、出现开环调度丢弃或阈值不通过时，不计为满足本地预算。必要指标缺失、重置或新系列缺少前置基线时保持 `Incomplete`，不补零。修复将过载从 401 区分为 503，不表示提高了吞吐，也不能将新的超预算样本登记为通过。

## 最终状态与后续边界

1. C2 功能修复、包图、新 Native 产物及相关功能验收已完成；Node 诊断保护和真实闭环接续也已验证。C1/C2 首次失败、旧父退出 1 和原 ELF 来源均保留，不把冻结候选结果外推为当前 main 全量验收。
2. 本次聚焦容量矩阵已按预定停止规则采集完毕，恢复段和双库生命周期证据完整；3 个正式样本的 SQL 总量缺口仍为 null、不可验收或复用。正常容量验收仍未通过，不能用后置核验退出 0 覆盖正式 run failed。
3. SQL 独立总量及采集器已实现、Node 25/25，生产指标仍待 .NET/AOT/Native 与双库采集验证。新生产输入必须形成新冻结候选和匹配发布物；已确认的 12 个满足预算样本与失败边界保留其 C2 原范围，不跨图复用为新候选结果。只重跑新指标实际影响的验证与压力档位，不盲目重复无关测试；开放负载的唯一瓶颈仍待区分，不能直接关闭权威读取或扩大池/队列掩盖失败。
4. 生产连接预算、默认队列 0、撤销与 fail-closed 语义不变；本实验队列 64 / 250 ms 不作为生产配置建议。任何可重试 503 仍计入错误预算。
5. 缓存当前无运行中的压力、等待程序或自有容器。AI attempt2 因内存不足在重型阶段前退出；共享 Docker 按既有授权恢复后，底座已接续原冻结集中余项，缓存不在其窗口间隙另起压力。后续新验证须重新协调并核对源、资源与内存。
6. 多租户工作集、L1 容量斜率、混合业务/Worker、长时 Soak 与 2K/5K/10K 同时在途没有本批证据；能力继续为 Capacity-not-verified，不承诺生产固定 QPS 或 SLO。
