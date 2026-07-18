# Full.NET 命名体系设计

- 日期：2026-07-18
- 状态：已批准
- 决策来源：项目所有者确认保留 `fn_` 作为 Full.NET 所有权前缀，具体项目采用自己的 OwnerKey，不使用 `sys_` 作为默认前缀
- 实现状态：规范已确定；自动化门禁、存量数据库迁移和代码生成器命名内核尚未实现

## 1. 问题与目标

Full.NET 同时面向 .NET 10、Dapper、SQL Server、MySQL、DbUp、代码生成器、Vue/Layui、uni-app 和 Flutter。命名规则必须解决以下问题：

1. 快速判断表、契约和消息由 Full.NET 还是具体项目拥有；
2. 避免 MySQL Linux 表名大小写差异和两库标识符长度差异；
3. 让 Dapper 投影保持直接、显式、可审查，不依赖隐藏 ORM 约定；
4. 让 CodeGeneration 从同一个 Schema 稳定生成 SQL、C# 和多客户端契约；
5. 防止为了统一形式破坏已经持久化的表名、消息类型和公共错误码。

## 2. 方案比较

### 2.1 方案 A：所有权前缀＋Dapper 直接映射（采用）

- 表：`{owner}_{module}_{entity}` 小写 snake_case；
- Full.NET OwnerKey 固定为 `fn`，项目使用创建时冻结的项目键；
- 列：PascalCase，与 C# 属性直接同名；
- 协议：按平台使用 JSON camelCase、点分层稳定代码和小写路由。

优点是所有权清晰、MySQL 表名安全、Dapper 无需全局下划线映射，当前大多数代码和列名可以继续使用。代价是数据库表和列采用两种大小写风格，但这种差异是有意的平台边界，不是偶然混乱。

### 2.2 方案 B：数据库全量 snake_case（不采用）

该方案要求 `tenant_id AS TenantId` 等机械别名或 Dapper 全局下划线映射。它让数据库外观统一，但会扩大每条手写 SQL、代码生成器、第三方映射和存量迁移的复杂度；全局映射还会把隐式行为引入 Dapper-first 边界。lowercase 列名不是解决 MySQL Linux 表名大小写问题的必要条件。

### 2.3 方案 C：`sys_` 核心表＋项目前缀（不采用）

`sys_` 语义看似表示“系统管理表”，但 SQL Server 通过 `sys` 暴露系统目录，MySQL 默认安装 `sys` Schema。即使 `sys_identity_user` 在业务数据库中通常不会与系统对象发生直接名称冲突，也会让运维、权限审计和排障误判所有权。它还无法区分 Full.NET 官方模块和项目自建“系统功能”。

因此，`fn` 表示 Full.NET 发行物所有权，项目 OwnerKey 表示具体产品所有权。表是不是“后台系统表”由模块语义决定，不由 `sys_` 泛化前缀决定。

## 3. 统一命名模型

```text
Database table: {OwnerKey}_{ModuleKey}_{EntityKey}
Permission:     {module}.{plural_resource}.{action}
Error:          {module}.{area}.{reason}
Message type:   {owner}.{module}.{entity}.{event}
Cache key:      fullnet:{environment}:{tenant_or_host}:{module}:{resource}:{id}:{version}
Configuration:  Section:Subsection:Key
```

同一个 `FullNetSchema` 必须同时保存物理数据库名称和逻辑代码名称，不能在模板中重复猜测单复数、缩写或 snake/Pascal 转换。建议核心字段：

```text
OwnerKey
ModuleKey
EntityKey
DatabaseTableName
ClrTypeName
ApiResourceName
PermissionResourceName
Columns[]: DatabaseName + ClrPropertyName + JsonPropertyName
```

生成器允许从规范名称推导默认值，但用户确认并持久化后的名称成为 Schema 的显式数据；重新生成不得因词典升级静默改名。

## 4. 数据库决策

### 4.1 表与列

表名小写是跨平台要求。MySQL 在多数 Unix 环境的表名比较受文件系统和 `lower_case_table_names` 影响；所有迁移和查询必须按完全相同的小写形式引用表。列名采用 PascalCase，与 C# 属性直接映射：

```sql
SELECT
    Id,
    TenantId,
    NormalizedUsername,
    CreatedAtUtc
FROM fn_identity_user
WHERE TenantId = @TenantId;
```

只有联表冲突、计算列或不同语义投影需要 `AS`。本设计不引入 Dapper.FluentMap，不设置全局 `MatchNamesWithUnderscores`，也不因命名规范强制引入 Dapper.SqlBuilder。

### 4.2 长度与确定性摘要

SQL Server 普通标识符上限为 128 字符，MySQL 的表、列、索引和约束上限为 64 字符，因此 Full.NET 使用 64 字符共同下限。表和列超长时必须重新命名；索引和约束由生成器按统一算法压缩：

```text
if length <= 64: full_name
else: first_55_ascii_chars + "_" + first_8_hex(SHA256(UTF8(full_name)))
```

摘要输入是未截断的规范完整名称，输出必须跨操作系统、文化区和进程稳定。碰撞测试覆盖大量生成名称；发生碰撞必须失败，不能追加随机数。

### 4.3 时间语义

所有时间线瞬间显式带 `Utc`。事件/审计通常使用 `AtUtc`，有效区间和租约可使用 `FromUtc`、`ToUtc`、`UntilUtc`、`EndUtc`。日历日期不带 Utc。后缀用于防误用，但实际写入仍由 `IClock`、类型和测试保证。

### 4.4 不使用运行时动态前缀

项目 OwnerKey 只在创建项目或生成初始迁移时确定。运行时根据配置替换表前缀会导致：

- SQL 无法作为固定可审查资产；
- DbUp 历史与应用 SQL 可能指向不同对象；
- Statement 缓存、执行计划和测试矩阵分叉；
- 标识符无法参数化，只能进行危险字符串拼接。

需要同一部署承载多个客户时使用 `TenantId` 或已批准的独立数据库租户 Provider，而不是为每个租户修改表前缀。

## 5. C# 与纵向 Feature

C# 遵循 .NET 平台惯例：类型与公开成员 PascalCase，参数和局部变量 camelCase，接口前缀 `I`，异步方法后缀 `Async`。Full.NET 固定缩写词典，尤其使用 `Id` 而非 `ID`、`FullNet` 而非 `FullNET`。

Feature 以行为命名：`ProvisionTenant`、`RefreshSession`、`GetCurrentUser`。跨 Feature 消息使用完整名称，例如 `ProvisionTenantCommand`；处于唯一 Feature Namespace 内的基础适配类型可以使用 `Handler`、`Endpoint`、`Validator`。HTTP 类型使用 `Request/Response`，持久化专用类型使用 `Row`，避免 `Dto/Model/Info` 等无法表达边界的后缀。

## 6. API、权限、错误和消息

### 6.1 HTTP

- 路径：`/api/v1`＋小写 kebab-case；集合资源使用复数；
- JSON：C# PascalCase，通过 System.Text.Json 输出 camelCase；
- 已发布路径和字段不能因生成器词典变化自动重命名。

### 6.2 权限与错误

权限码使用 `{module}.{plural_resource}.{action}`。错误码使用 `{module}.{area}.{reason}`，每个分段采用小写 snake_case。点号表达语义层级，下划线连接同一个语义词组，禁止混用连字符。

当前 Identity/Tenancy 已存在 `invalid-password`、`domain-exists`、`context_mismatch` 等混合形式。它们是公共契约债务，不能只改常量值；1.0 前规范化必须同步资源键、ProblemDetails、客户端回退、测试和兼容说明。

### 6.3 Outbox

消息类型使用 `{owner}.{module}.{entity}.{event}`，例如 `fullnet.tenancy.tenant.provisioned`。`SchemaVersion` 独立保存，禁止把 CLR 全名或版本拼入 MessageType。

当前 `fullnet.tenancy.tenant-provisioned` 已可能持久化。规范化期间 Worker 必须同时识别旧类型或先排空旧 Outbox，保留明确退役窗口，不能直接让旧消息变成毒消息。

## 7. CodeGeneration 职责

代码生成器不是简单字符串转换器，而是命名规则的执行入口：

1. 读取并验证 OwnerKey、ModuleKey、EntityKey；
2. 使用固定词典处理缩写、单复数和保留字；
3. 生成表、列、约束、C#、API、权限和客户端名称；
4. 生成 SQL Server/MySQL 同名对象与配对迁移；
5. 对长约束名执行稳定摘要；
6. 输出命名报告，列出显式覆写、兼容别名和冲突；
7. 重复生成必须无 Git 漂移。

Schema 导入不能看到 `fn_identity_user` 后就无条件猜出 `IdentityUser`。生成器先拆分 Owner/Module/Entity，再在模块上下文中默认生成 `User`；是否需要 `IdentityUser` 必须由显式领域名称决定。

## 8. 存量兼容策略

### 8.1 新增内容

规范批准后，新增表、列、代码和协议立即遵守正式规则。自动化门禁尚未落地前由审查执行，不能复制存量不一致形式。

### 8.2 已识别债务

| 债务 | 目标 | 处理方式 |
| --- | --- | --- |
| `fn_tenant_tenant` | `fn_tenancy_tenant` | 双库 expand/copy/switch/contract；不修改历史迁移 |
| Tenancy `CreatedAt/UpdatedAt` | `CreatedAtUtc/UpdatedAtUtc` | 新列、回填、应用切换、延后删除旧列 |
| Outbox 时间列缺少 `Utc` | 对应 `...Utc` | 保持 Worker 可升级顺序的分阶段迁移 |
| Outbox `Type` | `MessageType` | 新列回填和双版本读取；不得丢失待处理消息 |
| 错误/Audit/Statement 使用连字符 | 点分层＋snake_case 分段 | 按公共/内部契约分类；公共值提供兼容窗口 |
| 随机或非规范约束名 | 显式规范名 | 新对象立即执行，旧对象按模块迁移处理 |

上述目标是 `Designing`，不是已完成能力。迁移必须遵守破坏性 DDL 规则、SQL Server/MySQL 双库验证和发布前备份/数据核对。

## 9. 验收

命名治理达到 `Verified` 需要：

1. 机器可读 Naming Profile、保留字表、缩写词典和精确技术债清单进入仓库；
2. C#、SQL、迁移、协议和生成器命名检查进入 CI；
3. SQL Server/MySQL Linux 容器通过迁移、大小写和 Dapper 映射测试；
4. 当前 Foundation/Identity/Tenancy 债务按批准计划完成或保留有期限的精确豁免；
5. CodeGeneration 使用同一命名内核并通过快照、重复生成和碰撞测试；
6. 公共契约改名具有版本、兼容说明和客户端验证。

## 10. 参考依据

- [C# 标识符命名规则](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names)
- [.NET EditorConfig 命名规则](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/naming-rules)
- [MySQL 标识符长度限制](https://dev.mysql.com/doc/mysql-reslimits-excerpt/8.0/en/identifier-length.html)
- [MySQL 标识符大小写](https://dev.mysql.com/doc/refman/8.4/en/identifier-case-sensitivity.html)
- [MySQL sys Schema](https://dev.mysql.com/doc/refman/8.0/en/sys-schema.html)
- [SQL Server 数据库标识符](https://learn.microsoft.com/en-us/sql/relational-databases/databases/database-identifiers?view=sql-server-ver17)
