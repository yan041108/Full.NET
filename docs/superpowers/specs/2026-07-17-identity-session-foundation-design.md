# Full.NET 身份与会话基础切片设计

- 日期：2026-07-17
- 状态：已确认
- 决策方式：依据项目所有者“后续自动确认（按推荐方案来）”的持续授权，采用推荐方案
- 适用范围：Identity 后端模块、SQL Server/MySQL 迁移、Vue/Layui 双管理端登录会话

## 1. 背景与目标

Tenancy 模块和两套管理端壳层已经建立，但 `/api/v1/me` 仍是客户端模拟契约，正式登录、刷新、退出、CSRF 与授权尚未落地。用户、角色、菜单、组织和租户管理都依赖可靠的身份边界，因此下一步必须先完成最小但可上线演进的身份与会话纵向切片。

本切片交付以下闭环：

1. 显式引导创建首个宿主管理员，不在仓库中保存默认密码；该账号按后续批准设计升级为受保护超级管理员；
2. 账号密码验证、登录失败锁定和可靠登录审计；
3. 短期 JWT Access Token、Refresh Token 轮换、旧令牌重用检测和会话撤销；
4. `GET /api/v1/me`、登录、刷新、退出四个稳定 HTTP 契约；
5. Vue 与 Layui 两端真实登录页、内存 Access Token、启动恢复和统一 401 处理；
6. SQL Server 与 MySQL 使用等价的 Dapper 显式 SQL 和迁移；
7. 为后续用户、角色、权限、菜单、在线用户、强制下线和标准 OIDC Provider 保留清晰扩展点。

本切片不实现用户管理 CRUD、角色/菜单授权管理、验证码、第三方登录、API Key、密码找回或多设备会话管理页面。这些能力在会话底座验证后按独立纵向切片交付。

## 2. 方案比较与结论

| 方案 | 优点 | 主要风险 | 结论 |
|---|---|---|---|
| 先做租户/用户 CRUD，临时匿名或临时密钥保护 | 页面成果快 | 管理接口安全模型会返工，无法真实验证按钮与页面权限 | 不采用 |
| 一次实现完整 Identity、RBAC、菜单、组织和双端页面 | 功能面完整 | 变更过大，安全和双库并发问题难以隔离评审 | 不采用 |
| 先实现身份与会话基础切片，再扩展 RBAC/CRUD | 最先稳定所有管理模块共同依赖的认证、审计和客户端会话语义 | 首个切片仍需同时覆盖后端、双库和双前端 | 采用 |

ASP.NET Core 官方资料建议生产令牌使用 OAuth/OIDC 标准，并优先使用非对称签名。Full.NET 当前自有账号密码流只面向第一方管理端，不宣称为通用授权服务器；令牌颁发、密码验证与外部身份提供者通过接口隔离，后续可接入标准 OIDC Provider，而不更改业务 Endpoint 的认证方式。

## 3. 模块边界与依赖

新增 `Full.NET.Modules.Identity`，采用与 Tenancy 一致的 Core 模块形态：

```text
src/Modules/Full.NET.Modules.Identity/
├── Contracts/
├── Domain/
├── Features/
│   ├── Login/
│   ├── RefreshSession/
│   ├── Logout/
│   └── GetCurrentUser/
├── Persistence/
├── Security/
├── Serialization/
└── IdentityModule.cs
```

Identity 可以依赖 `Full.NET.Abstractions`、Dapper 数据抽象、Modularity 和 FluentValidation，但不得依赖 Tenancy 的内部实现。用户使用 `TenantId` 表达租户归属；首版只开放宿主作用域登录，租户账号在后续用户管理切片启用。Tenancy 不反向依赖 Identity。

宿主 API 负责注册认证中间件并按 `UseAuthentication -> UseAuthorization -> MapFullNetModules` 顺序建立请求管道。Identity 模块只注册服务、策略和 Endpoint，不读取宿主私有实现。

## 4. 数据模型与双库约束

### 4.1 用户表

`fn_identity_user` 保存：

- `Id`、可空 `TenantId`、非空 `ScopeKey`；
- 原始 `Username` 与 `NormalizedUsername`、`DisplayName`；
- ASP.NET Core Identity 标准格式的 `PasswordHash`；
- `IsActive`、`FailedLoginCount`、`LockoutEndUtc`；
- `SecurityStamp`、`CreatedAtUtc`、`UpdatedAtUtc`、`Version`。

`ScopeKey + NormalizedUsername` 建立唯一索引。宿主账号使用固定 `ScopeKey=host`；租户账号后续使用 `tenant:{TenantId:N}`。不直接依赖数据库对 `NULL` 唯一索引的差异。

### 4.2 刷新会话表

`fn_identity_refresh_session` 保存：

- `Id`、`UserId`、`FamilyId`、`ClientId`；
- SHA-256 后的 `TokenHash`，不保存 Refresh Token 明文；
- `ExpiresAtUtc`、`ConsumedAtUtc`、`RevokedAtUtc`、`ReplacedById`；
- `CreatedAtUtc`、`Version`。

刷新时在事务内执行条件更新：仅未消费、未撤销且版本匹配的记录可以被消费。并发刷新只有一个请求成功；已经消费的令牌再次出现时撤销同一 `FamilyId` 的全部活动会话，并记录安全审计。

### 4.3 认证审计表

`fn_identity_auth_audit` 可靠保存登录成功、登录失败、刷新、退出、令牌重用和会话撤销事件。审计只保存规范化用户名的 SHA-256 指纹、用户 ID、事件类型、稳定结果码、发生时间以及经过长度限制的客户端元数据；不记录密码、Access Token、Refresh Token 或 CSRF Token。

成功登录的会话创建与成功审计使用同一数据库事务；失败计数更新与失败审计也使用同一事务。普通运行日志只用于诊断，不能代替认证审计。

### 4.4 数据库兼容

迁移编号在两个 Provider 中保持一致。SQL Server 与 MySQL 分别提供方言文件，主键、唯一索引、时间精度、并发条件和级联行为必须等价。业务代码通过 `ISqlDialect` 或 Provider 分支选择显式 SQL，不在业务层拼接数据库名称或未验证标识符。

## 5. 密码、锁定与引导账号

密码使用 `IPasswordHasher<IdentityUser>` 的 ASP.NET Core Identity 标准实现，不自建密码哈希算法。密码策略首版要求最少 12 个字符，并同时包含大小写字母、数字和非字母数字字符；后续可以配置化，但降低策略必须显式记录安全决策。

同一账号连续 5 次失败后锁定 15 分钟。未知账号、错误密码、禁用账号和锁定账号对外统一返回 `401` 与 `identity.invalid_credentials`，避免账号枚举；内部审计保存具体原因。成功登录清零失败计数。

首个宿主管理员只允许由 Migrator 的显式引导流程创建：

- 用户名和密码来自环境变量或 Secret Provider；
- 缺少任一值时不创建账号，并输出不含秘密的操作提示；

默认引导账号的动态全权限、最后一名保护和高风险管理以[超级管理员设计](2026-07-18-super-administrator-design.md)为准。该能力尚未实现前，现有账号仍依赖 Bootstrap 同步显式权限，文档和发布说明不得混称为已经具备动态超级管理员。
- 重复执行保持幂等，不覆盖已有密码；
- 仓库、镜像、日志和命令行参数中不得包含默认密码或明文秘密。

## 6. Access Token 与密钥管理

Access Token 使用 JWT Bearer，仅用于 Full.NET API 访问，默认有效期 10 分钟。至少包含并严格校验 `iss`、`aud`、`sub`、`exp`、`iat`、`jti`、`client_id`、`sid`、用户安全戳与作用域声明。API 对无效或过期令牌返回标准 `401`，对权限不足返回 `403`，不重定向登录页。

生产环境必须配置 RSA 密钥环：

- 一个活动私钥负责签名；
- 多个带 `KeyId` 的公钥负责验证当前与轮换期令牌；
- 私钥只来自 Secret Provider、证书存储或外部密钥服务，不提交到仓库；
- 配置缺失、活动 KeyId 不存在或密钥强度不合格时生产环境启动失败。

开发和测试环境在没有配置密钥时允许生成进程级临时 RSA 密钥，并输出明确警告；重启后旧 Access Token 失效是可接受的开发行为。后续标准 OIDC Provider 可以替换 `IAccessTokenIssuer`，API 继续使用 JwtBearer 认证。

## 7. Refresh Token、Cookie 与 CSRF

Refresh Token 是 256 位密码学随机不透明值，数据库只保存 SHA-256 哈希。默认有效期 7 天，每次成功刷新都轮换 Token 并延续同一会话族。检测到旧 Token 重用时撤销整个会话族，客户端必须重新登录。

浏览器通过 Cookie 持有 Refresh Token：

- 名称：`__Host-fullnet-refresh`；
- `HttpOnly=true`、`Secure=true`、`SameSite=Strict`、`Path=/`，禁止 `Domain`；
- 生产环境不允许降低 Cookie 安全属性。

登录成功同时下发一个可由前端读取的随机 CSRF Cookie。刷新和退出请求必须在 `X-CSRF-Token` Header 中回传同值，服务器使用固定时间比较；缺失或不匹配返回 `403` 与 `identity.csrf_validation_failed`。登录请求同时校验允许的 `Origin`，防止 Login CSRF。开发 HTTP 例外只能通过显式 Development 配置启用，并产生警告。

## 8. HTTP 契约与错误模型

### 8.1 Endpoint

| 方法 | 路径 | 身份要求 | 成功响应 |
|---|---|---|---|
| `POST` | `/api/v1/auth/login` | 匿名 + Origin 校验 + 限流 | `200 TokenResponse` 并设置 Refresh/CSRF Cookie |
| `POST` | `/api/v1/auth/refresh` | Refresh Cookie + CSRF | `200 TokenResponse` 并轮换 Cookie |
| `POST` | `/api/v1/auth/logout` | Refresh Cookie + CSRF | 始终幂等返回 `204` 并清 Cookie |
| `GET` | `/api/v1/me` | Bearer Token | `200 CurrentUserResponse` |

`TokenResponse` 只返回 `accessToken`、`tokenType=Bearer` 和 `expiresAtUtc`，不在 JSON 中返回 Refresh Token。`CurrentUserResponse` 返回用户 ID、用户名、显示名、租户 ID、作用域、权限集合和当前会话 ID，不返回密码哈希、安全戳或内部锁定状态。

### 8.2 ProblemDetails

稳定错误码至少包括：

- `identity.invalid_credentials`；
- `identity.invalid_refresh_token`；
- `identity.refresh_token_reuse_detected`；
- `identity.csrf_validation_failed`；
- `identity.origin_not_allowed`；
- `identity.validation_failed`。

外部 API 始终使用标准状态码与 ProblemDetails。Admin.NET 包络只由兼容适配器转换，Identity 核心 Endpoint 不直接返回统一成功包络。

## 9. 双管理端会话实现

### 9.1 公共行为

Vue 与 Layui 必须实现相同会话语义：

1. 登录提交用户名和密码，成功后只把 Access Token 保存在 JavaScript 内存；
2. 页面启动时调用刷新接口恢复会话，不从 LocalStorage 或 SessionStorage 恢复令牌；
3. API 请求自动附加 Bearer Token；
4. 遇到首个 `401` 时最多触发一次去重后的刷新，刷新成功只重放一次原请求；
5. 刷新失败立即清理内存状态并回到登录页，不形成重试循环；
6. 退出调用服务端撤销接口，随后无条件清理本地状态；
7. ProblemDetails 的 `code`、`detail` 和 `traceId` 使用同一展示规则。

共享的是 OpenAPI/HTTP 契约与 E2E 场景，不共享 Vue 组件或将 Vue 运行时引入 Layui。Layui 继续使用原生 ES Module；Vue 使用 Pinia 管理会话状态。

### 9.2 第一阶段页面

两端同步增加登录页、当前用户展示、退出入口和会话过期提示。现有概览页的 `/api/v1/me` 契约探针改为真实会话状态。租户切换、菜单权限树和按钮权限在 RBAC/租户账号后续切片实现，不在客户端伪造权限数据。

## 10. 安全、可观测性与限流

- 登录 Endpoint 使用按来源与用户名指纹组合的固定窗口/令牌桶限流，具体阈值可配置；
- 日志记录事件名、结果码、用户 ID、会话 ID 和 TraceId，不记录任何令牌或密码；
- 指标至少覆盖登录成功/失败、锁定、刷新成功/失败、重用检测和活动会话数量；
- 所有时间判断通过 `IClock`，随机令牌通过可替换安全随机源，保证确定性测试；
- 所有字符串输入设置长度上限，客户端元数据在持久化前截断和规范化；
- Secret 和私钥配置必须在启动校验中 fail-fast。

## 11. 测试与验收

### 11.1 单元测试

- 用户名规范化、密码策略和锁定边界；
- JWT 必需 Claim、过期时间和 KeyId；
- Refresh Token 哈希、轮换、并发消费和重用撤销；
- CSRF 固定时间比较与 Origin 规则；
- ProblemDetails 稳定错误码。

### 11.2 双数据库集成测试

SQL Server 与 MySQL 分别验证：

- 迁移可重复执行；
- 引导账号幂等创建；
- 登录、`/me`、刷新、退出闭环；
- 同一 Refresh Token 并发刷新只有一个成功；
- 旧 Token 重用撤销会话族；
- 失败锁定和成功清零；
- 审计与会话写入的事务一致性。

### 11.3 双前端测试

- Vue 与 Layui 各自覆盖登录成功/失败、启动恢复、401 单次刷新、刷新失败回登录和退出；
- 两端 E2E 使用同一场景清单，分别验证 Cookie/CSRF、当前用户和错误展示；
- Layui 生产依赖扫描继续确认不存在 Vue/React 等 SPA 运行时。

## 12. 发布与后续演进

本切片完成后，C1 的正式会话能力可以从 `Implementing` 推进到 `Verified`，但租户切换、国际化和完整权限导航仍单独跟踪。下一切片按以下顺序演进：

1. 用户管理与密码重置；
2. 角色、权限、菜单和双端动态导航；
3. 租户账号、租户切换和套餐约束；
4. 组织、职位与数据范围；
5. 在线用户、强制下线、验证码和外部 OIDC Provider。

任何后续扩展都必须复用本切片的会话族、审计、ProblemDetails 和双端等价门禁，不得另建一套旁路认证协议。

## 13. 参考资料

- [ASP.NET Core 10 JWT Bearer 配置](https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0)
- [ASP.NET Core 10 Authentication 概览](https://learn.microsoft.com/aspnet/core/security/authentication/?view=aspnetcore-10.0)
- [PasswordHasher&lt;TUser&gt; API](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.identity.passwordhasher-1?view=aspnetcore-10.0)
- [Microsoft.AspNetCore.Authentication.JwtBearer 10.0.10（MIT）](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer/10.0.10)
