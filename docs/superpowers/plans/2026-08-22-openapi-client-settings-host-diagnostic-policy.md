# OpenAPI 客户端迁移：Settings Host Diagnostic Policy

**Goal:** 将 `ui/admin/src/api/diagnostic-policy.ts` 迁入 OpenAPI 生成客户端（`settings-host-diagnostic-policy`）。

**Architecture:** 主 Tag `SettingsHostDiagnosticPolicy`；手写 `isDiagnosticPolicy`/`isDiagnosticPolicyRule` 保留。

**Status:** `Slice-passed` — 见 [`../verification/openapi-client-settings-host-diagnostic-policy-2026-08-22.md`](../verification/openapi-client-settings-host-diagnostic-policy-2026-08-22.md)。

| operationId | Vue 导出 |
| --- | --- |
| `settingsGetHostDiagnosticPolicy` | `getDiagnosticPolicy` |
| `settingsUpdateHostDiagnosticPolicy` | `updateDiagnosticPolicy` |
| `settingsRestoreHostDiagnosticPolicy` | `restoreDiagnosticPolicy` |

清单 115→118（3 条 pilot）。
