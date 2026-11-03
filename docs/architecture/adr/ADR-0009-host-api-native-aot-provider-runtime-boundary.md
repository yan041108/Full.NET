# ADR-0009：Host.Api Native AOT Provider 运行时边界

- 状态：已批准
- 决策日期：2026-08-24
- 适用范围：`Full.NET.Host.Api` Native AOT 发布闭包内的 **S3 对象存储**与 **Kafka 范围重放 API**；不覆盖 Worker、Migrator 全量 Kafka Provider
- 关联文档：[`ADR-0008`](ADR-0008-api-native-aot-runtime-boundary.md)、[`ADR-0006`](ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)

## 1. 上下文

Phase 2 已在完整 API 闭包内完成 linux-x64 Native AOT publish 与核心 HTTP/SignalR JSON 外部进程验证。`AWSSDK.S3` 与 `Confluent.Kafka`（API Replay）已纳入 Host.Api Native 发布闭包；Phase 3 补 Provider 真实运行证据。

Phase 3 在不关闭模块、不替换为空实现的前提下，为 **Provider 运行路径**建立可验证边界与精确状态声明。

## 2. 决策

### 2.1 Phase 3A — S3（`Native-provider-verified: s3`）

1. S3 已在 Phase 2 发布闭包内；Phase 3A 仅补 **真实 AWSSDK.S3 客户端 + MinIO/S3 端点** 的运行证据。
2. 验收必须通过 **Native Host.Api 外部进程** + 真实 HTTP 上传/下载/删除；不得使用 `IS3BlobClient` 内存替身作为 Native 证据。
3. 凭据仅经环境变量注入；禁止写入 appsettings、日志或验证记录。
4. 未真实验证的 AWS 凭据链（Workload Identity、实例角色、Web Identity 等）必须标记 **未验证**，不得由单元测试推断。

### 2.2 Phase 3B — Kafka Replay（`Native-provider-verified: kafka-replay`）

1. 仅覆盖 API 的 `AddFullNetKafkaReplayOperations` / `IKafkaReplayService`；不覆盖 Worker Producer/Consumer、CDC Relay、DLQ、Lag Observer。
2. Native AOT 下必须注册 **真实** Kafka 重放实现；`DisabledKafkaReplayService` 可保留给其他明确禁用 Profile，**不得**作为 Phase 3B 验收路径。
3. JIT Host 行为保持不变。
4. 若 Confluent.Kafka / librdkafka 仅能通过通配 Root、通配 linker 或全局 `NoWarn=IL*` 换绿，Phase 3B 标记 **Blocked/Experimental**，禁止合入假闭包。

### 2.3 通用治理

2026-10-29 项目所有者另行授权 Messaging 完整 Native 控制面及 Worker CDC 的定向验收，Worker 独立阶段见 [ADR-0010 Phase 8](ADR-0010-worker-native-aot-analysis-boundary.md#2026-10-29-独立-phase-8messaging-cdc-原生运行)。该实施授权不改变本 ADR 原有 Phase 3B 的历史证据范围；新的切流/回退/恢复、消费者/重放/DLQ 路径须由本轮真实 Linux ELF、双库及外部进程故障证据独立确认，不由 Replay 或分析通过外推。

该独立Messaging范围现已用真实API/Worker ELF在双库原恢复方法中 **2/2、零跳过** 通过，包括原会话失锁后有限迟到pause/resume、真实恢复/重放/DLQ及Worker SIGTERM退出0。当前API ELF为167445072 bytes、SHA256 `889f78d6376f1a093af821f02c70021355e2f8c9bb97153af06dec53faab1e81`；不是全产品Native、Native长稳或生产容量证明，也不改变既有S3及其它Provider边界。详见[最终本机证据](../../verification/2026-10-28-messaging-reliability-continuation.md#2026-10-29分片发现与完整-native-控制链续办)。

1. Provider 能力不得通过关闭 Identity/Tenancy/Files/CodeGeneration/SignalR 等模块换绿。
2. 第三方 ILC 告警按 **程序集名 + IL 告警码** 精确登记；未观察到的告警禁止预先加入 allowlist。
3. 禁止 `NoWarn=IL*`、通配 `TrimmerRootAssembly`、通配 linker descriptor、无依据 `UnconditionalSuppressMessage`。
4. Worker / Migrator Native AOT 属于后续独立 Phase，本 ADR 不得顺手开启。

## 3. 完成状态（只能精确声明）

| 状态标签 | 含义 |
|---|---|
| `Native-provider-verified: s3` | Linux Native Host.Api + 双库元数据 + 真实 MinIO/S3 HTTP 链路通过 |
| `Native-provider-verified: kafka-replay` | Linux Native Host.Api + 真实 Kafka 范围重放 E2E 通过 |
| `Kafka Replay Native: Experimental/Blocked` | publish spike 或 E2E 无法在无通配 suppression 下通过 |

不得写成「Kafka Provider 全面 Native AOT」或「AWS 全凭据链已验证」。

## 4. 验证入口

- `pnpm test:aot:native:s3:e2e`
- `pnpm test:aot:native:kafka-replay:e2e`（Phase 3B）
- `pnpm test:aot:native:providers:e2e`（S3 + Kafka Replay 组合）

非 Linux：JSON discovery 验证最低发现数后 Skip；禁止零测试假绿。

## 5. 第三方 ILC 边界（Phase 3B publish spike）

| 程序集 | 告警 | 证据 | 状态 |
|---|---|---|---|
| `Confluent.Kafka` | `IL2104` | `librdkafka.so` 随 linux-x64 产物发布；`NativeAotRoots.xml` 精确保留 API Replay native binding | **`Native-provider-verified: kafka-replay`**；Linux CI 双库 E2E 已闭合 |

## 6. 完成证据

- 基线：`f3ea5f51c76275968f0525b4b5c57c0a865eed6b`
- CI：[`api-native-aot-linux` run 32821397581](https://github.com/yan041108/Full.NET/actions/runs/32821397581)，2026-08-25 结论 `success`
- S3：Native Host.Api + SQL Server/MySQL 元数据 + 真实 MinIO HTTP E2E 通过
- Kafka Replay：Native Host.Api + SQL Server/MySQL + 真实 Kafka 范围重放 E2E 通过
- 精确状态：`Native-provider-verified: s3`、`Native-provider-verified: kafka-replay`

### 2026-10-30 Workflow 消费者定向切片

Workflow 新增真实 Native API/Worker 协作附件的 S3 双库入口：`workflow-native-s3-my-sql`、`workflow-native-s3-sql-server`。两项实际2/2、零跳过，使用同机已核对的API ELF `c05a8dc8…893df8`、Worker ELF `b4b4d3dd…0c28c`；各自独立MinIO、数据库和回收会话。Native API实际上传/读取对象，夹具另核对独占桶内4个对象的真实字节；Worker真实执行协作能力、结果、取消及撤权链路。全部容器正常回收，发布二进制及冻结控制器前后摘要一致。

该切片不证明Worker直接S3 Blob I/O、通用S3删除、AWS全部凭据链或物理Kafka链路，也不扩大上面的Provider状态；具体新鲜TRX、失败记录、资源预算及复用来源见[Workflow交接记录](../../superpowers/plans/2026-10-28-workflow-cross-machine-handoff.md)。

2026-10-31 Workflow 物理 Kafka 基本协作补证：本批新 API ELF `cfd62580…208b7`（171929488 bytes）与新 Worker ELF `b7eb86bb…ff090`，已在 SQL Server/MySQL 各1例、零跳过通过。各例覆盖八条流程及四流逐次证明/切换、结果/取消、Broker暂停恢复、重复Offset、348重放和Requested单流回退。同一对ELF随后完成本地Files、两层子代、目标双实例、取消/撤权及停机续办组合双库2/2、零跳过，并由真实OTLP/Prometheus验证四流采集、共享组映射正式流汇总及现行两分钟生产告警触发/恢复。故障注入期间持有真实控制会话锁；后续新API和Worker已另行完成多流迟到REST纠偏Native双库2/2、零跳过。仅增加这些消费者切片的本机Linux原生运行证据，不扩大通用Provider、全产品Native、容量或SLO状态。冻结输入、原始291/302/305轮TRX、失败与复用边界见上述交接记录。
