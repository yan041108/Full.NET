# Identity 用户/改密 real-stack 与 WCAG（Lane A 验收）

**日期**：2026-09-22

## real-stack

| 套件 | 路径 |
|------|------|
| Host 用户 | `tests/e2e/admin-real-stack/tests/host-users.spec.mjs` |
| 超管 | `host-super-administrators.spec.mjs` |
| Production TOTP | `host-super-administrators-production-totp.spec.mjs`（CI job `real-stack-e2e-production-totp`） |
| 改密/安全 | `SecuritySettingsView` + `/api/v1/me/password`（集成矩阵 Identity） |

## WCAG

- admin-parity / axe：超管页对比度已收口（见 `2026-09-22-identity-super-admin-b1-closeout.md`）
- 用户列表掩码/揭示：随 `host-users` 与 parity 子集执行

## CI 跟踪

合入 `main` 后确认：`build-test`、`real-stack-e2e`、`real-stack-e2e-production-totp` 绿后再讨论 capability-status Identity 用户行升档。
