# 模块系统总览

## 1. 模块入口约定

每个业务模块实现 `IFullNetModule` 接口（[`src/BuildingBlocks/Full.NET.Modularity/Modules/IFullNetModule.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Modularity/Modules/IFullNetModule.cs)），提供统一的注册和端点映射入口。

```csharp
public interface IFullNetModule
{
    string Name { get; }                                                  // 稳定唯一模块键（区分大小写 Ordinal）
    IReadOnlyCollection<string> Dependencies { get; }                    // 必须依赖的稳定模块键
    IReadOnlyCollection<string> OptionalContractDependencies => [];     // 可选契约依赖（仅事件/最小只读 Port）
    void AddServices(IServiceCollection services, IConfiguration configuration);
    void AddMigrationServices(IServiceCollection services, IConfiguration configuration);  // 默认空实现
    void MapEndpoints(IEndpointRouteBuilder endpoints);
    void AddBackgroundServices(IServiceCollection services, IConfiguration configuration); // 默认空实现
    void UseModuleMiddleware(IApplicationBuilder app, ModulePipelineStage stage);         // 默认空实现
}
```

> 关键变化（2026-09 基线）：
> - `AddMigrationServices` / `AddBackgroundServices` / `UseModuleMiddleware` 都有默认空实现，模块只在需要时覆写。
> - 新增 `OptionalContractDependencies`，仅用于"消费事件或最小只读契约"，**不能**用于同步调用、数据库访问或服务解析；缺少对方模块时当前模块必须能安全退化为无事件输入。例如 `Notifications.OptionalContractDependencies = ["Workflow"]` 表示禁用 Workflow 时通知中心仍能独立提供公告、站内信和渠道投递。
> - `Dependencies` 与 `OptionalContractDependencies` 必须互斥；后者中的键必须存在于 `OfficialModuleNames` 全集中。

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

**Composition 组合根**是唯一可引用具体模块实现的位置（[`src/Composition/Full.NET.Composition/FullNetModuleCatalog.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/Composition/Full.NET.Composition/FullNetModuleCatalog.cs)）：

```csharp
// Composition 同时也是唯一持有 OfficialModuleNames 与 Preset 的位置
public static class FullNetModuleCatalog
{
    public const string BrowserCorsPolicy = IdentityModule.BrowserCorsPolicy;

    public static IServiceCollection AddFullNetApplicationModules(
        this IServiceCollection services,
        IConfiguration configuration,
        FullNetHostProfile profile)
    {
        // 1. 调用 AddFullNetModularity()（IFullNetModuleCatalog 等基础设施）
        // 2. CreateModules(configuration) 调用 FullNetModuleSelection.ResolveEnabledModules
        //    读取 FullNet:Modules 配置裁剪 CreateAllModules() 返回的官方全集
        // 3. Api Profile：若启用 AiModule，先 AddAiProviderServices（避免悬空依赖）
        // 4. 逐个调用 services.AddFullNetModule(module, configuration)
        // 5. Api Profile 物化只读 catalog snapshot（AddFullNetModuleCatalogSnapshot）
    }

    private static IReadOnlyList<IFullNetModule> CreateAllModules() =>
    [
        new IdentityModule(),
        new AuditingModule(),
        // ... 全部 30 个稳定模块（详见 §3.1）
    ];
}
```

启用集解析与依赖闭包校验在 [`FullNetModuleSelection.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/Composition/Full.NET.Composition/FullNetModuleSelection.cs)：

- `ResolveEnabledNames(IConfiguration)` 读取 `FullNet:Modules:Enabled` 或 `FullNet:Modules:Preset`，校验名称合法、无重复、必须包含 `Identity`
- `ResolveEnabledModules` 进一步校验每个启用模块的 `Dependencies` 都在启用集内（DAG 闭包），并校验 `OptionalContractDependencies` 中的键必须存在于 `OfficialModuleNames` 且与 `Dependencies` 互斥
- 部署期生效：`DeploymentNotice` 常量声明 Api/Worker/Migrator 必须重启，禁止运行时动态加载程序集

### 2.2 Host Profile（按运行角色装配能力）

| Profile | 角色 | 装配内容 |
|---------|------|----------|
| `Api` | API 宿主 | 完整 HTTP 模块（`AddServices` + `MapEndpoints`）、认证、授权、CORS、限流、SignalR；物化只读 catalog snapshot 与 `IFullNetModuleSelectionPreview` |
| `Worker` | Worker 宿主 | 仅装配 `AddBackgroundServices`：Outbox 处理器、Retention 处理器、Kafka Consumer、事件投影、各模块 Worker HostedService |
| `Migrator` | Migrator 宿主 | 仅装配 `AddMigrationServices`：DbUp、Seed Contributor、迁移专用领域服务最小闭包 |
| `Test` | 测试夹具 | 由测试项目自行控制模块子集（不在宿主入口直接使用） |

### 2.3 模块预设（`FullNet:Modules:Preset`）

| 预设 | 包含模块（来源 `FullNetModuleSelection`） |
|------|---------------------------------------------|
| `Minimal` | Identity, Tenancy, Settings, Organization |
| `Platform` | Minimal + Auditing, Files, Notifications, Calendar, Platform, Regions, Jobs, Messaging, ObservabilityAdmin, Mqtt, Cryptography |
| `Content` | Platform + Document |
| `Saas` | Platform + Payments, Webhooks |
| `Enterprise` | Platform + Webhooks, Workflow, ImportExport, Reporting, Printing, EnterpriseRequest |
| `Full`（默认） | `OfficialModuleNames` 全集（30 个稳定键） |

> 启用 `FullNet:Modules:Enabled` 显式列表时优先级最高，跳过 Preset；空集、含空名、含未知名或缺少 Identity 都会抛 `InvalidOperationException`。

### 2.4 依赖图约束（架构测试门禁）

- 模块依赖图必须是 **DAG（有向无环图）**，由 `FullNetModuleSelection.ValidateDependencies` 在启动期校验
- 生产模块只能依赖其他模块的 **公开 Contracts**（`*.Contracts.csproj`），由架构测试 `DependencyRulesTests` 守护
- `OptionalContractDependencies` 中的键必须存在于 `OfficialModuleNames` 全集中，且与 `Dependencies` 互斥
- 禁止 `InternalsVisibleTo` 跨生产模块
- Composition 是唯一可引用具体实现模块的位置；宿主项目（Api/Worker/Migrator）必须通过 `AddFullNetApplicationModules` 装配

---

## 3. 模块清单

### 3.1 已落地模块

> 30 个稳定模块键与 [`FullNetModuleSelection.OfficialModuleNames`](file:///G:/wwwroot/github_fork/Full.NET/src/Composition/Full.NET.Composition/FullNetModuleSelection.cs) 一一对应；`EnterpriseRequest` 位于 `samples/` 目录，但已注册到 `CreateAllModules()` 与 `OfficialModuleNames`。

| 模块 | 稳定键 | 项目 | 核心职责 |
|------|--------|------|----------|
| **Identity** | `Identity` | `Full.NET.Modules.Identity` | 用户、角色、菜单、RBAC 授权、OIDC、OAuth、LDAP、注册邀请、JWT/RSA 签名、API Key、TOTP MFA、超级管理员、在线会话 |
| **Tenancy** | `Tenancy` | `Full.NET.Modules.Tenancy` | 租户开通、域名解析、租户切换、租户包、租户缓存失效、订阅履约、品牌 |
| **Organization** | `Organization` | `Full.NET.Modules.Organization` | 组织单元(部门)、岗位、职级、用户组织归属、跨模块投影 |
| **Settings** | `Settings` | `Full.NET.Modules.Settings` | 参数配置(ConfigEntry)、字典(DictType/DictItem)、枚举目录、网格偏好、Host/Tenant 双作用域 |
| **Auditing** | `Auditing` | `Full.NET.Modules.Auditing` | 操作日志、访问日志、异常日志、出站调用日志、保留策略 Runner、游标分页查询 |
| **Files** | `Files` | `Full.NET.Modules.Files` | 宿主文件上传/下载、Blob 引用声明(ReferenceClaim)、软删除 Blob 清理、租户文件所有权 Port |
| **Document** | `Document` | `Full.NET.Modules.Document` | 文档项、分类、标签、细粒度权限、分享、统计、回收站、版本保留 |
| **Notifications** | `Notifications` | `Full.NET.Modules.Notifications` | 公告、站内信收件箱、通知模板、Intent + 渠道适配（SMTP/AliyunSms/DingTalk/WeCom/WeChatMiniProgram）、SignalR 实时推送 |
| **Calendar** | `Calendar` | `Full.NET.Modules.Calendar` | 当前用户个人日程 CRUD + 完成状态切换 |
| **Platform** | `Platform` | `Full.NET.Modules.Platform` | Host 更新日志、用户已读、备份执行器状态 |
| **Regions** | `Regions` | `Full.NET.Modules.Regions` | 行政区划参考数据、级联查询、可审计导入、Baseline 种子 |
| **Jobs** | `Jobs` | `Full.NET.Modules.Jobs` | 任务定义、Cron 调度、立即触发、执行记录、出错统计 |
| **Messaging** | `Messaging` | `Full.NET.Modules.Messaging` | 事件流所有权切换、死信查询与重放、CDC/Kafka 运维 API |
| **ImportExport** | `ImportExport` | `Full.NET.Modules.ImportExport` | 静态 Schema 目录、模板下载、预校验、批量导入任务（Worker） |
| **Reporting** | `Reporting` | `Full.NET.Modules.Reporting` | 数据源管理、报表定义、执行、Excel 导出（Worker）、查询端口目录 |
| **Printing** | `Printing` | `Full.NET.Modules.Printing` | 固定表单 Schema、打印模板版本、浏览器预览、租户绑定 Port |
| **Ai** | `Ai` | `Full.NET.Modules.Ai` | 模型配置、连通性测试、聊天会话、MCP 远程连接、Agent 运行、审批、委派、预算与配额 |
| **Payments** | `Payments` | `Full.NET.Modules.Payments` | 微信 Native/支付宝 Page Pay 商户配置、订单、退款、回调、租户订阅履约 |
| **GoView** | `GoView` | `Full.NET.Modules.GoView` | 大屏项目草稿、发布快照、只读预览 |
| **K3Cloud** | `K3Cloud` | `Full.NET.Modules.K3Cloud` | 金蝶 K3Cloud ValidateUser 连接配置、销售订单 Save/Submit 同步 |
| **Ocr** | `Ocr` | `Full.NET.Modules.Ocr` | OCR Provider 配置、身份证识别任务、人工确认 |
| **CodeGeneration** | `CodeGeneration` | `Full.NET.Modules.CodeGeneration` | CRUD 模板管理、预览、执行、Git 集成、检查点回滚链 |
| **SerialNumbers** | `SerialNumbers` | `Full.NET.Modules.SerialNumbers` | 编号规则、原子生成、并发控制 |
| **DataApproval** | `DataApproval` | `Full.NET.Modules.DataApproval` | 跨模块变更审批请求、场景、工作流终端事件投影 |
| **ObservabilityAdmin** | `ObservabilityAdmin` | `Full.NET.Modules.ObservabilityAdmin` | 日志文件查询、服务器监控、缓存策略控制面、ES 健康探针 |
| **Workflow** | `Workflow` | `Full.NET.Modules.Workflow` | 工作流定义、表单、实例、待办、抄送、恢复任务、终端事件 |
| **Mqtt** | `Mqtt` | `Full.NET.Modules.Mqtt` | MQTT Broker 状态、客户端目录、消息记录、受控发布 |
| **Webhooks** | `Webhooks` | `Full.NET.Modules.Webhooks` | Webhook 订阅、事件入队、投递 Worker、HMAC-SHA256 签名 |
| **Cryptography** | `Cryptography` | `Full.NET.Modules.Cryptography` | 国密 SM2 密钥目录、受控签名、验签 |
| **EnterpriseRequest**（sample） | `EnterpriseRequest` | `samples/enterprise-request/...` | 跨模块业务请求样板（Identity+Tenancy+Organization+Files+Workflow+ImportExport） |

### 3.2 存在 Contracts 独立项目的模块

| Contracts 项目 | 跨模块 `.csproj` 消费者（2026-09 基线） | 说明 |
|----------------|---------------------------------------------|------|
| `Full.NET.Modules.Identity.Contracts` | Auditing、CodeGeneration、Document、Files、Jobs、Messaging、Notifications、Organization、SerialNumbers、Settings、Tenancy、Workflow、Ai、Payments、GoView、K3Cloud、Ocr、ImportExport、Reporting、Printing、Cryptography、Webhooks、Mqtt、ObservabilityAdmin、Calendar、Platform、Regions、DataApproval | 平台 hub：权限、会话、Host 目录 Port、组织投影 Port 等 |
| `Full.NET.Modules.Files.Contracts` | Document、Notifications、Workflow、Identity、Tenancy、Ocr、Reporting、ImportExport | 文件引用声明、租户文件所有权 |
| `Full.NET.Modules.Organization.Contracts` | 无（仅 Organization 主项目自引用） | OpenAPI/序列化隔离，暂无外部模块消费者 |
| `Full.NET.Modules.Settings.Contracts` | 无（仅 Settings 主项目自引用） | OpenAPI/序列化隔离，暂无外部模块消费者 |
| `Full.NET.Modules.Tenancy.Contracts` | Payments | 租户订阅履约 Port |
| `Full.NET.Modules.Notifications.Contracts` | Identity | Identity 账号挑战邮件投递 Port |
| `Full.NET.Modules.Workflow.Contracts` | Notifications、DataApproval、Webhooks、EnterpriseRequest（sample） | 工作流实例启动/取消 Port、终端事件 Sink |
| `Full.NET.Modules.ImportExport.Contracts` | Organization、EnterpriseRequest（sample） | 静态 Schema 贡献 Port |
| `Full.NET.Modules.Reporting.Contracts` | 无（仅 Reporting 主项目自引用） | OpenAPI/序列化隔离 |
| `Full.NET.Modules.Printing.Contracts` | Tenancy | 租户打印配置绑定 Port |
| `Full.NET.Modules.Ai.Contracts` | 无（仅 Ai 主项目自引用） | OpenAPI/序列化隔离 |
| `Full.NET.Modules.GoView.Contracts` | 无（仅 GoView 主项目自引用） | OpenAPI/序列化隔离 |
| `Full.NET.Modules.K3Cloud.Contracts` | 无（仅 K3Cloud 主项目自引用） | OpenAPI/序列化隔离 |
| `Full.NET.Modules.Ocr.Contracts` | 无（仅 Ocr 主项目自引用） | OpenAPI/序列化隔离 |
| `Full.NET.Modules.Payments.Contracts` | 无（仅 Payments 主项目自引用） | OpenAPI/序列化隔离 |
| `Full.NET.Modules.DataApproval.Contracts` | 无（仅 DataApproval 主项目自引用） | 业务模块内部 Port |

是否保留独立 `.Contracts` 项目以 **真实跨模块编译引用** 为准；禁止在 wiki 中假设 Identity 仍引用 `Organization.Contracts`。

**Contracts 拆分决策表（2026-09 基线）：**

| 条件 | 建议 |
|------|------|
| ≥1 个其他模块 `.csproj` 需要稳定 DTO/Port/事件类型 | 独立 `{Module}.Contracts.csproj` |
| 仅本模块 OpenAPI/序列化隔离，无外部 consumer | 可保留 isolation-only `.Contracts`（须 ADR/wiki 登记豁免） |
| 仅本模块 Features 使用的 DTO | 主项目内 `Contracts/` 目录即可（见 §3.3） |
| 跨模块读取 Port / Integration Event | **消费方** Contracts 定义 Port 与 wire 类型；owner 模块实现适配器（见 ADR-0002 数据所有权） |

### 3.3 主项目内 Contracts 目录（无独立 `.csproj`）

| 模块 | 命名空间示例 | 说明 |
|------|--------------|------|
| Tenancy | `Full.NET.Modules.Tenancy.Contracts` | 租户摘要等内部公开契约（仍存在独立 .Contracts 项目的 Tenancy.Contracts 仅供 Payments 消费） |
| Auditing | `Full.NET.Modules.Auditing.Contracts` | 审计 DTO |
| Jobs | `Full.NET.Modules.Jobs.Contracts` | 调度契约 |
| Messaging | `Full.NET.Modules.Messaging.Contracts` | 运维控制面 DTO |
| Document | `Full.NET.Modules.Document.Contracts` | 文档模块 DTO |
| Notifications | `Full.NET.Modules.Notifications.Contracts` | 通知 DTO（与 .Contracts 项目并存，详见 §3.2） |
| CodeGeneration | `Full.NET.Modules.CodeGeneration.Contracts` | 生成器契约 |
| SerialNumbers | `Full.NET.Modules.SerialNumbers.Contracts` | 编号规则 DTO |
| Workflow | `Full.NET.Modules.Workflow.Contracts` | 工作流 DTO（与 .Contracts 项目并存） |
| Calendar | `Full.NET.Modules.Calendar.Contracts` | 日历 DTO |
| Cryptography | `Full.NET.Modules.Cryptography.Contracts` | 国密 DTO |
| DataApproval | `Full.NET.Modules.DataApproval.Contracts` | 审批 DTO |
| Mqtt | `Full.NET.Modules.Mqtt.Contracts` | MQTT DTO |
| Platform | `Full.NET.Modules.Platform.Contracts` | 平台 DTO |
| Regions | `Full.NET.Modules.Regions.Contracts` | 区划 DTO |
| Webhooks | `Full.NET.Modules.Webhooks.Contracts` | Webhook DTO |

Consumer Port 范例见 [`OrganizationUnitProjectionContracts.cs`](../src/Modules/Full.NET.Modules.Identity.Contracts/OrganizationUnitProjectionContracts.cs) 与 ADR-0002 §数据所有权。

### 3.4 模块依赖速查

详见 [`modules-core.md` 附录 A.1](modules-core.md#a1-模块依赖矩阵按-dependencies-字段)。所有模块的硬依赖与可选契约依赖均与 `IFullNetModule.Dependencies` / `OptionalContractDependencies` 字段一致，架构测试 `DependencyRulesTests` 守护。

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
| Notifications | `notifications` | `fn_notifications_announcement`、`fn_notifications_template`、`fn_notifications_inbox_message` |
| Calendar | `calendar` | `fn_calendar_personal_schedule` |
| Platform | `platform` | `fn_platform_release_note`、`fn_platform_backup_run` |
| Regions | `regions` | `fn_regions_administrative_region` |
| Jobs | `jobs` | `fn_jobs_host_definition`、`fn_jobs_host_schedule` |
| Messaging/Outbox | `outbox` / `messaging` | `fn_outbox_message`、`fn_messaging_outbox_event`、`fn_messaging_inbox_message` |
| ImportExport | `import_export` | `fn_import_export_task` |
| Reporting | `reporting` | `fn_reporting_data_source`、`fn_reporting_definition` |
| Printing | `printing` | `fn_printing_template`、`fn_printing_template_version` |
| Ai | `ai` | `fn_ai_model_config`、`fn_ai_agent_run`、`fn_ai_chat_session` |
| Payments | `payments` | `fn_payments_merchant_config`、`fn_payments_order`、`fn_payments_refund` |
| GoView | `goview` | `fn_goview_project`、`fn_goview_project_published` |
| K3Cloud | `k3cloud` | `fn_k3cloud_connection_config`、`fn_k3cloud_document_sync` |
| Ocr | `ocr` | `fn_ocr_provider`、`fn_ocr_id_card_task` |
| CodeGeneration | `code_generation` | `fn_code_generation_template`、`fn_code_generation_run` |
| SerialNumbers | `serial_numbers` | `fn_serial_numbers_rule`、`fn_serial_numbers_allocation` |
| DataApproval | `data_approval` | `fn_data_approval_request`、`fn_data_approval_scenario` |
| ObservabilityAdmin | `observability_admin` | `fn_observability_admin_*` |
| Workflow | `workflow` | `fn_workflow_definition`、`fn_workflow_instance`、`fn_workflow_todo` |
| Mqtt | `mqtt` | `fn_mqtt_*` |
| Webhooks | `webhooks` | `fn_webhooks_subscription`、`fn_webhooks_delivery` |
| Cryptography | `cryptography` | `fn_cryptography_gm_key` |
| EnterpriseRequest（sample） | `enterprise_request` | `fn_enterprise_request_*` |

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
