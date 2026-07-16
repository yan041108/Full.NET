# Full.NET

Full.NET 是面向产品研发和项目快速交付的 .NET 10 基础框架。项目以模块化单体作为默认部署形态，吸收 eShop 的边界与可观测性思路，并以 Admin.NET 的业务能力范围作为长期功能对标目标；底层实现保持独立、可测试和可逐步拆分。

项目最终以 MIT 许可证发布。所使用的第三方组件及其许可证见 [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES)。

## M0–M1 基础与 M2 验证管道已实现

- 标准 HTTP 状态码与 ProblemDetails；Admin.NET 响应信封为显式可选适配器。
- 显式模块注册、CQRS 分发、租户上下文和基于域名的租户解析。
- 传输无关的 Command/Query 行为管道；FluentValidation 显式注册、统一 `validation.failed` 错误，并在事务开启前短路无效命令。
- Dapper-first 数据访问、SQL 作用域保护和事务边界，不引入 EF Core。
- SQL Server/MySQL 双数据库 DbUp 迁移及 Testcontainers 集成测试。
- MessagePack 二进制 Outbox、租约式至少一次消费、schema 版本路由和指数退避。
- FusionCache 作为唯一缓存实现，同时暴露 `IFusionCache` 与 `.AsHybridCache()` 适配的 `HybridCache`。
- System.Text.Json 源生成 HTTP 合约、Serilog 有界异步日志、OpenTelemetry 和健康检查。
- API、Worker、Migrator 与 .NET Aspire AppHost 的完整本地编排。

## 环境要求

- .NET 10 SDK
- Docker Desktop（Windows 使用 Linux containers/WSL 2）或兼容 Docker Engine
- Git

## 快速开始

```powershell
dotnet restore Full.NET.slnx
dotnet build Full.NET.slnx --configuration Release
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --minimum-expected-tests 48
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --minimum-expected-tests 4
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --minimum-expected-tests 7
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --minimum-expected-tests 6 --timeout 10m
dotnet run --project src/Hosts/Full.NET.AppHost/Full.NET.AppHost.csproj
```

AppHost 默认启动 SQL Server、Redis、Migrator、API 和 Worker。Migrator 成功退出后，API 与 Worker 才会启动；本地 `localhost` 租户会被幂等创建。

更完整的数据库切换、部署顺序、缓存和 API 约定见 [本地开发指南](docs/development/getting-started.md)。架构设计及 Admin.NET 功能对标路线位于 `docs/`。

## 当前边界

M1 聚焦可运行的基础设施与第一条租户垂直切片，M2 已先落地跨传输验证管道。SignalR/Realtime 在 M2 后续迭代中引入；真实服务拆分后才引入 gRPC + Protobuf；AI、MCP 与 Agentic Web/AG-UI 位于独立的 M5+ 计划中。
