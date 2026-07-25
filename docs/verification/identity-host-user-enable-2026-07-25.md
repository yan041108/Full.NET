# Identity Host 用户启用验证记录（2026-07-25）

- 范围：`POST /api/v1/identity/users/{userId}/enable`；Vue/Layui 启用操作
- 计划：[实施计划](../superpowers/plans/2026-07-25-identity-host-user-enable-vertical-slice.md)
- 状态：**Build-verified**（用户管理整体仍为 `Implementing`）

## 自动化证据

| 层 | 结果 |
|---|---|
| Integration 双库 | `VerifyEnableUserRestoresLoginAsync` 纳入 `Host_user_management` **2/2**（门槛 **152** 不变；本机缺容器运行时未重跑，以 CI 为准） |
| OpenAPI 夹具 | `identity-host-users-v1` 含 `enable` 路径 |
| Vue API 单测 | `users.test.ts` 创建/禁用/启用 **1/1** |
| Mock parity | 「用户列表、创建与禁用」场景内禁用后启用 × 双端 **2/2** |
| 四处 canonical 门槛 | **349/7/38/152**（本切片未新增 Integration 测试方法） |

## 行为摘要

- 仅对已禁用 Host 用户生效；`IsActive` 置回 `true` 并递增 `Version`
- 不轮换 `SecurityStamp`、不自动恢复会话；启用后凭据可重新登录

## 非目标

- 批量启用、审计事件专码、Production MFA
