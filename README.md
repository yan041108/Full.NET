# Full.NET

Full.NET 是面向产品研发和项目快速交付的 .NET 10 基础框架。项目以模块化单体作为默认部署形态，吸收 eShop 的边界与可观测性思路，并以 Admin.NET 的业务能力范围作为长期功能对标目标；底层实现保持独立、可测试和可逐步拆分。

项目最终以 MIT 许可证发布。所使用的第三方组件及其许可证见 [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES)。

## 当前基础能力

项目仍处于 M2 建设阶段。以下是已经落地的基础范围，不代表完整后台框架、完整 RBAC 或 Admin.NET 全功能已经交付；权威状态、证据和缺口见[当前能力状态矩阵](docs/roadmap/capability-status.md)。

- 标准 HTTP 状态码与 `zh-CN/en-US` 本地化 ProblemDetails，机器字段和结构化校验违规保持稳定；Admin.NET 响应信封为显式可选适配器。
- 显式模块注册、CQRS 分发、租户上下文和基于域名的租户解析。
- 传输无关的 Command/Query 行为管道；FluentValidation 显式注册、统一 `validation.failed` 错误，并在事务开启前短路无效命令。
- Dapper-first 数据访问、SQL 作用域保护和事务边界，不引入 EF Core；原生 QueryMultiple 已通过自有抽象和 SQL Server/MySQL 真实测试落地，SqlBuilder 仍等待首个真实动态列表命中准入门禁。
- 跨工具 Naming Profile、SQL/C#／稳定协议命名门禁，以及供脚手架复用的确定性 CodeGeneration 命名内核；存量债务按文件和值精确登记，不会被新代码继承。
- SQL Server/MySQL 双数据库 DbUp 迁移及 Testcontainers 集成测试。
- MessagePack 二进制 Outbox、租约式至少一次消费、schema 版本路由和指数退避。
- FusionCache 作为唯一缓存实现，同时暴露 `IFusionCache` 与 `.AsHybridCache()` 适配的 `HybridCache`。
- System.Text.Json 源生成 HTTP 合约、Serilog 有界异步日志、OpenTelemetry 和健康检查。
- ASP.NET Core `Accept-Language` 请求协商、`zh-CN/en-US` 规范化、异步 CultureScope、模块错误资源和本地化响应头能力。
- Identity 安全会话与授权上下文底座：强密码引导、RSA JWT、登录锁定、Refresh Token 轮换/重用撤销、CSRF、CORS、审计、最小 RBAC、可信租户切换和权限导航。
- API、Worker、Migrator 与 .NET Aspire AppHost 的完整本地编排。

## 环境要求

- .NET 10 SDK
- Node.js 24 与 pnpm 10.26.0（建议通过 Corepack 管理）
- Docker Desktop（Windows 使用 Linux containers/WSL 2）或兼容 Docker Engine
- Git

## 快速开始

```powershell
dotnet restore Full.NET.slnx
dotnet build Full.NET.slnx --configuration Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --minimum-expected-tests 203
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --minimum-expected-tests 5
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --minimum-expected-tests 15
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --minimum-expected-tests 18 --timeout 15m
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
```

AppHost 默认启动 SQL Server、Redis、Migrator、API 和 Worker。首次运行会要求输入宿主管理员账号和强密码，其中密码按 Secret Parameter 处理；Migrator 成功退出后，API 与 Worker 才会启动，本地 `localhost` 租户和宿主管理员均被幂等创建。Bootstrap 现在幂等创建受保护超级管理员角色，不再同步逐项权限；签名 Claim、当前作用域动态权限、逐请求 Session/SecurityStamp 校验、双库并发最后一名保护、远程授予/撤销 API、事务内可追责审计和 Vue/Layui 对等管理页已经实现。远程写操作只允许 Development/Testing 显式开启，Production 在 MFA/强认证 Provider 落地前无法开启；账号禁用/删除路径保护和真实后端浏览器 E2E 仍按[设计](docs/superpowers/specs/2026-07-18-super-administrator-design.md)与[计划](docs/superpowers/plans/2026-07-18-super-administrator.md)后续交付，因此当前不能标记为完整 `Verified`。

当前本地数据仍由 Migrator 的 `--seed-local` 硬编码入口创建。模块化种子管道已经完成设计但尚未实现：Production 可显式运行安全 `baseline`，Development/Demo/Test 在 Baseline 上叠加各自数据，Testcontainers 中的场景数据继续由隔离 Test Factory 创建。设计与后续步骤见[种子数据模块设计](docs/superpowers/specs/2026-07-17-seed-data-module-design.md)和[实施计划](docs/superpowers/plans/2026-07-17-seed-data-module.md)。

更完整的数据库切换、部署顺序、缓存和 API 约定见 [本地开发指南](docs/development/getting-started.md)。新增数据库对象、API、机器码或生成模板必须遵守 [Full.NET 命名规范](rules/naming-conventions.md)：官方表保留 `fn` OwnerKey，项目表使用脚手架阶段冻结的项目 OwnerKey，`sys` 不作为项目表前缀。当前能力以[状态矩阵](docs/roadmap/capability-status.md)为唯一总览；架构设计及 Admin.NET 功能对标路线位于 `docs/`。

客户端基础验证：

```powershell
corepack enable
pnpm install --frozen-lockfile
pnpm test:workspace
pnpm test:clients
pnpm build:clients
pnpm test:e2e
pnpm test:e2e:uniapp
```

## 客户端规划

- `ui/admin`：Vue 3 + TypeScript + Vite + Element Plus 主管理端；后续按独立迁移计划采用 MIT Art Design Pro 管理壳层、ECharts 图表与 Tiptap Core 富文本基线，保留 Full.NET 自有认证、租户、权限和 API 契约；
- `ui/admin-layui`：基于 MIT Layui 2 独立实现的 HTML/CSS/原生 JavaScript 管理端，与 Vue 覆盖相同后台功能并同步验收；layuiAdmin 仅作交互参考，不复制其非 MIT 主题资产；
- `clients/uniapp`：一套代码覆盖 H5、微信小程序和支付宝小程序，默认采用官方 uni-ui；当前尚未引入组件包；
- `clients/flutter`：原生 Android/iOS 与 Windows/macOS/Linux 桌面客户端，采用 Flutter 3.44 Material 3 + Cupertino；当前尚未创建工程；
- .NET MAUI：仅在真实 C#/Windows 企业需求命中决策门禁后提供可选模板。

Vue/Layui 的浏览器契约、原创管理壳、登录、启动恢复、刷新轮换、退出、当前用户、可信租户切换、Host 返回、动态权限导航、按钮可见性、标准错误展示与同场景双端 E2E 已经实现。两端当前共享 `zh-CN/en-US` 管理壳层契约；Element Plus/Day.js 与 Layui 2.13.8 组件语言会随账号偏好同步，每个 HTTP 请求在发送前读取活动语言并携带规范 `Accept-Language`。服务端 ProblemDetails 与 Admin.NET 兼容适配器按协商语言返回本地化错误标题，而稳定 `status/code/traceId/violations` 不随语言变化。账号语言偏好与租户默认语言已由 SQL Server/MySQL 双库持久化，`/api/v1/me` 是客户端偏好的唯一可信来源；认证切换通过 `PUT /api/v1/me/locale` 使用独立资料版本乐观并发，失败不会退出、改变租户或覆盖旧语言，偏好也不进入 JWT Claim。现有自动验收还覆盖无 axe 排除项的 WCAG 2.2 A/AA、键盘焦点、320 CSS px 重排和减弱动画。

`clients/uniapp` 已进入 `Implementing / Build-verified`：Vue I18n、规范 `zh-CN/en-US` 与平台 `zh-Hans/en` 映射、pages/manifest 静态资源、逐请求 `Accept-Language`、账号偏好成功后原子提交、ProblemDetails 回退、96 项单元测试、标准 SFC 类型检查、H5/微信/支付宝 CLI 构建和 5 项 Edge H5 冒烟已经通过。可用命令为 `pnpm --filter @fullnet/uniapp dev:h5`、`test`、`typecheck`、`build:h5`、`build:mp-weixin`、`build:mp-alipay` 与根 `pnpm test:e2e:uniapp`。官方 uni-ui 已确定为默认 UI 组件库但尚未引入，因此现有成果不能描述为“uni-ui 基础完成”。微信和支付宝开发者工具当前未安装，因此没有开发者工具或真机验收，不能标记为 `Verified`；详见[验证记录](docs/verification/uniapp-localization.md)。这也不代表 Flutter、通知或业务内容已经完成全栈多语言。动态导航只能映射到各客户端本地精确白名单；令牌和租户授权状态不写入 Web Storage。Windows Edge + NVDA 和强制颜色模式仍待人工验证，因此 C1 保持 `Implemented`；后台业务 CRUD 继续按 C2 路线交付。详细决策见[客户端 UI 框架设计](docs/superpowers/specs/2026-07-18-client-ui-framework-design.md)、[多客户端前端策略](docs/superpowers/specs/2026-07-17-multi-client-frontend-strategy-design.md)、[全栈多语言设计](docs/superpowers/specs/2026-07-17-full-stack-localization-design.md)和[客户端交付路线图](docs/roadmap/client-delivery-roadmap.md)。

## 当前边界

M1 聚焦可运行的基础设施与第一条租户垂直切片，M2 已落地跨传输验证管道、Identity 安全会话、最小 RBAC 授权上下文和双端权限导航。当前能力仍不等于完整后台 RBAC：用户与租户账号 CRUD、角色授权、菜单管理、组织数据范围和强制下线仍属于后续切片。SignalR/Realtime 在 M2 后续迭代中引入；真实服务拆分后才引入 gRPC + Protobuf；AI、MCP 与 Agentic Web/AG-UI 位于独立的 M5+ 计划中。
