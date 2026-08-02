# Vue 页面/操作精确授权：Settings Dict Types

- 日期：2026-08-02
- 迁移：067
- 退役权限：`settings.dict_types.write`

## 目标权限

| 操作 | 权限码 |
| --- | --- |
| 创建类型/字典项 | `settings.dict_types.create` |
| 编辑类型/字典项 | `settings.dict_types.update` |
| 禁用类型/字典项 | `settings.dict_types.disable` |

## 验证

| 层级 | 夹具 |
| --- | --- |
| 单元 | `Host_dict_types_actions_bind_to_exact_permissions` |
| 架构 | `Api_v1_endpoints_do_not_bind_retired_settings_dict_types_write` |
| 迁移 | `Migration067SettingsDictTypeActionPermissionsRecoveryTests` |
| Integration | `VerifyExactDictTypeActionPermissionBoundariesAsync` |
| OpenAPI | `settings-dict-types-contract.test.mjs` |