# Full.NET 当前能力状态矩�?

- 快照日期�?026-07-25
- 基线提交：本文件所在提�?
- 文档职责：作为“当前能用到什么程度”的唯一总览；详细范围仍由各规格、路线图和验证记录负�?
- 更新规则：每次里程碑、公开发布和能力状态变化时更新；没有可定位证据不得提升状�?

## 1. 状态定�?

| 状�?| 含义 |
|---|---|
| `Planned` | 已进入路线图，但尚未形成可实施规�?|
| `Designing` | 规格或实施计划正在形成，尚不能作为可用能�?|
| `Implemented` | 实现已经存在，但尚未完成本能力要求的全部构建、集成或人工验收 |
| `Build-verified` | 当前目标的编译、静态检查或自动化测试已有记录，但仍缺真实环境、双库、跨端或人工验收中的至少一�?|
| `Verified` | 规格定义的自动化、真实依赖、双库、跨端和必要人工验收全部具备可定位证�?|
| `Decision Gate` | 只有命中明确业务条件后才进入设计，不属于默认承诺 |

`Implemented` 不等于生产就绪；“测试文件存在”也不等�?`Build-verified`。本快照引用的是基线提交及仓库现有记录，不替代发布前的新鲜验证�?

## 2. 当前可用范围

| 能力 | 状�?| 当前证据 | 主要缺口/下一门禁 |
|---|---|---|---|
| 模块化单体、显式模块依赖与宿主 Profile | `Build-verified` | `Full.NET.Modularity`、`Full.NET.Composition`、Api/Worker/Migrator 显式 Profile；Task 4A 已关闭跨模块实现引用和生产友元；Task 4B �?33 �?Architecture Tests（含跨平�?Include �?API 递归项目依赖闭包）、双�?API/迁移聚焦验证�?Release 发布物零命中关闭 API 迁移执行能力；Task 4C 已新�?`IFullNetModule.AddMigrationServices(...)`，Migrator 仅装�?Migration/Seed 最小闭包，Unit **21/21**、Architecture **3/3**、Seeding Integration **6/6** 通过；Task 4D 已补齐真实健康探针，Task 4E 已为全部 `/api/v1/**` 路由建立显式认证/匿名门禁，Architecture **1/1**、Tenancy API SQL Server/MySQL **2/2**、OpenAPI **14/14** 通过；Task 4F 已将 Tenancy 历史 Core/Http 拆分合并回单主项目，并以 Architecture **36/36**、Unit **343/343**、Tenancy API + TenantProvisioning 双库 Integration **4/4**、Development/Production Seed 双库 Integration **4/4** 及三宿主 Release 发布�?`Full.NET.Modules.Tenancy.Http` **0** 命中关闭存量拓扑债务 | H2/H2A 的当前代码门禁已关闭；后续仍需更广真实环境、编排与运维证据，完成前不能标记�?`Verified` |
| 跨栈命名治理与生成器命名内核 | `Build-verified` | `contracts/naming/`、`pnpm test:naming`�?3 项）�?10/011 双库迁移�?9 �?Naming Integration 矩阵 + **发布候选逻辑克隆升级演练** 2 项（见[1.0 前规范化验证](../verification/pre-v1-naming-normalization.md)）、[命名治理](../verification/naming-governance.md)；债务 **87** �?| 真实生产维护窗口与备份介�?RPO/RTO、协议别名排空与客户�?E2E 升级路径未实跑；动�?SQL 仍须人工审查；完整业务模板与重复生成快照未交付，因此不能标记�?`Verified` |
| Dapper-first、事务与租户 SQL 作用�?| `Build-verified` | Data BuildingBlocks；QueryMultiple 顺序/完整消费�?SQL Server/MySQL 真实测试 | `TenantRequired` 仍需从参数文本检查升级为受控语义元数据，Global Statement 需精确目录；SqlBuilder 只在真实消费者命中门禁后引入 |
| UUID v7 主键与跨库物理存�?| `Build-verified` | `UuidStorageContractV1`�?08/009 双库迁移、`PrimaryKeyTypeMapping`、`validate-uuid-storage-sql`�?10+ 门禁）、UUID 集成测试（Expand/Contract/Recovery 31 项）、应用持久化/外部契约测试、Runbook 与[自动化恢复演练记录](../verification/uuid-v7-primary-key-storage-2026-07-19.md)、真实栈 MySQL E2E �?Binary16；当前声明门�?**349/7/38/156** | 真实生产维护窗口与整库备份恢�?RPO/RTO 实跑、SQL Server 聚集索引性能基准尚未完成 |
| SQL Server / MySQL DbUp 迁移 | `Build-verified` | 双库迁移测试（Integration **103** 项）�?10/011 Naming Expand/Contract、迁移文件配对与 CI SQL 命名 Lint；破坏�?DDL/�?WHERE 写操作由 [`pnpm test:sql-safety`](../verification/sql-safety-governance-2026-07-21.md) 强制 | 通用半完成迁移扫描仍依赖既有双库恢复用例；动�?SQL 精确债务须持续人工审�?|
| MessagePack Outbox、租约、重�?| `Build-verified` | Outbox 表、Worker、`MessageType + SchemaVersion` 精确路由、`OutboxWorker` Options（Batch/Lease/Poll/MaxAttempts）、`DeadLetteredAtUtc`/`DeadLetterReasonCode` 双库迁移、`OutboxProcessorTests` + `IntegrationEventHandlerMatcherTests` **11/11**、`OutboxRecoveryTests` + 死信迁移恢复聚焦双库 Integration **10/10**、完�?Unit **348/348**，以及[Outbox Worker 运维说明](../operations/outbox-worker-topology.md) | 相邻版本升级�?版本退役扫描、真实多副本压力基准和受控人工重放自动化仍待补；未完成前不能标记�?`Verified` |
| CDC Relay / Kafka 事件交付 | `Decision Gate` | [总体架构 Spec §9.1](../superpowers/specs/2026-07-17-fullnet-architecture-design.md#91-事件交付演进基线)、[2026-07-22 复核](../verification/architecture-review-2026-07-22.md) | 当前不实现；Outbox 生产闭环、真实消费者、SLA、双�?CDC 运维能力和瓶颈基准全部具备后，才创建 ADR/Provider 规格与实施计�?|
| FusionCache + `.AsHybridCache()` | `Build-verified` | 单一实现、L2/Backplane、全局关闭 Fail-Safe；Tenancy 域名解析缓存已实现“提交后本机同步失效 + Outbox 跨节点修复”最小闭环；`CacheConsistencyTests` �?SQL Server/MySQL 上以�?API 节点场景锁定“先负缓存、再创建租户、主节点立即可见而第二节点在 Outbox 处理前仍陈旧”，聚焦 Integration **2/2** 通过 | Redis/Backplane 中断、Redis 不可用与恢复、延�?Worker、多实例指标和更�?S0/S1/S2 分级仍待补；完成前不能标记为 `Verified` |
| 健康检查与编排器就绪信�?| `Build-verified` | `/health/live`、`/health/ready`、`/health/startup` 已完成真实门禁：�?`ready/startup` 标签集合在映射时直接失败；Data.Dapper 提供数据库连通性与 `fn_uuid_contract_state` Schema Contract 探针；Caching 在配�?Redis 时追�?ready 探针。`HealthEndpointTests` 覆盖 SQL Server/MySQL 迁移后健康、数据库断连、缺�?Schema Contract、Redis 不可达与 live 保持 200，当�?**7/7** 通过 | 仍缺编排器环境下 Redis 恢复演练与真实发布编排文档截图；补齐真实发布与运维证据前仍不能标记为 `Verified` |
| 标准 HTTP + ProblemDetails | `Build-verified` | API、兼容测试、Admin.NET 适配�?| OpenAPI 破坏性变更门禁和多客户端生成待补 |
| System.Text.Json 源生成基础 | `Implemented` | 模块 JSON Context �?HTTP 契约 | 后续 DTO 必须持续纳入源生成和兼容测试 |
| 高并发结构化日志�?OpenTelemetry | `Implemented` | 有界异步 Serilog、队列监控、OTel | Warning/Error 独立高优先级通道和降级演练未实现 |
| Identity 会话安全基础 | `Build-verified` | 登录、事务轮换、重�?family 撤销、逐请�?Session/账号/安全戳校验、CSRF/CORS/Origin、Refresh/Logout 限流与审计测试；`SessionRaceAssertions` 双库集成�?0 项门槛） | 事务故障注入�?Redis 分布式会话未实现 |
| Tenancy 可信上下文切�?| `Build-verified` | 租户解析、可用租户、切换与刷新集成测试�?*Host 租户管理**见[验证](../verification/tenancy-host-tenant-management-2026-07-23.md)�?*Host 租户套餐目录**见[验证](../verification/tenancy-host-tenant-package-management-2026-07-23.md)�?*租户-套餐绑定**见[验证](../verification/tenancy-host-tenant-package-assignment-2026-07-23.md)�?*开通可选套�?*见[验证](../verification/tenancy-provision-with-package-2026-07-24.md) | 过期/配额策略、独立库租户与完整对标验收未完成；不能宣传为完整租户后台 |
| 最�?RBAC 与权限导�?| `Build-verified` | **Host 用户**（含**用户-角色分配**）�?*Host 角色**（含**数据范围**）�?*运行时多角色数据范围并集**（租户机构与用户-机构隶属只读过滤）�?*Host 菜单**�?*Host 租户管理**�?*租户机构**�?*用户-机构隶属** API + 双端 UI + 双库集成 + Mock/真实栈冒�?+ OpenAPI 夹具（见[用户验证](../verification/identity-user-management-2026-07-21.md)、[用户-角色分配验证](../verification/identity-user-roles-assignment-2026-07-21.md)、[运行时数据范围验证](../verification/identity-runtime-data-scope-2026-07-21.md)、[角色验证](../verification/identity-role-management-2026-07-21.md)、[角色数据范围验证](../verification/identity-role-data-scope-2026-07-21.md)、[菜单验证](../verification/identity-menu-management-2026-07-21.md)、[租户管理验证](../verification/tenancy-host-tenant-management-2026-07-23.md)、[机构验证](../verification/organization-unit-management-2026-07-21.md)、[用户-机构隶属验证](../verification/organization-user-unit-assignment-2026-07-21.md)�?| 其他业务模块全面接入机构过滤 |
| Settings Host 数据字典 | `Build-verified` | 首个 Settings 模块；`020_SettingsDictionary.sql`；字典类�?�?CRUD API；双�?UI；OpenAPI **20/20**；Parity 类型 **2/2** + �?**2/2**；真实栈 `host-dict-types` SQL Server **4/4** + MySQL **4/4**；Integration SettingsApi 双库 **2/2**（见[验证记录](../verification/settings-dictionary-2026-07-25.md)�?| 租户级字典、字典缓存失效、L5 字典文本翻译与强类型配置消费者未交付；全量真实栈矩阵依赖 CI；不能标记为 `Verified` |
| Settings Host 系统配置 | `Build-verified` | `021_SettingsConfigEntry.sql`；配置项 CRUD API（含 by-key、ValueKind 校验）；双端 UI；OpenAPI 夹具；Parity **2/2**；真实栈 `host-config-entries` SQL Server **4/4** + MySQL **4/4**（见[验证记录](../verification/settings-system-config-2026-07-25.md)�?| 租户/用户覆盖、`ISettingsStore<T>`、加密与 L5 说明多语言未交付；不能标记�?`Verified` |
| Settings Host 枚举/常量元数据 | `Build-verified` | `IEnumCatalogContributor` + Registry；只读 API；首批 `settings.config_value_kind`；双端只读页；Integration **2/2**；见[验证记录](../verification/settings-enum-catalog-2026-07-25.md) | 跨模块 Contributor 扩展、代码生成消费与 L5 标签未交付；不能标记为 `Verified` |
| Auditing Host 操作日志 | `Build-verified` | `023_AuditingOperationLog.sql`；已认证写操作中间件；Host 查询 API；双端只读页；Integration **2/2**；见[验证记录](../verification/auditing-operation-log-2026-07-25.md) | 业务显式埋点与保留清理未交付；不能标记为 `Verified` |
| Auditing Host 访问日志 | `Build-verified` | 新模块 `Full.NET.Modules.Auditing`；`022_AuditingAccessLog.sql`；中间件尽力写入；Host 分页查询 API；双端只读页；Integration **2/2**；见[验证记录](../verification/auditing-access-log-2026-07-25.md) | 保留清理、可靠 Outbox 审计通道未交付；不能标记为 `Verified` |
| Auditing Host 异常日志 | `Build-verified` | `024_AuditingExceptionLog.sql`；捕获中间件尽力写入后重抛；Host 查询 API；Testing 探针；双端只读页；Integration **2/2**；见[验证记录](../verification/auditing-exception-log-2026-07-25.md) | 告警通道与保留清理未交付；不能标记为 `Verified` |
| Organization 职位管理 | `Build-verified` | `025_OrganizationPosition.sql`；租户职位 CRUD；双端 UI；Integration **2/2**；见[验证记录](../verification/organization-position-2026-07-25.md) | 职级、职位-机构绑定未交付；不能标记为 `Verified` |
| Organization 用户-职位隶属 | `Build-verified` | `026_OrganizationUserPosition.sql`；租户用户-职位分配；双端 UI；Integration **2/2**；见[验证记录](../verification/organization-user-position-assignment-2026-07-25.md) | 职级、职位-机构绑定未交付；不能标记为 `Verified` |
| Files Host 文件元数据 | `Build-verified` | `027_FilesFile.sql`；上传/列表/下载/软删除；本地存储 Provider；双端 UI；Integration **2/2**；Mock parity **2/2**；真实栈 `host-files` SQL Server **4/4** + MySQL **4/4**；见[验证记录](../verification/files-host-file-metadata-2026-07-26.md) | S3/OSS Provider、租户文件；全量真实栈矩阵依赖 CI；不能标记为 `Verified` |
| Identity Host 在线用户与强制下线 | `Build-verified` | 活跃 refresh 会话分页列表；撤销 family 强制下线；权限 `identity.sessions.read`/`write`；双端 UI；Integration **2/2**；Mock parity **2/2**；真实栈 `host-online-sessions` SQL Server **4/4** + MySQL **4/4**；见[验证记录](../verification/identity-host-online-sessions-2026-07-26.md) | 实时推送、租户作用域在线用户；全量真实栈矩阵依赖 CI；不能标记为 `Verified` |
| 受保护超级管理员 | `Implemented` | 005/006 双库迁移、动�?Catalog 权限、逐请求会话校验、远程授�?撤销 API、当前密码重认证�?*TOTP 强认�?Provider + ADR-0004 Production 三条件解�?*（见[TOTP 验证](../verification/identity-totp-strong-reauth-2026-07-21.md)）、含 ActorUserId 的事务审计、双库最后一名保护�?*禁用最后一名超管拒�?*、Vue/Layui 对等管理页与 Mock E2E�?*双端 TOTP 登记/确认 UI**（见[TOTP UI 验证](../verification/identity-totp-admin-ui-2026-07-21.md)）�?*超管管理页真实栈授予/撤销 E2E**（见[真实栈验证](../verification/identity-super-admin-real-stack-2026-07-21.md)）；真实�?`permission-denied` 覆盖受限账号 API/UI 403 | Production TOTP 强制路径真实栈；账号硬删�?API 不在 1.0 范围 |
| Vue 管理壳层 | `Build-verified` | Art Design Pro 已迁�?`ui/admin/src/framework/art-design/`（四种菜单布局、设置抽屉、标签页/面包屑、全局搜索、通知/聊天演示面板、`FullNetChart`）；边界测试 **2/2**、Vue 单测 **140/140**；双端壳�?E2E 见[菜单布局验证](../verification/admin-art-design-pro-menu-layout-2026-07-24.md)与[迁移验收](../verification/admin-art-design-pro.md)；a11y E2E 覆盖 320px/键盘/主题 | Tiptap 富文本、NVDA/强制颜色人工验收、通知/聊天真实 API；完成前不能标记�?`Verified` |
| Vue 图表与双管理端富文本 | `Designing` | ECharts 6.1 �?Tiptap Core 3.28 已完成选型和边界设计；`FullNetChart` 已落�?| 依赖、主题、懒加载、服务端 HTML 净化、Files 上传�?Vue/Layui Adapter 尚未实现 |
| Layui 管理壳层 | `Build-verified` | Vite/Vitest **81/81**、Art 对等壳层（`shell-art-settings`/`shell-layout`/`shell-tabs`/`shell-topbar`/`shell-global-search`/`shell-notification-panel`/`shell-chat-drawer`）；�?Vue 同场景双�?E2E�?*长期并行**（所有�?2026-07-21 确认�?| 会话/HTTP/导航已收敛到 `@fullnet/client-contracts`；业务切片须�?Vue 同步；人�?a11y 与真�?API 通知未验�?|
| 双管理端真实后端浏览器联�?| `Build-verified` | `tests/e2e/admin-real-stack`：Testcontainer SQL Server/MySQL + Migrator Development Seed + 真实 API；受限查看者已移出发布物，API 健康后由测试脚本�?Host 管理 API 幂等准备；Vue/Layui **84** 项真实栈（登�?刷新/�?Tab/租户/**Host 租户**（含**分配套餐**�?**Host 租户套餐**/**Host 数据字典**/**Host 系统配置**/**Host 枚举常量**/**Host 访问日志**/**Host 操作日志**/**Host 异常日志**/**Host 文件管理**/**Host 在线用户**/用户/角色/菜单/机构与用�?机构隶属列表与权限裁�?*/超级管理员授予撤销/ProblemDetails/403/权限拒绝/退出）；Mock parity 全量预计 **92** 项（`shell-parity` **56** 含租户开通可选套餐、租户列表分配套餐、套餐引用禁用失败、数据字典类�?�?CRUD、系统配�?CRUD；另�?accessibility / menu-layout）；租户上下文导�?Host 目录 SQL 作用域修复见[验证](../verification/identity-tenant-navigation-host-sql-scope-2026-07-21.md)；CI `real-stack-e2e` + `real-stack-e2e-mysql`（main�?| Task 3A 当前机器缺容器运行时，新的查看者准备路径尚未重�?SQL Server/MySQL；Redis 未纳入真实栈；Overview「检查会话」级 API 403 UI 探针仍依�?mock parity；租户内可分�?Host 用户 API 仍开放；不能标为 `Verified` |
| uni-app H5/微信/支付宝基础 | `Build-verified` | 96 项单测、类型检查、三目标 CLI 构建、H5 E2E | uni-ui 已选定但尚未引入；微信/支付宝开发者工具、真机及真实后端会话未验�?|
| Flutter 移动/桌面客户�?| `Designing` | Flutter 3.44、Material 3 + Cupertino、平台与多语言边界已确�?| 工程、设计令牌映射、构建节点、登�?API 冒烟均未实现 |
| 全栈多语言 L0-L3 | `Build-verified` | 服务端、双管理端、uni-app 自动化记�?| L4 Flutter �?L5 业务内容/异步消息仍为设计状�?|
| 模块�?Seed Baseline/Overlay | `Build-verified` | Migrator 两阶�?Seed、双�?Development/Test/Production 契约、Production �?Secret 拒绝；Identity 生产程序集只注册 Baseline Contributor，Task 3A 发布物扫描零命中（见[种子双库验证](../verification/seed-dual-database-contract-2026-07-21.md)、[Production Secret 验证](../verification/seed-production-secret-and-super-admin-disable-2026-07-21.md)、[运维 Runbook](../operations/seed-production-baseline.md)；门�?**349/7/38/156**�?| Task 3A 当前机器缺容器运行时，更新后的双�?Seed/真实栈未重跑；完�?Aspire/CI Profile E2E 仍开放；Production 远程超管写已可按 ADR-0004 开启，不能标记�?`Verified` |
| SignalR / Realtime | `Build-verified` | 架构边界已定�?| 抽象、鉴权分组、MessagePack Hub、Redis Backplane 尚未实现 |
| gRPC 服务通信 | `Planned` | 架构边界已定�?| 首次真实服务拆分前不引入 |
| AI / Agent / MCP / Agentic Web | `Planned` | M5+ 安全边界已定�?| 不属�?1.0 当前可用能力，不应占用近期底座优先级 |
| Admin.NET 功能全量对标 | `Planned` | 功能矩阵已建�?| 当前真正业务落地主要�?Identity/Tenancy 基础，绝非完整后台框�?|

## 3. 当前发布表述

�?Seed、模块生命周期、宿主清单和真实后端 E2E 等近期硬化项完成前，对外应使用以下表述：

> Full.NET 当前是处�?M2 建设阶段的模块化 .NET 10 快速开发底座，已具�?Identity/Tenancy 安全基础、双数据库基础设施、Vue/Layui 管理壳层�?uni-app 多端构建基础；完整后�?CRUD、生�?Seed、Realtime、Flutter �?AI/Agent 能力仍在路线图中�?

禁止使用“Admin.NET 全功能已完成”“全端已验证”“生�?Seed 已就绪”“完�?RBAC 已交付”等超出本矩阵的描述�?

## 4. 近期优先队列

> 2026-07-21 起纳入[外部全面分析吸收](../verification/external-review-2026-07-21.md)：在基础设施债继续收敛的同时�?*必须尽早完成首个可重复业务纵向切�?*，否则治理成本无法被模块复杂度验证�?

1. **P0：规则合规与生产可控性（收尾�?*——E2E 受限查看者已移出 Identity 发布程序集，Architecture Test 与三宿主 Release 发布物扫描已关闭生产发布物边界；SQL Server/MySQL 新准备路径仍需在具备容器运行时的环境重跑。Seed 双库契约、SQL 安全门禁、Production Bootstrap Secret、禁用最后一名超管、TOTP Provider/双端 UI 与真实栈授予撤销已关闭；仍待 Production TOTP 强制路径真实栈�?
2. **P0：恢复客户端主干门禁**——修�?Layui 用户-机构隶属测试的机�?用户 Mock 顺序，连续两次聚焦通过后运行完�?`pnpm test:clients`；禁止用延长等待、降低调用次数或跳过用例掩盖失败（硬�?Task 3B）�?
3. **P0�?.0 前命名发布闭�?*——治理清单共 **87** 项，其中包含不可改写的历史迁移登记和动�?SQL 精确豁免，不等于 87 个现行数据库缺陷�?10/011 后运行时对象已规范化，剩余门禁是真实生产维护窗口、备份介质恢复和协议别名排空/退役�?
4. **P1：模块和运行角色边界**——Task 4A�?F 已完成：跨模块实现引�?生产友元已移除，API 迁移执行能力已隔离，Migrator 已收敛为最�?Migration/Seed Profile，真实健康探针与显式 Endpoint 安全意图均已落地，Tenancy 历史 Core/Http 拓扑也已合并回单主项目并通过发布物扫描。后续优先项集中�?TenantRequired/Global SQL、缓存一致性与更广真实发布补证�?
5. **P1：真实健康与 Endpoint 安全意图**——Task 4D�?E 已达 `Build-verified`：`ready/startup` 检查当前数据库、已配置 Redis 与初始化状态，空集合不得作为成功证据；所�?Endpoint 均已显式认证或匿名。后续仍需随真实发�?编排环境继续补证，才能推动整体能力迈�?`Verified`�?
6. **P1：可靠�?*——Outbox 最大重�?死信/版本共存与默认数据库租约拓扑已达 `Build-verified`；Task 7 已完成缓存一致性的最小闭环，当前 Tenancy 安全关键缓存具备“提交后本机同步失效 + Outbox 跨节点修复”的双库双节点证据。下一优先项转�?TenantRequired/Global SQL 语义门禁、Redis/Backplane 故障注入和高优先级日志通道（见[硬化计划](../superpowers/plans/2026-07-18-architecture-hardening.md) Task 7/8）�?
7. **P1：工程门�?*——PR 集成冒烟已加宽到 Identity/Tenancy/Outbox 核心双库 filter **8** 项，新鲜运行 **8/8**、墙�?**3m 42s**；`push main` 仍保持全�?Integration **134** 项。后续继续补 Architecture Tests 的模块表所有权�?`SqlDataScope` 显式性�?
8. **P1：交付真实性补�?*——真实栈 Redis、Overview �?403 UI 探针、Production TOTP 强制路径真实栈；浏览器跨 Tab 协调已有基础，故障注入仍缺�?
9. **P1：复用而不耦合**——OpenAPI/协议夹具扩展�?uni-app/Flutter；headless 契约层已起步，继续防止双端逻辑漂移�?
10. **已决策：Layui 长期并行**——所有�?2026-07-21 确认；Vue �?Layui 继续按同一模块同步开发与验收，不设退役窗口（�?[`client-frontend.md`](../../rules/client-frontend.md) §4）�?
11. **P2：运维与可维护�?*——JWT 轮换 / Redis 故障 / Seed 失败 Runbook �?Outbox 受控人工重放自动化；按行为不变方式拆�?`IdentityModule.AddServices`；Login Handler 只在相邻行为变更或基准支持时拆分�?
12. **M5+ 最后阶�?Decision Gate**——当前只�?Outbox。Kafka/CDC Relay 必须排在现有硬化和核心业务模块之后，并满足真�?SLA、轮询瓶颈、双�?CDC 运维和事件目录门禁；不得按瞬�?QPS 动态切换可靠性语义�?
13. **P2：后续业务能�?*——用户管理切片退出后，再排角色、菜单、Organization �?L5 业务翻译样例�?

## 5. 关联文档

- [人类阅读入口（Onboarding）](../development/onboarding.md)
- [总体架构设计](../superpowers/specs/2026-07-17-fullnet-architecture-design.md)
- [架构风险复核与硬化设计](../superpowers/specs/2026-07-18-architecture-hardening-design.md)
- [外部静态分析复核记录（2026-07-18）](../verification/external-review-2026-07-18.md)
- [外部全面分析复核与吸收记录（2026-07-21）](../verification/external-review-2026-07-21.md)
- [Identity 用户管理纵向切片计划](../superpowers/plans/2026-07-21-identity-user-management-vertical-slice.md)
- [Organization 用户-机构隶属纵向切片计划](../superpowers/plans/2026-07-21-organization-user-unit-assignment-vertical-slice.md)
- [Organization 机构管理纵向切片计划](../superpowers/plans/2026-07-21-organization-unit-management-vertical-slice.md)
- [Identity 菜单管理纵向切片计划](../superpowers/plans/2026-07-21-identity-menu-management-vertical-slice.md)
- [Identity 角色管理验证记录�?026-07-21）](../verification/identity-role-management-2026-07-21.md)
- [Identity Host 用户管理验证记录�?026-07-21）](../verification/identity-user-management-2026-07-21.md)
- [测试数量门槛核对记录](../verification/test-threshold-audit-2026-07-19.md)
- [架构硬化实施计划](../superpowers/plans/2026-07-18-architecture-hardening.md)
- [Full.NET 命名规范](../../rules/naming-conventions.md)
- [命名体系设计](../superpowers/specs/2026-07-18-fullnet-naming-conventions-design.md)
- [命名治理实施计划](../superpowers/plans/2026-07-18-naming-governance.md)
- [1.0 前存量命名规范化计划](../superpowers/plans/2026-07-18-pre-v1-naming-normalization.md)
- [1.0 前命名规范化验证记录](../verification/pre-v1-naming-normalization.md)
- [UUID v7 主键存储 ADR](../architecture/adr/ADR-0003-uuid-v7-primary-key-storage.md)与[专项实施计划](../superpowers/plans/2026-07-18-uuid-v7-primary-key-storage.md)
- [客户端交付路线图](client-delivery-roadmap.md)
- [Admin.NET.Pro 功能对标路线](adminnet-feature-parity.md)
- [种子数据模块设计](../superpowers/specs/2026-07-17-seed-data-module-design.md)
- [超级管理员设计](../superpowers/specs/2026-07-18-super-administrator-design.md)与[实施计划](../superpowers/plans/2026-07-18-super-administrator.md)
- [Dapper 辅助能力设计](../superpowers/specs/2026-07-18-dapper-tooling-design.md)与[实施计划](../superpowers/plans/2026-07-18-dapper-tooling.md)
