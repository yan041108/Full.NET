# Full.NET 总体架构设计规格

- 状态：已批准；后续在既定目标和授权范围内默认采用本文推荐方案推进
- 硬化补充：[`2026-07-18-architecture-hardening-design.md`](2026-07-18-architecture-hardening-design.md)
- 2026-07-22 巡检增补：[`../../verification/architecture-review-2026-07-22.md`](../../verification/architecture-review-2026-07-22.md)
- 架构演进决策：[`ADR-0002：强化型模块化单体与按证据拆分`](../../architecture/adr/ADR-0002-modular-monolith-evolution.md)
- 高并发生产决策：[`ADR-0005：高并发模块化单体多实例生产基线`](../../architecture/adr/ADR-0005-high-concurrency-modular-monolith-multi-instance-production-baseline.md)
- 客户端生成决策：[`ADR-0007：OpenAPI 驱动客户端生成边界`](../../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)
- 高并发评估证据：[`2026-08-01 高并发模块化单体多实例改造评估`](../../verification/high-concurrency-modular-monolith-multi-instance-assessment-2026-08-01.md)
- 当前能力：[`../../roadmap/capability-status.md`](../../roadmap/capability-status.md)
- 日期：2026-07-17
- 高并发多实例封板：2026-08-01
- 模块数据关联与一致性标准修订：2026-08-07
- 架构不足复核与演进门禁修订：2026-08-08
- OpenAPI 驱动客户端生成标准修订：2026-08-21
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
- Vue 3 管理端是后台产品唯一持续交付线；Layui 2 原生 JS/HTML 管理端自 2026-08-02 起作为存量冻结客户端，不再新增功能或参与新能力验收；所有客户端通过 OpenAPI 与后端解耦。
- H5、微信小程序和支付宝小程序采用 uni-app Vue 3；原生移动端和 Windows/macOS/Linux 桌面端默认采用 Flutter，.NET MAUI 仅作为命中决策门禁后的可选模板。
- 外部 REST JSON 统一使用 System.Text.Json 源代码生成；Newtonsoft.Json 只允许作为可选兼容 Provider。
- 同进程模块调用不序列化；跨进程同步调用使用 gRPC + Protobuf；可靠异步事件默认使用 MessagePack，不使用 JSON 载荷。
- 业务代码只依赖 `ILogger<T>`；高频日志使用 `LoggerMessage` 源生成，Serilog 负责异步有界结构化输出。
- SignalR 通过实时通信抽象接入；官方客户端优先 MessagePack Hub Protocol，同时保留 JSON 客户端兼容。
- AI 核心保持模型供应商中立；Agent、MCP、AG-UI 等能力必须位于独立模块或协议适配层。
- 成熟生产参考拓扑采用 Kubernetes + Helm，强化型模块化单体以 API、Worker、Migrator 运行角色多实例部署；应用 Chart 不安装生产数据库、Redis、对象存储和可观测性后端。
- 月度可用性 SLO 为 `99.9%`。单体 `1 万个同时在途动态请求` 是正式容量认证目标，不是开发机功能交付门禁，也不等同于固定 QPS 承诺。

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
8. 以多实例正确性、明确资源预算和过载自保支撑正式环境容量认证；未在专用硬件验证前只声明 `Capacity-not-verified`，不得宣称达到 1 万在途。

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
- 为容量目标提前全面微服务化或引入服务网格；Kafka/CDC 只按 ADR-0006 的可靠事件交付单项授权实施；
- 跨地域双活、数据库读副本或分片；
- 由应用 Helm Chart 承载生产数据库、Redis、对象存储或日志平台；
- 把 `99.99%` 可用性、开发机 1 万在途压测或特定硬件 QPS 作为 Full.NET 1.0 默认承诺。

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

需要立即读取权威结果时使用 Contract Service；允许最终一致时使用 Integration Event。外部或可靠异步事件经 Outbox 发布，消费端必须幂等，不采用分布式事务。Contract Service 只提供调用时刻的权威回答，不自动形成跨模块快照或跨模块事务。

模块内和模块间的数据读取使用以下固定决策顺序：

1. 关联数据全部由当前模块拥有：使用参数化 `JOIN/LEFT JOIN`、批量查询或多结果集，一次性取得用例所需投影；Query 默认不启动显式事务。
2. 数据由另一个模块拥有，且请求必须立即获得权威值：消费方定义最小只读 Port，通过所有者公开 Contract 的同进程适配实现；列表场景必须使用批量接口，禁止逐行远程化风险。
3. 数据由另一个模块拥有，读取频繁或必须隔离所有者故障：所有者发布版本化 Integration Event，消费方在自己的表中维护只包含自身用例所需字段的本地投影。
4. 报表或搜索横跨多个模块：默认由专用读模型/投影承载；不得让报表 SQL 直接 JOIN 多个模块的业务表。临时存量债务只能进入 ADR-0002 指定的精确债务目录。

跨模块引用只持久化稳定业务标识和消费方需要的不可变/版本化快照，不建立跨模块数据库外键。Contract Service 的同步读取可能在返回后立即变旧；若该状态参与写入强不变量，必须把校验与写入收敛到同一数据所有者，而不是依赖“先同步读取、后本地提交”。

领域参数由执行对应业务规则的模块拥有并以强类型模型持久化。通用 Settings 不得以字符串或任意 JSON 代管预约、支付、工作流等领域不变量；跨模块高频消费采用携带完整小型快照和单调 `Version` 的变更事件，消费者拒绝乱序旧版本覆盖。

存量跨模块本地事务按业务风险退役，不以代码位置变化替代一致性设计：提示性校验可以移到事务外并配合失败关闭或对账；事务内频繁需要的外部事实使用消费方本地投影；引用建立与资源删除存在竞态时使用所有者 claim/reserve/release 状态机；跨模块长流程使用 Saga/Process Manager。投影必须具备所有者同事务 Outbox、完整必要快照、单调版本、消费方幂等提交、首次回填、重建、差异对账和双 Provider 验证。详细验收与存量债务目录见 [`ADR-0002`](../../architecture/adr/ADR-0002-modular-monolith-evolution.md#存量债务退役与本地投影验收)。

### 5.4 通信与序列化矩阵

| 场景 | 传输/调用 | 序列化 | 约束 |
|---|---|---|---|
| 对外 REST API | ASP.NET Core HTTP | System.Text.Json 源生成 | 标准 HTTP、ProblemDetails、OpenAPI |
| Admin.NET 兼容 API | HTTP 适配器 | System.Text.Json | 只改变响应形状，不伪造 HTTP 200 |
| 同进程模块 | Contract Service、Command/Query | 无 | 不制造网络边界和序列化开销 |
| 跨进程同步服务 | gRPC | Protobuf | 复用 Channel，统一 Deadline、取消、认证和追踪 |
| 内部可靠异步事件 | Outbox + EventBus Provider | MessagePack；跨语言时可选 Protobuf | 二进制原样存储，消费者幂等，契约显式版本化 |
| 浏览器实时通信 | SignalR | MessagePack 优先，JSON 兼容 | 不是内部 EventBus，不承载业务事务；Host.Api Native AOT 发布与分析构建仅 JSON，见 [`ADR-0008`](../../architecture/adr/ADR-0008-api-native-aot-runtime-boundary.md) |
| 文件和超大二进制 | HTTP 流或对象存储引用 | 原始二进制 | 不放入单个 gRPC/SignalR/Outbox 大消息 |
| MCP、AG-UI 等开放 AI 协议 | 协议规定的 HTTP、SSE、JSON-RPC | 按协议标准 | 这是互操作边界，不受“内部业务消息不用 JSON”限制 |

gRPC 是 RPC 框架，MessagePack 是序列化格式，二者不作为同层替代项。Full.NET 不在 gRPC 中嵌套 MessagePack，也不为了未来可能拆分服务而让当前模块化单体内部走 gRPC。

### 5.5 二进制契约演进与安全

MessagePack 集成事件使用显式 `[MessagePackObject]` 和整数 `[Key(n)]`。字段只能在尾部追加；已发布 Key 不得重排、复用或改变语义，删除字段后保留其编号。禁止 Typeless 和 Contractless Resolver，所有网络、数据库及消息来源均按不可信数据处理，启用 `MessagePackSecurity.UntrustedData` 并使用最新无已知高危漏洞的受支持版本。

每个可靠事件保存 `MessageId`、`MessageType`、`SchemaVersion`、`ContentType`、`TenantId`、`TraceId` 和 `OccurredAt` 等可查询元数据。载荷以 SQL Server `varbinary(max)` 或 MySQL `longblob` 保存，不做 Base64，不依赖人工直接阅读二进制正文。压缩只在基准证明载荷大小收益超过 CPU 成本时按阈值启用。

发布第二个事件版本时必须在兼容窗口内保留并行版本 Handler，或提供基于显式旧版本契约的相邻版本升级链；升级链不能先把旧载荷反序列化为当前 DTO，也不能启用 Typeless/Contractless。发布顺序固定为“先消费者、后生产者、最后退役旧消费者”，兼容窗口覆盖最长 Outbox 保留、失败重试和部署回滚窗口。超过最大尝试次数或确定不可重试的消息必须进入可查询、可审计重放的死信状态，单条毒消息不得永久阻塞批次。

当前基础设施已经具备精确 `MessageType + SchemaVersion` 路由、并行 Handler、consumer-first 发布顺序和旧版本退役扫描；这不自动证明每个业务事件都已完成 v1→v2 升级演练。相邻版本 upgrader 只在首个真实非加法变更出现时实现，且必须使用真实旧契约、旧载荷样本、乱序/重放、最长保留期消息和回滚窗口完成双库验证；禁止为了状态好看创建没有真实消费者的通用升级器。

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

提供登录、操作、异常、API、数据变更和慢 SQL 审计，以及查询、保留、清理和脱敏策略。审计按 B0 Domain Audit、B1 重要 HTTP Operation/Exception Audit 和 B2 普通 HTTP Operation Log/Access/诊断遥测分流；三类记录的可靠性、阻塞和保留语义不得混用。

### 6.6 Files

提供文件元数据、上传、下载、删除、临时文件、权限和本地存储默认实现。S3、MinIO、OSS、COS 等作为独立 Provider；成熟生产参考拓扑使用集群外 S3 兼容对象存储，本地文件 Provider 只用于开发、测试或明确的单机部署。

### 6.7 Notifications

提供站内通知、未读消息、通知模板、SignalR 推送、渠道抽象、发送记录和失败重试。邮件、短信和公众号作为 Provider。

### 6.8 Jobs

提供即时、延迟、周期任务、执行记录、重试、超时、失败处理和分布式锁抽象。默认实现采用数据库任务表、到期时间和带租约的 Worker 轮询，保证崩溃后任务可以重新领取；核心不绑定 Hangfire 或 Quartz.NET，二者可作为 Provider。

### 6.9 CodeGeneration

提供数据库元数据读取、模型定义、后端、SQL、Vue 管理页面、多平台 API 客户端和测试生成，是快速交付的核心能力。`Full.NET.Data.CodeGeneration` 是不依赖 Web 的生成引擎和 CLI 基础；当前已实现嵌入 Naming Profile 的表名、索引/约束确定性摘要和稳定协议校验纯函数，元数据读取、模板渲染、CLI 与 Vue 纵向样例继续推进；既有 Layui 生成产物冻结，不再扩展。`Full.NET.Modules.CodeGeneration` 负责权限、任务记录、模板管理和后台页面 API，不重复实现模板引擎。

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
-> 按准入条件写入 Domain Audit
-> 仅在产生重要业务 Integration Event 时写入 Outbox
-> 提交
-> 释放连接
```

Query 默认不启动显式事务。Outbox 记录与业务数据在同一事务提交，由 Worker 按至少一次语义发布。处理器使用事件 ID 或业务幂等键防止重复副作用。

Outbox 只承载需要与业务事务原子提交、可靠重试和跨模块/跨进程交付的重要业务 Integration Event。缓存失效、日志、Metrics、Trace、普通 HTTP Operation Log 和 Audit 均不使用 Outbox；Domain Audit 若要求“无审计不成功”，必须作为业务事实直接加入同一数据库事务，而不是转换为 Outbox 消息。

Outbox 默认用 MessagePack 保存二进制载荷，并将消息类型、模式版本和内容类型保存为独立列。Worker 根据 `MessageType + SchemaVersion` 选择唯一处理器；处理器通过统一 `IIntegrationEventSerializer` 反序列化强类型事件。Outbox 处理路径不解析 JSON，也不启用 Typeless 反序列化。

跨模块立即一致操作通过 Contract Service 完成；最终一致操作通过 Integration Event 完成。不使用分布式事务。

这里的“立即一致”只表示同步获得所有者在调用时刻作出的业务决定，不表示两个模块自动共享事务。事务边界按下表执行：

| 场景 | 标准事务策略 | 失败与恢复语义 |
| --- | --- | --- |
| 模块内多表写入 | 最外层用例开启一次 `ICommandTransaction`，嵌套数据操作加入同一 `DbSession` | 任一已开始写入后的失败必须以异常触发整体回滚；返回 `Result.Failure` 本身不等于回滚 |
| 模块内一致读取 | Query 默认无事务；只有业务明确要求一致快照时才开启只读本地事务并记录隔离级别 | 不得用默认多次查询结果宣称快照一致 |
| 模块间同步读取后写入 | 先通过只读 Port 获取决策输入，再由数据所有者独立提交；不得把读取结果描述为长期锁定 | 允许状态变化时使用版本/前置条件；必须强一致时重新划分所有权 |
| 模块间多个写入 | 每个模块只写自己的表，发起模块将业务状态与 Outbox 原子提交，下游独立幂等提交 | 至少一次投递、最终一致、死信、重放、补偿和对账 |
| 数据库与文件/Redis/HTTP/gRPC/Broker | 数据库只提交可回滚状态和恢复意图，外部副作用由提交后状态机执行 | 提交结果不确定时保留证据并对账，禁止无条件反向补偿 |

新流程禁止让一个外层 `ICommandTransaction` 跨越多个模块的写服务。当前共享 Scope/连接带来的技术可行性只是部署细节，不是公共契约；未来拆进程时不得要求 `TransactionScope`、DTC 或跨服务两阶段提交。跨模块强不变量应通过合并所有权、命令转交给唯一所有者或显式 Saga/状态机重新建模。

事务内禁止等待外部网络、Broker、Redis、对象存储或文件系统。缓存只在业务提交后清理；重要业务事件只通过同事务 Outbox 发布；实时推送和下游副作用由 Worker 或提交后尽力路径执行，权威恢复仍依赖 Outbox/状态机。

`ICommandTransaction` 当前以异常判断回滚。实现者必须在发生任何写入后对需要整体失败的路径抛出受控异常，不得写入后仅返回失败 `Result` 并假设事务会回滚；前置校验可以在首次写入前返回失败。提交连接中断等结果不确定场景不得立即删除已经完成的外部成果，应进入可重入对账状态。

### 9.1 事件交付演进基线

1.0 当前已实现事务 Outbox + Worker 轮询；项目所有者于 2026-08-08 批准提前建设追加式 Outbox + CDC Relay + Kafka + Inbox，但该批准不等于功能已经实现或允许立即生产切流。可靠业务 Integration Event 必须与业务数据原子写入 Outbox，按至少一次语义发布，并由消费者以稳定 `EventId` 和持久化 Inbox/业务幂等键去重。不能因吞吐量预估绕过 Outbox 直接写消息中间件。

同进程模块内部事件继续使用类型化 Contract/Dispatcher，不进入外部 Broker。未来事件交付按事件 SLA 静态分类，不根据运行时瞬时 QPS 动态切换：

- **尚未迁移的默认可靠业务事件**：事务 Outbox + Worker；
- **已进入批准目录的可靠业务事件**：事务追加式 Outbox + SQL Server CDC/MySQL Binlog + CDC Relay + Kafka + 消费 Inbox；
- **可丢失、可重算且不要求与业务事务原子的遥测流**：可在后期评估直接 Kafka，但不得使用可靠业务事件接口伪装其语义。

CDC Relay、Kafka Producer 与 Consumer 端到端仍按至少一次设计，不宣称 Exactly-Once；稳定 EventId、分区键、Schema 兼容、消费幂等、死信、重放和审计均为强制能力。轮询 Worker 与 CDC Relay 不得同时发布同一事件流；切换时必须有单一 Relay 所有权、排空、回退和可观测性。

Kafka/CDC 已由 [`ADR-0006`](../../architecture/adr/ADR-0006-transactional-outbox-cdc-kafka-event-delivery.md) 批准提前进入分阶段实施：先完成契约、追加式 Outbox、Inbox、Kafka Provider 和双库 CDC Shadow，再以单一发布所有权迁移低风险事件流。生产切流前仍必须具备真实消费者与 SLA、轮询基准、SQL Server CDC/MySQL Binlog 运维能力、故障矩阵、排空、回退、许可与成本复核。该演进不构成服务拆分授权，也不改变模块化单体基线；详细设计以[事件交付 Spec](2026-08-08-transactional-outbox-cdc-kafka-design.md)为准。

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

默认主键为应用端生成的 UUID v7，C# 类型为 `Guid`，统一由 `IIdGenerator` 在写库前产生，因此父子记录、审计与 Outbox 可在同一事务中直接引用，不依赖数据库序列。SQL Server 持久化为 `uniqueidentifier`；MySQL 使用 RFC 9562 大端字节序的 `BINARY(16)`，只由 Full.NET 数据层统一转换，业务模块不得感知 `byte[]` 或自行交换字节。HTTP/JSON 始终使用规范 UUID 字符串。008/009 已完成 MySQL `char(36)` 的 expand→backfill→contract 迁移并具有双库恢复测试；尚未完成的是生产等价环境中的维护窗口、备份恢复和 RPO/RTO 演练，不能把构建验证表述为生产迁移认证。

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

生成器可以创建实体、DTO、Command、Query、Handler、Validator、Endpoint、逐操作权限、参数化 SQL、分页 SQL、Vue 页面、TypeScript API 客户端和基础测试，并分阶段扩展 uni-app 与 Dart 客户端。既有 Layui 页面/JavaScript 产物只保留冻结回归，不接受新能力。生成的 SQL 自动包含适用的租户、软删除、审计和并发字段规则，并通过同一命名内核生成表、列、约束和稳定协议码。

TypeScript API 客户端的最终权威不是数据库元数据或 CRUD 模板，而是 Endpoint 真实运行时产生的标准 OpenAPI。`FullNetSchema` 负责生成服务端纵向切片和稳定 OpenAPI 元数据；客户端统一从规范化 OpenAPI 快照生成低层模型、运行时守卫与 Operation，再由手写薄适配层向 Vue 暴露业务函数。禁止由数据库 Schema 和 OpenAPI 长期维护两套互不校验的路径、DTO、可空性与序列化规则。

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

成熟生产必须暴露两个独立 Redis 连接边界：`Cache/Backplane` 与 `Realtime`。开发环境可以共用同一 Redis；生产默认物理隔离，只有容量、故障域和恢复证据证明安全时才允许同机部署，并仍须保持独立连接、前缀、配额和告警。

### 13.3 安全缓存策略

缓存按一致性与陈旧预算静态分类，不根据瞬时并发切换语义：

| 类别 | 典型数据 | 缓存形态 | 失效与回退 |
| --- | --- | --- | --- |
| C0 权威强一致决策 | 余额扣减、库存占用、关键状态迁移、必须立即生效的精确授权 | 禁用 L1；L2 只能作提示或带版本复核，必要时完全绕过 | 权威源完成决策；不可用时 fail-closed |
| S0-L2 共享即时缓存 | 不能接受节点间 L1 漂移的安全/配置读取 | 禁用 L1，只用 Redis L2 | 直接删除/更新 L2；Redis 失败时读权威源或 fail-closed；TTL/版本限制失败窗口 |
| S1 重要业务缓存 | 订单读模型、权限目录、租户/配置/字典等行为投影 | L1 + L2 + Backplane | 当前实例删除 L1/L2，Backplane 通知其他实例，短 TTL/版本兜底 |
| S2 可降级展示 | 非关键统计、推荐、展示聚合 | L1 + L2，可选 Backplane/后台刷新 | 允许声明上限内的有界陈旧和 Fail-Safe |
| N0 不缓存 | 低频、高敏感、变化快且收益低的数据 | 无 L1/L2 | 直接读取权威源，以查询、索引、限流和连接预算治理 |

全局默认关闭 Fail-Safe。只有 S2 可按条目显式启用并声明最大陈旧时间；C0、S0-L2、S1 不允许以 Fail-Safe、Background Refresh 或陈旧 L1 作为正确性证明。S0-L2 只消除节点间 L1 漂移，不等于数据库与 Redis 强一致；要求数据库提交后任何读取都绝不能看到旧值时，必须选择 C0/N0。

缓存键格式：

```text
fullnet:{environment}:{tenantId}:{module}:{resource}:{id}:{version}
```

批量失效使用 Tag，不允许通过 Redis `KEYS` 扫描。

### 13.4 提交后失效

```text
业务事务提交
-> 当前实例直接 Remove/RemoveByTag，清除 L1 与 Redis L2
-> Redis Backplane 快速通知其他实例清除 L1
-> TTL、缓存版本与权威数据源校验兜底
```

缓存失效禁止使用 Outbox。提交后的本机 L1/L2 删除可以在当前实例同一调用链执行，但不加入已提交业务事务，也不把 Redis 成功伪装成数据库原子性；删除或通知失败必须计量、告警并由短 TTL、版本门禁或权威源读取收敛。重复删除 L1/L2 必须幂等，不得因此主动触发一次数据库回填。

L2 未命中后的回填默认依赖 FusionCache 的单实例合并与合理 TTL；只有“源查询昂贵 + 热点键 + 并发击穿证据”同时成立时才增加带租约和超时的分布式锁。锁获得者双检 L2 后回源并回填，未获得者短暂等待后重读 L2 或按类别回退；禁止给所有缓存键机械加分布式锁。

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

生产 Endpoint 的请求、成功响应、分页项和 ProblemDetails 类型必须由 Architecture 门禁从 Endpoint 元数据枚举并验证进入模块源生成上下文。Vue 主交付线必须优先消费 `packages/client-contracts` 的共享强类型契约；每个 Vue API 模块还必须在覆盖清单中关联对应 OpenAPI 路由/Schema 和共享契约入口，新增调用点缺少任一映射时失败关闭。

项目所有者于 2026-08-21 批准 OpenAPI 驱动客户端生成方向：每个进入生成范围的 Endpoint 必须声明全局唯一、稳定的 `operationId` 和一个稳定主 Tag，OpenAPI 必须完整表达请求、成功响应、ProblemDetails、鉴权、格式、`required`、`nullable`、enum、集合项、Blob/multipart 与 `204` 语义。仓库内规范化标准 OpenAPI 快照是生成输入；现有逐切片轻量夹具继续用于兼容检查，但不得冒充完整 OpenAPI。

生成层只产生低层 TypeScript 模型、运行时守卫、参数编码与 Operation，不替换 `packages/client-contracts` 的 `createHttpClient`。Access Token、并发 Refresh、Cookie、语言协商、ProblemDetails、401 单次重试、Blob、`204` 和取消仍由 Full.NET 共享运行时统一处理。Vue 页面不得直接依赖第三方生成 Class 或模板内部类型，`ui/admin/src/api/*.ts` 保持稳定薄适配层。所有 JSON 响应先以 `unknown` 进入生成守卫，禁止用 `request<T>` 断言冒充运行时校验。完整取舍见 [`ADR-0007`](../../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)。

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

### 15.4 多实例 Data Protection

所有 API 副本必须使用稳定且一致的 `ApplicationName`，共享持久化 Data Protection Key Ring，并对静态 Key 使用 X.509 证书加密。Kubernetes 参考实现采用专用 RWX 持久卷；Key Ring、历史证书及私钥备份必须纳入恢复演练，滚动升级期间保留能够解密现有 Cookie、CSRF Token 和临时令牌的历史材料。禁止把 Data Protection Key Ring 放入可驱逐的缓存 Redis、容器临时文件系统或某个 Pod 的本地卷。

## 16. 可观测性与高并发日志

Full.NET 使用 OpenTelemetry 标准关联 Log、Trace 和 Metrics，不绑定单一查询平台。业务代码只调用 `ILogger<T>`，高频固定模板使用 `[LoggerMessage]` 源生成；禁止业务模块直接调用 Serilog 静态 API、指定物理文件名、数据库表或具体 Sink。

### 16.1 四维分类与逻辑流

每条结构化日志同时具有以下四个维度：

1. `Level`：Trace、Debug、Information、Warning、Error、Critical；
2. `LogClass`：Access、HttpOperation、Diagnostic、Business、System、Security、Audit；
3. `ReliabilityClass`：B0、B1、B2 或普通运行通道；
4. `DataClassification`：Public、Internal、Restricted、Secret。

固定逻辑流为 `access/http-operation`、`diagnostic`、`operational-priority`、`security`、`audit`。逻辑分组由 `LogClass`、`DiagnosticGroup`、`EventId/EventName`、`SourceContext` 等低基数字段表达；平台可据此路由到不同 Loki Stream、OpenSearch Index 或对象存储归档，但程序员不得在业务代码里选择文件或物理分区。

程序员在任意代码行添加的调试诊断日志属于 `Diagnostic`。使用 `ILogger<T>` + 结构化模板，并填写受治理的 `EventId/EventName` 与低基数 `DiagnosticGroup`；统一携带 `TraceId`、`SpanId`、`TenantId`、`Module` 和 `SourceContext`。生产默认关闭 Trace/Debug，通过受保护的管理 API 按命名空间、Endpoint、TraceId、租户或诊断组临时开启；配置与变更 Audit 在同一数据库事务提交，当前实例立即刷新，经 Redis Backplane 快速通知其他实例，并由配置版本与 TTL 兜底，不使用 Outbox。每个开关强制 TTL、操作者、原因、速率/字节上限，禁止无限期全局开启。

### 16.2 普通 HTTP Operation Log

普通 HTTP Operation Log 是 B2 可观测日志，不是 Domain Audit，也不是重要 HTTP Operation Audit。每个进入 Web 应用并完成的请求最多产生一条汇总记录，合并应用 Access 摘要，主要字段包括：

- `TraceId/SpanId`、路由模板、Controller/Action/Endpoint、HTTP Method、规范化 URL/Host/Scheme；
- 状态码、业务结果码、`ElapsedMs`、客户端取消/异常类型；
- 可信代理解析后的客户端 IP、来源 URL（`Referer`，仅在存在且通过清洗时）、协议和受控 User-Agent 摘要；
- 经 Endpoint 白名单和字段投影后的请求/响应摘要。

生产默认 `Enabled=true`、`CaptureMode=Summary`。成功请求使用确定性采样；错误、慢请求和安全事件进入独立 Priority 通道且不参加成功采样，但 Priority 仍是容量有界、可观测丢弃的运行日志，不构成不可丢承诺；要求持久证据的事件必须进入 B1/B0。`SanitizedPayload` 只能由 Endpoint 显式白名单启用，必须限制字段、长度、嵌套深度和集合数量；密码、Token、Cookie、Authorization、签名、完整证件号/银行卡号等 Secret 永不记录，nonce 只允许 HMAC 摘要。禁止复制 Furion Logging Monitor 那种每请求输出完整系统信息、全部 Header、请求体和响应体的大文本块作为生产默认。

框架必须提供六档部署初始模板：`S [0,1K)`、`M [1K,5K)`、`L [5K,10K)`、`XL [10K,50K)`、`XXL [50K,100K)`、`Ultra >=100K`。Profile 只控制成功请求/Trace 的起始采样、Payload 捕获和事件/字节容量预算，不改变 B0/B1/B2 可靠性语义，也不得按瞬时在途数自动抖动切档。面向 1 万在途边界的生产初始参考为 `Enabled=true`、`CaptureMode=Summary`、`CapacityProfile=XL`，该档只是保守起始保护值，最终选择还必须结合经认证的事件/秒、字节/秒和日志后端预算，仍须由目标硬件校准。

### 16.3 Audit 与批量写入

| 类别 | 典型内容 | 保存方式 | 请求失败语义 |
| --- | --- | --- | --- |
| B0 Domain Audit | 权限/超管、租户、资金、订单或其他要求“无审计不成功”的领域变更 | 与业务状态在同一数据库事务直接写入 | fail-closed |
| B1 重要 HTTP Operation/Exception Audit | 重要管理操作、敏感导出/删除、高风险文件、手工重放/修复及异常审计 | 有界跨请求微批直接写审计库；请求等待所属批次写入尝试 | 默认 fail-open + 告警 |
| B2 普通 HTTP Operation/Access/Diagnostic | 普通请求、访问与诊断遥测 | 异步有界日志管道/日志平台，可采样 | 不阻塞业务 |

Audit 不使用 Outbox。B0 与业务事务原子写；B1 批写器必须定义容量、最大批量、最大等待、关闭排空、失败重试上限、降级、指标与告警，使用一次数据库往返写入一批记录，禁止每条记录单独开连接执行。B1 若被要求 fail-closed，必须重新建模为 B0。Outbox 仍只服务重要业务 Integration Event。

首批强制 Audit 目录覆盖：认证与会话；权限、角色和超级管理员；租户；生产配置和动态诊断开关；支付、订单、资金、库存的重要写；敏感导出/删除；高风险文件；手工重放、死信和运维修复。HTTP 请求本身不因存在 Audit 而经过 Outbox。

### 16.4 管道、压力状态与保留

成熟生产参考管道为：

```text
应用 JSON stdout
-> Fluent Bit DaemonSet（磁盘缓冲）
-> Loki（热查询）/ 对象存储（长期归档）

OTLP
-> OpenTelemetry Collector
-> Tempo + Prometheus
-> Grafana
```

OpenSearch、Seq 或其他 APM 可以替换查询后端，应用侧字段和可靠性契约不随平台变化。应用日志管道、优先日志通道和 Audit 批写器都必须有界并暴露队列深度、字节数、丢弃数、批次耗时和失败数。压力状态固定为 `Normal -> Degraded -> Critical -> Recovering`，只允许逐级收缩 B2/Best Effort 的采样和 Payload；不得降级 B0/B1 语义，也不得默认在请求线程同步写网络或磁盘。

默认保留期：Diagnostic 3 天、普通 HTTP Operation/Access 14 天、Warning/Error 运行日志 30 天、Security 90 天、Trace 7 天、重要 HTTP Operation Audit 365 天、Exception Audit 90 天；Domain Audit 按模块法规与业务策略确定。Metrics 热数据默认 30 天，长期数据采用降采样。具体项目可因法规延长，但缩短 Audit 保留期必须经过安全与合规评审。

追踪链路覆盖：

```text
HTTP -> Endpoint -> Command/Query -> Dapper SQL
     -> Outbox -> Worker -> 外部 HTTP 服务
```

指标至少覆盖请求量、在途请求、耗时、错误率、登录失败、SQL 耗时、慢 SQL、数据库连接池等待、缓存 L1/L2 命中与失效通知延迟、日志/Audit 队列深度/容量/丢弃或失败数、任务积压及最老年龄、Outbox 积压、通知成功率和文件上传失败率。

健康端点：

- `/health/live`：进程存活；
- `/health/ready`：数据库、缓存和必要依赖就绪；
- `/health/startup`：迁移和初始化完成。

`ready` 和 `startup` 必须至少注册一个与当前部署拓扑相符的真实检查；空检查集合不得返回可供编排器采用的成功信号。数据库检查必须覆盖当前 Provider，配置 Redis/Backplane 时必须检查 Redis，Worker/Outbox 必须暴露积压或持续失败状态，startup 必须证明所需 Schema 与初始化阶段已经完成。检查使用稳定标签分组，并通过依赖失败集成测试验证 HTTP 状态，而不是只断言服务已注册。

## 17. 实时通信

实时能力分为 `Full.NET.Realtime.Abstractions` 与 `Full.NET.Realtime.SignalR`。业务模块依赖 `IRealtimePublisher`，不得直接依赖 `IHubContext`；Hub 只负责连接、鉴权、分组和传输，不实现业务规则。所有业务通知在数据库事务提交后由 Outbox/Worker 触发，不能在事务提交前直接推送。

服务端使用强类型 `Hub<TClient>`。租户、用户、角色和业务对象采用有命名空间的组名，所有加入组操作重新验证租户和权限。官方 .NET/Vue 客户端优先使用 MessagePack Hub Protocol，普通浏览器和兼容客户端可继续选择 JSON。服务端限制消息大小、连接数、调用速率和流持续时间，并支持取消和断线重连。

单实例使用本机 SignalR；自建多实例使用同机房 Redis Backplane。开发环境可以与 FusionCache 共用 Redis；生产参考拓扑使用独立 `Realtime` Redis，与 `Cache/Backplane` Redis 分离，例外必须有容量和故障域证据。除非客户端被约束为 WebSockets-only 且启用 `SkipNegotiation`，负载均衡入口必须为 SignalR 保持连接亲和；在线状态使用 Redis TTL 或可替换 Presence Store，不保存在某一台 API 的进程内存。

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

Vue 主管理端放在 `ui/admin`，采用 Vue 3、TypeScript、Vite 和 Element Plus，并以 MIT Art Design Pro 作为管理壳层、主题、布局与通用交互基线。Apache ECharts 是标准图表引擎，使用 `echarts/core` 模块化注册、路由级懒加载和 Full.NET 主题；富文本默认使用 MIT Tiptap Core，不采用 Art Design Pro 自带编辑器作为隐式默认，也不引入 Tiptap 付费 Pro 扩展。采用方式是固定上游版本后审计并选择性迁入，不直接用其 Mock、认证、请求、动态路由或后端约定替换 Full.NET 的安全与协议层；导入代码、修改声明和许可证通知必须可追踪。`ui/admin-layui` 保留已实现的 Layui 2、HTML、CSS 和原生 JavaScript 历史成果，但自 2026-08-02 起冻结，不再新增页面、业务操作、适配器、生成模板或对等 E2E。layuiAdmin 仍只可作为公开页面的功能/交互参考；未经允许公开源码并以 MIT 再发布的明确书面授权，禁止复制其源码和产品资产。

后台首版页面覆盖登录、用户、角色、权限、菜单、组织、租户、配置、字典、审计、文件、通知、任务和代码生成。每个后台功能按服务端、共享契约和 Vue 分别记录状态；只有 Vue 的页面/逐操作权限、租户、流程、错误处理、可访问性和关键真实栈 E2E 都通过后，客户端功能才可标记为 `Verified`。角色授权按模块、页面和操作展示同一权威目录，无权限业务按钮不进入 DOM，直接 API 仍由精确 Endpoint 权限失败关闭。Admin.NET.Pro 的页面可作为功能与交互验收基准，但视觉设计、状态模型和 API 接入围绕 Full.NET 模块边界重新实现。

H5、微信小程序与支付宝小程序统一放在 `clients/uniapp`，采用 uni-app Vue 3 和官方 uni-ui 作为默认组件库；原版 uView 2 不进入默认依赖，也不允许两套全量 UI 组件库长期并存。原生 Android/iOS 和 Windows/macOS/Linux 桌面端放在 `clients/flutter`，以 Flutter 3.44 的 Material 3、Cupertino 和 Full.NET 设计令牌构建自适应组件层，不绑定第三方整套 UI 框架。Flutter 不再重复承担 H5，uni-app 默认不再重复输出原生 App。.NET MAUI 只在 C#/Windows 企业项目的真实需求命中决策门禁时建立模板，不与 Flutter 长期维护全功能对等实现。

所有客户端通过同一 OpenAPI 契约、标准 HTTP 状态码和 ProblemDetails 与后端解耦，共享权限标识、租户语义和设计令牌，不共享具体 UI 实现。Vue 的生成客户端只位于 `packages/client-contracts` 的低层生成区，页面通过 `ui/admin/src/api` 薄适配层消费；第三方生成器的 Class、Configuration、Runtime 和命名不得成为页面公共 API。详细 UI 选型见 [`2026-07-18-client-ui-framework-design.md`](2026-07-18-client-ui-framework-design.md)；平台安全策略、测试矩阵和客户端阶段见 [`2026-07-17-multi-client-frontend-strategy-design.md`](2026-07-17-multi-client-frontend-strategy-design.md)。

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
- Playwright 分为快速 Mock 契约层和最小真实栈层。Mock 层覆盖 Vue 组件与协议场景；真实 API、数据库与 Redis 层覆盖 Cookie、精确 CORS、CSRF、登录、并发刷新、租户切换、逐页面/逐操作权限、直接 API 403、退出和 ProblemDetails，二者不能互相替代。Layui 历史场景只作为冻结基线，不参与新增功能退出门槛。

### 20.5 性能基线

建立单行查询、分页、批量写入、权限检查、租户解析、Token、System.Text.Json 源生成、MessagePack、gRPC 契约、日志热路径和 Outbox 的可重复 Benchmark。日常开发只要求按高并发目标完成正确性、资源边界、可观测性和轻量回归验证，不要求在没有目标硬件的开发机达到 2K/5K/10K 在途或固定 QPS。未完成正式容量认证时状态必须为 `Capacity-not-verified`。

性能变更必须记录场景、数据规模、并发、预热、时长、运行环境、Provider、基线提交、吞吐、错误率、P50/P95/P99 与受影响资源指标。请求链优先减少数据库和网络往返；Dapper 仅按稳定 Statement 名称暴露低基数指标。认证撤销、租户隔离、Audit/Outbox 可靠性和双库兼容是性能优化的硬停止条件，不能用缓存、fire-and-forget 或单库执行计划换取表面吞吐。

轮询 Worker 在取得满批次时应立即继续领取，未满批次才进入 Poll 等待；并发必须有租约、顺序键、作用域和连接池预算。管理端以路由动态导入和依赖按需加载控制首包，发布验证同时记录 minified、gzip 与可用时的 Brotli，并以相对基线退化作为门禁。

`1 万个同时在途动态请求` 只在 P4 专用硬件和生产等价 Kubernetes 拓扑认证：依次执行 2K、5K、10K 台阶，包含稳态、长时间 Soak、N+1 副本故障、依赖故障注入和 SQL Server/MySQL 分 Provider 认证，记录吞吐、错误率、P50/P95/P99、在途数、队列、连接池、数据库/Redis/GC/CPU/内存。只有该证据完整时才允许声明 10K 能力；设计同步、多实例正确性、资源治理和适用的 Kubernetes 生产门禁完成后，可以保守流量进入 `Capacity-not-verified`，不得把上线本身当作容量证明。详细执行规则见 [`rules/performance-engineering.md`](../../../rules/performance-engineering.md)，重复工作流使用项目 Skill `$fullnet-performance-hardening`。

## 21. 运行与部署

四个宿主职责：

- `Host.Api`：HTTP API；
- `Host.Worker`：任务、Outbox、通知；
- `Host.Migrator`：数据库迁移和种子数据；
- `AppHost`：本地 Aspire 编排。

支持三种运行拓扑，均保持 API、Worker、Migrator 的职责边界：

1. 开发编排：AppHost 启动独立 API、Worker、Migrator 进程及其依赖；
2. 标准分离：API、Worker 独立运行，Migrator 作为发布前一次性作业；
3. 成熟生产：Kubernetes + Helm 部署多个 API/Worker，Migrator 仍是发布管线控制的一次性 Job。

禁止为了减少部署单元把迁移、Seed 或可靠后台消费静默放回 API 进程。若某个业务模块需要独立宿主，仍必须先满足第 4.1 节和 ADR-0002 的拆分门禁。

### 21.1 Kubernetes 参考基线

- 集群至少 3 个 Worker Node；API 最少 2 副本，配置 Pod Anti-Affinity/Topology Spread、`PodDisruptionBudget minAvailable: 1`、requests/limits、非 root、只读根文件系统、专用 ServiceAccount 和 NetworkPolicy；
- API Deployment 使用 RollingUpdate，`maxUnavailable: 0`、`maxSurge: 1`，并同时实现 startup/readiness/liveness、`preStop`、终止宽限期和停止接收新请求后的排空；
- Worker 独立 Deployment，生产最少 2 副本以提供接管能力，但 Outbox/Jobs 默认 `MaxConcurrency=1`；扩缩容优先看积压深度和最老消息年龄，并受数据库总连接预算约束；
- Migrator 由发布管线以一次性 Job 运行，具有单一执行权、超时和失败阻断；API/Worker 不执行迁移或 Production Seed；
- Ingress/Gateway 负责 TLS、可信代理、全局连接/请求/Body 限制、WAF、外层限流和超时；应用继续保留 Endpoint、本实例和下游资源限制，二者共同构成过载保护；
- HPA 以 CPU、内存、在途请求、队列和延迟等稳定指标扩容，但最大副本数必须先满足 `API 最大副本 × 每实例连接上限 + Worker/Migrator/运维连接 <= 数据库安全连接预算`；达到下游预算后必须排队、限流或快速失败，禁止无限扩 Pod 压垮数据库；
- 应用 Helm Chart 只安装 Full.NET 的 Deployment、Service、Ingress、Config、RBAC 和必要 PVC 引用，不安装生产数据库、Redis、对象存储、Loki、Tempo、Prometheus 或 Grafana。

生产密钥由外部 Secret 管理系统注入。Data Protection 使用专用共享 RWX PVC + X.509；文件进入外部 S3 兼容对象存储；Cache/Backplane Redis 与 Realtime Redis 默认物理分离。所有状态依赖必须有独立高可用、备份、恢复和容量责任人。

### 21.2 发布与回滚

生产发布采用 Expand/Contract 和消费者优先：

```text
构建并签名不可变镜像
-> 配置与依赖预检
-> 执行向后兼容的 Expand 迁移
-> 部署兼容新旧事件/Schema 的 Worker 消费者
-> 滚动部署 API 并观察错误率、P99、连接池和队列
-> 排空旧 Pod，保留可回滚镜像与旧契约
-> 经过兼容窗口后在独立发布执行 Contract 迁移
```

若迁移、健康、错误率、P99、数据库/Redis 饱和或队列最老年龄触发停止条件，必须停止推进并回滚应用；已经执行的 Expand 迁移保持兼容，禁止在同一窗口强行破坏性回滚数据库。Docker 镜像采用多阶段构建、非 root 用户、最小端口和只读文件系统；生产密钥不进入镜像。

### 21.3 可用性与恢复目标

月度可用性 SLO 为 `99.9%`，按“符合准入的业务请求中，在 Endpoint 超时预算内得到非 5xx/非基础设施拒绝结果”的比例计算；客户端认证/权限/验证或明确业务拒绝不计为坏事件，系统过载产生的 429、网关/应用超时和超预算成功均计为坏事件。默认月度错误预算约 43 分 50 秒，计划维护也计入，除非具体项目合同显式另订；必须按多窗口 Burn Rate 告警。参考恢复目标如下；更严格的项目合同必须通过独立容量和灾备设计提高，不得静默改写框架默认：

| 资产 | RPO | RTO | 基线 |
| --- | --- | --- | --- |
| 业务数据库与 Domain Audit | 同城高可用故障 RPO 0；备份恢复不超过 5 分钟 | 30 分钟 | 高可用、PITR/日志备份、双库恢复演练 |
| Data Protection Key Ring 与历史证书 | 0 | 15 分钟 | 共享持久化、加密、备份及解密演练 |
| 对象存储已确认文件 | 0 | 30 分钟 | 版本化/复制或等价耐久策略 |
| Redis Cache/Backplane | 不承诺持久数据 RPO | 15 分钟 | 可重建；故障期间按缓存类别回退 |
| Redis Realtime | 不保存离线业务事实 | 15 分钟 | 连接重建；离线事实由数据库/通知记录承担 |
| 普通日志 | 以 Fluent Bit 磁盘 Spool 容量为丢失预算 | 60 分钟 | 查询后端恢复后续传 |
| Audit 查询库 | 按 B0/B1 数据库策略 | 30 分钟 | 双库备份、恢复与保留验证 |

受控 Production 只有在设计同步、多实例正确性、资源治理、Kubernetes 部署、双库、恢复和回滚门禁通过后，才可以保守流量上线并明确标记 `Capacity-not-verified`；这不授权宣传 10K 容量。正式 10K 声明必须完成第 20.5 节专用环境认证。

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

依赖漏洞检查是合并与发布门禁，不是只读报告：npm 与 NuGet 的 `Critical` 必须阻断，`High` 默认阻断。例外必须精确到 advisory、包、实际依赖路径和受影响范围，记录不可利用或暂缓依据、缓解措施、责任人、复核日期和有限到期日；禁止使用通配符、永久忽略或仅按包名整体放行。扫描命令失败、输出无法解析、数据源不可用且没有新鲜可信缓存时必须失败关闭；只有明确批准的应急发布流程可以临时越过，并形成可审计记录。

## 24. 交付里程碑

### M0：工程基础

独立仓库、解决方案、中央包管理、许可证、CI、代码规范、Host 骨架、System.Text.Json 源生成规范、Serilog 高并发日志和架构测试。

### M1：可运行垂直底座

Dapper、DbUp、SQL Server/MySQL、租户上下文、事务、MessagePack Outbox、ProblemDetails、OpenTelemetry、FusionCache 和最小 API 链路。记录 gRPC 和实时通信边界，但不为未出现的跨进程调用提前引入运行时依赖。

### M2：核心后台能力

Tenancy、Identity、Organization、RBAC、数据范围、菜单、Realtime 抽象、SignalR/MessagePack、Redis Backplane，以及 Vue 管理端核心流程与逐页面/逐操作授权。

### M3：快速交付能力

Settings、Auditing、Files、Notifications、Jobs、代码生成、应用模板、CRM 示例、Vue 管理端对应页面，以及 uni-app H5/微信/支付宝基础客户端。

### M4：1.0 加固

双数据库测试矩阵、Vue 管理端权限与真实栈 E2E、Layui 存量退役治理、uni-app 三目标构建、性能基线、Kubernetes + Helm 成熟生产基线、滚动发布/恢复演练、升级文档、安全审查和 MIT 发布检查。容量认证作为独立 P4 在专用硬件执行；未完成时只允许标记 `Capacity-not-verified`。

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
- Vue 管理端可以完成核心管理流程，页面与业务操作均可独立授权，并通过权限、可访问性和真实栈 E2E 验收；Layui 不再是 1.0 验收条件；
- uni-app 可以分别构建 H5、微信小程序和支付宝小程序基础客户端；
- Docker 可以启动完整开发环境；
- Kubernetes + Helm 可以按 API/Worker/Migrator 角色部署多实例，完成滚动发布、排空、故障接管和受控回滚演练；
- 日志、Trace、Metrics 和健康检查可用；
- 普通 HTTP Operation Log、Diagnostic、B0/B1 Audit 按逻辑分组、脱敏、批写和可靠性契约分流，缓存与日志/Audit 均不借用 Outbox；
- Data Protection Key Ring、对象存储、Cache/Backplane Redis 与 Realtime Redis 满足多实例共享和恢复边界；
- 对外 JSON 热路径使用 System.Text.Json 源生成，Outbox 使用带版本元数据的 MessagePack 二进制载荷；
- SignalR 实时通道具备租户隔离、MessagePack 客户端和 Redis 多实例验证；
- 架构、集成、生成器和 E2E 测试通过；
- 仓库满足 MIT 和第三方许可证发布要求。

上述 1.0 验收不自动证明 1 万同时在途。只有第 20.5 节专用容量环境的 2K/5K/10K、Soak、N+1、故障注入和双 Provider 证据完成后，才能把容量状态从 `Capacity-not-verified` 提升为已认证。

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
14. Outbox 只承载重要业务 Integration Event；缓存失效、日志、Trace、Metrics、普通 HTTP Operation Log 和 Audit 不使用 Outbox。
15. 开发阶段以万级在途为设计目标，但容量声明只能来自专用生产等价环境；没有证据时必须如实标记 `Capacity-not-verified`。

## 27. 参考资料

- [ASP.NET Core HybridCache](https://learn.microsoft.com/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0)
- [FusionCache Microsoft HybridCache Support](https://github.com/ZiggyCreatures/FusionCache/blob/main/docs/MicrosoftHybridCache.md)
- [Dapper](https://github.com/DapperLib/Dapper)
- [DbUp](https://github.com/DbUp/DbUp)
- [dotnet/eShop](https://github.com/dotnet/eShop)
- [EF Core Performance](https://learn.microsoft.com/ef/core/performance/)
- [System.Text.Json Source Generation](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)
- [gRPC Performance Best Practices](https://learn.microsoft.com/aspnet/core/grpc/performance?view=aspnetcore-10.0)
- [ASP.NET Core Data Protection configuration](https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)
- [ASP.NET Core SignalR hosting and scaling](https://learn.microsoft.com/aspnet/core/signalr/scale?view=aspnetcore-10.0)
- [Kubernetes Deployments](https://kubernetes.io/docs/concepts/workloads/controllers/deployment/)
- [Kubernetes Pod Disruption Budgets](https://kubernetes.io/docs/tasks/run-application/configure-pdb/)
- [Grafana Loki storage](https://grafana.com/docs/loki/latest/operations/storage/)
- [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp)
- [High-performance logging in .NET](https://learn.microsoft.com/dotnet/core/extensions/logging/high-performance-logging)
- [Serilog.Sinks.Async](https://github.com/serilog/serilog-sinks-async)
- [SignalR MessagePack Hub Protocol](https://learn.microsoft.com/aspnet/core/signalr/messagepackhubprotocol?view=aspnetcore-10.0)
- [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)
- [Microsoft Agent Framework](https://learn.microsoft.com/agent-framework/overview/)
- [MCP C# SDK](https://csharp.sdk.modelcontextprotocol.io/)
- [Agent Framework AG-UI Integration](https://learn.microsoft.com/agent-framework/integrations/ag-ui/)
