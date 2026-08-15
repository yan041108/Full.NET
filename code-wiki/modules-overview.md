# 模块系统总览

## 1. 模块入口约定

每个业务模块实现 `IFullNetModule` 接口，提供统一的注册和端点映射入口。

```csharp
public interface IFullNetModule
{
    string Name { get; }                                       // 稳定唯一模块键
    IReadOnlyCollection<string> Dependencies { get; }          // 依赖的稳定模块键
    void AddServices(IServiceCollection services, IConfiguration configuration);
    void AddMigrationServices(IServiceCollection services, IConfiguration configuration);
    void AddBackgroundServices(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(IEndpointRouteBuilder endpoints);
    void UseModuleMiddleware(IApplicationBuilder app, ModulePipelineStage stage);
}
```

### 1.1 模块内部标准结构

```text
Full.NET.Modules.{ModuleName}/
├── {ModuleName}Module.cs           // IFullNetModule 入口实现
├── {ModuleName}AuthorizationContributor.cs  // 权限目录贡献者
├── Contracts/                      // 公开契约（DTO、权限、错误码、Integration Event）
│   ├── {ModuleName}ErrorCodes.cs
│   ├── Host{Feature}Contracts.cs
│   └── Host{Feature}Permissions.cs
├── Domain/                         // 领域实体、值对象、枚举
├── Features/                       // 垂直功能切片
│   ├── {UseCase}/
│   │   ├── Endpoint.cs             // 最小 API Endpoint 定义
│   │   ├── Command.cs              // 写命令
│   │   ├── Query.cs                // 读查询
│   │   ├── Handler.cs              // Command/Query Handler
│   │   ├── Validator.cs            // FluentValidation 校验器
│   │   └── {UseCase}Service.cs     // 领域服务（复杂逻辑抽离）
│   └── ...
├── Persistence/                    // 持久化实现
│   ├── Sql/                        // 显式 SQL 语句（按模块/功能组织）
│   │   ├── SqlServer/
│   │   └── MySql/
│   ├── Migrations/                 // DbUp 迁移脚本（若模块自带）
│   ├── {Entity}Record.cs           // Dapper 行投影类型（PascalCase 直接映射）
│   └── {Entity}Sql.cs              // 本模块 SQL Statement 常量
├── Security/                       // 安全类（密码哈希、签名器、Token 保护器）
├── RateLimiting/                   // 模块特有策略
├── DataScope/                      // 数据范围过滤
├── Seeding/                        // IDataSeedContributor 实现
├── Resources/                      // 本地化错误资源
│   ├── {ModuleName}ErrorResourceSource.cs
│   ├── {ModuleName}Errors.resx
│   └── {ModuleName}Errors.en-US.resx
└── Serialization/                  // 模块专属 JSON/MessagePack 序列化上下文
```

---

## 2. 模块注册机制

### 2.1 显式注册（无程序集扫描）

**Composition 组合根**是唯一可引用具体模块实现的位置：

```csharp
// src/Composition/Full.NET.Composition/FullNetModuleCatalog.cs
public static IServiceCollection AddFullNetModules(this IServiceCollection services)
{
    services.AddModule<IdentityModule>()
            .AddModule<TenancyModule>()
            .AddModule<OrganizationModule>()
            .AddModule<SettingsModule>()
            .AddModule<AuditingModule>()
            // ... 其他模块
            .BuildCatalog();
    return services;
}
```

### 2.2 Host Profile（按运行角色装配能力）

| Profile | 角色 | 装配内容 |
|---------|------|----------|
| `Api` | API 宿主 | 完整 HTTP 模块、认证、授权、CORS、限流、Endpoint、SignalR |
| `Worker` | Worker 宿主 | 后台能力：Outbox 处理器、Retention 处理器、Kafka Consumer、事件投影 |
| `Migration` | Migrator 宿主 | 最小闭包：DbUp、Seed Contributor、迁移专用连接工厂 |
| `Test` | 测试夹具 | Api + Worker 合并，可按需替换外部依赖 |

### 2.3 依赖图约束（架构测试门禁）

- 模块依赖图必须是 **DAG (有向无环图)**
- 生产模块只能依赖其他模块的 **公开 Contracts**（`*.Contracts.csproj`）
- 禁止 `InternalsVisibleTo` 跨生产模块
- Composition 是唯一可引用具体实现模块的地方

---

## 3. 模块清单

### 3.1 已落地模块

| 模块 | 稳定键 | 项目 | 核心职责 |
|------|--------|------|----------|
| **Identity** | `Identity` | `Full.NET.Modules.Identity` | 用户、角色、菜单、RBAC 授权、认证会话、JWT/RSA 签名、API Key、TOTP MFA、超级管理员、在线会话 |
| **Tenancy** | `Tenancy` | `Full.NET.Modules.Tenancy` | 租户开通、域名解析、租户切换、租户包、租户缓存失效、集成事件 |
| **Organization** | `Organization` | `Full.NET.Modules.Organization` | 组织单元(部门)、岗位、职级、用户组织归属、跨模块投影 |
| **Settings** | `Settings` | `Full.NET.Modules.Settings` | 参数配置(ConfigEntry)、字典(DictType/DictItem)、枚举目录、网格偏好、Host/Tenant 双作用域 |
| **Auditing** | `Auditing` | `Full.NET.Modules.Auditing` | 操作日志、访问日志、异常日志、出站调用日志、保留策略 Runner、游标分页查询 |
| **Files** | `Files` | `Full.NET.Modules.Files` | 宿主文件上传/下载、Blob 引用声明(ReferenceClaim)、软删除 Blob 清理 |
| **Document** | `Document` | `Full.NET.Modules.Document` | 文档项、分类、标签、细粒度权限、分享、统计、回收站 |
| **Notifications** | `Notifications` | `Full.NET.Modules.Notifications` | 站内消息收件箱、SignalR 实时推送、消息状态 |
| **Jobs** | `Jobs` | `Full.NET.Modules.Jobs` | 任务定义、Cron 调度、立即触发、执行记录、出错统计 |
| **Messaging** | `Messaging` | `Full.NET.Modules.Messaging` | 事件流所有权切换、死信查询与重放、CDC/Kafka 运维 API |
| **CodeGeneration** | `CodeGeneration` | `Full.NET.Modules.CodeGeneration` | CRUD 模板管理、预览、执行、Git 集成、检查点回滚链 |
| **SerialNumbers** | `SerialNumbers` | `Full.NET.Modules.SerialNumbers` | 编号规则、原子生成、并发控制 |

### 3.2 存在 Contracts 独立项目的模块（有真实跨模块消费者）

| Contracts 项目 | 消费者 |
|----------------|--------|
| `Full.NET.Modules.Identity.Contracts` | Organization、Tenancy 等需要用户目录只读投影 |
| `Full.NET.Modules.Organization.Contracts` | Identity 数据范围过滤、组织单元选择目录 |
| `Full.NET.Modules.Settings.Contracts` | 其他模块读取平台配置 |
| `Full.NET.Modules.Files.Contracts` | 其他模块引用文件资源 |

---

## 4. 模块表命名约定

数据库表使用三段式命名：`{owner_key}_{module_key}_{entity_key}`

| 模块 | Module Key | 表示例 |
|------|------------|--------|
| Identity | `identity` | `fn_identity_user`、`fn_identity_role`、`fn_identity_user_role` |
| Tenancy | `tenancy` | `fn_tenancy_tenant`、`fn_tenancy_tenant_package` |
| Organization | `organization` | `fn_organization_unit`、`fn_organization_position` |
| Settings | `settings` | `fn_settings_config_entry`、`fn_settings_dict_type` |
| Auditing | `auditing` | `fn_auditing_operation_log`、`fn_auditing_access_log` |
| Files | `files` | `fn_files_host_file`、`fn_files_host_file_reference_claim` |
| Document | `document` | `fn_document_item`、`fn_document_category`、`fn_document_tag` |
| Jobs | `jobs` | `fn_jobs_host_definition`、`fn_jobs_host_schedule` |
| Messaging/Outbox | `outbox` / `messaging` | `fn_outbox_message`、`fn_messaging_outbox_event`、`fn_messaging_inbox_message` |
| SerialNumbers | `serial_numbers` | `fn_serial_numbers_rule`、`fn_serial_numbers_allocation` |

> 注意：`fn` 是 Full.NET 官方 OwnerKey（固定保留）；具体产品使用脚手架冻结的项目 OwnerKey。

---

## 5. 模块垂直切片示例：Identity → Login

```text
Features/Login/
├── Endpoint.cs          // app.MapPost("/api/v1/auth/login", ...)
├── Command.cs           // LoginCommand : ITransactionalCommand<LoginResponse>
├── Handler.cs           // ICommandHandler<LoginCommand, LoginResponse>
│                        //   ├── 校验用户名密码
│                        //   ├── 检查锁定
│                        //   ├── 创建 RefreshSession
│                        //   ├── 签发 JWT + CSRF Token
│                        //   └── 写入登录审计
├── LoginCommandValidator.cs  // FluentValidation 校验（事务前短路）
```

**调用链**：
```
HTTP Request
  → AuthenticationMiddleware
  → AuthorizationMiddleware
  → Login Endpoint
    → ICommandDispatcher.SendAsync<LoginCommand, LoginResponse>()
      → ValidationBehavior（FluentValidation）
      → LoggingBehavior
      → TransactionBehavior（开启 ICommandTransaction）
        → LoginHandler.HandleAsync
          ├── 读：fn_identity_user（Dapper QueryFirstOrDefault）
          ├── 密码哈希校验（IdentityPasswordPolicy）
          ├── 写：fn_identity_refresh_session（事务内）
          ├── 写：Outbox 登录事件（事务内）
          └── 领域审计写入（事务内）
      → 事务 Commit
  → HTTP 200 { accessToken, refreshToken, csrfToken, userInfo }
```

---

## 6. 跨模块数据关联标准速查

| 场景 | 推荐方式 | 禁止 |
|------|----------|------|
| **模块内 JOIN** | 直接 JOIN 本模块 `fn_mod_*` 表 | 为复用建立跨模块 Repository |
| **跨模块低频同步读取** | 消费方最小 Port → 对方公开 Contract Service | 直接 SQL 读取对方表 |
| **跨模块高频读取** | 所有者发布 Integration Event → 消费方本地投影表 | 逐条查所有者 + 用缓存冒充权威 |
| **模块内写入** | 单 `ICommandTransaction` 维护本模块表 + Outbox + 审计 | 事务内执行不可回滚 HTTP/Broker/Redis |
| **跨模块写入** | Saga/Process Manager + Outbox + 幂等消费者 + 对账 | 共享 DbSession / 跨模块本地事务 / 分布式事务 |
