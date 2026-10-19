# 日志 B1 与集中投递分档容量验收（2026-10-19）

本批按项目本地验收规则完成：B1 双库八组、Collector/ApplicationKafka 接受资源配置下八组容量及原短档四轮回归均有实测。接受范围由未变闭包复用与定向复验组成；**不是一次会话八组全部绿色**，原失败 TRX、原预算与不利数据保留。

## 基线、环境与资源

- 分支 main，基线 `cad1998fd711f0125f487781dae0804213405517` 加共享工作树，任务快照 `logging-remaining-capacity-20261019`。每轮保存实际源清单及正式构建身份/输出，未覆盖其他任务改动。
- Microsoft Windows 10 专业版 10.0.19045，12th Gen Intel(R) Core(TM) i7-12700H，物理内存 63.75GiB、20 逻辑处理器；SDK 10.0.402 / Runtime 10.0.13。Docker 为同机 Linux amd64，20 vCPU、约 19.30GiB 内存。
- 所有 .NET 进程 `DOTNET_PROCESSOR_COUNT=2`；Windows Job 在恢复执行前绑定并实际回读 CPU/6GiB/Kill-on-close。B1、500 档和 Collector 高档为整棵测试进程树 2CPU；ApplicationKafka 高档接受配置为整树 4CPU。树中含负载发生器、Kestrel、独立消费者及观察端，不能称为应用进程独享 CPU。
- SQL Server 3GiB/2CPU，内部 `MSSQL_MEMORY_LIMIT_MB=2048`；MySQL 1.5GiB/1CPU；Kafka 2GiB/1CPU；ES 500 档为 2GiB/1CPU、高档为 2GiB/2CPU；Fluent Bit 512MiB/0.5CPU，Ryuk 256MiB/0.5CPU。容器创建时固定限额与零额外 swap，不复用、不隐式拉取；独享随机归属及 Ryuk ACK 后才启动。
- Kafka 4.1.2、ES digest `82ac14f43fe7…`、Fluent Bit 4.1.1 digest `2a5cb41f99b7…`、Ryuk 0.14.0 digest `7c1a8a9a47c7…`，镜像 ID、平台及实际预算均回读。TLS Kafka、私有 CA HTTPS ES、正式独立 JIT Consumer 均为真实依赖。

## B1 持续写库

真实 API Host 的正式协调器、Dapper 与事务；每库以 1、8、32 并发各 30 秒，再复测 32，并使用原默认微批参数。每调用写一条 Operation，序号每十条另写 Exception。每档预热 64 条独立 TraceId；测量结果逐 ActionKey、状态、异常内容与接收/批次计数对账。

| Provider | 并发 / 轮次 | Operation / Exception | 调用/s / 行/s | 调用 P50 / P95 / P99 ms | 每信封等待 P99 ms |
| --- | --- | --- | --- | --- | --- |
| SqlServer | 1 / 1 | 956 / 95 | 31.84 / 35.00 | 31.263 / 33.694 / 36.026 | 36.056 |
| SqlServer | 8 / 1 | 3056 / 305 | 101.74 / 111.90 | 78.635 / 84.028 / 88.892 | 88.876 |
| SqlServer | 32 / 1 | 20064 / 2006 | 668.04 / 734.83 | 47.203 / 56.434 / 66.108 | 66.100 |
| SqlServer | 32 / 2 | 20160 / 2016 | 671.04 / 738.15 | 47.064 / 52.984 / 72.796 | 72.794 |
| MySql | 1 / 1 | 894 / 89 | 29.78 / 32.75 | 31.987 / 44.766 / 48.034 | 48.018 |
| MySql | 8 / 1 | 6304 / 630 | 210.12 / 231.12 | 35.240 / 48.894 / 53.055 | 53.049 |
| MySql | 32 / 1 | 23200 / 2320 | 772.70 / 849.97 | 44.377 / 50.277 / 55.001 | 54.997 |
| MySql | 32 / 2 | 23776 / 2377 | 791.75 / 870.90 | 43.690 / 50.672 / 54.319 | 54.313 |

共 98,410 条实测 Operation、9,838 条 Exception，另有 512 条预热 Operation。非预期写入失败、拒绝、毒记录均为零；每档结束队列深度和预约字节归零。调用 P99 最大 88.8923ms，小于预设 500ms。20ms gauge 采样最大深度 34、字节 10,544、最老排队年龄约 0.3482ms；gauge 不代表信封全部等待，直方图与调用等待分别报告，不能据采样保证没有瞬时峰值。

进程两核归一 CPU 最大 7.49%，进程寿命工作集峰值 230.43MiB。最高档估算信封预算吞吐 SQL Server 208.22–209.17KiB/s、MySQL 240.98–246.93KiB/s；这是内存预约估算，**不是物理 SQL 行、网络或磁盘字节吞吐**。分配原始值、每信封时序、Meter 与水位在结果 JSON。此场景测量 B1 调用等待，未测完整认证业务 HTTP P99、数据库 Query Store/锁等待分解或满历史表查询。

## B2 集中投递容量

每档 200 次预热后，父进程先确认全部预热的真实下游位点，再开始 30 秒固定节奏发送。每路线 500 与 1000 请求/s、正反序各一轮；8 个发送循环，实际 Kestrel 在途峰值均为 8。安全投影固定 pageSize=20、totalCount=100，10% 预期 HTTP 500；这些预期状态不计为非预期错误。

两路线 Consumer 显式批次 64，原始 Value＋Key 合计最多 512KiB；默认仍为单条，原短回归保留批次 8。ES 完整 NDJSON 请求 1MiB、响应 64KiB 上限不变，逐项成功或可靠 DLQ ACK 后才提交每分区连续前缀。Collector 容量使用可选低延迟配置，输出及 Tail 进度检查均为 200ms，默认参考配置不改；模拟 CRI 的可信来源与伪造镜像逐字段/ID 拒绝仍核验。

所有接受样本均满足原预算：实际发送至少目标 95%、HTTP P99<100ms、含调度等待的计划完成 P99<250ms；窗口末有效观测距结束不超过 1.5 秒且至少 95% 实测记录已真实提交；尾部位点追赶等待<5 秒；零非预期响应、零宿主丢弃，最终全部 ES 文档、位点高水位与零 DLQ 对账。

| 路线 | 目标/s / 轮次 | ES CPU / 测试树 CPU | 实际/s | HTTP P50 / P95 / P99 ms | 计划 P99 ms | 窗口内确认 | 尾部位点等待 ms |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Collector | 500 / 1 | 1 / 2 | 499.95 | 0.981 / 2.099 / 3.030 | 17.886 | 97.72% | 1.942 |
| ApplicationKafka | 500 / 1 | 1 / 2 | 499.89 | 0.784 / 1.776 / 2.917 | 17.002 | 98.93% | 1.417 |
| ApplicationKafka | 500 / 2 | 1 / 2 | 499.81 | 0.833 / 2.010 / 3.323 | 18.151 | 99.37% | 2.172 |
| Collector | 500 / 2 | 1 / 2 | 499.93 | 0.694 / 1.930 / 2.697 | 16.721 | 96.98% | 2.901 |
| Collector | 1000 / 1 | 2 / 2 | 999.75 | 0.621 / 2.011 / 3.220 | 61.650 | 98.22% | 1.932 |
| Collector | 1000 / 2 | 2 / 2 | 999.83 | 0.739 / 2.260 / 3.425 | 116.396 | 97.22% | 2.090 |
| ApplicationKafka | 1000 / 1 | 2 / 4 | 999.99 | 0.696 / 2.795 / 4.472 | 16.223 | 99.42% | 1.053 |
| ApplicationKafka | 1000 / 2 | 2 / 4 | 999.96 | 0.619 / 1.987 / 3.090 | 15.826 | 98.54% | 1.204 |

合计 180,000 个实测请求、1,600 个预热 HTTP 事件，加 48 条生命周期日志，ES 181,648 个文档逐 ID/字段一致；实测 Kafka 消息正文合计 282,277,125 字节（不含协议、复制和磁盘开销）。HTTP P99 为 2.6971–4.4718ms，计划完成 P99 为 15.8261–116.3962ms。最低窗口确认 96.98%，尾部位点等待最大 2.901ms；Collector 源排空/正常停止及校验为独立计时，不冒充纯消费延迟。

| 路线 | 目标/s / 轮次 | 实测正文 KiB/s | 请求/客户端进程两核归一 CPU | 寿命峰值 MiB | 测量期分配 MiB |
| --- | --- | --- | --- | --- | --- |
| Collector | 500 / 1 | 787.01 | 11.33% | 93.02 | 621.94 |
| ApplicationKafka | 500 / 1 | 743.96 | 35.02% | 102.19 | 591.70 |
| ApplicationKafka | 500 / 2 | 743.83 | 39.41% | 101.88 | 592.58 |
| Collector | 500 / 2 | 786.99 | 13.57% | 91.98 | 620.93 |
| Collector | 1000 / 1 | 1574.15 | 24.42% | 92.75 | 1236.91 |
| Collector | 1000 / 2 | 1574.28 | 34.11% | 92.97 | 1238.08 |
| ApplicationKafka | 1000 / 1 | 1488.58 | 53.28% | 102.86 | 1176.22 |
| ApplicationKafka | 1000 / 2 | 1488.54 | 51.90% | 106.40 | 1176.68 |

平均单条实测正文约 1.5–1.6KiB，不能外推到每条 16/64KiB。CPU 包含同进程请求发生器；工作集为进程寿命观测峰值，不是堆大小，也不是树内全部进程内存。容器 CPU/内存/BlockIO 原始采样保留于各阶段 `*-stats.jsonl`，包括启动及停机，只有实际有效样本才计量；生命周期 EOF 缺样单独记录，不伪造连续指标。资源配额本身不证明服务不会饱和。

## 实际失败与选择

1. 初次夹具缺 PullPolicy 导入、实时 CRI 桥固定 10K 条上限分别造成编译/测试失败，已修正为有界档位；原短档默认上限保留。预热只等 HTTP 曾遇协调器未就绪及调度停顿，现等 200 条真实位点后测量，并只对三种明确的协调器未就绪查询继续有界观察。
2. Consumer 批次 8 的两组 500/s 基线最终位点追平约 86.9/88.4 秒，最终可对账也不算持续容量通过。新上限 64 同时保留 512KiB 总预算；旧实现的 64 条跨分区用例真实 RED，修正后相关 53 项单测通过。
3. 默认 Collector 周期下首个 500/s 仅确认 13,516/15,000；可选低延迟配置后四个接受的 Collector 样本均超过 95%。固定 [Fluent Bit 4.1.1 Tail 参数](https://github.com/fluent/fluent-bit/blob/v4.1.1/plugins/in_tail/tail.c#L568) 与[进度检查实现](https://github.com/fluent/fluent-bit/blob/v4.1.1/plugins/in_tail/tail_fs_inotify.c#L284) 支持该显式配置。更频繁 fstat 会增加文件系统调用，本批只有少量模拟 CRI 文件，不证明大文件数或真实 kubelet/inotify 时序。
4. ES 1CPU 的 1000 档三组未到 95%、一组到 97.59%，完整矩阵仍失败；ES 两核后后端四组都过，但两核共享测试树下 ApplicationKafka 计划 P99=560.695/782.954ms，正式汇总仍失败。最后只重测两组，预先声明测试树四核后均过。**不降低 95%、P99 或尾等待预算；不把增加硬件当成同资源代码提速或最低资源保证。**
5. 临时观察器曾将正常删除的 Dead/单行或多行 EOF 当致命错误，造成真实中止；原中止不是容量通过。修订观察器只在 Docker 身份查询仍有效时把纯 EOF 记录为指标缺样，归属、CPU/内存/OOM、Ryuk 和清理检查仍严格；7 项回归通过。

## 命令与证据

容量类仅在 `/p:FullNetLoggingCapacity=true` 编译；日常默认程序集不发现这些长时用例，没有用 Skip 冒充验收。依赖由正式资源池、创建前硬预算和外层 Ryuk 会话启动，不可只伪造环境变量直跑。实际入口为：

```text
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release /p:FullNetLoggingCapacity=true /p:RestoreLockedMode=true -m:1
dotnet <Integration.dll> --filter FullyQualifiedName~AuditingSustainedCapacityTests
dotnet <Integration.dll> --filter FullyQualifiedName~KafkaLogRouteCapacityTests
FULLNET_LOG_CAPACITY_ONLY_RATE=1000
FULLNET_LOG_CAPACITY_ONLY_ROUTE=ApplicationKafka
dotnet <Integration.dll> --filter FullyQualifiedName~Sustained_routes_reconcile_every_record_within_declared_capacity_budgets
node --test tests/deployment/log-consumer-contract.test.mjs
node scripts/testing/verify-integration-shards.mjs
```

定向 Unit 53/53、Helm 契约 6/6、真实消费者 9/9、B1 双库 2/2、低延迟契约及 CRI 边界、短回归四轮均按各自完整原件成立。原低档完整 MTP 为 3 通过/1 失败，ES 两核四组 MTP 为 0/1，最终定向为 1/0，不重写为同次全部绿色。下表各 TRX 与同次 discovery 的唯一 UID 已精确相等；接受样本原始 HTTP 与计划完成 P99 重新按全部时序计算一致，12 个压缩逐条收据 SHA256 全部匹配。

| 证据组 | Passed / Failed | 原始 TRX SHA256 |
| --- | --- | --- |
| unit | 53 / 0 | `effd5246244d2c1ef45e1c6c7849e0f541ce4c3b10567a73e124ee901c23e01a` |
| b1 | 2 / 0 | `725701e799160505daf6ed30dcfeaec0207c7802f6e4529a85383ae4959dca7d` |
| consumer | 9 / 0 | `64f5afda07f04b1dfd1f9f5bccf80059fd10d090f911ab46e2e825e24980e175` |
| lower | 3 / 1 | `727fd43a91f28b54a29fd43a8c548164a7a204463a621abb17d228c2e06f6c10` |
| intermediate | 0 / 1 | `27f57a70badd2322248c5d72b84907311c0368e947fbe83df650a06286e08873` |
| higher | 1 / 0 | `9b2d762056a629151a8511152b0a33f0d485c9bb64d1a887cf221154b8f1f99b` |

最终默认 Release 正常构建 0 警告/错误，正式 Integration 五分片 202/202/560/288/80=1332，迁移子组 250/310 完整互斥。最后父进程实际退出 0、Job 活动进程 0，无强杀；各源与产物守卫通过。只读物理核对 B1、消费者和三个完整资源阶段的 **57 个自有容器、79 个匿名卷均已不存在**，每阶段 Ryuk/归属资源清理收据保留。

本机最小证据入口：`C:/Users/Administrator/AppData/Local/Temp/fullnet-logging-app4-capacity-14ae8376f14547b9b6ad5b69c155fa3a/batch` 中 `verified-summary.json`、`physical-cleanup-proof.json`、`state.json`、`terminal.json`、正式构建记录及源前后清单；摘要指向前序五组 TRX、逐条收据及哈希。这里是系统临时证据，不是运行时依赖或整仓副本。两轮遗留日志目录的递归清理命令被命令策略以 `blocked by policy` 拒绝，完整命令/拒绝留在本任务工具记录；未用另一解释器绕过，旧失败数据暂保留，不宣称磁盘已全部清空。

本轮关键交付源 SHA256：

- `src/Platform/Full.NET.LogConsumer/BatchLogDeliveryProcessor.cs`：`7379deacc61d6ca53b410ecedf6b2d47c0cf58223aef527d86fd36dbc1b1f8e0`
- `src/Hosts/Full.NET.Host.LogConsumer/Program.cs`：`8dc608ca3d31366afb0d332c9afece9b6e84b75ed46a3c088b258e24c9764d14`
- `deploy/observability/fluent-bit-low-latency-values.yaml`：`6824decbce61cc3e50f711a5b37bf18fb43c07856d79e081de95abda11afaa79`

## 使用边界

可按本项目规则关闭本批有限本地容量余项。正式使用高吞吐配置须显式配置独立 Consumer 批次 64，Collector 的低延迟 overlay 按需要叠加；组件及资源选择仍可配置，默认部署不开实验消费者，不自动改生产环境。默认批次 1、默认 Collector 周期或 ES 1CPU 不享有这里的高档结论。

本结论不是完整认证/数据库业务 HTTP P99、独立负载发生器压测、Linux/AOT 容量、Kafka/ES HA、多实例混合业务、磁盘 fsync、跨崩溃零丢失或 2K/5K/10K/Soak。未测规模继续 `Capacity-not-verified`。B1 仍等待写入尝试并默认 fail-open；B2 仍为有界、允许按策略丢弃的遥测，不改成可靠业务事件或 Outbox。
