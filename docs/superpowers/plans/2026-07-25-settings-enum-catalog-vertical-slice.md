# Settings 枚举/常量元数据纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。在既有 `Full.NET.Modules.Settings` 内新增垂直切片，禁止再拆 `.csproj`。

- 建立日期：2026-07-25
- 状态：**Build-verified（Task 1–4）**
- 批准依据：
  - [`adminnet-feature-parity.md`](../../roadmap/adminnet-feature-parity.md)「枚举、常量查询」
  - [`capability-status.md`](../../roadmap/capability-status.md) / 客户端路线图 C2.2
  - 系统配置已闭环：[`2026-07-25-settings-system-config-vertical-slice.md`](2026-07-25-settings-system-config-vertical-slice.md)

**Goal:** Host 只读查询代码注册的稳定枚举/常量目录及其成员；显式 Contributor，禁止全程序集反射。

**Architecture:** Settings 模块内 `IEnumCatalogContributor` + `EnumCatalogRegistry`；权限 `settings.enums.read`；API `/api/v1/settings/enum-catalogs`；导航 `enum-catalogs` → `/settings/enum-catalogs`。

**Tech Stack:** 无新迁移；ProblemDetails；Vue/Layui；Playwright。

---

## 范围与非目标

### 必须交付

1. Contributor 契约与 Registry 校验（key 唯一、成员非空）。
2. 列表 + 按键详情只读 API。
3. 首批目录 `settings.config_value_kind`（对齐 `ConfigValueKinds`）。
4. OpenAPI + Integration 双库 + 双端只读 UI + Mock parity + 真实栈冒烟。

### 非目标

- 数据库持久化、动态增删改、租户覆盖。
- CLR enum 反射扫描。
- `ISettingsStore<T>`、列显示个性化、Auditing。

---

## 附录 A：契约

见计划正文中的 `IEnumCatalogContributor` / DTO / HTTP 表（Cursor 计划已冻结）。

---

## 任务分解

### Task 1: Contracts、权限与 RED

1. [x] 本计划。
2. [x] Contracts / 错误码 / 权限。
3. [x] 授权与导航 + client-contracts 白名单。
4. [x] RED：列表 403；Integration **138 → 140**。

### Task 2: 后端 API

1. [x] Registry + Query + Endpoint + OpenAPI。
2. [x] Integration 完整断言。

### Task 3: 双端 UI 与 E2E

1. [x] contracts / i18n / Vue / Layui。
2. [x] shell-parity + 真实栈；门槛上调。

### Task 4: 验证记录与状态矩阵

1. [x] `docs/verification/settings-enum-catalog-2026-07-25.md`
2. [x] capability / adminnet-feature-parity
