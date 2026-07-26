# Full.NET 架构硬化实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:test-driven-development` for every behavior change and `fullnet-module-delivery` for module/CRUD/Dapper work. Execute one phase at a time; do not start lower-priority feature expansion while a P0 gate remains open without an explicit decision record. Steps use checkbox (`- [ ]`) syntax for newly added work.

**Goal:** 把已确认的实施雷区转化为可验证的生产门禁，并让状态、宿主、数据库、消息和真实客户端链路随着模块增长仍保持一致。

**Architecture:** 保留模块化单体、Dapper-first、SQL Server/MySQL、MessagePack Outbox、FusionCache、标准 HTTP/ProblemDetails 和 Vue/Layui 双端基线。通过显式 Profile、语义 SQL Catalog、版本升级链、缓存一致性等级和双层 E2E 加固，而不引入运行时扫描、通用 ORM、全局 EAV 或额外缓存实现。

**Tech Stack:** .NET 10、Dapper、DbUp、SQL Server 2022、MySQL 8、MessagePack-CSharp、FusionCache、Redis、Serilog、OpenTelemetry、Vue 3、Layui 2、Vite、Vitest、Playwright、Microsoft.Testing.Platform、Testcontainers。

## Global Constraints

- Full.NET 1.0 保持强化型模块化单体，跨模块编译依赖只指向公开 Contracts；Composition 是具体模块入口的唯一组合根。
- API、Worker、Migrator 按运行角色分离；API 不携带迁移执行能力，Migrator 不装入完整 HTTP 模块。
- 行为变更先建立可失败契约，再实现最小修复；SQL/数据库行为必须同时验证 SQL Server 与 MySQL。
- 每个 Endpoint 显式声明 `RequireAuthorization(...)` 或 `AllowAnonymous()`；每个 ready/startup 健康端点必须由真实检查支撑。
- 当前阶段只完善 Outbox；Task 17 的 CDC/Kafka Decision Gate 必须最后执行。

---

## 执行顺序总览

| 阶段 | 优先级 | 退出条件 |
|---|---:|---|
| H0 状态与门禁基线 | P0 | 状态矩阵成为 README/路线图唯一总览；CI 可识别危险 SQL 与命名漂移 |
| H1 Seed 闭环 | P0 | 既有 Seed 计划 S0-S2 全部通过 SQL Server/MySQL |
| H1A 测试数据发布物隔离 | P0 | E2E 专用 Contributor、配置与凭据不进入 Identity/Host 发布物，真实栈场景仍可重复建立 |
| H1B 客户端主干门禁恢复 | P0 | Layui 聚焦测试与 `pnpm test:clients` 全部通过，且未放宽断言或等待时间 |
| H2 模块边界与宿主 Profile（重新打开） | P1 | 生产模块只依赖 Contracts；API 无迁移引用；Migrator 只装配 Migration/Seed；架构测试阻止回归 |
| H2A 真实健康与显式 Endpoint 安全意图 | P1 | ready/startup 有真实依赖检查；依赖失败时非 2xx；所有 Endpoint 显式认证或匿名 |
| H3 消息、缓存与日志可靠性 | P1 | 多版本/死信、陈旧窗口、高优先级日志故障场景可验证 |
| H4 浏览器真实链路与共享策略 | P1 | Vue/Layui 在真实后端完成安全关键 E2E；镜像策略收敛 |
| H5 L5 i18n 与工具链治理 | P2 | 首个业务翻译表双库验证；兼容性队列有自动检查 |
| H6 事件交付演进门禁（最后执行） | M5+ Decision Gate | 有真实 SLA 与瓶颈证据后完成 ADR/Provider 选型；未命中则保持纯 Outbox |

当前固定执行顺序为 `Task 3A → Task 3B → Task 4A → Task 4B → Task 4C → Task 4D`，之后再继续 Task 5/6/7/13/16 等既有 P1；Task 4E 可在 4D 后独立交付，Task 4F 是不阻塞 4B～4E 的 Tenancy 存量拓扑后续任务。Task 17 始终最后执行。任何跳过 P0/P1 前置门禁的情况都必须有独立决策记录，不得仅在提交说明中口头放行。

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

**状态：已完成（实现基线见验证记录 `sql-safety-governance-2026-07-21`）。** 破坏性规则由 `scripts/sql/validate-sql-safety.mjs` 扫描；命名仍走既有 Naming Profile，不在安全脚本重复实现。

**Files:**
- Create: `eng/sql-lint.ps1`
- Create: `eng/sql-waivers/README.md`（指向 `contracts/sql-safety/`）
- Create: `contracts/sql-safety/waivers.json`
- Create: `scripts/sql/validate-sql-safety.mjs`
- Modify: `.github/workflows/ci.yml`、`package.json`
- Test: `tests/sql/sql-safety.test.mjs`

1. [x] 测试夹具证明拒绝无 `WHERE` 的 `UPDATE/DELETE`、`TRUNCATE`、`DROP TABLE/COLUMN`、直接 `RENAME`；命名违规由同一 CI 阶段 `pnpm test:naming` 报告。
2. [x] 定义窄范围、带 `backupVerified`/`reviewer`/`removalMilestone` 的精确豁免 Schema。
3. [x] 扫描模块内嵌 SQL、迁移脚本与登记的 C# 静态 SQL；排除测试夹具；禁止目录级通配豁免。
4. [x] 危险 DDL 继续由 SQL Server/MySQL 半完成迁移集成测试验证，Lint 不替代真实数据库。
5. [x] 违规输出含行号、规则码和修复指导。

### Task 3: 执行 Seed Baseline/Overlay 既有计划

**Files:**
- Follow: `docs/superpowers/plans/2026-07-17-seed-data-module.md`
- Modify: `docs/roadmap/capability-status.md`

1. 严格执行既有 S0-S2/Task 1-7，不在本计划复制另一套 Seed 设计。
2. 增加文档断言：不提供通用 Down；开发/Test 重置采用临时数据库重建或受控备份恢复。
3. SQL Server/MySQL 必须覆盖首次、重复、失败重跑、冲突、Production Profile 拒绝、Outbox 幂等和 Secret 脱敏。
4. 只有当前 `--seed-local` 被安全替代、Production 显式门禁生效且双库通过后，状态才从 `Designing` 提升。

### Task 3A: E2E 场景数据移出发布物（2026-07-22 P0）

**批准依据：** [2026-07-22 架构复核](../../verification/architecture-review-2026-07-22.md)与强制规则 `R-20260717-seed-data-boundary`。本任务必须先于剩余 P1 工作执行。

**状态：实现完成，环境验证待补（实现基线 `c463311`，审查修正 `8a09c00`）。** Identity 发布物隔离、测试侧 API 准备脚本、跨页幂等门禁和三宿主 Release 扫描均已关闭；当前机器缺少容器运行时，SQL Server/MySQL 真实栈必须由具备容器能力的 CI 或开发机补跑，补证前不得宣称本任务双库 `Verified`。

**Files:**
- Delete: `src/Modules/Full.NET.Modules.Identity/Seeding/E2eHostViewerSeedContributor.cs`
- Delete: `src/Modules/Full.NET.Modules.Identity/Configuration/IdentityE2eViewerOptions.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Configuration/IdentityOptions.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Delete: `tests/Full.NET.UnitTests/Identity/E2eHostViewerSeedContributorTests.cs`
- Modify: `tests/Full.NET.UnitTests/Identity/HostAdministratorSeedContributorTests.cs`
- Create: `tests/e2e/admin-real-stack/scripts/provision-viewer.mjs`
- Modify: `tests/e2e/admin-real-stack/scripts/bootstrap-stack.mjs`
- Modify: `tests/Full.NET.ArchitectureTests/SeedingDependencyRulesTests.cs`

1. 先增加失败的 Architecture Test：扫描 Identity、Tenancy、Organization 和 Host 发布程序集，拒绝类型名/Contributor 名包含 `E2e`、`TestOnly` 或测试场景配置节；同时保留现有 API/Worker 不执行 Seed 的断言。
2. 删除 Identity 发布程序集中的 E2E Contributor、`Identity:E2eViewer` 配置模型与 DI 注册；更新单元测试，证明模块只注册生产 Baseline Contributor。
3. 在真实栈 API 启动并健康后，由测试目录内 `provision-viewer.mjs` 使用 Bootstrap 管理员经真实 Host 用户/角色/权限/用户角色 API 幂等建立受限查看者；脚本只读取 `FULLNET_E2E_*` 测试环境变量，不向 Host 传入 `Identity__E2eViewer__*`。
4. SQL Server 与 MySQL 真实栈分别验证受限查看者登录、导航裁剪和 API 403；重复执行准备脚本不得创建重复角色或用户。
5. 对 API、Worker、Migrator 执行 Release `dotnet publish` 到临时目录，扫描 E2E 类型名、默认查看者用户名和 `Identity:E2eViewer` 均为零命中；临时目录不得提交。
6. 更新 Seed 验证记录、能力矩阵和测试门槛；执行规则与 Skill 复盘后单独提交。

### Task 3B: 恢复 Layui 用户-机构隶属客户端门禁（2026-07-22 P0）

**状态：已完成。** 测试门禁修复：`b718245eaa3f84e8a626ca1784e2d9dd8f857764`；新鲜验证记录：`e4e9580447d92628a20e5b1f6896ccb1a8a5042f`。

**Files:**
- Modify: `ui/admin-layui/tests/org-user-units.test.js`
- Verify only: `ui/admin-layui/js/core/org-user-units.js`
- Modify: `docs/verification/architecture-review-2026-07-22.md`

**Interfaces:**
- Consumes: `createOrgUserUnitsController(root, { request, translation })`
- Produces: 稳定锁定“首次加载 3 请求 → 创建 1 请求 → 刷新 3 请求”的 Layui 测试契约

- [x] **Step 1: 保留并复现 RED**

  运行 `pnpm --filter @fullnet/admin-layui exec vitest run tests/org-user-units.test.js`，预期在现状下失败为 `expected 7 times, but got 3 times`。禁止先延长 `vi.waitFor` 超时。

- [x] **Step 2: 修正测试夹具的响应顺序**

  保持控制器的并行请求顺序为 `user-units`、`organization/units`、`identity/users`；把 Mock 的第 2 个响应固定为机构分页、第 3 个响应固定为用户分页，第 5～7 个刷新响应保持同一顺序。增加 NthCalledWith 断言锁定 1～7 次请求 URL，创建请求仍必须是第 4 次。

- [x] **Step 3: 证明不是等待时间偶发问题**

  连续运行聚焦命令两次，预期均为 1/1 通过；随后运行 `pnpm test:clients`，预期整个聚合门禁退出码为 0。不得用 `.skip`、降低调用次数或宽泛 `toHaveBeenCalled()` 转绿。

- [x] **Step 4: 同步验证记录并单独提交**

  在本巡检报告的新鲜验证表追加修复提交和通过数量；只提交测试与文档，不混入现有未跟踪日志。

### Task 4: 模块目录、宿主 Profile 与初始化生命周期

**状态：目录与生命周期基础已完成（实现基线 `d5c109c`），模块/角色边界于 2026-07-22 重新打开。** 无真实消费者的 `InitializeAsync` 已删除，Catalog 位于独立 Composition 组合根；但第二轮巡检发现跨模块具体类型依赖、API 迁移能力和 Migrator 完整 HTTP 装配，必须继续执行 Task 4A～4C。

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Modularity/Modules/IFullNetModule.cs`
- Create: `src/Composition/Full.NET.Composition/FullNetHostProfile.cs`
- Create: `src/Composition/Full.NET.Composition/FullNetModuleCatalog.cs`
- Modify: `src/Hosts/Full.NET.Host.Api/Program.cs`
- Modify: `src/Hosts/Full.NET.Host.Worker/Program.cs`
- Modify: `src/Hosts/Full.NET.Host.Migrator/Program.cs`
- Test: `tests/Full.NET.UnitTests/Modularity/ModuleLifecycleTests.cs`
- Test: `tests/Full.NET.ArchitectureTests/HostModuleProfileTests.cs`

1. [x] 以失败测试证明 `InitializeAsync` 是没有执行方的接口承诺。
2. [x] 删除无真实使用者的钩子；未来重引入必须重新定义生命周期与失败门禁。
3. [x] 建立显式 Catalog/Profile，不使用程序集扫描；Worker 只选择模块公开的最小后台入口。
4. [x] Unit Tests 验证 Profile 内容和依赖顺序，Architecture Tests 阻止 Api/Worker/Migrator 绕过共享目录。
5. [ ] 按 Task 4A～4C 关闭模块实现引用、API 迁移执行能力与 Migrator 完整 HTTP 装配；未关闭前不得把 H2 描述为已完成。

### Task 4A: 关闭跨模块实现依赖（2026-07-22 P1）

**状态：已完成（2026-07-22）。** 注册时稳定模块键/依赖快照、Contracts 授权入口、实现引用/生产友元清理及负向架构门禁已经落地；聚焦 Identity/Organization SQL Server/MySQL Integration **6/6** 通过。Task 4B 已关闭 API 迁移执行能力，Task 4C 仍独立开放，模块能力状态保持 `Implemented`。

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Modularity/Modules/IFullNetModule.cs`
- Modify: `src/BuildingBlocks/Full.NET.Modularity/Modules/FullNetModuleRegistry.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Properties/AssemblyInfo.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy.Http/TenancyModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy.Http/Full.NET.Modules.Tenancy.Http.csproj`
- Modify: `src/Modules/Full.NET.Modules.Organization/OrganizationModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Full.NET.Modules.Organization.csproj`
- Modify: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUnits/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Organization/Features/ManageTenantUserUnits/Endpoint.cs`
- Modify: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`
- Modify: `tests/Full.NET.UnitTests/Modularity/ModuleLifecycleTests.cs`

**Interfaces:**
- Produces: `IFullNetModule.Name : string` 作为唯一模块键，`IFullNetModule.Dependencies : IReadOnlyCollection<string>` 只保存稳定模块键
- Consumes: `FullNetPermissionPolicies.For(string permissionCode)` 作为跨模块 Endpoint 的公开授权策略入口

- [x] **Step 1: 写入失败的架构契约**

  新增扫描断言：`src/Modules/*/*.csproj` 不得引用另一个逻辑业务模块的非 `.Contracts` 项目；同一逻辑模块的已有 Core/Http 引用可被依赖测试识别，例如 Tenancy.Http→Tenancy，但这不构成新建 `.Http` 项目的授权，新增项目仍须满足 `rules/development-quality.md` 第 3 节的项目拓扑门禁。生产模块 `AssemblyInfo.cs` 不得把另一个生产模块列入 `InternalsVisibleTo`。现状必须报告 Organization→Identity/Tenancy.Http、Tenancy.Http→Identity 和 Identity→Organization 友元。

- [x] **Step 2: 用稳定模块键替代具体类型依赖**

  将 `Dependencies` 改为字符串模块键；Registry 以 `module.Name` 建立唯一字典，拒绝空键、重复键、未知依赖和循环。Identity 使用 `[]`，Tenancy 使用 `["Identity"]`，Organization 使用 `["Identity", "Tenancy"]`；单元测试分别覆盖确定顺序、未知键和循环。

- [x] **Step 3: 移除实现引用和生产友元**

  Organization Endpoint 改用 `.RequireAuthorization(FullNetPermissionPolicies.For(permissionCode))`，删除对 `Full.NET.Modules.Identity.Authorization` 的引用；随后删除 Organization→Identity、Organization→Tenancy.Http、Tenancy.Http→Identity 的项目引用，以及 Identity 对 Organization 的 `InternalsVisibleTo`。Contracts 引用保持不变。

- [x] **Step 4: 验证边界与行为未漂移**

  运行 Architecture、Unit、Organization/Identity/Tenancy 聚焦 Integration 与 OpenAPI 门禁；对比模块顺序和权限策略名称，预期公开 API、权限码及 Endpoint 行为无变化。

  Architecture **30/30**、Unit **342/342**、使用 Organization.Contracts 最小替身的 TenantProvisioning SQL Server/MySQL **2/2**，以及 Identity 登录、机构管理、用户-机构隶属 SQL Server/MySQL 聚焦 Integration **6/6** 已通过。复核确认原失败不是模块依赖改造造成的行为回归：Identity 导航精确数组未同步新增角色/菜单；Organization 权限夹具遗漏前置租户读取权限并在租户上下文误调用 Host 用户目录；自定义 Guid TypeHandler 未显式设置 `DbType.Guid`，导致 SQL Server 参数成为 `sql_variant`；Organization Endpoint 同时缺少成功响应 OpenAPI 元数据。对应契约与最小实现已修正，未放宽权限断言。

- [x] **Step 5: 复核 Tenancy 存量项目拓扑**

  在实现引用清理与 Host Profile 边界验证完成后，记录 `Tenancy.Http` 的真实非 HTTP Core 消费者、依赖/打包收益和架构测试证据。证据满足门禁则保留并在验证记录中说明；证据不足则新增独立合并任务，不在 Task 4A 中顺手移动类型或改变公开 API。无论结论如何，Tenancy 的存量拆分都不得作为新模块模板。

  复核确认 Worker 确实消费 Core 中的 `TenantProvisionedCacheInvalidationHandler`，但注册入口 `TenancyModule.AddBackgroundServices` 位于 `.Http`，Worker/Migrator 因而仍装载 `.Http` 项目；现有拆分未形成足够的依赖或打包隔离收益。Task 4A 不移动类型、不改变公开 API，合并评估与实施转入下方独立 Task 4F。

### Task 4B: API 移除迁移执行能力（2026-07-22 P1）

**状态：已完成（2026-07-22）。** API 项目与启动注册已移除 DbUp 迁移能力；测试夹具改为启动 API 前直接迁移一次。Architecture **33/33**（含正/反斜杠 Include 与 API 递归项目依赖闭包）、SQL Server/MySQL API 聚焦 **2/2**、双库 migration idempotence **2/2** 通过，Release 发布物的 `.deps.json` 与 DLL 扫描均为零命中。

**Files:**
- Modify: `src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj`
- Modify: `src/Hosts/Full.NET.Host.Api/Program.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/FullNetApiFactory.cs`
- Modify: `tests/Full.NET.ArchitectureTests/DependencyRulesTests.cs`

**Interfaces:**
- Consumes: 测试夹具直接构造的 `DbUpMigrationRunner`
- Produces: 不含 `Full.NET.Migrations.DbUp`、`AddFullNetMigrations` 或 `IDatabaseMigrationRunner` 的 API 发布物

- [x] **Step 1: 新增迁移消费者失败契约**

  架构测试扫描全部 `.csproj`，生产消费者只允许 `Full.NET.Host.Migrator`；测试项目可以显式引用迁移组件。另扫描 API 源码，拒绝 `AddFullNetMigrations` 和 `IDatabaseMigrationRunner`。

- [x] **Step 2: 先让测试夹具脱离 API DI**

  `FullNetApiFactory.InitializeAsync` 保留启动 API 前直接构造 `DbUpMigrationRunner` 的一次迁移，删除从 `Services` 解析并再次执行 `IDatabaseMigrationRunner` 的路径；租户和管理员初始化仍在 API Scope 中执行。

- [x] **Step 3: 删除 API 引用与注册**

  从 API csproj 删除迁移项目引用，从 `Program.cs` 删除命名空间和 `AddFullNetMigrations`。执行 Release publish 后扫描 `.deps.json` 与输出 DLL，预期 API 发布物不包含 `Full.NET.Migrations.DbUp`。

- [x] **Step 4: 运行 API 与双库迁移验证**

  运行 API 聚焦 Integration、SQL Server/MySQL migration idempotence、Architecture Tests；预期测试夹具仍可初始化数据库，而 API 宿主无法解析迁移执行器。

### Task 4C: Migrator 建立最小 Migration/Seed Profile（2026-07-22 P1）

**状态：已完成（2026-07-22）。** Migrator 已改为通过 `IFullNetModule.AddMigrationServices(...)` 只装配 Migration/Seed 最小闭包；Identity/Tenancy 将 Contributor 及其传递依赖从完整 HTTP/认证运行时中拆分，Organization 保持默认空实现。Unit **21/21**、Architecture **3/3**、Seeding SQL Server/MySQL Integration **6/6** 通过，模块与运行角色边界恢复 `Build-verified`。

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Modularity/Modules/IFullNetModule.cs`
- Modify: `src/Composition/Full.NET.Composition/FullNetModuleCatalog.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Tenancy.Http/TenancyModule.cs`
- Modify: `tests/Full.NET.UnitTests/Modularity/ModuleLifecycleTests.cs`
- Modify: `tests/Full.NET.ArchitectureTests/HostModuleProfileTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Seeding/*`

**Interfaces:**
- Produces: `IFullNetModule.AddMigrationServices(IServiceCollection, IConfiguration)`；默认空实现，只由存在 Seed Contributor 的模块显式实现
- Consumes: `FullNetHostProfile.Migrator` 调用每个模块的最小迁移注册入口

- [x] **Step 1: 建立服务集合 RED 快照**

  测试要求 Migrator 能解析所有 `IDataSeedContributor` 及其传递依赖，但不能注册认证 Scheme、动态权限 Provider、CORS、RateLimiter、HTTP JSON Resolver、Endpoint Handler 或后台 Outbox Consumer。现状必须因 Api/Migrator 共用 `AddServices` 而失败。

- [x] **Step 2: 分离每个模块的迁移注册闭包**

  为当前确有 Contributor 的 Identity 与 Tenancy 提取 Contributor、Options、Validator、持久化服务和所需领域服务的最小闭包；`AddServices` 复用该闭包后再追加 HTTP/认证能力，`AddMigrationServices` 只调用闭包。Organization 当前没有 Contributor，保持默认空实现，未来只有新增真实 Seed 时才能加入。不得复制同一 ServiceDescriptor 清单。

- [x] **Step 3: 修改 Catalog 的 Migrator 分支**

  Api 继续 `AddFullNetModule`，Worker 继续 `AddBackgroundServices`，Migrator 只执行 `AddFullNetModularity` 与 `AddMigrationServices`。Catalog 仍是唯一具体模块清单，不在 Migrator Program 恢复手写列表。

- [x] **Step 4: 双库验证 Contributor 完整性**

  运行 Development/Test/Production Seed 的 SQL Server/MySQL Integration，覆盖首次、重复、失败重跑和 Production 拒绝；再运行服务集合快照，证明减少注册没有漏掉 Contributor 依赖，也没有重新装入 HTTP 服务。

### Task 4D: 健康端点提供真实就绪信号（2026-07-22 P1）

**状态：已完成（2026-07-22）。** `/health/live` 继续只表达进程存活；`/health/ready` 现已检查数据库连通性和已配置 Redis；`/health/startup` 通过只读查询 `fn_uuid_contract_state` 验证当前 Schema Contract 已存在。空 `ready/startup` 标签集合在映射时直接失败，避免把空集合 Healthy 当成编排器成功信号。`HealthEndpointTests` 覆盖 SQL Server/MySQL 迁移后健康、数据库断连、缺少 Schema Contract、Redis 不可达与 live 保持 200，当前 **7/7** 通过。

**Files:**
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Health/DatabaseConnectivityHealthCheck.cs`
- Create: `src/BuildingBlocks/Full.NET.Data.Dapper/Health/DatabaseSchemaHealthCheck.cs`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper/ServiceCollectionExtensions.cs`
- Create: `src/BuildingBlocks/Full.NET.Caching.Fusion/Health/DistributedCacheHealthCheck.cs`
- Modify: `src/BuildingBlocks/Full.NET.Caching.Fusion/ServiceCollectionExtensions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Hosting/Observability/ServiceDefaultsExtensions.cs`
- Modify: `src/BuildingBlocks/Full.NET.Hosting/Observability/HealthEndpointExtensions.cs`
- Modify: `src/Hosts/Full.NET.Host.Api/Program.cs`
- Create: `tests/Full.NET.IntegrationTests/Api/HealthEndpointTests.cs`
- Modify: `docs/development/getting-started.md`

**Interfaces:**
- Produces: `live` 只证明进程存活；`ready` 检查当前数据库及已配置的 Redis；`startup` 通过只读 Schema Contract 查询证明所需迁移已经完成
- Consumes: Data.Dapper 内部连接工厂、配置存在时的 `IDistributedCache`；Hosting 只负责端点分组，不反向依赖 Data/Caching

- [x] **Step 1: 写入空集合和依赖失败 RED**

  测试分别覆盖：空 `ready`/`startup` 注册在映射端点时失败；数据库断开时 `/health/ready` 非 2xx；配置 Redis 但不可达时非 2xx；缺少当前 Schema Contract 时 `/health/startup` 非 2xx；live 在上述依赖失败时仍可返回 200。

- [x] **Step 2: 注册稳定标签和真实检查**

  Data.Dapper 注册只读 `SELECT 1` 的 ready 检查和读取 `fn_uuid_contract_state` 当前契约状态的 startup 检查；Caching 仅在 Redis 已配置时注册对固定不存在键执行 `GetAsync` 的 ready 探针。标签只使用 `ready`、`startup`，同一检查可同时属于两组；API Program 在 Data/Caching 注册完成后映射端点。

- [x] **Step 3: 防止健康检查泄密和放大故障**

  HTTP 响应不返回连接字符串、SQL、异常堆栈或内部类型；检查使用短超时且不重试，不在健康请求中执行迁移、Seed、缓存写入或 Outbox 消费。Hosting 项目不得为实现健康检查新增对 Data.Dapper 或 Caching 的项目引用。

- [x] **Step 4: 运行 API 集成与故障注入**

  在 SQL Server/MySQL 分别验证 ready/startup 正常与断连；Redis 场景验证正常、断连、恢复。更新 getting-started 中三类端点的编排器用途和“空集合不得作为成功证据”说明。

### Task 4E: Endpoint 显式声明安全意图（2026-07-22 P2）

**状态：已完成（2026-07-23）。** `/api/v1/tenancy/current` 已显式声明 `AllowAnonymous()`；运行时元数据门禁证明当前 `/api/v1/**` 路由全部显式声明认证或匿名意图。Tenancy API 双库集成进一步锁定匿名已知域返回最小 `TenantSummary` 字段集合、未知域返回标准 ProblemDetails，且响应不泄露账号、角色、权限、连接信息或内部配置。Architecture **1/1**、Tenancy API SQL Server/MySQL Integration **2/2**、`pnpm test:openapi` **14/14** 通过。

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Tenancy.Http/Features/GetCurrentTenant/Endpoint.cs`
- Create: `tests/Full.NET.ArchitectureTests/EndpointAuthorizationTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/TenancyApiAssertions.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/TenancyApiSqlServerTests.cs`
- Modify: `tests/Full.NET.IntegrationTests/Api/TenancyApiMySqlTests.cs`

**Interfaces:**
- Produces: `/api/v1/tenancy/current` 显式 `AllowAnonymous()`；所有 Endpoint 必须显式认证或匿名

- [x] **Step 1: 写入 Endpoint 授权意图 RED**

  架构测试扫描 Endpoint 映射，要求每条路由最终声明 `RequireAuthorization`/权限策略或 `AllowAnonymous`；现状必须只报告 `tenancy/current`。若静态扫描不能可靠理解路由组元数据，改用测试宿主枚举 `EndpointDataSource` 的授权元数据，禁止字符串猜测。

- [x] **Step 2: 固定匿名租户发现契约**

  在 current Endpoint 显式调用 `AllowAnonymous()`；API 测试锁定匿名已知域返回的最小 `TenantSummary` 字段、未知域的标准 ProblemDetails，以及响应不包含账号、角色、权限、连接信息或内部配置。

- [x] **Step 3: 验证全路由与 OpenAPI**

  运行 Architecture、SQL Server/MySQL Tenancy API Integration 和 `pnpm test:openapi`；在验证记录说明匿名是既有行为显式化，不是放宽授权。当前没有 tenancy/current 的独立 OpenAPI 基线，不得顺手创建与本任务无关的全局快照。

### Task 4F: Tenancy 存量 Core/Http 拓扑评估与合并（2026-07-22 P2）

**状态：已完成（2026-07-23）。** 复核确认 Worker 的真实非 HTTP 消费者只有 Core 内的 `TenantProvisionedCacheInvalidationHandler`，而 `AddBackgroundServices`、中间件与 Endpoint 映射却挂在 `Full.NET.Modules.Tenancy.Http`，未形成可验证的依赖或打包隔离收益。本任务已将 `TenancyModule`、`TenantResolutionMiddleware` 与三个 Tenancy Endpoint 合并回 `Full.NET.Modules.Tenancy` 主项目，组合根改为直接引用主项目，历史 `.Http` 项目与相关测试引用已删除，并补齐“宿主不得再通过 Web 拆分项目获取后台能力”的架构门禁。新鲜验证为：Architecture **36/36**、Unit **343/343**、Tenancy API + TenantProvisioning SQL Server/MySQL Integration **4/4**、Development/Production Seed SQL Server/MySQL Integration **4/4**、`pnpm test:openapi` **14/14** 通过；Api/Worker/Migrator Release 发布物扫描 `Full.NET.Modules.Tenancy.Http` 为 **0** 命中。

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Tenancy/**`
- Delete: `src/Modules/Full.NET.Modules.Tenancy.Http/**`
- Modify: `src/Composition/Full.NET.Composition/Full.NET.Composition.csproj`
- Modify: `tests/Full.NET.ArchitectureTests/*`
- Modify: `tests/Full.NET.UnitTests/*`
- Modify: `tests/Full.NET.IntegrationTests/Api/*`
- Modify: `tests/Full.NET.IntegrationTests/Seeding/*`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`

- [x] **Step 1: 以发布物与依赖图建立 RED**

  锁定 Api/Worker/Migrator 的项目依赖和发布物清单，证明 Worker 只需要 Core 事件处理器却因组合入口装载 `.Http`；测试必须区分“真实非 HTTP 消费者存在”和“独立 `.Http` 项目有隔离收益”。

- [x] **Step 2: 选择并记录最小拓扑**

  优先评估把现有 Tenancy Core/Http 合并为一个主项目；只有量化发布隔离、许可或独立消费者证据满足项目拆分门禁时才保留双项目。不得新增第三个项目或借机改变 Endpoint、Contracts、权限码和序列化契约。

- [x] **Step 3: 实施并收紧回归门禁**

  迁移组合入口和项目引用，删除失去消费者的项目边界；Architecture Tests 阻止宿主重新通过 Web 项目获取后台能力，也阻止其他模块复制该历史拓扑。

- [x] **Step 4: 验证宿主与双库行为**

  运行 Release build、Architecture/Unit、SQL Server/MySQL Tenancy API、TenantProvisioning、Seed、Worker Outbox 与 OpenAPI 门禁，并扫描三宿主发布物；任何公开 API 或后台事件行为变化都必须停止并拆成独立契约任务。

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

**状态：已完成（2026-07-23）。** 当前实现采用“并行版本 Handler + 精确 `SchemaVersion` 路由”的最小闭环，而不额外引入升级链；`OutboxWorker` 已参数化 `BatchSize`/`LeaseSeconds`/`PollMilliseconds`/`MaxAttempts`，永久失败与超过最大尝试次数的消息会写入死信终态并保留稳定原因码。新鲜验证为：`OutboxProcessorTests` + `IntegrationEventHandlerMatcherTests` **11/11**、`OutboxRecoveryTests` 与死信迁移恢复聚焦双库 Integration **10/10**、完整 Unit **348/348**、Integration 发现数 **124**；运维边界见 [`docs/operations/outbox-worker-topology.md`](../../operations/outbox-worker-topology.md)。

**Files:**
- Modify: `src/BuildingBlocks/Full.NET.Abstractions/Messaging`
- Modify: `src/BuildingBlocks/Full.NET.Data.Abstractions`
- Modify: `src/BuildingBlocks/Full.NET.Data.Dapper`
- Modify: `src/Hosts/Full.NET.Host.Worker/OutboxProcessor.cs`
- Create: `src/Hosts/Full.NET.Host.Worker/OutboxWorkerOptions.cs`
- Add: SQL Server/MySQL forward-only migrations
- Create: `docs/operations/outbox-worker-topology.md`（或写入 getting-started 运维小节）
- Test: `tests/Full.NET.UnitTests/Messaging/IntegrationEventHandlerMatcherTests.cs`
- Test: `tests/Full.NET.UnitTests/Outbox/OutboxProcessorTests.cs`
- Test: `tests/Full.NET.IntegrationTests/Messaging/OutboxRecoveryTests.cs`

1. 先建立 V1 旧载荷、V2 当前载荷、未知版本、不可信载荷和缺 Handler 的失败样例。
2. 支持并行版本 Handler 或相邻版本升级链；升级器必须使用显式旧版本契约，禁止 Typeless。
3. 区分瞬时/永久失败，加入最大尝试、死信状态、错误摘要和可审计人工重放。
4. 双库验证多 Worker 租约、坏消息不阻塞批次、重启恢复、版本退役扫描；**至少**覆盖两并发领取进程（同库）下无重复成功处理、租约过期回收。
5. 文档记录兼容窗口和“先消费者、后生产者、最后退役”发布顺序。
6. **部署拓扑（2026-07-21 吸收）**：书面明确默认依赖数据库租约的多副本安全模型；BatchSize/Lease/Poll 改为 Options；在证明租约压力场景前，**禁止**把“必须上 Redis Leader Election”写成唯一解。若生产强制单副本，必须在运维文档与 Compose/Helm 示例中写死 `replicas: 1` 及风险说明。

### Task 7: 缓存一致性等级与故障注入

**状态：最小闭环已完成并于 2026-07-26 复核。** Tenancy 域名解析缓存已按规则实现“提交后本机同步失效 + Outbox 跨节点修复”的最小安全边界：`TenantProvisioningService` 在事务命令成功返回后只修复当前请求节点，且提交后的本机清理不再受请求取消影响；该路径禁止承担 Backplane 可靠交付。Worker 通过 `TenantProvisionedCacheInvalidationHandler` 同步等待 Backplane 发布完成并让广播异常冒泡，只有发布成功才允许 Outbox 消息进入已处理状态，失败则释放租约并安排重试。`CacheConsistencyTests` 已覆盖 SQL Server/MySQL 的本机负缓存修复、两个 API 节点 + Redis + Worker 的可观测精确失效、Backplane 不可达时 Outbox 不确认并重试，以及 Redis 不可达时主节点提交后仍立即可见，聚焦 Integration **6/6** 通过。延迟 Worker、指标暴露与完整 S0/S1/S2 分级仍待后续步骤补齐，因此本任务尚不能标记为 `Verified`。

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

### Task 13: PR 集成冒烟加宽（2026-07-21 吸收）

**状态：已完成（2026-07-23）。** PR 快门禁已从单纯双库迁移 2 项冒烟扩展为 Identity/Tenancy/Outbox 核心双库组合：迁移 Outbox schema、登录契约、匿名租户发现与 TenantProvisioning 写 Outbox。当前稳定 filter 新鲜运行 **8/8** 通过，墙钟 **3m 42s**，继续保持 `push main` 执行全量 Integration **126** 项与 90m 超时不变。

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `docs/development/getting-started.md`
- Modify: `README.md`
- Modify: `.agents/skills/fullnet-module-delivery/references/delivery-map.md`
- Modify: `docs/verification/test-threshold-audit-2026-07-19.md`
- Test: 现有 Integration 用例的稳定 filter 组合（不新建慢套件）

1. 保持 `push main` 全量 **85**（或当时门槛）与 90m 超时不变。
2. 将 PR 门禁从仅 `migration_is_idempotent_and_creates_binary_outbox_schema`（2 项）扩展为 Identity/Tenancy/Outbox 核心场景 filter；目标墙钟 **≤15m**，失败即阻断合入。
3. filter 必须点名稳定测试名或明确命名前缀；禁止用过于宽泛的正则把 PR 拖回全量矩阵。
4. 同步 README/getting-started/delivery-map 对“日常/PR/发布”三档的说明；更新门槛审计。
5. 数据库结构或 Outbox SQL 变更的 PR 仍须额外跑相关聚焦 filter 或全量，不能只靠加宽后的冒烟宣称双库完成。

### Task 14: 首个业务纵向切片跟踪

业务实现不在本硬化计划内展开，统一执行 [`2026-07-21-identity-user-management-vertical-slice.md`](2026-07-21-identity-user-management-vertical-slice.md)。本 Task 仅要求：硬化门禁与用户管理切片并行时，Outbox/SQL 守卫变更不得破坏切片测试；切片合入后回头补 Architecture Tests（模块表所有权、SqlDataScope 显式性）。

### Task 15: Identity 组合根按职责拆分（P2）

**Files:**
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityAuthenticationExtensions.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityAuthorizationExtensions.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityDomainServiceExtensions.cs`
- Create: `src/Modules/Full.NET.Modules.Identity/DependencyInjection/IdentityHttpPolicyExtensions.cs`
- Create: `tests/Full.NET.UnitTests/Identity/IdentityModuleRegistrationTests.cs`

1. 先以服务描述符快照建立失败测试，覆盖认证 Scheme、Options Validator、授权 Handler、Command Handler、Seed Contributor、CORS、限流和 JSON Context；重复调用 `AddServices` 后不可出现非预期重复注册。
2. 将认证/Token/TOTP 注册移入 `AddIdentityAuthentication`，授权 Catalog/Policy/DataScope 移入 `AddIdentityAuthorization`，业务服务/Handler/Validator/Seed 移入 `AddIdentityDomainServices`，CORS/限流/JSON 移入 `AddIdentityHttpPolicies`。
3. `IdentityModule.AddServices` 只按固定顺序调用上述私有扩展；不得新增程序集扫描、Service Locator、公共扩展入口或改变生命周期。
4. 运行 Identity 单元测试、Architecture Tests、登录/刷新/权限/Seed 聚焦集成测试以及 Vue/Layui 登录真实栈冒烟；快照差异必须为零。
5. 只报告结构调整，不提升任何能力状态；完成规则与 Skill 复盘后提交。

### Task 16: 可信代理后的客户端地址与限流（生产前 P1）

**Files:**
- Modify: `src/Hosts/Full.NET.Host.Api/Program.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Api/Forwarding/TrustedProxyOptions.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Api/Forwarding/TrustedProxyOptionsValidator.cs`
- Create: `src/BuildingBlocks/Full.NET.Hosting/Api/Forwarding/ServiceCollectionExtensions.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/IdentityModule.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/Login/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/RefreshSession/Endpoint.cs`
- Modify: `src/Modules/Full.NET.Modules.Identity/Features/Logout/Endpoint.cs`
- Create: `tests/Full.NET.IntegrationTests/Api/TrustedProxyForwardingTests.cs`
- Modify: `docs/development/getting-started.md`

1. 先建立失败测试：无可信代理配置时伪造 `X-Forwarded-For` 不得改变客户端地址；来自显式可信代理/网络且转发层数不超限时才接受最右侧受信链解析结果；无效 IP、超长链和未知代理必须拒绝或回退连接地址。
2. 使用 ASP.NET Core `ForwardedHeadersMiddleware`，只启用所需 Header，显式配置 `KnownProxies`/`KnownNetworks` 与 `ForwardLimit`；Production 配置为空时不得自动信任任意代理。
3. `UseForwardedHeaders` 必须位于请求日志、限流、认证和 Endpoint 之前。限流与登录/刷新/退出审计统一读取中间件规范化后的 `Connection.RemoteIpAddress`，不在业务模块自行解析 Header。
4. 覆盖直连、单层可信代理、多层可信链、恶意客户端伪造和 IPv4/IPv6；验证相同真实客户端共享限流分区、不同客户端不因代理地址合并。
5. 文档记录 Aspire、Nginx/Kubernetes 的可信代理配置示例和错误配置风险；运行 Hosting/Identity 单元与集成测试后提交。

### Task 17: Outbox 后的 CDC/Kafka 演进门禁（M5+，最后执行）

**批准依据：** [总体架构 Spec §9.1](../specs/2026-07-17-fullnet-architecture-design.md#91-事件交付演进基线)与[2026-07-22 架构复核](../../verification/architecture-review-2026-07-22.md)。Task 3A、5、6、7、8、11、13 及当前核心业务模块未完成前，不得开始本任务。

**Files:**
- Create: `tests/performance/outbox/README.md`
- Create: `tests/performance/outbox/outbox-throughput.js`
- Create: `docs/verification/event-delivery-capacity-gate.md`
- Create on gate pass only: `docs/architecture/adr/ADR-0005-event-delivery-provider.md`
- Modify on gate pass only: `docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md`
- Create on gate pass only: `docs/superpowers/specs/2026-07-22-event-delivery-provider-design.md`
- Create on gate pass only: `docs/superpowers/plans/2026-07-22-event-delivery-provider.md`

1. 从真实消费者登记稳定事件目录：事件类型、可靠性、可丢失/可重算预算、顺序键、Payload P50/P95/P99、持续/峰值速率、端到端延迟和保留期；缺少任一字段即停止，不以 `1000 QPS` 默认值替代。
2. 对已完成 Task 6 的 Outbox 在 SQL Server/MySQL 分别压测单/多 Worker、不同 Batch/Lease/Poll、正常消费、慢消费者、进程崩溃和恢复；记录数据库 CPU/IO/锁、队列深度、最老消息年龄、重复率和 P95/P99 延迟。
3. 先调优索引、批量、Payload 与消费者并复测。两库均满足 SLA 时，记录“保持纯 Outbox”并停止，不创建 Kafka/CDC 依赖。
4. 仅在可靠业务事件仍有可复现瓶颈时，评估 Outbox + CDC Relay + Kafka；仅对可丢失、可重算且无事务原子要求的流量评估直接 Kafka。禁止运行时按 QPS 动态切换，禁止轮询与 CDC 同时拥有同一事件流。
5. Gate Pass 前核对 SQL Server CDC/MySQL Binlog 权限与保留、Connector Offset/恢复、Kafka ACL/TLS/分区/Schema/DLQ/重放/监控、至少一次消费幂等、许可证、成本、RPO/RTO 和责任人；任何一项缺失即保持 Decision Gate。
6. Gate Pass 后才创建 ADR，对比保持轮询、CDC Relay 和直接 Kafka，并锁定 Provider、版本、事件目录、切换/排空/回退流程；随后按文档分层创建独立 Spec 与逐步实施计划。本 Task 本身不安装 Broker、不修改产品运行时。

## 完成门禁

每个 Task 完成时必须：

1. 运行风险相称的 Unit、Architecture、Compatibility、SQL Server/MySQL Integration、前端单测/构建/E2E；
2. 直接运行 Microsoft.Testing.Platform 测试 DLL，并更新准确的 `--minimum-expected-tests`；
3. 更新能力状态矩阵、受影响规格、README、路线图和验证记录；
4. 完成规则与 Skill 演进复盘；
5. 执行 `git diff --check` 和 `git status`，不得把未执行写成通过。
