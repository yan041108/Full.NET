# 双管理端 TOTP 登记与超管写操作 UI 验证记录

- 日期：2026-07-21
- 切片：Vue/Layui 同步 TOTP 登记确认与 grant/revoke `totpCode`

## 交付范围

| 层级 | 内容 |
|---|---|
| 契约 | `@fullnet/client-contracts` TOTP status/begin 类型与守卫 |
| Vue | `totpEnrollment` API、超管页登记条带、授予/撤销携带可选 `totpCode` |
| Layui | 对等登记条带与授予表单字段；撤销二次提示收集 TOTP |
| i18n | `packages/admin-i18n` zh-CN/en-US `superAdmin.totp*` |
| E2E | Mock parity 断言 grant 正文含 `totpCode` |

## 本地验证

| 命令 | 结果 |
|---|---|
| `pnpm --filter @fullnet/client-contracts test` | **35/35** 通过 |
| `pnpm --filter @fullnet/admin test` | **61/61** 通过 |
| `pnpm --filter @fullnet/admin-layui test` | **56/56** 通过 |
| `pnpm --filter @fullnet/admin-parity-e2e exec playwright test -g "超级管理员列表"` | **2/2** 通过（vue-admin + layui-admin） |

## 明确仍开放

- Production 环境 TOTP 强制路径的真实栈覆盖（Development 密码重认证路径见[超管真实栈验证](./identity-super-admin-real-stack-2026-07-21.md)）
