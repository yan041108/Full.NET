# 后台粗粒度权限清零库存

- 日期：2026-08-02
- 权威设计：[Vue 页面/操作精确授权](../superpowers/specs/2026-08-02-vue-action-authorization-design.md)
- 实施计划：[Identity Users 样板](../superpowers/plans/2026-08-02-vue-action-authorization.md)
- 架构门禁：`LegacyCoarseActionPermissionRegistry` + `EndpointAuthorizationTests`（冻结全部现存 `.write` Endpoint 绑定；禁止 `identity.users.write`）

## 波次

| 波次 | 模块 | 状态 | 计划 |
| --- | --- | --- | --- |
| W0 | Identity Users | **已完成** | 迁移 054；见[验证记录](../verification/vue-action-authorization-2026-08-02.md) |
| W1 | Identity（Roles/Menus/Sessions/API Key/Field Grants） | 待排期 | 独立纵向切片 |
| W2 | Tenancy + Organization | 待排期 | 租户/套餐/机构树等 |
| W3 | Settings + Auditing | 待排期 | 字典/配置/诊断策略/日志只读页 |
| W4 | Files + Notifications + Jobs + CodeGeneration | 待排期 | 含 `jobs.schedules.write` |
| W5 | Document 及后续官方模块 | 待排期 | Document 已 `Verified` 切片，操作拆分另开 |

## W0：Identity Users（已完成）

| Vue 组件 | 操作 | 当前权限 | 目标权限 | 迁移 |
| --- | --- | --- | --- | --- |
| `UsersView.vue` | 页面 | `identity.users.read` | 同左 | 无 |
| `UsersView.vue` | 创建 | ~~`identity.users.write`~~ | `identity.users.create` | 054 |
| `UsersView.vue` | 编辑 | ~~`identity.users.write`~~ | `identity.users.update` | 054 |
| `UsersView.vue` | 角色 | ~~`identity.users.write`~~ | `identity.users.assign_roles` | 054 |
| `UsersView.vue` | 重置密码 | ~~`identity.users.write`~~ | `identity.users.reset_password` | 054 |
| `UsersView.vue` | 禁用/启用 | ~~`identity.users.write`~~ | `identity.users.disable` / `enable` | 054 |
| `UsersView.vue` | 导出 | ~~`identity.users.write`~~ | `identity.users.export` | 054 |
| `RolesView.vue` | 权限树 | `identity.roles.write`（粗粒度门控） | `identity.roles.assign_permissions` 等 | W1 |

## W1–W5：粗粒度 `.write` 仍绑定 Endpoint（冻结清单）

下列权限仍通过 `LegacyCoarseActionPermissionRegistry.AllowedBindings` 冻结；新增 Endpoint 必须先扩展库存并指定目标拆分波次。

| 权限码 | Vue 入口（示例） | 波次 |
| --- | --- | --- |
| `identity.roles.write` | `RolesView.vue` | W1 |
| `identity.role_field_grants.write` | `RolesView.vue` | W1 |
| `identity.menus.write` | `MenusView.vue` | W1 |
| `identity.sessions.write` | `OnlineSessionsView.vue` | W1 |
| `identity.api_keys.write` | `ApiKeysView.vue` | W1 |
| `tenancy.tenants.write` | `TenantsView.vue` | W2 |
| `tenancy.tenant_packages.write` | `TenantPackagesView.vue` | W2 |
| `organization.units.write` | `OrgUnitsView.vue` | W2 |
| `organization.positions.write` | `OrgPositionsView.vue` | W2 |
| `organization.position_levels.write` | `OrgPositionLevelsView.vue` | W2 |
| `organization.user_positions.write` | `OrgUserPositionsView.vue` | W2 |
| `organization.user_units.write` | `OrgUserUnitsView.vue` | W2 |
| `settings.dict_types.write` | `DictTypesView.vue` | W3 |
| `settings.tenant_dict_types.write` | `TenantDictTypesView.vue` | W3 |
| `settings.config.write` | `ConfigEntriesView.vue` | W3 |
| `settings.diagnostic_policy.write` | `DiagnosticPolicyView.vue` | W3 |
| `files.files.write` | `HostFilesView.vue` | W4 |
| `notifications.announcements.write` | `HostAnnouncementsView.vue` | W4 |
| `notifications.inbox.write` | `InboxMessagesView.vue` | W4 |
| `jobs.definitions.write` / `jobs.schedules.write` | `HostJobsView.vue` | W4 |
| `codegen.templates.write` | `CodeGenerationPreviewsView.vue` | W4 |
| `serial_numbers.rules.write` | （API 已交付，Vue 待切片） | W4 |
| `document.host_documents.write` | `HostDocumentItemsView.vue` | W5 |

## 退役权限

| 权限码 | 状态 |
| --- | --- |
| `identity.users.write` | **已退役**：不可分配、不可出现在 Endpoint；由 054 展开为精确动作权限 |

## 本地 UI（无需权限码）

对话框取消/关闭、表单内本地校验提示、分页控件等仅影响客户端状态、不触发受保护 API 的控件标记为 `local-ui`，不进入授权目录。