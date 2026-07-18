# ADR-0003：UUID v7 主键与跨数据库物理存储

- 状态：已批准
- 日期：2026-07-18
- 决策者：项目所有者在当前任务中明确确认
- 适用范围：Full.NET 官方表、项目脚手架默认表、Dapper 数据边界、数据库迁移、代码生成器、HTTP/JSON 契约与客户端标识类型
- 正式规格：[Full.NET 总体架构设计规格](../../superpowers/specs/2026-07-17-fullnet-architecture-design.md)
- 命名规范：[Full.NET 命名规范](../../../rules/naming-conventions.md)

## 背景

Full.NET 需要在应用写入数据库前得到主键，使父子记录、审计、Outbox 和领域事件能够在同一事务中直接引用；模块、服务、离线客户端和导入程序也不应依赖数据库自增序列才能生成标识。当前代码已经通过 `IIdGenerator` 使用 `Guid.CreateVersion7()`，SQL Server 使用 `uniqueidentifier`，但 MySQL 现有迁移仍使用 `char(36)`。

`char(36)` 可读且兼容简单，但每个值占用更多索引空间，比较和连接成本高于 16 字节二进制。直接在业务模块中调用 `Guid.ToByteArray()`、`UUID_TO_BIN(..., 1)` 或自行交换字节，又会导致不同模块、导入工具和客户端产生不兼容的物理顺序。SQL Server 的 `uniqueidentifier` 比较顺序也不等同于 RFC 9562 UUID v7 的字节顺序，因此不能仅凭“使用 UUID v7”就假定聚集索引天然按时间追加。

## 候选方案

### 方案一：两库都使用文本 UUID

SQL Server/MySQL 都以 36 字符文本持久化。实现直观，但放弃 SQL Server 原生 `uniqueidentifier`，并持续承担更大的 MySQL 主键、外键和二级索引空间成本。

### 方案二：逻辑 UUID v7，按 Provider 使用原生/紧凑物理类型（采用）

C# 和公共契约统一使用 UUID v7；SQL Server 使用 `uniqueidentifier`，MySQL 使用 `BINARY(16)`，RFC 9562 网络字节序转换只发生在 Full.NET 数据访问边界。该方案保留应用侧生成与跨节点去中心化能力，同时缩小 MySQL 索引。

### 方案三：默认使用 Snowflake `bigint`

它具有紧凑、排序友好等优点，但需要节点号、时钟回拨和生成器运维治理；JavaScript 还必须把 64 位整数序列化为字符串。它适合作为明确选择的项目模板，而不是 Full.NET 核心默认值。

### 方案四：两库都使用 `BINARY(16)`

物理宽度一致，但 SQL Server 会失去成熟的 `uniqueidentifier` 驱动映射和运维工具体验，同时仍不能消除 SQL Server 特有的聚集索引顺序问题，收益不足以抵消额外转换层。

## 决策

### 1. 逻辑标识

1. Full.NET 官方框架与官方模块的单列默认主键采用应用端生成的 UUID v7，C# 类型为 `Guid`，通过 `IIdGenerator` 在执行数据库写入前生成。
2. 空 `Guid` 不得作为已分配标识持久化。数据库默认值不得成为业务主键的主要生成机制。
3. 父子记录、审计、Outbox 和同一事务中的其他引用直接使用已经生成的 `Guid`，不依赖回读数据库生成值。
4. UUID v7 只解决唯一性、去中心化生成和近似时间排序，不构成授权凭据；它会暴露大致生成时间，外部资源仍必须执行身份、租户和权限校验。

### 2. Provider 物理类型

| 边界 | 类型/格式 | 强制要求 |
| --- | --- | --- |
| C# 领域、Command/Query、Dapper 参数 | `Guid` | 业务模块不得改用 `byte[]` 或字符串保存 UUID |
| SQL Server 主键/外键/租约标识 | `uniqueidentifier` | 由 Microsoft.Data.SqlClient 直接绑定 |
| MySQL 主键/外键/租约标识 | `BINARY(16)` | 使用 RFC 9562 大端/网络字节序，禁止 time-swap |
| HTTP/JSON/OpenAPI | 规范 UUID 文本 | 小写 `8-4-4-4-12`；客户端按字符串处理 |
| 日志、缓存键和消息头 | 规范 UUID 文本 | 只在边界格式化，不改变数据库物理字节 |

MySQL 最终连接必须由 `Full.NET.Data.Dapper` 统一强制 `GuidFormat=Binary16`，其语义与 `UUID_TO_BIN(uuid, 0)`/`BIN_TO_UUID(bytes, 0)`一致。禁止使用 `TimeSwapBinary16`、`UUID_TO_BIN(uuid, 1)`、`Guid.ToByteArray()` 或业务模块自定义字节交换。Migrator、测试夹具、导入工具和后台 Worker 必须复用同一连接策略或统一存储编解码契约。

同一关系链中的主键和外键必须使用相同物理类型与字节序。复合关系主键继续由其组成标识构成，不为方便转换额外增加代理 `Id`。

### 3. SQL Server 聚集索引

SQL Server 的主键约束和聚集索引是两个独立决策，不得依赖 `PRIMARY KEY` 的隐式聚集默认值：

1. 每个新表必须显式声明主键是否 `CLUSTERED`/`NONCLUSTERED`，并显式命名主键与聚集索引。
2. Outbox、审计、执行记录、历史和其他高频追加表，默认使用 UUID 主键非聚集索引，并依据真实读取/清理路径设计显式聚集索引，例如 `(OccurredAtUtc, Id)` 或 `(CreatedAtUtc, Id)`。
3. 租户列表型业务表只有在查询证据支持时，才使用 `(TenantId, CreatedAtUtc, Id)` 等复合聚集键；不得把未经验证的模板推广到所有表。
4. 关系表的复合主键若与主导连接顺序一致，可以显式作为聚集索引。
5. 低写入、字典型表可以显式选择 UUID 主键聚集索引，但必须在迁移审查中记录理由；高写入表必须提供页分裂、碎片率和典型查询基准证据。

### 4. 可选 Snowflake 标识

具体项目可在脚手架阶段通过独立 ADR 选择 Snowflake `long`/数据库 `bigint`，但不得把同一实体的 UUID 与 Snowflake 混用。生成器必须同时产出节点号、时钟回拨、容量和冲突测试方案；HTTP/JSON 对 JavaScript 客户端必须序列化为十进制字符串。Full.NET 官方核心表继续使用 UUID v7。

## 存量迁移

现有 MySQL `char(36)` 是 1.0 前存储债务，不修改 001-006 历史迁移。迁移采用独立的 `expand -> verify -> switch -> contract` 计划：

1. 冻结所有 UUID 主键、外键、租约和审计引用清单；
2. 添加 `BINARY(16)` 影子列并使用 `UUID_TO_BIN(value, 0)` 回填；
3. 验证非空、版本位、唯一性、主外键引用和 `BIN_TO_UUID(value, 0)` 往返；
4. 在停止 API/Worker 写入、完成备份且新连接策略已验证后切换；
5. 经过升级、回退演练和观察窗口后删除文本列并重建目标约束/索引。

若无法证明旧、新值一一对应，迁移必须停止，禁止用截断、随机替换或跳过坏行继续执行。正式迁移编号和依赖顺序由[专项实施计划](../../superpowers/plans/2026-07-18-uuid-v7-primary-key-storage.md)统一管理。

## 后果

正面后果：

- 应用在写库前即可获得主键，父子数据、审计和 Outbox 能在同一事务中直接引用；
- 多节点、导入程序和离线场景可以独立生成标准 UUID v7，不依赖中央序列；
- MySQL 主键/外键从 36 字符缩小为 16 字节，二级索引中复制的主键也同步缩小；
- 业务代码继续只处理 `Guid`，Provider 差异集中在 Full.NET 数据层；
- 外部 API 与各客户端保持稳定字符串契约，不暴露数据库物理格式。

成本与限制：

- 存量 MySQL 转换属于高风险数据迁移，必须有维护窗口、备份、核对和恢复演练；
- SQL Server 仍需按表设计聚集索引，UUID v7 不能替代查询与写入基准；
- 数据库人工查询 MySQL ID 时需要 `BIN_TO_UUID`，排障脚本必须提供可读投影；
- 第三方直接写库工具必须采用相同 RFC 字节序，否则应通过导入适配层而不是直接写表。

## 验证

- 单元测试验证固定 UUID 的 RFC 字节序、规范文本和空值拒绝；
- MySQL Testcontainers 验证 `Guid -> BINARY(16) -> Guid`、`UUID_TO_BIN(..., 0)` 与驱动结果完全一致，并显式证明 time-swap 结果不被接受；
- SQL Server/MySQL 集成测试验证主键、外键、事务、QueryMultiple、Outbox、Seed 和并发路径；
- 迁移测试覆盖空库、001-006 升级、部分 Expand 重跑、冲突数据拒绝、切换和恢复；
- SQL 扫描检查新 MySQL UUID 列不再使用 `char(36)`，业务模块不出现手写 UUID 字节转换；
- SQL Server 高写入表在 Contract 前提交聚集索引基准和执行计划证据；
- 能力状态在上述实现与双库验证完成前保持 `Designing`。

## 参考依据

- [RFC 9562：Universally Unique IDentifiers](https://www.rfc-editor.org/rfc/rfc9562.html)
- [MySqlConnector Guid Format 连接选项](https://mysqlconnector.net/connection-options/#guid-format)
- [MySQL `UUID_TO_BIN`/`BIN_TO_UUID`](https://dev.mysql.com/doc/refman/8.4/en/miscellaneous-functions.html#function_uuid-to-bin)
- [SQL Server `uniqueidentifier`](https://learn.microsoft.com/en-us/sql/t-sql/data-types/uniqueidentifier-transact-sql?view=sql-server-ver17)
