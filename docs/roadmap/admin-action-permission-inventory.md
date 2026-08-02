# 后台粗粒度权限清零库存

- 日期：2026-08-02
- 权威设计：[Vue 页面/操作精确授权](../superpowers/specs/2026-08-02-vue-action-authorization-design.md)
- 实施计划：[Identity Users 样板](../superpowers/plans/2026-08-02-vue-action-authorization.md)
- 架构门禁：`LegacyCoarseActionPermissionRegistry` + `EndpointAuthorizationTests`（冻结全部现存 `.write` Endpoint 绑定；W1 Identity 粗粒度写权限已全部退役）

## 波次

| 波次 | 模块 | 状态 | 计划 |
| --- | --- | --- | --- |
| W0 | Identity Users | **已完成** | 迁移 054；见[验证记录](../verification/vue-action-authorization-2026-08-02.md) |
| W1 | Identity Roles | **已完成** | 迁移 055；见[验证记录](../verification/vue-action-authorization-roles-2026-08-02.md) |
| W1 | Identity Field Grants | **已完成** | 迁移 056；见[验证记录](../verification/vue-action-authorization-field-grants-2026-08-02.md) |
| W1 | Identity Menus | **已完成** | 迁移 057；见[验证记录](../verification/vue-action-authorization-menus-2026-08-02.md) |
| W1 | Identity Sessions | **已完成** | 迁移 058；见[验证记录](../verification/vue-action-authorization-sessions-2026-08-02.md) |
| W1 | Identity API Keys | **已完成** | 迁移 059；见[验证记录](../verification/vue-action-authorization-api-keys-2026-08-02.md) |
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

## W1：Identity Roles（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `RolesView.vue` / Layui `roles.js` | 页面 | `identity.roles.read` | 无 |
| `RolesView.vue` / Layui | 创建 | `identity.roles.create` | 055 |
| `RolesView.vue` / Layui | 编辑 | `identity.roles.update` | 055 |
| `RolesView.vue` / Layui | 权限树 | `identity.roles.assign_permissions` | 055 |
| `RolesView.vue` / Layui | 数据范围 | `identity.roles.assign_data_scope` | 055 |
| `RolesView.vue` / Layui | 禁用 | `identity.roles.disable` | 055 |

## W1：Identity Field Grants（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `RolesView.vue` / Layui | 查看字段授权 | `identity.role_field_grants.read` | 无 |
| `RolesView.vue` / Layui | 保存字段授权 | `identity.role_field_grants.replace` | 056 |

## W1：Identity Menus（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `MenusView.vue` / Layui `menus.js` | 页面 | `identity.menus.read` | 无 |
| `MenusView.vue` / Layui | 创建 | `identity.menus.create` | 057 |
| `MenusView.vue` / Layui | 编辑 | `identity.menus.update` | 057 |
| `MenusView.vue` / Layui | 禁用 | `identity.menus.disable` | 057 |

## W1：Identity Sessions（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `OnlineSessionsView.vue` / Layui `online-sessions.js` | 页面 | `identity.sessions.read` | 无 |
| `OnlineSessionsView.vue` / Layui | 强制下线 | `identity.sessions.revoke` | 058 |

## W1：Identity API Keys（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `ApiKeysView.vue` / Layui `api-keys.js` | 页面 | `identity.api_keys.read` | 无 |
| `ApiKeysView.vue` / Layui | 创建 | `identity.api_keys.create` | 059 |
| `ApiKeysView.vue` / Layui | 轮换 | `identity.api_keys.rotate` | 059 |
| `ApiKeysView.vue` / Layui | 禁用 | `identity.api_keys.disable` | 059 |

## W2：Tenancy Tenants（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `TenantsView.vue` / Layui `tenants.js` | 页面 | `tenancy.host_tenants.read` | 无 |
| `TenantsView.vue` / Layui | 开通 | `tenancy.tenants.create` | 060 |
| `TenantsView.vue` / Layui | 编辑 | `tenancy.tenants.update` | 060 |
| `TenantsView.vue` / Layui | 禁用 | `tenancy.tenants.disable` | 060 |
| `TenantsView.vue` / Layui | 分配套餐 | `tenancy.tenants.assign_package` | 060 |

## W2：Tenancy Tenant Packages（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `TenantPackagesView.vue` / Layui `tenant-packages.js` | 页面 | `tenancy.tenant_packages.read` | 无 |
| `TenantPackagesView.vue` / Layui | 创建 | `tenancy.tenant_packages.create` | 061 |
| `TenantPackagesView.vue` / Layui | 编辑 | `tenancy.tenant_packages.update` | 061 |
| `TenantPackagesView.vue` / Layui | 禁用 | `tenancy.tenant_packages.disable` | 061 |

## W1–W5：粗粒度 `.write` 仍绑定 Endpoint（冻结清单）

下列权限仍通过 `LegacyCoarseActionPermissionRegistry.AllowedBindings` 冻结；新增 Endpoint 必须先扩展库存并指定目标拆分波次。

| 权限码 | Vue 入口（示例） | 波次 |
| --- | --- | --- |
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
| `identity.roles.write` | **已退役**：不可分配、不可出现在 Endpoint；由 055 展开为精确动作权限 |
| `identity.role_field_grants.write` | **已退役**：不可分配、不可出现在 Endpoint；由 056 展开为 `identity.role_field_grants.replace` |
| `identity.menus.write` | **已退役**：不可分配、不可出现在 Endpoint；由 057 展开为 `identity.menus.create` / `update` / `disable` |
| `identity.sessions.write` | **已退役**：不可分配、不可出现在 Endpoint；由 058 展开为 `identity.sessions.revoke` |
| `identity.api_keys.write` | **已退役**：不可分配、不可出现在 Endpoint；由 059 展开为 `identity.api_keys.create` / `disable` / `rotate` |
| `tenancy.tenants.write` | **已退役**：不可分配、不可出现在 Endpoint；由 060 展开为 `tenancy.tenants.create` / `update` / `disable` / `assign_package` |
| `tenancy.tenant_packages.write` | **已退役**：不可分配、不可出现在 Endpoint；由 061 展开为 `tenancy.tenant_packages.create` / `update` / `disable` |

## 本地 UI（无需权限码）

对话框取消/关闭、表单内本地校验提示、分页控件等仅影响客户端状态、不触发受保护 API 的控件标记为 `local-ui`，不进入授权目录。
