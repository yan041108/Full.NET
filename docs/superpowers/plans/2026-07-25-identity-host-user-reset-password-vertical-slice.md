# Identity Host 用户管理员重置密码纵向切片

- 日期：2026-07-25
- 状态：**Build-verified**
- 验证：[验证记录](../../verification/identity-host-user-reset-password-2026-07-25.md)

## 目标

Host 管理员为活跃用户重置密码；重置后旧凭据失效、所有会话撤销；双端管理 UI 与 Mock parity 扩展既有用户管理场景。

## 交付清单

1. [x] `POST /api/v1/identity/users/{userId}/reset-password`（`identity.users.write`）
2. [x] Integration：`VerifyResetPasswordInvalidatesOldCredentialsAsync`（SQL Server + MySQL）
3. [x] OpenAPI：`identity-host-users-v1.json` + 静态门禁
4. [x] `packages/client-contracts`：`ResetHostUserPasswordRequest`
5. [x] Vue `UsersView` + `api/users.ts`；Layui `users.js`
6. [x] `shell-parity`「用户列表、创建与禁用」场景内增补重置密码步骤（门槛 **52** 不变）
7. [x] Integration 门槛 **150 → 152**

## 非目标

- 用户自助改密、邮件重置链接、MFA 重认证
- 真实栈 E2E 增补（沿用既有 `host-users.spec.mjs` 冒烟）
