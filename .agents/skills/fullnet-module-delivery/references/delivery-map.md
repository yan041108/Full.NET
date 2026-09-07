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
| `src/Modules/Full.NET.Modules.*`（主项目） | 每个内聚业务模块默认只有一个主项目，按 Contracts、Domain、Features、Persistence、Serialization 组织；CRUD、菜单、实体、用例与 Endpoint 不单独建项目 |
| `src/Modules/Full.NET.Modules.*.Contracts`（可选） | 只有存在真实跨模块或外部编译期消费者且需要稳定契约程序集隔离时创建；否则使用主项目内 `Contracts/` |
| `src/Modules/Full.NET.Modules.*.Http`（可选） | 只有同一 web-free Core 被非 HTTP 宿主真实复用且能证明独立传输适配收益时创建；Tenancy 历史 `.Http` 拆分已合并回主项目，不再作为新模块模板 |
| `src/Modules/Full.NET.Modules.Identity.Contracts` | Identity 跨模块契约（Claim 类型、会话上下文、导航/权限定义等），web-free，供其他模块 Core 引用而不拖入 ASP.NET Core |
| `src/Hosts/Full.NET.Host.Api` | HTTP Host 与模块装配 |
| `src/Hosts/Full.NET.Host.Worker` | Outbox、通知和后台处理 |
| `tests/Full.NET.*Tests` | Unit、Compatibility、Architecture、Integration 四类验证 |

## 现有 Tenancy 参考切片

读取以下文件以观察当前约定，不要机械复制不适用部分：

- `src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs`：模块服务注册、后台能力（`AddBackgroundServices`）与中间件贡献（`UseModuleMiddleware`）；宿主通过 `UseFullNetModuleMiddleware(stage)` 统一应用，禁止在宿主直接引用模块或手写 `UseXxx`；
- `src/Modules/Full.NET.Modules.Tenancy/Features/`：同一主项目内同时承载 Command、Validator、Handler、Endpoint 与服务；只有满足项目拓扑门禁时才允许把 Web 面拆到独立 `.Http`；
- `src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantSql.cs`：显式 SQL；
- `src/Modules/Full.NET.Modules.Tenancy/Serialization/`：JSON 源生成；可靠事件 DTO 在 Contracts 中使用 `[MemoryPackable] partial`；
- `tests/Full.NET.IntegrationTests/Tenancy/TenantProvisioningTests.cs`：双数据库事务与 Outbox；
- `tests/Full.NET.IntegrationTests/Api/`：SQL Server/MySQL API 契约。

## 变更到文件的映射

| 变更类型 | 检查或修改位置 |
| --- | --- |
| 新 Command/Query | 模块 `Features/<UseCase>/`、Dispatcher 注册、Unit Tests |
| 新校验 | `IValidator<T>`、模块显式注册、Validation Behavior Tests |
| 新表或列 | `Migrations/SqlServer` 与 `Migrations/MySql` 同序号脚本、旧结构升级与未记账部分完成恢复 Integration Tests |
| 新集成事件 | 模块 `Contracts` 的 `[MemoryPackable] partial` DTO、Outbox 写入、Worker Handler、序列化测试；仅重要业务 Integration Event 才允许 Outbox |
| 新缓存 | 模块缓存消费者、租户化 Key、`C0/S0-L2/S1/S2/N0` 分类、事务提交后直接删除 L1/L2 并广播 Backplane、Unit/Integration Tests |
| 新公开 JSON DTO | 模块 `JsonSerializerContext`、API 测试、兼容性评估 |
| 新对外 HTTP 契约 | 冻结 `contracts/openapi/<slice>-v1.json`、补充 `tests/openapi/<slice>-contract.test.mjs`、运行 `pnpm test:openapi`；对应 C# 契约/Endpoint 变化后用 Integration `OpenApi*ContractAssertions` 对运行时 `/openapi/v1.json` 做断言，不把 node 夹具当唯一证据 |
| 新 Admin.NET 响应 | Compatibility 层 Mapper 与 Compatibility Tests |
| 新模块依赖 | 只引用对方公开 Contracts；Architecture Tests、`Directory.Packages.props`、许可通知 |
| 新 Endpoint/中间件（Web 面） | 默认放在模块主项目并保持 internal；只有满足项目拓扑门禁时才新建 `.Http`，同时补充 Core web-free、导出和依赖断言 |
| 新模块宿主装配 | `Full.NET.Composition`（引用模块主项目或有证据的适配项目）、`FullNetHostProfile`、Profile Unit Tests 与宿主 Architecture Tests |
| 新数据库/API/机器码或生成模板 | `rules/naming-conventions.md`、`contracts/naming/`、CodeGeneration 内核、`pnpm test:naming` |

## 缓存分类强制门禁

缓存条目必须在设计时归入 `C0/S0-L2/S1/S2/N0`，不得按瞬时 QPS 切换语义。权威定义见 [`ADR-0005`](../../../../docs/architecture/adr/ADR-0005-high-concurrency-modular-monolith-multi-instance-production-baseline.md) 与总体架构 Spec [§13](../../../../docs/superpowers/specs/2026-07-17-fullnet-architecture-design.md#13-缓存设计)：

| 类别 | 形态 | 失效与回退 |
| --- | --- | --- |
| C0 | 权威源决策；禁用以 L1 作正确性证明 | fail-closed；必要时完全绕过缓存 |
| S0-L2 | 仅共享 L2，禁用 L1 | 直接删/更 L2；失败读权威源或 fail-closed |
| S1 | L1 + L2 + Backplane | 提交后直接删当前 L1/共享 L2 并广播 Backplane；短 TTL/版本兜底 |
| S2 | L1 + L2，可选 Backplane | 仅显式配置才允许有界 Fail-Safe |
| N0 | 不缓存 | 直接读权威源 |

缓存失效不写 Outbox。不要再新增“缓存失效 Outbox Handler”示例；存量兼容排空属于独立收缩发布，不在模块交付默认路径。

## 验证命令

以下是受影响 .NET 模块的快速命令参考，按任务选择；只有命名变化才运行命名检查。执行策略见下方权威链接。

```powershell
pnpm test:naming
dotnet build Full.NET.slnx -c Release
```

再按 Microsoft Testing Platform 入口运行：

```powershell
pnpm test:dotnet:unit -- --no-build
pnpm test:dotnet:compatibility -- --no-build
pnpm test:dotnet:architecture -- --no-build
```

测试数量、超时和 Integration 分片只维护在
[`eng/testing/test-matrix.json`](../../../../eng/testing/test-matrix.json)；Skill
不得复制这些易变数字。本地任务不得运行完整集合。

Integration 的影响集、阶段、Provider、执行位置和未验证项统一按 [测试与验证](../../../../rules/development-quality.md#11-测试与验证) 选择；不要顺序执行一份通用重测清单。测试数量仍只维护在上述矩阵。

## 文档与状态

普通任务使用会话计划与交付说明；仅达到 [文档预算](../../../../rules/development-quality.md#121-文档产物分层) 时创建下面的永久文档。

- 架构决策：`docs/superpowers/specs/`；
- 实施步骤：`docs/superpowers/plans/`；
- Admin.NET 功能状态：`docs/roadmap/adminnet-feature-parity.md`；
- 第三方许可：`THIRD-PARTY-NOTICES`；
- 项目规则：`AGENTS.md` 与 `rules/`。

只有功能、关键流程、授权、租户、双数据库、API 契约、来源和文档全部验收后，才能把路线图状态改为 `Verified`。

## 配置与外部资源

- 新增配置或外部资源边界时，必须同时提供启动期校验、失败路径测试和运行时断言，禁止把配置错误推迟到首个业务请求。
