# Full.NET 当前能力状态矩阵

- 快照日期：2026-07-21
- 基线提交：本文件所在提交
- 文档职责：作为“当前能用到什么程度”的唯一总览；详细范围仍由各规格、路线图和验证记录负责
- 更新规则：每次里程碑、公开发布和能力状态变化时更新；没有可定位证据不得提升状态

## 1. 状态定义

| 状态 | 含义 |
|---|---|
| `Planned` | 已进入路线图，但尚未形成可实施规格 |
| `Designing` | 规格或实施计划正在形成，尚不能作为可用能力 |
| `Implemented` | 实现已经存在，但尚未完成本能力要求的全部构建、集成或人工验收 |
| `Build-verified` | 当前目标的编译、静态检查或自动化测试已有记录，但仍缺真实环境、双库、跨端或人工验收中的至少一项 |
| `Verified` | 规格定义的自动化、真实依赖、双库、跨端和必要人工验收全部具备可定位证据 |
| `Decision Gate` | 只有命中明确业务条件后才进入设计，不属于默认承诺 |

`Implemented` 不等于生产就绪；“测试文件存在”也不等于 `Build-verified`。本快照引用的是基线提交及仓库现有记录，不替代发布前的新鲜验证。

## 2. 当前可用范围

| 能力 | 状态 | 当前证据 | 主要缺口/下一门禁 |
|---|---|---|---|
| 模块化单体、显式模块依赖与宿主 Profile | `Build-verified` | `Full.NET.Modularity`、`Full.NET.Composition`、Api/Worker/Migrator 显式 Profile、Unit 与 Architecture Tests | 新模块必须进入共享目录；Worker 只允许最小后台入口，禁止宿主恢复手工模块清单 |
| 跨栈命名治理与生成器命名内核 | `Build-verified` | `contracts/naming/`、`pnpm test:naming`（23 项）、010/011 双库迁移、19 项 Naming Integration 矩阵、[命名治理](../verification/naming-governance.md)与[1.0 前规范化验证](../verification/pre-v1-naming-normalization.md)；债务 **83** 项 | 真实维护窗口、备份升级演练、协议别名排空与客户端 E2E 升级路径未实跑；动态 SQL 仍须人工审查；完整业务模板与重复生成快照未交付，因此不能标记为 `Verified` |
| Dapper-first、事务与租户 SQL 作用域 | `Build-verified` | Data BuildingBlocks；QueryMultiple 顺序/完整消费及 SQL Server/MySQL 真实测试 | `TenantRequired` 仍需从参数文本检查升级为受控语义元数据，Global Statement 需精确目录；SqlBuilder 只在真实消费者命中门禁后引入 |
| UUID v7 主键与跨库物理存储 | `Build-verified` | `UuidStorageContractV1`、008/009 双库迁移、`PrimaryKeyTypeMapping`、`validate-uuid-storage-sql`（010+ 门禁）、UUID 集成测试（Expand/Contract/Recovery 31 项）、应用持久化/外部契约测试、Runbook 与[自动化恢复演练记录](../verification/uuid-v7-primary-key-storage-2026-07-19.md)、真实栈 MySQL E2E 走 Binary16；当前声明门槛 **314/7/26/89** 见[测试门槛核对](../verification/test-threshold-audit-2026-07-19.md) | 真实生产维护窗口与整库备份恢复 RPO/RTO 实跑、SQL Server 聚集索引性能基准尚未完成 |
| SQL Server / MySQL DbUp 迁移 | `Build-verified` | 双库迁移测试（Integration **103** 项）、010/011 Naming Expand/Contract、迁移文件配对与 CI SQL 命名 Lint；破坏性 DDL/无 WHERE 写操作由 [`pnpm test:sql-safety`](../verification/sql-safety-governance-2026-07-21.md) 强制 | 通用半完成迁移扫描仍依赖既有双库恢复用例；动态 SQL 精确债务须持续人工审查 |
| MessagePack Outbox、租约、重试 | `Implemented` | Outbox 表、Worker、`MessageType + SchemaVersion` 路由 | 缺跨版本升级链、版本退役策略、最大重试/死信闭环 |
| FusionCache + `.AsHybridCache()` | `Implemented` | 单一实现、L2/Backplane、全局关闭 Fail-Safe | 安全关键数据的同步本机失效、陈旧窗口和故障注入验证待补 |
| 标准 HTTP + ProblemDetails | `Build-verified` | API、兼容测试、Admin.NET 适配层 | OpenAPI 破坏性变更门禁和多客户端生成待补 |
| System.Text.Json 源生成基础 | `Implemented` | 模块 JSON Context 与 HTTP 契约 | 后续 DTO 必须持续纳入源生成和兼容测试 |
| 高并发结构化日志与 OpenTelemetry | `Implemented` | 有界异步 Serilog、队列监控、OTel | Warning/Error 独立高优先级通道和降级演练未实现 |
| Identity 会话安全基础 | `Build-verified` | 登录、事务轮换、重用/family 撤销、逐请求 Session/账号/安全戳校验、CSRF/CORS/Origin、Refresh/Logout 限流与审计测试；`SessionRaceAssertions` 双库集成（60 项门槛） | 事务故障注入和 Redis 分布式会话未实现 |
| Tenancy 可信上下文切换 | `Build-verified` | 租户解析、可用租户、切换与刷新集成测试 | 完整租户/套餐 CRUD 尚未开始；不能宣传为完整租户后台 |
| 最小 RBAC 与权限导航 | `Build-verified` | **Host 用户**（含**用户-角色分配**）、**Host 角色**（含**数据范围**）、**运行时多角色数据范围并集**（租户机构与用户-机构隶属只读过滤）、**Host 菜单**、**租户机构**与**用户-机构隶属** API + 双端 UI + 双库集成 + Mock/真实栈冒烟 + OpenAPI 夹具（见[用户验证](../verification/identity-user-management-2026-07-21.md)、[用户-角色分配验证](../verification/identity-user-roles-assignment-2026-07-21.md)、[运行时数据范围验证](../verification/identity-runtime-data-scope-2026-07-21.md)、[角色验证](../verification/identity-role-management-2026-07-21.md)、[角色数据范围验证](../verification/identity-role-data-scope-2026-07-21.md)、[菜单验证](../verification/identity-menu-management-2026-07-21.md)、[机构验证](../verification/organization-unit-management-2026-07-21.md)、[用户-机构隶属验证](../verification/organization-user-unit-assignment-2026-07-21.md)） | 其他业务模块全面接入机构过滤 |
| 受保护超级管理员 | `Implemented` | 005/006 双库迁移、动态 Catalog 权限、逐请求会话校验、远程授予/撤销 API、当前密码重认证、**TOTP 强认证 Provider + ADR-0004 Production 三条件解锁**（见[TOTP 验证](../verification/identity-totp-strong-reauth-2026-07-21.md)）、含 ActorUserId 的事务审计、双库最后一名保护、**禁用最后一名超管拒绝**、Vue/Layui 对等管理页与 Mock E2E、**双端 TOTP 登记/确认 UI**（见[TOTP UI 验证](../verification/identity-totp-admin-ui-2026-07-21.md)）、**超管管理页真实栈授予/撤销 E2E**（见[真实栈验证](../verification/identity-super-admin-real-stack-2026-07-21.md)）；真实栈 `permission-denied` 覆盖受限账号 API/UI 403 | Production TOTP 强制路径真实栈；账号硬删除 API 不在 1.0 范围 |
| Vue 管理壳层 | `Implemented` | 自研 Element Plus 壳层、会话、租户、导航、i18n、可访问性自动化 | Art Design Pro 已选定但尚未迁入；迁移期间必须保留现有安全契约和 E2E |
| Vue 图表与双管理端富文本 | `Designing` | ECharts 6.1 与 Tiptap Core 3.28 已完成选型和边界设计 | 依赖、主题、懒加载、服务端 HTML 净化、Files 上传及 Vue/Layui Adapter 尚未实现 |
| Layui 管理壳层 | `Implemented` | Vite/Vitest、独立 JS/HTML 壳层、同场景 E2E；**长期并行**（所有者 2026-07-21 确认，非过渡兼容端） | 会话/HTTP/导航白名单已收敛到 `@fullnet/client-contracts` headless 层；Layui 仅保留 DOM 渲染适配；业务切片须与 Vue 同步 |
| 双管理端真实后端浏览器联调 | `Build-verified` | `tests/e2e/admin-real-stack`：Testcontainer SQL Server/MySQL + Migrator Development Seed + 真实 API；Vue/Layui **38** 项（登录/刷新/跨 Tab/租户/**Host 用户/角色/菜单/机构与用户-机构隶属列表与权限裁剪**/超级管理员授予撤销/ProblemDetails/403/权限拒绝/退出）；租户上下文导航 Host 目录 SQL 作用域修复见[验证](../verification/identity-tenant-navigation-host-sql-scope-2026-07-21.md)；CI `real-stack-e2e` + `real-stack-e2e-mysql`（main） | Redis 未纳入真实栈；Overview「检查会话」级 API 403 UI 探针仍依赖 mock parity；租户内可分配 Host 用户 API 仍开放；不能标为 `Verified` |
| uni-app H5/微信/支付宝基础 | `Build-verified` | 96 项单测、类型检查、三目标 CLI 构建、H5 E2E | uni-ui 已选定但尚未引入；微信/支付宝开发者工具、真机及真实后端会话未验证 |
| Flutter 移动/桌面客户端 | `Designing` | Flutter 3.44、Material 3 + Cupertino、平台与多语言边界已确定 | 工程、设计令牌映射、构建节点、登录/API 冒烟均未实现 |
| 全栈多语言 L0-L3 | `Build-verified` | 服务端、双管理端、uni-app 自动化记录 | L4 Flutter 与 L5 业务内容/异步消息仍为设计状态 |
| 模块化 Seed Baseline/Overlay | `Build-verified` | Migrator 两阶段 Seed、双库 Development/Test/Production 契约、Production 缺 Secret 拒绝（见[种子双库验证](../verification/seed-dual-database-contract-2026-07-21.md)、[Production Secret 验证](../verification/seed-production-secret-and-super-admin-disable-2026-07-21.md)、[运维 Runbook](../operations/seed-production-baseline.md)；门槛 **333/7/26/107**） | 完整 Aspire/CI Profile E2E 仍开放；Production 远程超管写已可按 ADR-0004 开启，不能标记为 `Verified` |
| SignalR / Realtime | `Planned` | 架构边界已定义 | 抽象、鉴权分组、MessagePack Hub、Redis Backplane 尚未实现 |
| gRPC 服务通信 | `Planned` | 架构边界已定义 | 首次真实服务拆分前不引入 |
| AI / Agent / MCP / Agentic Web | `Planned` | M5+ 安全边界已定义 | 不属于 1.0 当前可用能力，不应占用近期底座优先级 |
| Admin.NET 功能全量对标 | `Planned` | 功能矩阵已建立 | 当前真正业务落地主要是 Identity/Tenancy 基础，绝非完整后台框架 |

## 3. 当前发布表述

在 Seed、模块生命周期、宿主清单和真实后端 E2E 等近期硬化项完成前，对外应使用以下表述：

> Full.NET 当前是处于 M2 建设阶段的模块化 .NET 10 快速开发底座，已具备 Identity/Tenancy 安全基础、双数据库基础设施、Vue/Layui 管理壳层和 uni-app 多端构建基础；完整后台 CRUD、生产 Seed、Realtime、Flutter 与 AI/Agent 能力仍在路线图中。

禁止使用“Admin.NET 全功能已完成”“全端已验证”“生产 Seed 已就绪”“完整 RBAC 已交付”等超出本矩阵的描述。

## 4. 近期优先队列

> 2026-07-21 起纳入[外部全面分析吸收](../verification/external-review-2026-07-21.md)：在基础设施债继续收敛的同时，**必须尽早完成首个可重复业务纵向切片**，否则治理成本无法被模块复杂度验证。

1. **P0：生产可控性（收尾）**——Seed 双库契约、SQL 安全门禁、Production Bootstrap Secret Runbook 与缺 Secret 双库拒绝、禁用最后一名超管保护已关闭（见[Production Secret 验证](../verification/seed-production-secret-and-super-admin-disable-2026-07-21.md)）；TOTP 强认证 Provider 与 ADR-0004 已关闭后端门禁（见[验证记录](../verification/identity-totp-strong-reauth-2026-07-21.md)）；双端 TOTP 登记/确认 UI 已同步（见[TOTP UI 验证](../verification/identity-totp-admin-ui-2026-07-21.md)）；超管管理页真实栈授予/撤销 E2E 已关闭（见[真实栈验证](../verification/identity-super-admin-real-stack-2026-07-21.md)）；仍待 Production TOTP 强制路径真实栈。运行时数据范围并集切片已关闭（见[运行时数据范围验证](../verification/identity-runtime-data-scope-2026-07-21.md)）。
2. **P0：1.0 前命名债务收敛**——剩余 **83** 项（协议别名窗口、动态 SQL 等）与真实维护窗口/备份升级演练待闭环。
3. **P1：可靠性**——Outbox 最大重试/死信/版本共存、**多 Worker 租约压力与部署拓扑文档**、TenantRequired/Global SQL 语义门禁、缓存一致性分级和高优先级日志通道（见[硬化计划](../superpowers/plans/2026-07-18-architecture-hardening.md) Task 6 扩展）。
4. **P1：工程门禁**——PR 集成冒烟从“仅迁移 2 项”加宽到 Identity/Tenancy/Outbox 核心 filter（目标 ≤15m）；门槛审计与 CI **333/7/26/107** 保持同步；Architecture Tests 随模块增长补表所有权与 SqlDataScope 显式性。
5. **P1：交付真实性补强**——真实栈 Redis、Overview 级 403 UI 探针、Production TOTP 强制路径真实栈；浏览器跨 Tab 协调已有基础，故障注入仍缺。
6. **P1：复用而不耦合**——OpenAPI/协议夹具扩展到 uni-app/Flutter；headless 契约层已起步，继续防止双端逻辑漂移。
7. **已决策：Layui 长期并行**——所有者 2026-07-21 确认；Vue 与 Layui 继续按同一模块同步开发与验收，不设退役窗口（见 [`client-frontend.md`](../../rules/client-frontend.md) §4）。
8. **P2：运维与体验**——JWT 轮换 / Outbox 死信 / Redis 故障 / Seed 失败 Runbook；Aspire HealthCheck 钩子；人类[onboarding](../development/onboarding.md) 已建，继续补 ARCHITECTURE 总览图；Login Handler 拆分与性能基准。
9. **P2：后续业务能力**——用户管理切片退出后，再排角色、菜单、Organization 与 L5 业务翻译样例。

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
- [Identity 角色管理验证记录（2026-07-21）](../verification/identity-role-management-2026-07-21.md)
- [Identity Host 用户管理验证记录（2026-07-21）](../verification/identity-user-management-2026-07-21.md)
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
