# 执行清单 40 — OpenAccess 可观测性 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **40**（接入方访问日志、每日配额用量、有界 HMAC 签名调试；依赖 39；不回显服务器存储密钥、不跨应用读日志）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `GET …/access-logs`、`GET …/usage`、`POST …/signature-debug` |
| 服务 | `OpenAccessClientObservabilityService` |
| 权限 | `identity.open_access_clients.read`、`identity.open_access_clients.debug_signature` |
| Vue | `OpenAccessClientDetailDrawer.vue`（日志/用量/调试页签） |
| 迁移 | `140`（配额列）、`141`（调试权限，双库） |
| 集成 | `IdentityOpenAccessClientAssertions.VerifyObservabilityAndQuotaAsync` |
| 单元 | `OpenAccessClientObservabilityServiceTests` |

## OA01 核验结论（40 子集）

- **已有**：ApiKey 认证写入访问日志；`dailyRequestQuota=1` 时第二次认证 401；签名调试返回 canonical/期望签名且 diagnostics 不含明文 Secret；异构 clientId 查日志 404。
- **本槽**：新增 real-stack `host-open-access-clients-observability.spec.mjs`。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`OpenAccessClientObservabilityServiceTests` | **2/2** |
| `pnpm exec vitest run` …`open-access-clients.test.ts` | **2/2** |
| real-stack | `host-open-access-clients-observability.spec.mjs` |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
