# Full.NET 总体架构设计规格

- 状态：已完成方案确认，等待正式规格复核
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

### 2.2 已确认约束

- 新建独立框架，不在 Admin.NET.Pro 中直接重构。
- Full.NET 目录必须是独立 Git 仓库，不能把父级脏仓库的内容带入提交。
- 后端采用 .NET 10 LTS、ASP.NET Core、C#，开启 Nullable。
- 架构形态为模块化单体；首版不采用微服务。
- 数据访问采用 Dapper-first，不以 EF Core 作为默认运行时依赖。
- 数据迁移采用 DbUp 和可审查的 SQL 脚本。
- 最终公开仓库使用 MIT License，并维护第三方许可清单。
- 管理端继续采用 Vue 3 技术路线，但与后端通过 OpenAPI 和 TypeScript 客户端解耦。

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

首版不实现：

- 微服务拆分和服务网格；
- 分布式事务；
- 自研 ORM 或表达式树转 SQL；
- 通用万能 Repository；
- 工作流、支付、电商、微信、OCR、MQTT、IoT 等具体业务系统；
- 同时完整支持所有关系型数据库；
- 用缓存代替数据库一致性；
- 直接把 Admin.NET.Core 的大合集结构复制到新框架。

上述能力以后只能以独立模块、Provider、模板或示例项目加入，不得污染核心 BuildingBlocks。

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

模块在同一进程中运行，但拥有明确的代码和数据边界。后续如果某个模块确有独立伸缩或隔离需求，可以围绕已有 Contracts 和 Integration Events 拆分，而首版不为未来假设支付微服务复杂度。

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
│   │   ├── Full.NET.Migrations.DbUp
│   │   ├── Full.NET.Caching.Fusion
│   │   ├── Full.NET.Data.CodeGeneration
│   │   └── Full.NET.Observability
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
│   └── Hosts/
│       ├── Full.NET.Host.Api
│       ├── Full.NET.Host.Worker
│       ├── Full.NET.Host.Migrator
│       └── Full.NET.AppHost
├── ui/admin
├── templates/
│   ├── fullnet-app
│   └── fullnet-module
├── samples/Full.NET.Sample.Crm
├── tests/
│   ├── Full.NET.ArchitectureTests
│   ├── Full.NET.IntegrationTests
│   ├── Full.NET.GeneratorTests
│   └── Full.NET.E2E
└── docs/
```

BuildingBlocks 必须保持小而稳定，不允许依赖业务模块。每个业务模块首版保持单程序集，避免为每个模块拆出 Domain/Application/Infrastructure/API 四个项目造成项目数量膨胀。

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

不依赖大范围运行时程序集扫描。模块依赖图必须是有向无环图，由架构测试验证。

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

提供数据库元数据读取、模型定义、后端、SQL、Vue 页面、TypeScript 客户端和测试生成，是快速交付的核心能力。`Full.NET.Data.CodeGeneration` 是不依赖 Web 的生成引擎和 CLI 基础；`Full.NET.Modules.CodeGeneration` 负责权限、任务记录、模板管理和后台页面 API，不重复实现模板引擎。

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
- `ICommandExecutor`；
- `ISqlDialect`；
- `IMigrationRunner`。

业务代码不能直接创建连接，也不能直接调用裸 Dapper 扩展方法。框架执行器统一处理连接、事务、参数、租户上下文、超时、取消、日志、追踪和慢 SQL。

### 7.2 SQL 原则

- 所有用户输入必须参数化；
- 不拼接用户提供的列名、排序或筛选表达式；
- 允许白名单映射后的排序字段；
- 不构建表达式树到 SQL 的隐藏 ORM；
- 不使用覆盖所有实体的通用 Repository；
- 简单 CRUD 由生成器产生，复杂 SQL 存为靠近 Feature 的 `.sql` 文件；
- 原生 SQL 逃生口必须显式声明数据作用域并进入审计。

### 7.3 数据库支持

首版正式支持 SQL Server 和 MySQL 8。PostgreSQL 保留 Provider 接口，在后续里程碑补齐迁移脚本和完整测试矩阵。

分页、标识符、批量操作和迁移 SQL 由 `ISqlDialect` 和数据库专用脚本处理，不追求一条复杂 SQL 在所有数据库无条件通用。

### 7.4 迁移

DbUp 读取模块内嵌的、有序、数据库专用 SQL 脚本。`Host.Migrator` 负责执行并写入 Journal。已经发布的迁移脚本不可修改，只能新增向前迁移。

生产环境 API 启动时不自动迁移数据库，迁移必须作为独立部署步骤。

## 8. 多租户数据隔离

首版默认使用共享数据库、共享表和 `TenantId` 隔离。租户业务表的 `TenantId` 不允许为空；Host 或公共表显式声明为全局表。

每条 SQL 定义都携带数据作用域元数据：

- `TenantRequired`：必须存在租户上下文和 `@TenantId` 参数；
- `HostOnly`：只能由 Host 管理端执行；
- `Global`：公共数据。

框架自带及生成的租户 SQL 必须包含：

```sql
WHERE TenantId = @TenantId
  AND IsDeleted = 0
```

执行器负责注入当前租户参数，并在缺少租户上下文或必要参数时拒绝执行。框架不尝试通过 SQL Parser 自动重写任意 SQL；安全性由生成规范、作用域声明、执行器校验、代码审查和架构测试共同保证。

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

跨模块立即一致操作通过 Contract Service 完成；最终一致操作通过 Integration Event 完成。不使用分布式事务。

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

模块拥有自己的表，使用 `fn_{module}_{entity}` 前缀。默认主键为应用端生成的 UUID v7，C# 类型为 `Guid`，统一由 `IIdGenerator` 产生。项目模板可选择 Snowflake `long`，但框架核心表采用 UUID v7。

### 11.2 公共字段

租户业务表默认包含：

```text
Id, TenantId,
CreatedAt, CreatedBy,
UpdatedAt, UpdatedBy,
IsDeleted, DeletedAt, DeletedBy,
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
-> 生成配置
-> 后端 / SQL / 前端 / 测试模板
```

数据库导入和 YAML 定义最终转换成同一个 `FullNetSchema`，避免两套生成管线。

### 12.2 生成内容

生成器可以创建实体、DTO、Command、Query、Handler、Validator、Endpoint、权限、参数化 SQL、分页 SQL、Vue 页面、TypeScript API 客户端和基础测试。生成的 SQL 自动包含租户、软删除、审计和并发字段规则。

### 12.3 防覆盖策略

- `Scaffold`：首次创建，目标文件已存在时拒绝覆盖；
- `RefreshGenerated`：只更新 `.g.cs` 或 `.generated.ts` 文件。

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

## 14. API 与错误模型

API 使用 `/api/v1` 版本前缀和 OpenAPI。成功响应直接返回强类型数据，不包装成永远 HTTP 200 的 `{code,msg,data}`。分页统一返回 `items`、`page`、`pageSize` 和 `total`。

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

## 16. 可观测性

Full.NET 使用 OpenTelemetry 标准输出日志、Trace 和 Metrics，不绑定单一监控平台。

结构化日志包含 `TraceId`、`SpanId`、`TenantId`、`UserId`、`Module`、`RequestPath`、`ElapsedMs` 和 `ResultCode`，但不得记录密码、Token、Cookie、完整证件号或银行卡信息。

追踪链路覆盖：

```text
HTTP -> Endpoint -> Command/Query -> Dapper SQL
     -> Outbox -> Worker -> 外部 HTTP 服务
```

指标至少覆盖请求量、耗时、错误率、登录失败、SQL 耗时、慢 SQL、缓存命中率、任务积压、Outbox 积压、通知成功率和文件上传失败率。

健康端点：

- `/health/live`：进程存活；
- `/health/ready`：数据库、缓存和必要依赖就绪；
- `/health/startup`：迁移和初始化完成。

## 17. 管理端

管理端放在 `ui/admin`，采用 Vue 3、TypeScript、Vite 和 Element Plus。前后端通过 OpenAPI 和生成的 TypeScript 客户端解耦。

首版页面覆盖登录、用户、角色、权限、菜单、组织、租户、配置、字典、审计、文件、通知、任务和代码生成。Admin.NET.Pro 的页面可作为交互验收基准，但视觉设计、状态模型和 API 接入应围绕 Full.NET 模块边界重新整理。

管理端详细设计系统、组件规范和页面信息架构另立 UI 规格，不在本总体架构文档中展开。

## 18. 测试策略

### 18.1 单元测试

覆盖纯业务规则，如账号规则、权限合并、数据范围、租户状态、Token 轮换和并发处理；不为简单属性追求形式覆盖率。

### 18.2 模块集成测试

使用 Testcontainers 对 SQL Server 和 MySQL 运行真实数据库测试，验证 SQL、租户条件、Dapper 映射、事务回滚、迁移和 Outbox 原子性。

### 18.3 架构测试

自动禁止模块循环依赖、跨模块内部引用、裸连接、业务层直接调用 Dapper、Endpoint 包含业务逻辑、租户查询绕过执行器、Service Locator，以及 BuildingBlocks 反向依赖业务模块。

### 18.4 API、生成器和 E2E

- API 契约测试验证 OpenAPI、状态码、ProblemDetails、权限和兼容性；
- 生成器使用 Golden File 测试，并编译生成结果、执行集成测试；
- Playwright 覆盖登录、Token 刷新、权限、多租户、代码生成、文件和通知关键流程。

### 18.5 性能基线

建立单行查询、分页、批量写入、权限检查、租户解析、Token、序列化和 Outbox 的可重复 Benchmark。发布门禁比较相对退化，不承诺脱离环境的固定 QPS。

## 19. 运行与部署

四个宿主职责：

- `Host.Api`：HTTP API；
- `Host.Worker`：任务、Outbox、通知；
- `Host.Migrator`：数据库迁移和种子数据；
- `AppHost`：本地 Aspire 编排。

支持三种部署：

1. 简单部署：API 内运行轻量后台任务；
2. 标准部署：API、Worker、Migrator 分离；
3. 多实例部署：多个 API 和 Worker，共享数据库与 Redis。

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

## 20. 从参考项目演进

采用旁路重建和逐步替换：

1. Admin.NET.Pro 保持可运行，作为功能和交互验收基线；
2. 先建立 Full.NET 工程底座、数据层、租户、安全和可观测性；
3. 按 Tenancy、Identity、Organization、Permissions 顺序形成第一条完整垂直链路；
4. 管理端逐页面接入新 API；
5. Settings、Auditing、Files、Notifications、Jobs 和 CodeGeneration 后续迁移；
6. 支付、微信、MQTT、OCR 等只作为可选扩展，不迁入核心；
7. 新框架达到验收基线后再冻结旧系统。

eShop 主要用于参考宿主拆分、Aspire、本地编排、容器化和测试组织，不复制其微服务数量。

## 21. 授权与来源治理

Full.NET 根仓库最终使用 MIT License，并维护 `THIRD-PARTY-NOTICES`、依赖许可证清单、来源记录和自动许可证扫描。

“二开和商用授权”不自动等于“允许公开再分发并以 MIT 再许可”。在没有书面条款明确允许公开再分发及 MIT 再许可前：

- Admin.NET.Pro 仅作为功能和实现参考；
- Full.NET 核心代码重新设计、重新实现；
- 不直接复制大段源码、资源和注释；
- 确需复用的代码必须登记来源和授权依据。

MIT 项目可以在遵守原版权和许可证声明的前提下复用。依赖版本由 `Directory.Packages.props` 集中锁定；升级前检查许可证、破坏性变化和安全公告。

## 22. 交付里程碑

### M0：工程基础

独立仓库、解决方案、中央包管理、许可证、CI、代码规范、Host 骨架和架构测试。

### M1：可运行垂直底座

Dapper、DbUp、SQL Server/MySQL、租户上下文、事务、Outbox、ProblemDetails、OpenTelemetry、FusionCache 和最小 API 链路。

### M2：核心后台能力

Tenancy、Identity、Organization、RBAC、数据范围、菜单和 Vue 管理端核心流程。

### M3：快速交付能力

Settings、Auditing、Files、Notifications、Jobs、代码生成、应用模板和 CRM 示例。

### M4：1.0 加固

双数据库测试矩阵、E2E、性能基线、Docker 部署、升级文档、安全审查和 MIT 发布检查。

每个里程碑都必须保持可构建、可测试、可演示，不允许长期维护一个无法运行的大分支。

## 23. 1.0 验收标准

- 可以创建、迁移和初始化 SQL Server、MySQL 数据库；
- 可以登录、刷新 Token、退出和撤销会话；
- 用户、角色、权限、菜单、组织和租户可完整管理；
- 多租户和数据范围通过自动化隔离测试；
- Dapper 事务、并发控制和 Outbox 可靠；
- FusionCache 单机和 Redis 多实例模式可用；
- 代码生成能生成可编译、可运行的 CRUD 前后端；
- Vue 管理端可以完成核心管理流程；
- Docker 可以启动完整开发环境；
- 日志、Trace、Metrics 和健康检查可用；
- 架构、集成、生成器和 E2E 测试通过；
- 仓库满足 MIT 和第三方许可证发布要求。

## 24. 实施原则

后续实施计划必须遵守：

1. 先做可运行的纵向切片，再横向铺开模块；
2. 数据隔离、安全和可观测性从第一条 API 开始验证；
3. SQL 和迁移脚本必须进入评审；
4. 每一项基础抽象至少有一个真实模块消费，避免为假设场景设计；
5. 新依赖必须说明用途、许可证和替代方案；
6. 不以“以后可能微服务化”为理由引入当前不需要的网络边界；
7. 不以快速开发为理由绕过模块、租户、权限、审计和测试规范。

## 25. 参考资料

- [ASP.NET Core HybridCache](https://learn.microsoft.com/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0)
- [FusionCache Microsoft HybridCache Support](https://github.com/ZiggyCreatures/FusionCache/blob/main/docs/MicrosoftHybridCache.md)
- [Dapper](https://github.com/DapperLib/Dapper)
- [DbUp](https://github.com/DbUp/DbUp)
- [dotnet/eShop](https://github.com/dotnet/eShop)
- [EF Core Performance](https://learn.microsoft.com/ef/core/performance/)
