# Full.NET 租户上下文与权限导航设计

- 日期：2026-07-17
- 状态：已确认
- 决策方式：依据项目所有者“后续自动确认（按推荐方案来）”的持续授权，采用推荐方案
- 适用范围：Identity 最小 RBAC、Tenancy 上下文切换、Vue/Layui 动态权限导航

## 1. 背景与目标

身份与会话基础切片已经实现登录、短期 JWT、Refresh Token 轮换、CSRF、退出和双管理端会话恢复，但当前权限集合为空，租户卡片和导航仍是客户端静态演示数据。后续用户、角色、菜单和组织功能不能建立在客户端自报租户或仅隐藏按钮的基础上，因此本切片先收口 C1 的租户与权限导航底座。

本切片交付以下闭环：

1. 使用最小 RBAC 表为宿主管理员分配显式权限，不使用用户名或隐式超级管理员判断；
2. 宿主管理员查询可访问租户、进入有效租户上下文并安全返回 Host 上下文；
3. 当前租户上下文写入刷新会话，刷新与页面恢复后继续保持；
4. JWT 携带受信任的有效租户、作用域和权限声明，租户中间件不信任普通 Header、查询参数或请求体；
5. 后端根据权限投影动态导航，Vue 与 Layui 使用同一契约和白名单组件映射；
6. 两套管理端同步实现租户选择、权限导航、按钮权限和 403 反馈；
7. SQL Server 与 MySQL 使用等价 Dapper SQL、DbUp 迁移和真实集成测试。

本切片不实现用户、角色或菜单管理 CRUD，不支持租户套餐、过期策略、独立数据库租户、组织数据范围或普通租户账号登录。这些能力在 C2.1 中按独立纵向切片交付。

## 2. 方案比较与结论

| 方案 | 优点 | 主要风险 | 结论 |
|---|---|---|---|
| 客户端发送 `X-Tenant-Id`，前端自行过滤菜单 | 实现最少 | 租户边界依赖不可信输入，隐藏菜单不能形成服务端授权 | 不采用 |
| 切换到租户域名并重新登录 | 域名边界直观 | 宿主管理员跨租户操作低效，不能保持当前控制台会话 | 后续租户用户登录可用，本切片不采用 |
| 服务端切换会话上下文，JWT 携带租户与权限，导航由服务端投影 | 租户来源可信，刷新可恢复，权限与导航解耦 | 需要同时调整会话、RBAC、中间件和双端状态 | 采用 |

Admin.NET.Pro 提供了登录租户选择、角色菜单和按钮权限等功能参考。Full.NET 对标使用流程，但不复制其源码、表结构或客户端传入租户的授权方式。Full.NET 的菜单是权限的界面投影，后端权限策略始终是授权事实来源。

## 3. 模块边界

### 3.1 Identity

Identity 负责：

- 角色、用户角色、角色权限的最小持久化；
- 模块权限与导航贡献契约；
- 权限声明加载、动态权限策略和导航投影；
- 当前刷新会话的有效租户上下文；
- 上下文切换后的 Access Token 签发与认证审计。

Identity 不查询 Tenancy 表，也不判断租户是否有效。它只接受 Tenancy 通过公开契约传入的已验证租户摘要。

### 3.2 Tenancy

Tenancy 依赖 Identity 的公开 Contracts，负责：

- 查询可供宿主管理员选择的活动租户；
- 验证选中租户存在且启用；
- 提供进入租户或返回 Host 的 HTTP Endpoint；
- 向 Identity 的会话上下文服务提交已验证选择；
- 根据已认证 JWT 与域名建立请求级 `ICurrentTenant`。

该依赖必须在模块依赖图中显式声明为 `Tenancy -> Identity`。两个模块仍禁止访问对方内部类型和表。

### 3.3 双管理端

Vue 与 Layui 共享客户端契约、权限码和 E2E 场景，但保持独立状态管理和 UI 实现。Vue 使用 Pinia 与 Vue Router；Layui 使用原生 ES Module、Hash 路由和 DOM 渲染，不引入 Vue/React 运行时。

## 4. 最小 RBAC 数据模型

新增 `003_AuthorizationContext.sql`，两个数据库保持同一迁移编号。

### 4.1 角色

`fn_identity_role`：

- `Id`、可空 `TenantId`、非空 `ScopeKey`；
- `Code`、`Name`、`IsSystem`、`IsActive`；
- `CreatedAtUtc`、`UpdatedAtUtc`、`Version`。

`ScopeKey + Code` 建立唯一索引。Host 角色使用 `ScopeKey=host`；未来租户角色使用 `tenant:{TenantId:N}`。租户角色的 `TenantId` 必须非空，该不变量由写入 Handler、集成测试和后续 CRUD 共同保护。

### 4.2 用户角色与角色权限

`fn_identity_user_role` 使用 `(UserId, RoleId)` 复合主键；`fn_identity_role_permission` 使用 `(RoleId, PermissionCode)` 复合主键。外键阻止悬空关系，权限码最长 160 个字符并区分稳定英文编码。

权限定义不建立可任意编辑的数据库主表。每个模块通过公开贡献契约提供稳定权限码、说明和适用作用域；角色权限表只保存已授予的编码。这样可以防止删除代码权限后数据库仍把未知编码当成有效授权。

### 4.3 会话上下文

`fn_identity_refresh_session` 增加可空 `ActiveTenantId`。登录初始值为空；切换租户后更新当前活动 Refresh Session；刷新轮换时复制到替代会话；返回 Host 时清空。

`fn_identity_auth_audit` 增加可空 `ContextTenantId`，记录上下文切换结果。审计不得保存 Token、Cookie 或 CSRF 值。

已发布的 `001`、`002` 迁移保持不变，只新增向前迁移。

## 5. 权限与导航目录

### 5.1 权限码

首批稳定权限码：

```text
platform.dashboard.read
identity.navigation.read
tenancy.tenants.read
tenancy.tenants.switch
```

Identity 提供权限/导航贡献接口和聚合器；Identity、Tenancy 分别注册自己的定义。聚合器在启动验证重复权限码、重复导航标识、未知父节点、未知权限和循环依赖，失败时阻止启动。

Bootstrap 每次运行都幂等确保：

- `host-administrator` 系统角色存在且启用；
- 当前全部 Host 权限已授予该角色；
- 首个宿主管理员已分配该角色。

已有账号不能因为“用户已存在”而跳过角色和权限同步。

### 5.2 动态策略

使用动态权限策略名称 `FullNET.Permission:<permission-code>`。策略只检查 JWT 中的 `fullnet_permission` Claim；Endpoint 必须显式声明所需权限。前端隐藏按钮只改善体验，不能替代 Endpoint 授权。

角色或权限发生变化后，旧 Access Token 最多保留到 10 分钟过期；后续角色 CRUD 必须同时更新用户安全戳并撤销活动会话，以实现即时收敛。本切片不提前实现角色变更流程。

### 5.3 导航契约

`GET /api/v1/navigation` 返回已经按权限过滤并组成树的导航节点：

- `id`、`parentId`；
- `routeName`、`path`、`componentKey`；
- `title`、`caption`、`icon`、`order`；
- `requiredPermission`、`children`。

首批导航包含工作台和租户上下文。服务端不返回任意文件路径、脚本或 HTML。Vue 与 Layui 各自维护固定 `componentKey` 白名单；未知组件键必须拒绝并呈现受控错误，禁止动态执行服务端提供的代码。

## 6. 租户上下文数据流

### 6.1 可用租户

`GET /api/v1/tenancy/available` 要求 `tenancy.tenants.read`，返回按名称、标识和 ID 稳定排序的活动租户摘要。该 SQL 显式命名为宿主管理跨租户查询，使用 `Global` 数据作用域，但安全边界由 Endpoint 权限与宿主演员声明共同保证。

`TenantContextSummary` 固定返回 `id`、`identifier`、`name`、`domain`，不暴露连接信息、内部版本或未来套餐秘密。

普通租户账号未来只能看到自己的租户；本切片只有宿主管理员可调用。

### 6.2 切换上下文

`PUT /api/v1/tenancy/context` 请求：

```json
{ "tenantId": "UUID 或 null" }
```

- 非空 `tenantId`：Tenancy 验证租户存在且启用，再调用 Identity 会话上下文服务；
- 空值：仅宿主管理员可以返回 Host 上下文；
- Endpoint 要求 Bearer Token 与 `tenancy.tenants.switch`；Bearer Token 不会被浏览器自动附加，因此该请求不依赖 Cookie 完成授权；
- Identity 再次验证 `sub`、`sid`、宿主演员作用域、权限与活动会话归属；
- 更新成功后返回扁平的 `TenantContextTokenResponse`：保留 `accessToken`、`tokenType`、`expiresAtUtc` 三个既有 Token 字段，并增加 `context`；`context` 固定包含 `tenantId`、`identifier`、`name`、`scope`，Host 上下文的 `tenantId` 为空、`identifier` 为 `host`；客户端替换内存 Token，再重新加载 `/me` 和导航；
- 会话已轮换、撤销或不属于当前用户时返回标准 `401`；租户不存在或已停用时返回 `404`；权限不足返回 `403`。

切换不改变宿主管理员的演员身份，也不增加角色权限；它只收窄后续租户数据查询的有效上下文。因此切换前尚未过期的 Host Token 不会形成新的提权路径。后续租户委派权限必须建立独立设计。

### 6.3 JWT Claim

新增或明确以下 Claim：

```text
fullnet_actor_scope = host
fullnet_scope = host | tenant:{TenantId:N}
fullnet_tenant_id = 有效租户 UUID（Host 时不存在）
fullnet_permission = 重复 Claim，每项一个稳定权限码
```

`/api/v1/me` 增加 `actorScope`，`scope` 表达当前有效上下文。权限集合排序并去重，保证客户端契约稳定。

## 7. 租户解析中间件

HTTP 管道调整为：

```text
Exception -> CORS -> RateLimit -> Authentication -> Tenancy -> Authorization -> Endpoints
```

租户解析规则：

1. 已认证请求存在租户 Claim 时，按 ID 查询活动租户；非 Host 域还必须与域名解析结果一致；
2. 已认证请求没有租户 Claim 时，只允许在 Host 域建立 Host 上下文；在租户域调用受保护 API 返回 `403 tenancy.context_mismatch`；
3. 匿名请求继续按 Host 域或租户域建立上下文，为未来租户登录和公开 Endpoint 保留入口；
4. 任意客户端 Header、查询字符串或请求体都不能直接设置 `ICurrentTenant`；
5. 中间件必须在 `finally` 中清理作用域状态，防止请求复用污染。

## 8. 双管理端交互

### 8.1 公共行为

登录或恢复成功后，两端按顺序加载 `/me`、`/navigation`，具备租户读取权限时再加载 `/tenancy/available`。任何契约不匹配都清理本地导航并呈现受控错误。

切换流程：

1. 用户从顶栏或租户上下文页选择租户；
2. 客户端调用上下文 Endpoint 并替换内存 Access Token；
3. 重新加载当前用户、导航和租户摘要；
4. 当前路由不再授权时跳转首个可访问导航或 403；
5. 切换失败保持原 Token 与原 UI 上下文，不先乐观修改租户名称；
6. 401 刷新延续服务端保存的 `ActiveTenantId`，客户端无需 LocalStorage 保存租户授权状态。

租户 ID 可以作为非敏感体验偏好在未来独立评估，但本切片不写入 Web Storage，避免偏好与授权上下文发生分叉。

### 8.2 Vue

- Pinia 会话 Store 保存当前用户、导航树和可用租户；
- Vue Router 只映射 `overview`、`tenant-context` 等本地组件白名单；
- `can(permission)` 控制按钮展示；路由进入时仍根据服务端导航集合检查，失败进入 403；
- 顶栏租户选择器和租户上下文页使用 Element Plus，保持现有 Full.NET 设计令牌。

### 8.3 Layui

- 原生会话状态机的快照增加导航和租户数据；
- 导航与租户选项通过 DOM 安全创建并使用 `textContent`，禁止拼接不可信 HTML；
- Hash 路由只映射本地 `data-route-view` 白名单；
- 按钮以 `data-permission` 显示/隐藏，但服务端仍执行相同权限策略；
- 生产产物继续禁止 Vue、React 等 SPA 运行时。

## 9. API 与错误模型

| 方法 | 路径 | 权限 | 成功 |
|---|---|---|---|
| `GET` | `/api/v1/navigation` | `identity.navigation.read` | `200 NavigationNode[]` |
| `GET` | `/api/v1/tenancy/available` | `tenancy.tenants.read` | `200 TenantContextSummary[]` |
| `PUT` | `/api/v1/tenancy/context` | `tenancy.tenants.switch` | `200 TenantContextTokenResponse` |
| `GET` | `/api/v1/me` | 已认证 | 增加 `actorScope`，保留现有字段 |

新增稳定错误码：

- `authorization.permission_denied`；
- `identity.session_not_active`；
- `identity.session_context_conflict`；
- `identity.invalid_actor_scope`；
- `tenancy.context_not_found`；
- `tenancy.context_mismatch`；
- `navigation.catalog_invalid` 只用于启动诊断，不向生产客户端泄露内部定义。

所有外部错误继续使用标准状态码与 ProblemDetails。Admin.NET 包络只由 Compatibility 适配层处理。

## 10. 并发与安全

- 上下文更新使用 `SessionId + UserId + Version + 未消费/未撤销` 条件更新；冲突后重新读取，已消费或撤销返回 `401 identity.session_not_active`，仍活动但版本已变化返回 `409 identity.session_context_conflict`；客户端遇到该 `409` 时刷新一次会话并最多重试一次切换；
- Refresh 与上下文切换竞争时不得覆盖较新的会话。切换命中已消费会话时客户端通过既有单次刷新恢复后重试一次；
- 权限 Claim 来自数据库角色授权与代码目录的交集，未知或已删除权限码不进入 Token；
- 租户查询、导航和上下文切换日志使用结构化字段，不记录 Token、Cookie 或完整客户端载荷；
- 租户列表不进入永久缓存；租户按 ID/域名解析继续使用 FusionCache，并复用租户事件失效标签；
- 所有列表排序稳定，输入长度和 UUID 格式由强类型模型与 FluentValidation 约束。

## 11. 测试与验收

### 11.1 单元与架构测试

- 权限目录去重、未知授权过滤、导航父子裁剪和循环检测；
- 动态权限策略只接受精确 Claim，不接受前缀或大小写变体；
- Token 的 actor/effective scope、租户与排序权限 Claims；
- 模块依赖只有 `Tenancy -> Identity Contracts`，不存在反向依赖；
- Vue/Layui 契约守卫拒绝未知组件键和畸形导航。

### 11.2 SQL Server/MySQL 集成测试

两个 Provider 分别验证：

- `003` 迁移重复运行与角色/权限关系约束；
- Bootstrap 重复运行仍同步系统角色、权限和用户分配；
- 登录 Token 包含显式宿主权限；
- 可用租户只返回活动租户且排序稳定；
- 进入租户、刷新保持上下文、返回 Host 完整闭环；
- 停用/不存在租户、无权限、跨域 Claim 不匹配被拒绝；
- 并发 Refresh 与上下文切换不产生跨租户或旧会话覆盖。

### 11.3 双管理端测试

- Vue 与 Layui 各自覆盖动态导航、未知组件、按钮权限、租户切换成功/失败和刷新恢复；
- Playwright 用同一场景分别验证权限菜单、进入租户、当前租户展示、返回 Host、无权限 403；
- E2E 路由模拟之外，真实 Host 集成测试继续覆盖带凭据 CORS 与认证/租户中间件顺序；
- Layui 生产依赖扫描确认不存在 SPA 运行时。

## 12. 发布、兼容与后续

本切片会增加数据库表、列、JWT Claim 和 `/me` 字段，属于向前兼容扩展。现有客户端忽略新 JSON 字段仍可工作；新客户端必须用契约守卫验证所需字段。生产部署顺序为先执行 Migrator，再发布 API，最后发布双管理端。

完成后 C1 的会话、租户和权限导航可以进入 `Verified`；国际化入口仍单独跟踪。下一切片按以下顺序实施：

1. 用户管理、密码重置和租户账号；
2. 角色 CRUD、角色权限分配与会话撤销；
3. 菜单管理、按钮权限和租户菜单覆盖；
4. Organization、职位和数据范围；
5. 在线用户与强制下线。

本切片只参考 Admin.NET.Pro 的公开功能和交互，不复制其源码、表结构、样式或产品资产；Full.NET 发布边界保持 MIT。
