# Settings 枚举/常量元数据验证记录（2026-07-25）

## 范围

Host 只读枚举/常量目录：`IEnumCatalogContributor` + `EnumCatalogRegistry`；权限 `settings.enums.read`；API `/api/v1/settings/enum-catalogs`；首批目录 `settings.config_value_kind`。

非目标：DB 持久化、动态写、反射扫描、`ISettingsStore`。

## 证据摘要

| 层 | 结果 |
| --- | --- |
| Integration | `Host_enum_catalog_query` 双库 **2/2** |
| OpenAPI Node | **2/2** |
| client-contracts / Vue / Layui | 聚焦单测通过 |
| Mock parity | 「枚举常量目录列表与详情」**2/2**（`shell-parity` **40 → 42**，全量预计 **80 → 82**） |
| 真实栈 | `host-enum-catalogs` SQL Server **4/4** + MySQL（门槛 **54 → 58**） |

## 新鲜运行

| 命令 | 结果 |
| --- | --- |
| Integration `Host_enum_catalog` | **2/2** |
| Mock parity | **2/2** |
| 真实栈 SqlServer | **4/4** |
| 真实栈 MySql | **4/4** |

## 门槛

| 变更 | 说明 |
| --- | --- |
| Integration | **138 → 140** |
| Mock parity | **40 → 42** |
| 真实栈 | **54 → 58** |

## 关联

- 计划：[`2026-07-25-settings-enum-catalog-vertical-slice.md`](../superpowers/plans/2026-07-25-settings-enum-catalog-vertical-slice.md)
