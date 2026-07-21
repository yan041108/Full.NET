# Full.NET 超级管理员设计

- 日期：2026-07-18
- 状态：已批准
- 决策来源：项目所有者要求对标 Admin.NET，引入默认拥有全部权限的超级管理员账号概念
- 实现状态：角色标记、动态全权限、签名 Claim、Bootstrap、双库并发最后一名保护及双端标识已实现；远程高风险管理入口、可靠 Audit/Outbox、TOTP 强认证 Provider（后端）已实现；双端 MFA UI 与真实浏览器 E2E 尚未实现

## 1. 目标与非目标

Full.NET 必须提供一个开箱可恢复的超级管理员身份，用于首次部署、平台配置、租户治理和故障恢复。默认引导账号自动获得超级管理员能力；后续新增官方或项目模块权限时，不要求再次手工勾选权限。

“全部权限”只表示：对当前 `AuthorizationCatalog` 已注册、且适用于当前有效作用域的功能权限自动授权。它不表示：

- 绕过认证、账号禁用、锁定、安全戳、会话撤销、CSRF、CORS、限流或重新认证；
- 绕过 `TenantId` 数据隔离、HostOnly/Global SQL 声明或独立数据库租户选择；
- 自动取得未注册 Endpoint、任意 SQL、服务器文件、Secret 或 AI Tool；
- 跳过付款、删除、外发消息、密钥轮换等高风险操作的审计、双人复核或人工确认。

## 2. 方案比较

### 2.1 给账号写入全部权限行

旧版 Bootstrap 近似采用该方案：把当时全部 Host 权限写入 `fn_identity_role_permission`。优点是沿用普通 RBAC；缺点是新模块权限会漂移，只有再次同步后才完整，租户作用域权限也无法自然表达。当前实现已改为 2.3 的动态权限目录方案，不再依赖逐项权限行。

### 2.2 用户表布尔字段或硬编码用户名直接绕过

实现简单，但会在授权处理器中形成不可审计的魔法分支；改用户名、导入用户或复制数据库时容易误提权。该方案不采用。

### 2.3 受保护系统角色＋动态权限目录（采用）

超级管理员能力由持久化的 Host 系统角色表达，角色增加 `IsSuperAdministrator`。授权时先验证账号、会话和角色关系，再从代码 `AuthorizationCatalog` 动态投影当前有效作用域的全部权限。角色分配是可审计事实，权限目录是功能事实，不存储通配符 `*`，也不把每个权限行当作超级管理员能力的事实源。

现有 `host-administrator` 系统角色保持稳定 Code，升级迁移只增加并设置 `IsSuperAdministrator`，避免为名称美化破坏存量数据。管理端显示名称可调整为“超级管理员”，Code 不直接改名。

## 3. 数据模型与不变量

`fn_identity_role` 增加：

```text
IsSuperAdministrator bit/bool NOT NULL DEFAULT false
```

数据库检查约束保证该值为真时：

- `IsSystem = true`；
- `TenantId IS NULL`；
- `ScopeKey = 'host'`。

应用服务继续保证数据库难以跨表表达的不变量：

1. 默认 Bootstrap 账号必须分配到唯一的 `host-administrator` 超级管理员系统角色；
2. 系统角色不能由普通角色 CRUD 删除、复制、改 Code、改作用域或取消超级管理员标记；
3. 系统必须至少保留一个“账号启用＋角色启用＋关系有效”的超级管理员；禁止禁用或移除最后一个；
4. 可以显式增加多个超级管理员，但授予、撤销和账号禁用都属于高风险操作；
5. 角色权限明细页面对该角色显示“全部目录权限（动态）”，不允许勾选权限制造第二事实源。

升级期间既有 `fn_identity_role_permission` 行可保留用于回退，但授权不再依赖这些行判断超级管理员；迁移验证完成后停止为该角色同步逐项权限。

## 4. Bootstrap、Seed 与恢复

首次引导仍只允许 Migrator/Baseline Seed 显式执行：

- 用户名、密码和显示名来自 Secret/部署配置，不提供仓库默认密码；
- 新建或已存在的合法 Host 账号都会幂等获得超级管理员系统角色；
- 重复执行不覆盖密码，不产生第二个默认账号，不降低现有安全设置；
- Bootstrap 失败不留下“用户已建但角色未分配”的半完成状态；用户、系统角色、关系和审计在同一事务内提交；
- Production 恢复使用显式 Break-glass 命令和一次性 Secret，不能由 API 启动自动执行；恢复动作写入不可被普通日志丢弃的安全审计。

未来 `Identity` Baseline Contributor 必须调用同一领域服务，不复制角色 SQL 或权限同步算法。Test/Demo Overlay 可以创建普通受限管理员，不能再创建超级管理员。

## 5. 授权与 Token 语义

### 5.1 服务端事实源

登录、刷新和安全关键授权读取以下事实：

```text
有效用户
+ 有效 Session / SecurityStamp
+ Host 系统角色关系
+ IsSuperAdministrator
+ 当前 AuthorizationCatalog
+ ActorScope / EffectiveTenant
```

超级管理员在 Host 上下文只获得标记为 `AuthorizationScope.Host` 的权限；通过可信流程切换到某个租户后，只获得 `AuthorizationScope.Tenant` 权限，并继续受该 `TenantId` 约束。具有双作用域的权限按当前上下文投影。跨租户管理必须使用显式 HostOnly/Global Endpoint，不允许在租户查询中省略 `TenantId`。

### 5.2 JWT 与策略

Access Token 增加签名 Claim：

```text
fullnet_super_administrator = true
```

该 Claim 只能由服务端根据持久化系统角色关系签发，客户端输入、Header 或普通角色 Code 不可信。为避免模块增多后 JWT 权限 Claim 膨胀，超级管理员 Token 不要求枚举所有权限 Claim；动态权限策略执行以下顺序：

1. 验证 Token、Session、安全戳、用户状态和 Actor/Effective Scope；
2. 验证请求权限存在于 `AuthorizationCatalog` 且适用于当前有效作用域；
3. 普通账号检查精确 `fullnet_permission` Claim；
4. 超级管理员检查受信 `fullnet_super_administrator` Claim 后授权。

每个 Endpoint 仍必须声明精确权限。未知权限、未声明权限或作用域不匹配必须拒绝，禁止将超级管理员实现为“授权处理器无条件成功”。

当前实现通过 JWT `OnTokenValidated` 对每个受保护请求读取权威 Session/User 记录，同时核对 `sub`、`sid`、SecurityStamp、账号/锁定状态、Refresh Session 活性、ActorScope、EffectiveScope 与 TenantId。该 S0 判定暂不使用缓存，因此授予、撤销、刷新轮换和上下文切换后旧 Access Token 会立即失效；未来若引入缓存，必须先建立同步本机失效和可靠跨节点传播。

### 5.3 客户端契约

`GET /api/v1/me` 以加法兼容增加：

```json
{
  "isSuperAdministrator": true
}
```

同时返回当前有效作用域的规范权限集合，供 Vue、Layui、uni-app 和 Flutter 使用相同导航/按钮逻辑。客户端可以展示超级管理员标识和高风险提示，但不得用该字段绕过服务端权限，也不得自行补全未知菜单。

## 6. 管理与安全门禁

超级管理员管理不复用普通角色权限勾选 API。专用领域服务至少执行：

- 当前操作者本身是有效超级管理员；
- 最近重新认证，Production 默认要求 MFA/强认证 Provider 可用后再开放远程授予；
- 目标是 Host 账号，不能把租户账号直接提升为平台超级管理员；
- 并发版本检查，防止两个请求同时移除最后一个超级管理员；
- 授予、撤销、禁用、恢复和失败原因全部审计；
- 变更用户安全戳并撤销目标账号全部活动 Session；
- 缓存按 S0 安全数据处理，禁止 Fail-Safe，提交后先清本机再通过 Outbox/Backplane 修复其他节点。

第一阶段远程写入口仅允许 Development/Testing 通过 `Identity:EnableRemoteSuperAdministratorManagement=true` 显式开启，并要求当前密码重认证。Production 须同时满足：[ADR-0004](../../architecture/adr/ADR-0004-production-super-admin-strong-reauth.md) 规定的 TOTP 强认证 Provider（`Identity:EnableTotpStrongReauthentication=true`）、操作者已登记 TOTP，以及请求中的当前密码与验证码；禁止只修改配置绕过。只读列表与审计仍受 Host 精确权限保护。

双管理端必须同步实现超级管理员标识、系统角色只读状态、授予/撤销确认、最后一名保护错误和审计入口。只有服务端、SQL Server/MySQL、Vue/Layui 和真实后端 E2E 全部通过后才可标记 `Verified`。

## 7. 测试与验收

至少覆盖：

- Bootstrap 首次、重复、已有账号、事务失败和 Secret 脱敏；
- 新增模块权限后无需更新角色权限行即可授权；
- Host/Tenant 双作用域投影、未知权限拒绝和租户隔离不被绕过；
- 伪造 Claim/Header、普通系统角色、禁用用户/角色、撤销 Session 全部拒绝；
- 最后一名超级管理员不能撤销、禁用或删除，并发操作只能有一个成功；
- 多名超级管理员的授予/撤销、SecurityStamp、Session 撤销和缓存失效；
- SQL Server/MySQL 从现有 003/004 结构升级、半完成恢复和回滚/前滚说明；
- Vue/Layui 相同权限导航、高风险确认和真实 API E2E；
- 日志、ProblemDetails、审计和遥测不泄露密码、Token 或 Secret。

## 8. 参考与关联

- [Identity 会话基础设计](2026-07-17-identity-session-foundation-design.md)
- [租户上下文、权限与导航设计](2026-07-17-tenant-context-permission-navigation-design.md)
- [种子数据模块设计](2026-07-17-seed-data-module-design.md)
- [超级管理员实施计划](../plans/2026-07-18-super-administrator.md)
