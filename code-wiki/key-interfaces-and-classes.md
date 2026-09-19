# 关键接口与类详解

## 1. CQRS 消息抽象

### 1.1 `ICommand<TResult>` 命令接口

> 命名空间：`Full.NET.Abstractions.Messaging`
> 文件：[`src/BuildingBlocks/Full.NET.Abstractions/Messaging/ICommand.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Abstractions/Messaging/ICommand.cs)

写操作标记接口。空接口用于泛型约束和 DI 扫描：

```csharp
public interface ICommand<TResult>;

// 例：创建用户命令
public record CreateUserCommand(
    string Username,
    string Password,
    string DisplayName
) : ICommand<Guid>;
```

### 1.2 `ITransactionalCommand` 事务命令标记

继承 `ICommand<TResult>` 的命令若同时实现此标记接口，分发器会自动开启 `ICommandTransaction`：

```csharp
public interface ITransactionalCommand;
public interface ITransactionalCommand<TResult> : ICommand<TResult>, ITransactionalCommand;
```

### 1.3 `ICommandHandler<TCommand, TResult>` 处理器

```csharp
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<Result<TResult>> HandleAsync(
        TCommand command,
        CancellationToken cancellationToken);
}
```

**Handler 设计规范**：
- 一个 Handler 只处理一个 Command 类型（单一职责）
- 返回 `Result<T>`，不抛业务异常（技术异常除外）
- 所有方法接受 `CancellationToken` 并正确传播
- 通过 DI 注入依赖（DbSession、Outbox、其他 Port）

### 1.4 `ICommandDispatcher` 分发器

```csharp
public interface ICommandDispatcher
{
    Task<Result<TResult>> SendAsync<TCommand, TResult>(
        TCommand command,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>;
}
```

调用方无需关心具体 Handler，按 Command 类型分发。[`CommandDispatcher`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Modularity/Messaging/CommandDispatcher.cs) 内部实现：
1. 从 `IServiceProvider` 获取匹配的 `ICommandHandler<,>`
2. 按注册顺序逆序串联 `IDispatchBehavior<TCommand, TResult>` 形成俄罗斯套娃式管道
3. 对标记 `ITransactionalCommand` 的命令，自动包裹在 `ICommandTransaction` 事务中执行（无注册事务组件时抛 `InvalidOperationException`）
4. 调用 `HandleAsync` 并返回结果

### 1.5 `IQuery<TResult>` 与 `IQueryDispatcher`

与 Command 对称的读操作抽象，模式完全相同，区别仅在于语义意图（只读 vs 可写）和默认不开启事务。文件：[`IQuery.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Abstractions/Messaging/IQuery.cs) 与 [`QueryDispatcher.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Modularity/Messaging/QueryDispatcher.cs)。`QueryDispatcher` 共享相同的 Behavior 管道机制，但不参与数据库事务。

### 1.6 `IDispatchBehavior<TMessage, TResult>` 管道行为

> 文件：[`src/BuildingBlocks/Full.NET.Abstractions/Messaging/IDispatchBehavior.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Abstractions/Messaging/IDispatchBehavior.cs)

```csharp
public delegate Task<Result<TResult>> DispatchHandlerDelegate<TResult>(CancellationToken cancellationToken);

public interface IDispatchBehavior<in TMessage, TResult>
{
    Task<Result<TResult>> HandleAsync(
        TMessage message,
        DispatchHandlerDelegate<TResult> next,
        CancellationToken cancellationToken);
}
```

Behavior 按注册顺序逆序嵌套（最外层先执行），未调用 `next` 表示短路管道。典型实现包括日志、参数校验、事务包装、权限检查、缓存、性能度量与重试策略。`Full.NET.Validation.FluentValidation` 通过 `FluentValidationBehavior` 自动注入校验 Behavior。

---

## 2. `IFullNetModule` 模块入口

> 文件：[`src/BuildingBlocks/Full.NET.Modularity/Modules/IFullNetModule.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Modularity/Modules/IFullNetModule.cs)

```csharp
public interface IFullNetModule
{
    string Name { get; }                                       // 稳定唯一模块键（发布后不改）
    IReadOnlyCollection<string> Dependencies { get; }          // 依赖的模块键（DAG 排序用）
    IReadOnlyCollection<string> OptionalContractDependencies => []; // 可选消费事件/只读契约模块键

    void AddServices(IServiceCollection services, IConfiguration configuration);         // API 宿主完整注册
    void AddMigrationServices(IServiceCollection services, IConfiguration configuration) { } // Migrator 最小闭包（默认实现）
    void MapEndpoints(IEndpointRouteBuilder endpoints);      // HTTP Endpoint 映射
    void AddBackgroundServices(IServiceCollection services, IConfiguration configuration) { } // Worker 后台能力（默认实现）
    void UseModuleMiddleware(IApplicationBuilder app, ModulePipelineStage stage) { }      // 管道阶段贡献中间件（默认实现）
}
```

**OptionalContractDependencies 关键约束**：仅用于消费事件或最小只读契约；不能用于同步调用、数据库访问或服务解析，不参与启用集依赖闭包；只有缺少对方模块时仍能安全退化为无事件输入的集成消费者才可登记。`FullNetModuleSelection` 启动校验时检查声明的可选依赖必须在 `OfficialModuleNames` 集合内，且不能与强依赖 `Dependencies` 重复。

### 模块实现类示例

```csharp
// IdentityModule.cs
public class IdentityModule : IFullNetModule
{
    public string Name => "Identity";

    public IReadOnlyCollection<string> Dependencies =>
        new[] { "Tenancy", "Settings" };  // 按稳定键声明依赖

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdentityAuthentication(configuration);
        services.AddIdentityAuthorization(configuration);
        services.AddIdentityDomainServices();
    }

    public void AddMigrationServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSeedContributor<HostAdministratorSeedContributor>();
        services.AddSeedContributor<HostNavigationCatalogSeedContributor>();
    }

    public void AddBackgroundServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOrganizationUnitProjectionServices();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapIdentityAuthEndpoints();     // 登录/登出/刷新
        endpoints.MapIdentityHostUserEndpoints();  // 宿主用户管理
        // ...
    }
}
```

---

## 3. 数据访问执行接口

### 3.1 `ICommandExecutor` / `IQueryExecutor`

> 命名空间：`Full.NET.Data.Abstractions`
> 文件：[`ICommandExecutor.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Abstractions/ICommandExecutor.cs) / [`IQueryExecutor.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Abstractions/IQueryExecutor.cs)

```csharp
public interface ICommandExecutor
{
    // 执行写命令并返回受影响行数；INSERT/UPDATE/DELETE/DDL
    Task<int> ExecuteAsync(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default);
}

public interface IQueryExecutor
{
    // 查询期望返回 0 或 1 行；超过 1 行抛 InvalidOperationException
    Task<T?> QuerySingleOrDefaultAsync<T>(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default);

    // 物化全部行
    Task<IReadOnlyList<T>> QueryAsync<T>(
        SqlStatement statement,
        object? parameters = null,
        CancellationToken cancellationToken = default);
}
```

`ICommandExecutor` / `IQueryExecutor` 不直接管理事务；显式事务通过 `ICommandTransaction` 协调器包裹。多结果集（JOIN 拆分的一对多投影）使用 `IMultiResultQueryExecutor` + `IMultiResultReader`。

### 3.2 `SqlStatement` + `SqlScopeGuard` 安全边界

`SqlStatement` 是不可变 record，按位置参数 `(Name, Text, Scope, TenantBinding)` 构造；执行前由 `SqlScopeGuard` 强制校验：

```csharp
// 定义语句（通常在 Persistence/*Sql.cs 中作为静态常量）
public static readonly SqlStatement GetUserById = new(
    Name: "identity.get_user_by_id",
    Text: """
        SELECT Id, Username, PasswordHash, SecurityStamp, IsEnabled, TenantId
        FROM fn_identity_user
        WHERE Id = @Id
        """,
    Scope: SqlDataScope.TenantRequired,
    TenantBinding: SqlTenantBinding.CurrentTenantId);

// 实际执行前 SqlScopeGuard.Validate 会同步校验：
// 1. Scope == TenantRequired 时，CurrentTenant 必须 IsAvailable 且非 Host 且 Id 非空
// 2. TenantBinding 必须 == CurrentTenantId
// 3. SQL 文本必须在 WHERE/JOIN ON/INSERT VALUES 中出现 @TenantId 等值比较
//    （轻量词法 TenantSqlValidation 缓存校验结果，防注释/字符串绕过）
// 4. Scope == HostOnly 时，必须在 Host 上下文；TenantBinding 必须 == None
// 5. Scope == Global 时，TenantBinding 必须 == None
// 违反时抛 TenantContextMissingException / TenantScopeViolationException / HostContextRequiredException
```

`SqlStatement` 还提供 `(Name, Text, Scope)` 三参构造（默认 `TenantBinding.None`，用于 Global/HostOnly）与三元 `Deconstruct` 解构器。

### 3.3 `ICommandTransaction` 事务

> 文件：[`src/BuildingBlocks/Full.NET.Abstractions/Messaging/ICommandTransaction.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Abstractions/Messaging/ICommandTransaction.cs)

```csharp
public interface ICommandTransaction
{
    // 在当前作用域事务内执行操作并返回结果
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken);

    // 在事务内执行返回 Result<T> 的操作，保持失败传播语义
    Task<Result<T>> ExecuteResultAsync<T>(
        Func<CancellationToken, Task<Result<T>>> action,
        CancellationToken cancellationToken)
        => ExecuteAsync(action, cancellationToken);
}
```

**事务行为**（`CommandDispatcher` 对 `ITransactionalCommand` 自动调用）：
- 进入：打开连接并 `BEGIN TRANSACTION`（通常 ReadCommitted）
- 成功：`COMMIT` 并归还连接
- 异常：自动 `ROLLBACK`，连接归还池
- 事务内可多次 `ICommandExecutor.ExecuteAsync()`，全部在同一连接 + 事务内
- Outbox 写入必须包裹在同一 `ICommandTransaction` 中，保证「业务写 + 事件追加」原子提交

---

## 4. 统一结果模型

### 4.1 `Result<T>` 与 `Result`

> 文件：[`src/BuildingBlocks/Full.NET.Abstractions/Results/Result.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Abstractions/Results/Result.cs)

```csharp
// 成功/失败的统一包装
public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }         // IsSuccess=true 时有值
    public Error? Error { get; }     // IsSuccess=false 时有值

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(Error error) => new(false, default, error);
}
```

### 4.2 `Error` 结构化错误

> 文件：[`src/BuildingBlocks/Full.NET.Abstractions/Results/Error.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Abstractions/Results/Error.cs)

```csharp
public sealed record Error
{
    [JsonConstructor]
    public Error(
        string Code,                 // 稳定机器码："module.area.reason"（zh-CN/en-US 都不变）
        string Message,              // 兼容调用方的安全默认消息（DefaultMessage 别名）
        ErrorType Type,              // Validation / Authorization / NotFound / Conflict / ...
        IReadOnlyDictionary<string, string[]>? ValidationErrors = null); // 兼容旧客户端的字段消息

    public Error(...,
        IReadOnlyDictionary<string, object?>? Arguments,             // 命名格式化参数
        IReadOnlyList<ValidationViolation>? ValidationViolations);   // 结构化字段验证违反项

    public string Code { get; init; }
    public string Message { get; init; }
    public ErrorType Type { get; init; }
    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; init; }
    public IReadOnlyDictionary<string, object?>? Arguments { get; init; }
    public IReadOnlyList<ValidationViolation>? ValidationViolations { get; init; }

    [JsonIgnore]
    public string DefaultMessage => Message;  // 资源缺失或格式化失败时的安全回退
}
```

`Code / Type / Arguments / ValidationViolations` 属于机器契约，不得随显示语言改变；`Message` 仅作为安全回退默认值，本地化由 `IErrorMessageLocalizer` 在传输层格式化。

### 4.3 `PagedResult<T>` 分页结果

```csharp
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; }
    public long TotalCount { get; }
    public int PageNumber { get; }
    public int PageSize { get; }
    public bool HasNextPage => PageNumber * PageSize < TotalCount;
}
```

---

## 5. 多租户抽象

### 5.1 `ICurrentTenant` 当前租户（只读）

> 文件：[`src/BuildingBlocks/Full.NET.Abstractions/Tenancy/ICurrentTenant.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Abstractions/Tenancy/ICurrentTenant.cs)

```csharp
public interface ICurrentTenant
{
    bool IsAvailable { get; }     // 已解析（含 Host 与具体租户）
    bool IsHost { get; }          // 是否为宿主作用域（Id 为 null）
    Guid? Id { get; }             // 当前租户 ID；Host 或未解析时为 null
    string? Identifier { get; }   // 可读标识（域名前缀/短代码），未解析为 null
    string? Name { get; }         // 显示名，未解析为 null
}
```

### 5.2 `ICurrentTenantContextWriter` 受限写能力

> 文件：[`src/BuildingBlocks/Full.NET.Abstractions/Tenancy/ICurrentTenantContextWriter.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Abstractions/Tenancy/ICurrentTenantContextWriter.cs)

普通业务处理器只应依赖 `ICurrentTenant`，不得根据请求输入直接切换上下文。`ICurrentTenantContextWriter` 显式实现于 `CurrentTenantAccessor`，只有经审查的基础设施（解析中间件、后台任务、Migrator、跨租户编排）才能解析该写接口：

```csharp
public interface ICurrentTenantContextWriter : ICurrentTenant
{
    void SetTenant(TenantContext tenant);  // 绑定到已验证租户上下文，清除 Host 标记
    void SetHost();                         // 切换到 Host 上下文
    void Clear();                           // 清除租户与 Host 状态，回到未解析
}
```

### 5.3 `TenantContext` 已解析的不可变上下文

```csharp
public sealed record TenantContext(
    Guid Id,            // 稳定唯一标识
    string Identifier,  // 可读标识（域名前缀/短代码/外部编号）
    string Name);       // 显示名
```

`TenantContext` 只代表一次成功的租户解析，创建后字段不可为空；Host 级别操作不使用该类型，而是通过 `ICurrentTenant.IsHost` 判定。

---

## 6. Outbox / Inbox 核心接口

### 6.1 `IOutboxWriter` 业务侧写入

> 文件：[`src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxWriter.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxWriter.cs)

```csharp
public interface IOutboxWriter
{
    // 在当前事务内追加事件，使用从 Ambient 上下文自动提取的元数据
    Task AddAsync<TEvent>(
        string eventType,           // 规范化事件类型 "fullnet.organization.unit.changed"
        int schemaVersion,          // 结构版本正整数
        TEvent payload,             // 由 IIntegrationEventSerializer 序列化
        CancellationToken cancellationToken = default);

    // 显式覆盖元数据；仅限 Host 级网关场景（Webhook/Saga/历史回填），
    // 其中 TenantId 为 null 时仍从上下文自动注入
    Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        IntegrationEventMetadata metadata,
        CancellationToken cancellationToken = default);
}
```

**安全不变量**：必须在已打开的本地命令事务内执行；事件顺序严格等于调用顺序；`TenantId / TraceId / OccurredAtUtc` 由实现自动从 Ambient 提取，调用方不得覆盖。

### 6.2 `OutboxEnvelope` 事件信封

> 文件：[`src/BuildingBlocks/Full.NET.Data.Abstractions/OutboxEnvelope.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Abstractions/OutboxEnvelope.cs)

```csharp
public sealed record OutboxEnvelope(
    Guid Id,                      // 事件 UUID v7，同时是 Broker MessageId 与 Inbox 幂等键
    Guid LockId,                  // 当前领取批次的租约标识；未领取为 Guid.Empty
    string MessageType,           // 路由键 "fullnet.organization.unit.changed"
    int SchemaVersion,            // 事件结构版本正整数，单调递增
    string ContentType,           // 线格式 "application/json;charset=utf-8"
    Guid? TenantId,               // 租户边界；Host 级全局事件为 null
    string? TraceId,              // W3C trace-id (32 hex)；null 时 Relay 生成新 TraceId
    byte[] Payload,               // 载荷字节（≤1 MiB，大载荷走对象存储引用）
    int Attempts,                 // 已尝试发布次数，从 0 开始；超 MaxAttempts 进死信
    DateTimeOffset OccurredAtUtc // 业务发生 UTC 时间，由 DB 服务器时钟保证单调
    // 派生字段（init）：DueRetryCount / ActiveLeaseCount / DeadLetterCount / OldestDeadLetteredAtUtc
);
```

### 6.3 `IOutboxStore` 领取与状态更新

> 文件：[`src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxStore.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxStore.cs)

```csharp
public interface IOutboxStore
{
    // 领取一批待处理消息，写入有限期租约；同批次共享 LockId
    Task<IReadOnlyList<OutboxEnvelope>> AcquireAsync(int batchSize, TimeSpan lease, CancellationToken ct);
    // 延长未进入终态的消息租约
    Task RenewLeaseAsync(IReadOnlyCollection<Guid> messageIds, Guid lockId, TimeSpan lease, CancellationToken ct);
    // 成功完成 → 释放租约
    Task MarkProcessedAsync(Guid id, Guid lockId, CancellationToken ct);
    // 临时失败 → 释放回队列，记录下次允许重试时间
    Task MarkFailedAsync(Guid id, Guid lockId, string error, DateTimeOffset nextAttemptAt, CancellationToken ct);
    // 永久失败 → 写入死信终态，释放租约，保留 Payload 与原因码供审计与重放
    Task MarkDeadLetterAsync(Guid id, Guid lockId, string error, string deadLetterReasonCode, DateTimeOffset deadLetteredAt, CancellationToken ct);
}
```

异常：`OutboxConcurrencyException`（单条 LockId 不再归属）、`OutboxLeaseLostException`（整批次租约丢失）。稳定死信原因码常量集合 `OutboxDeadLetterReasons`：`UnsupportedContentType / HandlerNotFound / AmbiguousHandler / InvalidPayload / MaxAttemptsExceeded / LegacyOwnerRevoked`。

### 6.4 `IOutboxBacklogReader` 积压只读快照

> 文件：[`src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxBacklogReader.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxBacklogReader.cs)

不改变租约和消息状态，只读快照用于运维监控、切流门禁、版本退役判定：

```csharp
public interface IOutboxBacklogReader
{
    // 全量未处理且未死信消息的积压快照
    Task<OutboxBacklogSnapshot> ReadBacklogAsync(CancellationToken ct);
    // 指定事件流 (EventType, SchemaVersion) 的积压快照
    Task<OutboxBacklogSnapshot> ReadStreamBacklogAsync(string eventType, int schemaVersion, CancellationToken ct);
    // 旧 Outbox 中该事件流最后写入事件边界，用于所有权切换时固定 CDC 起点
    Task<OutboxStreamCutoffSnapshot?> ReadLastStreamEventAsync(string eventType, int schemaVersion, CancellationToken ct);
    // 按 Handler 声明的 (canonical + legacy) 类型集合与 SchemaVersion 读取版本退役快照
    Task<OutboxVersionRetirementSnapshot> ReadVersionRetirementAsync(IReadOnlyCollection<string> messageTypes, int schemaVersion, CancellationToken ct);
}
```

`OutboxBacklogSnapshot` 含 `PendingCount / OldestOccurredAtUtc / DueRetryCount / ActiveLeaseCount / DeadLetterCount / OldestDeadLetteredAtUtc`；`OutboxVersionRetirementSnapshot` 含 `PendingCount / DeadLetterCount / OldestUnprocessedOccurredAtUtc`。

### 6.5 `IOutboxRetentionStore` 保留清理

> 文件：[`src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxRetentionStore.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Abstractions/IOutboxRetentionStore.cs)

```csharp
public interface IOutboxRetentionStore
{
    // 删除严格早于 cutoffUtc 的成功终态消息；待处理/重试/租约/死信必须保留
    Task<int> DeleteProcessedBatchAsync(DateTimeOffset cutoffUtc, int batchSize, CancellationToken ct);
}
```

### 6.6 `IIntegrationEventInbox` 消费端去重

> 文件：[`src/BuildingBlocks/Full.NET.Data.Abstractions/IIntegrationEventInbox.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Data.Abstractions/IIntegrationEventInbox.cs)

所有写入必须在调用方已打开的本地命令事务内执行：

```csharp
public interface IIntegrationEventInbox
{
    // 一次只读查询预检最多 100 条消息；返回的 Unknown 仍需进入 Claim 事务，不视为已取得处理权
    Task<IReadOnlyList<InboxPrecheckResult>> PrecheckBatchAsync(
        string consumerName,
        IReadOnlyList<InboxMessageFingerprint> messages,
        CancellationToken ct);

    // 在当前事务内声明 consumerName 对 Envelope 的处理权
    //   AlreadyProcessed：同 MessageId 且 PayloadHash 一致，可跳过 Handler
    //   PayloadMismatch：同 MessageId 但 SHA-256 不同，契约冲突，Dispatcher 拒绝消费
    Task<InboxClaimResult> ClaimAsync(string consumerName, IntegrationEventEnvelope envelope, CancellationToken ct);

    // 标记已声明的处理行为 processed；要求当前状态为 processing
    Task MarkProcessedAsync(string consumerName, Guid messageId, CancellationToken ct);
}
```

`InboxMessageFingerprint` 必须包含 32 字节 SHA-256 `PayloadHash`；`InboxPrecheckResult` 返回 `Unknown / AlreadyProcessed / PayloadMismatch`。

---

## 7. 缓存抽象

### 7.1 `CacheKeyBuilder` 统一键构造

> 文件：[`src/BuildingBlocks/Full.NET.Caching.Fusion/CacheKeyBuilder.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Caching.Fusion/CacheKeyBuilder.cs)

7 段格式 `fullnet:{env}:{scope}:{module}:{res}:{id}:{ver}`，`scope` 段区分 `host` 与具体 `TenantId`，禁止业务模块自行拼接：

```csharp
public static class CacheKeyBuilder
{
    // 租户隔离键（scope = tenantId）
    public static string ForTenant(string environment, Guid tenantId, string module, string resource, object id, string version);
    // 宿主级全局键（scope = host）
    public static string ForGlobal(string environment, string module, string resource, object id, string version);
    // 域名解析的全局键
    public static string TenantResolutionByDomain(string environment, string domain);
    // 租户 ID 解析的全局键
    public static string TenantResolutionById(string environment, Guid tenantId);
    // 按租户批量失效标签
    public static string TenantTag(Guid tenantId);
    // 域名解析类缓存失效标签
    public static string DomainTag(string domain);
}
// 示例："fullnet:prod:host:tenancy:domain:example.com:v1"
//       "fullnet:prod:00000000-...:identity:user:profile:42:v1"
```

### 7.2 `ICachePolicyRegistry` 策略注册表

> 文件：[`src/BuildingBlocks/Full.NET.Caching.Fusion/ICachePolicyRegistry.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Caching.Fusion/ICachePolicyRegistry.cs)

避免在各处硬编码 TTL/一致性等级，按稳定 `entryName` 集中查询策略：

```csharp
public interface ICachePolicyRegistry
{
    CacheEntryPolicy GetRequired(string entryName);                      // 未知条目必须抛错
    IReadOnlyList<CacheEntryPolicy> ListPolicies();                      // 列出全部策略供管理控制面
    CacheAccessDecision ResolveAccess(string entryName);                 // C0/N0 路径分别返回 AuthorityRead/Bypass
    FusionCacheEntryOptions CreateEntryOptions(string entryName);        // C0/N0 抛错
    HybridCacheEntryOptions CreateHybridEntryOptions(string entryName, CacheEntryLifetime lifetime = Normal);
}
```

策略在 `CacheOptions.Entries` 配置下注册，`CachePolicyRegistry.Create(options)` 合并内置默认条目（`TenantResolution / DiagnosticPolicy / GridPreference`）。

### 7.3 `ICacheInvalidator` 失效边界

> 文件：[`src/BuildingBlocks/Full.NET.Caching.Abstractions/ICacheInvalidator.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Caching.Abstractions/ICacheInvalidator.cs)

业务模块提供稳定 `entryName` 与已隔离的键或标签，Provider 负责映射具体缓存选项：

```csharp
public interface ICacheInvalidator
{
    ValueTask RemoveAsync(string entryName, string key, CacheInvalidationScope scope, CancellationToken ct = default);
    ValueTask RemoveByTagAsync(string entryName, string tag, CacheInvalidationScope scope, CancellationToken ct = default);
}
```

`CacheInvalidationScope`：`Local`（本机 L1）与 `AllLevels`（L1 + L2 + Backplane 广播）。

---

## 8. 种子数据抽象

### 8.1 `IDataSeedContributor`

> 文件：[`src/BuildingBlocks/Full.NET.Seeding.Abstractions/IDataSeedContributor.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Seeding.Abstractions/IDataSeedContributor.cs)

```csharp
public interface IDataSeedContributor
{
    string Name { get; }                                  // 发布后保持稳定的小写点分名称
    int Version { get; }                                  // 从 1 开始的 Contributor 数据契约版本
    IReadOnlySet<SeedProfile> Profiles { get; }           // 直接所属的 Profile 层集合
    IReadOnlyCollection<string> Dependencies { get; }     // 必须先成功执行的 Contributor 稳定名称

    // 幂等协调模块数据并返回不包含 Secret 的执行计数
    Task<SeedContributionResult> SeedAsync(
        SeedContext context,
        CancellationToken cancellationToken = default);
}
```

实现必须在自有边界内完成幂等协调：使用稳定自然键检查真实状态后再决定新建/更新/跳过，不得删除已有用户修改、重置密码或覆盖审计历史。Contributor 在独立事务内写入自身数据，不应跨 Contributor 共享本地事务；并发执行由编排器通过租约与依赖图保证。

### 8.2 `SeedProfile` 确定性继承

> 文件：[`src/BuildingBlocks/Full.NET.Seeding.Abstractions/SeedProfile.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Seeding.Abstractions/SeedProfile.cs)

```text
                    Baseline (生产根，所有环境必须包含)
                       /      |        \
            Development   Demo            Test
        (Baseline + 本地租户 +    (Baseline +    (Baseline + 自动化夹具，
         测试用户，仅开发叠加)    演示数据)      仅测试/Sample 程序集，不进发布物)
```

继承关系由 `SeedProfileNames.EffectiveLayers` 封闭表达：禁止运行时组合未声明的 Profile 集合，也禁止把开发/演示数据改名后放入 Baseline 绕过生产门禁。Overlay 层 Contributor 不得删除数据、重置密码或覆盖用户修改。Production 环境只允许执行 Baseline。

---

## 9. 集成事件订阅与所有权

### 9.1 `IIntegrationEventHandler` Handler 契约

> 文件：[`src/BuildingBlocks/Full.NET.Abstractions/Messaging/IIntegrationEventHandler.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Abstractions/Messaging/IIntegrationEventHandler.cs)

```csharp
public interface IIntegrationEventHandler
{
    string EventType { get; }                                      // 规范消息类型
    IReadOnlyList<string> LegacyEventTypes => [];                   // 迁移窗口内仍可消费的历史类型
    int SchemaVersion { get; }                                      // 载荷 Schema 版本
    IntegrationEventIdempotencyStrategy IdempotencyStrategy =>     // 默认 Unspecified，Worker 启动会拒绝
        IntegrationEventIdempotencyStrategy.Unspecified;

    // 使用完整消息上下文处理载荷（旧实现默认转发至 payload-only 重载）
    Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken ct)
        => HandleAsync(payload, ct);

    // 处理原始 MemoryPack 载荷（兼容尚未读取消息上下文的 Handler）
    Task HandleAsync(ReadOnlyMemory<byte> payload, CancellationToken ct);
}
```

`IntegrationEventIdempotencyStrategy`：`Unspecified`（启动拒绝）/ `NaturallyIdempotent`（重复执行收敛到相同业务状态）/ `MessageIdDeduplication`（按稳定 MessageId 在副作用提交边界持久化去重）。

### 9.2 `IntegrationEventContext` 投递上下文

```csharp
public sealed record IntegrationEventContext(
    Guid MessageId,           // Outbox 持久化消息稳定标识
    string MessageType,       // 当前持久化记录使用的规范或兼容消息类型
    int SchemaVersion,        // 消息载荷 Schema 版本
    Guid? TenantId,            // 消息所属租户；Host 级消息为 null
    string? TraceId,          // 生产消息时捕获的追踪标识；不可用时为 null
    DateTimeOffset OccurredAtUtc);  // 业务事件发生并写入 Outbox 的 UTC 时间
```

### 9.3 `IIntegrationEventSubscription` 订阅身份

> 文件：[`src/BuildingBlocks/Full.NET.Messaging.Abstractions/IIntegrationEventSubscription.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Messaging.Abstractions/IIntegrationEventSubscription.cs)

```csharp
public interface IIntegrationEventSubscription
{
    string ConsumerName { get; }      // Consumer Group 稳定机器码
    string EventType { get; }          // 稳定事件类型名
    int SchemaVersion { get; }         // Schema 版本号，从 1 开始
    IntegrationEventIdempotencyStrategy IdempotencyStrategy { get; }

    Task HandleAsync(IntegrationEventContext context, ReadOnlyMemory<byte> payload, CancellationToken ct);
}
```

路由键 `(ConsumerName, EventType, SchemaVersion)` 在订阅目录中唯一。

### 9.4 `IIntegrationEventSubscriptionCatalog` 订阅目录

> 文件：[`src/BuildingBlocks/Full.NET.Messaging.Abstractions/IntegrationEventSubscriptionCatalog.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Messaging.Abstractions/IntegrationEventSubscriptionCatalog.cs)

Scoped 生命周期，与 Handler/Inbox 事务作用域一致；构造期校验 TopicCode 唯一、ConsumerName 唯一且合规、路由三元组唯一、订阅引用 Topic 存在、幂等策略可识别：

```csharp
public interface IIntegrationEventSubscriptionCatalog
{
    IIntegrationEventSubscription GetRequired(string consumerName, string eventType, int schemaVersion);
    IIntegrationEventSubscription GetByHandlerTypeRequired(Type handlerType);
    EventDeliveryOwner GetDeliveryOwner(string eventType, int schemaVersion);              // 目录默认所有权
    EventDeliveryOwner ResolveDeliveryOwner(string eventType, int schemaVersion, EventDeliveryOwner? persistedCurrentOwner); // 叠加切流记录
    IntegrationEventTopicDefinition GetTopicRequired(string eventType, int schemaVersion);
    IntegrationEventTopicDefinition GetTopicByCodeRequired(string topicCode);              // 范围重放禁止目录外 Topic
    IReadOnlyCollection<IIntegrationEventSubscription> GetAllSubscriptions();              // CdcKafka 启动守卫
}
```

### 9.5 `IEventStreamOwnershipGate` 事件流所有权互斥

> 文件：[`src/BuildingBlocks/Full.NET.Messaging.Abstractions/IEventStreamOwnershipGate.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Messaging.Abstractions/IEventStreamOwnershipGate.cs)

锁必须由数据库事务持有到提交或回滚，调用方不得提前释放。三种互斥角色对应：生产者写 Outbox、消费者读 Inbox、运维/管理端变更所有权：

```csharp
public interface IEventStreamOwnershipGate
{
    Task<bool> AcquireProducerAsync(string eventType, int schemaVersion, CancellationToken ct = default);
    Task<bool> AcquireConsumerAsync(string eventType, int schemaVersion, CancellationToken ct = default);
    Task<EventStreamConsumerFenceResult> AcquireConsumerFenceAsync(string eventType, int schemaVersion, CancellationToken ct = default)
        => Task.FromResult(EventStreamConsumerFenceResult.Unsupported);  // 默认不支持 Fence 优化
    Task<bool> AcquireOwnershipChangeAsync(string eventType, int schemaVersion, CancellationToken ct = default);
}
```

`EventStreamConsumerFenceResult`：`Unsupported` / `Missing` / `Acquired(owner)`，在同一次事务锁定查询中返回 Consumer 可见的当前 Owner。

### 9.6 `IKafkaConnectAdminClient` Connect REST 管理

> 文件：[`src/BuildingBlocks/Full.NET.Messaging.Abstractions/IKafkaConnectAdminClient.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Messaging.Abstractions/IKafkaConnectAdminClient.cs)

用于集成测试、容量 Runner 与回退控制面：

```csharp
public interface IKafkaConnectAdminClient : IDisposable
{
    Task<bool> WaitUntilReadyAsync(TimeSpan timeout, CancellationToken ct = default);
    Task RegisterConnectorAsync(string connectorName, IReadOnlyDictionary<string, string> config, CancellationToken ct = default);
    Task<bool> WaitForConnectorHealthyAsync(string connectorName, TimeSpan timeout, CancellationToken ct = default);
    Task DeleteConnectorAsync(string connectorName, CancellationToken ct = default);
    Task PauseConnectorAsync(string connectorName, CancellationToken ct = default);
    Task ResumeConnectorAsync(string connectorName, CancellationToken ct = default);
    Task<bool> IsConnectorPausedAsync(string connectorName, CancellationToken ct = default);
    Task<CdcDeliveryPosition?> TryReadConnectorPositionAsync(string connectorName, CancellationToken ct = default);
    Task<string?> TryGetConnectorStatusAsync(string connectorName, CancellationToken ct = default);
}
```

### 9.7 `IntegrationEventEnvelope` 跨边界事件信封

> 文件：[`src/BuildingBlocks/Full.NET.Messaging.Abstractions/IntegrationEventEnvelope.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Messaging.Abstractions/IntegrationEventEnvelope.cs)

跨 Outbox、CDC、Kafka 与 Inbox 保持不变的可靠集成事件 Envelope V2。所有构造路径通过 `Create` 工厂进行契约校验：

```csharp
public sealed class IntegrationEventEnvelope
{
    public Guid EventId { get; }                // 全局唯一标识；幂等去重/死信溯源/切流 Cutoff 锚点
    public string MessageType { get; }          // 稳定事件契约类型名
    public int SchemaVersion { get; }           // 从 1 开始单调递增
    public string ContentType { get; }          // 当前固定 "application/x-memorypack"
    public Guid? TenantId { get; }              // 租户边界；Host 级为 null
    public string PartitionKey { get; }         // Kafka 分区键
    public string? CorrelationId { get; }       // 跨服务请求追踪链路
    public Guid? CausationId { get; }           // 直接前序命令/事件 ID
    public string? TraceParent { get; }         // W3C traceparent
    public string Producer { get; }             // 生产者模块标识
    public DateTimeOffset OccurredAtUtc { get; } // 业务侧实际发生 UTC 时间
    public ReadOnlyMemory<byte> Payload { get; } // MemoryPack 载荷字节

    public static IntegrationEventEnvelope Create(...);  // 校验并构造
}
```

字段顺序、命名与语义一旦发布即视为长期契约，任何变更必须同步提升 `SchemaVersion`。
