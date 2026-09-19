# 架构总览

## 1. 架构形态

Full.NET 1.0 采用 **强化型模块化单体 (Reinforced Modular Monolith)** 架构：

```text
┌──────────────────────────────────────────────────────────────────┐
│                        客户端 (Clients)                          │
│  Vue Admin  │  Layui Admin  │  uni-app  │  Flutter  │  OpenAPI  │
└──────────────────────────┬───────────────────────────────────────┘
                           │ HTTP / WebSocket / gRPC
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                      Host + 横切管道 (Pipelines)                 │
│  ┌─────────┐ ┌────────┐ ┌────────┐ ┌──────────┐ ┌────────────┐ │
│  │  CORS   │ │  限流  │ │ 认证   │ │  授权    │ │  审计日志  │ │
│  └─────────┘ └────────┘ └────────┘ └──────────┘ └────────────┘ │
│  ┌─────────┐ ┌────────┐ ┌────────┐ ┌──────────┐ ┌────────────┐ │
│  │ 转发代理 │ │DataProt│ │OpenAPI │ │ 本地化   │ │  诊断策略  │ │
│  └─────────┘ └────────┘ └────────┘ └──────────┘ └────────────┘ │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                    Composition (组合根 + 模块目录)                │
│  FullNetModuleCatalog → FullNetModuleSelection (Preset/Enabled)   │
│  → Host Profile (Api / Worker / Migrator / Test)                 │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                      业务模块 (Business Modules)                  │
│  ┌────────┐ ┌─────────┐ ┌────────────┐ ┌──────┐ ┌────────────┐ │
│  │Identity│ │ Tenancy │ │Organization│ │Files │ │  Auditing  │ │
│  └────────┘ └─────────┘ └────────────┘ └──────┘ └────────────┘ │
│  ┌────────┐ ┌─────────┐ ┌────────────┐ ┌──────┐ ┌────────────┐ │
│  │Settings│ │  Jobs   │ │  Document  │ │Messag│ │  CodeGen   │ │
│  └────────┘ └─────────┘ └────────────┘ └──────┘ └────────────┘ │
│  ┌────────┐ ┌─────────┐ ┌────────────┐ ┌──────┐ ┌────────────┐ │
│  │  Ai    │ │Workflow │ │ DataApprov │ │Webhks│ │  Payments  │ │
│  └────────┘ └─────────┘ └────────────┘ └──────┘ └────────────┘ │
│  ┌────────┐ ┌─────────┐ ┌────────────┐ ┌──────┐ ┌────────────┐ │
│  │Crypto  │ │  Mqtt   │ │  Regions   │ │Import│ │  Reporting │ │
│  └────────┘ └─────────┘ └────────────┘ └──────┘ └────────────┘ │
│  ┌────────┐ ┌─────────┐ ┌────────────┐ ┌──────┐ ┌────────────┐ │
│  │ Serial │ │ Calendar│ │  Platform  │ │Observ│ │ Enterprise │ │
│  └────────┘ └─────────┘ └────────────┘ └──────┘ └────────────┘ │
│  ┌────────┐ ┌─────────┐ ┌────────────┐ ┌──────┐ ┌────────────┐ │
│  │  Ocr   │ │ K3Cloud │ │   GoView   │ │Print  │ │ Enterprise │ │
│  └────────┘ └─────────┘ └────────────┘ └──────┘ └────────────┘ │
└──────────────────────────┬───────────────────────────────────────┘
                           │ 公开 Contracts / 事务 Outbox
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                  BuildingBlocks (基础设施抽象 + 实现)              │
│  ┌──────────────┐ ┌──────────────────┐ ┌──────────────────────┐ │
│  │ Abstractions │ │ Modularity(CQRS) │ │ Data(Dapper+DbUp)    │ │
│  └──────────────┘ └──────────────────┘ └──────────────────────┘ │
│  ┌──────────────┐ ┌──────────────────┐ ┌──────────────────────┐ │
│  │ Caching.Fusion│ │  Messaging(Kafka)│ │  Realtime(SignalR)   │ │
│  └──────────────┘ └──────────────────┘ └──────────────────────┘ │
│  ┌──────────────┐ ┌──────────────────┐ ┌──────────────────────┐ │
│  │ Localization │ │ Validation(Fluent)│ │  Hosting(横切)         │ │
│  └──────────────┘ └──────────────────┘ └──────────────────────┘ │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                     外部依赖 (External Systems)                   │
│     SQL Server / MySQL    │    Redis    │    S3 / MinIO         │
│         Kafka / CDC       │  OpenTelemetry Collector             │
└──────────────────────────────────────────────────────────────────┘
```

### 核心原则 (ADR-0002)

1. **模块化而非微服务化**：Full.NET 1.0 不进行全面微服务化，局部模块只有满足 6 项拆分门禁后才能独立部署
2. **明确的模块边界**：模块内部实现默认 `internal`，跨模块只依赖公开 Contracts；禁止直接访问其他模块内部表
3. **单一主项目原则**：每个业务模块默认只创建一个主项目；Contracts/Http/Worker 适配项目只能按真实消费者和隔离收益选择性增加
4. **运行角色分离**：API、Worker、Migrator 三种宿主按职责分离，但不等于业务服务拆分
5. **依赖方向**：Host/Module → BuildingBlocks（抽象与实现），抽象层不得反向依赖业务模块

---

## 2. 解决方案结构

```text
src/
├── BuildingBlocks/                          # 基础设施层（无反向依赖）
│   ├── Full.NET.Abstractions                # 核心抽象：Result、ICommand、Tenancy、Clock、Ids、Auditing
│   ├── Full.NET.Modularity                  # 模块系统 + CQRS 分发器 + 集成事件消费调度
│   ├── Full.NET.Data.Abstractions           # 数据访问抽象：Executor、Outbox/Inbox、SQL Scope、Retention
│   ├── Full.NET.Data.Dapper                 # Dapper 实现：事务、Scope Guard、Outbox/Inbox、AOT
│   ├── Full.NET.Data.MySql                  # MySQL 特有策略
│   ├── Full.NET.Data.CodeGeneration         # 代码生成命名/Schema/Generation/Integration 内核
│   ├── Full.NET.Migrations.DbUp             # DbUp 迁移引擎封装
│   ├── Full.NET.Seeding.Abstractions        # 种子数据贡献者抽象
│   ├── Full.NET.Seeding.Dapper              # Dapper 种子编排器
│   ├── Full.NET.Caching.Abstractions        # 缓存失效边界抽象：ICacheInvalidator
│   ├── Full.NET.Caching.Fusion              # FusionCache 混合缓存实现 + HybridCache 双抽象
│   ├── Full.NET.Messaging.Abstractions      # 消息抽象：事件流所有权、订阅目录、Kafka 重放
│   ├── Full.NET.Messaging.Kafka             # Kafka Consumer/Producer、DLQ、Offset 管理、Connect Admin
│   ├── Full.NET.Realtime.Abstractions       # 实时通信抽象
│   ├── Full.NET.Realtime.SignalR            # SignalR 实现 + Redis Backplane + 探针端点
│   ├── Full.NET.Serialization.MemoryPack    # MemoryPack 序列化（可靠事件载荷契约）
│   ├── Full.NET.Validation.FluentValidation # FluentValidation 集成 + 自动扫描 Behavior
│   ├── Full.NET.Localization                # 全栈多语言基础设施
│   └── Full.NET.Hosting                     # 宿主横切：异常处理、日志、限流、OpenAPI、转发、DataProtection
│
├── Modules/                                 # 业务模块层（数据所有权边界）
│   ├── Full.NET.Modules.Identity            # 身份认证：用户、角色、菜单、授权、会话、OIDC
│   ├── Full.NET.Modules.Identity.Contracts
│   ├── Full.NET.Modules.Tenancy             # 多租户：租户、租户包、解析、切换
│   ├── Full.NET.Modules.Organization        # 组织架构：部门、岗位、职级
│   ├── Full.NET.Modules.Organization.Contracts
│   ├── Full.NET.Modules.Settings            # 平台配置：参数、字典、枚举、网格偏好、诊断策略
│   ├── Full.NET.Modules.Settings.Contracts
│   ├── Full.NET.Modules.Auditing            # 审计：操作/访问/异常/出站日志、保留策略
│   ├── Full.NET.Modules.Files               # 文件管理：上传、下载、Blob 引用、对账
│   ├── Full.NET.Modules.Files.Contracts
│   ├── Full.NET.Modules.Document            # 文档中心：分类、标签、分享、回收站、预览任务
│   ├── Full.NET.Modules.Notifications       # 通知：站内消息、SignalR 推送
│   ├── Full.NET.Modules.Notifications.Contracts
│   ├── Full.NET.Modules.Calendar            # 日历：日程、提醒、会议预订
│   ├── Full.NET.Modules.Platform            # 平台公告、横幅、全局开关
│   ├── Full.NET.Modules.Regions             # 行政区划与地区字典
│   ├── Full.NET.Modules.Jobs                # 任务调度：定义、调度、执行记录、心跳
│   ├── Full.NET.Modules.Messaging           # 消息运维：事件流管理、重放、死信、退役
│   ├── Full.NET.Modules.ImportExport        # 导入导出：任务编排、模板、运行器
│   ├── Full.NET.Modules.ImportExport.Contracts
│   ├── Full.NET.Modules.Reporting            # 报表：导出任务、模板渲染
│   ├── Full.NET.Modules.Reporting.Contracts
│   ├── Full.NET.Modules.Printing            # 打印：模板、作业队列
│   ├── Full.NET.Modules.Printing.Contracts
│   ├── Full.NET.Modules.Ai                  # AI：模型客户端、Agent、聊天、工具
│   ├── Full.NET.Modules.Ai.Contracts
│   ├── Full.NET.Modules.Payments            # 支付：渠道、订单、对账
│   ├── Full.NET.Modules.Payments.Contracts
│   ├── Full.NET.Modules.GoView              # 数据可视化看板
│   ├── Full.NET.Modules.GoView.Contracts
│   ├── Full.NET.Modules.K3Cloud             # 金蝶 K3 Cloud 集成
│   ├── Full.NET.Modules.K3Cloud.Contracts
│   ├── Full.NET.Modules.Ocr                 # OCR 识别：任务、模板
│   ├── Full.NET.Modules.Ocr.Contracts
│   ├── Full.NET.Modules.CodeGeneration      # 代码生成：模板、预览、运行、回滚、保留
│   ├── Full.NET.Modules.SerialNumbers       # 流水号：规则引擎、并发生成
│   ├── Full.NET.Modules.SerialNumbers.Contracts
│   ├── Full.NET.Modules.DataApproval        # 数据审批：流程、申请、恢复
│   ├── Full.NET.Modules.DataApproval.Contracts
│   ├── Full.NET.Modules.ObservabilityAdmin  # 可观测性管理：诊断策略、日志降级
│   ├── Full.NET.Modules.Workflow            # 工作流：定义、执行、待办超时
│   ├── Full.NET.Modules.Workflow.Contracts
│   ├── Full.NET.Modules.Mqtt                # MQTT：设备消息接入
│   ├── Full.NET.Modules.Webhooks            # Webhook：出站事件投递
│   ├── Full.NET.Modules.Cryptography        # 加密：对称/非对称、密钥管理
│   └── Full.NET.Modules.EnterpriseRequest   # 企业请求样板：跨模块审批样例
│
├── AI/                                       # AI Provider 与 Agent 抽象（业务无关）
│   ├── Full.NET.AI.Abstractions              # AI 抽象：凭据、连接探针、模型客户端
│   ├── Full.NET.AI.Providers.Http            # HTTP 通用 AI 网关
│   ├── Full.NET.AI.Providers.OpenAI          # OpenAI 兼容客户端
│   ├── Full.NET.AI.Providers.AzureOpenAI    # Azure OpenAI 客户端
│   ├── Full.NET.AI.Providers.Ollama         # Ollama 本地模型客户端
│   ├── Full.NET.Agents                      # Agent 运行时抽象
│   ├── Full.NET.AgenticWeb.Mcp              # MCP 协议适配
│   └── Full.NET.AgenticWeb.AgUi             # AG-UI 协议适配
│
├── Composition/
│   └── Full.NET.Composition                 # 组合根：模块目录 + Host Profile + 启用集解析
│
├── Compatibility/
│   └── Full.NET.Compatibility.AdminNet      # Admin.NET 统一响应信封适配 + Pre-v1 兼容
│
├── Generators/
│   └── Full.NET.Messaging.Generators        # 集成事件订阅路由源代码生成器
│
├── Hosts/
│   ├── Full.NET.Host.Api                    # API 宿主（HTTP Endpoint + 实时通信）
│   ├── Full.NET.Host.Worker                 # Worker 宿主（Outbox/Retention/Shadow/Kafka 后台处理）
│   ├── Full.NET.Host.Migrator               # Migrator 宿主（数据库迁移 + 种子数据）
│   └── Full.NET.AppHost                     # .NET Aspire 本地编排宿主
│
└── Tools/
    ├── Full.NET.CodeGeneration.Cli          # 代码生成 CLI
    └── Full.NET.Messaging.Cli               # 消息运维 CLI
```

---

## 3. 模块通信规则

### 3.1 模块内通信

- 使用 `ICommand<TResult>` / `IQuery<TResult>` + `ICommandDispatcher` / `IQueryDispatcher`
- 同进程直接调用，不序列化
- 模块内 SQL 可直接 JOIN 本模块拥有的 `fn_<module>_*` 表

### 3.2 跨模块同步读取

| 模式 | 适用场景 | 实现方式 |
|------|----------|----------|
| 最小只读 Port | 请求当下必须获得权威答案，频次低 | 消费方 A 在自身 Contracts 定义最小 Port，B 实现适配 |
| 版本化本地投影 | 高频读取、列表筛选搜索、事务内需要 | 所有者发布 Integration Event → 消费方维护本地投影表 |

**禁止事项**：
- ❌ 直接 SQL/JOIN/视图/同义词/存储过程读取其他模块表
- ❌ 跨模块数据库外键
- ❌ 共享 DbSession / 跨模块本地事务

### 3.3 跨模块写入

- **唯一数据所有者原则**：强不变量收敛到唯一所有者模块
- **最终一致性推进**：其他模块通过事务 Outbox + 集成事件 + 幂等消费者推进
- **跨模块长流程**：Saga/Process Manager + 各模块本地事务 + Outbox + 补偿

### 3.4 可靠消息交付 (ADR-0006)

```text
业务事务 ──同一 DB 事务──► IOutboxWriter.AddAsync
                              │   (事务内原子追加：fn_messaging_outbox_event)
                              ▼
              ┌─────────────────────────────────────────┐
              │  IEventStreamOwnershipGate 事务级互斥     │
              │  ├── Producer 锁：阻止切流期 Legacy 写入  │
              │  ├── Consumer Fence：消费端读 Owner 快照  │
              │  └── OwnershipChange：切流独占写锁       │
              └─────────────────────────────────────────┘
                              │
                              ▼
              ┌─────────────────────────────────────────┐
              │  EventDeliveryOwner 三种交付所有权        │
              │  ├── LegacyPolling: Worker 轮询 Outbox   │
              │  ├── ShadowCdcKafka: 影子比对验证         │
              │  └── HybridKafka: CDC Relay → Kafka     │
              │      (CdcKafka 为过时别名，规范化保留一版) │
              └─────────────────────────────────────────┘
                              │
                              ▼
                        Kafka Broker
                              │
                              ▼
                    IIntegrationEventSubscriptionCatalog
                    ├── 路由键 (ConsumerName, EventType, SchemaVersion)
                    ├── IIntegrationEventHandler (编译期生成)
                    └── Inbox Precheck/Claim/MarkProcessed（DB 提交后 Offset）
```

---

## 4. 数据访问架构

### 4.1 Dapper-First 原则

权威 SQL 三入口说明见 [`dapper-sql-sources.md`](dapper-sql-sources.md)。

- **禁止引入 EF Core** 作为业务数据访问路径
- **所有 SQL 参数化**；表名/排序片段来自封闭白名单
- **禁止 SELECT \***；禁止无 WHERE 的 UPDATE/DELETE

### 4.2 SQL 作用域守卫

`SqlStatement` 不可变 record 携带 `Name / Text / Scope / TenantBinding` 元数据，由 [`SqlScopeGuard`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Dapper/SqlScopeGuard.cs) 在 `DapperSqlExecutor` 构造 CommandDefinition 前同步强制校验：

```text
SqlDataScope + SqlTenantBinding 合法矩阵:
├── TenantRequired + CurrentTenantId  // 必须在租户上下文执行
│     SQL 文本必须在 WHERE/JOIN ON/INSERT VALUES 中出现 @TenantId 等值比较
│     轻量词法检查 TenantSqlValidation 缓存校验结果，防注释/字符串绕过
├── HostOnly + None                    // 必须在 Host 上下文执行
│     TenantBinding 必须为 None，禁止自动注入 @TenantId
└── Global + None                      // 允许任意上下文执行
      SQL 自身负责显式行过滤；TenantBinding 必须为 None
```

- 违反时抛出 `TenantContextMissingException` / `TenantScopeViolationException` / `HostContextRequiredException`
- `SqlScopeGuard` 为 `internal static sealed`，校验无返回值，调用路径必须经过 `DapperSqlExecutor.CreateCommand`
- 每条生产 Global Statement 必须在 `contracts/architecture/global-sql-statements.json` 登记

### 4.3 双数据库策略

- **一等支持**：SQL Server 与 MySQL，任何结构变更必须同时提供两份迁移 + 测试
- **统一命名**：表/列/索引/约束名称两库完全一致，Provider 语法差异不得改变领域命名
- **Provider 物理类型**：
  - SQL Server：`uniqueidentifier` (UUID)
  - MySQL：`BINARY(16)` RFC 9562 大端字节序
  - 应用端统一使用 C# `Guid`

---

## 5. 多租户架构

```text
请求管道:
  1. Trusted Proxy → 规范化 X-Forwarded-* (TrustedProxyForwardingExtensions)
  2. TenantResolutionMiddleware
     ├── Host Header 解析 (域名租户，缓存键 TenantResolutionByDomain)
     ├── 认证 Claim 中可信 TenantId (TenantResolutionById)
     └── 显式切换 (ICurrentTenantContextWriter.SetTenant/SetHost，需授权 + 审计)
  3. SqlScopeGuard → TenantRequired 语句强制带 @TenantId
  4. CacheKeyBuilder.ForTenant / ForGlobal → scope 段区分 host 或具体 TenantId
  5. Outbox 消息携带 TenantId → 消费方 ICurrentTenantContextWriter 恢复租户上下文
  6. CurrentTenantAccessor (Scoped) 通过显式接口实现隐藏写能力
```

---

## 6. 权限与安全架构

### 6.1 超级管理员边界

- 持久化 `host-administrator` 系统角色 + 服务端动态投影全部权限
- ❌ 禁止用户名判断、魔法字段、通配符权限
- ❌ 禁止绕过租户隔离、账号/会话状态、审计
- **最后一名保护**：并发下至少保留一名有效超级管理员

### 6.2 精确权限授权

```text
权限码格式: {module}.{plural_resource}.{action}
示例:
  tenancy.tenants.read
  identity.users.write
  jobs.definitions.trigger
```

- **每个管理操作绑定独立权限码**，禁止粗粒度 `*.write` 隐式覆盖
- Vue 无权限时不创建操作入口（只负责体验）
- **服务端 Endpoint 必须独立授权校验**（真正的安全边界）
- 角色授权按「模块 / 页面 / 操作」分层展示

---

## 7. 缓存架构

- **唯一实现**：FusionCache + HybridCache 双抽象（通过 `AddFullNetCaching` 注册）
- **两级缓存**：L1 内存 + L2 Redis + Redis Backplane 广播失效
- **一致性分类** `CacheConsistencyClass`：`Weak / Eventual / StrongNoL1`；`StrongNoL1` 禁用 L1，只走 L2 + 权威源
- **统一键构造** [`CacheKeyBuilder`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Caching.Fusion/CacheKeyBuilder.cs)：7 段格式 `fullnet:{env}:{scope}:{module}:{res}:{id}:{ver}`，`scope` 段区分 `host` 与具体 `TenantId`，禁止业务模块自行拼接
- **策略注册表** `ICachePolicyRegistry`：按稳定 `entryName` 查询 `CacheEntryPolicy`，禁止手写 TTL；`C0/N0` 路径抛错避免绕过
- **失效边界** [`ICacheInvalidator`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Caching.Abstractions/ICacheInvalidator.cs)（在 `Full.NET.Caching.Abstractions`）：
  - `RemoveAsync(entryName, key, scope)` / `RemoveByTagAsync(...)`
  - `scope` 区分 `Local`（本机 L1）与 `AllLevels`（L1+L2+Backplane）
- **失效闭环**：提交事务 → 本机同步 L1 删除；Outbox 事件 → 跨节点 Backplane 广播删除；TTL + 版本号 + 权威源兜底
- **可靠性监控** `FusionCacheReliabilityMonitor` + `CacheReliabilityTelemetry`：失效时延/失败、陈旧命中、Backplane 熔断恢复的低基数指标

---

## 8. 可观测性

| 维度 | 实现 |
|------|------|
| 结构化日志 | Serilog 有界异步 Sink（`FullNetBoundedAsyncSink`），普通/高优先级独立队列，`HighPriorityLoggingHealthCheck` 监控高优队列积压；可观测性管理模块按 `DiagnosticPolicy` 动态降级 |
| 分布式追踪 | OpenTelemetry OTLP Export，Outbox/Kafka/Consumer 全链路 ActivitySource |
| 指标 | OpenTelemetry Metrics + Prometheus 抓取；Outbox 积压/保留/影子比对、Kafka Lag、Consumer Lag 等独立 Meter |
| 健康检查 | `HealthEndpointExtensions`：数据库连通性/Schema、分布式缓存、Kafka Broker/Consumer、SignalR Backplane、高优日志队列 |
| HTTP 操作日志 | `HttpOperationLogMiddleware` + `HttpOperationLogEmitter`，按 `HttpOperationLogProfile` 选择性记录，`HttpOperationLogSanitizer` 脱敏 |
| 审计 | 操作日志、访问日志、异常日志、出站调用日志，按 `AuditReliabilityClass` 分类写入；事务型审计走 `ITransactionalDomainAuditWriter` |

---

## 9. 代码生成内核

> 项目：[`Full.NET.Data.CodeGeneration`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.CodeGeneration) + [`Full.NET.CodeGeneration.Cli`](file:///G:/wwwroot/github_fork/Full.NET/src/Tools/Full.NET.CodeGeneration.Cli) + 业务模块 [`Full.NET.Modules.CodeGeneration`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.CodeGeneration)

代码生成内核按数据库 Schema 元数据驱动，自动产出 CRUD 后端 + Vue 前端 + 迁移脚本 + 集成装配代码，三阶段流水线分层：

```text
Schema 层 (src/.../Schema/)
  ├── DatabaseTableCatalogReader     # 反读 information_schema，产出 DatabaseColumnMetadata
  ├── DatabaseCrudSchemaImporter    # 导入外部 Schema 草稿 (DatabaseCrudImportOptions)
  ├── DatabaseCrudSchemaAssembler   # 装配 FullNetCrudSchema（实体能力、关系、所有权模式、删除模式）
  └── CatalogMigrationDraftGenerator # 反向生成迁移草稿

Generation 层 (src/.../Generation/)
  ├── CrudGenerationWorkspace       # 工作区：GenerationWorkspaceStore / Snapshot / Path / Conflict
  ├── GenerationWritePlanner        # 写入计划：GenerationWritePlan / WriteAction
  ├── CrudArtifactGenerator         # 产物分发：
  │     ├── CrudBackendFeatureGenerator    # 后端 Command/Query/Handler/Endpoint
  │     ├── CrudOpenApiContractGenerator    # OpenAPI 契约
  │     ├── CrudVueViewGenerator            # Vue 视图
  │     ├── CrudClientPageModelGenerator    # 前端页面模型
  │     ├── CrudMigrationTemplateGenerator  # 迁移脚本
  │     ├── CrudOrganizationOwnershipGenerator # 组织归属
  │     ├── CrudAuthorizationContributorFragmentGenerator # 权限片段
  │     └── CrudBackendFeatureGenerator    # 后端 Feature
  ├── GenerationManifest            # 清单：GenerationManifestDocument
  └── GenerationRollbackWorkspace   # 回滚：Checkpoint / Document / Store

Integration 层 (src/.../Integration/)
  ├── CompositionIntegrationApplyCommand  # 修改 Composition 项目（CompositionCatalogEditor / CompositionProjectEditor）
  ├── ModuleEntryIntegrationApplyCommand  # 修改模块入口（ModuleEntryIntegrationEditor）
  ├── ModuleIntegrationBackendApplyCommand # 修改模块后端工作区（ModuleIntegrationBackendWorkspace）
  ├── ClientRouteIntegrationApplyCommand   # 修改前端路由（ClientRouteIntegrationEditors）
  ├── AuthorizationContributorIntegrationEditor # 权限贡献者装配
  ├── ModuleIntegrationCompilationCommand   # 编译校验（ModuleIntegrationBuildProjection / Snapshot）
  └── ModuleIntegrationHostOrchestrator     # 宿主编排

辅助层
  ├── Naming/        # NamingProfile、SchemaName、DatabaseObjectNameBuilder、ContractNameValidator
  ├── PrimaryKeys/   # PrimaryKeyProfile、PrimaryKeyTypeMapping、PrimaryKeyPhysicalTypes
  ├── Packaging/     # GeneratedArtifactZip
  └── Serialization/ # CodeGenerationToolchainJsonSerializerContext
```

**关键约束**：
- 生成器不直接执行写盘，必须经 `GenerationWritePlanner` 产出 `GenerationWritePlan`，由 `GenerationWorkspaceStore` 原子落盘
- 每次生成产生 `GenerationManifestDocument` 与 `GenerationRollbackCheckpointDocument`，支持 `GenerationRollbackWorkspace` 回滚
- `CrudSceneGuard` 阻止非授权场景下的批量生成
- 所有装配命令均为 `*ApplyCommand` 模式，可被宿主 `ModuleIntegrationHostOrchestrator` 编排为单一事务
- 命名、主键、Schema 草稿由 `NamingProfile` / `PrimaryKeyProfile` / `FullNetCrudSchema` 强约束，避免运行时漂移
