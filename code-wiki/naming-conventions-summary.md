# 命名规范摘要

> 完整规范：[`rules/naming-conventions.md`](file:///G:/wwwroot/github_fork/Full.NET/rules/naming-conventions.md) | 机器契约：[`contracts/naming/fullnet-naming-profile.json`](file:///G:/wwwroot/github_fork/Full.NET/contracts/naming/fullnet-naming-profile.json)、[`contracts/naming/pre-v1-name-map.json`](file:///G:/wwwroot/github_fork/Full.NET/contracts/naming/pre-v1-name-map.json)、[`contracts/naming/naming-debt.json`](file:///G:/wwwroot/github_fork/Full.NET/contracts/naming/naming-debt.json)

## 1. 全局原则

| 原则 | 说明 |
|------|------|
| **名称表达所有权** | 从名称判断框架/项目/模块归属 |
| **跨平台惯例不同** | 表名：snake_case；列/C# 属性：PascalCase；HTTP JSON：camelCase |
| **Dapper 直接映射** | 列与属性同名，禁用全局 snake_case 匹配 |
| **稳定契约不可随意美化** | API/JSON/错误码/权限码/表名发布后必须版本化 |
| **共同下限** | 仅 ASCII 字母数字下划线，标识符 ≤ 64 字符（MySQL 上限） |
| **禁止引用符解决坏名称** | 不使用 SQL 保留字、空格、连字符、非 ASCII 字符 |
| **统一缩写** | `FullNet`, `Id`, `Ids`, `Api`, `Http`, `Https`, `Json`, `Html`, `Sql`, `Jwt`, `Uri`, `Url`, `Ip`, `Ui`, `Utc`, `Uuid`, `Csp`, `Csrf`, `Grpc`，不混用 `ID/Id`、`JSON/Json`、`FullNET/FullNet` |

## 2. 数据库命名

### 2.1 表名：三段式

```text
{owner_key}_{module_key}_{entity_key}
```

| 部分 | 说明 | 示例 |
|------|------|------|
| `owner_key` | 发布与迁移所有者。**`fn` 仅 Full.NET 官方**，项目表使用脚手架冻结的项目键；禁止 `sys/mysql/dbo/information_schema/performance_schema`；匹配 `^[a-z][a-z0-9]{1,11}$` | `fn`, `crm` |
| `module_key` | 稳定限界上下文标识，匹配 `^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$` | `identity`, `tenancy`, `jobs`, `outbox` |
| `entity_key` | 单数实体/关系名；关系表按参与实体稳定顺序命名 | `user`, `role`, `user_role` |

**表示例**（来自实际迁移 `002_Identity.sql`、`003_AuthorizationContext.sql`、`030_JobsDefinitionAndExecution.sql`）：

| 正确 | 错误 |
|------|------|
| `fn_identity_user` | `sys_user`(禁 sys), `identity_users`(复数), `fn_identity_UserInfo`(无意义后缀) |
| `fn_identity_user_role` | `user_roles`, `user_to_role` |
| `fn_jobs_definition` | `host_job_def`(缩写不清), `fn_jobs_host_definitions`(无 host 段) |
| `fn_outbox_message` | `outbox_messages`(复数), `fn_outbox_msg`(缩写) |

### 2.2 列名：PascalCase

| 列 | 说明 |
|----|------|
| `Id` | 单一主键 |
| `TenantId` | 租户边界列（**所有租户业务表固定此名**，不得 TenantID / Tenant / OrganizationId） |
| `RoleId`, `CreatedById`, `ApprovedById`, `ReplacedById` | 外键：{角色}Id；同目标多角色必须表达角色 |
| `CreatedAtUtc`, `UpdatedAtUtc`, `OccurredAtUtc`, `ExpiresAtUtc`, `LockedUntilUtc`, `ValidFromUtc` | UTC 时间线瞬间必须带 Utc 后缀 |
| `BirthDate` | 仅日历日期不加 Utc；当地墙上时间用 `LocalTime` + `TimeZoneId` |
| `TimeoutSeconds`, `SizeBytes`, `OffsetMinutes` | 数值必须体现单位 |
| `IsEnabled`, `HasProfile`, `CanShare`, `ShouldNotify` | 布尔：Is/Has/Can/Should 前缀，禁 `Flag`/`StatusBool` |
| `Version` | 乐观并发；消息 Schema 用 `SchemaVersion`；业务版本带领域限定词 |
| `IsDeleted`, `DeletedAtUtc`, `DeletedById` | 软删除三件套（非全部表强制） |
| `ExtendedPropertiesJson` | JSON 文本列：{Purpose}Json 后缀（须通过 JSON 准入门禁） |
| `Payload` | 二进制消息正文（格式由 `ContentType` + `SchemaVersion` 表达） |

### 2.3 主键/索引/约束命名

| 对象 | 格式 | 示例 |
|------|------|------|
| 主键 | `PK_{table}` | `PK_fn_identity_user` |
| 外键 | `FK_{table}_{column}` | `FK_fn_identity_user_role_UserId` |
| 唯一索引 | `UX_{table}_{key_columns}` | `UX_fn_identity_user_ScopeKey_NormalizedUsername` |
| 普通索引 | `IX_{table}_{key_columns}` | `IX_fn_identity_refresh_session_UserId_ExpiresAtUtc` |
| 检查约束 | `CK_{table}_{rule}` | `CK_fn_identity_role_TenantScope` |
| 默认约束 | `DF_{table}_{column}` | `DF_fn_identity_user_Version` |

> 名称最长 64 字符；超长时使用规范名的 SHA-256 前 8 位小写十六进制摘要（前 55 字符 + `_` + 8 位摘要）。Include 列不写入索引名；两个索引键相同时追加 PascalCase 用途。

**SQL Server 主键注意**：高频追加表（Outbox/Audit/History）默认使用**非聚集** UUID 主键，聚集索引按时间路径设计 `(OccurredAtUtc, Id)` / `(CreatedAtUtc, Id)`。`fullnet-naming-profile.json` 中 `primaryKey.highWriteUuidTables` 显式登记：`fn_outbox_message`、`fn_identity_auth_audit`。

### 2.4 数据库对象注释

- 所有新建/修改的表与列必须携带中文说明，写入迁移脚本并登记在 [`contracts/database/object-comments.json`](file:///G:/wwwroot/github_fork/Full.NET/contracts/database/object-comments.json)
- MySQL：`CREATE TABLE` / `ADD COLUMN` 内联 `COMMENT='...'`；SQL Server：`sys.sp_addextendedproperty` (`MS_Description`)
- 同一表/列两库说明文本必须一致
- 工具：`node scripts/database/generate-object-comments.mjs`、`node scripts/database/apply-migration-comments.mjs`、`node scripts/database/validate-sql-comments.mjs`

## 3. UUID 主键存储

> ADR：[`ADR-0003`](file:///G:/wwwroot/github_fork/Full.NET/docs/architecture/adr/ADR-0003-uuid-v7-primary-key-storage.md)

- 逻辑类型：C# **`Guid`**；默认 profile `uuidV7`（`fullnet-naming-profile.json` 也允许 `snowflake`，需独立 ADR）
- 应用必须在数据库写入前通过 `IIdGenerator` 生成非空标识；**禁止依赖数据库默认值**
- Provider 物理类型：

| Provider/边界 | UUID 类型或格式 |
|----------|------|
| SQL Server | `uniqueidentifier` |
| MySQL | RFC 9562 大端/网络字节序 `BINARY(16)` |
| C# 与 Dapper 参数 | `Guid` |
| HTTP/JSON/OpenAPI | 小写规范 UUID 字符串 |

- MySQL 必须由 Full.NET 数据边界统一使用 `GuidFormat=Binary16` 或等价受测编解码契约
- 业务模块**禁止** `Guid.ToByteArray()` / `UUID_TO_BIN(..., 1)` / `TimeSwapBinary16` / 自行交换字节序
- 同一关系链的主键、外键、租约与审计引用必须采用相同物理类型和字节序
- API、缓存键、日志和消息头只在边界输出规范 UUID 文本，禁止把 MySQL 二进制表现泄漏给客户端

## 4. C# / .NET 命名

| 类型 | 规范 | 示例 |
|------|------|------|
| Namespace / 类 / 方法 / 属性 | PascalCase | `HostUserManagementService` |
| 接口 | `I` 前缀 + PascalCase | `ICommandTransaction` |
| 异步方法 | `Async` 后缀 + `CancellationToken` | `HandleAsync` |
| 枚举（非 Flags） | 单数 + 显式整数值 | `public enum DatabaseProvider { SqlServer = 1, MySql = 2 }` |
| Flags 枚举 | 复数 + 二进制幂 | `[Flags] public enum SeedProfiles { Baseline = 1, Development = 2, ... }` |
| 方法参数 / 局部变量 | camelCase | `tenantId`, `command` |
| 私有实例字段 | `_camelCase` | `_dbSession`, `_logger` |
| Positional record 主构造参数 | PascalCase（同时生成公开属性；普通构造函数/方法参数仍为 camelCase） | `record Error(ErrorType Type, string Code)` |
| 常量 | PascalCase（禁止 UPPER_SNAKE_CASE） | `public const int DefaultPageSize = 20` |

### 项目与命名空间

```
Full.NET.BuildingBlocks.{LayerName}        // BuildingBlocks
Full.NET.Modules.{ModuleName}              // 主模块（默认承载实现/持久化/注册/Endpoint）
Full.NET.Modules.{ModuleName}.Contracts    // 可选：仅在存在真实跨模块消费者且需稳定契约隔离时
Full.NET.Modules.{ModuleName}.Http        // 可选：同一 web-free Core 被非 HTTP 宿主真实复用时
Full.NET.Host.{Role}                       // 宿主：Api / Worker / Migrator / AppHost
```

> 品牌名在代码标识符中写作 `FullNet`；模块 Namespace 已表达上下文时不重复模块名（如 `Full.NET.Modules.Tenancy.Domain.Tenant`，不写 `TenancyTenant`）。

### 类型语义后缀

- 数据库读取专用类型用 `Row` 后缀；领域投影按用途用 `Summary` / `Details`，禁用 `Model`
- HTTP 边界用 `Request` / `Response`；应用消息用 `Command` / `Query`
- Feature Namespace 用 `VerbNoun`（如 `ProvisionTenant`）；Feature 内唯一适配类型可简化为 `Endpoint` / `Handler` / `Validator`

### 文件命名

- 默认与主要类型同名：`IdentityModule.cs`, `TenantResolver.cs`
- Feature 内短适配可简化：`Endpoint.cs`, `Handler.cs`, `Validator.cs`
- 测试类以 `Tests` 结尾，测试方法表达 `场景_行为_结果`
- 生成文件：`.g.cs` / `.generated.ts` / `.generated.js` 后缀

## 5. HTTP / JSON / 稳定机器码

### 5.1 API 路径

```text
/api/v{major}/{kebab-case-plural-resource}
```

示例：`/api/v1/host/users`, `/api/v1/tenants/{id}/switch`, `/api/v1/settings/dict-types/by-code/{code}/items`

> 路径段匹配 `^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$`；操作能表达为资源状态转换时不新增动词路由。

### 5.2 OpenAPI operationId 与 Tag

- 客户端生成的 Operation 必须显式提供全局唯一 lowerCamelCase `operationId`：`{module}{Verb}{Resource}[Qualifier]`
  - 示例：`identityListHostUsers`、`identityCreateHostUser`、`identityResetHostUserPassword`
- 每个 Operation 恰有一个稳定主 Tag：PascalCase 的 `{Module}{Resource}`，如 `IdentityHostUsers`、`FilesHostFiles`
- 禁止从路径、Lambda 方法名或显示文本隐式推导

### 5.3 JSON

- C# 属性 PascalCase → System.Text.Json 对外 camelCase
- 禁止同一 API 混用 snake_case
- 公开契约一旦发布，必须版本化或提供兼容迁移

### 5.4 权限码

```text
{module}.{plural_resource}.{action}
```

每段 `^[a-z][a-z0-9_]*$`。示例：

| 权限码 | 说明 |
|--------|------|
| `tenancy.tenants.read` | 读取租户列表 |
| `identity.users.write` | 新增或修改用户 |
| `jobs.definitions.trigger` | 立即执行任务 |
| `identity.roles.data_scope.configure` | 配置角色数据范围 |

### 5.5 错误码

```text
{module}.{area}.{reason}
```

小写 snake_case。示例：`identity.password.minimum_length`, `tenancy.identifier.duplicate`。

### 5.6 集成事件消息类型

```text
{owner}.{module}.{entity}.{event}
```

示例：`fullnet.tenancy.tenant.provisioned`。`SchemaVersion` 使用独立正整数，不写入消息类型。

> Audit Event/Result Code、Agent Tool 名称及稳定枚举采用同一小写点分层原则，不使用 CLR 类型名或翻译文本。

### 5.7 SQL Statement 标识

```text
{module}.{verb_or_purpose}
```

每段小写 snake_case，如 `tenancy.provision_tenant`、`identity.list_users`。Provider 后缀使用 `.sql_server`、`.my_sql`。

## 6. 配置、缓存与客户端

### 6.1 配置与环境变量

| 层级 | 格式 | 示例 |
|------|------|------|
| .NET `IConfiguration` | PascalCase 冒号分层 | `Identity:SigningKeys:ActiveKeyId` |
| 环境变量 | 双下划线映射 | `Identity__SigningKeys__ActiveKeyId` |

> Secret 名称表达用途，不包含真实环境、账号或密钥值。

### 6.2 缓存键、Tag 与指标

```text
fullnet:{environment}:{tenant_or_host}:{module}:{resource}:{id}:{version}
```

示例：`fullnet:prod:tenant_01H:identity:user:profile:42:v1`。全小写冒号分段；模块/资源/Tag/版本片段只使用稳定小写 ASCII。

> OpenTelemetry Meter/Counter/Activity 使用小写点分层；标签 Key 使用稳定小写 snake_case 并遵守低基数规则。

### 6.3 客户端平台

| 平台 | 文件命名 | 类型/成员 |
|------|----------|----------|
| TypeScript / Vue 组件 | PascalCase；Composable `use{Name}.ts` | 类型 PascalCase，函数/变量 camelCase |
| 原生 JS / Layui | kebab-case | 导出函数 camelCase |
| uni-app 页面 | 小写 kebab-case | Vue 组件 / TS 沿用 Vue 规则 |
| Flutter / Dart | snake_case 文件 | UpperCamelCase 类型，lowerCamelCase 成员 |

> 各客户端可遵循平台文件命名惯例，但 JSON 字段、权限码、错误码和 API 路径不得自行改名。

## 7. SQL 与迁移代码风格

- SQL 关键字大写：`SELECT / FROM / WHERE / INSERT INTO / INNER JOIN`
- 表名小写 snake_case，列/参数 PascalCase；SQL Server 默认 `dbo` Schema，MySQL 使用配置数据库
- 参数与 Command/Query 属性同名：`WHERE TenantId = @TenantId`
- 禁止 `SELECT *`；禁止无 `WHERE` 的 `UPDATE/DELETE`；普通同名列不写机械 `AS`
- 排序字段/表名必须来自封闭白名单，不得拼接用户输入
- 复杂 SQL 靠近 Feature 保存（`Features/*Sql.cs`、`Persistence/*Sql.cs`）
- Provider 语法差异不得改变领域命名；两库保留字需在 Linux MySQL 容器验证表名大小写

### 7.1 迁移文件命名

- 目录：[`src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer) 与 [`.../MySql/`](file:///G:/wwwroot/github_fork/Full.NET/src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql)
- 文件名：`{NNN}_{PascalCasePurpose}.sql`，两库同名同序（如 `002_Identity.sql`、`017_OutboxDeadLetter.sql`、`030_JobsDefinitionAndExecution.sql`）
- MySQL 缺少 `ADD COLUMN/CONSTRAINT IF NOT EXISTS` 时允许 `INFORMATION_SCHEMA` + `PREPARE/EXECUTE`，但必须在同一变更内向 `contracts/naming/naming-debt.json` 登记精确 `dynamic_sql` 条目（含原因与最晚移除里程碑）

## 8. 存量债务

- 不兼容的存量命名精确登记在 [`contracts/naming/naming-debt.json`](file:///G:/wwwroot/github_fork/Full.NET/contracts/naming/naming-debt.json)
- 旧 → 新名称映射在 [`contracts/naming/pre-v1-name-map.json`](file:///G:/wwwroot/github_fork/Full.NET/contracts/naming/pre-v1-name-map.json)；典型 Pre-v1 债务：
  - 表 `fn_tenant_tenant` → `fn_tenancy_tenant`（OwnerKey 段错误）
  - 列 `CreatedAt` / `UpdatedAt` / `OccurredAt` / `ProcessedAt` / `NextAttemptAt` / `LockedUntil` → 全部加 `Utc` 后缀
  - 列 `fn_outbox_message.Type` → `MessageType`
  - 消息类型 `fullnet.tenancy.tenant-provisioned` → `fullnet.tenancy.tenant.provisioned`（连字符 → 点分）
  - MySQL 001-007 的 UUID 主键/外键/租约/Seed 仍使用 `char(36)`，尚未迁移为统一 RFC 字节序的 `BINARY(16)`
- 命名规范化使用 `expand -> migrate/backfill -> contract`，提供 SQL Server/MySQL 成对迁移、数据核对、部署顺序和回滚/前滚方案
- 第三方数据库若无法改名，必须在独立 Compatibility/Provider 层使用显式映射，并记录来源和退出条件；不得放宽 Full.NET 自有表规范
- 偏离本规范需要 ADR；`sys_`、运行时动态表前缀及隐式全局 snake_case 映射没有默认例外
- **新代码必须完全合规**，不得继承存量债务；触碰对应模块时必须更新技术债清单或执行已批准迁移计划
