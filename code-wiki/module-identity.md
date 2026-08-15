# Identity 身份认证与授权模块

> 项目：[`src/Modules/Full.NET.Modules.Identity`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Identity)
> 稳定模块键：`Identity`
> 公开契约：[`src/Modules/Full.NET.Modules.Identity.Contracts`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Identity.Contracts)

## 1. 模块职责

Identity 是 Full.NET 的**安全底座模块**，负责：

- ✅ **身份认证**：用户名密码登录、RSA JWT 签发、Refresh Token 轮换/重用撤销、退出登录
- ✅ **授权体系**：RBAC 角色权限、权限策略、数据范围(DataScope)、字段投影授权
- ✅ **会话管理**：在线会话、踢人下线、会话刷新、CSRF Token
- ✅ **用户管理**：宿主/租户用户 CRUD、用户角色分配、资料维护
- ✅ **角色管理**：宿主/租户角色、数据范围、字段授权
- ✅ **菜单与导航**：宿主/租户菜单目录、动态权限导航
- ✅ **API Key 认证**：用于服务端集成的签名认证
- ✅ **TOTP MFA**：基于时间的一次性密码多因子认证
- ✅ **超级管理员**：受保护的 `host-administrator` 系统角色，动态投影全部权限
- ✅ **安全策略**：强密码策略、登录锁定、安全戳、RSA 密钥环

---

## 2. 主要数据表

| 表名 | 说明 | 关键字段 |
|------|------|----------|
| `fn_identity_user` | 用户表 | Id, TenantId, Username, NormalizedUsername, PasswordHash, SecurityStamp, IsEnabled, Locale, CreatedAtUtc |
| `fn_identity_role` | 角色表 | Id, TenantId, RoleCode, RoleName, RoleScope(Host/Tenant), DataScopeJson, IsSystem |
| `fn_identity_user_role` | 用户角色关联 | UserId, RoleId, TenantId |
| `fn_identity_refresh_session` | 刷新会话 | Id, UserId, TenantId, RefreshTokenHash, ClientId, ExpiresAtUtc, RotatedAtUtc, RevokedAtUtc |
| `fn_identity_api_key` | API Key | Id, TenantId, KeyName, KeyHash(只存哈希), Scope, ExpiresAtUtc, LastUsedAtUtc |
| `fn_identity_menu` | 菜单目录 | Id, TenantId, ParentId, MenuPath, RoutePath, Icon, Sort, PermissionCodesJson |
| `fn_identity_user_totp` | TOTP 注册 | Id, UserId, EncryptedSecret(DPAPI), Algorithm, Digits, Period |
| `fn_identity_online_session` | 在线会话(投影) | 来自 Identity Event 的本地投影 |

---

## 3. 核心类与服务

### 3.1 认证安全

| 类 | 路径 | 职责 |
|----|------|------|
| `JwtAccessTokenIssuer` | Security/ | 使用 `RsaSigningKeyRing` 签发 RSA JWT，包含自定义 Claim |
| `RsaSigningKeyRing` | Security/ | 多 RSA 密钥管理：激活密钥、轮转密钥、密钥 ID、过期时间 |
| `RefreshSession` | Domain/ | 值对象：Refresh Token 轮换算法、重用检测、撤销 |
| `IdentityPasswordPolicy` | Security/ | 强密码规则：最小长度、复杂度、禁止字典、历史密码检查 |
| `TokenHash` | Security/ | Refresh Token / API Key 的哈希存储（SHA-256 + Salt） |
| `CsrfTokenValidator` | Security/ | CSRF Token 校验（双 Cookie Submit 模式） |
| `SignatureAuthenticationHandler` | Security/ | API Key HMAC-SHA256 请求签名认证 |
| `TotpAlgorithm` | Security/ | TOTP 算法（RFC 6238），支持 SHA-1/SHA-256/SHA-512 |
| `TotpSecretProtector` | Security/ | 使用 Data Protection 加密存储 TOTP 密钥 |

### 3.2 授权基础设施

| 类 | 路径 | 职责 |
|----|------|------|
| `FullNetPermissionPolicyProvider` | Authorization/ | 将权限码 → `IAuthorizationPolicy` 动态构造 |
| `FullNetPermissionHandler` | Authorization/ | 权限 Handler：校验用户是否拥有请求的权限码 |
| `FullNetPermissionRequirement` | Authorization/ | 权限需求：携带目标权限码 |
| `PermissionClaimEvaluator` | Authorization/ | 从用户 Claim → 权限码集合（含超管动态投影） |
| `IPermissionSnapshotReader` | Authorization/ | 权限快照读取器：权限码 → 页面可见 / 按钮可见 |
| `AuthorizationCatalog` | Authorization/ | 权限目录：所有模块注册的权限码元数据 |
| `AuthorizationCatalogValidator` | Authorization/ | 权限目录校验：无重复、无孤立、页面/操作父子约束 |
| `AuthorizationTreeProjector` | Features/GetAuthorizationTree/ | 树形权限结构投影（角色授权页用） |
| `UserDataScopeResolver` | DataScope/ | 用户数据范围解析（ALL / Self / CustomUnits / Subtree） |
| `DataScopeSqlFilterBuilder` | DataScope/ | 将数据范围规则 → SQL WHERE 片段注入 |
| `UserFieldProjectionResolver` | FieldProjection/ | 字段级授权：按角色隐藏/只读字段 |
| `FieldProjectionCatalog` | FieldProjection/ | 字段投影目录 |

### 3.3 领域管理服务

| 服务 | 职责 |
|------|------|
| `HostUserManagementService` | 宿主用户 CRUD：创建/更新/禁用/删除、密码重置、安全戳更新 |
| `HostUserQueryService` | 宿主用户查询：分页、筛选、资料详情、用户角色分配查询 |
| `HostUserRolesService` | 用户角色分配：按用户替换角色集合、审计 |
| `HostRoleManagementService` | 宿主角色 CRUD：创建/更新/删除、最后保护、数据范围配置 |
| `HostRoleQueryService` | 宿主角色查询：分页、详情、权限树关联 |
| `HostRoleDataScopeService` | 角色数据范围配置和校验 |
| `HostMenuManagementService` | 菜单 CRUD：树形结构、排序、路由路径、图标、权限关联 |
| `HostMenuQueryService` | 菜单查询：按用户权限过滤的导航树、完整目录 |
| `HostNavigationCatalogSyncService` | 导航目录同步：菜单变动 → 权限码目录联动 |
| `HostApiKeyManagementService` | API Key 创建（显示一次明文）/撤销/续期 |
| `HostOnlineSessionManagementService` | 在线会话查询、强制下线 |
| `SuperAdministratorService` | 超级管理员授予/撤销、并发最后一名保护 |
| `TotpEnrollmentService` | TOTP 注册流程：生成密钥 → 验证 → 持久化 |
| `IdentitySessionContextService` | 会话上下文：可信租户切换、Host 返回 |

### 3.4 租户本地投影

| 类 | 说明 |
|----|------|
| `OrganizationUnitProjectionDirectory` | 消费端投影：租户上下文导航需要的部门树 |
| `OrganizationUnitChangedIntegrationEventHandler` | 监听 Organization 事件 → 更新本地投影表 |
| `OrganizationUnitProjectionBackfillService` | 新消费者首次回填：调用 Organization 批量 Contract |
| `OrganizationUnitProjectionReconciliationService` | 对账：投影表 vs 权威源差异修复 |

---

## 4. 认证流程

### 4.1 用户名密码登录

```
POST /api/v1/auth/login
  { username, password, clientId }

  1. FluentValidation 校验（事务前短路）
  2. 开启 ICommandTransaction
  3. Normalize 用户名 → 按 (TenantId=NULL, NormalizedUsername) 查 Host 用户
     或按 (TenantId, NormalizedUsername) 查租户用户
  4. PasswordHash 验证（IdentityPasswordPolicy）
  5. 失败递增失败计数 → 达到锁定阈值 IsLockedUntilUtc
  6. 创建 RefreshSession：
     - 生成 Refresh Token (安全随机 32 字节)
     - 只存 SHA-256 哈希
     - 设置过期 + 滑动窗口
  7. 签发 JWT AccessToken：
     - RSA 私钥签名 (RsaSigningKeyRing.ActiveKey)
     - Claim: sub, tid(host|tenant), username, role_codes, security_stamp, locale
     - 短过期 (默认 15 分钟)
  8. 生成 CSRF Token（双 Cookie Submit）
  9. 写入登录领域审计（事务内）
  10. 事务 Commit
  11. Cookie: Set-Cookie refresh_token + csrf_token(HttpOnly, Secure, SameSite=Strict)
  
  返回 200 { accessToken, csrfToken, user, permissions: [] }
```

### 4.2 Refresh Token 轮换

```
POST /api/v1/auth/refresh
  Cookie: refresh_token, csrf_token
  Header: X-CSRF-Token

  1. CSRF Token 校验
  2. Refresh Token Hash → 查询 fn_identity_refresh_session
  3. 检查：未过期、未撤销、会话用户仍然有效、安全戳匹配
  4. **重用检测**：若该 Token 已被轮换过（RotatedAtUtc ≠ NULL）
     → 立即撤销该用户该 ClientId 的全部 Refresh Session（泄露保护）
  5. 标记旧 session.RotatedAtUtc = NOW
  6. 创建新 RefreshSession（新 Token、新过期窗口）
  7. 重新签发 JWT（读取最新角色/安全戳）
  8. 审计：轮换成功 / 重用检测触发
```

---

## 5. 授权机制

### 5.1 Endpoint 权限声明

```csharp
// 每个管理 Endpoint 绑定独立权限码（粗粒度被架构测试拒绝）
[RequirePermission("identity.users.read")]
[HttpGet("/api/v1/host/users")]
public async Task<PagedResult<HostUserListRow>> QueryHostUsers(...)

[RequirePermission("identity.users.write")]
[HttpPost("/api/v1/host/users")]
public async Task<Result<Guid>> CreateHostUser(...)

[RequirePermission("identity.users.disable")]
[HttpPut("/api/v1/host/users/{id}/disable")]
public async Task<Result> DisableHostUser(...)
```

### 5.2 超级管理员边界

- 不是用户名判断、不是通配符权限、不是 Handler 无条件成功
- 是**持久化的 `host-administrator` 系统角色**
- 通过 `PermissionClaimEvaluator` 从授权目录**动态投影当前作用域的全部已知权限**
- 仍受：租户隔离、账号禁用、会话状态、安全戳、审计、最后一名保护

---

## 6. Host Profile 注册

### API Profile
```csharp
// AddServices
services.AddIdentityAuthentication()   // JWT Bearer + Cookie + Signature + API Key
        .AddIdentityAuthorization()    // 权限策略 + 目录 + 超管
        .AddIdentityDomainServices()   // 所有管理服务 + 仓储
        .AddIdentityHttpPolicies();    // 限流（登录/刷新）
```

### Worker Profile
```csharp
// AddBackgroundServices
services.AddOrganizationUnitProjectionHandlers()  // 事件投影处理器
        .AddHostUserDirectoryProjectionHandlers();
```

### Migration Profile
```csharp
// AddMigrationServices
services.AddSeedContributor<HostAdministratorSeedContributor>()
        .AddSeedContributor<HostNavigationCatalogSeedContributor>();
```
