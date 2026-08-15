# 命名规范摘要

> 完整规范：[`rules/naming-conventions.md`](file:///G:/wwwroot/github_fork/Full.NET/rules/naming-conventions.md)

## 1. 全局原则

| 原则 | 说明 |
|------|------|
| **名称表达所有权** | 从名称判断框架/项目/模块归属 |
| **跨平台惯例不同** | 表名：snake_case；列/C# 属性：PascalCase；HTTP JSON：camelCase |
| **Dapper 直接映射** | 列与属性同名，禁用全局 snake_case 匹配 |
| **稳定契约不可随意美化** | API/JSON/错误码/权限码/表名发布后必须版本化 |
| **共同下限** | 仅 ASCII 字母数字下划线，标识符 ≤ 64 字符 |
| **统一缩写** | FullNet, Id, Api, Json, Sql, Utc, Jwt, Uri，不混用 ID/Id, JSON/Json |

## 2. 数据库命名

### 2.1 表名：三段式

```text
{owner_key}_{module_key}_{entity_key}
```

| 部分 | 说明 | 示例 |
|------|------|------|
| `owner_key` | 发布与迁移所有者。**`fn` 仅 Full.NET 官方**，项目表使用脚手架冻结的项目键；禁止 `sys/mysql/dbo` | `fn`, `crm` |
| `module_key` | 稳定限界上下文标识 | `identity`, `tenancy`, `jobs` |
| `entity_key` | 单数实体/关系名 | `user`, `role`, `user_role` |

**表示例**：
| 正确 | 错误 |
|------|------|
| `fn_identity_user` | `sys_user`(禁 sys), `identity_users`(复数), `fn_identity_UserInfo`(无意义后缀) |
| `fn_identity_user_role` | `user_roles`, `user_to_role` |
| `fn_jobs_host_definition` | `host_job_def`(缩写不清), `fn_jobs_host_definitions` |

### 2.2 列名：PascalCase

| 列 | 说明 |
|----|------|
| `Id` | 单一主键 |
| `TenantId` | 租户边界列（**所有租户业务表固定此名**，不得 TenantID / OrganizationId） |
| `RoleId`, `CreatedById`, `ApprovedById` | 外键：{角色}Id |
| `CreatedAtUtc`, `UpdatedAtUtc`, `OccurredAtUtc`, `ExpiresAtUtc` | UTC 时间线瞬间必须带 Utc 后缀 |
| `BirthDate` | 仅日历日期不加 Utc |
| `TimeoutSeconds`, `SizeBytes` | 数值必须体现单位 |
| `IsEnabled`, `HasProfile`, `CanShare`, `ShouldNotify` | 布尔：Is/Has/Can/Should 前缀 |
| `Version` | 乐观并发；消息 Schema 用 `SchemaVersion` |
| `IsDeleted`, `DeletedAtUtc`, `DeletedById` | 软删除三件套（非全部表强制） |
| `ExtendedPropertiesJson` | JSON 文本列：{Purpose}Json 后缀 |
| `Payload` | 二进制消息正文 |

### 2.3 主键/索引/约束命名

| 对象 | 格式 | 示例 |
|------|------|------|
| 主键 | `PK_{table}` | `PK_fn_identity_user` |
| 外键 | `FK_{table}_{column}` | `FK_fn_identity_user_role_UserId` |
| 唯一索引 | `UX_{table}_{key_columns}` | `UX_fn_identity_user_ScopeKey_NormalizedUsername` |
| 普通索引 | `IX_{table}_{key_columns}` | `IX_fn_identity_refresh_session_UserId_ExpiresAtUtc` |
| 检查约束 | `CK_{table}_{rule}` | `CK_fn_identity_role_TenantScope` |

**SQL Server 主键注意**：高频追加表（Outbox/Audit/History）默认使用**非聚集** UUID 主键，聚集索引按时间路径设计 `(OccurredAtUtc, Id)`。

## 3. UUID 主键存储

- 逻辑类型：C# **`Guid`**（应用端生成 UUID v7，禁止依赖数据库默认值）
- Provider 物理类型：

| Provider | 类型 |
|----------|------|
| SQL Server | `uniqueidentifier` |
| MySQL | `BINARY(16)` RFC 9562 大端字节序 |

- API/JSON：输出小写规范 UUID 字符串（不泄漏 MySQL 二进制表现）
- 业务模块**禁止** `Guid.ToByteArray()` / `UUID_TO_BIN` / 自行交换字节序

## 4. C# / .NET 命名

| 类型 | 规范 | 示例 |
|------|------|------|
| Namespace / 类 / 方法 / 属性 | PascalCase | `HostUserManagementService` |
| 接口 | `I` 前缀 + PascalCase | `ICommandTransaction` |
| 异步方法 | `Async` 后缀 | `HandleAsync` |
| 枚举（非 Flags） | 单数 + 显式整数值 | `public enum DatabaseProvider { SqlServer = 1, MySql = 2 }` |
| Flags 枚举 | 复数 + 二进制幂 | `[Flags] public enum SeedProfiles { Baseline = 1, Development = 2, ... }` |
| 方法参数 / 局部变量 | camelCase | `tenantId`, `command` |
| 私有实例字段 | `_camelCase` | `_dbSession`, `_logger` |
| Positional record 主构造参数 | PascalCase（因为同时生成公开属性） | `record Error(ErrorType Type, string Code)` |
| 常量 | PascalCase（禁止 UPPER_SNAKE_CASE） | `public const int DefaultPageSize = 20` |

### 项目与命名空间

```
Full.NET.BuildingBlocks.{LayerName}    // BuildingBlocks
Full.NET.Modules.{ModuleName}          // 主模块
Full.NET.Modules.{ModuleName}.Contracts // 可选：稳定契约程序集
Full.NET.Host.{Role}                   // 宿主：Api / Worker / Migrator / AppHost
```

### 文件命名

- 默认与主要类型同名：`IdentityModule.cs`, `TenantResolver.cs`
- Feature 内短适配可简化：`Endpoint.cs`, `Handler.cs`, `Validator.cs`
- 生成文件：`.g.cs` / `.generated.ts` / `.generated.js` 后缀

## 5. HTTP / JSON / 稳定机器码

### 5.1 API 路径

```text
/api/v{major}/{kebab-case-plural-resource}
```

示例：`/api/v1/host/users`, `/api/v1/tenants/{id}/switch`, `/api/v1/settings/dict-types/by-code/{code}/items`

### 5.2 JSON

- C# 属性 PascalCase → System.Text.Json 对外 camelCase
- 禁止混用 snake_case
- 公开契约一旦发布，必须版本化或提供兼容迁移

### 5.3 权限码

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

### 5.4 错误码

```text
{module}.{area}.{reason}
```

小写 snake_case。示例：`identity.password.minimum_length`, `tenancy.identifier.duplicate`。

### 5.5 集成事件消息类型

```text
{owner}.{module}.{entity}.{event}
```

示例：`fullnet.tenancy.tenant.provisioned`。`SchemaVersion` 使用独立正整数，不写入消息类型。

## 6. 配置、缓存与客户端

### 6.1 配置与环境变量

| 层级 | 格式 | 示例 |
|------|------|------|
| .NET `IConfiguration` | PascalCase 冒号分层 | `Identity:SigningKeys:ActiveKeyId` |
| 环境变量 | 双下划线映射 | `Identity__SigningKeys__ActiveKeyId` |

### 6.2 缓存键

```text
fullnet:{environment}:{tenant_or_host}:{module}:{resource}:{id}:{version}
```

示例：`fullnet:prod:tenant_01H:identity:user:profile:42:v1`。全小写冒号分段。

## 7. SQL 代码风格

- SQL 关键字大写：`SELECT / FROM / WHERE / INSERT INTO / INNER JOIN`
- 表名小写 snake_case，列/参数 PascalCase
- 参数与属性同名：`WHERE TenantId = @TenantId`
- 禁止 `SELECT *`；禁止无 `WHERE` 的 `UPDATE/DELETE`
- 排序字段/表名必须来自封闭白名单，不得拼接用户输入

## 8. 存量债务

- 不兼容的存量命名精确登记在 `contracts/naming/naming-debt.json`
- 旧 → 新名称映射在 `contracts/naming/pre-v1-name-map.json`
- **新代码必须完全合规**，不得继承存量债务
