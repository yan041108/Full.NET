# Settings Host 系统配置纵向切片实施计划

> **For agents:** 使用 [`fullnet-module-delivery`](../../../.agents/skills/fullnet-module-delivery/SKILL.md)。在既有 `Full.NET.Modules.Settings` 内新增垂直切片，禁止再拆 `.csproj`。

- 建立日期：2026-07-25
- 状态：**Build-verified（Task 1–6）**
- 批准依据：
  - [`adminnet-feature-parity.md`](../../roadmap/adminnet-feature-parity.md)「系统配置」
  - [`capability-status.md`](../../roadmap/capability-status.md) C2.2
  - [总体架构 §6.4](../specs/2026-07-17-fullnet-architecture-design.md#64-settings-与-dictionaries)
  - 字典切片已闭环：[`2026-07-25-settings-dictionary-vertical-slice.md`](2026-07-25-settings-dictionary-vertical-slice.md)

**Goal:** Host 管理员维护系统级配置项（分页列表、按键查询、创建、更新值、禁用）；配置键创建后不可变；值为字符串存储并带 `ValueKind` 元数据。

**Architecture:** 复用 Settings 模块；表 `fn_settings_config_entry`；权限 `settings.config.read` / `settings.config.write`；API 前缀 `/api/v1/settings/config-entries`；导航 `config-entries` → `/settings/config-entries`。

**Tech Stack:** DbUp `021` 双库迁移、Dapper、ProblemDetails、Vue/Layui、Playwright。

---

## 范围与非目标

### 本切片必须交付

1. 双库迁移 `021_SettingsConfigEntry.sql`。
2. Host 配置项分页列表、按 Id/Key 详情、创建、更新（乐观锁）、禁用。
3. 权限与导航 Contributor。
4. OpenAPI 夹具 + Integration 双库 + Vue/Layui 双端 UI + Mock parity E2E。
5. 真实栈冒烟（管理员列表/创建 + 受限 403）。

### 明确非目标

- 租户级 / 用户级覆盖与解析优先级（`用户 > 租户 > 系统 > 默认`）——留后续切片。
- 强类型 `ISettingsStore<T>` 消费者 API 与缓存失效（可在本切片后单独交付）。
- 敏感配置加密 / 密钥保险库对接。
- L5 配置说明多语言。

---

## 附录 A：数据模型（Task 1 冻结）

### `fn_settings_config_entry`

| 列 | 类型 | 说明 |
|---|---|---|
| Id | UUID v7 PK | 应用生成 |
| ConfigKey | varchar(128) | Host 全局唯一稳定键（小写） |
| DisplayName | nvarchar(128) | 显示名称 |
| Description | nvarchar(512) NULL | 备注 |
| ValueKind | varchar(32) | `string` / `boolean` / `integer` / `decimal` / `json` |
| Value | nvarchar(4000) | 规范化字符串存储 |
| DisplayOrder | int | 排序 |
| IsActive | bit | 禁用后保留历史 |
| CreatedAtUtc / UpdatedAtUtc / Version | | 审计与乐观锁 |

唯一约束：`UX_fn_settings_config_entry_ConfigKey` on `(ConfigKey)`。

---

## 附录 B：API 验收表（草案）

| 场景 | 方法 | 路径 | 权限 | 成功 | 典型失败 |
|---|---|---|---|---|---|
| 列表 | GET | `/api/v1/settings/config-entries` | read | 200 | 403 |
| 详情 | GET | `/api/v1/settings/config-entries/{id}` | read | 200 | 404 |
| 按键 | GET | `/api/v1/settings/config-entries/by-key/{configKey}` | read | 200 | 404 |
| 创建 | POST | `/api/v1/settings/config-entries` | write | 201 | 409 key_exists |
| 更新 | PUT | `/api/v1/settings/config-entries/{id}` | write | 200 | version_conflict / validation |
| 禁用 | POST | `/api/v1/settings/config-entries/{id}/disable` | write | 200 | 404 |

---

## 任务分解

### Task 1: 迁移、Contracts 扩展与 RED 夹具

1. [x] 本计划附录 A/B。
2. [x] 双库迁移 `021_SettingsConfigEntry.sql`。
3. [x] Contracts：权限常量、DTO、错误码、`ConfigValueKinds`。
4. [x] RED：无 `settings.config.read` 时列表 403；Integration **136 → 138**。
5. [x] 模块注册查询服务骨架 + 授权/导航 Contributor 扩展 + client-contracts 导航白名单。

### Task 2–3: 后端 API

1. [x] 查询/命令服务、SQL、错误资源、JSON 源生成。
2. [x] Endpoint：列表 / 详情 / by-key / 创建 / 更新 / 禁用。
3. [x] OpenAPI 夹具 `settings-config-entries-v1.json` + Node 契约测试。
4. [x] Integration 完整验收（重复键、ValueKind 校验、乐观锁、禁用、OpenAPI）。

### Task 4–5: 双端 UI 与 E2E

1. [x] client-contracts / admin-i18n / Vue / Layui。
2. [x] shell-parity：「系统配置列表、创建与禁用在两端保持一致」。
3. [x] 真实栈：`host-config-entries.spec.mjs`（管理员加载 + 受限 403）；门槛 **50 → 54**。

### Task 6: 验证记录与状态矩阵

1. [x] `docs/verification/settings-system-config-2026-07-25.md`
2. [x] 更新 capability / adminnet-feature-parity（Build-verified，非 Verified）

---

## 参考

- 字典类型切片：`Features/ManageHostDictTypes`
- 套餐目录：`Features/ManageHostTenantPackages`
