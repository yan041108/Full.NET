# Full.NET

Full.NET 是面向产品研发和项目快速交付的 .NET 10 基础框架。项目以模块化单体作为默认部署形态，吸收 eShop 的边界与可观测性思路，并以 Admin.NET 的业务能力范围作为长期功能对标目标；底层实现保持独立、可测试和可逐步拆分。

项目最终以 MIT 许可证发布。所使用的第三方组件及其许可证见 [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES)。

## M0–M1 基础与 M2 首批能力已实现

- 标准 HTTP 状态码与 ProblemDetails；Admin.NET 响应信封为显式可选适配器。
- 显式模块注册、CQRS 分发、租户上下文和基于域名的租户解析。
- 传输无关的 Command/Query 行为管道；FluentValidation 显式注册、统一 `validation.failed` 错误，并在事务开启前短路无效命令。
- Dapper-first 数据访问、SQL 作用域保护和事务边界，不引入 EF Core。
- SQL Server/MySQL 双数据库 DbUp 迁移及 Testcontainers 集成测试。
- MessagePack 二进制 Outbox、租约式至少一次消费、schema 版本路由和指数退避。
- FusionCache 作为唯一缓存实现，同时暴露 `IFusionCache` 与 `.AsHybridCache()` 适配的 `HybridCache`。
- System.Text.Json 源生成 HTTP 合约、Serilog 有界异步日志、OpenTelemetry 和健康检查。
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
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --minimum-expected-tests 116
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --minimum-expected-tests 4
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --minimum-expected-tests 9
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --minimum-expected-tests 8 --timeout 10m
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
```

AppHost 默认启动 SQL Server、Redis、Migrator、API 和 Worker。首次运行会要求输入宿主管理员账号和强密码，其中密码按 Secret Parameter 处理；Migrator 成功退出后，API 与 Worker 才会启动，本地 `localhost` 租户和宿主管理员均被幂等创建。

更完整的数据库切换、部署顺序、缓存和 API 约定见 [本地开发指南](docs/development/getting-started.md)。架构设计及 Admin.NET 功能对标路线位于 `docs/`。

客户端基础验证：

```powershell
corepack enable
pnpm install --frozen-lockfile
pnpm test:workspace
pnpm test:clients
pnpm build:clients
pnpm test:e2e
```

## 客户端规划

- `ui/admin`：Vue 3 + TypeScript + Vite + Element Plus 主管理端；
- `ui/admin-layui`：基于 MIT Layui 2 独立实现的 HTML/CSS/原生 JavaScript 管理端，与 Vue 覆盖相同后台功能并同步验收；layuiAdmin 仅作交互参考，不复制其非 MIT 主题资产；
- `clients/uniapp`：一套代码覆盖 H5、微信小程序和支付宝小程序；
- `clients/flutter`：原生 Android/iOS 与 Windows/macOS/Linux 桌面客户端；
- .NET MAUI：仅在真实 C#/Windows 企业需求命中决策门禁后提供可选模板。

Vue/Layui 的浏览器契约、原创管理壳、登录、启动恢复、刷新轮换、退出、当前用户、可信租户切换、Host 返回、动态权限导航、按钮可见性、标准错误展示与同场景双端 E2E 已经实现。两端还共享 `zh-CN/en-US` 纯文本国际化契约，并通过无 axe 排除项的 WCAG 2.2 A/AA、键盘焦点、320 CSS px 重排和减弱动画自动验收。动态导航只能映射到各客户端本地精确白名单；除语言偏好外，令牌和租户授权状态不写入 Web Storage。Windows Edge + NVDA 和强制颜色模式仍待人工验证，因此 C1 保持 `Implemented`；后台业务 CRUD 继续按 C2 路线交付。uni-app、Flutter 与可选 MAUI 目前仅处于规划阶段。详细决策见[多客户端前端策略](docs/superpowers/specs/2026-07-17-multi-client-frontend-strategy-design.md)，分阶段状态和依赖见[客户端交付路线图](docs/roadmap/client-delivery-roadmap.md)。

## 当前边界

M1 聚焦可运行的基础设施与第一条租户垂直切片，M2 已落地跨传输验证管道、Identity 安全会话、最小 RBAC 授权上下文和双端权限导航。当前能力仍不等于完整后台 RBAC：用户与租户账号 CRUD、角色授权、菜单管理、组织数据范围和强制下线仍属于后续切片。SignalR/Realtime 在 M2 后续迭代中引入；真实服务拆分后才引入 gRPC + Protobuf；AI、MCP 与 Agentic Web/AG-UI 位于独立的 M5+ 计划中。
