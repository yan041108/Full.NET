# Full.NET 总体架构设计规格

- 状态：已批准；后续在既定目标和授权范围内默认采用本文推荐方案推进
- 硬化补充：[`2026-07-18-architecture-hardening-design.md`](2026-07-18-architecture-hardening-design.md)
- 2026-07-22 巡检增补：[`../../verification/architecture-review-2026-07-22.md`](../../verification/architecture-review-2026-07-22.md)
- 架构演进决策：[`ADR-0002：强化型模块化单体与按证据拆分`](../../architecture/adr/ADR-0002-modular-monolith-evolution.md)
- 当前能力：[`../../roadmap/capability-status.md`](../../roadmap/capability-status.md)
- 日期：2026-07-17
- 项目目录：`G:\wwwroot\github_fork\Full.NET`
- 产品名称：Full.NET
- 最终发布许可证：MIT

## 1. 文档目的

本文定义 Full.NET 第一阶段到 1.0 版本的总体架构、模块边界、数据访问、权限、多租户、缓存、安全、代码生成、测试和部署规范。后续实施计划必须服从本文；如需改变本文中的架构决策，应先更新设计规格并记录原因。

Full.NET 的定位不是业务成品，也不是 Admin.NET.Pro 的原地重构，而是一套面向管理系统、企业应用和中小型 SaaS 的开源快速开发底座。它需要具备完善的基础功能、清晰的扩展边界、可审查的 SQL 和适合接项目快速交付的代码生成能力。

## 2. 背景与约束

### 2.1 可参考资产

- `G:\wwwroot\github_fork\Admin.NET.Pro`：作为功能清单、交互方式和验收基准。用户已取得作者授予的二开、商用授权。
- [dotnet/eShop](https://github.com/dotnet/eShop)：作为 .NET 官方架构、测试、容器化和 Aspire 编排参考。
- [Dapper](https://github.com/DapperLib/Dapper)：默认数据映射器。
- [DbUp](https://github.com/DbUp/DbUp)：数据库迁移引擎。
- [FusionCache](https://github.com/ZiggyCreatures/FusionCache)：唯一缓存实现。
- [System.Text.Json](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)：外部 HTTP JSON 的唯一默认实现。
- [gRPC for .NET](https://learn.microsoft.com/aspnet/core/grpc/)：出现跨进程同步调用时的默认 RPC 技术。
- [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)：内部可靠异步事件和高性能二进制传输的默认序列化实现。
- [Serilog](https://github.com/serilog/serilog)：`ILogger<T>` 后面的默认结构化日志实现。
- [ASP.NET Core SignalR](https://learn.microsoft.com/aspnet/core/signalr/)：浏览器和应用客户端实时通信实现。
- [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)、[Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/overview/) 和 [MCP C# SDK](https://csharp.sdk.modelcontextprotocol.io/)：AI、Agent 与 Agentic Web 的基础抽象和协议适配参考。

### 2.2 已确认约束

- 新建独立框架，不在 Admin.NET.Pro 中直接重构。
- Full.NET 目录必须是独立 Git 仓库，不能把父级脏仓库的内容带入提交。
- 后端采用 .NET 10 LTS、ASP.NET Core、C#，开启 Nullable。
- 架构形态为强化型模块化单体；Full.NET 1.0 不采用全面微服务，局部模块只有满足 ADR-0002 的全部拆分门禁后才能独立部署。
- 数据访问采用 Dapper-first，不以 EF Core 作为默认运行时依赖。
- 数据迁移采用 DbUp 和可审查的 SQL 脚本。
- 最终公开仓库使用 MIT License，并维护第三方许可清单。
- 管理端同时建设 Vue 3 主管理端与 Layui 2 原生 JS/HTML 管理端，两端覆盖相同后台功能并同步验收；所有客户端通过 OpenAPI 与后端解耦。
- H5、微信小程序和支付宝小程序采用 uni-app Vue 3；原生移动端和 Windows/macOS/Linux 桌面端默认采用 Flutter，.NET MAUI 仅作为命中决策门禁后的可选模板。
- 外部 REST JSON 统一使用 System.Text.Json 源代码生成；Newtonsoft.Json 只允许作为可选兼容 Provider。
- 同进程模块调用不序列化；跨进程同步调用使用 gRPC + Protobuf；可靠异步事件默认使用 MessagePack，不使用 JSON 载荷。
- 业务代码只依赖 `ILogger<T>`；高频日志使用 `LoggerMessage` 源生成，Serilog 负责异步有界结构化输出。
- SignalR 通过实时通信抽象接入；官方客户端优先 MessagePack Hub Protocol，同时保留 JSON 客户端兼容。
- AI 核心保持模型供应商中立；Agent、MCP、AG-UI 等能力必须位于独立模块或协议适配层。

### 2.3 长期功能基线

`G:\wwwroot\github_fork\Admin.NET.Pro` 的 v2.1 分支是 Full.NET 的长期功能对标基线。Admin.NET.Pro 中具备实际使用价值的功能，原则上都必须在 Full.NET 的 Core、Official Module、Provider、Sample 或 Client 中找到对应实现。

“全量对标”指业务能力、使用流程和交付价值对等，不要求复制原表结构、API、依赖方式或源码。Full.NET 可以为了安全性、模块边界、性能和可维护性采用不同实现。详细范围和状态维护在 `docs/roadmap/adminnet-feature-parity.md`。

## 3. 架构目标与非目标

### 3.1 目标

1. 新项目可以通过模板快速创建，并按需选择模块。
2. 核心功能覆盖身份、权限、组织、多租户、配置、审计、文件、通知、任务和代码生成。
3. SQL 可见、可调试、可分析执行计划，不在运行时隐藏生成复杂 SQL。
4. 模块能独立理解、独立测试，通过公开契约通信。
5. 同一套业务模块可在单机、标准分离和多实例模式中部署。
6. 安全、多租户和审计是默认能力，不依赖项目团队重复实现。
7. 生成的代码是普通可维护代码，不被运行时代码生成器锁定。

### 3.2 非目标

以下能力不进入首版核心，但仍可进入 Admin.NET 全量对标路线中的官方扩展、Provider 或 Sample：

- 微服务拆分和服务网格；
- 分布式事务；
- 自研 ORM 或表达式树转 SQL；
- 通用万能 Repository；
- 工作流、支付、微信、OCR、MQTT、IoT 等扩展能力；
- 同时完整支持所有关系型数据库；
- 用缓存代替数据库一致性；
- 直接把 Admin.NET.Core 的大合集结构复制到新框架。

上述能力以后只能以独立模块、Provider、模板或示例项目加入，不得污染核心 BuildingBlocks。电商不属于 Admin.NET 功能对标基线，只作为 Full.NET 架构示例或独立产品场景。

## 4. 总体架构

### 4.1 形态

Full.NET 采用模块化单体和垂直功能切片：

```text
HTTP / Worker
      |
      v
Host + Cross-cutting Pipelines
      |
      v
Business Modules
      |
      v
Data Abstractions + Dapper + DbUp
      |
      v
SQL Server / MySQL
```

业务模块默认在同一 API 进程中运行，但拥有明确的代码、契约和数据所有权边界。模块内部实现默认 `internal`，跨模块只依赖公开 Contracts；共享数据库实例不等于共享数据模型，模块禁止直接访问其他模块的内部表。API、Worker、Migrator 按运行角色分离，AppHost 只负责本地编排；这种角色分离不构成微服务拆分。

Full.NET 1.0 不为未来假设支付微服务复杂度。局部模块只有同时满足以下门禁并通过独立 ADR 后，才能围绕稳定 Contracts、Integration Events 或 gRPC 契约拆分：

1. 存在可测量的独立伸缩、SLA、故障隔离或发布节奏需求；
2. 模块能够独占写入自己的数据，不访问其他模块内部表；
3. 目标业务流程不依赖跨模块本地事务；
4. Integration Event 或 RPC 契约已经版本化并具有兼容测试；
5. Outbox、重试、死信、重放和可观测性已经在生产等价拓扑验证；
6. ADR 证明独立部署收益高于新增运维、测试和故障处理成本。

详细理由、备选方案和演进后果见 [`ADR-0002`](../../architecture/adr/ADR-0002-modular-monolith-evolution.md)。

### 4.2 解决方案结构

```text
Full.NET/
├── Full.NET.slnx
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
├── LICENSE
├── THIRD-PARTY-NOTICES
├── src/
│   ├── BuildingBlocks/
│   │   ├── Full.NET.Abstractions
│   │   ├── Full.NET.Hosting
│   │   ├── Full.NET.Modularity
│   │   ├── Full.NET.Data.Abstractions
│   │   ├── Full.NET.Data.Dapper
│   │   ├── Full.NET.Serialization.MessagePack
│   │   ├── Full.NET.Migrations.DbUp
│   │   ├── Full.NET.Caching.Fusion
│   │   ├── Full.NET.Realtime.Abstractions
│   │   └── Full.NET.Data.CodeGeneration
│   ├── Modules/
│   │   ├── Full.NET.Modules.Identity
│   │   ├── Full.NET.Modules.Organization
│   │   ├── Full.NET.Modules.Tenancy
│   │   ├── Full.NET.Modules.Settings
│   │   ├── Full.NET.Modules.Auditing
│   │   ├── Full.NET.Modules.Files
│   │   ├── Full.NET.Modules.Notifications
│   │   ├── Full.NET.Modules.Jobs
│   │   └── Full.NET.Modules.CodeGeneration
│   ├── Compatibility/
│   │   └── Full.NET.Compatibility.AdminNet
│   └── Hosts/
│       ├── Full.NET.Host.Api
│       ├── Full.NET.Host.Worker
│       ├── Full.NET.Host.Migrator
│       └── Full.NET.AppHost
├── ui/
│   ├── admin
│   └── admin-layui
├── clients/
│   ├── uniapp
│   └── flutter
├── templates/
│   ├── fullnet-app
│   └── fullnet-module
├── samples/Full.NET.Sample.Crm
├── tests/
│   ├── Full.NET.UnitTests
│   ├── Full.NET.ArchitectureTests
│   ├── Full.NET.IntegrationTests
│   ├── Full.NET.GeneratorTests
│   ├── Full.NET.CompatibilityTests
│   └── Full.NET.E2E
├── benchmarks/Full.NET.Benchmarks
└── docs/
    └── roadmap/adminnet-feature-parity.md
```

BuildingBlocks 必须保持小而稳定，不允许依赖业务模块。逻辑模块、功能切片和物理项目是三个不同层级：一个逻辑模块可以包含多个 CRUD、实体和用例，但这些小功能默认只形成目录与垂直切片，不形成新的 `.csproj`。

业务模块物理拓扑默认采用“一个主项目＋按证据可选项目”：

```text
Full.NET.Modules.<Module>             # 默认且必须：模块实现、持久化、注册与 Endpoint
Full.NET.Modules.<Module>.Contracts   # 可选：存在真实跨模块或外部编译期消费者
Full.NET.Modules.<Module>.Http        # 可选：存在独立传输适配收益
```

项目创建规则如下：

| 变化 | 默认承载方式 | 允许新增项目的证据 |
| --- | --- | --- |
| CRUD、菜单、实体、Command/Query、Endpoint | 主项目内 `Features/<UseCase>` 等垂直切片 | 不允许按小功能拆项目 |
| 公开接口、DTO、权限定义、Integration Event | 无外部消费者时放在主项目 `Contracts/` | 至少一个真实跨模块或外部编译期消费者，且需要稳定契约的程序集隔离 |
| HTTP、Worker 或其他适配 | 先由主项目提供分层注册入口，由 Host Profile 选择能力 | 同一核心被非该传输宿主真实复用，并能证明依赖、打包或安全隔离收益 |
| 独立业务模块 | 新建一个主项目 | 具有独立数据所有权、业务不变量、生命周期和公开契约；不能只由菜单层级决定 |

禁止为每个模块机械拆出 Domain/Application/Infrastructure/API 项目，禁止按 CRUD、数据表或前端菜单增加项目，也禁止为了压低项目数量把无关业务合并为大杂烩模块。API、Worker、Migrator 的运行角色分离不自动要求模块拆出 `.Http` 或 `.Worker`；先使用同一程序集内的显式注册入口和 Host Profile，只有上表证据成立并由 Spec 或计划记录真实消费者、依赖方向、收益与架构测试时才增加可选项目。

### 4.3 模块内部结构

```text
Full.NET.Modules.Identity/
├── Contracts/
├── Domain/
├── Features/
│   └── Users/
│       ├── CreateUser/
│       └── QueryUsers/
├── Persistence/
│   ├── Sql/
│   └── Migrations/
├── Endpoints/
└── IdentityModule.cs
```

只有 `Contracts` 中的契约、DTO、权限定义和集成事件可以作为其他模块的依赖。Domain、Features、Persistence 和 Endpoints 默认使用 `internal`。

## 5. 模块系统与通信

### 5.1 模块入口

每个模块实现统一入口，提供名称、依赖、服务注册、Endpoint 映射和初始化能力。应用优先采用显式注册：

```csharp
builder.Services
    .AddFullNet()
    .AddIdentityModule()
    .AddOrganizationModule()
    .AddTenancyModule();
```

不依赖大范围运行时程序集扫描。模块依赖图必须是有向无环图，由架构测试验证。模块排序依赖使用唯一、稳定的模块键表达，不得要求业务模块引用另一个模块的实现程序集或具体 `Module` 类型。跨模块编译期依赖只允许指向公开 Contracts；生产模块不得通过 `InternalsVisibleTo` 获取另一个生产模块的内部实现。Composition 是唯一允许引用具体模块入口并组装 Catalog 的位置。

Api、Worker、Migrator 和 Test 使用显式 Host Profile 声明完整模块或最小后台能力，模块 Catalog 与架构测试必须阻止宿主漏注册、顺序漂移和非 HTTP 宿主装入完整 HTTP 模块。API Profile 装配 HTTP、认证与业务运行时能力；Worker Profile 只装配后台消费者；Migration/Seed Profile 只装配 DbUp、Seed 编排与 Contributor 所需服务。三者不得为了复用测试夹具而共享超出角色职责的注册集合。

模块初始化必须在宿主接收业务流量前按依赖顺序恰好执行一次，失败时宿主不得进入就绪状态。初始化钩子只允许执行幂等运行时自检或准备，不得替代 Migration/Seed，也不得产生不可回滚外部副作用。接口一旦存在就必须有统一调用链；如果没有真实使用者，应删除钩子而不是保留“接口有、行为无”的能力。

### 5.2 Command 和 Query

框架提供轻量抽象：

- `ICommand<TResult>`、`ICommandHandler<TCommand, TResult>`；
- `IQuery<TResult>`、`IQueryHandler<TQuery, TResult>`；
- `ICommandDispatcher`、`IQueryDispatcher`。

默认调度器只负责 Handler 定位、验证、权限、事务、审计、追踪、取消和超时，不实现通用消息总线，也不绑定 MediatR。写操作进入事务管道；查询默认不启动显式事务。

### 5.3 模块通信规则

允许：

- 调用对方公开的 Contract Service；
- 发布和订阅公开的 Integration Event；
- 使用 BuildingBlocks 中稳定的通用抽象。

禁止：

- 访问其他模块的表、内部实体或内部 Handler；
- 跨模块共享业务 Repository；
- 使用 Service Locator 获取服务；
- 通过反射绕过模块可见性。

需要立即一致结果时使用 Contract Service；允许最终一致时使用 Integration Event。外部或可靠异步事件经 Outbox 发布，消费端必须幂等，不采用分布式事务。

### 5.4 通信与序列化矩阵

| 场景 | 传输/调用 | 序列化 | 约束 |
|---|---|---|---|
| 对外 REST API | ASP.NET Core HTTP | System.Text.Json 源生成 | 标准 HTTP、ProblemDetails、OpenAPI |
| Admin.NET 兼容 API | HTTP 适配器 | System.Text.Json | 只改变响应形状，不伪造 HTTP 200 |
| 同进程模块 | Contract Service、Command/Query | 无 | 不制造网络边界和序列化开销 |
| 跨进程同步服务 | gRPC | Protobuf | 复用 Channel，统一 Deadline、取消、认证和追踪 |
| 内部可靠异步事件 | Outbox + EventBus Provider | MessagePack；跨语言时可选 Protobuf | 二进制原样存储，消费者幂等，契约显式版本化 |
| 浏览器实时通信 | SignalR | MessagePack 优先，JSON 兼容 | 不是内部 EventBus，不承载业务事务 |
| 文件和超大二进制 | HTTP 流或对象存储引用 | 原始二进制 | 不放入单个 gRPC/SignalR/Outbox 大消息 |
| MCP、AG-UI 等开放 AI 协议 | 协议规定的 HTTP、SSE、JSON-RPC | 按协议标准 | 这是互操作边界，不受“内部业务消息不用 JSON”限制 |

gRPC 是 RPC 框架，MessagePack 是序列化格式，二者不作为同层替代项。Full.NET 不在 gRPC 中嵌套 MessagePack，也不为了未来可能拆分服务而让当前模块化单体内部走 gRPC。

### 5.5 二进制契约演进与安全

MessagePack 集成事件使用显式 `[MessagePackObject]` 和整数 `[Key(n)]`。字段只能在尾部追加；已发布 Key 不得重排、复用或改变语义，删除字段后保留其编号。禁止 Typeless 和 Contractless Resolver，所有网络、数据库及消息来源均按不可信数据处理，启用 `MessagePackSecurity.UntrustedData` 并使用最新无已知高危漏洞的受支持版本。

每个可靠事件保存 `MessageId`、`MessageType`、`SchemaVersion`、`ContentType`、`TenantId`、`TraceId` 和 `OccurredAt` 等可查询元数据。载荷以 SQL Server `varbinary(max)` 或 MySQL `longblob` 保存，不做 Base64，不依赖人工直接阅读二进制正文。压缩只在基准证明载荷大小收益超过 CPU 成本时按阈值启用。

发布第二个事件版本时必须在兼容窗口内保留并行版本 Handler，或提供基于显式旧版本契约的相邻版本升级链；升级链不能先把旧载荷反序列化为当前 DTO，也不能启用 Typeless/Contractless。发布顺序固定为“先消费者、后生产者、最后退役旧消费者”，兼容窗口覆盖最长 Outbox 保留、失败重试和部署回滚窗口。超过最大尝试次数或确定不可重试的消息必须进入可查询、可审计重放的死信状态，单条毒消息不得永久阻塞批次。

## 6. 首版业务模块

### 6.1 Identity

提供用户、账号、角色、权限、菜单、JWT Access Token、Refresh Token、会话、强制下线、验证码、登录锁定、API Key 和第三方登录扩展接口。

安全基础能力复用 ASP.NET Core Identity，数据存储实现自有 Dapper Store，不引入 EF Core Store。

### 6.2 Organization

提供组织树、部门、岗位、职级、用户多组织关系、主部门、兼任部门、负责人和数据范围。

### 6.3 Tenancy

提供租户创建、启停、过期、套餐、初始化、租户配置、域名识别和 Host 管理端租户切换。首版支持单租户及共享数据库多租户；独立数据库多租户作为后续连接工厂 Provider。

### 6.4 Settings 与 Dictionaries

提供系统级、租户级、用户级强类型配置和数据字典。优先级为：

```text
用户配置 > 租户配置 > 系统配置 > 代码默认值
```

### 6.5 Auditing

提供登录、操作、异常、API、数据变更和慢 SQL 审计，以及查询、保留、清理和脱敏策略。

### 6.6 Files

提供文件元数据、上传、下载、删除、临时文件、权限和本地存储默认实现。S3、MinIO、OSS、COS 等作为独立 Provider。

### 6.7 Notifications

提供站内通知、未读消息、通知模板、SignalR 推送、渠道抽象、发送记录和失败重试。邮件、短信和公众号作为 Provider。

### 6.8 Jobs

提供即时、延迟、周期任务、执行记录、重试、超时、失败处理和分布式锁抽象。默认实现采用数据库任务表、到期时间和带租约的 Worker 轮询，保证崩溃后任务可以重新领取；核心不绑定 Hangfire 或 Quartz.NET，二者可作为 Provider。

### 6.9 CodeGeneration

提供数据库元数据读取、模型定义、后端、SQL、Vue/Layui 双管理端页面、多平台 API 客户端和测试生成，是快速交付的核心能力。`Full.NET.Data.CodeGeneration` 是不依赖 Web 的生成引擎和 CLI 基础；当前已实现嵌入 Naming Profile 的表名、索引/约束确定性摘要和稳定协议校验纯函数，元数据读取、模板渲染、CLI 与双管理端生成仍按 M3 纵向样例推进。`Full.NET.Modules.CodeGeneration` 负责权限、任务记录、模板管理和后台页面 API，不重复实现模板引擎。

## 7. Dapper-first 数据层

### 7.1 分层

```text
Full.NET.Data.Abstractions
    |
    +-- Full.NET.Data.Dapper
    +-- Full.NET.Migrations.DbUp
    +-- Full.NET.Data.CodeGeneration
```

核心抽象包括：

- `IDbConnectionFactory`；
- `IDbSession`；
- `IUnitOfWork`；
- `ICurrentTransaction`；
- `IQueryExecutor`；
- `IMultiResultQueryExecutor` 与 `IMultiResultReader`；
- `ICommandExecutor`；
- `ISqlDialect`；
- `IMigrationRunner`。

业务代码不能直接创建连接，也不能直接调用裸 Dapper 扩展方法。框架执行器统一处理连接、事务、参数、租户上下文、超时、取消、日志、追踪和慢 SQL。

原生 `QueryMultiple` 通过自有多结果集执行器暴露，业务层不能接触 `GridReader`。`Dapper.SqlBuilder` 只有真实动态列表消费者出现时才可由 `Full.NET.Data.QueryBuilding` 封装；用户值参数化，标识符和 SQL 片段来自代码白名单。详细准入见[Dapper 辅助能力设计](2026-07-18-dapper-tooling-design.md)。

### 7.2 SQL 原则

- 所有用户输入必须参数化；
- 不拼接用户提供的列名、排序或筛选表达式；
- 允许白名单映射后的排序字段；
- 不构建表达式树到 SQL 的隐藏 ORM；
- 不使用覆盖所有实体的通用 Repository；
- 简单 CRUD 由生成器产生，复杂 SQL 存为靠近 Feature 的 `.sql` 文件；
- 原生 SQL 逃生口必须显式声明数据作用域并进入审计；
- 表、列、参数、Statement、索引和约束统一服从 [`rules/naming-conventions.md`](../../../rules/naming-conventions.md)，SQL Server/MySQL 不得各自派生命名。
- 不引入 Dapper.ProviderTools、Dapper.Transaction、Rainbow、通用 Repository 或自动 CRUD 扩展；Provider 和事务继续服从 Full.NET 自有抽象。

### 7.3 数据库支持

首版正式支持 SQL Server 和 MySQL 8。PostgreSQL 保留 Provider 接口，在后续里程碑补齐迁移脚本和完整测试矩阵。

分页、标识符、批量操作和迁移 SQL 由小而稳定的 `ISqlDialect` 原语和数据库专用 Statement 处理，不追求一条复杂 SQL 在所有数据库无条件通用。Provider 专用 SQL 必须在同一语义名称下提供 SQL Server/MySQL 成对实现，明确输入、输出、并发与空值语义，并通过双库真实测试；业务 Handler 只选择语义，不得隐藏数据库函数分支。

CTE、窗口函数、Upsert、锁、JSON 路径/聚合、日期函数和排序规则属于受控专有能力。JSON 聚合和局部更新默认放在应用层；SQL Server `MERGE` 不作为默认 Upsert。只有基准或原子性要求成立、两库实现完整且 ADR 获批时才能进入业务 SQL。

多结果集只用于具有共同参数和一致性窗口的聚合读取；必须顺序消费并在统一 Executor 内释放 Reader。动态 SQL 构建不能替代成对 Provider Statement，也不能把任意字符串变成安全 SQL。

### 7.4 迁移

DbUp 读取模块内嵌的、有序、数据库专用 SQL 脚本。`Host.Migrator` 负责执行并写入 Journal。已经发布的迁移脚本不可修改，只能新增向前迁移。

生产环境 API 启动时不自动迁移数据库，迁移必须作为独立部署步骤。API 项目及其发布物不得引用、注册或解析 `Full.NET.Migrations.DbUp`/`IDatabaseMigrationRunner`；`Host.Migrator` 和显式测试基础设施是迁移执行能力的唯一消费者。API 集成测试必须在启动 API 前由测试夹具直接完成迁移，不能以测试便利为由把迁移器注入 API DI。

数据库结构采用 `expand -> migrate/backfill -> contract`。破坏性 DDL、缩窄类型、无保护的大表回填、应用 SQL 的 `SELECT *` 和无 `WHERE` 的 `UPDATE/DELETE` 默认由 CI 拒绝；确需执行时必须有机器可检查的限期豁免、数据验证/备份、前滚或回滚策略和独立数据审查者。Lint 不能替代 SQL Server/MySQL 的半完成迁移与真实集成测试。

## 8. 多租户数据隔离

首版默认使用共享数据库、共享表和 `TenantId` 隔离。租户业务表的 `TenantId` 不允许为空；Host 或公共表显式声明为全局表。

每条 SQL 定义都携带数据作用域元数据：

- `TenantRequired`：必须存在租户上下文，并显式声明 `SqlTenantBinding.CurrentTenantId`；
- `HostOnly`：只能由 Host 管理端执行；
- `Global`：公共数据，或必须跨匿名/Host/租户上下文执行且由 SQL 自身精确收敛的受审查例外。

框架自带及生成的租户 SQL 必须包含：

```sql
WHERE TenantId = @TenantId
  AND IsDeleted = 0
```

执行器只对显式声明 `CurrentTenantId` 的 Statement 注入受信任的当前租户参数；`TenantRequired` 与该绑定必须成对出现且 SQL 必须引用 `@TenantId`，`Global`/`HostOnly` 禁止携带租户绑定。每条生产 `Global` Statement 必须在 `contracts/architecture/global-sql-statements.json` 以 Statement Name、声明成员和源码文件逐项精确登记安全分类、理由与关键 SQL 片段；Architecture Tests 双向拒绝未登记声明、过期或重复目录、通配符和关键行条件漂移。框架不尝试通过 SQL Parser 自动重写任意 SQL；安全性由静态 SQL/生成规范、作用域与绑定声明、Global 精确目录、参数存在性纵深检查、代码审查、双库集成测试和全模块架构门禁共同保证。

独立数据库租户模式后续通过 `IDbConnectionFactory` 选择连接，不改变业务 Handler 契约。

## 9. 事务、Outbox 与一致性

每个 Command 默认使用一个连接和一个本地数据库事务：

```text
打开连接
-> 开始事务
-> 业务写入
-> 写入 Outbox
-> 提交
-> 释放连接
```

Query 默认不启动显式事务。Outbox 记录与业务数据在同一事务提交，由 Worker 按至少一次语义发布。处理器使用事件 ID 或业务幂等键防止重复副作用。

Outbox 默认用 MessagePack 保存二进制载荷，并将消息类型、模式版本和内容类型保存为独立列。Worker 根据 `MessageType + SchemaVersion` 选择唯一处理器；处理器通过统一 `IIntegrationEventSerializer` 反序列化强类型事件。Outbox 处理路径不解析 JSON，也不启用 Typeless 反序列化。

跨模块立即一致操作通过 Contract Service 完成；最终一致操作通过 Integration Event 完成。不使用分布式事务。

### 9.1 事件交付演进基线

1.0 当前只实现事务 Outbox + Worker 轮询。可靠业务 Integration Event 必须与业务数据原子写入 Outbox，按至少一次语义发布，并由消费者以稳定 `EventId` 或业务幂等键去重。不能因吞吐量预估绕过 Outbox 直接写消息中间件。

同进程模块内部事件继续使用类型化 Contract/Dispatcher，不进入外部 Broker。未来事件交付按事件 SLA 静态分类，不根据运行时瞬时 QPS 动态切换：

- **默认可靠业务事件**：事务 Outbox + Worker；
- **高吞吐且仍需事务原子性的业务事件**：只有在轮询瓶颈有基准证据后，才允许评估事务 Outbox + CDC Relay + Kafka；
- **可丢失、可重算且不要求与业务事务原子的遥测流**：可在后期评估直接 Kafka，但不得使用可靠业务事件接口伪装其语义。

CDC Relay、Kafka Producer 与 Consumer 端到端仍按至少一次设计，不宣称 Exactly-Once；稳定 EventId、分区键、Schema 兼容、消费幂等、死信、重放和审计均为强制能力。轮询 Worker 与 CDC Relay 不得同时发布同一事件流；切换时必须有单一 Relay 所有权、排空、回退和可观测性。

Kafka/CDC 属于当前业务与硬化任务之后的 M5+ Decision Gate。进入实现前必须有真实消费者和吞吐/延迟/SLA 数据、Outbox 双库生产闭环、轮询瓶颈基准、SQL Server CDC/MySQL Binlog 运维能力，以及独立 ADR、Provider 规格、许可与成本复核。该演进不构成服务拆分授权，也不改变模块化单体基线。详细复核见[2026-07-22 架构复核](../../verification/architecture-review-2026-07-22.md)。

## 10. 权限模型

### 10.1 RBAC

首版采用 RBAC 角色权限，不引入通用 ABAC 引擎。权限由所属模块定义并使用稳定编码，例如：

```text
identity.users.read
identity.users.create
identity.roles.assign
organization.units.manage
```

用户通过角色取得权限。菜单是权限的界面投影，不是后端授权来源。首版不实现复杂的用户级拒绝规则；临时授权通过专用角色表达。

### 10.2 数据范围

功能权限与数据范围分离。支持：

- 全部；
- 当前组织；
- 当前组织及下级；
- 本人；
- 自定义组织集合。

多个角色的数据范围取并集，但任何角色都不能越过租户边界。Handler 获取规范化 `DataScope` 并将其转换为明确、参数化的 SQL 条件。

### 10.3 管理员

Host 管理员管理平台和租户；Tenant 管理员管理当前租户。Tenant 管理员可以跳过租户内部普通功能权限，但永远不能跳过租户隔离。所有管理员越权操作必须审计。

## 11. 数据库规范

### 11.1 表与主键

模块拥有自己的表，物理名统一为 `{owner_key}_{module_key}_{entity_key}` 的小写 snake_case。Full.NET 官方框架和官方模块的 OwnerKey 固定为 `fn`，具体项目在脚手架创建时冻结独立 OwnerKey（例如 `crm`）；项目扩展官方模块也必须使用项目 OwnerKey。`sys` 保留给数据库系统语义，禁止作为项目 OwnerKey；表名不得由租户或运行时配置动态拼接。

默认主键为应用端生成的 UUID v7，C# 类型为 `Guid`，统一由 `IIdGenerator` 在写库前产生，因此父子记录、审计与 Outbox 可在同一事务中直接引用，不依赖数据库序列。SQL Server 持久化为 `uniqueidentifier`；MySQL 目标类型为 RFC 9562 大端字节序的 `BINARY(16)`，只由 Full.NET 数据层统一转换，业务模块不得感知 `byte[]` 或自行交换字节。HTTP/JSON 始终使用规范 UUID 字符串。现有 MySQL `char(36)` 是尚未完成的 1.0 前存储债务，不能把目标设计表述为已实现。

SQL Server 必须把主键约束与聚集索引分开显式设计：高频追加表优先采用 UUID 非聚集主键和符合时间/租户访问路径的显式聚集索引，不能假定 UUID v7 按 SQL Server `uniqueidentifier` 比较顺序天然追加。项目模板可通过独立 ADR 选择 Snowflake `long`；面向 JavaScript 的 API 必须输出十进制字符串，框架核心表继续采用 UUID v7。完整决策、迁移和验证门禁见 [ADR-0003](../../architecture/adr/ADR-0003-uuid-v7-primary-key-storage.md)。

数据库列使用 PascalCase 并与 C# 持久化投影直接映射，不启用全局 snake_case 隐式映射。表、列、约束、代码、API 和稳定机器码的完整规则及存量兼容边界见 [`Full.NET 命名规范`](../../../rules/naming-conventions.md)。

### 11.2 公共字段

租户业务表按真实能力从下列公共字段中选择，不为模板整齐强迫每张表具备软删除或完整审计：

```text
Id, TenantId,
CreatedAtUtc, CreatedById,
UpdatedAtUtc, UpdatedById,
IsDeleted, DeletedAtUtc, DeletedById,
Version
```

时间统一以 UTC 存储，对外按用户时区转换。`Version` 用于跨数据库乐观并发；更新条件必须同时包含 `Id`、`TenantId`、`Version` 和未删除条件。影响行数为零时返回并发冲突。

### 11.3 类型约定

- 金额使用 `decimal(18,4)`，禁止 `double`；
- 时间在 C# 中使用 `DateTimeOffset`；
- 枚举存整数，不使用数据库原生 Enum；
- 手机、证件号和外部编号使用字符串；
- JSON 只用于低频扩展字段；
- 文件内容进入文件存储，数据库只保存元数据。

## 12. 代码生成体系

### 12.1 流程

```text
数据库元数据或 YAML 模型
-> 统一 FullNetSchema
-> NamingProfile + 生成配置
-> 后端 / SQL / 前端 / 测试模板
```

数据库导入和 YAML 定义最终转换成同一个 `FullNetSchema`，避免两套生成管线。Schema 同时保留逻辑名称与已验证的物理名称；OwnerKey 在项目初始化时冻结。生成器、SQL Lint 和多客户端模板读取同一 Naming Profile，禁止各模板自行实现单复数、snake_case、PascalCase 或长约束截断。

### 12.2 生成内容

生成器可以创建实体、DTO、Command、Query、Handler、Validator、Endpoint、权限、参数化 SQL、分页 SQL、Vue/Layui 页面、TypeScript/JavaScript API 客户端和基础测试，并分阶段扩展 uni-app 与 Dart 客户端。生成的 SQL 自动包含适用的租户、软删除、审计和并发字段规则，并通过同一命名内核生成表、列、约束和稳定协议码。

### 12.3 防覆盖策略

- `Scaffold`：首次创建，目标文件已存在时拒绝覆盖；
- `RefreshGenerated`：只更新 `.g.cs`、`.generated.ts`、`.generated.js` 或其他明确登记的生成文件。

业务扩展放在普通文件或 partial 类型中。生成器不做模糊文本合并，也不使用保护区注释修改人工代码。

### 12.4 使用入口

- CLI：`dotnet fullnet generate`，适合 CI 和可复现生成；
- 管理后台代码生成页面，适合项目快速交付。

两者共用同一个引擎、配置和模板。模板与生成配置进入版本控制。

## 13. 缓存设计

### 13.1 唯一实现与双抽象

FusionCache 是 Full.NET 唯一缓存实现，通过 `.AsHybridCache()` 同时暴露：

- `HybridCache`：业务模块默认依赖的微软标准抽象；
- `IFusionCache`：基础设施和高级缓存场景使用。

两种抽象指向同一个底层 FusionCache 实例。不得同时调用 `AddHybridCache()` 注册微软默认实现，也不开发 Full.NET 自有通用 `ICache` 实现。`IDistributedCache` 只作为 FusionCache 的 L2 存储，不作为业务模块入口。

### 13.2 部署形态

- 单实例：FusionCache + L1 Memory；
- 多实例：FusionCache + L1 Memory + Redis L2 + Redis Backplane。

业务模块一般使用 `HybridCache` 的获取、设置、删除和 Tag 失效能力。只有需要 Fail-Safe、软硬超时、Eager Refresh、Adaptive Caching、Auto-Recovery、事件或高级诊断时才直接使用 `IFusionCache`。

### 13.3 安全缓存策略

全局默认关闭 Fail-Safe。用户会话、用户禁用状态、权限、租户启停状态和 API Key 不允许返回长时间过期数据。字典、普通配置和只读展示投影可以显式启用 Fail-Safe，并必须声明最大陈旧时间。

缓存键格式：

```text
fullnet:{environment}:{tenantId}:{module}:{resource}:{id}:{version}
```

批量失效使用 Tag，不允许通过 Redis `KEYS` 扫描。

### 13.4 提交后失效

```text
业务事务提交
-> Outbox 事件
-> Worker 调用 FusionCache Remove/RemoveByTag
-> 删除 L2
-> Backplane 通知所有节点
-> 各节点清除 L1
```

Outbox 保证失败后重试，Backplane 负责多节点本地缓存同步，两者职责不同。

缓存按一致性分级：权限、用户禁用/安全戳、租户启停/到期、API Key 和 Session 属于安全关键数据，写事务提交后必须同步清除当前进程缓存，再由 Outbox/Backplane 修复其他节点；授权决定不能只依赖可能陈旧的 L1。业务关键配置声明最大陈旧窗口；字典和展示投影才可按上限使用 Fail-Safe 或 Background Refresh。Background Refresh 只优化延迟，不是正确性保证。

## 14. API 与错误模型

API 使用 `/api/v1` 版本前缀和 OpenAPI。成功响应直接返回强类型数据，不包装成永远 HTTP 200 的 `{code,msg,data}`。分页统一返回 `items`、`page`、`pageSize` 和 `total`。

“统一 API”统一的是 HTTP 语义、错误码、ProblemDetails、分页、验证和客户端处理规则，不要求文件、流、SignalR、健康检查及普通 JSON API 使用同一个外层 JSON 结构。应用层统一返回 `Result<T>`、`ResultError` 或 `PagedResult<T>`，Endpoint 负责将其转换为标准 HTTP 响应。

错误响应使用 ProblemDetails，并增加稳定的 `code`、`traceId` 和可选字段错误集合。状态码规则：

| 场景 | HTTP 状态码 |
|---|---:|
| 参数验证失败 | 400 |
| 未认证或令牌失效 | 401 |
| 权限不足 | 403 |
| 数据不存在 | 404 |
| 重复数据或并发冲突 | 409 |
| 业务规则不满足 | 422 |
| 限流 | 429 |
| 未处理异常 | 500 |

前端根据稳定错误码处理逻辑，不匹配中文消息。生产环境不得返回堆栈、SQL、连接字符串和内部类型名。

API 路径使用小写 kebab-case，HTTP JSON 使用 camelCase；权限、错误、消息和 Statement 使用各自的小写点分层规则。稳定值一旦发布必须按兼容契约迁移，不能只为视觉统一直接替换连字符或下划线。

每个 Endpoint 必须显式调用 `RequireAuthorization(...)` 或 `AllowAnonymous()`，禁止依赖路由组或框架默认行为表达安全意图。匿名 Endpoint 必须有契约测试锁定最小返回字段，避免租户、账号、权限或内部标识被后续 DTO 扩展意外公开。

`Full.NET.Compatibility.AdminNet` 提供可选的 Admin.NET 响应适配器，用于旧前端或迁移项目。适配器可以把普通 JSON API 转换为统一外壳，但必须保留真实 HTTP 状态码；不得把未认证、禁止、验证失败、冲突和服务器异常全部伪装成 HTTP 200。文件下载、SSE、SignalR、Webhook、健康检查和 `204 No Content` 不进入响应外壳。默认 Full.NET Host 不启用该适配器。

JSON 统一使用 System.Text.Json 的 Web 默认语义和 UTF-8 输出。每个模块维护自己的 `JsonSerializerContext`，由模块注册入口把生成的 `JsonTypeInfoResolver` 加入 Host；公开热路径 DTO 必须进入源生成上下文。运行时反射只允许用于动态插件或兼容层，不能成为核心 API 的默认路径。`JsonSerializerOptions` 由 Host 单例配置并复用，不得在每次请求中创建。大列表优先采用分页、异步流或 `Utf8JsonWriter`，避免构造巨大中间字符串。

## 15. 安全设计

### 15.1 Token 与会话

- Access Token 使用短有效期；
- Refresh Token 在数据库只保存哈希；
- Refresh Token 每次使用后轮换，并检测旧 Token 重用；
- 修改密码、禁用用户或撤销会话后，旧会话失效；
- JWT 签名密钥支持轮换和 `KeyId`；
- API Key 只保存哈希。

同源管理后台默认将 Access Token 保存在内存，Refresh Token 使用 `HttpOnly + Secure + SameSite` Cookie，并启用 CSRF 防护。移动端和第三方客户端使用独立 Token 交换方式。

### 15.2 租户识别

租户优先通过域名或可信路由识别。普通客户端不能只修改 `TenantId` Header 切换租户。Host 管理员切换租户需要专用权限、重新授权和完整审计。

### 15.3 默认基线

默认启用 HTTPS、CORS 白名单、限流、安全响应头、输入长度限制、文件校验、SQL 参数化、日志脱敏和敏感操作审计。真实密钥和连接字符串不得进入仓库，由环境变量、Secret Store 或 Vault Provider 提供。

## 16. 可观测性与高并发日志

Full.NET 使用 OpenTelemetry 标准关联日志、Trace 和 Metrics，不绑定单一监控平台。业务代码只调用 `ILogger<T>`，高频路径和固定模板使用 `[LoggerMessage]` 源生成；禁止业务模块直接调用 Serilog 静态 API。

默认日志管道为 `ILogger<T> -> Serilog -> 异步有界 Sink -> JSON Console/集中式平台`。文件和网络 Sink 不在请求线程同步写入；队列必须有容量上限、使用率指标和丢弃计数，Debug/Information 在过载时允许按策略丢弃以保护业务吞吐。Error/Critical 使用独立容量、独立指标和本地短期 Spool/可靠 Sink 的高优先级通道；高优先级通道耗尽时进入健康降级和告警，但不默认阻塞请求线程同步写网络。登录、授权、资金、租户、配置和 Agent 工具调用等审计记录不属于普通运行日志，必须通过数据库事务或 Outbox 可靠保存，不能因日志队列满而丢失。

每个 HTTP 请求默认只产生一条汇总访问日志；生产环境不默认记录完整请求体、响应体或高基数对象。日志输出在应用结束时有界刷新，不能无限等待慢 Sink。

结构化日志包含 `TraceId`、`SpanId`、`TenantId`、`UserId`、`Module`、`RequestPath`、`ElapsedMs` 和 `ResultCode`，但不得记录密码、Token、Cookie、完整证件号或银行卡信息。

追踪链路覆盖：

```text
HTTP -> Endpoint -> Command/Query -> Dapper SQL
     -> Outbox -> Worker -> 外部 HTTP 服务
```

指标至少覆盖请求量、耗时、错误率、登录失败、SQL 耗时、慢 SQL、缓存命中率、日志队列深度/容量/丢弃数、任务积压、Outbox 积压、通知成功率和文件上传失败率。

健康端点：

- `/health/live`：进程存活；
- `/health/ready`：数据库、缓存和必要依赖就绪；
- `/health/startup`：迁移和初始化完成。

`ready` 和 `startup` 必须至少注册一个与当前部署拓扑相符的真实检查；空检查集合不得返回可供编排器采用的成功信号。数据库检查必须覆盖当前 Provider，配置 Redis/Backplane 时必须检查 Redis，Worker/Outbox 必须暴露积压或持续失败状态，startup 必须证明所需 Schema 与初始化阶段已经完成。检查使用稳定标签分组，并通过依赖失败集成测试验证 HTTP 状态，而不是只断言服务已注册。

## 17. 实时通信

实时能力分为 `Full.NET.Realtime.Abstractions` 与 `Full.NET.Realtime.SignalR`。业务模块依赖 `IRealtimePublisher`，不得直接依赖 `IHubContext`；Hub 只负责连接、鉴权、分组和传输，不实现业务规则。所有业务通知在数据库事务提交后由 Outbox/Worker 触发，不能在事务提交前直接推送。

服务端使用强类型 `Hub<TClient>`。租户、用户、角色和业务对象采用有命名空间的组名，所有加入组操作重新验证租户和权限。官方 .NET/Vue 客户端优先使用 MessagePack Hub Protocol，普通浏览器和兼容客户端可继续选择 JSON。服务端限制消息大小、连接数、调用速率和流持续时间，并支持取消和断线重连。

单实例使用本机 SignalR；自建多实例使用同机房 Redis Backplane，开发环境可以与 FusionCache 共用 Redis，生产环境至少隔离前缀和连接配置，高负载时使用独立实例。在线状态使用 Redis TTL 或可替换 Presence Store，不保存在某一台 API 的进程内存。

## 18. AI 与 Agentic Web

AI 分层如下：

```text
Full.NET.AI.Abstractions        Microsoft.Extensions.AI、模型/配额/工具策略
Full.NET.AI.Providers.*         OpenAI、Azure OpenAI、Ollama 等供应商适配
Full.NET.Agents                Microsoft Agent Framework 适配与持久化运行时
Full.NET.AgenticWeb.Mcp        MCP Client/Server
Full.NET.AgenticWeb.AgUi       AG-UI Web 协议适配
```

核心 AI 抽象使用 `IChatClient`、`IEmbeddingGenerator` 等供应商中立接口。供应商 SDK、模型密钥和特殊能力只存在于 Provider。Agent Framework 用于单/多 Agent、显式工作流、会话、检查点、长任务和人工审批，但不进入普通业务模块核心依赖。AG-UI Hosting 若仍为预览包，必须封装在可替换适配器中，不作为 Full.NET 1.0 核心稳定 API。

Full.NET 同时支持作为 MCP Server 暴露经过授权的工具、资源和提示，以及作为 MCP Client 消费外部能力。任何 Controller、Service、SQL 或插件都不得因为公开方法存在而自动成为 Agent Tool；工具必须显式注册，逐次执行租户、用户、权限和数据范围校验。写入、删除、付款、发送外部消息等副作用默认要求人工确认或明确的策略豁免。

Agent 运行保存会话、步骤、工具参数摘要、模型、Token、费用、Trace 和审批结果；设置执行时长、工具次数、Token 和费用预算，支持取消、幂等、重试与断点恢复。工具输出视为不可信输入，提示注入不能绕过服务端授权。标准 AG-UI 使用 HTTP + SSE；Full.NET 原生管理端可以另外使用 SignalR 适配，但不能破坏 AG-UI/MCP 标准互操作。

## 19. 客户端

Vue 主管理端放在 `ui/admin`，采用 Vue 3、TypeScript、Vite 和 Element Plus，并以 MIT Art Design Pro 作为管理壳层、主题、布局与通用交互基线。Apache ECharts 是标准图表引擎，使用 `echarts/core` 模块化注册、路由级懒加载和 Full.NET 主题；富文本默认使用 MIT Tiptap Core，由 Vue/Layui 分别建立 Adapter，不采用 Art Design Pro 自带编辑器作为隐式默认，也不引入 Tiptap 付费 Pro 扩展。采用方式是固定上游版本后审计并选择性迁入，不直接用其 Mock、认证、请求、动态路由或后端约定替换 Full.NET 的安全与协议层；导入代码、修改声明和许可证通知必须可追踪。JS/HTML 管理端放在 `ui/admin-layui`，采用 Layui 2、HTML、CSS 和原生 JavaScript。两套管理端覆盖相同后台功能并按同一后端模块同步开发，不要求像素一致，也不得共享框架相关 UI 组件源码。layuiAdmin 可以作为公开页面的布局和交互参考，但其静态主题并非 MIT 资产，未经允许公开源码并以 MIT 再发布的明确书面授权，禁止复制其源码和产品资产。

双管理端首版页面覆盖登录、用户、角色、权限、菜单、组织、租户、配置、字典、审计、文件、通知、任务和代码生成。每个后台功能必须分别记录 Vue 与 Layui 的实现状态，只有两端的权限、流程、错误处理和关键 E2E 都通过后，客户端功能才可标记为 `Verified`。Admin.NET.Pro 的页面可作为交互验收基准，但视觉设计、状态模型和 API 接入应围绕 Full.NET 模块边界重新整理。

H5、微信小程序与支付宝小程序统一放在 `clients/uniapp`，采用 uni-app Vue 3 和官方 uni-ui 作为默认组件库；原版 uView 2 不进入默认依赖，也不允许两套全量 UI 组件库长期并存。原生 Android/iOS 和 Windows/macOS/Linux 桌面端放在 `clients/flutter`，以 Flutter 3.44 的 Material 3、Cupertino 和 Full.NET 设计令牌构建自适应组件层，不绑定第三方整套 UI 框架。Flutter 不再重复承担 H5，uni-app 默认不再重复输出原生 App。.NET MAUI 只在 C#/Windows 企业项目的真实需求命中决策门禁时建立模板，不与 Flutter 长期维护全功能对等实现。

所有客户端通过同一 OpenAPI 契约、标准 HTTP 状态码和 ProblemDetails 与后端解耦，共享权限标识、租户语义和设计令牌，不共享具体 UI 实现。详细 UI 选型见 [`2026-07-18-client-ui-framework-design.md`](2026-07-18-client-ui-framework-design.md)；平台安全策略、测试矩阵和客户端阶段见 [`2026-07-17-multi-client-frontend-strategy-design.md`](2026-07-17-multi-client-frontend-strategy-design.md)。

## 20. 测试策略

### 20.1 单元测试

覆盖纯业务规则，如账号规则、权限合并、数据范围、租户状态、Token 轮换和并发处理；不为简单属性追求形式覆盖率。

### 20.2 模块集成测试

使用 Testcontainers 对 SQL Server 和 MySQL 运行真实数据库测试，验证 SQL、租户条件、Dapper 映射、事务回滚、迁移和 Outbox 原子性。

### 20.3 架构测试

自动禁止模块循环依赖、跨模块内部引用、生产模块之间的 `InternalsVisibleTo`、模块实现项目引用、裸连接、业务层直接调用 Dapper、Endpoint 包含业务逻辑、租户查询绕过执行器、未精确登记的 Global SQL、Service Locator，以及 BuildingBlocks 反向依赖业务模块。架构测试还必须限制 DbUp 迁移组件消费者、验证 Host Profile 的服务集合边界，并拒绝未显式声明认证或匿名意图的 Endpoint。

### 20.4 API、生成器和 E2E

- API 契约测试验证 OpenAPI、状态码、ProblemDetails、权限和兼容性；
- 兼容性测试验证 Admin.NET 响应适配、真实 HTTP 状态码及文件、流、SignalR、健康检查等排除规则；
- 生成器使用 Golden File 测试，并编译生成结果、执行集成测试；
- Playwright 分为快速 Mock 契约层和最小真实栈层。Mock 层覆盖双端一致场景；真实 API、数据库与 Redis 层覆盖 Cookie、精确 CORS、CSRF、登录、并发刷新、租户切换、退出和 ProblemDetails，二者不能互相替代。

### 20.5 性能基线

建立单行查询、分页、批量写入、权限检查、租户解析、Token、System.Text.Json 源生成、MessagePack、gRPC 契约、日志热路径和 Outbox 的可重复 Benchmark。发布门禁比较相对退化，不承诺脱离环境的固定 QPS。序列化基准必须使用 Full.NET 的真实分页、树形权限、租户和事件 DTO，不能只引用第三方项目的微基准结论。

性能变更必须记录场景、数据规模、并发、预热、时长、运行环境、Provider、基线提交、吞吐、错误率、P50/P95/P99 与受影响资源指标。请求链优先减少数据库和网络往返；Dapper 仅按稳定 Statement 名称暴露低基数指标。认证撤销、租户隔离、Audit/Outbox 可靠性和双库兼容是性能优化的硬停止条件，不能用缓存、fire-and-forget 或单库执行计划换取表面吞吐。

轮询 Worker 在取得满批次时应立即继续领取，未满批次才进入 Poll 等待；并发必须有租约、顺序键、作用域和连接池预算。管理端以路由动态导入和依赖按需加载控制首包，发布验证同时记录 minified、gzip 与可用时的 Brotli，并以相对基线退化作为门禁。详细执行规则见 [`rules/performance-engineering.md`](../../../rules/performance-engineering.md)，重复工作流使用项目 Skill `$fullnet-performance-hardening`。

## 21. 运行与部署

四个宿主职责：

- `Host.Api`：HTTP API；
- `Host.Worker`：任务、Outbox、通知；
- `Host.Migrator`：数据库迁移和种子数据；
- `AppHost`：本地 Aspire 编排。

支持三种运行拓扑，均保持 API、Worker、Migrator 的职责边界：

1. 开发编排：AppHost 启动独立 API、Worker、Migrator 进程及其依赖；
2. 标准生产：API、Worker 独立运行，Migrator 作为发布前一次性作业；
3. 多实例生产：多个 API 和 Worker 共享数据库与 Redis，Migrator 仍作为独立发布作业。

禁止为了减少部署单元把迁移、Seed 或可靠后台消费静默放回 API 进程。若某个业务模块需要独立宿主，仍必须先满足第 4.1 节和 ADR-0002 的拆分门禁。

生产发布顺序：

```text
部署新镜像
-> 运行 Migrator
-> 确认迁移成功
-> 启动 API/Worker
-> 健康检查
-> 切换流量
```

Docker 镜像采用多阶段构建、非 root 用户、最小端口、健康检查，并支持只读文件系统；生产密钥不进入镜像。

## 22. 参考项目映射与演进

### 22.1 eShop 架构映射

Full.NET 引入 eShop 的工程和可靠性模式，但不照搬其服务数量：

| eShop 设计 | Full.NET 落地 |
|---|---|
| `eShop.AppHost` | `Full.NET.AppHost`，编排 API、Worker、数据库和 Redis |
| `eShop.ServiceDefaults` | `Full.NET.Hosting/Observability`，统一日志、OpenTelemetry、健康检查和弹性配置；管理型可观测能力后续作为独立模块 |
| 服务独立边界 | 模块边界与公开 `Contracts` |
| 服务拥有自己的数据 | 模块拥有自己的表，禁止跨模块直接访问 |
| Integration Event 与 EventBus | 模块 Integration Event 与可替换 EventBus Provider |
| IntegrationEventLogEF | Dapper Outbox；保留可靠发布思想，不引入 EF 实现 |
| Order/Payment Processor | `Full.NET.Host.Worker` 后台处理模型 |
| Aspire 资源引用与启动顺序 | AppHost 本地资源编排及依赖等待 |
| OpenTelemetry 与健康端点 | Full.NET 统一可观测性和 `/health/*` 端点 |
| 容器和自动化测试 | Docker、模块集成测试、E2E 和部署门禁 |

Full.NET 不默认引入 eShop 的每模块微服务、每服务独立数据库、RabbitMQ 强依赖、EF Integration Event Log 或 API Gateway/BFF。现有 Contracts、Integration Events、Outbox 和 Worker 只提供演进基础，不自动证明模块已经适合拆分；局部服务拆分必须满足第 4.1 节门禁并新增 ADR。

### 22.2 Admin.NET 功能演进

采用旁路重建和逐步替换：

1. Admin.NET.Pro 保持可运行，作为功能和交互验收基线；
2. 先建立 Full.NET 工程底座、数据层、租户、安全和可观测性；
3. 按 Tenancy、Identity、Organization、Permissions 顺序形成第一条完整垂直链路；
4. 管理端逐页面接入新 API；
5. Settings、Auditing、Files、Notifications、Jobs 和 CodeGeneration 后续迁移；
6. 按 `docs/roadmap/adminnet-feature-parity.md` 继续实现支付、微信、MQTT、OCR、工作流、文档、AI 等官方扩展；
7. 每项对标功能必须记录归属、状态、测试、差异和来源；
8. 新框架达到对应阶段验收基线后再冻结旧系统中的同类功能。

对标验收以能力和关键用户流程为准，不以复制 Admin.NET.Pro 的源码、表结构或 Furion/SqlSugar 实现为准。安全性不合理或模块边界混乱的旧行为不做原样兼容。

## 23. 授权与来源治理

Full.NET 根仓库最终使用 MIT License，并维护 `THIRD-PARTY-NOTICES`、依赖许可证清单、来源记录和自动许可证扫描。

“二开和商用授权”不自动等于“允许公开再分发并以 MIT 再许可”。在没有书面条款明确允许公开再分发及 MIT 再许可前：

- Admin.NET.Pro 仅作为功能和实现参考；
- Full.NET 核心代码重新设计、重新实现；
- 不直接复制大段源码、资源和注释；
- 确需复用的代码必须登记来源和授权依据。

MIT 项目可以在遵守原版权和许可证声明的前提下复用。依赖版本由 `Directory.Packages.props` 集中锁定；升级前检查许可证、破坏性变化和安全公告。

## 24. 交付里程碑

### M0：工程基础

独立仓库、解决方案、中央包管理、许可证、CI、代码规范、Host 骨架、System.Text.Json 源生成规范、Serilog 高并发日志和架构测试。

### M1：可运行垂直底座

Dapper、DbUp、SQL Server/MySQL、租户上下文、事务、MessagePack Outbox、ProblemDetails、OpenTelemetry、FusionCache 和最小 API 链路。记录 gRPC 和实时通信边界，但不为未出现的跨进程调用提前引入运行时依赖。

### M2：核心后台能力

Tenancy、Identity、Organization、RBAC、数据范围、菜单、Realtime 抽象、SignalR/MessagePack、Redis Backplane，以及 Vue/Layui 双管理端核心流程。

### M3：快速交付能力

Settings、Auditing、Files、Notifications、Jobs、代码生成、应用模板、CRM 示例、Vue/Layui 双管理端对应页面，以及 uni-app H5/微信/支付宝基础客户端。

### M4：1.0 加固

双数据库测试矩阵、Vue/Layui 双管理端 E2E、uni-app 三目标构建、性能基线、Docker 部署、升级文档、安全审查和 MIT 发布检查。

### M5+：Admin.NET 全量功能对标

按功能矩阵持续交付官方扩展、Provider、Sample 和 Client，包括 Flutter 原生移动/桌面客户端、按需 MAUI 模板、在线构建、导入导出、报表、微信、支付、OAuth、APIJSON、数据库视图、ES 日志、MQTT、AI、Agent、MCP、Agentic Web、审批、钉钉、文档、GoView、K3Cloud、OCR、ReZero、工作流和企业微信等。每个子模块独立完成设计、计划、实现和验收，不阻塞核心 1.0 发布。

每个里程碑都必须保持可构建、可测试、可演示，不允许长期维护一个无法运行的大分支。

## 25. 1.0 验收标准

- 可以创建、迁移和初始化 SQL Server、MySQL 数据库；
- 可以登录、刷新 Token、退出和撤销会话；
- 用户、角色、权限、菜单、组织和租户可完整管理；
- 多租户和数据范围通过自动化隔离测试；
- Dapper 事务、并发控制和 Outbox 可靠；
- FusionCache 单机和 Redis 多实例模式可用；
- 代码生成能生成可编译、可运行的 CRUD 前后端；
- Vue 与 Layui 两套管理端都可以完成核心管理流程，并分别通过权限和 E2E 验收；
- uni-app 可以分别构建 H5、微信小程序和支付宝小程序基础客户端；
- Docker 可以启动完整开发环境；
- 日志、Trace、Metrics 和健康检查可用；
- 对外 JSON 热路径使用 System.Text.Json 源生成，Outbox 使用带版本元数据的 MessagePack 二进制载荷；
- SignalR 实时通道具备租户隔离、MessagePack 客户端和 Redis 多实例验证；
- 架构、集成、生成器和 E2E 测试通过；
- 仓库满足 MIT 和第三方许可证发布要求。

1.0 验收不等于 Admin.NET 全量功能对标完成。长期对标完成标准是功能矩阵中所有适用项达到 `Verified`，或者经过设计评审明确记录为 `Not Applicable` 并给出替代方案。

## 26. 实施原则

后续实施计划必须遵守：

1. 先做可运行的纵向切片，再横向铺开模块；
2. 数据隔离、安全和可观测性从第一条 API 开始验证；
3. SQL 和迁移脚本必须进入评审；
4. 每一项基础抽象至少有一个真实模块消费，避免为假设场景设计；
5. 新依赖必须说明用途、许可证和替代方案；
6. 不以“以后可能微服务化”为理由引入当前不需要的网络边界；
7. 不以快速开发为理由绕过模块、租户、权限、审计和测试规范。
8. Admin.NET 兼容适配器只能放在 Compatibility 层，不能反向影响默认 API 契约。
9. Admin.NET 对标按能力和流程验收，不按源文件数量或代码相似度验收。
10. 核心不因未来可能需要 gRPC、SignalR 或 AI 而创建未被真实模块消费的抽象；首次真实使用时建立独立计划和验收。
11. 开放协议的标准格式优先于内部偏好；MCP、AG-UI 等要求 JSON/SSE 时必须保持协议兼容。
12. API、Worker、Migrator 必须保持运行角色分离；角色分离不等于业务服务拆分，AppHost 不承载业务能力。
13. 局部模块拆分必须先满足第 4.1 节全部门禁并通过独立 ADR；禁止以“未来可能扩容”或“团队可能增长”代替可测量证据。

## 27. 参考资料

- [ASP.NET Core HybridCache](https://learn.microsoft.com/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0)
- [FusionCache Microsoft HybridCache Support](https://github.com/ZiggyCreatures/FusionCache/blob/main/docs/MicrosoftHybridCache.md)
- [Dapper](https://github.com/DapperLib/Dapper)
- [DbUp](https://github.com/DbUp/DbUp)
- [dotnet/eShop](https://github.com/dotnet/eShop)
- [EF Core Performance](https://learn.microsoft.com/ef/core/performance/)
- [System.Text.Json Source Generation](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)
- [gRPC Performance Best Practices](https://learn.microsoft.com/aspnet/core/grpc/performance?view=aspnetcore-10.0)
- [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)
- [High-performance logging in .NET](https://learn.microsoft.com/dotnet/core/extensions/logging/high-performance-logging)
- [Serilog.Sinks.Async](https://github.com/serilog/serilog-sinks-async)
- [SignalR MessagePack Hub Protocol](https://learn.microsoft.com/aspnet/core/signalr/messagepackhubprotocol?view=aspnetcore-10.0)
- [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)
- [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/overview/)
- [MCP C# SDK](https://csharp.sdk.modelcontextprotocol.io/)
- [Agent Framework AG-UI Integration](https://learn.microsoft.com/agent-framework/integrations/ag-ui/)
