# Wave 2–3 实施收口（2026-09-22）

## 文档与治理

- [`capability-status.md`](../roadmap/capability-status.md)：Dapper 行同步 F08b 清债。
- [`2026-09-16-foundation-productization.md`](../superpowers/plans/2026-09-16-foundation-productization.md)：追加 §2026-09-22 F08b/F08a 切片，不改动 F01–F16 未勾选项正文。
- [`2026-09-22-wave3-workflow-dataapproval-gap-audit.md`](2026-09-22-wave3-workflow-dataapproval-gap-audit.md)：清单 01–12 与源码/证据对照。
- OpenAPI manifest 计数门禁更新为 **557**（`identityRetireHostUser`）。

## 测试增量

| 类型 | 路径 |
|------|------|
| Integration | `TenantQuotaMetricIdReconciliationAssertions`、`DataApprovalApiAssertions` |
| E2E | 上列 + `data-approval-requests`、`workflow-todos-history`、`workflow-forms` 子表、`workflow-instance-business`、`serial-number-rules` 审批提交（`6f2c027a`） |
| admin-parity WCAG | Document 子集（`accessibility-i18n.spec.mjs`）；B2 Tenancy/Org/Files 路径扩展留待 parity 夹具稳定后合入 |

## 未关闭（登记不冒充完成）

- Workflow Recovery **Linux Worker 原生 + 通知投影 E2E**（见 gap-audit）。
- B2 parity `Verified` 升档（见 [wave2-b2-verified-progress](2026-09-22-wave2-b2-verified-progress.md)）。
- 远端 `integration-matrix` / `real-stack-e2e*` 需在 push 后核对。
