# Full.NET High-Concurrency Multi-Instance Implementation Plan

> **Execution handoff（Cursor/Codex）：** 本计划已经批准。执行工具必须先读取根 `AGENTS.md`、适用规则和项目 Skill。`fullnet-high-concurrency-implementation-plan-20260801` 只作为整项实施的项目基线，供 Task 15 汇总影响集；Task 1～14 每项开始时必须创建独立 `<task-snapshot>`，避免后续 inner/slice 不断重跑此前全部任务。当前计划形成时 `main` 工作区已脏；推荐先由项目所有者把已批准设计封板改动保存为一个可追溯基线，再在专用功能分支/工作树实施。若仍在脏工作区执行，每次只允许完成一个 Task，禁止自动暂存、提交、覆盖或格式化无关改动。

**Goal:** 把已批准的“强化型模块化单体、多实例运行”生产设计落成可验证的缓存、Audit/日志、共享状态、对象存储、Realtime、Kubernetes 交付和专用硬件容量认证能力，同时在容量认证完成前始终保持 `Capacity-not-verified`。

**Architecture:** API、Worker、Migrator 继续保持同一模块化单体代码库内的运行角色分离；API/Worker 可多副本运行，数据库是业务事实源，FusionCache 是唯一缓存实现，缓存失效走提交后直接 L1/L2 删除与 Redis Backplane，Outbox 只承载重要业务 Integration Event。B0 Audit 与业务事务同提交，B1 Audit 采用请求等待结果的有界跨请求微批，B2 普通 HTTP Operation Log 进入有界结构化日志管道。生产由 Kubernetes + Helm 承载，共享 Data Protection Key Ring、S3 兼容对象存储、Cache Redis 与 Realtime Redis 分离，并以数据库连接预算约束扩容。

**Tech Stack:** .NET 10、ASP.NET Core、Dapper、SQL Server、MySQL、FusionCache 2.6、StackExchange.Redis、Serilog、OpenTelemetry、AWS SDK for .NET S3 4.0.101.4、MSTest、Testcontainers、Kubernetes、Helm 3、Fluent Bit、OpenTelemetry Collector、k6、Vue、Layui、pnpm 10。

**Global Constraints:**

- 权威设计为 `docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md`、`docs/architecture/adr/ADR-0005-high-concurrency-modular-monolith-multi-instance-production-baseline.md` 与 `docs/verification/high-concurrency-modular-monolith-multi-instance-assessment-2026-08-01.md`；实现不得反向修改其可靠性语义。
- 当前基线提交为 `e2ee63c8a925592339b16953867b6da5a504a339`，工作区已脏；每个任务开始前运行 `git status --short`，只修改该任务列出的文件。
- 行为变更必须先写会失败的测试并记录 RED 原因，再写最小实现转 GREEN；数据库行为必须同时验证 SQL Server/MySQL。
- 不引入 EF Core、通用 Repository、第二套缓存、缓存 Outbox、日志 Outbox 或 Audit Outbox。
- 开发机只验证正确性、边界和相对性能，不把 2K/5K/10K 压测设为开发完成门禁；Task 14 专用容量认证完成前不得移除 `Capacity-not-verified`。
- 新公共标识符、配置键、缓存键、日志字段和数据库对象遵循 `rules/naming-conventions.md`；手写代码注释使用中文并解释边界或风险。
- 本地只运行 affected selector 命中的 Integration 集；完整矩阵留给 `main` CI 的互斥分片门禁。
- 各 Task 内的 `git add`/`git commit` 代码块只表示建议提交范围和消息，不构成自动提交授权；Cursor 默认在验证后停止并报告。只有用户明确要求提交时，才可在确认 `git diff --cached` 仅含当前 Task 后执行。
- 不并行执行会修改同一文件、迁移序号、公共契约或同一数据库对象的 Task；迁移任务开始时重新确认 SQL Server/MySQL 的下一个成对编号，若 `049/050` 已被占用必须先修订计划，禁止覆盖已发布迁移。
- Task 1～14 每项开始时运行 `pnpm test:task:start -- fullnet-hc-task-<NN>-<date>`，并把本 Task 命令中的 `<task-snapshot>` 替换为实际 ID；Task 15 才使用项目基线快照汇总整个实施影响集。若已在干净独立分支执行，可按规则使用明确的任务 base SHA 代替 Task 快照。

## Cursor 优先执行顺序与依赖

Cursor 应严格按下列顺序推进，每次会话只处理表中一个 Task，完成验证并报告后等待下一次指令。不得从 Task 14 容量脚本倒推修改业务可靠性语义。

| 优先级 | 执行项 | 前置依赖 | 本阶段退出条件 |
|---|---|---|---|
| Gate-0 | 基线保护与执行预检 | 无 | 已确认分支、HEAD、任务快照、脏文件归属；未获提交授权时保持 index 不变 |
| P0 | Task 1：修正模块交付 Skill | Gate-0 | 两个项目 Skill 契约通过，旧缓存 Outbox 指引消失 |
| P1-A | Task 2 -> Task 3 Expand/Cutover -> Task 4：缓存分类、Tenancy 直接失效、多实例故障证据 | Task 1 | 缓存条目可治理；Tenancy 不再产生新的缓存 Outbox，兼容 Handler 保留至存量排空；双实例与故障边界有证据 |
| P1-B | Task 5 -> Task 6 -> Task 7：模块内 B0、B1 微批、B2 HTTP Operation Log | Task 2 | B0/B1/B2 语义可执行；B1 固定 fail-open + 告警；普通请求不逐条写业务主库 |
| P1-C | Task 9 -> Task 10 -> Task 11：Data Protection、S3、Redis/SignalR 隔离 | Task 2 | API/Worker 不依赖节点本地密钥/文件；Cache 与 Realtime 故障域分离 |
| P1-D | Task 8：限时动态诊断与双管理端 | Task 2、5、7、11 | 模块内 B0 Audit、S1 策略传播、TTL/限额和双端权限/E2E 全部成立 |
| P2-A（生产发布 P0 门禁） | Task 12：容器与 Helm | Task 9、10、11 | 三镜像实际构建；发布顺序、连接预算、Edge 全局限流不随副本放大、探针、PDB、拓扑和安全合同通过 |
| P2-B | Task 13：采集、告警与恢复 Runbook | Task 6、7、8、12 | B2/Priority 采集与 B0/B1 数据库边界不混淆，恢复和告警合同通过 |
| P3 | Task 14：容量认证套件 | Task 4、6、7、10、11、12、13 | 只完成可执行套件和静态合同，仍保持 `Capacity-not-verified` |
| P4 | Task 15：全链路验收记录 | Task 1～14 | affected slice/merge、Release 构建和治理验证完成；未做专用容量认证时不得宣称 10K |

每个 Task 的 Cursor 输出固定包含：修改文件、RED 证据、GREEN 证据、实际命令与结果、未执行项、风险/回滚点、`git status --short`；存在 Critical/Important 审查问题时不得自动进入下一 Task。

每个实现 Task 的 Integration 验证必须对实际 phase 严格执行 `pnpm test:integration:affected:plan ...`、人工审查命中集、再执行同 snapshot/phase 的 `pnpm test:integration:affected ...`；不得以 plan 代替测试。涉及 SQL/迁移的 Task 必须确认选择器命中 SQL Server/MySQL 聚焦及恢复集合。Task 结束前还要执行受影响项目的 fresh Release build、`git diff --check` 和 `git status --short`；环境缺少 Docker、Testcontainers、前端浏览器或其他基础设施时必须保持“未验证”，不得提升完成状态。

## 全任务通用：高并发与高性能开发门禁

以下要求适用于 Task 1～15。实现者必须在每个任务的 Verification 中记录受影响的请求/Worker 热路径、资源预算和实际验证证据；没有专用硬件时只声明相对回归与正确性，不声明容量达标。

1. **全链异步**：请求、数据库、Redis、HTTP、对象存储和日志 I/O 禁止 `.Result`、`.Wait()`、同步 Body 读写或“`Task.Run` + 立即 await”；所有可取消操作传播 `CancellationToken`，超时与调用方取消可区分。
2. **所有资源有界**：队列、并发、连接、批次、正文、响应、重试、超时、缓存回源和关闭排空都有显式上限及满载策略；禁止无界 `Task.WhenAll`、无限 Channel、无限重试或靠排队隐藏过载。
3. **请求链预算**：逐条列出数据库/Redis/HTTP/文件/日志往返数，消除 N+1、重复序列化和非必要串行等待；Middleware/Filter/授权/日志等每请求代码保持短小，但安全、租户、Trace、异常处理和应有 Audit 不得被条件分支绕过。
4. **数据库预算**：Dapper 只取所需列，稳定分页，事务保持最短；连接及时释放；API/Worker/Migrator 与故障保留按全体 Pod 汇总连接池。Statement 变化必须同时取得 SQL Server Query Store/执行计划和 MySQL `EXPLAIN ANALYZE` 证据。
5. **下游 HTTP 预算**：复用 `IHttpClientFactory` 或合规长生命周期 Client；禁止每请求新建 Client、错误捕获 Typed Client 到 Singleton。每个下游声明总超时、连接预算、协议策略、DNS 更新和仅限幂等瞬时失败的有限重试；`MaxConnectionsPerServer` 不使用经验公式直接放大。
6. **内存与 GC**：限制大请求/响应并优先流式；禁止生产 `GC.Collect`。只有分配/GC/Trace 证明热点时才引入 `ArrayPool<T>`、`Span<T>`、Pipelines 或源生成序列化；Pool 必须测试异常路径、单一所有权、敏感数据清零和归还后不可再用。
7. **缓存和并发语义不退化**：继续服从 C0/S0-L2/S1/S2/N0、FusionCache、B0/B1/B2、Outbox 准入和租户隔离；性能优化不得新增第二套缓存、绕过权威读取、降低 Audit 可靠性或改变双库语义。
8. **低基数可观测性**：至少可观察 active requests、RPS、P95/P99、错误/拒绝、ThreadPool queue/thread count、分配率、GC pause/Gen2、Socket/HttpClient、数据库连接等待/计划、Redis、日志/Audit 队列和 Worker backlog；Metrics 禁止 TenantId/UserId/TraceId/原始 URL 等高基数属性。
9. **证据优先**：先用 Benchmark、`dotnet-counters`、Trace、双库 Integration 或代表性小流量复现瓶颈，再做优化；不能以理论微秒数、第三方网关 QPS 或本地空接口结果作为 Full.NET 收益。复杂低级优化若没有稳定收益、正确性测试和回退条件，不准合入。
10. **评审交付**：每个任务明确负载形状、最坏 Payload/扇出、队列满行为、资源预算、失败语义和证据等级；所有阈值由 SLO/工作负载或 Task 14 容量记录给出，不制造“官方通用默认值”。

---

## Task 1：修正模块交付 Skill 的缓存一致性契约

**Files:**

- Modify: `.agents/skills/fullnet-module-delivery/SKILL.md`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- Modify: `tests/skills/validate_project_skills.py`
- Modify: `tests/skills/fullnet-module-delivery.contract.json`

1. 先为通用 Skill 验证器增加契约级和 scenario 级 `forbidden_terms` 支持，再在模块交付契约中加入反例，禁止“缓存失效由 Outbox 触发”等旧指引，并要求“缓存失效不写 Outbox”“事务提交后直接删除 L1/L2 并广播 Backplane”“重要业务事件才允许 Outbox”。此时暂不修改 Skill 正文，先运行并确认旧提示导致 RED：

   ```powershell
   pnpm test:skills
   ```

   预期：`fullnet-module-delivery` 合同因仍建议缓存 Outbox 而失败，`fullnet-performance-hardening` 保持通过。

2. 把模块交付流程改成以下不可变判断，且不复制性能 Skill 的整篇说明：

   ```text
   Cache invalidation: commit database state -> remove current L1/shared L2 -> publish Backplane.
   Outbox admission: only durable, transaction-coupled business Integration Events with idempotent consumers.
   ```

3. 更新 `delivery-map.md`，把缓存条目强制分类为 `C0/S0-L2/S1/S2/N0`，并引用 ADR-0005 与总体 Spec §13，不再保留旧 Handler 示例。

4. 为 `forbidden_terms` 验证器增加自身的正反例覆盖，确保未来新增 Skill 或 scenario 可复用且不会把“禁止缓存 Outbox”这类正确语句误判为违规。运行 `pnpm test:skills`，预期两个项目 Skill 全部通过；运行 `rg -n "缓存.*Outbox|Outbox.*缓存" .agents/skills/fullnet-module-delivery`，预期只出现“禁止”语义。

5. 建议提交范围（仅在用户明确授权后执行）：

   ```powershell
   git add .agents/skills/fullnet-module-delivery tests/skills/validate_project_skills.py tests/skills/fullnet-module-delivery.contract.json
   git commit -m "docs(skills): align cache invalidation delivery contract"
   ```

## Task 2：建立可执行的缓存分类与条目注册表

**Files:**

- Create: `src/BuildingBlocks/Full.NET.Caching.Fusion/CacheConsistencyClass.cs`
- Create: `src/BuildingBlocks/Full.NET.Caching.Fusion/CacheEntryPolicy.cs`
- Create: `src/BuildingBlocks/Full.NET.Caching.Fusion/CachePolicyRegistry.cs`
- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/CacheOptions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/ServiceCollectionExtensions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/CacheReliabilityTelemetry.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenantCacheInvalidator.cs`
- Modify: `tests/Full.NET.UnitTests/Caching/FusionCacheRegistrationTests.cs`
- Create: `tests/Full.NET.UnitTests/Caching/CachePolicyRegistryTests.cs`
- Create: `tests/Full.NET.ArchitectureTests/CachePolicyBoundaryTests.cs`

1. 先写 RED 测试覆盖：未知条目启动失败；S0-L2 关闭 L1；C0 禁用 Fail-Safe 并要求 Authority 行为；S1 具备短 L1 TTL、L2 TTL/Jitter 和直接失效声明；S2 只有显式配置才允许 Fail-Safe；N0 不得解析为缓存策略。

2. 增加以下稳定类型，不让业务模块直接拼装任意 `FusionCacheEntryOptions`：

   ```csharp
   public enum CacheConsistencyClass
   {
       AuthorityCritical,
       SharedL2Only,
       ImportantBusiness,
       DegradableDisplay,
       NotCached,
   }

   public sealed record CacheEntryPolicy(
       string EntryName,
       string OwnerModule,
       CacheConsistencyClass ConsistencyClass,
       TimeSpan L1Duration,
       TimeSpan L2Duration,
       TimeSpan Jitter,
       TimeSpan? NegativeDuration,
       bool FailSafeEnabled,
       bool RequiresVersionRecheck,
       int MaxSerializedBytes);

   public interface ICachePolicyRegistry
   {
       CacheEntryPolicy GetRequired(string entryName);
       CacheAccessDecision ResolveAccess(string entryName);
       FusionCacheEntryOptions CreateEntryOptions(string entryName);
   }
   ```

3. 在 `CacheOptions` 增加 `Entries` 字典并绑定 `Cache:Entries`；默认只注册当前租户解析条目 `tenancy.tenant-resolution` 为 S1，不在框架中为所有业务强塞同一 TTL。S0-L2 通过 FusionCache 的 `SkipMemoryCache` 选项关闭 L1；C0/N0 必须由 `ResolveAccess` 返回不可混淆的 `AuthorityRead`/`Bypass` 决策，且对这两类调用 `CreateEntryOptions` 必须失败，禁止让业务调用方自行猜测绕过语义。

4. 把现有 `TenantCacheInvalidator` 的手工 `FusionCacheEntryOptions` 迁移到注册表。新增 Architecture 扫描：业务模块不得直接 `new FusionCacheEntryOptions`、配置任意缓存选项或绕过注册表直连 L2；只允许 Caching BuildingBlock 内部和列入窄白名单的兼容适配器。测试必须枚举现有调用点，白名单包含原因和移除任务，不允许空泛目录豁免。

5. 为命中、绕过、版本不符、fail-closed、负缓存和序列化超限增加低基数指标标签：仅 `owner_module`、`consistency_class`、`operation`、`result`，禁止完整 Key/TenantId/UserId。

6. 运行：

   ```powershell
   dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~Caching"
   dotnet test tests/Full.NET.ArchitectureTests/Full.NET.ArchitectureTests.csproj -c Release --filter "FullyQualifiedName~CachePolicyBoundary"
   pnpm test:integration:affected:plan -- --snapshot <task-snapshot> --phase slice
   pnpm test:integration:affected -- --snapshot <task-snapshot> --phase slice
   ```

   预期：缓存注册、策略与架构测试全部通过，selector 命中并通过 Caching/Tenancy 及其 SQL Server/MySQL 影响集；执行 `git diff --check` 后提交：

   ```powershell
   git add src/BuildingBlocks/Full.NET.Caching.Fusion src/Modules/Full.NET.Modules.Tenancy/TenantCacheInvalidator.cs tests/Full.NET.UnitTests/Caching tests/Full.NET.ArchitectureTests/CachePolicyBoundaryTests.cs
   git commit -m "feat(cache): add governed consistency policies"
   ```

## Task 3：把 Tenancy 缓存迁移为提交后直接失效

**Files:**

- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenantCacheInvalidator.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Features/ManageHostTenants/HostTenantManagementService.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Features/ProvisionTenant/TenantProvisioningService.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Features/ProvisionTenant/Handler.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenantChangedCacheInvalidationHandler.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenantProvisionedCacheInvalidationHandler.cs`
- Modify: `tests/Full.NET.UnitTests/Tenancy/HostTenantCacheInvalidationTests.cs`
- Modify: `tests/Full.NET.UnitTests/Tenancy/TenantProvisionedCacheInvalidationHandlerTests.cs`
- Modify: `tests/Full.NET.UnitTests/Tenancy/TenantChangedCacheInvalidationHandlerTests.cs`
- Create: `tests/Full.NET.UnitTests/Tenancy/TenantCacheInvalidatorTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Tenancy/TenancyHostTenantManagementAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Caching/CacheConsistencyTests.cs`

1. RED：更新、禁用、开通租户成功后断言当前 L1 与共享 L2 都删除、另一 FusionCache 实例收到 Backplane 后清理 L1，且 Outbox 计数不增加；事务回滚时断言不失效。

2. 把失效入口收敛为一个公开语义清楚的方法：

   ```csharp
   internal sealed class TenantCacheInvalidator(
       IFusionCache cache,
       IHostEnvironment environment,
       ICachePolicyRegistry policies)
   {
       public Task InvalidateAfterCommitAsync(
           Guid tenantId,
           string domain,
           CancellationToken cancellationToken);
   }
   ```

   该方法使用 `AllowBackgroundDistributedCacheOperations = false` 和 `AllowBackgroundBackplaneOperations = false`；重复删除必须无副作用，失败要记录结构化 Warning 与可靠性指标，但不得补写 Outbox。

3. **Expand/Cutover 发布**：`HostTenantManagementService` 与开通流程只在 `ICommandTransaction.ExecuteAsync` 成功返回后调用 `InvalidateAfterCommitAsync`，并停止产生新的缓存专用 Outbox 消息；但必须保留旧消息类型、反序列化契约、两个兼容 Handler 及 Worker 注册。兼容 Handler 继续执行同一个幂等 `TenantCacheInvalidator`，使升级前已入库的旧消息可安全排空；若开通事件还有重要业务消费者，则保留该业务事件并只停止新增的缓存失效语义。

4. 保留旧 Handler 测试并改成兼容排空测试；新增 `TenantCacheInvalidatorTests` 和直接失效失败注入测试：Redis 删除失败、Backplane 发布失败、提交后进程终止都验证由 TTL/版本/Authority 边界收敛，不出现新的缓存 Outbox 记录。验证新旧版本滚动并存时，新版本不产生旧消息且旧 Handler 可重复消费存量消息。

5. 运行 Tenancy Unit，并对 slice phase 执行 affected 计划、审查、执行：

   ```powershell
   dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~Tenancy"
   pnpm test:integration:affected:plan -- --snapshot <task-snapshot> --phase slice
   pnpm test:integration:affected -- --snapshot <task-snapshot> --phase slice
   ```

   预期：Unit 全绿，selector 命中并通过 Tenancy/Caching/Outbox 相关分片。建议提交范围（仅在用户明确授权后执行）：

   ```powershell
   git add src/Modules/Full.NET.Modules.Tenancy tests/Full.NET.UnitTests/Tenancy tests/Full.NET.IntegrationTests/Tenancy tests/Full.NET.IntegrationTests/Caching
   git commit -m "refactor(tenancy): invalidate cache directly after commit"
   ```

### Task 3 Contract Gate：存量消息排空后的独立收缩发布

此 Gate 不是首次 Cursor 实施批次的一部分。只有 Expand/Cutover 版本已部署到全部环境、旧版本已退出、对应 Outbox `Pending/Processing/DeadLetter` 按稳定消息类型统计连续观察窗为 0，并完成备份与回滚确认后，才创建独立 Task snapshot 和独立发布变更，删除旧事件类型、兼容 Handler、注册及测试。收缩前先保存各环境排空证据；任一环境不为 0 都必须保留兼容代码。回滚时重新部署含兼容 Handler 的前一版本，不恢复新消息生产。

## Task 4：补齐多实例缓存故障与防击穿证据

**Files:**

- Modify: `tests/Full.NET.IntegrationTests/Caching/CacheConsistencyTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Caching/CacheConsistencyClassAssertions.cs`
- Modify: `tests/Full.NET.UnitTests/Caching/CacheReliabilityTelemetryTests.cs`
- Modify: `benchmarks/Full.NET.Benchmarks/MixedLoad/MixedLoadScenario.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Caching/CacheRecoveryBenchmarks.cs`

1. RED：以两个独立 FusionCache InstanceId 覆盖 S0-L2 不回填 L1、S1 正常广播、丢失一次 Pub/Sub 后 L1 TTL 收敛、L2 删除失败、提交后进程终止、Redis 冷启动和高基数不存在 Key。

2. 使用 FusionCache 同 Key 请求合并作为默认防击穿机制；只在基准证明确有跨实例昂贵热点回源放大时，才允许在该条目策略上启用有租约、等待上限和持有者校验的分布式回填锁。首版测试明确断言未配置热点锁时不创建全局 `SemaphoreSlim` 或 Redis 锁。

3. 基准输出至少包含 `l1_hit`、`l2_hit`、`factory_call`、`merged_waiter`、`invalidation_ms`、`stale_window_ms`、`redis_failure_db_amplification`，并保持标签低基数。

4. 运行：

   ```powershell
   dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~CacheReliabilityTelemetry"
   pnpm test:integration:affected:plan -- --snapshot <task-snapshot> --phase slice
   pnpm test:integration:affected -- --snapshot <task-snapshot> --phase slice
   dotnet build benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release
   ```

   预期：双库/Redis 影响集通过，Benchmark 只要求可构建，不声明 10K 容量结果。

5. 提交：

   ```powershell
   git add tests/Full.NET.IntegrationTests/Caching tests/Full.NET.UnitTests/Caching benchmarks/Full.NET.Benchmarks
   git commit -m "test(cache): prove multi-instance recovery boundaries"
   ```

## Task 5：建立 B0/B1/B2 Audit 可靠性目录和模块内 B0 同事务接口

**Files:**

- Create: `src/BuildingBlocks/Full.NET.Abstractions/Auditing/AuditReliabilityClass.cs`
- Create: `src/BuildingBlocks/Full.NET.Abstractions/Auditing/ITransactionalDomainAuditWriter.cs`
- Create: `src/Modules/Full.NET.Modules.Auditing/AuditReliabilityCatalog.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Features/ManageHostTenants/TenancyDomainAuditWrite.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenancyDomainAuditWriter.cs`
- Create: `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenancyDomainAuditSql.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/049_TenancyDomainAudit.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/049_TenancyDomainAudit.sql`
- Modify: `src/Modules/Full.NET.Modules.Auditing/AuditingModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy/Features/ManageHostTenants/HostTenantManagementService.cs`
- Modify: `tests/Full.NET.UnitTests/Tenancy/HostTenantCacheInvalidationTests.cs`
- Create: `tests/Full.NET.UnitTests/Auditing/AuditReliabilityCatalogTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Tenancy/TenancyDomainAuditTransactionAssertions.cs`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration049TenancyDomainAuditRecoveryTests.cs`

1. RED：B0 与业务写入同提交/同回滚；B1 固定为请求等待写入尝试、失败后 fail-open + 告警，任何要求 fail-closed 的动作必须归类为 B0；B2 不得解析数据库 Writer；未知 Endpoint/ActionKey 启动失败；领域 Audit 必须写模块所属表，禁止汇总到 `fn_auditing_domain_audit` 通用热表。

2. 增加稳定接口：

   ```csharp
   public enum AuditReliabilityClass { DomainTransactional, ImportantHttp, BestEffort }

   public interface ITransactionalDomainAuditWriter<in TAudit>
   {
       Task WriteAsync(TAudit audit, CancellationToken cancellationToken);
   }
   ```

   `TenancyDomainAuditWriter` 实现该接口并复用当前 `ICommandExecutor`，因此当调用方位于 `ICommandTransaction` 内时共享同一 `DbSession.Transaction`；它不得启动第二事务或写 Outbox。公共接口只约束事务参与方式，不接收任意模块名/表名，也不实现跨模块通用实体 JSON 表。

3. 049 双库迁移创建模块所属 `fn_tenancy_domain_audit`，UUID v7 `Guid` 主键、稳定 ActionKey/EntityId/Outcome、TraceId、Actor、脱敏差异摘要与 OccurredAtUtc，并按 `(OccurredAtUtc, Id)`、`(TenantId, OccurredAtUtc, Id)` 建立查询/保留索引；恢复测试覆盖空库、完整重跑和可恢复的半完成对象，不修改已发布迁移。后续模块使用同一事务参与接口，但各自拥有表、SQL、迁移和保留策略。

4. 先为 host 租户禁用接一个 B0 示例，证明业务行和 Audit 行同事务；Task 8 再用同一接口覆盖诊断安全配置变更。禁止把普通 GET 或每请求 Access 升级为 B0。

5. 运行迁移与 Audit 影响集，预期 SQL Server/MySQL 均通过后提交：

   ```powershell
   pnpm test:naming
   pnpm test:sql-safety
   pnpm test:integration:affected:plan -- --snapshot <task-snapshot> --phase slice
   pnpm test:integration:affected -- --snapshot <task-snapshot> --phase slice
   git add src/BuildingBlocks/Full.NET.Abstractions/Auditing src/Modules/Full.NET.Modules.Auditing src/Modules/Full.NET.Modules.Tenancy src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations tests/Full.NET.UnitTests/Auditing tests/Full.NET.UnitTests/Tenancy/HostTenantCacheInvalidationTests.cs tests/Full.NET.IntegrationTests/Tenancy tests/Full.NET.IntegrationTests/Migrations/Migration049TenancyDomainAuditRecoveryTests.cs
   git commit -m "feat(auditing): add transactional domain audit boundary"
   ```

## Task 6：把重要 HTTP Audit 改为请求等待的跨请求有界微批

**Files:**

- Create: `src/Modules/Full.NET.Modules.Auditing/Features/WriteAuditBatch/AuditMicroBatchOptions.cs`
- Create: `src/Modules/Full.NET.Modules.Auditing/Features/WriteAuditBatch/AuditWriteEnvelope.cs`
- Create: `src/Modules/Full.NET.Modules.Auditing/Features/WriteAuditBatch/AuditMicroBatchCoordinator.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/WriteAuditBatch/AuditWriteBuffer.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/WriteAuditBatch/AuditWriteBatchWriter.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/WriteAuditBatch/AuditWriteBatchSql.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Features/WriteOutboundCallLogs/OutboundCallAuditHandler.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/Middleware/AuditWriteCoordinatorMiddleware.cs`
- Modify: `src/Modules/Full.NET.Modules.Auditing/AuditingModule.cs`
- Modify: `src/Hosts/Full.NET.Host.Api/appsettings.json`
- Modify: `tests/Full.NET.UnitTests/Auditing/AuditingWritePathTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Auditing/AuditingBatchRollbackAssertions.cs`
- Create: `benchmarks/Full.NET.Benchmarks/Auditing/AuditMicroBatchBenchmarks.cs`

1. RED 覆盖：64 行或 256 KiB 或 20 ms 任一达到即排空；有界队列 4096；同批每张表一条参数化多行命令并共享一次连接/事务；Operation/Exception 请求必须等待自身批次结果；Outbound 按调用契约等待；数据库失败整批回滚；毒记录二分隔离后其余记录可提交；队列满默认 fail-open + 指标/告警；取消请求不取消最终持久化尝试；停机最多等待 5 秒。Access 不进入 B1。

2. 配置采用保守默认值并允许部署覆盖：

   ```csharp
   public sealed class AuditMicroBatchOptions
   {
       public const string SectionName = "Auditing:MicroBatch";
       public int Capacity { get; set; } = 4096;
       public int MaxBatchRows { get; set; } = 64;
       public int MaxBatchBytes { get; set; } = 262144;
       public TimeSpan MaxBatchDelay { get; set; } = TimeSpan.FromMilliseconds(20);
       public TimeSpan EnqueueTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
       public TimeSpan ShutdownFlushTimeout { get; set; } = TimeSpan.FromSeconds(5);
   }
   ```

   B1 不暴露 `FailClosed` 配置。若 Endpoint 要求“审计失败则业务失败”，必须在可靠性目录中改为 B0，并在业务事务内写模块领域 Audit。

3. `AuditMicroBatchCoordinator : BackgroundService` 持有 `Channel<AuditWriteEnvelope>`；Middleware 把请求内 Operation/Exception 快照入队并等待 `TaskCompletionSource<AuditWriteResult>`，Outbound Handler 按契约入队。后台每批创建一个 DI scope，按 Operation/Exception/Outbound 生成兼容双库的多值 `INSERT`，严格控制 SQL Server 参数数量、MySQL packet 字节和分块边界，所有表组共享一次 `ICommandTransaction.ExecuteAsync`。Access 从请求 Audit Buffer 中移出，转由 Task 7 的 B2 HTTP Operation 流承担。

4. 队列满、批次失败、毒记录、停机丢弃记录 `accepted/rejected/flushed/failed/poisoned/wait_ms/batch_rows/batch_bytes`；指标不得含 TraceId/UserId/TenantId。B1 失败不得降级写 Outbox。

5. 运行 Audit Unit、双库 Integration 与 Benchmark build；对比旧“每请求一命令”和新微批的命令数、连接租用与 P99，比较结果只作为相对回归证据：

   ```powershell
   dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~Auditing"
   pnpm test:integration:affected:plan -- --snapshot <task-snapshot> --phase slice
   pnpm test:integration:affected -- --snapshot <task-snapshot> --phase slice
   dotnet build benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release
   git add src/Modules/Full.NET.Modules.Auditing src/Hosts/Full.NET.Host.Api/appsettings.json tests/Full.NET.UnitTests/Auditing tests/Full.NET.IntegrationTests/Auditing benchmarks/Full.NET.Benchmarks/Auditing
   git commit -m "feat(auditing): batch important http audits across requests"
   ```

## Task 7：实现普通 HTTP Operation Log、逻辑分组和六档 Profile

**Files:**

- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/HttpOperationLogOptions.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/HttpOperationLogProfile.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/HttpOperationLogMiddleware.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/HttpOperationLogSanitizer.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/LogClassification.cs`
- Modify: `src/BuildingBlocks/Full.NET.Hosting/Observability/ServiceDefaultsExtensions.cs`
- Modify: `src/Hosts/Full.NET.Host.Api/Program.cs`
- Modify: `src/Hosts/Full.NET.Host.Api/appsettings.json`
- Modify: `src/Modules/Full.NET.Modules.Auditing/AuditingModule.cs`
- Delete: `src/Modules/Full.NET.Modules.Auditing/Middleware/AccessLogMiddleware.cs`
- Delete: `src/Modules/Full.NET.Modules.Auditing/Features/WriteAccessLogs/AccessLogWriter.cs`
- Modify: `tests/Full.NET.UnitTests/Auditing/AuditingWritePathTests.cs`
- Create: `tests/Full.NET.UnitTests/Hosting/HttpOperationLogTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Api/HttpRequestLoggingTests.cs`

1. RED：每请求最多一条完成事件；`Disabled/Summary/SanitizedPayload`；确定性成功采样；错误/慢请求优先保留；URL、Method、Route、StatusCode、ElapsedMs、SourceUrl、TraceId、ClientIpFingerprint 可查；Payload 只按 Route 白名单投影；密码、Token、Cookie、签名、连接串、CR/LF 和超长/深层结构被脱敏或截断；关闭 Operation Log 不关闭 Metrics、Error/Critical 或 B0/B1 Audit。

2. 定义稳定字段与枚举：

   ```csharp
   public enum HttpOperationCaptureMode { Disabled, Summary, SanitizedPayload }
   public enum LoggingCapacityProfile { S, M, L, XL, XXL, Ultra }
   public enum LoggingPressureState { Normal, Degraded, Critical }

   public static class LogClassification
   {
       public const string HttpOperation = "http.operation";
       public const string Diagnostic = "diagnostic";
       public const string Security = "security";
   }
   ```

   每条事件必须设置 `log.class`、`log.stream`、`reliability.class`、`data.classification`、`DiagnosticGroup`、`EventName`；这些字段只来自受治理目录，不能由请求参数动态创建。

3. 六档 Profile 映射 S `[0,1K)`、M `[1K,5K)`、L `[5K,10K)`、XL `[10K,50K)`、XXL `[50K,100K)`、Ultra `>=100K`，因此恰好 10K 属于 XL。Profile 只选择候选采样/队列/字节预算，面向 10K 目标的初始参考为 XL；部署最终选择必须同时依据经认证的事件/秒、字节/秒和后端预算，瞬时并发变化不得自动切换可靠性语义，最终数值留给 Task 14 冻结。

4. 用新 Middleware 替代 `UseSerilogRequestLogging` 的重复 Access 摘要，并从 Auditing 请求管道移除全量 `AccessLogMiddleware/AccessLogWriter`；保留现有 Access 表的查询和保留兼容，但生产默认不再逐请求写业务主库。成功请求按 RouteKey+TraceId 确定性采样；5xx、未处理异常和超过 `SlowRequestThreshold` 的请求进入独立 Priority 容量且不参加成功采样，但 Priority 仍须有界并记录丢弃，要求不可丢的事件必须升级为 B1/B0。响应体只允许显式投影字段，默认不捕获。

5. 运行：

   ```powershell
   dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~HttpOperationLog"
   pnpm test:integration:affected:plan -- --snapshot <task-snapshot> --phase slice
   pnpm test:integration:affected -- --snapshot <task-snapshot> --phase slice
   git add src/BuildingBlocks/Full.NET.Hosting/Observability src/Hosts/Full.NET.Host.Api src/Modules/Full.NET.Modules.Auditing tests/Full.NET.UnitTests/Hosting tests/Full.NET.UnitTests/Auditing tests/Full.NET.IntegrationTests/Api/HttpRequestLoggingTests.cs
   git commit -m "feat(logging): add governed http operation stream"
   ```

## Task 8：实现限时动态诊断控制和双管理端入口

**Files:**

- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/DiagnosticPolicy.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/DiagnosticPolicySnapshot.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Observability/IDiagnosticPolicyStore.cs`
- Create: `src/Modules/Full.NET.Modules.Settings/Features/ManageDiagnosticPolicy/DiagnosticPolicyStore.cs`
- Create: `src/Modules/Full.NET.Modules.Settings/Features/ManageDiagnosticPolicy/DiagnosticPolicyManagementService.cs`
- Create: `src/Modules/Full.NET.Modules.Settings/Features/ManageDiagnosticPolicy/Endpoint.cs`
- Create: `src/Modules/Full.NET.Modules.Settings/Features/ManageDiagnosticPolicy/DiagnosticPolicyAuditWrite.cs`
- Modify: `src/Modules/Full.NET.Modules.Settings/SettingsModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Settings/Persistence/ConfigEntrySql.cs`
- Create: `src/Modules/Full.NET.Modules.Settings/Persistence/DiagnosticPolicyAuditWriter.cs`
- Create: `src/Modules/Full.NET.Modules.Settings/Persistence/DiagnosticPolicyAuditSql.cs`
- Create: `src/Modules/Full.NET.Modules.Settings.Contracts/DiagnosticPolicyManagementContracts.cs`
- Modify: `src/Modules/Full.NET.Modules.Settings/Serialization/SettingsJsonSerializerContext.cs`
- Modify: `src/Modules/Full.NET.Modules.Settings/SettingsAuthorizationContributor.cs`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/050_SettingsDomainAudit.sql`
- Create: `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/050_SettingsDomainAudit.sql`
- Modify: `ui/admin/src/navigation/catalog.ts`
- Modify: `ui/admin/src/router/index.ts`
- Create: `ui/admin/src/api/diagnostic-policy.ts`
- Create: `ui/admin/src/views/DiagnosticPolicyView.vue`
- Create: `ui/admin/src/api/diagnostic-policy.test.ts`
- Create: `ui/admin-layui/js/core/diagnostic-policy.js`
- Modify: `ui/admin-layui/index.html`
- Modify: `ui/admin-layui/js/app.js`
- Modify: `ui/admin-layui/js/core/navigation.js`
- Modify: `ui/admin-layui/js/core/route-controllers.js`
- Create: `ui/admin-layui/tests/diagnostic-policy.test.js`
- Create: `tests/e2e/admin-parity/tests/diagnostic-policy.spec.mjs`
- Create: `tests/e2e/admin-real-stack/tests/host-diagnostic-policy.spec.mjs`
- Create: `tests/Full.NET.IntegrationTests/Settings/DiagnosticPolicyAssertions.cs`
- Create: `tests/Full.NET.IntegrationTests/Migrations/Migration050SettingsDomainAuditRecoveryTests.cs`

1. RED：只有 Host 管理权限可读写；策略只能按 Category/DiagnosticGroup/Endpoint/Trace/Tenant 五种作用域，且活动策略总数和 Tenant/Trace 定向项有硬上限；TTL 1 分钟至 2 小时；速率和字节上限必须为正；过期自动恢复；Degraded/Critical 只收缩 Best Effort；更新与模块所属 B0 Audit 同事务；当前实例立即刷新，其他实例通过 Redis Backplane 在目标窗口内刷新；不得写 Outbox。

2. 使用现有 `fn_settings_config_entry` 的固定键 `fullnet.logging.diagnostic-policy` 保存版本化 JSON，不新增任意动态表或动态 Sink。接口固定为：

   ```csharp
   public interface IDiagnosticPolicyStore
   {
       ValueTask<DiagnosticPolicySnapshot> GetCurrentAsync(CancellationToken cancellationToken);
       ValueTask RefreshAsync(long minimumVersion, CancellationToken cancellationToken);
   }
   ```

3. 050 双库迁移创建模块所属 `fn_settings_domain_audit`。`DiagnosticPolicyManagementService.UpdateAsync` 在同一 `ICommandTransaction` 中校验/更新固定配置，并通过 `ITransactionalDomainAuditWriter<DiagnosticPolicyAuditWrite>` 写 B0 `settings.logging-diagnostic-policy.updated`；提交成功后使用 S1 短 L1 + 共享 L2 策略立即刷新当前不可变快照并发布 Backplane，其他实例由版本与短 TTL 最终收敛。失败记录告警但不写 Outbox；过期或无法加载策略时回到生产安全默认值，不继续无限期沿用临时诊断放宽。

4. Vue 与 Layui 同步提供只读当前状态、受控作用域、截止时间、速率/字节预算和恢复按钮；禁止自由输入文件名、索引名、Sink 名或 Metrics 标签。Parity E2E 用受控 Mock 验证两端同一导航/表单/错误契约；真实栈 E2E 必须让两端分别验证 Host 管理员保存与恢复、受限 Host 账号无导航且 API 403、过期策略回到安全默认值。双库持久化/恢复语义由 affected Integration 覆盖，浏览器真实栈至少执行 SQL Server 后端。

5. 运行双端 Unit、Settings 双库 Integration 和权限 E2E 影响集：

   ```powershell
   pnpm --dir ui/admin test
   pnpm --dir ui/admin-layui test
   pnpm --dir ui/admin build
   pnpm --dir ui/admin-layui build
   pnpm test:bundle-budgets
   pnpm --filter @fullnet/admin-parity-e2e test -- diagnostic-policy.spec.mjs
   pnpm --filter @fullnet/admin-real-stack-e2e test -- host-diagnostic-policy.spec.mjs
   pnpm test:integration:affected:plan -- --snapshot <task-snapshot> --phase slice
   pnpm test:integration:affected -- --snapshot <task-snapshot> --phase slice
   git add src/BuildingBlocks/Full.NET.Hosting/Observability src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations src/Modules/Full.NET.Modules.Settings src/Modules/Full.NET.Modules.Settings.Contracts ui/admin ui/admin-layui tests/e2e/admin-parity/tests/diagnostic-policy.spec.mjs tests/e2e/admin-real-stack/tests/host-diagnostic-policy.spec.mjs tests/Full.NET.IntegrationTests/Settings tests/Full.NET.IntegrationTests/Migrations/Migration050SettingsDomainAuditRecoveryTests.cs
   git commit -m "feat(observability): add expiring diagnostic policy control"
   ```

## Task 9：配置多实例 Data Protection Key Ring

**Files:**

- Create: `src/BuildingBlocks/Full.NET.Hosting/Security/DataProtectionOptions.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Security/DataProtectionOptionsValidator.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Security/DataProtectionServiceCollectionExtensions.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityAuthenticationServiceCollectionExtensions.cs`
- Modify: `src/Hosts/Full.NET.Host.Api/Program.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/Program.cs`
- Modify: `src/Hosts/Full.NET.Host.Api/appsettings.json`
- Modify: `src/Hosts/Full.NET.Host.Worker/appsettings.json`
- Create: `tests/Full.NET.UnitTests/Hosting/DataProtectionRegistrationTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Identity/DataProtectionMultiInstanceAssertions.cs`

1. RED：Production 必须提供稳定 ApplicationName、绝对且非临时的 KeyRingPath 与 X.509 证书；API/Worker 使用相同配置；实例 A 保护的数据可由实例 B 解密；历史证书仍可解密旧数据；只读/丢失 Key Ring、证书轮换错误和临时目录配置启动失败。应用不伪造“路径一定是 RWX”的判定，共享 RWX、备份、快照与恢复由 Task 12 Helm 合同和 Task 13 演练证明。

2. 扩展方法固定为：

   ```csharp
   public static IServiceCollection AddFullNetDataProtection(
       this IServiceCollection services,
       IConfiguration configuration,
       IHostEnvironment environment);
   ```

   内部调用 `SetApplicationName`、`PersistKeysToFileSystem` 与 `ProtectKeysWithCertificate`；Production 禁止临时密钥和开发证书回退。

3. 从 Identity 移除裸 `services.AddDataProtection()`；API/Worker 在模块装配前调用共享扩展。活动证书使用 `ProtectKeysWithCertificate`，历史解密证书通过 `UnprotectKeysWithAnyCertificate` 或等价受控证书解析器注册；启动时验证证书标识不重复、私钥可用且历史列表不包含未知材料。证书通过 Secret 挂载，只在配置中引用路径/Thumbprint，不把 PFX 密码写入仓库。

4. Integration 使用两个独立 ServiceProvider 指向同一临时 Key Ring 与测试证书，验证跨实例往返和证书历史；测试结束只清理自己创建的临时目录。

5. 运行 Hosting Unit 与 Identity 影响集并提交：

   ```powershell
   dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~DataProtection"
   pnpm test:integration:affected:plan -- --snapshot <task-snapshot> --phase slice
   pnpm test:integration:affected -- --snapshot <task-snapshot> --phase slice
   git add src/BuildingBlocks/Full.NET.Hosting/Security src/Modules/Full.NET.Modules.Identity src/Hosts tests/Full.NET.UnitTests/Hosting tests/Full.NET.IntegrationTests/Identity
   git commit -m "feat(security): share protected data protection keys"
   ```

## Task 10：增加 S3 兼容对象存储并禁止生产 Local Provider

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `src/Modules/Full.NET.Modules.Files/Full.NET.Modules.Files.csproj`
- Create: `src/Modules/Full.NET.Modules.Files/Storage/S3FileStorageOptions.cs`
- Create: `src/Modules/Full.NET.Modules.Files/Storage/S3FileStorageOptionsValidator.cs`
- Create: `src/Modules/Full.NET.Modules.Files/Storage/S3HostFileBlobStorage.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/FilesModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Files/Storage/FileStorageProviderRegistry.cs`
- Modify: `THIRD-PARTY-NOTICES`
- Create: `docs/verification/awssdk-s3-dependency-review-2026-08-01.md`
- Create: `tests/Full.NET.UnitTests/Files/S3HostFileBlobStorageTests.cs`
- Modify: `tests/Full.NET.UnitTests/Files/FileStorageProviderRegistryTests.cs`
- Create: `tests/Full.NET.IntegrationTests/Files/S3MultiInstanceStorageAssertions.cs`

1. RED：Production 默认 Provider 不是 `s3` 时启动失败；Bucket 缺失失败；`Aws` 端点模式要求 Region，`Custom` 模式要求 ServiceUrl、签名 Region 和显式 ForcePathStyle；凭据不允许写普通 appsettings；A 上传后 B 可下载/删除/对账；Save 使用临时对象或 SDK 原子完成语义，不暴露部分最终对象；Delete 幂等；超时和取消不生成错误 ready 状态。

2. 在中央包管理固定：

   ```xml
   <PackageVersion Include="AWSSDK.S3" Version="4.0.101.4" />
   ```

   ProviderKey 固定为 `s3`；配置使用 `Files:S3:EndpointMode`（`Aws|Custom`）、`ServiceUrl`、`Region`、`BucketName`、`ForcePathStyle`、`RequestTimeout`。AWS 原生模式不强制伪造 ServiceUrl，自定义 S3 兼容端点必须校验 HTTPS/受信内网边界。AccessKey/SecretKey 只从环境变量、工作负载身份或挂载 Secret 解析。

3. `S3HostFileBlobStorage` 实现现有 `IFileStorageProvider`，对象键只接受模块生成的规范相对键；`OpenReadAsync` 返回可释放响应流包装；404 映射 `FileNotFoundException`；重试只处理幂等读/探测/删除，写入重试遵循 SDK 请求体可重放边界。

4. 使用 S3 兼容 Testcontainer/测试替身验证两实例共享；若本地没有 S3 容器，只运行 Unit，Integration 由 CI 的 `infrastructure` 分片提供，不能把未运行写成通过。

5. 在依赖评审记录中固定 AWSSDK.S3 的维护状态、传递依赖树、许可证/MIT 再分发兼容性、包/发布产物体积差异、裁剪与 Native AOT 影响、凭据 Provider 行为和备选方案/退出条件；同步 `THIRD-PARTY-NOTICES`。未取得其中任一证据时不得合入新 Provider。

6. 运行 Files Unit、affected Integration 与依赖审计后提交：

   ```powershell
   dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~Files"
   pnpm test:integration:affected:plan -- --snapshot <task-snapshot> --phase slice
   pnpm test:integration:affected -- --snapshot <task-snapshot> --phase slice
   dotnet list src/Modules/Full.NET.Modules.Files/Full.NET.Modules.Files.csproj package --vulnerable --include-transitive
   dotnet list src/Modules/Full.NET.Modules.Files/Full.NET.Modules.Files.csproj package --outdated --include-transitive
   dotnet list src/Modules/Full.NET.Modules.Files/Full.NET.Modules.Files.csproj package --include-transitive
   git add Directory.Packages.props src/Modules/Full.NET.Modules.Files tests/Full.NET.UnitTests/Files tests/Full.NET.IntegrationTests/Files THIRD-PARTY-NOTICES docs/verification/awssdk-s3-dependency-review-2026-08-01.md
   git commit -m "feat(files): add production s3 storage provider"
   ```

## Task 11：分离 Cache Redis 与 Realtime Redis 并校验 SignalR 路由

**Files:**

- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/CacheOptions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/ServiceCollectionExtensions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Realtime.SignalR/RealtimeOptions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Realtime.SignalR/ServiceCollectionExtensions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Realtime.SignalR/RealtimeRedisConfiguration.cs`
- Modify: `src/Hosts/Full.NET.Host.Api/appsettings.json`
- Modify: `src/Hosts/Full.NET.Host.Worker/appsettings.json`
- Modify: `tests/Full.NET.UnitTests/Realtime/RealtimeBackplaneRegistrationTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Realtime/RealtimeRedisBackplaneRecoveryTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/HealthEndpointTests.cs`

1. RED：Production 的 `Cache:RedisConnectionString` 与 `Realtime:RedisBackplaneConnectionString` 必须显式且不能相同；Worker 启用实时发布时必须有 Realtime Redis；Channel/Key 前缀包含环境与应用；共享 Redis 只允许 Development/Test 显式兼容；Redis 抖动不应使全部 API 同时 NotReady。

2. 删除 Realtime 回退 `ConnectionStrings:redis` 的生产语义；保留开发兼容时必须设置 `Realtime:AllowSharedRedisInDevelopment=true`。Cache 与 Realtime 分别注册独立健康检查和低基数连接/重连/消息指标。

3. 增加 `Realtime:TransportMode = Default|WebSocketsOnly` 与 `Realtime:SkipNegotiation` 校验：默认模式要求 Ingress 会话亲和；只有 `WebSocketsOnly + SkipNegotiation=true` 才允许关闭 affinity。应用只校验配置契约，具体 affinity 在 Task 12 Helm 模板实现。

4. Integration 覆盖跨节点连接/发布、断线重连、Realtime Redis 重启和 Cache Redis 压力不影响 Realtime Redis；Readiness 使用有滞回的 Degraded 语义，安全关键 Endpoint 自行 fail-closed。

5. 运行 Realtime/Caching Unit 与 Redis Integration 后提交：

   ```powershell
   dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~Realtime|FullyQualifiedName~Caching"
   pnpm test:integration:affected:plan -- --snapshot <task-snapshot> --phase slice
   pnpm test:integration:affected -- --snapshot <task-snapshot> --phase slice
   git add src/BuildingBlocks/Full.NET.Caching.Fusion src/BuildingBlocks/Full.NET.Realtime.SignalR src/Hosts tests/Full.NET.UnitTests/Realtime tests/Full.NET.IntegrationTests/Realtime
   git commit -m "feat(realtime): isolate redis backplanes"
   ```

## Task 12：建立 API/Worker/Migrator 容器和 Helm 生产 Chart

**Files:**

- Create: `eng/docker/Dockerfile`
- Create: `deploy/helm/fullnet/Chart.yaml`
- Create: `deploy/helm/fullnet/values.yaml`
- Create: `deploy/helm/fullnet/values.schema.json`
- Create: `deploy/helm/fullnet/templates/_helpers.tpl`
- Create: `deploy/helm/fullnet/templates/api-deployment.yaml`
- Create: `deploy/helm/fullnet/templates/api-service.yaml`
- Create: `deploy/helm/fullnet/templates/api-ingress.yaml`
- Create: `deploy/helm/fullnet/templates/api-hpa.yaml`
- Create: `deploy/helm/fullnet/templates/api-pdb.yaml`
- Create: `deploy/helm/fullnet/templates/worker-deployment.yaml`
- Create: `deploy/helm/fullnet/templates/worker-hpa.yaml`
- Create: `deploy/helm/fullnet/templates/worker-pdb.yaml`
- Create: `deploy/helm/fullnet/templates/migrator-job.yaml`
- Create: `deploy/helm/fullnet/templates/data-protection-pvc.yaml`
- Create: `deploy/helm/fullnet/templates/configmap.yaml`
- Create: `deploy/helm/fullnet/templates/serviceaccount.yaml`
- Create: `deploy/helm/fullnet/templates/networkpolicy.yaml`
- Create: `deploy/helm/fullnet/templates/NOTES.txt`
- Create: `eng/deploy/Invoke-FullNetRelease.ps1`
- Create: `tests/deployment/helm-contract.test.mjs`
- Create: `tests/deployment/release-order-contract.test.mjs`
- Create: `tests/deployment/container-image-contract.test.mjs`
- Modify: `package.json`
- Modify: `.github/workflows/ci.yml`

1. 先写 Helm/镜像合同 RED：API 最少 2 副本，生产默认 3；Worker 最少 2 且 `MaxConcurrency=1`；Migrator 是带 Helm hook 权重的 one-shot Job；API rolling `maxUnavailable=0/maxSurge=1`；PDB；topology spread；startup/readiness/liveness；preStop 与 terminationGracePeriod；只读根文件系统、非 root、seccomp、能力全删；NetworkPolicy；Data Protection 必须引用已有 RWX Claim，或显式提供经过验证支持 RWX、快照和备份的 StorageClass 后才创建 PVC；S3/Redis/DB/证书只引用 Secret；Chart 不安装数据库、Redis、S3、Loki、Tempo、WAF 或分布式限流服务。

2. `eng/docker/Dockerfile` 使用同一 SDK build stage 和三个 final target：`api`、`worker`、`migrator`。最终镜像使用 ASP.NET/Runtime 非 root 用户、只复制 publish 产物，并暴露构建提交标签。

3. Helm 默认值固定：API `replicaCount=3`、HPA `minReplicas=3/maxReplicas=12`；Worker `replicaCount=2`、HPA `minReplicas=2/maxReplicas=8`。CPU/内存指标可使用标准 Metrics API；API 入口指标和 Worker backlog age 属于可选自定义指标，只有 values 显式声明已安装并验证对应 Metrics Adapter、查询和指标名时才能启用，否则模板必须 fail，而不是渲染一个永远无法扩缩容的 HPA。`maxReplicas` 必须同时满足：

   ```text
   apiMaxReplicas * apiMaxPoolSize
   + workerMaxReplicas * workerMaxPoolSize
   + migrationReserve
   <= databaseConnectionBudget
   ```

   `values.schema.json` 要求明确提供四个连接预算值，模板用 `fail` 阻止超预算渲染；不得假设 HPA 可绕过数据库预算。

4. Ingress 采用集群已安装的 ingress-nginx 接口，开启请求体/超时/可信代理链/真实客户端键配置，并在默认 SignalR 模式设置 cookie affinity；WebSockets-only + SkipNegotiation 时模板允许关闭 affinity。禁止信任任意客户端提交的 `X-Forwarded-For`。生产 `edgeProtection` 必须声明已部署的 CDN/WAF/API Gateway 或外部分布式 Rate Limit Service、全局请求速率/突发/并发连接额度、认证与匿名维度以及不可用时 fail-open/fail-closed 策略；Chart 只集成其 Secret/Service/注解，不自行安装。仅 ingress-nginx Pod 本地的 `limit-rps/limit-connections` 不能宣称全局额度。应用内每实例限流只作为纵深防御，按 `maxReplicas` 分配后总和不得超过全局预算，并由 schema/反例证明扩容不会放大允许额度；WAF/DDoS 清洗状态与拒绝指标进入 Task 13。生产缺少全局 Edge 能力时模板必须 fail。

5. 生产以同一 Chart 的三个独立 Release（`fullnet-migrator`、`fullnet-worker`、`fullnet-api`）启用单一角色；`Invoke-FullNetRelease.ps1` 固定执行 Migrator Job 完成 -> Worker consumer 就绪 -> API 滚动就绪，任一阶段失败立即停止，数据库变更遵循 Expand/Contract。默认 values 不允许一个生产 Release 同时启用三个角色。

6. 增加 `pnpm test:helm`，执行 `helm lint`、两个数据库 values 渲染、连接预算反例、Edge 全局额度缺失/副本放大/伪造代理头反例、禁用 affinity 反例和 `kubectl --dry-run=client`；CI 只验证模板，不连接生产集群。

7. 实际构建并检查三个 final target。构建成功后记录镜像 digest、`Config.User`、Entrypoint/Cmd；以 `dotnet --info` 或等价无业务依赖 smoke 验证运行时存在。若当前环境没有 Docker/BuildKit，Task 12 状态必须保持未验证，不能进入 P2-A 完成：

   ```powershell
   pnpm test:helm
   docker build --target api -t fullnet-api:contract -f eng/docker/Dockerfile .
   docker build --target worker -t fullnet-worker:contract -f eng/docker/Dockerfile .
   docker build --target migrator -t fullnet-migrator:contract -f eng/docker/Dockerfile .
   pnpm test:container-images -- --tag-suffix contract
   git add eng/docker eng/deploy deploy/helm tests/deployment package.json .github/workflows/ci.yml
   git commit -m "feat(deploy): add production kubernetes helm baseline"
   ```

## Task 13：落地日志采集、OTel 管线、告警和恢复演练配置

**Files:**

- Create: `deploy/observability/fluent-bit-values.yaml`
- Create: `deploy/observability/otel-collector-values.yaml`
- Create: `deploy/observability/prometheus-rules.yaml`
- Create: `deploy/observability/grafana-dashboard.json`
- Create: `deploy/observability/README.md`
- Create: `docs/runbooks/high-concurrency-multi-instance-production.md`
- Create: `docs/runbooks/data-protection-key-recovery.md`
- Create: `docs/runbooks/cache-redis-recovery.md`
- Create: `docs/runbooks/audit-log-backpressure.md`
- Create: `tests/deployment/observability-contract.test.mjs`
- Modify: `package.json`

1. RED 合同检查：应用只写 Compact JSON stdout；Fluent Bit 使用磁盘 buffer、内存上限、损坏 chunk 隔离、重试上限与 TLS；B2 Best Effort 与运行时 Priority 使用独立容量/路由，冷归档进 S3；B0/B1 Durable Audit 继续直接写模块/Audit 数据库，只输出健康与失败指标，不得被复制成另一条“Durable 日志”管线；OTel Collector 开 memory_limiter/batch/retry/file_storage；任何管道故障不得递归写回同一 Sink。

2. 日志字段固定为 `timestamp/level/message/application/instance/trace_id/span_id/log.class/log.stream/reliability.class/data.classification/DiagnosticGroup/EventName`；禁止把动态 Group 变成文件名、索引名、租户标签或 Metrics label。

3. 告警至少覆盖：Error/Critical 风暴、普通/高优先队列丢弃、Spool 高水位/磁盘满、Audit B1 队列/等待/失败、缓存失效 P99/最大陈旧窗、Redis 重连/驱逐/复制延迟、数据库连接等待、Outbox/Jobs backlog age、Edge 全局速率/连接额度拒绝、WAF/DDoS 清洗或外部限流服务不可用、HPA 达上限和 PDB 不可满足。

4. Runbook 写出 99.9% 月度 SLO、恢复负责人、Data Protection 历史证书与 Key Ring 恢复、Cache/Realtime Redis 分别切换、S3 超时/孤立对象、Audit fail-open/fail-closed、Collector 中断、Expand/Contract 回滚和 RPO/RTO 验证步骤。

5. 运行合同测试和 YAML/JSON 解析，预期不需要真实后端：

   ```powershell
   pnpm test:observability-deploy
   git add deploy/observability docs/runbooks tests/deployment package.json
   git commit -m "ops(observability): add durable collection and recovery baseline"
   ```

## Task 14：建立专用硬件容量认证套件，保留开发阶段声明边界

**Files:**

- Create: `eng/load/k6/lib/config.js`
- Create: `eng/load/k6/lib/metrics.js`
- Create: `eng/load/k6/scenarios/read-heavy.js`
- Create: `eng/load/k6/scenarios/mixed-write.js`
- Create: `eng/load/k6/scenarios/cache-recovery.js`
- Create: `eng/load/k6/scenarios/audit-logging.js`
- Create: `eng/load/k6/scenarios/outbox-jobs-backlog.js`
- Create: `eng/load/profiles/2k.json`
- Create: `eng/load/profiles/5k.json`
- Create: `eng/load/profiles/10k.json`
- Create: `eng/load/profiles/soak.json`
- Create: `eng/load/validate-profiles.mjs`
- Create: `deploy/load/k6-test-run.yaml`
- Create: `tests/performance/load-profile-contract.test.mjs`
- Create: `eng/load/README.md`
- Create: `docs/verification/high-concurrency-capacity-certification-template.md`
- Modify: `package.json`
- Modify: `docs/roadmap/capability-status.md`

1. k6 套件先实现静态配置验证，不在开发机自动发起并发。每个 Profile 明确 `targetInFlight`、闭环并发与开环 arrival-rate 两种负载模型、预热、升压、稳定、突发、Soak、最大错误率、P50/P95/P99、恢复时间与停止条件；`10k.json` 的 `targetInFlight` 固定 10000，但必须以应用端 actual active requests 校验，不能把 k6 VU 数直接当作在途请求。硬件、目标 RPS 和实例数由认证记录据实填写。`deploy/load/k6-test-run.yaml` 只供隔离的专用容量集群使用，不进入普通 CI。

2. 场景覆盖热/冷缓存、读多写少、混合写、热点/不存在 Key、批量失效、Redis 冷启动、普通 HTTP Operation Log 六档 Profile、B1 Audit 微批、上传、SignalR、Outbox/Jobs 积压与数据库主节点切换。

3. 采集应用、负载生成器、Pod、Node、数据库、Redis、S3、Collector 的时间对齐指标；除业务指标外至少保存 actual active requests、arrival-rate 未达成/丢弃迭代、ThreadPool queue/thread count、分配率、GC pause/Gen2、Socket/HttpClient、数据库连接池等待和日志/Audit/Worker backlog。每次运行保存镜像 digest、Git SHA、Helm values、硬件、数据库参数、Redis 参数、数据规模、负载模型和原始结果 URI。缺任一项时结果只能标记 `Incomplete`。

4. 容量执行顺序固定为 2K -> 5K -> 10K -> Soak；任一错误率、P99、恢复时间、数据库连接/锁/IO、缓存陈旧窗、Audit/Outbox 可靠性、租户隔离、调度到达率或负载生成器资源超预算即停止扩压。闭环模型用于验证目标在途下的稳定性，开环模型用于验证目标到达率下是否出现排队，并识别闭环负载随系统变慢而自动降速形成的 coordinated omission；两者结论不得互相冒充。SQL Server 与 MySQL 分开认证，不能用一方结果代表另一方。

5. 开发阶段只运行静态合同；专用环境使用固定版本的 k6 镜像执行脚本：

   ```powershell
   node eng/load/validate-profiles.mjs
   node --test tests/performance/load-profile-contract.test.mjs
   ```

   预期：配置与场景可构建、可校验，但路线图仍为 `Capacity-not-verified`。只有专用硬件 10K 与 Soak 双库证据获批准后，才能在单独任务更新状态。提交：

   ```powershell
   git add eng/load deploy/load tests/performance/load-profile-contract.test.mjs package.json docs/verification/high-concurrency-capacity-certification-template.md docs/roadmap/capability-status.md
   git commit -m "test(performance): add dedicated capacity certification harness"
   ```

## Task 15：执行全链路验收并形成实施验证记录

**Files:**

- Create: `docs/verification/high-concurrency-multi-instance-implementation-2026-08-01.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/verification/high-concurrency-modular-monolith-multi-instance-assessment-2026-08-01.md`
- Modify: `eng/testing/test-matrix.json`（仅 fresh discovery 证明数量变化时）

1. 先运行 slice affected 计划并审查命中集，不手工追加未命中的全量 Integration：

   ```powershell
   pnpm test:integration:affected:plan -- --snapshot fullnet-high-concurrency-implementation-plan-20260801 --phase slice
   ```

2. 运行 fresh Unit、架构、治理、命名、SQL、OpenAPI、客户端、Helm、观测部署和 Release build：

   ```powershell
   dotnet restore Full.NET.slnx
   dotnet build Full.NET.slnx -c Release --no-restore
   pnpm test:dotnet:unit
   pnpm test:dotnet:architecture
   pnpm test:governance
   pnpm test:skills
   pnpm test:naming
   pnpm test:sql-safety
   pnpm test:openapi
   pnpm test:clients
   pnpm build:clients
   pnpm test:bundle-budgets
   pnpm test:e2e
   pnpm test:e2e:real -- host-diagnostic-policy.spec.mjs
   pnpm test:helm
   pnpm test:observability-deploy
   ```

3. 使用同一快照执行 slice；合并候选必须重新生成 merge 计划、审查后再执行 merge。等待 Testcontainers/Ryuk 自然退出并记录 Docker 残留为 0：

   ```powershell
   pnpm test:integration:affected -- --snapshot fullnet-high-concurrency-implementation-plan-20260801 --phase slice
   pnpm test:integration:affected:plan -- --snapshot fullnet-high-concurrency-implementation-plan-20260801 --phase merge
   pnpm test:integration:affected -- --snapshot fullnet-high-concurrency-implementation-plan-20260801 --phase merge
   ```

4. 再次实际构建 api/worker/migrator 三个镜像 target，运行镜像合同/smoke 并记录 digest、非 root User 与入口点。没有 Docker/BuildKit 时该部署项必须记录为未验证，Task 15 不得标为完整生产就绪：

   ```powershell
   docker build --target api -t fullnet-api:acceptance -f eng/docker/Dockerfile .
   docker build --target worker -t fullnet-worker:acceptance -f eng/docker/Dockerfile .
   docker build --target migrator -t fullnet-migrator:acceptance -f eng/docker/Dockerfile .
   pnpm test:container-images -- --tag-suffix acceptance
   ```

5. 验证记录逐项对应 ADR-0005：缓存不写 Outbox、旧缓存消息兼容排空、B0/B1/B2、普通 HTTP Operation Log、逻辑分组、动态诊断、Data Protection、S3、Redis 分离、Edge 全局限流不随副本放大、SignalR affinity、K8s 滚动/排空/预算、日志 Spool、RPO/RTO。没有专用硬件结果时明确写“实现与开发验证完成，容量状态仍为 `Capacity-not-verified`”。

6. 执行最终卫生检查和规则/Skill 演进检查：

   ```powershell
   git diff --check
   git status --short
   git branch --show-current
   ```

   只在真实规则/Skill 缺口触发时更新候选；否则在验证记录写“未命中演进条件”。提交最终记录：

   ```powershell
   git add docs/verification/high-concurrency-multi-instance-implementation-2026-08-01.md docs/verification/high-concurrency-modular-monolith-multi-instance-assessment-2026-08-01.md docs/roadmap/capability-status.md
   if (Test-Path eng/testing/test-matrix.json) { git add eng/testing/test-matrix.json }
   git commit -m "docs(verification): record multi-instance production readiness"
   ```

---

## 完成定义

- P0：权威 Spec、ADR、规则、两个项目 Skill 与合同无冲突。
- P1：API/Worker 多实例不依赖节点本地密钥、文件、会话或缓存正确性；Tenancy 缓存失效不再写 Outbox；Data Protection、S3、Cache Redis、Realtime Redis 和 SignalR 跨节点行为有测试证据。
- P2：缓存条目全部分类；B0/B1/B2 语义可执行；B1 是有界跨请求微批；普通 HTTP Operation Log 每请求最多一条并支持逻辑分组、六档 Profile、脱敏、采样和限时动态诊断；日志/Audit 均不写 Outbox。
- P3：API/Worker/Migrator 镜像与 Helm Chart 可重复渲染，满足滚动、PDB、拓扑、探针、排空、安全、网络和数据库连接预算门禁；可观测采集与恢复 Runbook 完整。
- P4：开发交付不要求 10K；专用硬件完成 SQL Server/MySQL 的 2K/5K/10K/Soak 认证前，任何文档、发布说明和状态页都保持 `Capacity-not-verified`。
- 性能工程门禁：所有热路径全链异步、资源有界、低基数可观测；数据库/Redis/HTTP 往返和全局连接预算可解释；低级内存优化有测量、测试和回退；Task 14 明确区分闭环在途与开环到达率并监控负载生成器自身饱和。
