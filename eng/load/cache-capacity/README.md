# 缓存本地容量基线

该驱动复用已验收的 Linux Native API，通过两个真实 API 进程测量同一受保护 Grid 热键。使用独立 SQL Server/MySQL 数据库、Redis 和签名会话，C0 与 S2 仅改变该缓存条目的策略，不修改认证、租户或审计的安全语义。Worker 数量为零。

运行环境沿用 `NativeApiProcessHost` 的 Testing 配置、localhost HTTP 和测试账号/登录限流；实验显式提供 RSA 签名密钥。本场景不验证生产 HTTPS、远程管理入口或生产账号配置。

当前驱动使用受哈希保护的 `database-budget.json`：两个 API 各最多 40 个池连接、普通许可 37、健康预留 2、独立授权预留 1，合计 API 池上限 80；声明未运行 Worker 的 10 个池连接及迁移/运维预留 20，总预算 110，不超过声明的 MySQL 151。API 角色与准入显式启用。实验采用最多 64 个排队请求、250ms 准入等待；生产 Helm 的 API 队列默认为零，不能将实验排队配置视为生产验收结果。

上一轮默认每 API 池上限 100、准入未启用的样本继续作为历史参照，不可与预算化样本混用。复用同时核对预算文件摘要、上下文完整预算及实际传入连接串的池上限；新运行还要求两个节点导出 API 角色的准入指标。MySQL 快照记录真实 max_connections、累积 Max_used_connections、Connection_errors_max_connections 与当前空闲/活动连接信息，不将 Threads_connected 当作活动查询数。

两个实验实例统一显式设置全局限流为每节点每分钟 1,000,000 次，仍保持限流开启。该设置只存在于临时实验进程，不修改项目默认的 1200/分钟或任何部署配置；登录、会话、认证、租户和端点权限检查保留。默认限流下观察到的 429 必须另列为策略基线，不能把拒绝请求的 RPS 当作成功业务吞吐或缓存容量。每个样本分别记录成功响应吞吐、429、5xx 和传输失败。

## 当前源码与资源前置

- 配方的 workspace 与 manifest.source 必须指向当前 Main；SDK 将其只读绑定到 /src，正常编译完整 ProjectReference 闭包，所有 bin/obj 进入自有卷的 /work/capacity。仅显式链接容量驱动，不覆盖 NativeApiProcessHost 源码，不复制整仓。
- 运行前须有同源 Native API、正式发布及当前依赖审计证据；本入口不关闭 NuGet 审计。任何复用必须核对真实来源、消费者产物与实际退出，不能依赖空缺的引用输出。
- 启动内存样本来自当前 Docker 引擎内 32 MiB/0.1 CPU 的只读探针，正式采样来自自有 SDK 的 /proc/meminfo；不读取或启动同名 WSL 注册实例。未确认的 daemon create 即使列表为空也保留租约，不记录归还；记录写失败也不得展开释放。
- SDK、Collector、k6、数据库和 Redis 均在创建时设置 memory-swap=memory。负载前核对数据库/Redis 的真实镜像、owner、CPU、内存和 swap；SQL Server 内部预算为 2048 MiB。禁用隐式 Ryuk，由当前容量 owner 生命周期和外层监督清理自有标签资源，外来容器不参与本批归还判定。
- 四份 Node 守卫（包含内存探针及异常资源保留回归）必须全部通过后才能启动负载；控制回归通过不代表真实容量通过。
- Collector 在本轮独立网络内使用短 DNS 别名 `otel`，OTLP 显式使用 gRPC；完整 UUID 所有权资源名不能作为超过 63 字符的单标签传输地址。
- 每个上下文的 `api-identities.json` 由两个受控 Native 宿主对象的实际 URL、日志路径和 `LoggingResourceAnnouncement` 生成，绑定应用、实例 UUID、角色、原公告及其 SHA-256。原始 Prometheus 文本保留实际 UUID；运行器与独立汇总仅按本上下文精确证据映射逻辑节点，未知、重复、交叉上下文或资源不符继续失败。
- 复用样本另外保存原始解析器、真实发生器终态、原生产进程身份文件及两份实际公告日志，逐项绑定 SHA-256；独立汇总使用旧样本的原始身份，不以本轮新进程 UUID 解释旧指标。缺失有效来源的历史样本拒绝复用。

## 场景与预算

- 每个 Provider、每种策略先预热 30 秒。C0 以 16 VU 测两次，每次 60 秒。
- S2 闭环依次为 1、16、64、128 VU，开环依次为 50、200、500、1000 RPS，每档 60 秒。每组首次超预算后停止升压，复测最高通过档位一次。
- 配方可显式选择 `selection: "connection-budget-recheck"`：SQL Server 为闭环 64 VU、开环 500 RPS，MySQL 为闭环 64/128 VU、开环 200/500 RPS，仍复测最高通过档位并执行原有写入/暂停场景。该选择只覆盖连接预算影响集，不代表重新执行完整低档位矩阵或 MySQL 1000 RPS。
- S2 另以不超过 16 VU 测持续写入失效和 `CLIENT PAUSE 5000 ALL`，各 45 秒。写入结果须在 10 秒内被另一实例观察到；计时包含 PUT 请求及轮询开销。
- 本地样本预算为业务错误率 `<1%`、P95 `<500ms`、P99 `<1000ms`，开环 dropped iterations 为零。401/403 或成功响应载荷损坏是正确性失败，直接停止批次。退出码 99 保留为预算边界；未知退出码、无请求或必需指标缺失是 `Incomplete`。
- 原生 API 每个进程显式限制 GC 堆为 1 GiB、可见 CPU 为 2；驱动容器上限 4 CPU/8 GiB，SQL Server 2 CPU/3 GiB、MySQL 2 CPU/2 GiB、Redis 1 CPU/512 MiB、Collector 1 CPU/512 MiB、k6 2 CPU/1 GiB。编译器使用默认 GC。启动要求 Windows 空闲物理/虚拟内存至少 4/5 GiB，Docker 引擎虚拟机 `MemAvailable` 至少 14 GiB；运行中相应余量低于 2/1/2 GiB 停止负载。引擎虚拟机的可回收页缓存属于已分配给虚拟机的内存，不能再次当作本批新增主机内存预算。

VU 和目标 RPS 是发生器配置。在途指标来自两个 API 的 `http_server_active_requests` 导出值之和；两个节点采样不同步，它不能证明同一时刻的全局峰值或其下界。发生器、应用和依赖共享主机，不能将本实验升级为平台 2K/5K/10K/Soak 验收；全部结果保持 `Capacity-not-verified`。

## 执行

在系统临时目录准备配方，再运行 `node eng/load/cache-capacity/run.mjs <recipe.json>`。来源清单绑定当前工作树或仍在使用的既有隔离目录及已验收发布物，不为本轮复制整仓、依赖或构建树。入口使用来源清单中的正式测试资源管理器，等待前序整窗终态，再申请工作区锁和同机重型锁。不得在同一机器上绕过锁另开压力负载。

`output` 必须是系统临时目录下尚不存在的直接子目录，名称以 `fullnet-cache-capacity-` 开头；拒绝仓库目录、共享临时根目录及现存结果目录。运行结束后，以监督进程记录的真实终态执行 `node eng/load/cache-capacity/summarize.mjs <output> <terminal.json> [boundary.json]`。独立汇总会重验原始样本与资源归还，将证明和报告写到标准输出，再删除带本轮所有权标记的结果目录；此前仅是待核验结果，不能提前清理。调用方应在本次交付说明或必要的 Verification 中记录结论，不依赖这些临时文件永久存在。

失败或资源归还未确认时不自动递归删除，定位完成后只保留必要原始诊断。历史无所有权标记的结果只读重验，不由新清理入口删除；既有构建卷不随结果目录删除，按实际使用状态单独回收。配方、监督终态等位于结果目录外的本轮临时文件由调用方在汇总完成后清理。

配方字段：

| 字段 | 内容 |
| --- | --- |
| `workspace`, `output`, `owner` | 当前仓库绝对路径、系统临时目录下尚不存在的 `fullnet-cache-capacity-*` 直接子目录、唯一 `fullnet-cache-capacity-*` 资源名称 |
| `manifest`, `source`, `sourceHashes` | 现有源码清单、清单 entries 的 SHA-256、Linux `sha256sum` 文件；记录 Git 基线及差异，不新建整仓副本 |
| `apiExecutable`, `apiSha256` | 已验收 Native API ELF 路径及 SHA-256 |
| `runSettings` | 单线程 MSTest settings 文件 |
| `volume`, `volumeOwner` | 本任务私有的可复用 Linux 构建卷，以及对应所有权标签的实际值 |
| `volumeOwnerLabel` | 可选，默认 `fullnet.cache-sdk402.owner`；独立认证修复候选可选 `fullnet.cache-auth.owner`，其他标签拒绝。所选标签的实际值必须等于 `volumeOwner`，且卷必须空闲 |
| `images`, `sdkImageId` | `sdk`、`k6`、`collector` 镜像引用及预期 SDK 镜像 ID；执行前将全部解析为不可变 ID |
| `resourceGate` | 可选：前序整窗 state 文件，必须同时确认 cleanup 和 resource release |
| `queueMinutes` | 排队期限，默认 90 分钟；不占用取得锁后的执行预算 |
| 引擎内存 | 通过本批精确所有权容器读取 `/proc/meminfo`；准入使用有界只读探针，运行时读取实际 SDK 容器，不修改 WSL 配置或清理缓存 |
| `consumerReuse` | 可选：同源、同卷且已成功构建的完整消费者 `state`、`buildExit` 和 `checksums`；旧 `referenceReuse` 模式明确拒绝 |

`driver.targets` 只在显式 `CustomAfterMicrosoftCommonTargets` 参数中将生命周期类链接到冻结的 Integration 消费者。它不加入常规测试项目发现集合，不重新发布 API。构建最多一次，输出独立消费者哈希、实际发现集合及生命周期 TRX。生命周期用例通过仅表示容器/API/控制流程完成，负载预算由每个样本独立判定。

首次运行在本任务私有卷中正常构建全部 ProjectReference，不要求历史引用产物。若驱动和引用输入都未变化，可显式使用 `consumerReuse` 绑定前一真实成功构建的 state、buildExit 和 checksums，验证现有消费者哈希后跳过构建。该路径只登记 `reused-consumer.exit`，不伪造本批 `driver-build.exit`。

容量消费者直接编译当前 Main 的 `NativeApiProcessHost.cs`，只显式链接容量驱动；不存在源码覆盖层。隔离卷仅以浅链接暴露只读 Main 的仓库标记、项目和构建配置，运行期 Migrator 通过相同 ArtifactsPath 复用已构建的产物，测试日志写入本轮独立结果。Native ELF 通过显式发布目录使用。每个真实测段开始前重新走默认登录流程，记录 JWT 到期时间并检查覆盖测段，不延长默认 10 分钟有效期。登录、验证和预热位于测量基线之前，Bearer 只写短期控制文件。

`reuseSamples` 可绑定尚在必要诊断期的前批目录，`reuseParentTerminal` 绑定其真实父终态。只有同一 API、源码清单、镜像、策略、额度和负载脚本下，已完成且没有认证/载荷/资源故障的样本可复用；重读原始指标并验证发生器终态。已清理的成功批次不能仅凭报告伪造样本复用。预热必须实际重跑，失效样本补跑，失败父批保持失败。报告注明多批聚合及每条来源，不宣称同轮全通过。

遇到有效凭据在压力下出现 401/403 时仍立即停止父批，不把故障改为通过。明确归因后，后批可通过 `providerCeilings` 按 Provider、closed/open 选择原有档位的较低上限，完成安全范围内其他场景；汇总必须同时传入独立核验的失败边界凭证。未执行的更高档位和原失败样本必须保留，不能把收窄后的生命周期通过当作原矩阵全通过。

预热后须观察两个节点各自真实的 Grid SQL 计数系列。尚未回源的节点由受保护的只读请求等待自然 TTL，最长为 L2 TTL 加 15 秒，记录实际读取和原始指标；不写入偏好值、不改变测段载荷，也不把缺失系列补成零。复用曲线后实际经过时间缩短，同样必须满足此门禁。

控制命令在同一目录完整写入临时文件后原子替换；跨 Windows/容器共享文件的占用错误最多异步重试 20 次、每次 50ms，其他错误立即失败。驱动解析完命令就释放允许删除共享的文件流，不跨轮询延时或数据库控制操作持有句柄。所有命令串行发送并通过唯一 ID 匹配回复。

原始证据包括 k6 summary、真实子进程终态、OTLP 前后快照和采样、容器 CPU/内存/IO、主机内存、数据库 IO/连接/锁状态、Redis INFO、运行时缓存策略和源码前后校验。Bearer 仅放在短期控制文件和子进程环境中，不写入命令参数或摘要；清理阶段移除控制文件。不要发布原始应用日志或控制目录。

日志归档仅复制本次生命周期开始后新写入的 Native 日志，并记录文件清单及复制退出码；保留 Linux 构建卷内的历史日志用于原批追溯，避免每次重复复制整个历史目录。源码校验、测试或归档任一失败都不能报告本轮成功。

`metrics.mjs` 对计数器重置、系列消失、身份混用和缺失样本拒绝给出成功结论。某一可选资源指标缺失时只能按原始证据注明未知，不能补零。数据库与运行时指标包含安全校验、审计和控制请求，SQL 总数不能全部归因于 Grid。

### SQL 总量基线

当前采集器默认且只允许 `sqlCounterMetric: "fullnet_data_sql_completed_operations_total"`。对应 .NET Instrument 为 `fullnet.data.sql.completed_operations`，只按 `statement_name`、`provider`、`operation` 分组，成功、失败和取消均递增同一系列；计数表示完成的操作尝试，包含在数据库准入阶段失败的尝试，不等于服务端实际执行 SQL 的次数。采样必须观察两个 API 各自真实的前置和后置系列，新语句、节点缺失、计数重置或系列消失仍是 `Incomplete`。

原 `fullnet.data.sql.executions`、`failures` 与 `duration` 保留结果和失败原因标签。首次失败新增分类系列时，该分类增量继续记为 null 和可选证据缺口；只有独立总量本身完整，才不再因此丢失总量。新总量缺失时禁止退回旧分类总量，也不补零。旧分类计数器仅用于可选诊断，不能选择为当前总量。

历史配方与报告仅使用该批冻结解析器重放；当前入口不保留旧总量选择或不完整样本继续采集开关。旧 API 没有新计数器，不能只改配方宣称已修复；新源码/API 须完成相应 Unit、AOT 与真实采集验证。复用同时要求 SQL 计数契约相同，旧发布物的成功或失败证据保持原范围，不与新 API 合并为同一容量曲线。

### 连接与命令阶段诊断

`fullnet.db.connection.phase.duration`（秒）在 DbSession 的准入、Provider `OpenAsync` 和自有连接释放边界记录 `admission/open/hold`。标签限定为 `provider`、`host_role`、`phase`、`access_mode`；访问模式为 `normal/critical/authorization`，表达调用方实际选择的路径，不承诺使用了某个物理连接池。准入拒绝不会产生 Open 样本，打开失败不会产生 Hold 样本，事务借用不会重复计量；阶段包含已经进入的失败/取消尝试，不按结果拆分直方图系列。

`fullnet.data.sql.command.duration`（毫秒）从取得连接后计时，覆盖命令准备、执行、结果读取/物化、QueryMultiple 投影及命令释放，早于连接归还结束。标签只有稳定语句名、Provider 和操作；没有取得连接的尝试不产生命令样本。原 `fullnet.data.sql.duration` 仍为包含准入和连接生命周期的总耗时，新指标也不是数据库服务端纯执行或 CPU 时间。

采集结果的 `connectionPhaseTimings` 与 `sqlCommandTimings` 分别按节点/访问模式/阶段和节点/语句/操作输出区间增量计数、总耗时和均值。Prometheus 出口分别读取 `_seconds_count/_seconds_sum` 和 `_milliseconds_count/_milliseconds_sum`。缺失、首次出现、重置、重复系列或不一致计数标记 `unavailable/incomplete`，没有完成尝试时均值为 null；不补零。诊断字段的 complete 只覆盖实际导出的系列，不证明全部业务阶段都出现；它们不替代正式 SQL 总量和正确性门禁。不同阶段完成时间不同且可能重叠，禁止相减拼成请求耗时，禁止用均值推导 P99。源码与采集器实现不等于匹配 Native 产物已导出，也不等于瓶颈已有实测结论。

该压测是一个受控热点场景。多租户工作集、序列化体积、L1 内存斜率、Worker 与混合业务、长时间 Soak 和更高在途规模，需要单独明确负载后测量。Redis 命令暂停不等于网络断连或丢消息。

参考：[k6 arrival executor](https://grafana.com/docs/k6/latest/using-k6/scenarios/executors/constant-arrival-rate/)、[dropped iterations](https://grafana.com/docs/k6/latest/using-k6/scenarios/concepts/dropped-iterations/)、[thresholds](https://grafana.com/docs/k6/latest/using-k6/thresholds/)、[Docker WSL 页缓存说明](https://docs.docker.com/desktop/features/wsl/)。
