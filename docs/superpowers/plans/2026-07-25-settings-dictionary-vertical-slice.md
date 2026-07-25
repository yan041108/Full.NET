# Settings 数据字典纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。行为变更必须先失败测试再实现。本切片首次引入 **Settings 模块**与 Host 作用域数据字典。

- 建立日期：2026-07-25
- 状态：**Build-verified（Task 1–6 + 字典项 UI + 双库真实栈聚焦 + Integration 均已实跑）**
- 批准依据：
  - [`capability-status.md`](../../roadmap/capability-status.md) C2.2 字典、系统配置
  - [`adminnet-feature-parity.md`](../../roadmap/adminnet-feature-parity.md)「字典管理」
  - [`2026-07-17-fullnet-architecture-design.md`](../specs/2026-07-17-fullnet-architecture-design.md) §6.4

**Goal:** Host 管理员维护系统数据字典类型与字典项（列表/详情/创建/更新/禁用）；租户级与用户级字典、系统强类型配置留后续切片。

**Architecture:** 新模块 `Full.NET.Modules.Settings` + `Full.NET.Modules.Settings.Contracts`；表 `fn_settings_dict_type`、`fn_settings_dict_item`；权限 `settings.dict_types.read` / `settings.dict_types.write`；API 前缀 `/api/v1/settings/dict-types`。

**Tech Stack:** DbUp 020 双库迁移、Dapper、ProblemDetails、Vue/Layui 同步、Playwright。

---

## 范围与非目标

### 本切片必须交付

1. 双库迁移 `020_SettingsDictionary.sql`（类型 + 项）。
2. Host 字典类型分页列表、详情、创建、更新、禁用。
3. Host 字典项按类型分页列表、创建、更新、禁用。
4. 权限与导航 Contributor（`dict-types` → `/settings/dict-types`）。
5. Vue/Layui 双端管理 UI、Mock/真实栈 E2E、OpenAPI 夹具。

### 明确非目标

- 租户级字典（`TenantId` 非空行）、用户级配置。
- 系统强类型配置键（`ISettingsStore`）、运行时缓存失效策略。
- L5 业务翻译表（字典展示文本多语言版本化）。

---

## 附录 A：数据模型（Task 1 冻结）

### `fn_settings_dict_type`

| 列 | 类型 | 说明 |
|---|---|---|
| Id | UUID v7 PK | 应用生成 |
| Code | varchar(64) | Host 全局唯一稳定码 |
| Name | nvarchar(128) | 显示名称 |
| Description | nvarchar(512) NULL | 备注 |
| DisplayOrder | int | 排序 |
| IsActive | bit | 禁用后保留历史 |
| CreatedAtUtc / UpdatedAtUtc / Version | | 审计与乐观锁 |

唯一约束：`UX_fn_settings_dict_type_Code` on `(Code)`。

### `fn_settings_dict_item`

| 列 | 类型 | 说明 |
|---|---|---|
| Id | UUID v7 PK | 应用生成 |
| DictTypeId | UUID FK | 所属类型 |
| Label | nvarchar(128) | 展示文本 |
| Value | varchar(128) | 稳定机器值（类型内唯一） |
| Color | varchar(32) NULL | 可选 UI 色值 |
| DisplayOrder | int | 排序 |
| IsActive | bit | 禁用 |
| CreatedAtUtc / UpdatedAtUtc / Version | | 审计与乐观锁 |

唯一约束：`UX_fn_settings_dict_item_Type_Value` on `(DictTypeId, Value)`。

---

## 附录 B：API 验收表（草案）

| 场景 | 方法 | 路径 | 权限 | 成功 | 典型失败 |
|---|---|---|---|---|---|
| 类型列表 | GET | `/api/v1/settings/dict-types` | `settings.dict_types.read` | 200 | 403 |
| 类型详情 | GET | `/api/v1/settings/dict-types/{id}` | read | 200 | 404 |
| 创建类型 | POST | `/api/v1/settings/dict-types` | write | 201 | 409 `settings.dict_type.code_exists` |
| 更新类型 | PUT | `/api/v1/settings/dict-types/{id}` | write | 200 | 409 version_conflict |
| 禁用类型 | POST | `/api/v1/settings/dict-types/{id}/disable` | write | 200 | 409 `settings.dict_type.items_active` |
| 项列表 | GET | `/api/v1/settings/dict-types/{typeId}/items` | read | 200 | 404 type |
| 创建项 | POST | `/api/v1/settings/dict-types/{typeId}/items` | write | 201 | 409 value_exists |
| 更新项 | PUT | `/api/v1/settings/dict-items/{id}` | write | 200 | version_conflict |
| 禁用项 | POST | `/api/v1/settings/dict-items/{id}/disable` | write | 200 | 404 |

---

## 任务分解

### Task 1: 规格冻结、迁移与 RED 夹具

1. [x] 本计划附录 A/B。
2. [x] 双库迁移 `020_SettingsDictionary.sql`。
3. [x] `Full.NET.Modules.Settings` + Contracts 项目骨架；注册 `FullNetModuleCatalog`。
4. [x] RED 集成测试：无 `settings.dict_types.read` 时列表 403。
5. [x] Architecture 测试与 Integration 门槛上调。

### Task 2–3: 后端 API

- [x] 查询/命令服务、Dapper SQL、错误资源、JSON 源生成上下文

### Task 4–5: 双端 UI 与 E2E

- [x] Vue `DictTypesView`、Layui `dict-types.js`、导航白名单、shell-parity 场景
  - client-contracts `settings-dict-types` 契约守卫 + `dict-types` 导航白名单
  - admin-i18n `dictTypes.*` 双语文案
  - shell-parity「字典类型列表、创建与禁用在两端保持一致」双端通过
  - 字典项 UI（按类型管理 Label/Value/Color）：见跟进切片 [`2026-07-25-settings-dict-items-ui-vertical-slice.md`](2026-07-25-settings-dict-items-ui-vertical-slice.md)（已完成）

### Task 6: 验证记录与状态矩阵

- [x] [`settings-dictionary-2026-07-25.md`](../../verification/settings-dictionary-2026-07-25.md)
- [x] `capability-status.md` 新增「Settings Host 数据字典」`Build-verified` 行；`adminnet-feature-parity.md`「字典管理」Designing → Build-verified
- [x] `tests/openapi/settings-dict-types-contract.test.mjs` 把夹具纳入 `pnpm test:openapi`（**18 → 20**）
- [x] 门槛核对增补：Architecture **37**、Integration **136**、OpenAPI **20**、shell-parity **36**、客户端单测 contracts **39**/Vue **138**/Layui **80**
- [x] 修复 Unit 全量暴露的既有失败 `AuthorizationCatalogTests`（缺 `tenancy.tenant_packages.*` 期望项），Unit 恢复 **349/349**

---

## 参考切片

- Host 主数据 CRUD：[`ManageHostTenantPackages`](../src/Modules/Full.NET.Modules.Tenancy/Features/ManageHostTenantPackages/)
- 模块注册：[`OrganizationModule`](../src/Modules/Full.NET.Modules.Organization/OrganizationModule.cs)
