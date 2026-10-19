# 缓存双实例本地容量基线

本批承接已完成的缓存 14/14 与 SDK 10.0.402 兼容性验收，新增容量实验，不重新打开原计划。**2026-10-16 01:34（Asia/Shanghai）完成本地热键基线收口，包含明确的失败边界；能力状态保持 Capacity-not-verified。** 25 个完整样本、1,237,087 次请求中，24 个满足本地预算，SQL Server 开环 1000 RPS 因调度丢弃超预算；另有两次 MySQL 中止失败档位单独留证，未计入这 25 个完整样本。本批不能称为原压力矩阵全通过。

**后续状态（2026-10-17）**：普通 JWT/OIDC 容量异常契约修复后，C2 新 API 与显式连接预算的 r7 已按停止规则完成聚焦诊断采集，15 个正式样本、807,312 请求（9 段复用），双库生命周期 2/2、后置核验退出 0。压力父仍真实退出 1，3 个 SQL 前置系列缺口和开放负载失败保留；MySQL 500 RPS 因 200 RPS 已超预算而未运行。详见[认证后聚焦容量报告](2026-10-16-session-authorization-capacity.md)。新旧两组的 API 图和连接预算不同，不合并样本或直接作吞吐 A/B；以下 A15 历史结果及原失败边界保持原范围，整体仍为 Capacity-not-verified。

## 范围与来源

- 开工 HEAD：`a75bd31a0e6675dfba60b8cb09e1f8191e166a19`，分支 `main`，任务快照 `cache-capacity-baseline-20261015`。主工作区存在其他任务改动，本批不能替代当前 main 全量验收。
- Native 基础源码清单指纹：`868b60c20bd10df07069ce4378868c4a62318447982a3816d0da2d0c6be7474c`，沿用已验收的 8,044 项冻结输入，运行前后分别核对。
- SDK 10.0.402 / Runtime 10.0.13，Ubuntu 24.04 x64；API ELF SHA-256：`48c657c68f3cb65b51d0161cdaf7ca4b9eba0e074896b7bd300b4e7de35510ce`。本批未重新发布或更改生产 API。
- 测试消费者显式链接 `eng/load/cache-capacity`，并覆盖 `NativeApiProcessHost.cs` 的大日志检查实现；消费者与运行辅助输入指纹 `bf88878a22949c220722a31c5140d22c3e0afd4954332be005036b5472694493`。此覆盖仅影响测试诊断，不属于上述冻结 Native 生产来源。
- 活动目录：`artifacts/cache-hardening/fullnet-cache-capacity-20261016-a15`；父进程监督目录：`artifacts/cache-hardening/cache-capacity-launch-20261016-a15`。原始日志及临时控制数据不作为可公开发布附件。

## 实验契约

场景为两个独立 Native API 进程、实验 Worker=0，经真实登录、会话、租户和授权链读取 `/api/v1/me/grid-preferences/identity.users` 的同一热键。沿用 `NativeApiProcessHost` 的 Testing 环境、localhost HTTP、测试账号和登录限流配置；实验显式提供同一 RSA 签名密钥。每个 Provider 使用独立 SQL Server/MySQL 数据库与 Redis；C0 权威读取和 S2 缓存读取使用同一 API 二进制。S2 的 L1=2 秒、L2=30 秒、抖动为零、Fail-Safe 关闭。

两实验节点显式设置全局限流为每节点每分钟 1,000,000 次，仍启用限流；仓库及部署默认的 1200/分钟、默认 JWT 10 分钟和安全校验均未修改。每个实跑测段在测量基线之前重新登录并检查凭据剩余期限，不延长令牌寿命。

每种策略先预热 4 VU/30 秒。C0 为 16 VU/60 秒两次；初始 S2 闭环 1/16/64/128 VU、开环 50/200/500/1000 RPS，各 60 秒，每组首个超预算档位后停止升压并复测最高通过档位一次。另以不超过 16 VU 进行 45 秒受控写入失效和 45 秒 Redis 命令暂停，后者执行一次 `CLIENT PAUSE 5000 ALL`。MySQL 在 128 VU 与 500 RPS 均触发数据库连接边界，A15 将后续明确收窄为闭环最高 64 VU、开环最高 200 RPS；不增加 MySQL max_connections，不改变应用连接池或安全策略。原失败档位仍未通过，MySQL 1000 RPS 未执行。

本地预算为业务错误率 <1%、P95 <500ms、P99 <1000ms，开环 dropped iterations=0。401/403 或成功载荷损坏属于正确性失败。实际退出 99 保留为预算边界；零请求、未知退出或缺少必需指标属于 Incomplete。生命周期通过仅证明驱动、控制与排空完成。

硬件为 i7-12700H（14 核/20 逻辑处理器）、约 64 GiB Windows 10 Pro 19045、Docker 29.6.2/WSL Linux 6.18.54.1；Docker 可见约 19.29 GiB 内存。驱动与两个 API 合计上限 4 CPU/8 GiB；每 API 可见 2 CPU、GC 堆上限 1 GiB；SQL Server 2 CPU/3 GiB（数据库内存上限 2048 MiB）、MySQL 2 CPU/2 GiB、Redis 1 CPU/512 MiB、Collector 1 CPU/512 MiB、k6 2 CPU/1 GiB。所有组件共享宿主，100% CPU 表示一个逻辑核。

启动要求宿主空闲物理/虚拟内存至少 4/5 GiB、WSL MemAvailable 至少 14 GiB；运行停止阈值分别为 2/1/2 GiB。使用正式工作区锁和同机重型锁，等待其他任务完成后执行，不清理其他任务容器或修改全局 WSL 配置。

## 执行、复用与异常

入口：`node eng/load/cache-capacity/run.mjs <recipe.json>`。配方、全部辅助输入哈希、不可变 SDK/k6/Collector 镜像、消费者编译产物和发现集合均封存在活动目录。C# 消费者变化时只重编消费者，复用同源引用项目输出；最终消费者在 A13 实际构建退出 0、零警告/错误，用时 1m01.57s。A14/A15 对其编译产物做真实哈希复用检查，未重复构建，也不生成本批构建通过的假记录。

SQL Server 的 14 个正式样本跨批复用：11 个读曲线来自 A8，500 RPS 复测、持续写入和 Redis 暂停来自 A12；MySQL 的两次 C0 对照及闭环 1/16/64 VU 来自 A13，闭环 64 VU 复测与开环 50/200 RPS 来自 A14。合计复用 22 个正式样本，其余安全范围样本与所有预热在 A15 实跑。复用按同一 Native API、基础源码、SDK/k6/Collector 镜像、策略、额度和发生器脚本核对，独立重读原始指标、冻结解析器及真实 k6 终态；转引来源链也核对状态和结果摘要。A8、A12、A13、A14 父进程实际均退出 1，失败状态保留，不生成合并 TRX 或宣称同一轮全通过。复用的原始在途说明存在“连续峰值下界”的错误文字，汇总时明确勘误，同时保留原始元数据和数值。

此前实际问题与处理：

1. Collector 重启后 Docker 动态端口变化，初始驱动访问旧端口；改为重启后重新读取当前映射。原失败批次不计通过。
2. 默认限流下 A7 预热产生大量 429，且后续宿主虚拟内存触及停止阈值；该批作为策略/资源观察单列，不计缓存容量。后续由用户释放内存，并仅在隔离实验提高限流额度。
3. A8 的开环复测在默认令牌到期后收到 607 个 401；停止该样本，改为每测段走真实登录刷新，不改变生产有效期。此前有效且无认证错误的 11 个正式样本保留。
4. A8 在结束时整文件读取约 506 MB 的单节点日志，导致测试诊断进程 OOM；本批改为逐行扫描相同故障标记，原生 API 不变。
5. A9 因复用前段曲线而缩短实际预热经过时间，另一节点到正式测段才首次回源，缺少 SQL 系列基线；30,000 次请求零业务错误仍保留为 Incomplete。新增预热后门禁，通过容器内受保护只读 RPC 等待两节点真实回源系列，保留缺失指标不能补零的规则。A10 在只读复核发现 Windows/容器回环边界问题后主动终止，修复为容器内 RPC 后启动 A11；两个失败父批次均保留真实退出 1 和清理证据。
6. A11 的基线门禁和 500 RPS 复测均通过；持续写入的第 10 次干预后，Windows/容器共享命令文件发生 EPERM 原子替换冲突。修复为解析命令后立即释放允许删除共享的文件流，发送端仅对 EPERM/EACCES/EBUSY 最多重试 20 次、间隔 50ms，不删除可见命令；13 项驱动检查覆盖瞬态占用、持续占用和不应重试的错误。失败父批仍保留实际退出 1，A12 继续验证剩余矩阵。
7. A12 SQL Server 生命周期通过、14 个正式样本全部留证，MySQL 在迁移 008 初始化阶段报 1419；原始 TRX 为 1 通过、1 失败，父进程实际退出 1。对齐仓库 `SharedDatabaseFixture`，仅给隔离 MySQL 追加 `--log-bin-trust-function-creators=1`，仍以 fullnet 应用账号执行；A13 复用已核验的 SQL Server 结果，补测 MySQL，不修改生产数据库或迁移。
8. A13 MySQL 的 128 VU 档在有效 JWT 下出现 1,033 次 401，立即停止父批。事后累积 SQL 计数记录 1,033 次会话权威查询 database_error，两节点的首条相关日志均在本测段内、数据库错误码均为 1040；测前没有该失败系列，不把缺失当作零基线来计算差值。Threads_connected 从 129 到 151、Aborted_connects 增加 65。令牌在样本结束后仍有超过 500 秒有效期，排除前述令牌过期故障。冻结来源的 AccessSessionValidator 在数据库异常时返回无效，形成 fail-closed；不把该失败改为通过。A14 只收窄后续负载，保留此可用性边界。独立凭证：`artifacts/cache-hardening/cache-capacity-mysql-connection-boundary.json`。
9. A14 的闭环 64 VU 复测及开环 50/200 RPS 均通过；开环 500 RPS 出现 25/14,847 次 401、125 次调度丢弃，按正确性门禁停止，父实际退出 1。相应累积数据库错误为 25，首条相关日志在本测段内、错误码仍为 1040；连接数 126→151、Aborted_connects 增加 4，令牌仍有效。A15 保留该失败，只补 200 RPS 复测及低并发写入/暂停场景，不以总体错误率低于 1% 将 401 判为可接受。

## 结果与验收

下列检查均已实际执行并退出 0；Node 驱动检查也在 A15 的正式资源入口内重新执行：

```text
node --test tests/performance/cache-capacity.test.mjs
node artifacts/cache-hardening/verify-cache-capacity-mysql-boundary.mjs
node eng/load/cache-capacity/summarize.mjs artifacts/cache-hardening/fullnet-cache-capacity-20261016-a15 artifacts/cache-hardening/cache-capacity-launch-20261016-a15/terminal.json artifacts/cache-hardening/cache-capacity-mysql-connection-boundary.json
node artifacts/cache-hardening/cache-capacity-default-quota-observation.mjs
node artifacts/cache-hardening/check-cache-capacity-owned-files.mjs
```

指标按成功业务响应吞吐与拒绝吞吐分别报告；SQL 通过稳定 StatementName 区分 Grid、认证、审计与控制请求。原始证据含 k6、OTLP、SQL IO/锁/连接、Redis INFO、容器 CPU/内存及宿主资源。缺失的可选指标保持未知。

| 验证范围 | 实际结果 | 证据边界 |
| --- | --- | --- |
| 驱动 Node 检查 | 14/14 通过，零失败/跳过 | A15 的 guard-tests.log 与真实退出 0 |
| Release 消费者 | A13 构建退出 0，零警告/错误；A15 哈希复用检查退出 0 | A15 未重复构建；Native API 未重新发布 |
| 双库生命周期 | 2/2 通过，零失败/跳过，MTP 实际退出 0 | 仅表示收窄范围驱动、控制和排空完成 |
| 完整压力样本 | 25 个，其中 24 个满足预算、1 个超预算 | 22 个复用，3 个新实跑；4 个新预热不计容量样本 |
| 失败边界独立核验 | MySQL closed128、open500 均保留为失败 | A13/A14 父进程实际均退出 1；不是通过样本 |
| 源码与清理 | 源码前后检查均退出 0，新增遗留容器为空，私有卷空闲 | A15 状态 completed-locks-released，正式资源正常释放 |
| 独立汇总 | 实际退出 0 | capacity-baseline-proof.json、capacity-baseline-report.md |

最终监督父进程 PID 99256 通过存活进程句柄取得退出码 0。原始压力退出 99 和历史失败父批没有被改写。旧缓存 Unit/Architecture/Native42 验收保持原有独立来源，本批不将其重计为当前 main 新验收。

`node artifacts/cache-hardening/write-cache-capacity-handoff-20261016.mjs` 实际退出 0，独立核对 withTestRun 的 passed/heavy 运行记录、工作区 normal 锁与同机 heavy 锁释放、标签容器为空及私有卷空闲。凭证为 `artifacts/cache-hardening/cache-capacity-resource-release-20261016.json`，SHA-256 `2d9c5da38dc16aca59137dab4f246f214752504839a01b214d6a060cdcceae21`。已按既有授权向 AI 任务交接资源；后续其他任务合法取得锁不改变本批释放事实。本批未提交、推送或部署。

## 关键结果

以下均为同宿主、单个认证热键和所列实验配额下的短测段，RPS 为成功业务响应吞吐。闭环 VU 不是同时在途数。

| Provider / 场景 | 成功 RPS | P99 ms | 结论 |
| --- | ---: | ---: | --- |
| SQL Server C0，16 VU，两次 | 1298.75 / 1329.34 | 58.24 / 57.10 | 通过预算 |
| SQL Server S2，16 VU | 1549.60 | 54.66 | 通过预算 |
| SQL Server S2，128 VU，两次 | 1776.28 / 1827.15 | 189.43 / 174.61 | 通过预算 |
| SQL Server S2，开环 500 RPS，两次 | 499.71 / 500.00 | 49.84 / 50.48 | 零业务错误、零丢弃 |
| SQL Server S2，开环 1000 RPS | 998.18 | 66.98 | 110 次调度丢弃，超预算，保留退出 99 |
| MySQL C0，16 VU，两次 | 575.82 / 897.74 | 120.94 / 45.68 | 通过预算，但吞吐波动明显 |
| MySQL S2，16 VU | 1166.53 | 39.67 | 通过预算 |
| MySQL S2，64 VU，两次 | 1440.80 / 1112.84 | 88.19 / 194.68 | 通过预算，但吞吐和长尾均有波动 |
| MySQL S2，开环 200 RPS，两次 | 200.01 / 199.97 | 202.81 / 39.77 | 零业务错误、零丢弃 |

MySQL 失败边界不能被上述通过结果覆盖：128 VU 中止样本有 1,033/8,853 次 401；500 RPS 中止样本有 25/14,847 次 401 和 125 次丢弃。两者均出现会话权威读取的数据库错误 1040，连接数达到 151。A15 实读 MySQL 8.0.46、max_connections=151，原始观察保存在 mysql-server-budget-observation.json。MySQL 1000 RPS 未执行。

错误 1040 对应连接数过多，参见 [MySQL 官方错误参考](https://dev.mysql.com/doc/mysql-errors/8.0/en/server-error-reference.html#error_er_con_count_error)。冻结实现中的 AccessSessionValidator 捕获数据库异常返回 false，FullNetJwtBearerEvents 据此使认证失败，支持此次 fail-closed 归因。尚未证明连接泄漏、双连接长持或某个池配置是唯一根因；Threads_connected 包含空闲连接，不能当作活动查询数。连接池上限和复用语义参见 [MySqlConnector 官方连接选项](https://mysqlconnector.net/connection-options/)。

缓存确实减少了 Grid 回源：SQL Server C0 两次分别为 77,979/77,979、79,811/79,811 次请求/查询；S2 的 16 VU 为 93,040 次请求、3 次 Grid 查询，总 SQL 仍为 93,047 次。MySQL S2 的 16 VU 为 70,005 次请求、3 次 Grid 查询，总 SQL 为 70,012 次。不能把整个请求链说成不访问数据库，权威安全读取及少量控制调用仍在统计中。

缓存命中也没有在该窗口体现出总分配下降：SQL Server C0 约 102.81–103.06 KiB/请求，S2 16 VU 约 109.37 KiB/请求；MySQL 分别约 76.55–76.74 和 90.11 KiB/请求。这是两 API 累积分配差值除以业务请求数的近似值，包含后台及控制开销，不能直接归因到 FusionCache 的单个对象或方法。GC、容器 CPU/内存与数据库原始快照保存在完整凭证中，未据此宣称已定位所有瓶颈。

| 故障/失效场景（16 VU） | 成功 RPS | P99 ms | HTTP Max ms | 跨节点写入观察 |
| --- | ---: | ---: | ---: | --- |
| SQL Server 受控写入 | 1322.89 | 56.42 | 86.74 | 11 次，最大 157.19ms |
| MySQL 受控写入 | 795.01 | 58.95 | 135.38 | 10 次，最大 242.04ms |
| SQL Server Redis 暂停 5 秒 | 1082.94 | 60.73 | 2991.30 | 不在暂停期间执行写入 |
| MySQL Redis 暂停 5 秒 | 709.32 | 88.18 | 2875.89 | 不在暂停期间执行写入 |

以上四个场景均无业务错误。写入观察计时包含 PUT 和对端轮询，不能解释为纯 Backplane 传输延迟。Redis 暂停期间虽然 P99 通过，仍有约 2.88–2.99 秒的最大请求；单看 P99 会忽略少量长尾。MySQL 首个 C0 对照也曾出现约 2.96 秒最大请求，不能把全部长尾仅归因于 Redis 故障。

## 建议与后续门禁

连接预算核对及聚焦复测进展见[连接预算复测](2026-10-16-cache-connection-budget-recheck.md)；本报告中的原失败、原预算超限和未执行范围保持不变。

1. 维持现有生产缓存、安全权威读取、默认限流及 TTL 决策。本批证明了特定 Grid 热键的数据库卸载，尚无增加分布式回填锁、全局 epoch 或替换缓存实现的容量证据。
2. 下一项优先核对冻结来源与当前 main 的连接管理差异，按实例数、实际池划分、池上限和维护预留核算数据库连接总预算，并补充空闲/活动连接证据。同步审查依赖过载时有效凭据被转换为 401 的可观测性及重试契约，始终保留 fail-closed。未经这一步，不将 MySQL 128 VU 或 500 RPS 改为通过。
3. 连接预算与契约调整后，只重测受影响的 MySQL 边界及邻近通过档，不重复整套缓存功能验收。多租户工作集、载荷尺寸、L1 内存斜率与长时稳定性仍需要下一批明确负载；不能用本次短测段给生产节点定容或承诺固定 QPS。

## 能力边界

两个节点导出的 active_requests 时间不同步，导出值之和最大值不能证明同一时刻的全局峰值或其下界；VU 和目标 RPS 也不能替代实际在途。开环丢弃可能来自发生器或服务资源限制，本批不能据此独立归因或认定容量天花板。

本批仅覆盖一个小载荷认证热键、两个 API、Worker=0、同宿主与短测段。受控低频写入失效不能替代一般写入容量；Redis 命令暂停不能替代网络断连、丢消息或节点故障。多租户工作集、载荷尺寸、L1 内存斜率、混合业务、Worker、长时 Soak 及 2K/5K/10K 同时在途均未验证。本地实验完成不改变 **Capacity-not-verified**，不承诺生产固定 QPS。
