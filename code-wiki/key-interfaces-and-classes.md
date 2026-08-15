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

调用方无需关心具体 Handler，按 Command 类型分发。`CommandDispatcher` 内部实现：
1. 从 `IServiceProvider` 获取匹配的 `ICommandHandler<,>`
2. 按顺序执行 `IDispatchBehavior` 管道（验证、日志、事务等）
3. 调用 `HandleAsync` 并返回结果

### 1.5 `IQuery<TResult>` 与 `IQueryDispatcher`

与 Command 对称的读操作抽象，模式完全相同，区别仅在于语义意图（只读 vs 可写）和默认不开启事务。

---

## 2. `IFullNetModule` 模块入口

> 文件：[`src/BuildingBlocks/Full.NET.Modularity/Modules/IFullNetModule.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Modularity/Modules/IFullNetModule.cs)

```csharp
public interface IFullNetModule
{
    string Name { get; }                                       // 稳定唯一模块键（发布后不改）
    IReadOnlyCollection<string> Dependencies { get; }          // 依赖的模块键（DAG 排序用）

    void AddServices(IServiceCollection services, IConfiguration configuration);         // API 宿主完整注册
    void AddMigrationServices(IServiceCollection services, IConfiguration configuration); // Migrator 最小闭包
    void AddBackgroundServices(IServiceCollection services, IConfiguration configuration); // Worker 后台能力
    void MapEndpoints(IEndpointRouteBuilder endpoints);      // HTTP Endpoint 映射
    void UseModuleMiddleware(IApplicationBuilder app, ModulePipelineStage stage);         // 管道阶段贡献中间件
}
```

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

```csharp
public interface ICommandExecutor
{
    // 执行无返回 SQL（INSERT/UPDATE/DELETE）
    Task<int> ExecuteAsync(
        SqlStatement statement,
        object? param = null,
        CancellationToken cancellationToken = default);

    // 执行插入并返回生成的标识
    Task<TIdentity> ExecuteScalarAsync<TIdentity>(
        SqlStatement statement,
        object? param = null,
        CancellationToken cancellationToken = default);
}

public interface IQueryExecutor
{
    Task<IReadOnlyList<T>> QueryAsync<T>(
        SqlStatement statement,
        object? param = null,
        CancellationToken cancellationToken = default);

    Task<T?> QueryFirstOrDefaultAsync<T>(
        SqlStatement statement,
        object? param = null,
        CancellationToken cancellationToken = default);

    Task<T> QuerySingleAsync<T>(...);
}
```

### 3.2 `SqlStatement` + `SqlScopeGuard` 安全边界

`SqlStatement` 携带显式的 `SqlDataScope` 和 `SqlTenantBinding` 元数据，执行前由 `SqlScopeGuard` 强制校验：

```csharp
// 定义语句（通常在 Persistence/*Sql.cs 中作为静态常量）
public static readonly SqlStatement GetUserById = new(
    Sql: """
        SELECT Id, Username, PasswordHash, SecurityStamp, IsEnabled, TenantId
        FROM fn_identity_user
        WHERE Id = @Id
        """,
    DataScope: SqlDataScope.TenantRequired,  // 必须在租户上下文执行
    TenantBinding: SqlTenantBinding.CurrentTenantId,  // 自动注入 @TenantId
    StatementName: "identity.get_user_by_id");

// 实际执行前 SqlScopeGuard 会验证：
// 1. DataScope == TenantRequired 时，CurrentTenant 不能是 Host
// 2. 自动在参数中添加 @TenantId = CurrentTenant.TenantId
// 3. 若 SQL 自身不带 AND TenantId = @TenantId，测试会失败（架构测试门禁）
```

### 3.3 `ICommandTransaction` 事务

```csharp
public interface ICommandTransaction : IAsyncDisposable
{
    Task BeginAsync(CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
```

**事务行为**：
- 开启：分配连接 + `BEGIN TRANSACTION`
- 提交：`COMMIT` + 释放连接（或归还池）
- 异常：自动 `ROLLBACK`（`DisposeAsync` 保证）
- 事务内可以多次 `ICommandExecutor.ExecuteAsync()`，全部在同一连接 + 事务内

---

## 4. 统一结果模型

### 4.1 `Result<T>` 与 `Result`

> 文件：[`src/BuildingBlocks/Full.NET.Abstractions/Results/Result.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Abstractions/Results/Result.cs)

```csharp
// 成功/失败的统一包装
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }         // IsSuccess=true 时有值
    public Error? Error { get; }     // IsSuccess=false 时有值

    public static Result<T> Success(T value) => new(true, value, default);
    public static Result<T> Failure(Error error) => new(false, default, error);
}

public class Result  // 无返回值版本
{
    public bool IsSuccess { get; }
    public Error? Error { get; }
}
```

### 4.2 `Error` 结构化错误

```csharp
public class Error
{
    public ErrorType Type { get; }     // Validation / Authorization / NotFound / Conflict / ...
    public string Code { get; }        // 稳定机器码："module.area.reason"（zh-CN/en-US 都不变）
    public string Message { get; }     // 已本地化的人类可读消息
    public string? TraceId { get; }    // 关联 Trace
    public IReadOnlyList<ValidationViolation>? Violations { get; }  // 校验违规列表
}
```

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

### 5.1 `ICurrentTenant` 当前租户

```csharp
public interface ICurrentTenant
{
    bool IsAvailable { get; }         // 是否已解析
    bool IsHost { get; }              // 是否为宿主作用域（TenantId = NULL）
    Guid? TenantId { get; }           // 当前租户 ID（Host 时为 NULL）
    string Name { get; }              // 租户显示名
}
```

### 5.2 `TenantContext` 显式切换

```csharp
public class TenantContext : IDisposable
{
    // 在 using 块内临时切换到目标租户（需授权）
    public static IDisposable Push(Guid tenantId, string name);
    public static IDisposable EnterHostScope();  // 临时进入宿主
    public void Dispose();  // 恢复之前的状态
}
```

---

## 6. Outbox / Inbox 核心接口

### 6.1 `IOutboxWriter` 业务侧写入

```csharp
public interface IOutboxWriter
{
    // 与业务数据同事务原子写入（调用方保证同一 DbSession + Transaction）
    Task AppendAsync(
        OutboxEnvelope envelope,
        IntegrationEventMetadata metadata,
        CancellationToken cancellationToken = default);
}
```

### 6.2 `OutboxEnvelope` 事件信封

```csharp
public record OutboxEnvelope(
    Guid MessageId,              // 稳定幂等 ID（UUID v7）
    string MessageType,          // "fullnet.tenancy.tenant.provisioned"
    int SchemaVersion,           // 正整数版本（单调递增）
    Guid? TenantId,              // 租户边界（NULL = Host）
    string ContentType,          // "application/x-messagepack"
    byte[] Payload,              // MessagePack 序列化的业务事件
    DateTimeOffset OccurredAtUtc // 业务发生时间
);
```

### 6.3 `IIntegrationEventInbox` 消费端去重

```csharp
public interface IIntegrationEventInbox
{
    // 前置检查：返回 Duplicate / New / Poisoned
    Task<InboxPrecheck> PrecheckAsync(
        string consumerName,
        Guid messageId,
        CancellationToken cancellationToken = default);

    // 标记完成（与业务写入同一事务）
    Task<InboxConsumeResult> MarkConsumedAsync(
        string consumerName,
        Guid messageId,
        int handlerVersion,
        CancellationToken cancellationToken = default);
}
```

---

## 7. 缓存抽象

### 7.1 `CacheKeyBuilder` 统一键构造

```csharp
public static class CacheKeyBuilder
{
    // fullnet:{environment}:{tenant_or_host}:{module}:{resource}:{id}:{version}
    public static string Build(
        string module,
        string resource,
        string? id = null,
        string? version = null);
    // 示例结果："fullnet:prod:tenant_abc:identity:user:profile:42:v1"
}
```

### 7.2 `ICachePolicyRegistry` 策略注册表

避免在各处硬编码 TTL/一致性等级，集中管理：

```csharp
public interface ICachePolicyRegistry
{
    CacheEntryPolicy? GetPolicy(string cacheKeyPrefix);
    void Register(string prefix, Action<CacheEntryPolicyOptions> configure);
}

// 启动时注册
services.AddFullNetCaching()
        .AddPolicy("fullnet:*:tenancy:tenant:*", opt => {
            opt.AbsoluteExpiration = TimeSpan.FromMinutes(30);
            opt.Consistency = CacheConsistencyClass.Eventual;
            opt.SlidingExpiration = TimeSpan.FromMinutes(5);
        });
```

---

## 8. 种子数据抽象

### 8.1 `IDataSeedContributor`

```csharp
public interface IDataSeedContributor
{
    string Name { get; }
    IReadOnlyCollection<string> Dependencies { get; }  // 其他 Contributor 名称
    SeedProfile ApplicableProfiles { get; }  // Baseline / Development / Test / Demo

    Task<SeedContributionResult> ContributeAsync(
        SeedContext context,
        CancellationToken cancellationToken = default);
}
```

### 8.2 `SeedProfile` 确定性继承

```text
Baseline (生产)
  └── Development (本地开发：Baseline + 本地租户 + 测试用户)
        └── Test (CI 测试：Development + 测试夹具数据)
              └── Demo (演示：Test + 示例业务数据)
```
