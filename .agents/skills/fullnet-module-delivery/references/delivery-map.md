# Full.NET 模块交付地图

## 目录职责

| 路径 | 责任 |
| --- | --- |
| `src/BuildingBlocks/Full.NET.Abstractions` | 无基础设施依赖的 Result、消息、租户、时间与 ID 契约 |
| `src/BuildingBlocks/Full.NET.Modularity` | 模块注册、Command/Query Dispatcher 和行为管道 |
| `src/BuildingBlocks/Full.NET.Data.Abstractions` | SQL 执行、事务、Outbox 和数据库选项契约 |
| `src/BuildingBlocks/Full.NET.Data.Dapper` | Dapper 执行器、会话、事务和 Outbox 存储 |
| `src/BuildingBlocks/Full.NET.Migrations.DbUp` | DbUp Runner 与 SQL Server/MySQL 迁移脚本 |
| `src/BuildingBlocks/Full.NET.Hosting` | API 映射、ProblemDetails、JSON、日志和健康端点 |
| `src/BuildingBlocks/Full.NET.Localization` | 规范语言目录、Accept-Language 请求协商、CultureScope 与响应头辅助能力 |
| `src/Compatibility/Full.NET.Compatibility.AdminNet` | Admin.NET 可选响应包络与适配注册 |
| `src/Modules/Full.NET.Modules.*` | 按 Contracts、Domain、Features、Persistence、Serialization 组织的业务模块 |
| `src/Hosts/Full.NET.Host.Api` | HTTP Host 与模块装配 |
| `src/Hosts/Full.NET.Host.Worker` | Outbox、通知和后台处理 |
| `tests/Full.NET.*Tests` | Unit、Compatibility、Architecture、Integration 四类验证 |

## 现有 Tenancy 参考切片

读取以下文件以观察当前约定，不要机械复制不适用部分：

- `src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs`：模块服务注册；
- `src/Modules/Full.NET.Modules.Tenancy/TenancyApplicationBuilderExtensions.cs`：中间件与 Endpoint 映射；
- `src/Modules/Full.NET.Modules.Tenancy/Features/ProvisionTenant/`：Command、Validator、Handler、Endpoint 与服务；
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

## 验证命令

先构建最终 Release 状态：

```powershell
dotnet build Full.NET.slnx -c Release
```

直接运行 Microsoft Testing Platform 程序集：

```powershell
dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 186
dotnet tests/Full.NET.CompatibilityTests/bin/Release/net10.0/Full.NET.CompatibilityTests.dll --no-ansi --progress off --minimum-expected-tests 5
dotnet tests/Full.NET.ArchitectureTests/bin/Release/net10.0/Full.NET.ArchitectureTests.dll --no-ansi --progress off --minimum-expected-tests 11
dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --minimum-expected-tests 18 --timeout 15m
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
