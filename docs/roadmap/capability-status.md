# Full.NET 当前能力状态矩阵

- 快照日期：2026-07-18
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
| 跨栈命名治理与生成器命名内核 | `Implemented` | `contracts/naming/`、`pnpm test:naming`、16 项 Architecture Tests、`Full.NET.Data.CodeGeneration` 与[验证记录](../verification/naming-governance.md) | 90 项存量债务仍待 1.0 前规范化；动态 SQL 继续要求人工审查，完整业务模板与重复生成快照尚未交付，因此不能标记为 `Verified` |
| Dapper-first、事务与租户 SQL 作用域 | `Build-verified` | Data BuildingBlocks；QueryMultiple 顺序/完整消费及 SQL Server/MySQL 真实测试 | `TenantRequired` 仍需从参数文本检查升级为受控语义元数据，Global Statement 需精确目录；SqlBuilder 只在真实消费者命中门禁后引入 |
| UUID v7 主键与跨库物理存储 | `Designing` | 应用侧 `IIdGenerator`/`Guid.CreateVersion7()`、SQL Server `uniqueidentifier` 与 ADR-0003 已存在；`UuidStorageContractV1` 已冻结 001-007 的 23 个 UUID 列、RFC 字节序向量、008/009 顺序和维护窗口 Runbook | MySQL 001-007 仍为 `char(36)`；需继续实现统一 `GuidFormat=Binary16` 数据边界、008/009 存量迁移、双库往返/恢复验证和 SQL Server 聚集索引治理 |
| SQL Server / MySQL DbUp 迁移 | `Build-verified` | 双库迁移测试、迁移文件配对与 CI SQL 命名 Lint | 破坏性 DDL 审批和通用半完成迁移扫描尚未闭环；动态 SQL 仍以精确债务触发人工审查 |
| MessagePack Outbox、租约、重试 | `Implemented` | Outbox 表、Worker、`MessageType + SchemaVersion` 路由 | 缺跨版本升级链、版本退役策略、最大重试/死信闭环 |
| FusionCache + `.AsHybridCache()` | `Implemented` | 单一实现、L2/Backplane、全局关闭 Fail-Safe | 安全关键数据的同步本机失效、陈旧窗口和故障注入验证待补 |
| 标准 HTTP + ProblemDetails | `Build-verified` | API、兼容测试、Admin.NET 适配层 | OpenAPI 破坏性变更门禁和多客户端生成待补 |
| System.Text.Json 源生成基础 | `Implemented` | 模块 JSON Context 与 HTTP 契约 | 后续 DTO 必须持续纳入源生成和兼容测试 |
| 高并发结构化日志与 OpenTelemetry | `Implemented` | 有界异步 Serilog、队列监控、OTel | Warning/Error 独立高优先级通道和降级演练未实现 |
| Identity 会话安全基础 | `Build-verified` | 登录、事务轮换、重用/family 撤销、逐请求 Session/账号/安全戳校验、CSRF/CORS/Origin、Refresh/Logout 限流与审计测试 | 多浏览器 Tab 刷新竞争、事务故障注入和上下文切换线性化专项验证待补 |
| Tenancy 可信上下文切换 | `Build-verified` | 租户解析、可用租户、切换与刷新集成测试 | 完整租户/套餐 CRUD 尚未开始；不能宣传为完整租户后台 |
| 最小 RBAC 与权限导航 | `Build-verified` | 当前用户、权限、Vue/Layui 导航与按钮门禁 | 用户、角色、菜单、组织、数据范围管理 CRUD 尚未实现 |
| 受保护超级管理员 | `Implemented` | 005/006 双库迁移、动态 Catalog 权限、逐请求会话校验、远程授予/撤销 API、当前密码重认证、含 ActorUserId 的事务审计、双库最后一名保护、Vue/Layui 对等管理页与 Mock E2E | Production MFA/强认证 Provider、账号禁用/删除及普通角色 CRUD 的系统角色保护、真实后端浏览器 E2E 尚未完成 |
| Vue 管理壳层 | `Implemented` | 自研 Element Plus 壳层、会话、租户、导航、i18n、可访问性自动化 | Art Design Pro 已选定但尚未迁入；迁移期间必须保留现有安全契约和 E2E |
| Vue 图表与双管理端富文本 | `Designing` | ECharts 6.1 与 Tiptap Core 3.28 已完成选型和边界设计 | 依赖、主题、懒加载、服务端 HTML 净化、Files 上传及 Vue/Layui Adapter 尚未实现 |
| Layui 管理壳层 | `Implemented` | Vite/Vitest、独立 JS/HTML 壳层、同场景 E2E | 与 Vue 重复的会话/HTTP/导航规则需收敛到 headless 契约层 |
| 双管理端真实后端浏览器联调 | `Designing` | 真实 Host 已有 API/CORS 集成测试 | Playwright 目前主要 Mock API；需补 Cookie/CORS/刷新/租户切换真实链路 |
| uni-app H5/微信/支付宝基础 | `Build-verified` | 96 项单测、类型检查、三目标 CLI 构建、H5 E2E | uni-ui 已选定但尚未引入；微信/支付宝开发者工具、真机及真实后端会话未验证 |
| Flutter 移动/桌面客户端 | `Designing` | Flutter 3.44、Material 3 + Cupertino、平台与多语言边界已确定 | 工程、设计令牌映射、构建节点、登录/API 冒烟均未实现 |
| 全栈多语言 L0-L3 | `Build-verified` | 服务端、双管理端、uni-app 自动化记录 | L4 Flutter 与 L5 业务内容/异步消息仍为设计状态 |
| 模块化 Seed Baseline/Overlay | `Implemented` | Migrator 已按迁移后可选 Seed 两阶段编排，支持显式 Profile 与稳定失败码；已落地 SQL Server/MySQL 锁和审计、Baseline Identity Contributor、Development Tenancy Contributor及宿主依赖门禁；Release Build、Unit 277、Architecture 20、Integration 22 项通过 | 完整 Profile 的 SQL Server/MySQL E2E、Production Secret 注入与运维验收尚未实施，不能视为生产 Seed 可用或标记为 `Verified` |
| SignalR / Realtime | `Planned` | 架构边界已定义 | 抽象、鉴权分组、MessagePack Hub、Redis Backplane 尚未实现 |
| gRPC 服务通信 | `Planned` | 架构边界已定义 | 首次真实服务拆分前不引入 |
| AI / Agent / MCP / Agentic Web | `Planned` | M5+ 安全边界已定义 | 不属于 1.0 当前可用能力，不应占用近期底座优先级 |
| Admin.NET 功能全量对标 | `Planned` | 功能矩阵已建立 | 当前真正业务落地主要是 Identity/Tenancy 基础，绝非完整后台框架 |

## 3. 当前发布表述

在 Seed、模块生命周期、宿主清单和真实后端 E2E 等近期硬化项完成前，对外应使用以下表述：

> Full.NET 当前是处于 M2 建设阶段的模块化 .NET 10 快速开发底座，已具备 Identity/Tenancy 安全基础、双数据库基础设施、Vue/Layui 管理壳层和 uni-app 多端构建基础；完整后台 CRUD、生产 Seed、Realtime、Flutter 与 AI/Agent 能力仍在路线图中。

禁止使用“Admin.NET 全功能已完成”“全端已验证”“生产 Seed 已就绪”“完整 RBAC 已交付”等超出本矩阵的描述。

## 4. 近期优先队列

1. **P0：主键物理存储与数据安全**——先实施 ADR-0003 的 MySQL `BINARY(16)` 数据边界和 008/009 存量迁移，完成备份、字节序、主外键、Seed 审计引用、部分迁移恢复和 SQL Server 聚集索引验证。
2. **P0：生产可控性**——在主键存储迁移完成后实施 Seed Baseline/Overlay，为超级管理员远程写操作接入 MFA/强认证 Provider 并补账号禁用/删除保护；建立 SQL 破坏性变更门禁，并复用现有命名扫描入口。
3. **P0：1.0 前命名规范化**——按 010/011 Expand/Contract 计划迁移 90 项精确债务，不修改已执行迁移，不把存量旧名称复制到新模板。
4. **P1：可靠性**——Outbox 版本兼容/死信、TenantRequired/Global SQL 语义门禁、缓存一致性分级和高优先级日志通道。
5. **P1：交付真实性**——真实后端参与的 Vue/Layui Playwright 安全冒烟；浏览器跨 Tab 刷新协调。
6. **P1：复用而不耦合**——浏览器 headless 契约层；OpenAPI/协议夹具扩展到 uni-app/Flutter。
7. **P2：后续业务能力**——首批 Identity/Tenancy/Organization 双管理端纵向切片、L5 业务内容翻译样例。

## 5. 关联文档

- [总体架构设计](../superpowers/specs/2026-07-17-fullnet-architecture-design.md)
- [架构风险复核与硬化设计](../superpowers/specs/2026-07-18-architecture-hardening-design.md)
- [外部静态分析复核记录](../verification/external-review-2026-07-18.md)
- [架构硬化实施计划](../superpowers/plans/2026-07-18-architecture-hardening.md)
- [Full.NET 命名规范](../../rules/naming-conventions.md)
- [命名体系设计](../superpowers/specs/2026-07-18-fullnet-naming-conventions-design.md)
- [命名治理实施计划](../superpowers/plans/2026-07-18-naming-governance.md)
- [1.0 前存量命名规范化计划](../superpowers/plans/2026-07-18-pre-v1-naming-normalization.md)
- [UUID v7 主键存储 ADR](../architecture/adr/ADR-0003-uuid-v7-primary-key-storage.md)与[专项实施计划](../superpowers/plans/2026-07-18-uuid-v7-primary-key-storage.md)
- [客户端交付路线图](client-delivery-roadmap.md)
- [Admin.NET.Pro 功能对标路线](adminnet-feature-parity.md)
- [种子数据模块设计](../superpowers/specs/2026-07-17-seed-data-module-design.md)
- [超级管理员设计](../superpowers/specs/2026-07-18-super-administrator-design.md)与[实施计划](../superpowers/plans/2026-07-18-super-administrator.md)
- [Dapper 辅助能力设计](../superpowers/specs/2026-07-18-dapper-tooling-design.md)与[实施计划](../superpowers/plans/2026-07-18-dapper-tooling.md)
