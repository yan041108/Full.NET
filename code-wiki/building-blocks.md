# 基础设施层 (BuildingBlocks)

BuildingBlocks 是 Full.NET 的底层基础设施，所有业务模块和宿主都依赖它。**核心约束：BuildingBlocks 不得反向引用任何业务模块或 Composition**。

---

## 1. 分层与依赖方向

```text
依赖方向（单向）：
Hosts / Modules / Composition
        │
        ▼
    BuildingBlocks
        │
        ▼
  第三方 NuGet 包
```

BuildingBlocks 之间的依赖关系：
```text
Abstractions (零依赖)
    ▲
    │
    ├── Modularity ──► 引用 Abstractions
    ├── Data.Abstractions ──► 引用 Abstractions
    │       ▲
    │       └── Data.Dapper ──► Data.Abstractions
    │               └── Data.MySql ──► Data.Dapper
    ├── Seeding.Abstractions ──► Abstractions
    │       └── Seeding.Dapper ──► Seeding.Abstractions + Data.Dapper
    ├── Migrations.DbUp ──► Data.Abstractions
    ├── Messaging.Abstractions ──► Abstractions
    │       └── Messaging.Kafka ──► Messaging.Abstractions + Data.Dapper
    ├── Caching.Fusion ──► Abstractions
    ├── Realtime.Abstractions ──► Abstractions
    │       └── Realtime.SignalR ──► Realtime.Abstractions
    ├── Serialization.MessagePack ──► Abstractions
    ├── Validation.FluentValidation ──► Abstractions
    ├── Localization ──► Abstractions
    ├── Data.CodeGeneration ──► Abstractions
    └── Hosting ──► 引用上述多数 BuildingBlock
```

---

## 2. Full.NET.Abstractions — 核心抽象层

> 项目：[`src/BuildingBlocks/Full.NET.Abstractions`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Abstractions)

**零依赖**的纯抽象程序集，定义整个系统的原子契约。

### 2.1 Results — 结果与错误模型

| 类型 | 说明 |
|------|------|
| `Result<T>` | 统一的操作结果，包含 `IsSuccess / Value / Error` |
| `Result` | 无返回值的结果 |
| `Error` | 结构化错误：`Type / Code / Message / TraceId` |
| `ErrorType` | 错误类型枚举：`Validation / Authorization / NotFound / Conflict / ...` |
| `CommonErrorCodes` | 通用错误码常量 |
| `PagedResult<T>` | 分页结果：`Items / TotalCount / PageNumber / PageSize` |
| `ValidationViolation` | 校验违规项：`PropertyName / ErrorCode / ErrorMessage` |

### 2.2 Messaging — CQRS 消息契约

| 接口 | 说明 |
|------|------|
| `ICommand<TResult>` | 写操作命令标记 |
| `ITransactionalCommand` | 需要事务的命令标记 |
| `ICommandHandler<TCommand, TResult>` | 命令处理器 |
| `ICommandDispatcher` | 命令分发器：`SendAsync<TCommand, TResult>()` |
| `IQuery<TResult>` | 读操作查询标记 |
| `IQueryHandler<TQuery, TResult>` | 查询处理器 |
| `IQueryDispatcher` | 查询分发器 |
| `IDispatchBehavior` | 分发管道行为（日志、校验、审计等） |
| `IIntegrationEventHandler<TEvent>` | 集成事件处理器 |
| `IntegrationEventContext` | 事件处理上下文（租户、元数据、关联 ID） |

### 2.3 Tenancy — 多租户抽象

| 类型 | 说明 |
|------|------|
| `ICurrentTenant` | 当前租户访问器：`TenantId / IsHost / Scope` |
| `TenantContext` | 租户上下文，支持 `Push()`/`Pop()` 嵌套切换 |
| `IActiveTenantContextResolver` | 解析请求中的活动租户 |
| `CurrentTenantAccessor` | `AsyncLocal` 实现的当前租户存储 |

### 2.4 其他

| 命名空间 | 关键类型 |
|----------|----------|
| `Auditing` | `AuditReliabilityClass` 审计可靠性分类枚举 |
| `Ids` | `IIdGenerator` / `GuidV7IdGenerator` — UUID v7 生成 |
| `Time` | `IClock` / `SystemClock` — 可测试时间抽象 |

---

## 3. Full.NET.Modularity — 模块系统与 CQRS 分发

> 项目：[`src/BuildingBlocks/Full.NET.Modularity`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Modularity)

### 3.1 模块系统

| 类型 | 说明 |
|------|------|
| `IFullNetModule` | 模块入口接口：`Name / Dependencies / AddServices / MapEndpoints / ...` |
| `IFullNetModuleCatalog` | 模块目录：按名称查询、依赖排序、Host Profile 选择 |
| `FullNetModuleDescriptor` | 模块描述符：名称、依赖、实例、注册阶段 |
| `FullNetModuleRegistry` | 模块注册器：静态注册和目录构建 |
| `ModulePipelineStage` | 中间件插入阶段枚举：`Authentication / Authorization / Routing / Endpoint` |

### 3.2 CQRS 分发器实现

| 类 | 职责 |
|----|------|
| `CommandDispatcher` | 扫描 `ICommandHandler<,>` 实现，按泛型类型分发，串联 `IDispatchBehavior` 管道 |
| `QueryDispatcher` | 查询分发器，模式同上 |

**分发管道执行顺序**：
```
CommandDispatcher.SendAsync
  └── IDispatchBehavior[] 依次执行
        ├── 校验 Behavior（FluentValidation）
        ├── 日志 Behavior
        ├── 审计 Behavior
        └── 实际 CommandHandler.HandleAsync
```

---

## 4. Full.NET.Data.Abstractions — 数据访问抽象

> 项目：[`src/BuildingBlocks/Full.NET.Data.Abstractions`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Abstractions)

### 4.1 核心执行器

| 接口 | 说明 |
|------|------|
| `ICommandExecutor` | 命令执行：`ExecuteAsync / ExecuteListAsync` |
| `IQueryExecutor` | 查询执行：`QueryAsync / QueryFirstOrDefaultAsync / QueryScalarAsync` |
| `IMultiResultQueryExecutor` | 多结果集执行（`QueryMultiple`） |
| `IMultiResultReader` | 多结果集顺序读取器 |
| `ICommandTransaction` | 事务边界：`BeginAsync / CommitAsync / RollbackAsync` |

### 4.2 SQL Scope（关键安全边界）

| 类型 | 说明 |
|------|------|
| `SqlDataScope` | 枚举：`TenantRequired / HostOnly / Global` |
| `SqlTenantBinding` | 租户绑定：`CurrentTenantId / None` |
| `SqlStatement` | SQL 语句包装器，携带 Scope + Binding 元数据 |
| `SqlScopeExceptions` | Scope 违规异常类型 |

### 4.3 Outbox / Inbox 抽象

| 接口 | 说明 |
|------|------|
| `IOutboxStore` | Outbox 查询：按状态领取、更新租约、标记完成/失败 |
| `IOutboxWriter` | Outbox 写入：与业务数据同事务原子写入 |
| `IOutboxBacklogReader` | 积压读取：流级别积压/重试统计 |
| `IOutboxRetentionStore` | 保留策略：清理旧消息、旧版本退役 |
| `IIntegrationEventInbox` | 消费 Inbox：幂等去重、完成标记、死信 |
| `IIntegrationEventSerializer` | 事件序列化（默认 MessagePack） |

### 4.4 数据库配置

| 类型 | 说明 |
|------|------|
| `DatabaseOptions` | 连接串、Provider 类型、最大池大小 |
| `DatabaseProvider` | 枚举：`SqlServer / MySql` |
| `MySqlGuidStorageMode` | MySQL UUID 存储模式枚举（仅 Binary16 受支持） |

---

## 5. Full.NET.Data.Dapper — Dapper 实现

> 项目：[`src/BuildingBlocks/Full.NET.Data.Dapper`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Dapper)

### 5.1 核心类型

| 类 | 职责 |
|----|------|
| `DbSession` | 数据库会话：持有连接 + 当前事务 |
| `DbConnectionFactory` | 连接工厂：按 Provider 创建连接 |
| `DapperCommandTransaction` | 事务实现：`ICommandTransaction` 的 Dapper 版本 |
| `DapperSqlExecutor` | SQL 执行器：`ICommandExecutor` + `IQueryExecutor` 实现 |
| `DapperMultiResultReader` | 多结果集读取器 |
| `SqlScopeGuard` | **关键安全类**：执行前校验 SqlDataScope 与当前租户上下文匹配 |

### 5.2 类型处理器

| 类 | 作用 |
|----|------|
| `AssignedGuidTypeHandler` | MySQL `BINARY(16)` ↔ C# `Guid` 互转（RFC 9562 大端） |
| `UtcDateTimeOffsetTypeHandler` | UTC `DateTimeOffset` 标准化存储 |

### 5.3 Outbox 实现

| 类 | 说明 |
|----|------|
| `DapperOutboxWriter` | 传统 Outbox Writer → `fn_outbox_message` 表 |
| `DapperAppendOnlyOutboxWriter` | 追加式 Outbox Writer → `fn_messaging_outbox_event` 表 |
| `DapperRoutedOutboxWriter` | 路由 Writer：按 `EventStreamOwnership` 选择写入目标 |
| `DapperOutboxStore` | Outbox 领取/状态更新（租约、续租、完成、死信） |
| `DapperEventStreamOwnershipGate` | 事件流所有权 CAS 切换（Compare-And-Swap + PreviousOwner） |

### 5.4 Inbox 实现

| 类 | 说明 |
|----|------|
| `DapperIntegrationEventInbox` | 消费端去重、完成标记、死信写入 |

### 5.5 健康检查

| 类 | 检查项 |
|----|--------|
| `DatabaseConnectivityHealthCheck` | 能否成功连接并执行 `SELECT 1` |
| `DatabaseSchemaHealthCheck` | 关键迁移脚本是否已执行 |

---

## 6. Full.NET.Migrations.DbUp — 数据库迁移

> 项目：[`src/BuildingBlocks/Full.NET.Migrations.DbUp`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Migrations.DbUp)

| 类型 | 说明 |
|------|------|
| `IDatabaseMigrationRunner` | 迁移运行器接口 |
| `DbUpMigrationRunner` | DbUp 封装：按 Provider 加载 SQL 脚本、记录已执行版本 |
| `MigrationAssembly` | 标记包含迁移脚本的程序集 + Provider 子目录 |
| `UuidBinaryContractOptions` | UUID 二进制契约配置（MySQL BINARY 16 编解码） |
| `PreV1NamingContractOptions` | Pre-v1 命名契约兼容选项 |

**迁移脚本位置约定**：
```text
{MigrationsAssembly}/Migrations/
  ├── SqlServer/
  │   ├── 001_Foundation.sql
  │   └── 002_Identity.sql
  └── MySql/
      ├── 001_Foundation.sql
      └── 002_Identity.sql
```

---

## 7. Full.NET.Caching.Fusion — 混合缓存

> 项目：[`src/BuildingBlocks/Full.NET.Caching.Fusion`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Caching.Fusion)

### 7.1 类型速查

| 类型 | 说明 |
|------|------|
| `CacheOptions` | 缓存配置：L1/L2/Backplane/序列化 |
| `CacheEntryPolicy` | 单条缓存策略：TTL / 失效分类 / 一致性等级 |
| `CacheConsistencyClass` | 一致性枚举：`Weak / Eventual / StrongNoL1` |
| `CacheEntryDefinitionOptions` | 缓存条目定义（用于集中注册） |
| `CachePolicyRegistry` / `ICachePolicyRegistry` | 策略注册表：按缓存键前缀查找策略 |
| `CacheKeyBuilder` | 统一缓存键构造器：`fullnet:{env}:{scope}:{module}:{res}:{id}:{ver}` |
| `FusionCacheReliabilityMonitor` | 可靠性监控：陈旧命中、失效失败、Backplane 状态 |
| `CacheReliabilityTelemetry` | 低基数指标发射 |

**注册方式**：
```csharp
services.AddFullNetCaching(configuration);  // 启用 FusionCache + HybridCache 双抽象
```

---

## 8. Full.NET.Messaging — 消息基础设施

### 8.1 Abstractions 抽象层

> 项目：[`src/BuildingBlocks/Full.NET.Messaging.Abstractions`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Messaging.Abstractions)

| 类型 | 说明 |
|------|------|
| `EventDeliveryOwner` | 所有权枚举：`LegacyPolling / ShadowCdcKafka / CdcKafka` |
| `EventStreamOwnershipRecord` | 事件流所有权记录：`StreamId / CurrentOwner / PreviousOwner / Version` |
| `IEventStreamOwnershipGate` | 所有权切换门（CAS 原子切换） |
| `IEventStreamOwnershipStore` | 所有权持久化 |
| `IntegrationEventEnvelope` | 事件信封：`MessageId / MessageType / SchemaVersion / TenantId / Payload` |
| `IntegrationEventMetadata` | 事务元数据：`CorrelationId / CausationId / TraceParent / PartitionKey` |
| `IntegrationEventFailure` | 失败信息：`ReasonCode / RetryCount / LastError` |
| `KafkaReplayContracts` | Kafka 范围重放 API 契约 |

### 8.2 Kafka 实现层

> 项目：[`src/BuildingBlocks/Full.NET.Messaging.Kafka`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Messaging.Kafka)

**Worker 处理链路**：
```
KafkaConsumerWorker (BackgroundService)
  ├── KafkaConsumerPollTiming           // 定时 + 有界批量 Poll
  ├── KafkaPartitionWorkScheduler       // 分区粒度工作调度
  ├── KafkaConsumerMessageProcessor     // 单消息处理：头解析 → 反序列化 → Handler
  ├── KafkaRetryRouter                  // 可重试消息 → 重试 Topic 路由
  ├── KafkaDeadLetterPublisher          // 毒消息 → DLQ Topic
  ├── KafkaOffsetCommitCoordinator      // 消费 DB 提交后才 Offset 提交
  └── KafkaConsumerLagObserver          // Consumer Lag 指标观测
```

| 辅助类 | 作用 |
|--------|------|
| `KafkaDeliveryHeaders` | 发布/消费 Header 读写（MessageType、SchemaVersion、TenantId、TraceParent 等） |
| `KafkaEnvelopeReader` | Kafka 消息 → `IntegrationEventEnvelope` 解析 |
| `KafkaMessagingProducer` | 生产端：CDC Relay 使用的发布器 |
| `KafkaReplayService` | 运维能力：范围重放 / 分区 Offset 重置 |
| `KafkaConsumerBufferPressure` | 缓冲压力观测：防止内存无限增长 |
| `KafkaTopicNames` | Topic 名称标准化构造 |
| `KafkaHealthCheck` | Broker 连通性 + 消费者组健康检查 |

---

## 9. Full.NET.Realtime — 实时通信

### 9.1 Abstractions

> 项目：[`src/BuildingBlocks/Full.NET.Realtime.Abstractions`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Realtime.Abstractions)

| 类型 | 说明 |
|------|------|
| `IRealtimePublisher` | 发布接口：`PublishAsync / PublishToGroupAsync` |
| `RealtimeMessage` | 消息封装：`Code / Payload / Group / TenantId` |
| `RealtimeMessageCodes` | 稳定消息码常量 |
| `RealtimeGroups` | 分组命名规范构造器（按租户/用户/会话） |
| `NullRealtimePublisher` | 空实现（开发/测试禁用实时） |

### 9.2 SignalR 实现

> 项目：[`src/BuildingBlocks/Full.NET.Realtime.SignalR`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Realtime.SignalR)

| 类型 | 说明 |
|------|------|
| `FullNetNotificationHub` | SignalR Hub 实现：鉴权连接、分组加入/离开 |
| `SignalRRealtimePublisher` | `IRealtimePublisher` 的 SignalR 实现 |
| `RealtimeRedisConfiguration` | Redis Backplane 配置（多实例部署必需） |
| `RealtimeBackplaneProbe` | Backplane 连通性探测健康检查 |

---

## 10. Full.NET.Hosting — 宿主横切能力

> 项目：[`src/BuildingBlocks/Full.NET.Hosting`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Hosting)

### 10.1 API 异常与响应

| 类型 | 职责 |
|------|------|
| `FullNetExceptionHandler` | 全局异常处理器：映射异常 → ProblemDetails |
| `IApiResultMapper` | 响应结果映射器接口 |
| `StandardApiResultMapper` | 标准映射：`Result<T>` → HTTP 状态码 + ProblemDetails/JSON |
| `AdminNetApiResultMapper` | 兼容 Admin.NET 统一信封映射（适配层启用） |
| `IErrorMessageLocalizer` | 错误消息本地化接口 |
| `ResourceErrorMessageLocalizer` | `.resx` 资源实现 |
| `PreV1ProtocolCompatibility` | Pre-v1 旧错误码兼容层 |

### 10.2 可观测性管道

| 命名空间 | 关键类型 |
|----------|----------|
| `Observability` | `ServiceDefaultsExtensions`（Serilog/OTel/Health 默认注入） |
| | `HttpOperationLogMiddleware`（HTTP 操作审计日志） |
| | `DiagnosticPolicy`（诊断策略 + 日志降级模式） |
| | `FullNetBoundedAsyncSink`（有界异步 Serilog Sink，防止日志反压） |
| `RateLimiting` | `FullNetRateLimitExtensions`（固定窗口/滑动窗口/令牌桶策略） |
| `Forwarding` | `TrustedProxyOptions`（可信代理边界，规范化 X-Forwarded-*） |
| `OpenApi` | `FullNetOpenApiExtensions`（Scalar + OpenAPI 文档配置） |
| `Serialization` | `FullNetJsonOptionsExtensions`（System.Text.Json 源生成） |

### 10.3 资源文件

- `Resources/CommonErrors.resx` — 通用错误字符串（zh-CN 默认 + en-US 卫星）

---

## 11. 其他 BuildingBlocks

| 项目 | 关键职责 |
|------|----------|
| `Full.NET.Data.MySql` | MySQL 特有：连接串策略、Schema 模式启动验证器 |
| `Full.NET.Data.CodeGeneration` | 代码生成内核：CRUD Schema 元数据、命名 Profile、主键 Profile |
| `Full.NET.Seeding.Abstractions` | `IDataSeedContributor` / `ISeedOrchestrator` / `SeedProfile` |
| `Full.NET.Seeding.Dapper` | 种子编排器：确定 Profile 继承、执行租约、幂等审计 |
| `Full.NET.Serialization.MessagePack` | MessagePack 契约解析器 + 格式化选项 |
| `Full.NET.Validation.FluentValidation` | FluentValidation 集成：自动扫描 + 统一 `validation.failed` 错误码 |
| `Full.NET.Localization` | 多语言：`LocaleCatalog`、`CultureScope`、BCP 47 规范化、HTTP Header 协商 |
