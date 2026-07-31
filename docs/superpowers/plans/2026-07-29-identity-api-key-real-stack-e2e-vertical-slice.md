# Identity API Key 真实栈 E2E 纵向切片（2026-07-29）

## 目标

在 Vue + Layui 双管理端对运行中的 Full.NET API 验证 API Key 创建、一次性明文展示、轮换后旧钥失效、新钥可认证、禁用后拒绝认证。

## 范围

- `tests/e2e/admin-real-stack/tests/host-api-keys.spec.mjs`
- `tests/e2e/admin-real-stack/tests/support/real-stack-auth.mjs`（`findSeedAdminUserViaApi`）

## 验收

- `FULLNET_E2E_SKIP_BOOTSTRAP=1` + `FULLNET_E2E_API_URL=http://localhost:5149` 下 **6/6**（Vue 3 + Layui 3）
- 受限 `e2e-viewer`：API `403` + 导航无 API Key + 直链 `#/identity/api-keys` 展示 403

## 非目标

- API Key 使用审计 UI
- shell-parity Mock 轮换场景扩展
