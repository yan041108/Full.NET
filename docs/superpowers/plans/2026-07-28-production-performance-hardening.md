# Production Performance Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> `fullnet-performance-hardening` for every task, `fullnet-module-delivery` for
> database or public-contract changes, and `superpowers:test-driven-development`
> for behavior changes. Execute tasks in order and update checkbox state only
> from fresh evidence.

**Goal:** 在提交 `1b2f358` 的性能治理基础上，先关闭 Outbox 长耗时重复消费风险，再建立
SQL Server/MySQL 生产等价混合负载基线，并按证据硬化 Audit、认证、Jobs、Outbox 运维、
真实用户前端性能和 Integration 反馈速度。

**Architecture:** 保持强化型模块化单体、Dapper 显式 SQL、SQL Server/MySQL 双 Provider、
事务 Outbox、至少一次投递、可靠 Audit、认证 fail-closed 和 Vue/Layui 双端基线。可靠性
缺口优先于吞吐调参；除已证实的 Outbox 租约风险外，其余候选先建立可重复基线，再以单变量
A/B 决定是否实施。不得引入 Kafka/CDC、动态可靠性降级、无界内存队列或未经证据的索引。

**Tech Stack:** .NET 10、Dapper、SQL Server 2022、MySQL 8、Testcontainers、
Microsoft Testing Platform、OpenTelemetry Metrics、Vue 3、Layui、Vite、Vitest。

## Global Constraints

- 当前基线提交固定为 `1b2f358`；本计划的验证结果不得与此前未提交工作树混写。
- 每项行为变更先建立可失败的 RED；数据库行为同时覆盖 SQL Server/MySQL。
- `OutboxWorker:MaxConcurrency` 默认保持 `1`，未经 Task 23 容量矩阵不得提高默认值。
- Outbox 与 Integration Event 继续按至少一次语义设计；租约续期只缩小重复窗口，不得
  宣称 Exactly-Once。
- Audit 可靠记录不得改成 fire-and-forget、普通日志或无界队列。访问遥测是否允许丢弃
  必须由独立契约明确，不从性能结果反推。
- 认证缓存必须关闭 Fail-Safe，冻结撤销时效与故障时 fail-closed 行为后才允许实现。
- 性能结果至少记录数据规模、并发、持续时间、P50/P95/P99、错误率、数据库连接池、
  GC、CPU、锁等待和 Outbox/Jobs 积压；本机样本不得表述为生产 SLA。
- 每个可独立审阅的 Task 完成后单独提交；`.cache/`、`.tmp/`、BenchmarkDotNet 原始
  工件和本地 TestResults 不进入 Git。

---

### Task 19: Outbox 主动续租与批尾租约保护

**Priority:** P0-Reliability

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxStore.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/DapperOutboxStore.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/OutboxSql.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxWorkerOptions.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs`
- Modify: `tests/Full.NET.UnitTests/Outbox/OutboxProcessorTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Messaging/OutboxRecoveryTests.cs`
- Modify: `docs/operations/outbox-worker-topology.md`
- Modify: `docs/roadmap/capability-status.md`
- Create: `docs/verification/outbox-active-lease-renewal-2026-07-28.md`

**Interfaces:**
- Consumes: 批次共享 `LockId`、现有 `LeaseSeconds`、独立消息 Scope、至少一次终态守卫。
- Produces: `OutboxWorker:LeaseRenewalSeconds`，默认 `10` 秒、范围 `1..1200` 且不得超过
  `LeaseSeconds / 2`；`IOutboxStore.RenewLeaseAsync` 按精确批次消息 ID 和共享
  `LockId`，只续期未处理、非死信消息。

- [x] **Step 1: 建立配置、SQL 与处理器 RED**

扩展 Options 单元测试，锁定默认值、范围和“续期间隔不超过租约一半”。新增处理器测试，
要求慢 Handler 执行期间周期调用续租，续租任务使用独立 DI Scope；续租失去所有权或数据库
失败时取消协作式 Handler 并传播原始续租异常，不能把租约故障写成业务重试。

- [x] **Step 2: 建立双库批尾 RED**

SQL Server/MySQL 各插入两条同路由消息，以 `BatchSize = 2`、`MaxConcurrency = 1`、
短租约和阻塞第一条 Handler 运行。将测试时钟推进到初始租约之后，等待至少一次续租，再从
第二个 Scope 调用 `AcquireAsync`；必须返回空集合，证明尚未开始执行的第二条消息也受到
保护。旧实现应允许第二个 Worker 重新领取。

- [x] **Step 3: 实现最小续租 GREEN**

Store 使用固定参数化 UPDATE 按主键集合与 `LockId` 单调延长 `LockedUntilUtc`，仅匹配
未处理、非死信行，避免未索引 LockId 扫描与时钟回拨缩短租约；MySQL 固定 matched-row
计数语义。零行更新表示租约不再持有。Processor 在消息批次存在时并行运行处理任务与续租
循环，续租每次创建独立 Async Scope、设置 Host 上下文并使用独立数据库会话。处理完成后
有界取消续租；续租先失败时取消批次并保留原始异常，同时处理最终成功与异常优先级竞态。

- [x] **Step 4: 聚焦、多轮与全量验证**

运行 Outbox Unit；SQL Server/MySQL 批尾测试各至少一轮，MySQL 连续三轮；运行 Release、
Unit、Compatibility、Architecture、完整 Integration、Governance、Naming、SQL Safety、
Skills 和 `git diff --check`。验证记录必须说明续租降低重复窗口但不替代 Handler 幂等。

### Task 20: 生产等价混合负载基线与性能预算

**Priority:** P0-Evidence

**Files:**
- Create: `benchmarks/Full.NET.Benchmarks/MixedLoad/MixedLoadOptions.cs`
- Create: `benchmarks/Full.NET.Benchmarks/MixedLoad/MixedLoadScenario.cs`
- Create: `benchmarks/Full.NET.Benchmarks/MixedLoad/MixedLoadRunner.cs`
- Create: `benchmarks/Full.NET.Benchmarks/MixedLoad/MixedLoadReportWriter.cs`
- Create: `benchmarks/Full.NET.Benchmarks/MixedLoad/MixedLoadResponseConsumer.cs`
- Create: `benchmarks/Full.NET.Benchmarks/MixedLoad/MixedLoadConnectionPoolTelemetry.cs`
- Create: `benchmarks/Full.NET.Benchmarks/MixedLoad/MixedLoadContainerTelemetry.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Program.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj`
- Create: `tests/Full.NET.UnitTests/Performance/MixedLoadContractTests.cs`
- Create: `docs/verification/production-equivalent-mixed-load-2026-07-28.md`

**Interfaces:**
- Consumes: 真实 API Host、JWT、API Key、读写请求、Audit、Outbox、SQL Server/MySQL。
- Produces: 可重复命令、固定 workload manifest、原始样本与汇总预算；不修改生产行为。

- [x] **Step 1: 冻结 workload 与 RED 契约**

定义至少四种流量：JWT 读、JWT 写、API Key 读、API Key 写；覆盖成功、验证失败、Audit
列表和产生 Outbox 的写请求。契约测试锁定权重、并发、预热、持续时间、随机种子、最大错误
率和必需指标，禁止只测单 Endpoint 或只报平均值。

- [x] **Step 2: 实现隔离基准驱动**

为每个 Provider 启动独立数据库与 API Host，完成迁移和确定性数据准备；运行前校验 Host、
认证与数据库健康。采集客户端延迟、状态码、Dapper Statement、连接池、GC、CPU、数据库
锁等待、Audit 写入和 Outbox 积压/最老年龄。

- [x] **Step 3: 运行双库基线并冻结预算**

先以低并发校验正确性，再运行 `1/4/16/32` 并发和至少 10 分钟稳态窗口。预算以各 Provider
独立 P95/P99、错误率和资源上限记录；只冻结回归门槛，不把本机吞吐作为生产承诺。

正式 V3 从提交 `db290c1de68024f9a8bc3e885decc0a67dfe24fa` 运行，双库 8 档共
778,263 个完整响应请求，证据与预算 8/8 PASS，非预期错误和 Dapper 失败均为 0。
SQL Server c=32 相对 c=16 仍增吞吐 18.6%，但 P95 翻倍、锁等待约 3.21 倍且 Gen2
由 6 增至 174；MySQL c=32 吞吐下降 3.2%，P95 增长 187.6%、P99 超过 1 秒。
后续 A/B 以 c=16 为主要参考、c=32 为压力退化档，不提高生产默认并发。

### Task 21: Audit contains 显式时间边界

**Priority:** P0

**Files:**
- Create: `docs/superpowers/specs/2026-07-28-auditing-contains-time-boundary.md`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Contracts/AccessLogContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Contracts/OperationLogContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Contracts/ExceptionLogContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/QueryHostAccessLogs/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/QueryHostOperationLogs/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/QueryHostExceptionLogs/Endpoint.cs`
- Modify: `contracts/openapi/auditing-access-logs-v1.json`
- Modify: `packages/client-contracts/src/auditing-access-logs.ts`
- Modify: `ui/admin/src/views/AccessLogsView.vue`
- Modify: `ui/admin-layui/js/core/access-logs.js`
- Modify: `tests/Full.NET.IntegrationTests/Auditing/AuditingAccessLogAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Auditing/AuditingOperationLogAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Auditing/AuditingExceptionLogAssertions.cs`

- [x] **Step 1: 先冻结公共契约**

禁止静默截断结果。Spec 明确 contains 查询必须携带显式 `FromUtc`/`ToUtc`，时间窗上限由稳定
配置给出；无 contains 的普通列表保持兼容。Vue/Layui 在用户启用 contains 时默认填入可见
时间范围，并允许用户在上限内调整。

- [x] **Step 2: 建立 RED 并实现双端 GREEN**

OpenAPI、共享客户端、双前端和双库 API 测试先锁定缺失/超窗 ProblemDetails，再实现统一
验证。复用当前参数化 SQL，不增加普通 B-tree 索引，不继续微调 OFFSET。

- [x] **Step 3: 复测 100,000 行矩阵**

复用现有 Audit benchmark 比较无界基线与新契约允许的最大时间窗，记录双库 P50/P95/P99
和执行计划；若最大允许窗口仍不满足预算，进入专用搜索设施 Decision Gate，不猜索引。

31 天候选覆盖完整 30 天数据集，SQL Server P95 与无界基本相同，已拒绝作为默认值。
最终生产默认与双端便利范围统一为 1 天；100,000 行同轮中 SQL Server P95 由
418.816ms 降至 37.180ms，MySQL 由 100.142ms 降至 16.484ms。完整 RED/GREEN、
双库 API、执行计划与否定证据见
[`auditing-contains-time-boundary-2026-07-28.md`](../../verification/auditing-contains-time-boundary-2026-07-28.md)。

### Task 22: Audit 同步写入尾延迟与可靠性分层

**Priority:** P0

**Files:**
- Create: `docs/superpowers/specs/2026-07-29-auditing-write-reliability-classification.md`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Middleware/AccessLogMiddleware.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Middleware/OperationLogMiddleware.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Middleware/ExceptionLogMiddleware.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/WriteAccessLogs/AccessLogWriter.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/WriteOperationLogs/OperationLogWriter.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/WriteExceptionLogs/ExceptionLogWriter.cs`
- Create: `tests/Full.NET.UnitTests/Auditing/AuditingWritePathTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Auditing/AuditingAccessLogAssertions.cs`
- Create: `docs/verification/auditing-write-tail-latency-2026-07-28.md`

- [ ] **Step 1: 只测不改，量化三次串行写占比**

在 Task 20 workload 中分别开启/关闭 Access、Operation、Exception 写入，记录单次和组合
增量 P95/P99、数据库命令数与锁等待。先确认“最多三次串行写”在真实请求组合中的发生率。

状态（2026-07-29）：Benchmark-only 的逐请求
`none/access/operation/exception/all` 归因、专用异常场景、Statement 耗时、三表行数和证据
门禁已落地；10 秒、并发 4 的双库正确性冒烟通过，并确认异常请求真实发生三次串行写。
正式 `30s/600s × c=1/4/16/32` 矩阵尚未执行，因此 Step 1 保持未完成。详见
[`auditing-write-tail-latency-2026-07-28.md`](../../verification/auditing-write-tail-latency-2026-07-28.md)。

- [x] **Step 2: 冻结可靠性分类**

Operation 与安全相关 Exception 保持可靠审计；Access 是否为可丢遥测必须由 Spec 明确。
若全部属于可靠 Audit，候选仅限同事务/同命令批处理或事务 Outbox，不允许进程内无界缓冲；
若 Access 被明确降级为遥测，必须有有界队列、丢弃计数和过载策略。

状态（2026-07-29）：已批准
[`Audit 写入可靠性分类与请求内批处理规格`](../specs/2026-07-29-auditing-write-reliability-classification.md)。
Access 明确为请求遥测；Operation 与安全相关 Exception 保持不可采样的同步数据库审计摘要。
选定候选为请求内固定三槽收集、单命令显式事务提交，不引入跨请求队列或后台任务。

- [ ] **Step 3: 单变量 A/B 后实施最小候选**

为选定候选先建立失败/崩溃/背压 RED，再实现。双库验证成功、回滚、异常和取消语义，复跑
Task 20 矩阵；只有 P95/P99 达标且可靠性测试不退化才保留。

状态（2026-07-29）：请求作用域固定三槽、单显式事务、单参数化命令候选已通过 Unit
`7/7`、基准契约 `20/20` 和 SQL Server/MySQL Auditing 影响集 `8/8`，其中两项真实制造
第二条 INSERT 失败并确认第一条回滚。并发 4 的 3 秒短时
A/B 中，两库总体 P95 均改善，MySQL `all` P95/P99 由 `78.878/281.212ms` 收敛到
`42.666/48.401ms`，且归因、行数、错误与预算证据门禁全部通过。正式 Task 20 长稳态矩阵
尚未复跑，故 Step 3 保持未完成；详见
[`auditing-write-tail-latency-2026-07-28.md`](../../verification/auditing-write-tail-latency-2026-07-28.md) 第 7 节。

### Task 23: Audit 与 Outbox 保留、清理和运维指标

**Priority:** P0/P1

**Files:**
- Create: `src/Modules/Full.NET.Modules.Auditing/Retention/AuditingRetentionOptions.cs`
- Create: `src/Modules/Full.NET.Modules.Auditing/Retention/AuditingRetentionRunner.cs`
- Create: `src/Modules/Full.NET.Modules.Auditing/Retention/AuditingRetentionHostedProcessor.cs`
- Create: `src/Modules/Full.NET.Modules.Auditing/Retention/AuditingRetentionTelemetry.cs`
- Create: `src/Modules/Full.NET.Modules.Auditing/Persistence/AuditingRetentionSql.cs`
- Create: `src/Hosts/Full.NET.Host.Worker/OutboxRetentionOptions.cs`
- Create: `src/Hosts/Full.NET.Host.Worker/OutboxRetentionProcessor.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/Outbox/OutboxSql.cs`
- Create: `tests/Full.NET.UnitTests/Auditing/AuditingRetentionTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Auditing/AuditingRetentionAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Messaging/OutboxRecoveryTests.cs`
- Create: `docs/operations/data-retention.md`

- [x] **Step 1: 冻结保留策略与人工暂停**

分别定义 Access/Operation/Exception、已处理 Outbox 和 Dead Letter 的保留期；Dead Letter
不得未经审批自动删除。配置包含小批量、轮询间隔、单轮上限和全局禁用开关。

状态（2026-07-29）：已批准
[`Audit 与 Outbox 数据保留和小批量清理规格`](../specs/2026-07-29-data-retention-and-cleanup.md)。
生产默认关闭；Dead Letter、Pending、待重试和持租约 Outbox 没有自动删除入口。Audit
先作为独立最小切片实施，Outbox 成功终态随后按同一门禁实现。

- [x] **Step 2: 建立双库 RED 并小批量删除**

SQL 使用稳定时间键和主键边界，小事务删除；验证仅删除截止时间前终态记录，不触碰待处理、
持租约、待重试或需审计保留的数据。记录事务日志/undo、锁等待、写放大和暂停恢复。

状态（2026-07-29）：Audit 与 Outbox 小批量删除均已完成。两者生产默认关闭并支持热暂停；
SQL Server 使用有界候选 CTE，MySQL 使用短事务领取 ID 后按领取集合删除。Audit 双库验证
三类记录公平推进；Outbox 双库验证只删除严格过期的成功终态，等于截止时间、Pending、
待重试、持租约和 Dead Letter 全部保留。持续写入容量矩阵仍由 Step 3 承接。

- [ ] **Step 3: 补充指标与容量复测**

增加清理行数、失败数、最近成功时间、Dead Letter 数/最老年龄、重试到期数和租约中数量；
指标只用低基数标签。以持续写入并行运行清理，确认请求和 Worker P99 不越预算。

状态（2026-07-29）：清理行数/失败/最近成功/耗时，以及 Outbox Pending、到期重试、活动
租约、Dead Letter 数与最老死信年龄均已落地；分类复用单次 backlog 采样查询，没有增加
数据库往返。持续写入并行清理的双库吞吐、P99、锁等待和日志/undo 写放大尚未复测，因此
本步骤保持开放。

状态（2026-07-29）：Audit 已发布删除行数、失败数、最近成功时间和单轮耗时指标，并注册到
Worker OpenTelemetry；Outbox 分类指标和持续写入容量复测仍待后续。

### Task 24: Outbox 容量矩阵、消息上下文与幂等门禁

**Priority:** P1

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Abstractions/Messaging/IIntegrationEventHandler.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Abstractions/OutboxEnvelope.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Outbox/OutboxCapacityRunner.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Outbox/OutboxCapacityReportWriter.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/Program.cs`
- Modify: `tests/Full.NET.IntegrationTests/Messaging/OutboxRecoveryTests.cs`
- Modify: `docs/operations/outbox-worker-topology.md`
- Create: `docs/verification/outbox-capacity-2026-07-28.md`

- [ ] **Step 1: 冻结消息上下文兼容方案**

为 Handler 暴露稳定 `MessageId`、MessageType、SchemaVersion、TenantId、TraceId 和
OccurredAtUtc；现有 Handler 通过兼容适配迁移。跨数据库或有外部副作用的消费者必须使用
MessageId 去重或证明天然幂等。

- [ ] **Step 2: 建立容量矩阵**

双库覆盖 Handler 延迟 `0/10/100/1000ms`、持续积压、多副本、并发 `1/2/4/8`、不同
BatchSize 与 payload 大小；记录吞吐、P95/P99、重复次数、续租命令、连接池、锁等待、GC
和恢复时间。

- [ ] **Step 3: 评估索引与默认并发**

只在执行计划证明需要时 A/B `DeadLetteredAtUtc` 过滤或调整 pending 索引；SQL Server
领取排序补齐 `(OccurredAtUtc, Id)` 的确定性等价验证。默认并发只有在两库所有正确性、
容量和资源门禁通过时才允许从 `1` 调整。

### Task 25: 认证请求链往返与 fail-closed 缓存

**Priority:** P1

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/Security/AccessSessionValidator.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Security/ApiKeyAuthenticationService.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Create: `tests/Full.NET.UnitTests/Identity/AuthenticationCacheTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Identity/SessionRaceAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Identity/IdentityApiKeyAssertions.cs`
- Create: `docs/verification/authentication-request-chain-2026-07-28.md`

- [ ] **Step 1: 基线每请求数据库往返**

在 Task 20 下按 JWT/API Key、成功/撤销/禁用/租户停用统计 Statement 次数与 P95/P99，
单独量化 API Key `LastUsed` 节流更新。

- [ ] **Step 2: 冻结撤销与故障语义**

明确最大撤销传播窗口、Redis 不可用行为和本机缓存上限。安全缓存关闭 Fail-Safe；缓存或
Backplane 故障时必须 fail-closed，不能仅依赖陈旧 L1。

- [ ] **Step 3: RED 后实现最小 FusionCache 候选**

覆盖写后本机同步失效、Outbox/Backplane 跨节点失效、Redis 故障、并发撤销和租户状态变化；
复跑混合负载，收益不足或安全语义退化则回退候选。

### Task 26: Jobs 有界并发与容量预算

**Priority:** P1

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobsWorkerOptions.cs`
- Modify: `src/Modules/Full.NET.Modules.Jobs/Execution/JobExecutionRunner.cs`
- Modify: `tests/Full.NET.UnitTests/Jobs/JobExecutionRunnerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Jobs/JobsActiveLeaseRenewalAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Jobs/JobsMultiWorkerClaimAssertions.cs`
- Create: `docs/verification/jobs-bounded-concurrency-2026-07-28.md`

- [ ] **Step 1: 冻结顺序键、Scope 与连接预算**

默认并发保持 `1`；并发仅对无相同 `JobKey` 顺序约束的执行启用，每条执行独立 Scope 和
数据库会话，租约续期覆盖批尾，连接预算按进程并发乘副本数计算。

- [ ] **Step 2: 建立双库 RED 并实现有界并发**

用闸门 Handler 验证峰值、Scope 隔离、续租、单条失败隔离和多 Worker 无重复终态；禁止
无界 `Task.WhenAll`。

- [ ] **Step 3: 容量矩阵决定是否启用**

双库比较并发 `1/2/4/8`、慢 Handler、持续积压和多副本；无真实收益或锁/连接池退化时
保留默认 `1`。

### Task 27: 前端真实用户性能

**Priority:** P0（开发反馈）

**Status:** 本地受影响测试选择器已于 2026-07-29 落地；本地任务禁止运行 199 项
全量，完整门禁只由 `main` CI 并行分片执行。数据库模板 A/B 与 main 分片耗时再平衡
仍待后续证据。

**Files:**
- Create: `tests/performance/admin-real-user-performance.test.mjs`
- Create: `tests/performance/admin-layui-real-user-performance.test.mjs`
- Create: `tests/performance/network-profiles.json`
- Modify: `package.json`
- Modify: `.github/workflows/ci.yml`
- Create: `docs/verification/frontend-real-user-performance-2026-07-28.md`

- [ ] **Step 1: 建立真实网络/设备矩阵**

Vue/Layui 分别覆盖冷缓存、Brotli、CDN 等价缓存头、Fast/Slow 4G、低端 CPU；记录
FCP/LCP/INP/DOMContentLoaded/Load、字体和 CSS 瀑布，不再只看单 JS chunk。

- [ ] **Step 2: 用瀑布证据选择候选**

仅对真实阻塞资源实施 preload、字体子集、CSS 拆分或缓存策略；每个候选单变量 A/B。
若 FCP/LCP 未改善，不因 Load 下降而宣称首屏成功。

- [ ] **Step 3: 冻结分层预算**

把稳定的本地合成预算加入 CI；生产 RUM 只记录接入和告警设计，未获得真实流量前不伪造
百分位。

### Task 28: Integration 反馈速度与隔离门禁

**Priority:** P2

**Files:**
- Modify: `scripts/testing/run-integration-shard.mjs`
- Modify: `scripts/testing/analyze-trx-durations.mjs`
- Modify: `.github/workflows/ci.yml`
- Modify: `tests/testing/integration-sharding.test.mjs`
- Create: `docs/verification/integration-feedback-speed-2026-07-28.md`

- [x] **Step 1: 用 TRX 锁定最慢用例**

基于新鲜 191 项 TRX 记录累计与墙钟时间，优先分析 MySQL Seed、Outbox、命名迁移演练；
禁止以跳过或共享污染数据库换取速度。

现行 canonical 已为 199 项。Task 21 全量墙钟为 `36m08s`；当前 Auditing 双库影响集
为 **8/8**。仓库已新增基于任务 Git 基线的 `test:integration:affected`，
将模块、共享能力、Smoke、migrations 与 tooling 映射为本地影响集。

- [ ] **Step 2: A/B 初始化与复用策略**

比较容器复用、迁移快照、数据库模板和测试类共享；迁移恢复、失败回滚和并发隔离用例必须
保持独立。每个候选验证测试发现数、数据清理和失败可诊断性。

- [ ] **Step 3: 调整分片并冻结回归**

以历史耗时平衡 SQL Server/MySQL/API/迁移/基础设施分片，目标是降低 PR 最慢分片墙钟，
同时保持 main 全量 199 项、失败 0、跳过 0 和独立恢复用例。

## Execution Order and Stop Conditions

1. 立即执行 Task 19；它是已确认的可靠性缺口，不等待容量基线。
2. Task 19 通过并提交后执行 Task 20，冻结后续所有优化的共同证据。
3. Task 21、22、23 按顺序处理 Audit；任何可靠性语义不清时停止实现，只保留基线。
4. Task 24、25、26 必须分别通过双库容量矩阵，不能用单库或平均值提高默认并发/缓存。
5. Task 27、28 可在后端 P0 完成后推进；不得挤占 Audit/Outbox 正确性工作。
6. 任一候选若只改善平均值、却让 P95/P99、错误率、锁等待、连接池或可靠性退化，则拒绝
   落入生产，并在验证文档记录否定证据。
