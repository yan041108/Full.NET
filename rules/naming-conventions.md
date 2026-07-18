# Full.NET 命名规范

## 1. 状态、范围与目标

- 状态：强制
- 来源：项目所有者于 2026-07-18 确认采用“Full.NET 所有权前缀＋项目所有权前缀＋Dapper 直接映射”的命名体系，并明确不使用 `sys_` 作为框架默认表前缀
- 适用范围：数据库对象、SQL、C#、HTTP/JSON、错误码、权限码、消息类型、缓存键、配置、前端与生成器产物
- 生效方式：新增或修改的名称必须遵守本文；已发布或已持久化名称按第 10 节兼容治理，禁止仅为形式统一静默重命名

命名的首要目标是表达所有权、边界和稳定语义，并让 SQL Server、MySQL、Dapper、代码生成器和多客户端共享同一套可验证契约。可读性不能以隐藏映射、运行时动态表名或破坏兼容性为代价。

## 2. 全局原则

1. **名称表达所有权。** 数据库表、消息类型、权限码和错误码必须能从名称判断所属框架、项目和模块。
2. **物理名称与代码名称允许采用不同平台惯例。** 表名使用小写 snake_case；数据库列与 C# 属性使用 PascalCase；HTTP JSON 使用 camelCase。
3. **Dapper 直接映射优先。** 数据库列默认与 C# 投影属性同名，不启用全局 snake_case 隐式映射，也不要求每个普通列机械书写 `AS`。
4. **稳定契约不可随意美化。** API 路径、JSON 字段、错误码、权限码、消息类型、配置键、缓存键和数据库对象一旦发布或持久化，重命名必须版本化或提供兼容迁移。
5. **跨数据库采用共同下限。** 普通标识符仅使用 ASCII 英文字母、数字和下划线；数据库对象名不得超过 MySQL 的 64 字符限制。
6. **禁止依赖引用符解决坏名称。** 新名称不得使用 SQL Server/MySQL 保留字、空格、连字符、非 ASCII 字符或需要 `[]`、反引号才能工作的形式。
7. **缩写统一。** 项目词典使用 `FullNet`、`Id`、`Ids`、`Api`、`Http`、`Https`、`Json`、`Html`、`Sql`、`Jwt`、`Uri`、`Url`、`Ip`、`Ui`、`Utc`、`Uuid`、`Csp`、`Csrf` 和 `Grpc`；禁止在同一语义中混用 `ID/Id`、`JSON/Json` 或 `FullNET/FullNet`。

## 3. 数据库所有权与表名

### 3.1 统一格式

```text
{owner_key}_{module_key}_{entity_key}
```

- `owner_key`：表的发布与迁移所有者；
- `module_key`：稳定的限界上下文标识；
- `entity_key`：模块内单数实体或关系名称。

三段都使用小写 snake_case，完整名称最多 64 个 ASCII 字符。表名不得由运行时配置拼接；生成后必须作为迁移和 SQL 的固定常量进入版本控制。

### 3.2 所有权前缀

- `fn` 仅保留给 Full.NET 官方框架和官方模块，例如 `fn_identity_user`、`fn_outbox_message`；
- 具体产品使用在项目脚手架创建时冻结的项目键，例如 `crm_sales_order`；
- 项目扩展官方模块仍使用项目所有权，例如 `crm_identity_user_profile`，不得冒充 `fn` 官方表；
- `owner_key` 必须匹配 `^[a-z][a-z0-9]{1,11}$`；发布后修改 OwnerKey 属于数据库兼容变更；
- 禁止使用 `sys`、`mysql`、`information_schema`、`performance_schema`、`dbo`、`fn`（项目表）及数据库保留字作为项目 OwnerKey。

`sys_` 不作为 Full.NET 默认前缀。SQL Server 使用 `sys` 暴露系统目录，MySQL 默认安装 `sys` Schema；把业务或框架表命名为 `sys_*` 会混淆数据库系统对象与应用对象的所有权。

### 3.3 模块与实体键

- ModuleKey 必须稳定并匹配 `^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$`，例如 `identity`、`tenancy`、`organization`、`files`、`notifications`、`jobs`、`codegen`、`seed`、`outbox`；
- EntityKey 默认使用单数，例如 `user`、`tenant`、`message`；关系表按参与实体的稳定顺序命名，例如 `fn_identity_user_role`；
- 模块名称本身可为领域固有复数词，例如 `files`、`settings`，不能为了语法形式擅自改成另一个 ModuleKey；
- 禁止无意义后缀 `table`、`data`、`info`；只有领域中确有区别时才使用限定词；
- 新表若超过 64 字符必须缩短 ModuleKey 或 EntityKey并通过审查，禁止自动截断表名。

### 3.4 Schema

- SQL Server 默认使用 `dbo`；MySQL 使用配置选择的数据库；
- 不以 SQL Server 独有的每模块 Schema 代替表前缀，因为 MySQL 的 Schema 语义不同；
- 所有 SQL 必须按 Provider 既定方式限定对象，但表名本身在两库保持一致。

## 4. 数据库列名与字段语义

### 4.1 基本形式

数据库列使用 PascalCase，并与 C# 持久化投影属性同名：

```text
Id
TenantId
NormalizedUsername
CreatedAtUtc
```

普通 Dapper 查询直接选择同名列。只有计算列、联表冲突或投影语义与物理列不同才使用 `AS PascalCaseProperty`；禁止启用 Dapper 全局下划线匹配或引入 FluentMap 作为默认桥接层。

### 4.2 标识和关系

- 单一主键使用 `Id`；复数标识集合在代码中使用 `Ids`；
- 外键使用 `{role}Id`，例如 `TenantId`、`UserId`、`CreatedById`；同一目标存在多个角色时必须表达角色，例如 `ApprovedById`、`ReplacedById`；
- 复合主键不再增加无业务用途的代理 `Id`；
- 租户业务表的租户边界列固定为 `TenantId`，不得使用 `TenantID`、`Tenant` 或 `OrganizationId` 代替隔离语义。

Full.NET 官方表的单列默认标识采用应用端 UUID v7，逻辑类型固定为 C# `Guid`。应用必须在数据库写入前通过 `IIdGenerator` 生成非空标识；数据库默认值不作为业务主键的主要生成机制。父子记录、审计、Outbox 和同一事务中的其他引用直接使用已生成的 `Guid`。

Provider 物理类型固定如下：

| Provider/边界 | UUID 类型或格式 |
| --- | --- |
| SQL Server | `uniqueidentifier` |
| MySQL | RFC 9562 大端/网络字节序 `BINARY(16)` |
| C# 与 Dapper 参数 | `Guid` |
| HTTP/JSON/OpenAPI | 小写规范 UUID 字符串 |

- MySQL 必须由 Full.NET 数据边界统一使用 `GuidFormat=Binary16` 或等价的受测编解码契约；业务模块不得把 UUID 改为 `byte[]`/字符串，也不得调用 `Guid.ToByteArray()`、`UUID_TO_BIN(..., 1)`、`TimeSwapBinary16` 或自行交换字节；
- 同一关系链的 UUID 主键、外键、租约与审计引用必须采用相同物理类型和字节序；
- API、缓存键、日志和消息头只在边界输出规范 UUID 文本，禁止把 MySQL 二进制表现泄漏给客户端；
- 项目模板选择 Snowflake `long` 必须有独立 ADR；对 JavaScript 客户端的 JSON 值必须使用十进制字符串，禁止与同一实体的 UUID 混用。

### 4.3 时间、日期和时区

- 表示时间线瞬间的列必须显式带 `Utc`，通常使用 `CreatedAtUtc`、`OccurredAtUtc`、`ExpiresAtUtc`、`LockedUntilUtc`、`ValidFromUtc`；
- 仅表示日历日期时使用 `Date`，例如 `BirthDate`，不得伪加 `Utc`；
- 当地墙上时间使用 `LocalTime`，时区使用规范 `TimeZoneId`，显式偏移量必须带单位，例如 `OffsetMinutes`；
- 持续时间、容量、距离等数值必须在名称或类型中体现单位，例如 `TimeoutSeconds`、`SizeBytes`；
- C# 时间线瞬间默认使用 `DateTimeOffset`。写入 UTC 的行为仍必须验证，后缀不能代替转换与测试。

### 4.4 状态、审计和扩展字段

- 布尔列使用 `Is`、`Has`、`Can`、`Should` 等谓词前缀，禁止 `Flag`、`StatusBool` 和 `0/1` 语义名称；
- 乐观并发列使用 `Version`；消息 Schema 使用 `SchemaVersion`；用户资料版本等业务版本必须带领域限定词；
- 采用软删除的表使用 `IsDeleted`、`DeletedAtUtc`、`DeletedById`，但不得为了模板整齐强迫所有表支持软删除；
- 创建和更新审计按真实需要使用 `CreatedAtUtc`、`CreatedById`、`UpdatedAtUtc`、`UpdatedById`；
- 存储 JSON 文本的列使用 `{Purpose}Json`，例如 `ExtendedPropertiesJson`。仅当字段通过 JSON 准入门禁时使用该后缀；
- 二进制消息正文使用 `Payload`，内容格式由 `ContentType` 和 `SchemaVersion` 表达，不在列名重复编码格式。

## 5. 主键、外键、索引和约束

| 对象 | 格式 | 示例 |
| --- | --- | --- |
| 主键 | `PK_{table}` | `PK_fn_identity_user` |
| 外键 | `FK_{table}_{column}` | `FK_fn_identity_user_role_UserId` |
| 唯一索引/约束 | `UX_{table}_{key_columns}` | `UX_fn_identity_user_ScopeKey_NormalizedUsername` |
| 普通索引 | `IX_{table}_{key_columns}` | `IX_fn_identity_refresh_session_UserId_ExpiresAtUtc` |
| 检查约束 | `CK_{table}_{rule}` | `CK_fn_identity_role_TenantScope` |
| 默认约束 | `DF_{table}_{column}` | `DF_fn_identity_user_Version` |

规则：

1. 所有主键和约束必须显式命名，禁止依赖数据库生成的随机名称。
2. 索引列按键顺序写入名称；Include 列不写入名称。两个索引键相同但筛选或用途不同，追加简短 PascalCase 用途。
3. 名称只使用 ASCII 字母、数字和下划线，完整长度最多 64 字符。
4. 生成名称超过 64 字符时，统一使用：规范完整名的 UTF-8 SHA-256 前 8 位小写十六进制摘要，并输出“前 55 字符＋下划线＋8 位摘要”。人工不得自创其他截断算法。
5. 自动截断仅适用于索引和约束；表名、列名、API 字段和稳定业务代码超长时必须重新命名。
6. SQL Server 的主键约束与聚集索引必须分别显式决定。高频追加表默认使用 UUID 主键非聚集索引，并按真实查询/清理路径设计显式聚集索引；关系表可在复合主键顺序与主导连接一致时显式聚集；禁止依赖 `PRIMARY KEY` 的 Provider 默认聚集行为。
7. SQL Server 聚集键不得从通用模板盲目复制。Outbox、审计、执行记录和历史表需优先评估 `(OccurredAtUtc, Id)`、`(CreatedAtUtc, Id)` 等时间路径；租户列表表只有在查询证据支持时才评估 `(TenantId, CreatedAtUtc, Id)`。高写入表在发布前必须验证页分裂、碎片率和典型执行计划。

## 6. SQL 与迁移

- SQL 关键字使用大写，表名使用规范小写，列名和参数使用 PascalCase；
- 参数与 Command/Query 属性同名，例如 `WHERE TenantId = @TenantId`；
- 禁止 `SELECT *`，必须显式列出投影；普通同名列不写机械别名；
- 排序字段、表名和列名不能直接来自用户输入，只能来自封闭白名单；
- 复杂 SQL 靠近 Feature 保存，Statement 标识使用 `{module}.{verb_or_purpose}`，每段为小写 snake_case；Provider 后缀使用 `.sql_server`、`.my_sql`；
- SQL Server/MySQL 迁移文件使用相同的 `{NNN}_{PascalCasePurpose}.sql` 文件名和顺序；
- SQL Server 与 MySQL 使用相同表、列、索引和约束名称，Provider 语法差异不得改变领域命名；
- 新名称必须检查两库保留字并在 Linux MySQL 容器验证表名大小写；禁止通过修改 `lower_case_table_names` 掩盖不一致 SQL。

## 7. C#、项目和 Feature

### 7.1 .NET 标识符

- Namespace、类型、方法、属性、事件和常量使用 PascalCase；接口使用 `I` 前缀；
- 参数、局部变量和非公开实例字段使用 camelCase，私有实例字段使用 `_camelCase`；
- positional record 的主构造参数同时生成公开属性，因此使用 PascalCase；该例外不适用于普通构造函数或方法参数。EditorConfig 对参数保留 IDE 建议，Architecture Tests 对排除编译器生成 record 成员后的普通方法参数执行硬门禁；
- 异步方法使用 `Async` 后缀并接受 `CancellationToken`；
- 非 Flags 枚举使用单数名并显式赋整数值；Flags 枚举使用复数名和二进制幂值；
- 禁止把全大写蛇形用于 C# 常量；协议字符串的值按其协议规则，不因常量属性名改变大小写。

### 7.2 模块与领域类型

- 项目和 Namespace 沿用 `Full.NET.{Layer}.{Module}`；代码标识符中的品牌写作 `FullNet`；
- 模块 Namespace 已表达上下文时，领域实体不重复模块名，例如 `Full.NET.Modules.Tenancy.Domain.Tenant`，不命名为 `TenancyTenant`；
- 数据库读取专用类型使用 `Row` 后缀，领域投影按用途使用 `Summary`、`Details` 等，禁止笼统 `Model`；
- HTTP 边界使用 `Request`、`Response`，应用消息使用 `Command`、`Query`，跨模块使用具体业务契约名；禁止无语义 `Dto`、`Data`、`Info`；
- Feature 目录和 Namespace 使用 `VerbNoun`，例如 `ProvisionTenant`。跨 Feature 可见的消息使用完整名称 `ProvisionTenantCommand`；Feature 内且唯一的适配类型可使用 `Endpoint`、`Handler`、`Validator`。

### 7.3 文件

- C# 文件默认与主要类型同名；Feature 内短适配类型可使用 `Endpoint.cs`、`Handler.cs`、`Validator.cs`；
- 测试类以被测类型或能力结尾 `Tests`，测试方法表达 `场景_行为_结果` 或清晰的英文行为句，不使用序号代替语义；
- 生成文件只使用登记的 `.g.cs`、`.generated.ts`、`.generated.js` 等后缀。

## 8. HTTP、JSON 与稳定机器契约

### 8.1 API 与 JSON

- API 前缀为 `/api/v{major}`；后续路径使用小写 kebab-case；集合资源使用复数名词；
- 操作能表达为资源状态转换时不新增动词路由；确需动作时使用稳定小写 kebab-case；
- C# DTO 属性使用 PascalCase，System.Text.Json 对外输出 camelCase；禁止同一 API 混用 snake_case；
- Query 参数和 Header 遵循 HTTP/OpenAPI 约定，名称一旦发布按公共契约治理。

### 8.2 权限、错误和消息

- 权限码格式为 `{module}.{plural_resource}.{action}`，例如 `tenancy.tenants.read`；每段匹配 `^[a-z][a-z0-9_]*$`；
- 错误码格式为 `{module}.{area}.{reason}`；可省略无意义层级，但每段仍使用小写 snake_case，例如 `identity.password.minimum_length`；
- 集成消息类型格式为 `{owner}.{module}.{entity}.{event}`，例如 `fullnet.tenancy.tenant.provisioned`；SchemaVersion 使用独立正整数，不写入消息类型；
- Audit Event/Result Code、Agent Tool 名称及稳定枚举采用同一小写点分层原则，不使用 CLR 类型名或翻译文本；
- 已发布错误码、权限码和消息类型不得把连字符与下划线互换；任何规范化都必须按第 10 节兼容处理。

## 9. 配置、缓存和客户端

### 9.1 配置与环境变量

- .NET 配置节和 Key 使用 PascalCase 冒号分层，例如 `Identity:SigningKeys:ActiveKeyId`；
- 环境变量使用双下划线映射层级，例如 `Identity__SigningKeys__ActiveKeyId`；
- Secret 名称表达用途，不包含真实环境、账号或密钥值。

### 9.2 缓存、Tag 与指标

- 缓存键使用小写冒号分段：`fullnet:{environment}:{tenant_or_host}:{module}:{resource}:{id}:{version}`；
- 模块、资源、Tag 和版本片段只使用稳定小写 ASCII；禁止把翻译文本、原始 PII 或未规范化域名放入 Key；
- OpenTelemetry Meter、Counter 和 Activity 使用小写点分层；标签 Key 使用稳定小写 snake_case，并继续遵守低基数规则。

### 9.3 客户端平台

- TypeScript/JavaScript 类型和 Vue 组件使用 PascalCase，函数与变量使用 camelCase；Vue 组件文件使用 PascalCase，Composable 使用 `use{Name}.ts`；
- 原生 JS/Layui 多词文件和 HTML 路径使用 kebab-case，导出函数使用 camelCase；
- uni-app 页面路径使用小写 kebab-case，Vue 组件和 TypeScript 继续遵循 Vue 规则；
- Flutter/Dart 文件使用 snake_case，类型使用 UpperCamelCase，成员使用 lowerCamelCase；
- 各客户端可遵循平台文件命名惯例，但 JSON 字段、权限码、错误码和 API 路径不得自行改名。

## 10. 兼容、存量债务与例外

1. 数据库表/列重命名必须使用 `expand -> migrate/backfill -> contract`，提供 SQL Server/MySQL 成对迁移、数据核对、部署顺序和回滚/前滚方案；禁止直接修改旧迁移脚本伪造历史。
2. 公共 API、JSON 字段、错误码、权限码和消息类型重命名必须版本化或同时接受旧值；Outbox 旧消息未排空前必须保留旧版本 Handler。
3. 当前已确认的 1.0 前债务包括：`fn_tenant_tenant` 所有权段错误；Foundation Tenancy/Outbox 的 UTC 时间列缺少 `Utc`；Outbox `Type` 未表达 `MessageType`；错误码、审计码、Statement 标识和事件类型混用连字符/下划线；部分主键、外键和索引未遵循本规范的显式名称；MySQL 001-007 的 UUID 主键、外键、租约和 Seed 执行标识仍使用 `char(36)`，尚未迁移为统一 RFC 字节序的 `BINARY(16)`。
4. 上述债务只表示已识别，不表示已经修复。新增代码不得复制债务形式；触碰对应模块时必须更新技术债清单或执行已批准迁移计划。
5. 第三方数据库若无法改名，必须在独立 Compatibility/Provider 层使用显式映射，并记录来源和退出条件；不得放宽 Full.NET 自有表规范。
6. 偏离本文需要 ADR，说明范围、兼容影响、两库验证、代码生成器行为和恢复方式。`sys_`、运行时动态表前缀及隐式全局 snake_case 映射没有默认例外。

## 11. 验证

- `.editorconfig`/分析器检查 C# 命名；
- SQL 命名扫描检查表、列、索引、约束、长度、保留字和双库迁移配对；
- 协议契约测试枚举错误码、权限码、消息类型、指标和配置 Key；
- 代码生成器快照测试验证相同 Schema 重复生成无漂移、长名称摘要稳定且不会碰撞；
- SQL Server/MySQL Testcontainers 在 Linux 环境执行迁移和典型 Dapper 投影，验证大小写与直接映射；
- 固定 UUID 向量验证 MySQL `Guid -> BINARY(16) -> Guid`、`UUID_TO_BIN(value, 0)` 一致性、主外键引用和规范 API 文本；测试必须拒绝 time-swap 与业务层手工字节转换；
- SQL Server 迁移扫描验证主键与聚集属性显式声明；高写入表必须具有与实际访问路径对应的基准/执行计划证据；
- 现有债务必须位于精确 Allowlist，包含负责人范围、原因和最晚移除里程碑；禁止使用通配符豁免。

详细决策背景与实施顺序见：

- [`../docs/superpowers/specs/2026-07-18-fullnet-naming-conventions-design.md`](../docs/superpowers/specs/2026-07-18-fullnet-naming-conventions-design.md)
- [`../docs/superpowers/plans/2026-07-18-naming-governance.md`](../docs/superpowers/plans/2026-07-18-naming-governance.md)
- [`../docs/superpowers/plans/2026-07-18-pre-v1-naming-normalization.md`](../docs/superpowers/plans/2026-07-18-pre-v1-naming-normalization.md)
- [`../docs/architecture/adr/ADR-0003-uuid-v7-primary-key-storage.md`](../docs/architecture/adr/ADR-0003-uuid-v7-primary-key-storage.md)
- [`../docs/superpowers/plans/2026-07-18-uuid-v7-primary-key-storage.md`](../docs/superpowers/plans/2026-07-18-uuid-v7-primary-key-storage.md)
