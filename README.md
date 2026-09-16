# Full.NET

Full.NET 是面向产品研发和项目快速交付的 .NET 10 基础框架。项目以模块化单体作为默认部署形态，吸收 eShop 的边界与可观测性思路，并�?Admin.NET 的业务能力范围作为长期功能对标目标；底层实现保持独立、可测试和可逐步拆分�?

项目最终以 MIT 许可证发布。所使用的第三方组件及其许可证见 [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES)�?

## 当前基础能力

项目仍处�?M2 建设阶段。以下是已经落地的基础范围，不代表完整后台框架、完�?RBAC �?Admin.NET 全功能已经交付；权威状态、证据和缺口见[当前能力状态矩阵](docs/roadmap/capability-status.md)�?

- 标准 HTTP 状态码�?`zh-CN/en-US` 本地�?ProblemDetails，机器字段和结构化校验违规保持稳定；Admin.NET 响应信封为显式可选适配器�?
- 显式模块注册、CQRS 分发、租户上下文和基于域名的租户解析�?
- 传输无关�?Command/Query 行为管道；FluentValidation 显式注册、统一 `validation.failed` 错误，并在事务开启前短路无效命令�?
- Dapper-first 数据访问、SQL 作用域保护和事务边界，不引入 EF Core；原�?QueryMultiple 已通过自有抽象�?SQL Server/MySQL 真实测试落地，SqlBuilder 仍等待首个真实动态列表命中准入门禁�?
- 跨工�?Naming Profile、SQL/C#／稳定协议命名门禁，以及供脚手架复用的确定�?CodeGeneration 命名内核；存量债务按文件和值精确登记，不会被新代码继承�?
- SQL Server/MySQL 双数据库 DbUp 迁移�?Testcontainers 集成测试�?
- MemoryPack 二进�?Outbox、精�?schema 版本路由、最大尝试、死信终态、租约式至少一次消费，以及默认关闭且使用独立消息作用域的有界并发�?
- 追加�?Outbox、SQL Server CDC/MySQL Binlog、Kafka 与消�?Inbox 已达 `Build-verified / Pilot`（Organization 真实 CDC E2E）；**默认不切�?*（`Messaging:DeliveryCutover:Enabled=false`）。权威边界见 [`ADR-0006`](docs/architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md)与[验证记录](docs/verification/cdc-kafka-pilot-2026-08-08.md)�?
- FusionCache 作为唯一缓存实现，同时暴�?`IFusionCache` �?`.AsHybridCache()` 适配�?`HybridCache`；安全关键租户缓存已实现“提交后删除当前实例 L1/L2 + Redis Backplane 广播 + TTL/版本兜底”的失效闭环，并暴露失效时延/失败、陈旧命中与 Backplane 熔断恢复的低基数指标�?
- System.Text.Json 源生�?HTTP 合约、Serilog 普�?高优先级独立有界异步日志、OpenTelemetry 和健康检查�?
- ASP.NET Core `Accept-Language` 请求协商、`zh-CN/en-US` 规范化、异�?CultureScope、模块错误资源和本地化响应头能力�?
- Identity 安全会话与授权上下文底座：强密码引导、RSA JWT、登录锁定、Refresh Token 轮换/重用撤销、CSRF、CORS、审计、最�?RBAC、可信租户切换和权限导航�?
- API、Worker、Migrator �?.NET Aspire AppHost 的完整本地编排�?

## 环境要求

- .NET 10 SDK
- Node.js 24 �?pnpm 10.26.0（建议通过 Corepack 管理�?
- Docker Desktop（Windows 使用 Linux containers/WSL 2）或兼容 Docker Engine
- Git

## 快速开�?

```powershell
dotnet restore Full.NET.slnx
dotnet build Full.NET.slnx --configuration Release
pnpm test:dotnet:unit -- --no-build
pnpm test:dotnet:compatibility -- --no-build
pnpm test:dotnet:architecture -- --no-build
# 日常按风险选择：冒烟、单提供程序 API、迁移或其他基础设施
# 任务开始先记录�?taskBase = git rev-parse HEAD
# 日常 inner / 切片关闭：pnpm test:inner -- --base $taskBase
# 完成时自动判�?none / tooling / 双库 focused / 专项分片
pnpm test:inner -- --base $taskBase --plan
pnpm test:integration:affected:plan -- --base $taskBase
pnpm test:integration:affected -- --base $taskBase
pnpm test:integration:smoke
pnpm test:integration:api:sqlserver
pnpm test:integration:api:mysql
pnpm test:integration:migrations
pnpm test:integration:infrastructure
# 快速校验四分片发现数、重复和遗漏
pnpm test:integration:partitions
pnpm test:integration:durations
# 可选：10 万行审计查询双库基准与执行计划（不属于日常测试门禁）
# dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query
# 可选：SQL Server 可选谓�?分支 SQL/RECOMPILE 混合顺序 A/B
# dotnet run --project benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj -c Release -- audit-query --mode sqlserver-plan-ab --providers sqlserver
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
```

测试套件、最低发现数、超时与 Integration 分片的唯一机器事实源是
[`eng/testing/test-matrix.json`](eng/testing/test-matrix.json)。`main` CI 运行其中�?
全部互斥分片；本地任务不得运行完整集合�?

Integration 依赖按需启动：SQL Server 聚焦测试不会额外启动 MySQL/Redis，反之亦然。工作区已脏或任务跨窗口时先�?`test:task:start` 创建快照，再�?`inner`、`slice`、`merge` 阶段使用 `pnpm test:inner` / `pnpm test:slice` / `test:integration:affected` 自动选择验证范围；多个过滤目标会�?UID 去重并合并为一次进程。本地只运行受影响测试，完整集合只保留给 `main` CI 的互斥并行分片。inner 不要�?`test:e2e:real` 或完�?`test:e2e:admin`�?

AppHost 默认启动 SQL Server、Redis、Migrator、API �?Worker。首次运行会要求输入宿主管理员账号和强密码，其中密码�?Secret Parameter 处理；Migrator 成功退出后，API �?Worker 才会启动，本�?`localhost` 租户和宿主管理员均被幂等创建。Bootstrap 现在幂等创建受保护超级管理员角色，不再同步逐项权限；签�?Claim、当前作用域动态权限、逐请�?Session/SecurityStamp 校验、双库并发最后一名保护、远程授�?撤销 API、事务内可追责审计和 Vue/Layui 对等管理页已经实现。远程写操作只允�?Development/Testing 显式开启，Production �?MFA/强认�?Provider 落地前无法开启；账号禁用/删除路径保护和真实后端浏览器 E2E 仍按[设计](docs/superpowers/specs/2026-07-18-super-administrator-design.md)与[计划](docs/superpowers/plans/2026-07-18-super-administrator.md)后续交付，因此当前不能标记为完整 `Verified`�?

模块化种子管道已经接�?Migrator：默认只迁移，显�?`--seed baseline|development|demo|test` 才播种，AppHost 使用 `--seed development`。管线已实现确定�?Profile 继承、SQL Server/MySQL 数据库锁与执行审计、Baseline 宿主管理�?Contributor �?Development 本地租户 Contributor；`--seed-local` 仅保留为带弃用告警的兼容别名。Production Bootstrap Secret 运维 Runbook 与缺 Secret 双库拒绝已落地（�?[docs/operations/seed-production-baseline.md](docs/operations/seed-production-baseline.md)）；完整 Aspire/CI Profile E2E �?MFA 到位前的远程超管写操作仍开放，因此 Production Seed 仍不能标记为 `Verified`。设计与后续步骤见[种子数据模块设计](docs/superpowers/specs/2026-07-17-seed-data-module-design.md)和[实施计划](docs/superpowers/plans/2026-07-17-seed-data-module.md)�?

新人阅读路径�?[Onboarding](docs/development/onboarding.md)；更完整的数据库切换、部署顺序、缓存和 API 约定�?[本地开发指南](docs/development/getting-started.md)�?*平台运行时拓�?*�?[platform-runtime-topology.md](docs/operations/platform-runtime-topology.md)�?*Messaging / CDC / Kafka** �?[messaging-runtime-topology.md](docs/operations/messaging-runtime-topology.md)。Outbox 多副本拓扑见 [Outbox Worker 运维说明](docs/operations/outbox-worker-topology.md)。Delivery 试点�?`Build-verified / Pilot`�?*默认不切�?*（`Messaging:DeliveryCutover:Enabled=false`）�?.0 �?Tenancy/Outbox 命名规范化自动化证据�?[验证记录](docs/verification/pre-v1-naming-normalization.md)。新增数据库对象、API、机器码或生成模板必须遵�?[Full.NET 命名规范](rules/naming-conventions.md)：官方表保留 `fn` OwnerKey，项目表使用脚手架阶段冻结的项目 OwnerKey，`sys` 不作为项目表前缀。当前能力以[状态矩阵](docs/roadmap/capability-status.md)为唯一总览；架构设计及 Admin.NET 功能对标路线位于 `docs/`�?

客户端基础验证�?

```powershell
corepack enable
pnpm install --frozen-lockfile
pnpm test:naming
pnpm test:openapi
# 分支开发时，将 main 替换为实�?PR 基线 ref
pnpm test:openapi:breaking -- --base-ref main
pnpm test:workspace
pnpm test:clients
pnpm test:performance-governance
pnpm build:clients
pnpm test:bundle-budgets
pnpm test:e2e
pnpm test:e2e:uniapp
```

## 客户端规�?

- `ui/admin`：Vue 3 + TypeScript + Vite + Element Plus **唯一持续交付**的后台管理端；已迁入 MIT Art Design Pro 管理壳层（见 `docs/verification/admin-art-design-pro.md`），ECharts 图表�?Tiptap Core 富文本基线按独立计划推进，保�?Full.NET 自有认证、租户、权限和 API 契约�?
- `ui/admin-layui`：自 2026-08-02 �?**Frozen**（停止新功能；仅授权安全修复/迁移/退役）；不再参与活�?CI/E2E/包体门禁�?
- `clients/uniapp`：一套代码覆�?H5、微信小程序和支付宝小程序，已引入官�?uni-ui、easycom �?Full.NET 主题令牌�?
- `clients/flutter`：原�?Android/iOS �?Windows/macOS/Linux 桌面客户端，采用 Flutter 3.44 Material 3 + Cupertino；当前尚未创建工程；
- .NET MAUI：仅在真�?C#/Windows 企业需求命中决策门禁后提供可选模板�?

Vue/Layui 的浏览器契约、原创管理壳、登录、启动恢复、刷新轮换、退出、当前用户、可信租户切换、Host 返回、动态权限导航、按钮可见性、标准错误展示与同场景双�?E2E 已经实现。两端当前共�?`zh-CN/en-US` 管理壳层契约；Element Plus/Day.js �?Layui 2.13.8 组件语言会随账号偏好同步，每�?HTTP 请求在发送前读取活动语言并携带规�?`Accept-Language`。服务端 ProblemDetails �?Admin.NET 兼容适配器按协商语言返回本地化错误标题，而稳�?`status/code/traceId/violations` 不随语言变化。账号语言偏好与租户默认语言已由 SQL Server/MySQL 双库持久化，`/api/v1/me` 是客户端偏好的唯一可信来源；认证切换通过 `PUT /api/v1/me/locale` 使用独立资料版本乐观并发，失败不会退出、改变租户或覆盖旧语言，偏好也不进�?JWT Claim。现有自动验收还覆盖�?axe 排除项的 WCAG 2.2 A/AA、键盘焦点�?20 CSS px 重排和减弱动画�?

`clients/uniapp` 已进�?`Implementing / Build-verified`：Vue I18n、规�?`zh-CN/en-US` 与平�?`zh-Hans/en` 映射、pages/manifest 静态资源、逐请�?`Accept-Language`、账号偏好成功后原子提交、ProblemDetails 回退、官�?uni-ui 1.5.12、easycom、Full.NET 主题令牌、真实语言设置页迁移�?03 项单元测试、标�?SFC 类型检查、H5/微信/支付�?CLI 构建�?6 �?Edge H5 冒烟已经通过。可用命令为 `pnpm --filter @fullnet/uniapp dev:h5`、`test`、`typecheck`、`build:h5`、`build:mp-weixin`、`build:mp-alipay` 与根 `pnpm test:e2e:uniapp`。微信和支付宝开发者工具当前未安装，因此没有开发者工具或真机验收，不能标记为 `Verified`；详见[多语言验证](docs/verification/uniapp-localization.md)与[uni-ui 验证](docs/verification/uniapp-uni-ui.md)。这也不代表 Flutter、通知或业务内容已经完成全栈多语言。动态导航只能映射到各客户端本地精确白名单；令牌和租户授权状态不写入 Web Storage。Windows Edge + NVDA 和强制颜色模式仍待人工验证，因此 C1 保持 `Implemented`；后台业�?CRUD 继续�?C2 路线交付。详细决策见[客户�?UI 框架设计](docs/superpowers/specs/2026-07-18-client-ui-framework-design.md)、[多客户端前端策略](docs/superpowers/specs/2026-07-17-multi-client-frontend-strategy-design.md)、[全栈多语言设计](docs/superpowers/specs/2026-07-17-full-stack-localization-design.md)和[客户端交付路线图](docs/roadmap/client-delivery-roadmap.md)�?

## 当前边界

2026-09-13 已确认标�?OIDC 认证中心�?SSO 演进，当前为 `Mapped`（P0 双库协议与原生门禁已挂接；T08 Vue 可�?`oidc-center` 路径 **35** 项真实栈 E2E 探针�?CI，`legacy` 默认并行；`Verified` 与跨应用 SSO 未关闭）。方案复用现�?Identity 账号、权限和权威会话。见 [Identity 设计](docs/superpowers/specs/2026-07-17-identity-session-foundation-design.md#14-oidc-认证中心�?sso-演进2026-09-13-已确�?、[ADR-0011](docs/architecture/adr/ADR-0011-identity-oidc-sso-evolution.md)、[执行计划](docs/superpowers/plans/2026-09-13-identity-oidc-sso-evolution.md)和[研究验证矩阵](docs/verification/2026-09-13-identity-oidc-sso-research-validation.md)�?

M1 聚焦可运行的基础设施与第一条租户垂直切片，M2 已落地跨传输验证管道、Identity 安全会话、Host 用户/角色/菜单与组织授权切片、在线会话、API Key，以�?Vue/Layui 双端权限导航。当前能力仍不等于完整后�?RBAC，租户级角色、完整数据范围和更多业务模块授权仍需继续交付。SignalR 鉴权 Hub、用�?租户分组、JSON Hub 协议、可�?Redis Backplane、专�?ready 探针、SQL Server/MySQL �?API 节点 stop/start 故障恢复，以�?Vue/Layui 管理端认证连接、首次失败退避恢复、切租户重连、未读徽标、当前通知页刷新、独�?Worker Outbox 修复推送和双库真实浏览器断网恢�?E2E 已达 `Build-verified`；生产多副本编排/告警�?Redis Cluster/Sentinel 仍未完成。真实服务拆分后才引�?gRPC + Protobuf；AI、MCP �?Agentic Web/AG-UI 位于独立�?M5+ 计划中�?
