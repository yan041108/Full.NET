# Identity Host 用户管理员重置密码验证记录（2026-07-25）

- 范围：`POST /api/v1/identity/users/{userId}/reset-password`；撤销全部会话；Vue/Layui 重置密码操作
- 计划：[实施计划](../superpowers/plans/2026-07-25-identity-host-user-reset-password-vertical-slice.md)
- 状态：**Build-verified**（用户管理整体仍为 `Implementing`；不能标记 `Verified`）

## 自动化证据

| 层 | 结果 |
|---|---|
| Integration 双库 | `VerifyResetPasswordInvalidatesOldCredentialsAsync` SQL Server/MySQL **2/2** → **150 → 152** |
| OpenAPI 夹具 | `identity-host-users-v1` 静态门禁（含 `reset-password` 路径） |
| client-contracts | `isResetHostUserPasswordRequest` |
| Vue API 单测 | `users.test.ts` 重置密码 **1/1** |
| Mock parity | 「用户列表、创建与禁用」场景内重置密码 × 双端（`shell-parity` **52** 不变） |
| 四处 canonical 门槛 | **349/7/38/152** |

## 行为摘要

- 仅 `identity.users.write` 可调用；目标用户必须存在且 `IsActive`
- 校验密码策略；更新 `PasswordHash` 与 `SecurityStamp`；清除锁定；递增 `Version`
- 重置后调用 `RevokeAllUserSessions`；旧密码登录失败、新密码可登录

## 非目标

- 自助改密、邮件/短信重置、Production MFA 门禁
