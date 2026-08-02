# Vue 页面/操作精确授权：Settings Config Entries

- 日期：2026-08-02
- 迁移：069
- 退役权限：`settings.config.write`

## 目标权限

| 操作 | 权限码 |
| --- | --- |
| 创建配置项 | `settings.config.create` |
| 编辑配置项 | `settings.config.update` |
| 禁用配置项 | `settings.config.disable` |

## 验证

| 层级 | 夹具 |
| --- | --- |
| 单元 | `Host_config_entries_actions_bind_to_exact_permissions` |
| 架构 | `Api_v1_endpoints_do_not_bind_retired_settings_config_write` |
| 迁移 | `Migration069SettingsConfigEntryActionPermissionsRecoveryTests` |
| Integration | `VerifyExactConfigEntryActionPermissionBoundariesAsync` |
| OpenAPI | `settings-config-entries-contract.test.mjs` |
