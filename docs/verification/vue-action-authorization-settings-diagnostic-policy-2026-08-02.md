# Vue 页面/操作精确授权：Settings Diagnostic Policy

- 日期：2026-08-02
- 迁移：070
- 退役权限：`settings.diagnostic_policy.write`

## 目标权限

| 操作 | 权限码 |
| --- | --- |
| 更新策略 | `settings.diagnostic_policy.update` |
| 恢复安全默认 | `settings.diagnostic_policy.restore` |

## 验证

| 层级 | 夹具 |
| --- | --- |
| 单元 | `Host_diagnostic_policy_actions_bind_to_exact_permissions` |
| 架构 | `Api_v1_endpoints_do_not_bind_retired_settings_diagnostic_policy_write` |
| 迁移 | `Migration070SettingsDiagnosticPolicyActionPermissionsRecoveryTests` |
| Integration | `VerifyExactDiagnosticPolicyActionPermissionBoundariesAsync` |
| E2E | `diagnostic-policy.spec.mjs` |
