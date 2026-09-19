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
Abstractions (零依赖，含 ICommandTransaction / IIntegrationEventHandler / Tenancy / Results / Auditing / Ids / Time)
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
    ├── Caching.Abstractions ──► Abstractions
    │       └── Caching.Fusion ──► Caching.Abstractions + Abstractions
    ├── Realtime.Abstractions ──► Abstractions
    │       └── Realtime.SignalR ──► Realtime.Abstractions
    ├── Serialization.MemoryPack ──► Data.Abstractions（实现 IIntegrationEventSerializer）
    ├── Validation.FluentValidation ──► Abstractions
    ├── Localization ──► Abstractions
    ├── Data.CodeGeneration ──► Data.Abstractions + Abstractions
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

> 文件目录：[`src/BuildingBlocks/Full.NET.Abstractions/Messaging/`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Abstractions/Messaging)

| 接口 | 说明 |
|------|------|
| `ICommand<TResult>` | 写操作命令标记（空接口） |
| `ITransactionalCommand` / `ITransactionalCommand<TResult>` | 需要事务的命令标记；分发器自动包裹 `ICommandTransaction` |
| `ICommandHandler<TCommand, TResult>` | 命令处理器：`HandleAsync(command, ct)` |
| `ICommandDispatcher` | 命令分发器：`SendAsync<TCommand, TResult>()` |
| `IQuery<TResult>` | 读操作查询标记 |
| `IQueryHandler<TQuery, TResult>` | 查询处理器 |
| `IQueryDispatcher` | 查询分发器 |
| `IDispatchBehavior<TMessage, TResult>` | 分发管道行为（日志、校验、审计等）；按注册顺序逆序嵌套 |
| `DispatchHandlerDelegate<TResult>` | Behavior 管道下一棒委托 |
| `ICommandTransaction` | 事务边界：`ExecuteAsync<T>` / `ExecuteResultAsync<T>`（非 Begin/Commit/Rollback） |
| `IIntegrationEventHandler<TEvent>` | 集成事件处理器；声明 `EventType / LegacyEventTypes / SchemaVersion / IdempotencyStrategy` |
| `IntegrationEventContext` | 事件处理上下文（租户、元数据、关联 ID） |
| `IntegrationEventIdempotencyStrategy` | 幂等策略枚举 |
| `IntegrationEventHandlerMatcher` | Handler 匹配器（含 Legacy 事件类型别名解析） |

### 2.3 Tenancy — 多租户抽象

> 文件目录：[`src/BuildingBlocks/Full.NET.Abstractions/Tenancy/`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Abstractions/Tenancy)

| 类型 | 说明 |
|------|------|
| `ICurrentTenant` | 当前租户只读访问器：`IsAvailable / IsHost / Id / Identifier / Name` |
| `ICurrentTenantContextWriter` | 上下文写入器：`SetTenant(context) / SetHost() / Clear()` |
| `TenantContext` | 不可变 `record (Id, Identifier, Name)`；不再支持 Push/Pop 嵌套 |
| `CurrentTenantAccessor` | Scoped 实现，同时实现 `ICurrentTenant` 与 `ICurrentTenantContextWriter`，内部使用 `AsyncLocal<TenantContext?>` |
| `IActiveTenantContextResolver` | 解析请求中的活动租户 |

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
| `IFullNetModule` | 模块入口接口：`Name / Dependencies / OptionalContractDependencies（默认空） / AddServices / AddMigrationServices（默认实现） / MapEndpoints / AddBackgroundServices（默认实现） / UseModuleMiddleware（默认实现）` 共 5 方法 + 3 属性 |
| `IFullNetModuleCatalog` | 模块目录：按名称查询、依赖排序、Host Profile 选择 |
| `FullNetModuleDescriptor` | 模块描述符：名称、依赖、实例、注册阶段 |
| `FullNetModuleRegistry` | 模块注册器：静态注册和目录构建 |
| `ModulePipelineStage` | 中间件插入阶段枚举：`Authentication / Authorization / Routing / Endpoint` |

> `OptionalContractDependencies` 仅用于消费事件或最小只读契约；不能用于同步调用、数据库访问或服务解析，不参与启用集依赖闭包。

### 3.2 CQRS 分发器实现

| 类 | 职责 |
|----|------|
| `CommandDispatcher` | 扫描 `ICommandHandler<,>` 实现，按泛型类型分发，按注册顺序**逆序**串联 `IDispatchBehavior` 管道形成俄罗斯套娃；对 `ITransactionalCommand` 自动包裹 `ICommandTransaction` |
| `QueryDispatcher` | 查询分发器，模式同上但不参与数据库事务 |

**分发管道执行顺序**（Behavior 逆序嵌套，最外层先执行）：
```
CommandDispatcher.SendAsync
  └── IDispatchBehavior[] 逆序嵌套
        ├── 校验 Behavior（FluentValidation，未通过短路）
        ├── 日志 Behavior
        ├── 审计 Behavior
        ├── 事务 Behavior（仅 ITransactionalCommand）
        └── 实际 CommandHandler.HandleAsync
```

---

## 4. Full.NET.Data.Abstractions — 数据访问抽象

> 项目：[`src/BuildingBlocks/Full.NET.Data.Abstractions`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Abstractions)

### 4.1 核心执行器

> 文件目录：[`src/BuildingBlocks/Full.NET.Data.Abstractions/`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Abstractions)

| 接口 | 说明 |
|------|------|
| `ICommandExecutor` | 命令执行：仅 `ExecuteAsync(statement, parameters, ct) -> Task<int>`（受影响行数） |
| `IQueryExecutor` | 查询执行：`QuerySingleOrDefaultAsync<T>`（0/1 行，>1 抛异常） + `QueryAsync<T> -> IReadOnlyList<T>` |
| `IMultiResultQueryExecutor` | 多结果集执行（`QueryMultiple`） |
| `IMultiResultReader` | 多结果集顺序读取器 |
| `IExternalDatabaseConnectionFactory` | 外部连接工厂（用于跨连接显式事务编排） |
| `IDatabaseSessionLock` | 数据库会话级锁（Distributed Lock 配合） |
| `IDatabaseAdmissionPriorityScope` | 数据库准入优先级作用域 |
| `IDataTransactionState` | 事务状态查询接口 |

> 注：`ICommandTransaction`（事务边界：`ExecuteAsync<T>` / `ExecuteResultAsync<T>`）定义在 [`Full.NET.Abstractions/Messaging/ICommandTransaction.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Abstractions/Messaging/ICommandTransaction.cs)，由 `CommandDispatcher` 对 `ITransactionalCommand` 自动调用，Dapper 实现见 `DapperCommandTransaction`。

### 4.2 SQL Scope（关键安全边界）

| 类型 | 说明 |
|------|------|
| `SqlDataScope` | 枚举：`TenantRequired / HostOnly / Global` |
| `SqlTenantBinding` | 租户绑定：`CurrentTenantId / None` |
| `SqlStatement` | 不可变 `record (Name, Text, Scope, TenantBinding)`，按位置参数构造；另提供 `(Name, Text, Scope)` 三参构造（默认 `TenantBinding.None`）与 `Deconstruct` 解构 |
| `SqlScopeExceptions` | Scope 违规异常类型：`TenantContextMissingException / TenantScopeViolationException / HostContextRequiredException` |
| `DataCommandException` | 数据命令执行异常 |

### 4.3 Outbox / Inbox 抽象

| 接口 | 说明 |
|------|------|
| `IOutboxWriter` | Outbox 写入：`AddAsync<TEvent>(...)` 与业务数据同事务原子写入；路由版按 `EventStreamOwnership` 选择目标表 |
| `IOutboxStore` | Outbox 查询/更新：领取、租约续租、标记完成/失败、死信；含 `OutboxDeadLetterReasons` 与 `OutboxConcurrencyException` / `OutboxLeaseExpiredException` |
| `IOutboxBacklogReader` | 积压读取：`ReadStreamBacklogAsync(messageType, schemaVersion, ct)` 按事件流粒度查询积压/重试；返回 `OutboxBacklogSnapshot` |
| `IOutboxRetentionStore` | 保留策略：`DeleteProcessedBatchAsync(...)` 清理已处理消息与旧版本退役 |
| `IIntegrationEventInbox` | 消费 Inbox：`PrecheckBatchAsync` 批量去重 → `ClaimAsync` 领取 → `MarkProcessedAsync` 标记完成/死信 |
| `InboxPrecheck` | 幂等预检模型：`InboxMessageFingerprint` + `InboxPrecheckStatus` + `InboxPrecheckResult` |
| `OutboxEnvelope` | 不可变 record（10 字段：MessageId / EventType / SchemaVersion / TenantId / Payload / ContentType / OccurredAt / Metadata / TraceParent / IdempotencyKey） |
| `IIntegrationEventSerializer` | 事件序列化抽象（默认实现为 MemoryPack，`ContentType = application/x-memorypack`） |

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
| `DbSession` | 数据库会话：持有连接 + 当前事务；`DbSessionConnectionLease` 控制连接生命周期 |
| `DbConnectionFactory` / `IDbConnectionFactory` | 连接工厂：按 Provider 创建连接；`ExternalDatabaseConnectionFactory` 支持显式外部连接 |
| `DapperCommandTransaction` | `ICommandTransaction` 的 Dapper 实现；`RecordingDbTransactionCoordinator` + `IDbTransactionCoordinator` 用于多 Outbox 协调 |
| `DapperSqlExecutor` | `ICommandExecutor` + `IQueryExecutor` 实现；AOT 路径走 `DapperAotSqlExecution` |
| `DapperMultiResultReader` | 多结果集读取器 |
| `SqlScopeGuard` | **关键安全类**（`internal static sealed`）：执行前同步校验 `SqlDataScope` 与当前租户上下文匹配；轻量词法 `TenantSqlValidation` 缓存校验结果 |
| `DatabaseAdmissionGate` / `DatabaseAdmissionPriorityScope` | 准入门与优先级作用域：过载保护 |
| `DatabaseConnectionTelemetry` / `DapperTelemetry` / `DapperLog` | 数据访问可观测性 |
| `DapperAotCommandFactory` / `DapperAotStaticCommandPlanRegistry` / `DapperAotMaterializerRegistry` | Native AOT 源生成所需注册器 |
| `IDapperAotMaterializerContributor` / `DapperAotEnumerableParameterExpander` | AOT 物化器贡献者与可枚举参数展开 |

### 5.2 类型处理器

| 类 | 作用 |
|----|------|
| `AssignedGuidTypeHandler` / `AssignedGuidAotTypeHandler` | MySQL `BINARY(16)` ↔ C# `Guid` 互转（RFC 9562 大端） |
| `UtcDateTimeOffsetTypeHandler` / `UtcDateTimeOffsetAotTypeHandler` | UTC `DateTimeOffset` 标准化存储 |
| `MySqlSchemaModeStartupValidator` | MySQL Schema 模式启动校验器 |
| `MySqlAotUtcDateTimeOffsetShim` | MySQL AOT DateTimeOffset 适配 |

### 5.3 Outbox 实现

| 类 | 说明 |
|----|------|
| `DapperOutboxWriter` | 传统 Outbox Writer → `fn_outbox_message` 表 |
| `DapperAppendOnlyOutboxWriter` | 追加式 Outbox Writer → `fn_messaging_outbox_event` 表（CdcKafka 路径） |
| `DapperRoutedOutboxWriter` | 路由 Writer：按 `EventStreamOwnership` 选择写入目标表 |
| `DapperOutboxStore` | Outbox 领取/状态更新（租约、续租、完成、死信）；`OutboxSql` / `OutboxMessage` / `AppendOnlyOutboxMessage` 为 SQL 与 DTO |
| `DapperOutboxCommandPath` / `DapperOutboxCommandPathPolicy` / `OutboxTypedCommandPlans` | SQL 路径策略与类型化命令计划 |
| `DapperEventStreamOwnershipGate` | 事件流所有权 CAS 切换（Compare-And-Swap + PreviousOwner） |
| `DapperEventDeliveryProducerFencePositionReader` | 发布端 Fence Position 读取（CutOver 门禁） |
| `MessagingOutboxOptions` | Outbox 选项 |

### 5.4 Inbox 实现

| 类 | 说明 |
|----|------|
| `DapperIntegrationEventInbox` | 消费端 PrecheckBatch → Claim → MarkProcessed 三阶段去重；`InboxBatchPrecheckSql` / `InboxSql` 为 SQL |
| `InboxConsumeResult` | Inbox 消费结果 DTO |

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

## 7. Full.NET.Caching — 缓存与失效

### 7.0 Full.NET.Caching.Abstractions — 失效边界抽象

> 项目：[`src/BuildingBlocks/Full.NET.Caching.Abstractions`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Caching.Abstractions)

业务模块通过稳定条目名声明失效意图，不接触具体缓存 Provider 选项。

| 类型 | 说明 |
|------|------|
| `ICacheInvalidator` | 失效接口：`RemoveAsync(entryName, key, scope, ct)` / `RemoveByTagAsync(entryName, tag, scope, ct)` |
| `CacheInvalidationScope` | 传播范围枚举：`CurrentNodeOnly`（仅本机 L1，不触发 Backplane）/ `AllLayersSynchronous`（L1+L2+Backplane 同步） |

### 7.1 Full.NET.Caching.Fusion — 混合缓存实现

> 项目：[`src/BuildingBlocks/Full.NET.Caching.Fusion`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Caching.Fusion)

#### 7.1.1 类型速查

| 类型 | 说明 |
|------|------|
| `CacheOptions` | 缓存配置：L1/L2/Backplane/序列化 |
| `CacheEntryPolicy` | 单条缓存策略：TTL / 失效分类 / 一致性等级 |
| `CacheConsistencyClass` | 一致性枚举：`Weak / Eventual / StrongNoL1`（强一致类别禁用 L1） |
| `CacheEntryDefinitionOptions` | 缓存条目定义（用于集中注册） |
| `CacheAccessDecision` | 缓存访问决策（L1/L2/Backplane） |
| `CacheEntryLifetime` | 缓存条目生命周期 |
| `CacheEntryNames` | 稳定缓存条目名常量 |
| `CachePolicyRegistry` / `ICachePolicyRegistry` | 策略注册表：`GetRequired / ListPolicies / ResolveAccess / CreateEntryOptions / CreateHybridEntryOptions` |
| `CacheKeyBuilder` | 统一缓存键构造器；按用途分专用方法：`ForTenant / ForGlobal / TenantResolutionByDomain / TenantResolutionById / TenantTag / DomainTag`；键格式 `fullnet:{env}:{scope}:{module}:{res}:{id}:{ver}` |
| `FusionCacheInvalidator` | `ICacheInvalidator` 的 FusionCache 实现；多实例失效使用直接 L1/L2 删除 + Redis Backplane |
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
| `EventDeliveryOwner` | 所有权枚举：`LegacyPolling=0 / ShadowCdc=1 / CdcKafka=2`；同一 (EventType, SchemaVersion) 只能声明一个所有者 |
| `IEffectiveEventDeliveryOwnerResolver` / `LegacyPollingEventDeliveryOwnerResolver` | 解析当前生效所有权；LegacyPolling 解析器用于无 Kafka 模式 |
| `EventStreamOwnershipRecord` | 事件流所有权记录：`StreamId / CurrentOwner / PreviousOwner / Version` |
| `IEventStreamOwnershipGate` | 所有权切换门（CAS 原子切换）；返回 `EventStreamConsumerFenceResult` |
| `IEventStreamOwnershipStore` | 所有权持久化 |
| `IKafkaConnectAdminClient` | Kafka Connect REST 管理客户端（9 方法：`WaitUntilReadyAsync / RegisterConnectorAsync / WaitForConnectorHealthyAsync / DeleteConnectorAsync / PauseConnectorAsync / ResumeConnectorAsync / IsConnectorPausedAsync / TryReadConnectorPositionAsync / TryGetConnectorStatusAsync`） |
| `IEventDeliveryProducerFencePositionReader` | 发布端 Fence Position 读取（Cutover 门禁按目标流积压判断） |
| `IEventDeliveryRollbackReadinessReader` | 切流回退就绪检查 |
| `EventDeliveryOwnershipRevokedException` / `EventDeliveryProducerFencedException` | 所有权吊销 / Producer 被围栏异常 |
| `IIntegrationEventSubscription` | 订阅声明：`ConsumerName / EventType / SchemaVersion / IdempotencyStrategy` + `HandleAsync` |
| `IIntegrationEventHandlerRegistry` | Handler 注册表 |
| `IntegrationEventSubscriptionCatalog` | Scoped 订阅目录：7 方法（注册/查询/枚举/匹配）；空目录时使用 `EmptyIntegrationEventSubscriptionCatalog` |
| `LegacyIntegrationEventHandlerSubscriptionAdapter` | 将旧 `IIntegrationEventHandler` 适配为新订阅声明 |
| `IntegrationEventEnvelope` | 事件信封：`MessageId / MessageType / SchemaVersion / TenantId / Payload / ContentType / ...`（11 字段，`ContentType = application/x-memorypack`） |
| `IntegrationEventMetadata` | 事务元数据：`CorrelationId / CausationId / TraceParent / PartitionKey`；CdcKafka 路径下缺失则抛 `InvalidOperationException` |
| `IntegrationEventFailure` | 失败信息：`ReasonCode / RetryCount / LastError` |
| `IntegrationEventPermanentException` | 不可重试的永久性事件异常 |
| `IntegrationEventTopicDefinition` | 事件流 Topic 定义 |
| `CdcDeliveryPosition` | CDC 投递位置 |
| `ShadowEventComparison` | Shadow CDC 比对结果 |
| `KafkaReplayContracts` | Kafka 范围重放 API 契约；`DisabledKafkaReplayService` / `DisabledKafkaReplayServiceCollectionExtensions` 用于未启用 Kafka 时的占位 |
| `MessagingNames` / `MessagingJsonSerializerContext` | 命名常量 + JSON 序列化上下文 |

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
| `IErrorMessageLocalizer` | 错误消息本地化接口 |
| `ResourceErrorMessageLocalizer` | `.resx` 资源实现 |
| `IErrorResourceSource` / `HostingErrorResourceSources` / `ResourceManagerErrorResourceSource` | 错误资源源与命名空间常量 |
| `NamedMessageFormatter` | 命名消息格式化器 |
| `IPreV1LegacyErrorCodeProfile` / `DefaultPreV1LegacyErrorCodeProfile` | Pre-v1 旧错误码兼容策略声明 |

> 注：`AdminNetApiResultMapper`、`PreV1ProtocolCompatibility`、`AdminNetEnvelope` 等 Admin.NET 包络与 Pre-v1 兼容实现不在 Hosting 项目，而是在独立适配层项目 [`src/Compatibility/Full.NET.Compatibility.AdminNet`](file:///G:/wwwroot/github_fork/Full.NET/src/Compatibility/Full.NET.Compatibility.AdminNet) 中；通过 `ServiceCollectionExtensions.AddAdminNetCompatibility` 显式启用。

### 10.2 可观测性管道

| 命名空间 | 关键类型 |
|----------|----------|
| `Observability` | `ServiceDefaultsExtensions`（Serilog/OTel/Health 默认注入） |
| | `FullNetLoggingPipeline` / `FullNetLoggingPipelineSink`（日志管道与 Serilog Sink） |
| | `LoggingOptions` / `HostingLog` / `LogClassification` |
| | `HttpOperationLogMiddleware` / `HttpOperationLogEmitter` / `HttpOperationLogOptions` / `HttpOperationLogProfile` / `HttpOperationLogSanitizer`（HTTP 操作审计日志全栈） |
| | `DiagnosticPolicy` / `IDiagnosticPolicyStore` / `DiagnosticPolicySnapshot`（诊断策略 + 日志降级模式） |
| | `FullNetBoundedAsyncSink`（有界异步 Serilog Sink，防止日志反压） |
| | `FullNetAsyncLogMonitor` / `FullNetLoggingMonitors` / `HighPriorityLoggingHealthCheck`（异步日志监控与健康检查） |
| | `HealthEndpointExtensions`（健康检查端点） |
| | `ElasticsearchSerilogSinkConfigurator` / `ElasticsearchEndpointRedactor` / `ElasticsearchLoggingOptions` / `ElasticsearchLoggingOptionsValidator`（Elasticsearch Sink 安全配置） |
| | `CacheReliabilityTelemetry`（缓存可靠性指标，跨 Caching 项目） |
| `RateLimiting` | `FullNetRateLimitExtensions` / `RateLimitingOptions` / `RateLimitingOptionsValidator` / `GlobalApiRateLimiterConfigurator` / `RateLimitPolicyErrorCodes`（固定窗口/滑动窗口/令牌桶策略） |
| `Forwarding` | `TrustedProxyOptions` / `TrustedProxyForwardingExtensions` / `TrustedProxyForwardedHeadersConfigurator` / `TrustedProxyOptionsValidator`（可信代理边界，规范化 X-Forwarded-*） |
| `OpenApi` | `FullNetOpenApiExtensions`（Scalar + OpenAPI 文档配置） |
| `Serialization` | `FullNetJsonOptionsExtensions` / `HostingJsonSerializerContext`（System.Text.Json 源生成） |
| `Security` | `DataProtectionServiceCollectionExtensions` / `DataProtectionOptions`（DataProtection 配置） |
| `Api` | `FullNetExceptionHandler` / `StandardApiResultMapper` / `IApiResultMapper` 等（见 10.1） |

### 10.3 资源文件

- `Resources/CommonErrors.resx` — 通用错误字符串（zh-CN 默认 + en-US 卫星）

---

## 11. 其他 BuildingBlocks

| 项目 | 关键职责 |
|------|----------|
| [`Full.NET.Caching.Abstractions`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Caching.Abstractions) | 缓存失效边界抽象：`ICacheInvalidator` + `CacheInvalidationScope`（详见 §7.0） |
| `Full.NET.Data.MySql` | MySQL 特有：连接串策略、Schema 模式启动验证器（`MySqlSchemaModeStartupValidator`）、AOT DateTimeOffset 适配 |
| [`Full.NET.Data.CodeGeneration`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.CodeGeneration) | 代码生成内核三阶段流水线：`Schema/`（`FullNetCrudSchema` / `DatabaseTableCatalogReader` / `DatabaseCrudSchemaImporter` / `FullNetColumn` / `FullNetCrudDataScope`） → `Generation/`（`CrudArtifactGenerator` / `CrudBackendFeatureGenerator` / `CrudVueViewGenerator` / `CrudClientPageModelGenerator` / `CrudMigrationTemplateGenerator` / `CrudOpenApiContractGenerator` / `CrudOrganizationOwnershipGenerator` / `CrudAuthorizationContributorFragmentGenerator` / `CrudSceneGuardGenerator` / `GenerationWritePlanner` / `GenerationWorkspaceStore` / `GenerationManifest` / `GenerationRollbackWorkspace`） → `Integration/`（`ModuleIntegrationPlanner` / `ModuleIntegrationHostOrchestrator` / `CompositionProjectEditor` / `CompositionCatalogEditor` / `ModuleEntryIntegrationEditor` / `ClientRouteIntegrationEditors` / `AuthorizationContributorIntegrationEditor`）；辅助 `Naming/`、`PrimaryKeys/`、`Packaging/`、`Serialization/` |
| [`Full.NET.Seeding.Abstractions`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Seeding.Abstractions) | `IDataSeedContributor`（`Name / Version / Profiles : IReadOnlySet<SeedProfile> / Dependencies / SeedAsync`） / `SeedProfile`（enum + `SeedProfileNames.EffectiveLayers` 封闭继承：Baseline + Development/Demo/Test Overlay） |
| `Full.NET.Seeding.Dapper` | 种子编排器：确定 Profile 继承链（Baseline 先于 Overlay）、执行租约、幂等审计 |
| [`Full.NET.Serialization.MemoryPack`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Serialization.MemoryPack) | `MemoryPackIntegrationEventSerializer`：实现 `IIntegrationEventSerializer`，`ContentType = application/x-memorypack`；通过 `AddFullNetMemoryPack()` 注册为 Singleton；事件 DTO 须标注 `[MemoryPackable]` 由源生成器产出 AOT 友好格式化器 |
| `Full.NET.Validation.FluentValidation` | FluentValidation 集成：自动扫描 + `FluentValidationBehavior` 注入 `IDispatchBehavior` 管道 + 统一 `validation.failed` 错误码 |
| `Full.NET.Localization` | 多语言：`LocaleCatalog`、`CultureScope`、BCP 47 规范化、HTTP Header 协商 |

> 注：`Full.NET.Serialization.MessagePack` 目录下当前无源文件（仅保留 obj 构建产物）；正式事件序列化由 `Full.NET.Serialization.MemoryPack` 提供，文档与代码均以 MemoryPack 为准。
