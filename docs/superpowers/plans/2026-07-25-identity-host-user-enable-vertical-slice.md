# Identity Host 用户启用纵向切片

- 日期：2026-07-25
- 状态：**Build-verified**
- 验证：[验证记录](../../verification/identity-host-user-enable-2026-07-25.md)

## 目标

Host 管理员重新启用已禁用用户；启用后可再次登录；双端管理 UI 与 Mock parity 扩展既有用户管理场景。

## 交付清单

1. [x] `POST /api/v1/identity/users/{userId}/enable`（`identity.users.write`）
2. [x] Integration：`VerifyEnableUserRestoresLoginAsync`（纳入 `Host_user_management` 双库契约）
3. [x] OpenAPI：`identity-host-users-v1.json` + 静态门禁
4. [x] Vue `UsersView` + `api/users.ts`；Layui `users.js`
5. [x] `shell-parity`「用户列表、创建与禁用」场景内增补启用步骤（门槛 **52** 不变）

## 非目标

- 启用时自动恢复角色/会话（用户需重新登录）
- 真实栈 E2E 增补
