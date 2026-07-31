# Settings 租户数据字典纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。本计划用于收口租户数据字典的双端管理 UI、导航与权限体验；后端 API、双库迁移和 OpenAPI 由同一纵向切片同步交付。

- 建立日期：2026-07-29
- 状态：**Build-verified**
- 依赖：[`2026-07-25-settings-dictionary-vertical-slice.md`](2026-07-25-settings-dictionary-vertical-slice.md)（Host 字典模式与共享契约）

**Goal:** 租户管理员在当前租户上下文维护数据字典类型与字典项（列表/创建/更新/禁用），与 Host 字典 UX 对齐。

**Architecture:** 复用 `packages/client-contracts` 的 `SettingsDictType` / `SettingsDictItem` 守卫；API 前缀 `/api/v1/settings/tenant-dict-types` 与 `/api/v1/settings/tenant-dict-items`；权限 `settings.tenant_dict_types.read` / `settings.tenant_dict_types.write`；导航 `tenant-dict-types` → `/settings/tenant-dict-types`。

**Tech Stack:** Vue 3 + Element Plus、Layui 壳层、Vitest 单元测试。

---

## 范围

### 本切片交付

1. Vue：`tenant-dict-types.ts` API 客户端、`TenantDictTypesView.vue`、路由注册。
2. Layui：`tenant-dict-types.js` 控制器、`index.html` 视图节、路由与导航注册。
3. 共享：`navigation-catalog` 白名单、`admin-i18n` 导航标题（租户数据字典）。
4. Mock Parity E2E：字典类型与字典项的列表、创建、禁用双端对等。
5. Layui 只读权限边界：允许查看类型与字典项，不呈现或触发写操作。
6. 验证记录增补（`settings-dictionary-2026-07-25.md` 租户切片注记）。

### 明确非目标

- L5 字典展示文本翻译表。
- 字典缓存失效与强类型配置消费者。

---

## 任务清单

| # | 任务 | 验收 |
|---|---|---|
| 1 | Vue API + View + 路由 | 完成；Vue 类型检查通过 |
| 2 | Layui 控制器 + 壳层接线 | 完成；Layui **105/105**、生产构建通过 |
| 3 | client-contracts 导航白名单 + i18n | 完成；client-contracts **82/82** |
| 4 | 双端 Mock Parity | 完成；类型与字典项 **4/4** |
| 5 | Layui 只读权限边界 | 完成；控制器回归测试 **1/1** |
| 6 | 验证记录更新 | 完成 |

---

## 关键路径

- API：`/api/v1/settings/tenant-dict-types`、`/api/v1/settings/tenant-dict-types/{typeId}/items`、`/api/v1/settings/tenant-dict-items/{id}`
- Vue：`ui/admin/src/views/TenantDictTypesView.vue`、`ui/admin/src/api/tenant-dict-types.ts`
- Layui：`ui/admin-layui/js/core/tenant-dict-types.js`、`index.html` `data-route-view="tenant-dict-types"`
- 权限：`settings.tenant_dict_types.read` / `settings.tenant_dict_types.write`
