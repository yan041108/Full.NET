# 真实栈 Redis 与 Production TOTP 验证记录

- 日期：2026-07-29
- 状态：`Build-verified`（Production TOTP 与 Redis ready 已在本机 Docker 环境通过）
- 任务基线：`975da1ee9c0e073e6cfbf0bd2c2cd530063d8313`
- 范围：Task H4 交付真实性补证——真实栈纳入 Redis；Production TOTP 强制路径 HTTP 验证

## 交付内容

| 项 | 说明 |
| --- | --- |
| Redis | `bootstrap-stack.mjs` 启动 `redis:8.6` Testcontainer，注入 `Cache__RedisConnectionString` |
| 健康检查 | `redis-ready-health.spec.mjs` 断言 `/health/ready` 在配置 Redis 后返回 Healthy |
| Production TOTP | `FULLNET_E2E_STACK_PROFILE=production-totp`：Production 环境、RSA 签名、`EnableTotpStrongReauthentication`、Baseline Seed |
| API 门禁 | `production-totp-grant.test.mjs`：TOTP 登记 → 缺码 401 → 带码授予成功 |
| 工具 | `totp-utils.mjs` 与 Identity `TotpAlgorithm` 对齐的 RFC 6238 计算 |
| 运维 | [缓存可靠性指标运维说明](../operations/cache-reliability-telemetry.md) |

## 新鲜自动验证（Docker 已启动）

| 验证 | 结果 |
| --- | --- |
| `pnpm --filter @fullnet/admin-real-stack-e2e test:production-totp` | **1/1** 通过（Production + TOTP 登记/缺码 401/带码授予） |
| `playwright test tests/redis-ready-health.spec.mjs`（Vue + Layui） | **2/2** 通过 |
| `totp-utils.test.mjs` + provisioner | **7/7** 通过 |
| `pnpm test:clients` | 连续通过 |

Production 栈修复：`Files__Local__RootPath` + RSA 签名键 `e2eprodsigning`（无连字符，避免配置绑定失败）。

## 待补跑

```powershell
pnpm test:e2e:real
pnpm test:e2e:real:mysql
```

完整 Playwright 真实栈（84×2 项）与 Realtime 专用 Backplane 浏览器断网恢复仍开放。

## 仍开放

- 真实栈纳入 Realtime 专用 Redis Backplane 与多 API 节点断网恢复浏览器 E2E
- Outbox 修复推送与 SignalR 跨 Tab 故障注入
