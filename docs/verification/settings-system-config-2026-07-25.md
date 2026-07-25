# Settings Host 系统配置验证记录（2026-07-25）

## 范围

Host 作用域系统配置项纵向切片：`fn_settings_config_entry`、权限 `settings.config.read|write`、API `/api/v1/settings/config-entries`、Vue/Layui 双端、Mock parity 与真实栈冒烟。

明确非目标：租户/用户覆盖、`ISettingsStore<T>`、敏感值加密、L5 多语言说明。

## 证据摘要

| 层 | 结果 |
| --- | --- |
| 迁移 | `021_SettingsConfigEntry.sql` SQL Server + MySQL |
| Integration | Settings `Host_config_entry_management` 双库通过（含 403、重复键、ValueKind 校验、乐观锁、by-key、禁用、OpenAPI） |
| OpenAPI Node | `settings-config-entries-contract.test.mjs` **2/2** |
| client-contracts / Vue / Layui 单测 | 聚焦配置相关用例通过 |
| Mock parity | 「系统配置列表、创建与禁用在两端保持一致」**2/2**（门槛 `shell-parity` **38 → 40**，全量预计 **78 → 80**） |
| 真实栈 | `host-config-entries.spec.mjs`：SQL Server **4/4** + MySQL **4/4**（门槛 **50 → 54**） |

## 门槛变更

| 变更 | 说明 |
| --- | --- |
| Integration | 已于 Task 1 调整为 **138** |
| Mock parity | `shell-parity` **38 → 40** |
| 真实栈 E2E | **50 → 54** |

## 新鲜运行

| 命令 | 结果 |
| --- | --- |
| Integration `Host_config_entry` + `Host_dict` | **4/4** |
| OpenAPI `settings-config-entries-contract` | **2/2** |
| client-contracts / Vue / Layui 聚焦单测 | 通过 |
| Mock parity `-g 系统配置列表、创建与禁用` | **2/2** |
| 真实栈 SqlServer `host-config-entries` | **4/4**（迁移含 `021_SettingsConfigEntry`） |
| 真实栈 MySql `host-config-entries` | **4/4** |

## 缺口

- 租户级 / 用户级配置覆盖与解析优先级未交付。
- 强类型 `ISettingsStore<T>` 与缓存失效未交付。
- 全量真实栈矩阵（非聚焦冒烟）与 CI 绿灯仍为开放项；未闭合前不得标 `Verified`。

## 关联

- 计划：[`2026-07-25-settings-system-config-vertical-slice.md`](../superpowers/plans/2026-07-25-settings-system-config-vertical-slice.md)
- 字典切片：[`settings-dictionary-2026-07-25.md`](settings-dictionary-2026-07-25.md)
