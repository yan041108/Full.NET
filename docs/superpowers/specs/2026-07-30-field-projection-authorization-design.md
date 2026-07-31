# 稳定字段投影授权设计

**状态：** Approved for implementation
**日期：** 2026-08-01
**首个资源：** `identity.host_users`

## 1. 决策摘要

Admin.NET.Pro 的角色字段配置证明了“角色可以决定管理页面可见字段”具有产品价值，但其以运行时实体反射、物理表名和列名作为授权标识，且现有查询链未形成可证明的服务端强制投影。Full.NET 只吸收产品意图，不复制该实现。

Full.NET 使用编译期注册的语义资源目录。公共授权只包含稳定的 `ResourceKey`、`FieldKey`、`Sensitivity` 和 `DefaultVisibility`；物理表名、列名、反射类型和 SQL 片段永不进入请求、响应或持久化授权。角色授权的并集必须受资源目录约束，未知或已退役字段失败关闭。

首个纵向切片覆盖 Host Users 的列表、详情和导出。三条读取路径使用同一个有效字段集合，并只从服务端维护的固定 SQL 投影片段中选择列，任何请求值都不能拼接为 SQL 标识符。

## 2. 兼容与字段分类

资源键固定为 `identity.host_users`。现有 v1 响应中的以下字段保持必选且默认可见，角色不能移除：

- `id`
- `username`
- `display_name`
- `is_active`
- `created_at_utc`
- `updated_at_utc`
- `version`

首批受限字段为：

| FieldKey | 敏感级别 | 默认可见 | 备注 |
| --- | --- | --- | --- |
| `preferred_locale` | Internal | 否 | 用户区域偏好 |
| `failed_login_count` | Sensitive | 否 | 认证风险信息 |
| `lockout_end_utc` | Sensitive | 否 | 账号锁定状态 |

下列字段永久禁止进入目录：`password_hash`、`security_stamp`、`normalized_username`。新增字段默认不可授权，只有安全评审后显式注册到目录才可使用。

## 3. 授权模型

`IUserFieldProjectionResolver.ResolveAsync(userId, tenantId, resourceKey, cancellationToken)` 返回有序、不可变的有效字段集合：

1. 校验资源键存在；不存在时失败关闭。
2. 查询用户当前有效角色，仅接受与目标资源作用域完全匹配的角色。Host 资源要求角色 `ScopeKey = 'host'` 且 `TenantId IS NULL`。
3. 必选字段始终加入结果。
4. 普通角色的显式授权取并集，再与目录的可授权字段取交集。
5. 有效的 Host 超级管理员角色获得该资源全部可授权字段；该能力不跨租户或作用域继承。
6. 未知、退役、重复和大小写不规范的持久化字段均不扩大结果。

数据库不存显式 deny。受限字段“没有 grant 即拒绝”，避免 allow/deny 优先级形成隐含覆盖规则。角色字段授权变更必须与角色版本递增处于同一事务，提供并发控制和缓存撤销证据。

## 4. 数据模型与权限

表 `fn_identity_role_field_grant` 保存：`Id`、`RoleId`、`ResourceKey`、`FieldKey`、`CreatedAtUtc`、`CreatedById`。唯一约束为 `(RoleId, ResourceKey, FieldKey)`，并通过外键依附角色生命周期。表不重复保存租户或作用域；所有读取均与角色表连接并验证角色的实际边界。

管理 API：

- `GET /api/v1/identity/field-projections/catalog`
- `GET /api/v1/identity/roles/{roleId}/field-grants`
- `PUT /api/v1/identity/roles/{roleId}/field-grants`

目录和授权读取要求 `identity.role_field_grants.read`，替换授权要求 `identity.role_field_grants.write`。写入必须拒绝未知资源、必选字段、不可授权字段、重复字段、系统角色和版本冲突，并记录行为审计。

## 5. 查询强制与 API 形状

Host Users 的基础 7 字段继续使用现有强类型属性。受限字段作为可选的 `projectedFields` 对象返回，并携带 `effectiveFieldKeys`，从而区分“无权查看”和“有权但数据库值为 null”。旧客户端忽略新增属性，现有 v1 构造与校验保持兼容。

列表、详情和导出均先解析一次有效字段集合，再选择预定义 SQL 语句或固定片段。实现不得 `SELECT *`，不得从 `FieldKey` 推导列名，也不得先读取全部敏感列后在内存中删除。导出使用与列表相同的资源键和有效字段集合，并受独立的 `identity.users.export` Endpoint 权限保护。

## 6. 缓存与撤销安全

第一阶段解析结果不使用跨请求缓存，以数据库读取换取明确的撤销一致性；角色授权替换提交后，下一请求立即重新解析。后续若引入缓存，缓存键必须包含用户有效角色及角色版本指纹，旧条目在角色版本变化后不可达，标签删除只能作为回收优化而不能作为安全正确性的唯一条件。

## 7. 双端行为

Vue 与 Layui 的角色管理页使用同一目录与 grant API，只展示 `Assignable = true` 的字段。Host Users 页面先读取当前用户的有效字段集合，再决定是否显示受限列；客户端隐藏仅用于体验，服务端 SQL 投影才是授权边界。两端必须一致处理 403、409 和目录退役字段。

## 8. 验收与非目标

验收必须证明：多角色并集、未知字段失败关闭、超级管理员作用域约束、必选字段不可移除、三条读取路径字段一致、SQL 未读取未授权敏感列、角色版本并发控制、SQL Server/MySQL 等价、OpenAPI 和双端契约一致。

本切片不提供任意实体反射、用户级字段覆盖、显式 deny、表达式字段、动态 SQL 列名、租户自定义字段目录，也不把 `ITenantIdFilter`、`IDeletedFilter`、`IOrgIdFilter` 或实体基类标记直接转换为字段授权。那些概念对数据范围和实体治理仍有参考价值，但不能替代此处的读取投影授权。
