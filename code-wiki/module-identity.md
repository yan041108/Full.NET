# Identity 身份认证与授权模块

> 项目：[`src/Modules/Full.NET.Modules.Identity`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Identity)
> 稳定模块键：`Identity`
> 模块入口：[`IdentityModule.cs`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Identity/IdentityModule.cs)
> 公开契约：[`src/Modules/Full.NET.Modules.Identity.Contracts`](file:///G:/wwwroot/github_fork/Full.NET/src/Modules/Full.NET.Modules.Identity.Contracts)

## 1. 模块职责

Identity 是 Full.NET 的**安全底座模块**，是依赖图根模块（`Dependencies: []`），并声明 `OptionalContractDependencies: ["Files", "Notifications"]`：用户头像/签名通过 Files 合同读写、账号挑战邮件通过 Notifications 投递；二者未启用时自助资料媒体与邮件挑战能力退化为不可用，但核心认证与授权仍可独立运行。

模块负责：

- ✅ **身份认证**：用户名密码登录、RSA JWT 签发、Refresh Token 轮换/重用撤销、退出登录
- ✅ **OIDC Provider**：Authorization、Token、UserInfo、Session 端点；OIDC 客户端、授权、签名密钥管理（受 `IdentityOidcOptions.Enable` 开关控制）
- ✅ **OAuth 登录**：OAuth Provider 配置、OAuth Flow（外部 IdP 登录）、OAuth 用户链接管理
- ✅ **LDAP 集成**：LDAP 连接配置、用户目录查询
- ✅ **注册流程**：自助注册账号、邮箱验证码挑战、注册策略、注册邀请码、注册方式（公开/邀请）
- ✅ **账号找回**：通过邮件挑战找回密码
- ✅ **租户成员**：宿主侧租户成员管理、邀请接受、成员席位配额 Port
- ✅ **授权体系**：RBAC 角色权限、权限策略、数据范围(DataScope)、字段投影授权
- ✅ **会话管理**：在线会话、踢人下线、会话刷新、CSRF Token、后台会话绑定校验 Port
- ✅ **用户管理**：宿主/租户用户 CRUD、用户角色分配、资料维护、自助资料（头像/媒体）
- ✅ **角色管理**：宿主/租户角色、数据范围、字段授权
- ✅ **菜单与导航**：宿主/租户菜单目录、动态权限导航
- ✅ **API Key 认证**：用于服务端集成的签名认证、开放访问客户端
- ✅ **TOTP MFA**：基于时间的一次性密码多因子认证
- ✅ **超级管理员**：受保护的 `host-administrator` 系统角色，动态投影全部权限
- ✅ **安全策略**：强密码策略、登录锁定、安全戳、RSA 密钥环
- ✅ **模块目录查询**：`QueryHostModuleCatalog` 与 `QueryHostModuleSelection` 暴露已注册模块与启用集预览（仅描述符，不暴露实现类型）
- ✅ **Host Dashboard**：仪表盘摘要（活跃用户数、租户指标 Port）

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
| `fn_identity_open_access_client` | 开放访问客户端 | ClientId, SecretHash, AllowedScopes |
| `fn_identity_registration_invitation` | 注册邀请 | InvitationCode, Email, StatusKey, ExpiresAtUtc |
| `fn_identity_account_challenge` | 账号挑战 | ChallengeId, Purpose, NormalizedEmail, Credential, ExpiresAtUtc |
| `fn_identity_oidc_client` | OIDC 客户端 | ClientId, RedirectUrisJson, SecretHash, ScopesJson |
| `fn_identity_oidc_authorization` | OIDC 授权 | AuthorizationCode, SubjectId, ClientId, ExpiresAtUtc |
| `fn_identity_oidc_signing_key` | OIDC 签名密钥 | Kid, Algorithm, PrivateKeyProtected, StatusKey |
| `fn_identity_oidc_session` | OIDC 会话 | SessionId, SubjectId, ClientId, ExpiresAtUtc |
| `fn_identity_oauth_provider` | OAuth Provider | ProviderKey, AuthorizationEndpoint, TokenEndpoint, UserInfoEndpoint, ClientId, ClientSecretProtected |
| `fn_identity_oauth_user_link` | OAuth 用户链接 | UserId, ProviderKey, SubjectAtProvider |
| `fn_identity_oauth_authorization_state` | OAuth 授权状态 | State, ProviderKey, ReturnUrl, ExpiresAtUtc |
| `fn_identity_ldap_connection` | LDAP 连接 | ConnectionKey, Host, Port, BindDn, CredentialProtected |
| `fn_identity_signature_nonce` | 签名 Nonce | Nonce, Timestamp, ConsumedAtUtc |
| `fn_identity_workflow_role_member` | 工作流角色成员投影 | RoleCode, UserId（供 Workflow 通过 `IWorkflowRoleMemberDirectory` 读取） |
| `fn_identity_host_user_directory` | Host 用户目录投影 | UserId, Username, DisplayName（供跨模块 Port 读取） |
| `fn_identity_host_tenant_user_directory` | Host 租户用户目录投影 | UserId, TenantId, Username |

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
| `PasswordChangeRequiredMiddleware` | Middleware/ | `BeforeAuthorization` 阶段拦截：强制改密后重定向到改密端点 |
| `IdentityOidcHostContextMiddleware` | Middleware/ | `BeforeAuthentication` 阶段：从 OIDC 端点解析 Host 上下文 |
| `BackgroundSessionBindingValidator` | Contracts + 实现 | Worker 后台进程会话绑定校验 Port（防失效会话仍被使用） |
| `BackgroundSessionAuthorization` | Contracts + 实现 | Worker 后台进程授权 Port |

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
| `HostRegistrationPolicyService` | 注册策略：开放注册、邀请注册、最小密码强度 |
| `RegistrationInvitationService` | 注册邀请码 CRUD：生成、撤销、消费 |
| `AccountChallengeService` | 账号挑战：生成验证码、调用 `IIdentityChallengeDeliveryPort` 投递 |
| `RegisterAccountService` | 自助注册：用户名/邮箱/密码、邮箱挑战、首次会话建立 |
| `RecoverAccountService` | 账号找回：邮箱挑战 → 重置密码 → 安全戳轮转 |
| `ManageTenantMembersService` | 宿主侧租户成员 CRUD：邀请、移除、角色分配 |
| `AcceptTenantInvitationService` | 用户接受租户邀请 → 加入租户 |
| `HostOidcClientManagementService` | OIDC 客户端 CRUD |
| `HostOidcAuthorizationManagementService` | OIDC 授权 CRUD、撤销 |
| `HostOidcSigningKeyManagementService` | OIDC 签名密钥 CRUD、轮转 |
| `HostLdapConnectionManagementService` | LDAP 连接 CRUD |
| `HostOAuthProviderManagementService` | OAuth Provider CRUD |
| `HostOAuthLinkManagementService` | OAuth 用户链接管理（绑定/解绑） |
| `HostOpenAccessClientManagementService` | 开放访问客户端 CRUD |
| `IdentityOidcRetentionBackgroundService` | OIDC 会话与授权过期保留清理（Worker HostedService） |
| `HostDashboardSummaryService` | Host 仪表盘摘要（活跃用户数、租户指标） |

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
// AddServices（IdentityModule.AddServices）
services.AddMigrationServices(services, configuration);  // 先注册 Migrator 闭包（Options + Seed Contributor）
services.AddIdentityAuthentication(configuration);         // JWT Bearer + Cookie + Signature + API Key
services.AddIdentityOidc(configuration);                   // OIDC Provider（Authorization/Token/UserInfo/Session）
services.AddIdentityAuthorization(configuration);          // 权限策略 + 目录 + 超管
services.AddIdentityDomainServices(configuration);         // 所有管理服务 + 仓储
services.AddIdentityHttpPolicies(configuration);           // 限流（登录/刷新/匿名会话轮换/签名认证）
AddOrganizationUnitProjection(services);                   // 组织单元本地投影
AddBackgroundServices(services, configuration);            // 注册 Worker 后台能力（API 与 Worker 共用）
```

> `UseModuleMiddleware`：
> - `BeforeAuthentication` 阶段注册 `IdentityOidcHostContextMiddleware`（OIDC 端点解析 Host 上下文）
> - `BeforeAuthorization` 阶段注册 `PasswordChangeRequiredMiddleware`（强制改密）

### Worker Profile
```csharp
// AddBackgroundServices
services.AddOrganizationUnitProjectionHandlers();              // 组织单元事件投影处理器
services.AddNotificationRecipientDirectories();                // Workflow 通知受众目录（受信作用域）
services.AddScoped<OrganizationUnitChangedIntegrationEventHandler>();
services.AddScoped<IBackgroundSessionBindingValidator, BackgroundSessionBindingValidator>();
services.AddScoped<IBackgroundSessionAuthorization, BackgroundSessionAuthorization>();
services.AddIdentityOidcRetentionBackgroundService(configuration);  // OIDC 会话/授权过期清理
```

### Migration Profile
```csharp
// AddMigrationServices
services.AddOptions<IdentityOptions>().Bind(...).ValidateOnStart();
services.AddOptions<SignatureAuthenticationOptions>().Bind(...).ValidateOnStart();
services.AddScoped<IPasswordHasher<IdentityUser>, PasswordHasher<IdentityUser>>();
services.AddScoped<IIdentityBootstrapService, IdentityBootstrapService>();
AddHostNavigationCatalogSeedClosure(services);                          // 导航目录同步闭包
services.AddSeedContributor<HostAdministratorSeedContributor>();        // Host 超级管理员种子
services.AddSeedContributor<HostNavigationCatalogSeedContributor>();    // Host 导航目录种子
```

---

## 7. Features 切片目录

按 `src/Modules/Full.NET.Modules.Identity/Features/` 实际子目录整理（共 44 个垂直切片）：

### 7.1 认证会话

| 切片 | 说明 |
|------|------|
| `Login` | 用户名密码登录（`/api/v1/auth/login`） |
| `RefreshSession` | Refresh Token 轮换（`/api/v1/auth/refresh`） |
| `Logout` | 退出登录 |
| `ChangeSessionContext` | 受信租户切换、Host 返回 |
| `GetCurrentUser` | 当前用户信息 + 权限快照 |
| `UpdateLocale` | 用户语言偏好切换 |
| `ChangePassword` | 自助改密（触发安全戳轮转） |
| `GetNavigation` | 按权限过滤的导航树 |
| `GetAuthorizationTree` | 授权目录树（角色授权页用） |

### 7.2 自助资料与媒体

| 切片 | 说明 |
|------|------|
| `SelfServiceProfile` | 资料 GET/UPDATE + 媒体端点（头像/签名，依赖 Files 可选契约） |
| `HostFileReferences` | 头像引用 Probe（`IHostFileReferenceClaimProbe` 实现） |

### 7.3 宿主侧管理

| 切片 | 说明 |
|------|------|
| `ManageHostUsers` | 宿主用户 CRUD |
| `ManageHostRoles` | 宿主角色 CRUD |
| `ManageHostRoleFieldGrants` | 角色字段授权 |
| `ManageHostMenus` | 菜单目录 CRUD |
| `ManageHostOnlineSessions` | 在线会话查询、强制下线 |
| `ManageHostApiKeys` | API Key CRUD |
| `ManageOpenAccessClients` | 开放访问客户端 CRUD |
| `ManageSuperAdministrators` | 超级管理员授予/撤销 |
| `ManageTotp` | TOTP 注册/验证/解除 |
| `GetHostDashboardSummary` | Host 仪表盘摘要 |

### 7.4 注册与找回

| 切片 | 说明 |
|------|------|
| `RegisterAccount` | 自助注册 + 邮箱验证码挑战（含 `SendEmailChallengeEndpoint`） |
| `RecoverAccount` | 账号找回（邮箱挑战 → 重置密码） |
| `RegistrationInvitations` | 邀请码 CRUD |
| `ManageRegistrationPolicy` | 注册策略（开放/邀请） |
| `ManageRegistrationWays` | 注册方式 CRUD |
| `PublicRegistrationWays` | 公开查询可用注册方式 |
| `AccountChallenges` | 账号挑战内部服务（调用 `IIdentityChallengeDeliveryPort`） |

### 7.5 租户成员

| 切片 | 说明 |
|------|------|
| `ManageTenantMembers` | 宿主侧租户成员 CRUD |
| `AcceptTenantInvitation` | 用户接受租户邀请 |

### 7.6 OIDC Provider

> 受 `IdentityOidcOptions.Enable` 开关控制；启用时映射下列端点。

| 切片 | 说明 |
|------|------|
| `OidcAuthorization` | `/oauth/authorize` 授权端点 |
| `OidcToken` | `/oauth/token` 令牌端点 |
| `OidcUserInfo` | `/oauth/userinfo` UserInfo 端点 |
| `OidcSession` | `/oauth/session` 会话端点 |
| `ManageOidcClients` | OIDC 客户端 CRUD |
| `ManageOidcAuthorizations` | OIDC 授权 CRUD |
| `ManageOidcSigningKeys` | OIDC 签名密钥 CRUD |

### 7.7 OAuth 与 LDAP

| 切片 | 说明 |
|------|------|
| `ManageOAuthProviders` | OAuth Provider CRUD |
| `OAuthFlow` | 外部 IdP 登录 Flow |
| `ManageOAuthLinks` | OAuth 用户链接管理 |
| `ManageLdapConnections` | LDAP 连接 CRUD |

### 7.8 模块目录与投影

| 切片 | 说明 |
|------|------|
| `QueryHostModuleCatalog` | 查询已注册模块（仅描述符） |
| `QueryHostModuleSelection` | 查询启用集预览（分析模式） |
| `OrganizationUnitProjection` | Organization 单元本地投影（写、查、回填、对账） |
| `Bootstrap` | 启动期初始化（Host 超管、导航目录） |

---
