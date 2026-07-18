# Full.NET 架构硬化实施计划

> **For Codex:** REQUIRED SUB-SKILL: Use `fullnet-module-delivery` for module/CRUD/Dapper work, and use test-driven development for every behavior change. Execute one phase at a time; do not start lower-priority feature expansion while a P0 gate remains open without an explicit decision record.

**Goal:** 把已确认的实施雷区转化为可验证的生产门禁，并让状态、宿主、数据库、消息和真实客户端链路随着模块增长仍保持一致。

**Architecture:** 保留模块化单体、Dapper-first、SQL Server/MySQL、MessagePack Outbox、FusionCache、标准 HTTP/ProblemDetails 和 Vue/Layui 双端基线。通过显式 Profile、语义 SQL Catalog、版本升级链、缓存一致性等级和双层 E2E 加固，而不引入运行时扫描、通用 ORM、全局 EAV 或额外缓存实现。

**Tech Stack:** .NET 10、Dapper、DbUp、SQL Server 2022、MySQL 8、MessagePack-CSharp、FusionCache、Redis、Serilog、OpenTelemetry、Vue 3、Layui 2、Vite、Vitest、Playwright、Microsoft.Testing.Platform、Testcontainers。

---

## 执行顺序总览

| 阶段 | 优先级 | 退出条件 |
|---|---:|---|
| H0 状态与门禁基线 | P0 | 状态矩阵成为 README/路线图唯一总览；CI 可识别危险 SQL 与命名漂移 |
| H1 Seed 闭环 | P0 | 既有 Seed 计划 S0-S2 全部通过 SQL Server/MySQL |
| H2 模块生命周期与宿主 Profile | P0 | 初始化钩子行为确定；三宿主注册漂移被架构测试阻止 |
| H3 消息、缓存与日志可靠性 | P1 | 多版本/死信、陈旧窗口、高优先级日志故障场景可验证 |
| H4 浏览器真实链路与共享策略 | P1 | Vue/Layui 在真实后端完成安全关键 E2E；镜像策略收敛 |
| H5 L5 i18n 与工具链治理 | P2 | 首个业务翻译表双库验证；兼容性队列有自动检查 |

### Task 1: 自动校验状态单一事实源和发布检查

**Files:**
- Modify: `README.md`
- Modify: `docs/roadmap/capability-status.md`
- Modify: `docs/roadmap/client-delivery-roadmap.md`
- Modify: `docs/roadmap/adminnet-feature-parity.md`
- Create: `eng/verify-capability-status.ps1`
- Test: `.github/workflows/ci.yml`

1. 以现有状态矩阵为输入先写失败检查：README 必须链接状态矩阵；矩阵快照日期、基线提交、状态枚举和关键证据列不能为空。
2. 让脚本校验路线图中公开状态只能来自统一枚举，并拒绝 `Verified` 行缺少证据链接。
3. README 只保留简短当前边界，详细状态指向矩阵，避免多份手工快照漂移。
4. 将检查接入 CI；发布标签前刷新基线提交和新鲜验证记录。
5. 验证文档链接、脚本单测/退出码和 `git diff --check`。

### Task 2: SQL 静态门禁与破坏性变更豁免

**Files:**
- Create: `eng/sql-lint.ps1`
- Create: `eng/sql-waivers/README.md`
- Follow: `docs/superpowers/plans/2026-07-18-naming-governance.md`
- Modify: `rules/development-quality.md`
- Modify: `.github/workflows/ci.yml`
- Test: `tests/Full.NET.ArchitectureTests/SqlGovernanceTests.cs`

1. 先用测试夹具证明扫描器会拒绝应用 SQL 的 `SELECT *`、无 `WHERE` 的 `UPDATE/DELETE`，以及迁移中的 DROP/TRUNCATE/直接重命名等危险语句；命名违规由同一 CI 阶段调用 Naming Profile 门禁报告，不在两个脚本重复实现规则。
2. 定义窄范围、带到期版本的豁免 Schema；没有风险、备份/验证、发布策略和数据审查者时拒绝。
3. 扫描模块内嵌 SQL、迁移脚本和代码生成模板；排除测试中明确标记的反例夹具。命名债务只能来自精确 Allowlist，禁止目录级或通配豁免。
4. 将危险 DDL 继续交给 SQL Server/MySQL 半完成迁移集成测试验证，Lint 不替代真实数据库。
5. 记录行号、规则码和修复指导，避免只有模糊失败。

### Task 3: 执行 Seed Baseline/Overlay 既有计划

**Files:**
- Follow: `docs/superpowers/plans/2026-07-17-seed-data-module.md`
- Modify: `docs/roadmap/capability-status.md`

1. 严格执行既有 S0-S2/Task 1-7，不在本计划复制另一套 Seed 设计。
2. 增加文档断言：不提供通用 Down；开发/Test 重置采用临时数据库重建或受控备份恢复。
3. SQL Server/MySQL 必须覆盖首次、重复、失败重跑、冲突、Production Profile 拒绝、Outbox 幂等和 Secret 脱敏。
4. 只有当前 `--seed-local` 被安全替代、Production 显式门禁生效且双库通过后，状态才从 `Designing` 提升。

### Task 4: 模块目录、宿主 Profile 与初始化生命周期

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Modularity/Modules/IFullNetModule.cs`
- Modify: `src/BuildingBlocks/Full.NET.Modularity/Modules/ModuleExtensions.cs`
- Create: `src/BuildingBlocks/Full.NET.Modularity/Modules/FullNetHostProfile.cs`
- Create: `src/BuildingBlocks/Full.NET.Modularity/Modules/FullNetModuleCatalog.cs`
- Modify: `src/Hosts/Full.NET.Host.Api/Program.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/Program.cs`
- Modify: `src/Hosts/Full.NET.Host.Migrator/Program.cs`
- Test: `tests/Full.NET.UnitTests/Modularity/ModuleLifecycleTests.cs`
- Test: `tests/Full.NET.ArchitectureTests/HostModuleProfileTests.cs`

1. 先证明现状：注册模块后 `InitializeAsync` 不会被调用；新增测试定义依赖顺序、恰好一次、失败阻止就绪和取消传播。
2. 在设计检查点选择“实现确定性初始化”或“删除无真实使用者的钩子”；禁止继续保留接口有、行为无的状态。
3. 建立显式 Catalog/Profile，不使用任意程序集扫描。Worker 只能选择模块公开的最小后台入口。
4. 架构测试比较 Api/Worker/Migrator 与模块声明，阻止漏依赖、重复注册、错误 Endpoint 和顺序漂移。
5. 初始化不得运行 Migration/Seed，不得执行不可回滚外部副作用。

### Task 5: 双数据库语义 SQL Catalog

原生多结果集、受控动态查询构建及扩展包准入统一执行 [`2026-07-18-dapper-tooling.md`](2026-07-18-dapper-tooling.md)；本任务不再引入 ProviderTools、Dapper.Transaction 或另一套查询构建抽象。

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Data.Abstractions`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper`
- Create: `docs/development/sql-portability.md`
- Test: `tests/Full.NET.UnitTests/Data/SqlStatementCatalogTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Data/SqlPortabilityTests.cs`

1. 从第一个真实分页/Upsert 业务切片写跨库语义测试，不先造覆盖所有数据库能力的大接口。
2. 定义稳定 Statement 名称、Provider 对、参数/返回/并发语义；缺一库实现立即失败。
3. 将 CTE、窗口、Upsert、锁、JSON、日期函数纳入准入表；默认业务 Handler 不含 Provider 分支。
4. JSON 聚合/更新先在应用层实现；只有基准和 ADR 通过才增加双 Provider SQL。
5. 在代码生成模板中生成两库配对测试骨架。
6. Statement 名、表/列/约束和模板输出必须调用 Naming Profile/命名内核，不在 SQL Catalog 另建命名算法。
7. 具有共同一致性窗口的聚合读取优先评估原生 QueryMultiple；动态筛选只有真实消费者命中门禁后才引入 SqlBuilder 封装。

### Task 6: Outbox 多版本、最大重试与死信

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Modularity/Messaging`
- Modify: `src/BuildingBlocks/Full.NET.Data.Abstractions`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper`
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs`
- Add: SQL Server/MySQL forward-only migrations
- Test: `tests/Full.NET.UnitTests/Messaging/OutboxVersioningTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/OutboxRecoveryTests.cs`

1. 先建立 V1 旧载荷、V2 当前载荷、未知版本、不可信载荷和缺 Handler 的失败样例。
2. 支持并行版本 Handler 或相邻版本升级链；升级器必须使用显式旧版本契约，禁止 Typeless。
3. 区分瞬时/永久失败，加入最大尝试、死信状态、错误摘要和可审计人工重放。
4. 双库验证多 Worker 租约、坏消息不阻塞批次、重启恢复、版本退役扫描。
5. 文档记录兼容窗口和“先消费者、后生产者、最后退役”发布顺序。

### Task 7: 缓存一致性等级与故障注入

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion`
- Modify: Identity/Tenancy security cache call sites
- Test: `tests/Full.NET.IntegrationTests/Caching/CacheConsistencyTests.cs`
- Modify: `docs/development/getting-started.md`

1. 为 S0/S1/S2 写策略测试；S0 禁止 Fail-Safe 和仅依赖陈旧缓存完成授权。
2. 对安全关键写入实现“提交后本机同步失效 + Outbox 跨节点修复”，避免提交后的本机请求回填旧 L1。
3. 用两 API 节点、Redis 和延迟 Worker 注入验证提交/失效窗口、Backplane 中断、Redis 不可用和恢复。
4. 暴露陈旧命中、失效延迟、Backplane 恢复和 Outbox backlog 指标。
5. 不以 Background Refresh 通过正确性测试。

### Task 8: 日志高优先级通道与降级演练

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Hosting/Observability`
- Test: `tests/Full.NET.UnitTests/Hosting/HighPriorityLoggingTests.cs`
- Create: `docs/operations/logging-degraded-mode.md`

1. 先复现共享队列满时 Error/Critical 与 Information 一起丢失。
2. 建立独立高优先级容量、指标和本地短期 Spool/可靠 Sink；不得在请求线程同步写网络。
3. Audit 保持数据库/Outbox 路径，并测试日志队列满不影响审计持久化。
4. 演练 Sink 慢、磁盘满、平台不可用和进程退出的有界刷新。
5. 将队列深度、丢弃数、高优先级降级和 Spool 容量接入健康与告警。

### Task 9: 浏览器跨 Tab 刷新与租户切换竞态

**Files:**
- Modify: `packages/client-contracts` or a new framework-neutral browser session package
- Modify: `ui/admin/src/auth/session.ts`
- Modify: `ui/admin-layui/js/core/session.js`
- Test: mirrored Vue/Layui unit tests
- Test: `tests/Full.NET.IntegrationTests/Identity/SessionRaceTests.cs`

1. 服务端测试并发 `PUT /tenancy/context` 与 Refresh，断言 Session 记录权威、版本重试后重新验证，旧 JWT 不覆盖租户。
2. 浏览器先用 Web Locks，使用 BroadcastChannel 传播完成/退出；提供不支持平台的保守回退。
3. 两个 BrowserContext/Page 共用 Cookie 复现并发 Refresh，不通过放宽重用检测让测试变绿。
4. 记录客户端协调失败时的安全行为和用户可恢复路径。

### Task 10: 双管理端 headless 契约层

Vue 壳层迁移按 [`2026-07-18-vue-art-design-pro-adoption.md`](2026-07-18-vue-art-design-pro-adoption.md) 执行；本任务先提供不会被模板覆盖的无框架契约边界。

**Files:**
- Modify: `packages/client-contracts`
- Modify: `ui/admin/src/api/http.ts`
- Modify: `ui/admin/src/auth/session.ts`
- Modify: `ui/admin-layui/js/core/http.js`
- Modify: `ui/admin-layui/js/core/session.js`
- Modify: `ui/admin-layui/js/core/navigation.js`
- Modify: `ui/admin-layui/jsconfig.json`
- Test: shared contract fixtures and both adapters

1. 用同一组输入夹具证明 Vue/Layui 在 ProblemDetails、刷新、退出、导航拒绝和权限失败上结果一致。
2. 只提取纯函数和状态机；DOM、路由器、Store、Layui/Vue 组件留在各适配层。
3. Layui 开启 JSDoc + `checkJs`，共享包输出浏览器可用 ESM 和类型声明。
4. 任何共享变更必须跑共享包、Vue、Layui 单测和双端 E2E。
5. 不引入 Web Components，除非另有通过门禁的 ADR。

### Task 11: 真实后端参与的双管理端 Playwright

**Files:**
- Create: `tests/e2e/admin-real-stack`
- Modify: `src/Hosts/Full.NET.AppHost/Program.cs` or dedicated test orchestrator
- Modify: `package.json`
- Modify: `.github/workflows/ci.yml`

1. 保留现有 Mock admin-parity E2E，新增真实 API/数据库/Redis 套件。
2. 以隔离数据库执行 Baseline/Test Seed，启动 API 与两管理端；测试精确 Origin 和代理配置。
3. Vue/Layui 分别覆盖登录 Cookie、CSRF、401 刷新、跨 Tab、租户切换、ProblemDetails、退出和无权限。
4. SQL Server/MySQL 至少各跑安全关键冒烟；较慢全套可按夜间/主干门禁分层，但发布前两库必跑。
5. 任何 Route Mock 都必须被测试配置拒绝，避免真实套件退化为壳层测试。

### Task 12: 首个 L5 业务翻译表与兼容性队列

**Files:**
- Create: first module-owned `*_translation` SQL Server/MySQL migrations
- Modify: `docs/superpowers/specs/2026-07-17-full-stack-localization-design.md`
- Create: `docs/development/frontend-version-cohorts.md`
- Test: module dual-db localization tests
- Test: dependency policy check

1. 等首个真实业务消费者（优先 Dictionaries/菜单元数据或通知模板）出现后再固化表，不先建立全局 EAV。
2. 双库验证 `(TenantId, EntityId, Locale)` 唯一性、Fallback、并发版本、索引与租户隔离。
3. 记录 Admin Web、shared packages、uni-app/DCloud、E2E 的版本队列、上游约束和复核日期。
4. CI 检查同一队列一致性和跨队列协议夹具；不强迫不兼容工具升级到同一版本。
5. 状态矩阵只有在真实消费者、双库和对应客户端通过后才提升 L5。

客户端 UI 落地分别执行 [`2026-07-18-vue-art-design-pro-adoption.md`](2026-07-18-vue-art-design-pro-adoption.md)、[`2026-07-18-rich-text-editor-foundation.md`](2026-07-18-rich-text-editor-foundation.md)、[`2026-07-18-uniapp-uni-ui-adoption.md`](2026-07-18-uniapp-uni-ui-adoption.md) 与 [`2026-07-18-flutter-ui-foundation.md`](2026-07-18-flutter-ui-foundation.md)，不得把“已选型”误标为“已集成”。

## 完成门禁

每个 Task 完成时必须：

1. 运行风险相称的 Unit、Architecture、Compatibility、SQL Server/MySQL Integration、前端单测/构建/E2E；
2. 直接运行 Microsoft.Testing.Platform 测试 DLL，并更新准确的 `--minimum-expected-tests`；
3. 更新能力状态矩阵、受影响规格、README、路线图和验证记录；
4. 完成规则与 Skill 演进复盘；
5. 执行 `git diff --check` 和 `git status`，不得把未执行写成通过。
