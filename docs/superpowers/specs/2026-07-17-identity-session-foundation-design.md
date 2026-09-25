# Full.NET 身份与会话基础切片设计

- 日期：2026-07-17
- 状态：已确认
- 2026-09-13 演进确认：新增 §14 OIDC 认证中心与 SSO，按 [ADR-0011](../../architecture/adr/ADR-0011-identity-oidc-sso-evolution.md) 和[执行计划](../plans/2026-09-13-identity-oidc-sso-evolution.md)推进；P0 尚未开始，不改变已实现能力的验证状态。原文双管理端描述为历史切片边界，后续仅持续交付 Vue，Layui 保持冻结。
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

默认引导账号的动态全权限、最后一名保护和高风险管理以[超级管理员设计](2026-07-18-super-administrator-design.md)为准。动态权限、角色标记和双库最后一名并发保护现已实现；公开高风险管理、可靠审计和双端完整流程完成前，文档和发布说明仍不得混称为已经具备完整超级管理员管理能力。
- 重复执行保持幂等，不覆盖已有密码；
- 仓库、镜像、日志和命令行参数中不得包含默认密码或明文秘密。

## 6. Access Token 与密钥管理

Access Token 使用 JWT Bearer，仅用于 Full.NET API 访问，默认有效期 10 分钟。至少包含并严格校验 `iss`、`aud`、`sub`、`exp`、`iat`、`jti`、`client_id`、`sid`、用户安全戳与作用域声明。API 对无效或过期令牌返回标准 `401`，对权限不足返回 `403`，不重定向登录页。

生产环境必须配置 RSA 密钥环：

- 一个活动私钥负责签名；
- 多个带 `KeyId` 的公钥负责验证当前与轮换期令牌；
- 私钥只来自 Secret Provider、证书存储或外部密钥服务，不提交到仓库；
- 配置缺失、活动 KeyId 不存在或密钥强度不合格时生产环境启动失败。

开发和测试环境在没有配置密钥时允许生成进程级临时 RSA 密钥，并输出明确警告；重启后旧 Access Token 失效是可接受的开发行为。`IAccessTokenIssuer` 是令牌签发扩展点，不独自承担 OIDC 服务端；后续按 §14 同时补齐客户端、协议、会话与资源验证适配，保留现有权威会话校验。

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

业务 API 使用标准状态码与 ProblemDetails。Admin.NET 包络只由兼容适配器转换，Identity 核心业务 Endpoint 不直接返回统一成功包络。§14 实际注册的 OAuth/OIDC 协议端点按 ADR-0011 使用协议规定的响应与错误；该例外不适用于客户端管理或普通业务 API。

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

后续扩展必须保留本切片的会话撤销、审计与业务 ProblemDetails 语义；新增标准 OIDC 会话按 §14 与旧会话族受控关联，不形成绕过认证的旁路。新功能只执行 Vue 主交付线门禁，历史 Layui 双端门禁不再扩大后续交付范围。

## 13. 参考资料

- [ASP.NET Core 10 JWT Bearer 配置](https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0)
- [ASP.NET Core 10 Authentication 概览](https://learn.microsoft.com/aspnet/core/security/authentication/?view=aspnetcore-10.0)
- [PasswordHasher&lt;TUser&gt; API](https://learn.microsoft.com/dotnet/api/microsoft.aspnetcore.identity.passwordhasher-1?view=aspnetcore-10.0)
- [Microsoft.AspNetCore.Authentication.JwtBearer 10.0.10（MIT）](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer/10.0.10)

## 14. OIDC 认证中心与 SSO 演进（2026-09-13 已确认）

### 14.1 授权、状态与唯一来源

项目所有者已确认[研究建议](../../verification/2026-09-13-identity-oidc-sso-research-validation.md)并要求同步项目文档及执行计划。采用 [ADR-0011](../../architecture/adr/ADR-0011-identity-oidc-sso-evolution.md) 的路线：保留现有 Identity，优先验证 OpenIddict，依次推进 P0 可行性、P1 最小 SSO、P2 接入治理和 P3 Vue 迁移。文档已确认，运行能力未验证；当前步骤见[唯一活动执行计划](../plans/2026-09-13-identity-oidc-sso-evolution.md)。

本节补充原规格的 OIDC 扩展与协议响应边界；除明确列出的补充外，不替代旧登录、刷新、改密、权限与租户契约。现有外部 OIDC 登录是客户端能力，不作为新服务端已经实现的证据。

### 14.2 功能与部署范围

- Identity 模块拥有账号、安全策略、协议状态与会话管理；Composition 装配，API/Worker/Migrator 继续分工。
- 不新增独立生产认证宿主或平行用户库；不引入 EF Core。Store 使用自有执行器、显式 SQL 与双库迁移。
- 首期使用 Host 账号和两个固定注册的第一方客户端；第二客户端仅用于证明 SSO，不新增持续维护的后台产品。
- 交互式接入要求授权码 + PKCE S256。公开客户端不依赖 client secret，机密客户端同时执行客户端认证；精确注册回调、退出回跳与允许 scope。
- 新中心入口在发布验收前不得作为生产默认入口；旧认证入口在迁移与回退验证完成前保持可用。

### 14.3 协议和资源授权契约

| 边界 | 要求 |
| --- | --- |
| Discovery／JWKS | 稳定 HTTPS Issuer；元数据与实际端点一致；JWKS 仅公钥 |
| Authorize／Token | 客户端、redirect_uri、scope、PKCE 绑定；授权码一次性原子消费；错误遵守协议 |
| ID Token／UserInfo | 最小身份信息；客户端验证签名、iss、aud、nonce、有效期，UserInfo sub 与认证结果一致 |
| Access Token／资源 API | 按资源设置 aud；检查 token 类型、scope、用户权限、权威会话、租户和数据归属 |
| 退出／撤销 | 当前应用、中心全部会话、管理员强制下线分开；后续请求和刷新遵守权威状态 |
| 管理 API | 继续标准状态码、ProblemDetails、逐页面／逐操作权限和源生成契约 |

协议端点采用协议原生字段名、重定向和错误格式，不交给业务包络或通用 camelCase 转换。具体 URI 和启用集合随 P0/P1 冻结，不能把全部 `/connect/*` 通配为匿名旁路；协议处理器仍负责客户端／用户认证及授权。登录和会话写 UI 保留 Origin、CSRF、可信代理、限流与秘密保护。

`scope` 与 `fullnet_scope` 保持独立；一般外部客户端不得获得完整管理权限、安全戳或超管声明。JWT 验证器必须拒绝 ID Token 替代 Access Token。OpenIddict Access Token 加密及现有 JwtBearer 的适配在 P0 验证，不默认共享中心私钥给资源 API。

### 14.4 会话与可信上下文

1. 中心登录会话稳定表示一次中心认证；应用会话绑定客户端和该中心认证；刷新令牌族管理该应用的续期与重用检测。三者各有生命周期，不复用不断轮换的旧刷新记录 ID 表达稳定 SSO sid。
2. 旧 `/api/v1/auth/*` 会话仍执行现有事件与 `AccessSessionValidator`。OIDC 会话通过独立、可验证的适配获得可信主体，不以放宽旧 Claim 要求方式接入。
3. 禁用账号、改密、安全戳变化和强制下线后，中心不能凭旧 Cookie 重新授权；已进入处理中的请求不承诺回溯撤销，但后续鉴权必须读取权威状态。
4. OAuth 授权结果不直接决定租户身份。tenant 参数仅是选择请求，经应用可用租户、用户成员关系与上下文切换授权检查后建立可信上下文；A 切租户不隐式改变 B。
5. 复用主认证会话不等于 `idsrv.session` Cookie 存在即通过；`prompt`、`max_age`、consent 和认证强度仍需满足。现有 TOTP 强重认证不自动等于完整 OIDC MFA 登录。
6. 第一方受保护 Full.NET 路径保留权威检查；未来外部离线 API 必须给出令牌窗口、撤销延迟与故障策略，未测量前不声称全局即时下线。

### 14.5 存储、多实例与客户端

Application、Authorization、Scope、Token Store 保持在 Identity 内，底层采用自有执行与事务边界。授权码消费、刷新轮换、授权撤销、客户端停用、账号状态检查与过期清理必须覆盖并发和故障窗口；所有新增表使用应用端 UUID v7、模块命名规则及成对迁移。迁移编号在实施时根据真实最大值分配，不在文档预占。

中心与各客户端的主 Cookie 按各自服务边界设置。正常 SSO 使用顶层跳转；BFF／服务端换码、令牌存储和 Data Protection 需通过双实例实测后再迁移 Vue。不能复用旧 `SameSite=Strict` Refresh Cookie 充当跨站 OIDC 主认证 Cookie，也不能为 SSO 降低旧 Cookie 安全属性。协议临时 Cookie 按所选 response_mode 和真实浏览器路径验证。

签名、加密和 Data Protection key ring 分清职责；同一服务的多实例共享必要材料，不让所有业务应用共享中心解密能力。组件进程缓存不能削弱客户端禁用／撤销；关键状态失败关闭。退出通知的持久化、幂等和重试随 P2 定义，不用缓存 Outbox 替代权威状态。

### 14.6 门禁、迁移与排除项

- P0：验证组件版本／闭包、双库 Store、一次性授权码、旧会话衔接和 Linux 原生运行。失败则记录原因，不关闭 AOT 或降低安全语义换绿。
- P1：两个受控客户端真实浏览器 SSO、协议负向用例、主会话与本地会话独立。
- P2：客户端管理、scope、授权／刷新／撤销、全局退出、双实例和密钥轮换；先冻结传播时限，再记录实测结果。
- P3：Vue 接入、契约兼容、回退与旧入口退役。只清客户端状态不能算服务端撤销；只切换 UI 入口不能算令牌信任回退。
- V01—V24 为稳定场景编号；所有任务保留真实执行证据，未执行／跳过不能标通过。执行位置与测试影响集遵守开发质量 §11。
- 暂不扩大到动态客户端注册、SAML/CAS、机器身份、设备流、令牌交换、跨租户账号合并或 Layui 新功能。

## 15. 认证事件日志（2026-09-25 规划增补）

本节定义认证事件日志的目标边界。Host 查询 API、Vue 页面、部分 OIDC 采集、默认关闭的保留清理和受控导出已完成首条纵向切片；完整事件覆盖与故障验证仍在计划中。实施和状态的唯一来源为[认证事件日志计划 AE01—AE06](../plans/2026-09-25-authentication-event-logs.md)。

### 15.1 现状与职责

Identity 已拥有 `AuthAuditEvent`、`fn_identity_auth_audit` 和登录、退出、刷新等写入。此前“没有登录/退出日志”的描述不准确；本次目标是补齐事件覆盖和管理查询体验，复用现有数据，不创建第二套认证日志表。

| 日志类别 | 表达内容 | 所有者 |
| --- | --- | --- |
| 访问日志 | API 请求、状态码、耗时；不证明认证成功或实际退出 | Auditing，B2 |
| 操作日志 | 业务写操作的请求结果 | Auditing，B1 |
| 异常日志 | 执行异常及排障关联 | Auditing，B1 |
| 认证事件日志 | 登录/退出、凭据/MFA、会话及身份安全状态变化 | Identity，按认证事务语义持久化 |

后台导航统一归入“运维与安全 → 审计日志”，不改变数据所有权。Auditing 不直查 Identity 表，不通过 Outbox 复制认证日志。

### 15.2 事件与事务语义

覆盖成功/失败登录、锁定与解锁、MFA 验证/恢复、密码修改/重置、刷新与重放拒绝、主动退出和管理撤销。OIDC 区分中心登录、中心会话复用、应用会话建立、应用退出和中心退出；会话自然过期、关闭浏览器、下游登出通知失败不能被解释为用户主动退出或全局退出成功。

事件以领域状态和协议处理结果为准，禁止通过 HTTP 200/401 推断。成功安全状态变更与 B0 审计同事务；认证拒绝记录必须有明确的提交边界，不能随失败命令回滚而静默丢失。审计存储失败不能放行认证。既有 `login/logout/refresh` 及结果码保持可读，通过静态目录归类，不批量重写历史。

### 15.3 数据、安全与查询

保留现有用户、刷新会话、用户名指纹、结果码、时间及上下文，规划增加可空的操作者、Trace、认证方式、客户端、中心会话及应用会话关联。旧字段 `SessionId` 不混存不同会话类型，未知用户/租户保持空值。用户名哈希仍属敏感信息，不视为匿名化；当前存储的 IP 地址也不能误称为不可逆指纹。

不采集密码、验证码、Token、Cookie、授权码、完整回调 URL 或请求正文。默认响应隐藏账号指纹，IP 脱敏，User-Agent 安全渲染；关联用户删除后仍保留事件。

首期只提供 Host 管理查询和详情，使用独立精确权限。列表默认最近 24 小时、单次最多 31 天、每页最多 100 条，以时间和 ID 游标排序；详情执行同样的权限/上下文检查。租户查询不在首期开放，不能把客户端传入的 tenantId 当作事件归属。

### 15.4 运维与验收

认证事件不进入可丢弃的访问日志队列。保留由 Identity Worker 独立配置、小批量清理，默认关闭；导出另设权限并审计。SQL Server/MySQL 迁移和事务行为、源生成/AOT、权限负例、真实登录/退出页面及多实例撤销均须验证。实现前不得把规划功能标为已交付。
