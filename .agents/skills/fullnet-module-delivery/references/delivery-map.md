# Full.NET 模块交付地图

## 目录职责

| 路径 | 责任 |
| --- | --- |
| `src/BuildingBlocks/Full.NET.Abstractions` | 无基础设施依赖的 Result、消息、租户、时间与 ID 契约 |
| `src/BuildingBlocks/Full.NET.Modularity` | 模块注册、Command/Query Dispatcher 和行为管道 |
| `src/BuildingBlocks/Full.NET.Data.Abstractions` | SQL 执行、事务、Outbox 和数据库选项契约 |
| `src/BuildingBlocks/Full.NET.Data.CodeGeneration` | 嵌入 Naming Profile 的表名、索引/约束、主键物理类型映射与稳定协议命名内核 |
| `src/BuildingBlocks/Full.NET.Data.Dapper` | Dapper 执行器、会话、事务和 Outbox 存储 |
| `src/BuildingBlocks/Full.NET.Migrations.DbUp` | DbUp Runner 与 SQL Server/MySQL 迁移脚本 |
| `src/BuildingBlocks/Full.NET.Hosting` | API 映射、ProblemDetails、JSON、日志和健康端点 |
| `src/BuildingBlocks/Full.NET.Localization` | 规范语言目录、Accept-Language 请求协商、CultureScope 与响应头辅助能力 |
| `src/Compatibility/Full.NET.Compatibility.AdminNet` | Admin.NET 可选响应包络与适配注册 |
| `src/Composition/Full.NET.Composition` | 官方模块共享目录、Api/Worker/Migrator 的显式 Host Profile 与最小后台装配 |
| `packages/client-contracts` | ProblemDetails/身份/租户/权限契约解析，以及无框架 headless 层（`createHttpClient`、`createIdentitySession`、`createAdminNavigationCatalog`）；Vue/Layui 只做渲染适配 |
| `src/Modules/Full.NET.Modules.*`（Core） | 按 Contracts、Domain、Features、Persistence、Serialization 组织的业务逻辑；禁止直接使用 ASP.NET Core API（`BusinessModuleCores_DoNotDependOnAspNetCore` 门禁），只对外暴露 `Contracts` 命名空间 |
| `src/Modules/Full.NET.Modules.*.Http`（可选） | 承载 `IFullNetModule` 实现、Endpoint 与中间件的 Web 面；引用同名 Core，仅暴露模块入口类型（如 Tenancy 已拆分） |
| `src/Modules/Full.NET.Modules.Identity.Contracts` | Identity 跨模块契约（Claim 类型、会话上下文、导航/权限定义等），web-free，供其他模块 Core 引用而不拖入 ASP.NET Core |
| `src/Hosts/Full.NET.Host.Api` | HTTP Host 与模块装配 |
| `src/Hosts/Full.NET.Host.Worker` | Outbox、通知和后台处理 |
| `tests/Full.NET.*Tests` | Unit、Compatibility、Architecture、Integration 四类验证 |

## 现有 Tenancy 参考切片

读取以下文件以观察当前约定，不要机械复制不适用部分：

- `src/Modules/Full.NET.Modules.Tenancy.Http/TenancyModule.cs`：模块服务注册、后台能力（`AddBackgroundServices`）与中间件贡献（`UseModuleMiddleware`）；宿主通过 `UseFullNetModuleMiddleware(stage)` 统一应用，禁止在宿主直接引用模块或手写 `UseXxx`；Web 面（Endpoint、`TenantResolutionMiddleware`）均在 `.Http` 项目；
- `src/Modules/Full.NET.Modules.Tenancy/Features/ProvisionTenant/`：Core 中的 Command、Validator、Handler 与服务（Endpoint 位于 `.Http` 的同名 Feature 目录）；
- `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantSql.cs`：显式 SQL；
- `src/Modules/Full.NET.Modules.Tenancy/Serialization/`：JSON 源生成与 MessagePack Resolver；
- `tests/Full.NET.IntegrationTests/Tenancy/TenantProvisioningTests.cs`：双数据库事务与 Outbox；
- `tests/Full.NET.IntegrationTests/Api/`：SQL Server/MySQL API 契约。

## 变更到文件的映射

| 变更类型 | 检查或修改位置 |
| --- | --- |
| 新 Command/Query | 模块 `Features/<UseCase>/`、Dispatcher 注册、Unit Tests |
| 新校验 | `IValidator<T>`、模块显式注册、Validation Behavior Tests |
| 新表或列 | `Migrations/SqlServer` 与 `Migrations/MySql` 同序号脚本、旧结构升级与未记账部分完成恢复 Integration Tests |
| 新集成事件 | 模块 `Contracts`、MessagePack Resolver、Outbox 写入、Worker Handler、序列化测试 |
| 新缓存 | 模块缓存消费者、租户化 Key、提交后失效 Handler、Unit/Integration Tests |
| 新公开 JSON DTO | 模块 `JsonSerializerContext`、API 测试、兼容性评估 |
| 新 Admin.NET 响应 | Compatibility 层 Mapper 与 Compatibility Tests |
| 新模块依赖 | 模块项目引用、Architecture Tests、`Directory.Packages.props`、许可通知 |
| 新 Endpoint/中间件（Web 面） | 模块 `.Http` 项目（Endpoint 保持 internal、中间件通过 `UseModuleMiddleware` 贡献）；Core 保持 web-free；`BusinessModuleCores_DoNotDependOnAspNetCore` 与导出断言 |
| 新模块宿主装配 | `Full.NET.Composition`（引用模块 `.Http`）、`FullNetHostProfile`、Profile Unit Tests 与宿主 Architecture Tests |
| 新数据库/API/机器码或生成模板 | `rules/naming-conventions.md`、`contracts/naming/`、CodeGeneration 内核、`pnpm test:naming` |

## 验证命令

先构建最终 Release 状态：

```powershell
pnpm test:naming
dotnet build Full.NET.slnx -c Release
```

直接运行 Microsoft Testing Platform 程序集：

```powershell
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 322
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 7
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 26
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --minimum-expected-tests 101 --timeout 90m
```

增删测试时同步更新：

- `README.md`；
- `docs/development/getting-started.md`；
- CI 工作流中的 `--minimum-expected-tests`；
- 本 Skill 中已经过期的命令或数量。

## 文档与状态

- 架构决策：`docs/superpowers/specs/`；
- 实施步骤：`docs/superpowers/plans/`；
- Admin.NET 功能状态：`docs/roadmap/adminnet-feature-parity.md`；
- 第三方许可：`THIRD-PARTY-NOTICES`；
- 项目规则：`AGENTS.md` 与 `rules/`。

只有功能、关键流程、授权、租户、双数据库、API 契约、来源和文档全部验收后，才能把路线图状态改为 `Verified`。
