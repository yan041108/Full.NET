# 超级管理员管理页真实栈 E2E 验证记录

- 日期：2026-07-21
- 切片：Vue/Layui 真实 API 超管目录加载、密码重认证授予与撤销；修复 real-stack Migrator 缺少 `PreV1NamingContract` 门禁导致 011 迁移失败

## 交付范围

| 层级 | 内容 |
|---|---|
| Bootstrap | `bootstrap-stack.mjs` 补齐 `PreV1NamingContract__*` 维护证据（与 Uuid 009 对称） |
| 辅助 | `loginHostAdminAccessToken` / `createHostUserViaApi` |
| E2E | `host-super-administrators.spec.mjs`：目录 + TOTP 条带可见、授予、审计、撤销（× Vue/Layui） |

## 本地验证

| 命令 | 结果 |
|---|---|
| `pnpm exec playwright test tests/host-super-administrators.spec.mjs` | **2/2** 通过 |
| `CI=1 pnpm exec playwright test tests/auth-smoke.spec.mjs tests/host-super-administrators.spec.mjs` | **4/4** 通过（含登录冒烟） |
| 完整 `CI=1 pnpm exec playwright test` | **28/38**（超管授予撤销 **2/2** 通过；机构/租户上下文 **10** 项本机失败，与本切片无关，见下方说明） |

> 说明：bootstrap 曾缺 `PreV1NamingContract` 导致 011 迁移失败；补齐后 Migrator 可执行至 016。默认禁止复用本机 Vite（`FULLNET_E2E_REUSE_SERVER=1` 才允许），避免陈旧进程缺 `VITE_API_BASE_URL` 表现为全套 `client.login_failed`。机构/租户上下文失败表现为进入租户后 `Full.NET Local` 选项 hidden，需另开切片排查。

## 明确仍开放

- Production 环境 TOTP 强制路径的真实栈覆盖（Development 下 `totpCode` 可选）
- 真实栈纳入 Redis
