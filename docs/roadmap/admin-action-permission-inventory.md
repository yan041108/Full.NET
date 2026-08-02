# 后台粗粒度权限清零库存

- 日期：2026-08-03
- 权威设计：[Vue 页面/操作精确授权](../superpowers/specs/2026-08-02-vue-action-authorization-design.md)
- 实施计划：[Identity Users 样板](../superpowers/plans/2026-08-02-vue-action-authorization.md)、[三级授权补齐与 W4–W5](../superpowers/plans/2026-08-03-vue-action-authorization-w4-w5.md)
- 架构门禁：`LegacyCoarseActionPermissionRegistry` + `EndpointAuthorizationTests`（冻结全部现存 `.write` Endpoint 绑定；W1 Identity 粗粒度写权限已全部退役）
- 客户端边界：只登记和交付 `ui/admin` Vue；`ui/admin-layui` 固定在 2026-08-02 冻结树，由 `tests/governance/layui-freeze.test.mjs` 阻止功能性修改。

## 波次

| 波次 | 模块 | 状态 | 计划 |
| --- | --- | --- | --- |
| 前置 | 角色授权模块分组 | **待补齐** | 当前只有页面/操作两级；先执行 W4–W5 计划 Task 0，并让 Host 角色树可选择跨上下文 Tenant 权限 |
| W0 | Identity Users | **已完成** | 迁移 054；见[验证记录](../verification/vue-action-authorization-2026-08-02.md) |
| W1 | Identity Roles | **已完成** | 迁移 055；见[验证记录](../verification/vue-action-authorization-roles-2026-08-02.md) |
| W1 | Identity Field Grants | **已完成** | 迁移 056；见[验证记录](../verification/vue-action-authorization-field-grants-2026-08-02.md) |
| W1 | Identity Menus | **已完成** | 迁移 057；见[验证记录](../verification/vue-action-authorization-menus-2026-08-02.md) |
| W1 | Identity Sessions | **已完成** | 迁移 058；见[验证记录](../verification/vue-action-authorization-sessions-2026-08-02.md) |
| W1 | Identity API Keys | **已完成** | 迁移 059；见[验证记录](../verification/vue-action-authorization-api-keys-2026-08-02.md) |
| W2 | Tenancy + Organization | **已完成** | 迁移 060–066；对应验证记录位于 `docs/verification/vue-action-authorization-*` |
| W3 | Settings | **已完成** | 迁移 067–070；字典、租户字典、配置与诊断策略已拆分 |
| W4 | Files + Notifications + Jobs + CodeGeneration | **已规划** | 按 W4–W5 计划 Tasks 1–6 串行执行，含 `jobs.schedules.write` |
| W5 | SerialNumbers + Document 及后续官方模块 | **已规划** | 按 W4–W5 计划 Tasks 7–10 串行执行 |

## W0：Identity Users（已完成）

| Vue 组件 | 操作 | 当前权限 | 目标权限 | 迁移 |
| --- | --- | --- | --- | --- |
| `UsersView.vue` | 页面 | `identity.users.read` | 同左 | 无 |
| `UsersView.vue` | 创建 | ~~`identity.users.write`~~ | `identity.users.create` | 054 |
| `UsersView.vue` | 编辑 | ~~`identity.users.write`~~ | `identity.users.update` | 054 |
| `UsersView.vue` | 角色 | ~~`identity.users.write`~~ | `identity.users.assign_roles` | 054 |
| `UsersView.vue` | 重置密码 | ~~`identity.users.write`~~ | `identity.users.reset_password` | 054 |
| `UsersView.vue` | 禁用/启用 | ~~`identity.users.write`~~ | `identity.users.disable` / `enable` | 054 |
| `UsersView.vue` | 导出 | `identity.users.export` | 同左（原本已独立） | 无 |

## W1：Identity Roles（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `RolesView.vue` | 页面 | `identity.roles.read` | 无 |
| `RolesView.vue` | 创建 | `identity.roles.create` | 055 |
| `RolesView.vue` | 编辑 | `identity.roles.update` | 055 |
| `RolesView.vue` | 权限树 | `identity.roles.assign_permissions` | 055 |
| `RolesView.vue` | 数据范围 | `identity.roles.assign_data_scope` | 055 |
| `RolesView.vue` | 禁用 | `identity.roles.disable` | 055 |

## W1：Identity Field Grants（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `RolesView.vue` | 查看字段授权 | `identity.role_field_grants.read` | 无 |
| `RolesView.vue` | 保存字段授权 | `identity.role_field_grants.replace` | 056 |

## W1：Identity Menus（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `MenusView.vue` | 页面 | `identity.menus.read` | 无 |
| `MenusView.vue` | 创建 | `identity.menus.create` | 057 |
| `MenusView.vue` | 编辑 | `identity.menus.update` | 057 |
| `MenusView.vue` | 禁用 | `identity.menus.disable` | 057 |

## W1：Identity Sessions（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `OnlineSessionsView.vue` | 页面 | `identity.sessions.read` | 无 |
| `OnlineSessionsView.vue` | 强制下线 | `identity.sessions.revoke` | 058 |

## W1：Identity API Keys（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `ApiKeysView.vue` | 页面 | `identity.api_keys.read` | 无 |
| `ApiKeysView.vue` | 创建 | `identity.api_keys.create` | 059 |
| `ApiKeysView.vue` | 轮换 | `identity.api_keys.rotate` | 059 |
| `ApiKeysView.vue` | 禁用 | `identity.api_keys.disable` | 059 |

## W2：Tenancy Tenants（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `TenantsView.vue` | 页面 | `tenancy.host_tenants.read` | 无 |
| `TenantsView.vue` | 开通 | `tenancy.tenants.create` | 060 |
| `TenantsView.vue` | 编辑 | `tenancy.tenants.update` | 060 |
| `TenantsView.vue` | 禁用 | `tenancy.tenants.disable` | 060 |
| `TenantsView.vue` | 分配套餐 | `tenancy.tenants.assign_package` | 060 |

## W2：Tenancy Tenant Packages（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `TenantPackagesView.vue` | 页面 | `tenancy.tenant_packages.read` | 无 |
| `TenantPackagesView.vue` | 创建 | `tenancy.tenant_packages.create` | 061 |
| `TenantPackagesView.vue` | 编辑 | `tenancy.tenant_packages.update` | 061 |
| `TenantPackagesView.vue` | 禁用 | `tenancy.tenant_packages.disable` | 061 |

## W2：Organization Units（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `OrgUnitsView.vue` | 页面 | `organization.units.read` | 无 |
| `OrgUnitsView.vue` | 创建 | `organization.units.create` | 062 |
| `OrgUnitsView.vue` | 编辑 | `organization.units.update` | 062 |
| `OrgUnitsView.vue` | 禁用 | `organization.units.disable` | 062 |

## W2：Organization Positions（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `OrgPositionsView.vue` | 页面 | `organization.positions.read` | 无 |
| `OrgPositionsView.vue` | 创建 | `organization.positions.create` | 063 |
| `OrgPositionsView.vue` | 编辑 | `organization.positions.update` | 063 |
| `OrgPositionsView.vue` | 禁用 | `organization.positions.disable` | 063 |
| `OrgPositionsView.vue` | 绑定机构 | `organization.positions.assign_unit` | 063 |
| `OrgPositionsView.vue` | 绑定职级 | `organization.positions.assign_position_level` | 063 |

## W2：Organization Position Levels（已完成）

| 组件 | 操作 | 目标权限 | 迁移 |
| --- | --- | --- | --- |
| `OrgPositionLevelsView.vue` | 页面 | `organization.position_levels.read` | 无 |
| `OrgPositionLevelsView.vue` | 创建 | `organization.position_levels.create` | 064 |
| `OrgPositionLevelsView.vue` | 编辑 | `organization.position_levels.update` | 064 |
| `OrgPositionLevelsView.vue` | 禁用 | `organization.position_levels.disable` | 064 |

## W2：Organization User Positions（065）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `OrgUserPositionsView.vue` | 页面 | `organization.user_positions.read` | 无 |
| `OrgUserPositionsView.vue` | 分配 / 可分配用户 | `organization.user_positions.create` | 065 |
| `OrgUserPositionsView.vue` | 设为主职位 | `organization.user_positions.update` | 065 |
| `OrgUserPositionsView.vue` | 取消隶属 | `organization.user_positions.disable` | 065 |

## W2：Organization User Units（066）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `OrgUserUnitsView.vue` | 页面 | `organization.user_units.read` | 无 |
| `OrgUserUnitsView.vue` | 分配 / 可分配用户 | `organization.user_units.create` | 066 |
| `OrgUserUnitsView.vue` | 设为主部门 | `organization.user_units.update` | 066 |
| `OrgUserUnitsView.vue` | 取消隶属 | `organization.user_units.disable` | 066 |

## W3：Settings Dict Types（067）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `DictTypesView.vue` | 页面 | `settings.dict_types.read` | 无 |
| `DictTypesView.vue` | 创建类型/字典项 | `settings.dict_types.create` | 067 |
| `DictTypesView.vue` | 编辑类型/字典项 | `settings.dict_types.update` | 067 |
| `DictTypesView.vue` | 禁用类型/字典项 | `settings.dict_types.disable` | 067 |

## W3：Settings Tenant Dict Types（068）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `TenantDictTypesView.vue` | 页面 | `settings.tenant_dict_types.read` | 无 |
| `TenantDictTypesView.vue` | 创建类型/字典项 | `settings.tenant_dict_types.create` | 068 |
| `TenantDictTypesView.vue` | 编辑类型/字典项 | `settings.tenant_dict_types.update` | 068 |
| `TenantDictTypesView.vue` | 禁用类型/字典项 | `settings.tenant_dict_types.disable` | 068 |

## W3：Settings Config Entries（069）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `ConfigEntriesView.vue` | 页面 | `settings.config.read` | 无 |
| `ConfigEntriesView.vue` | 创建配置项 | `settings.config.create` | 069 |
| `ConfigEntriesView.vue` | 编辑配置项 | `settings.config.update` | 069 |
| `ConfigEntriesView.vue` | 禁用配置项 | `settings.config.disable` | 069 |

## W3：Settings Diagnostic Policy（070）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `DiagnosticPolicyView.vue` | 页面 | `settings.diagnostic_policy.read` | 无 |
| API / 未来编辑入口 | 更新策略 | `settings.diagnostic_policy.update` | 070 |
| `DiagnosticPolicyView.vue` | 恢复安全默认 | `settings.diagnostic_policy.restore` | 070 |

## W4：Files Host Files（071）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `HostFilesView.vue` | 页面 | `files.files.read` | 无 |
| `HostFilesView.vue` | 上传 | `files.files.upload` | 071 |
| `HostFilesView.vue` | 下载 | `files.files.download` | 071 |
| `HostFilesView.vue` | 删除 | `files.files.delete` | 071 |

## W4：Notifications Host Announcements（072）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `HostAnnouncementsView.vue` | 页面 | `notifications.announcements.read` | 无 |
| `HostAnnouncementsView.vue` | 创建 | `notifications.announcements.create` | 072 |
| `HostAnnouncementsView.vue` | 编辑 | `notifications.announcements.update` | 072 |
| `HostAnnouncementsView.vue` | 发布 | `notifications.announcements.publish` | 072 |

## W4：Notifications Inbox Messages（073）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `InboxMessagesView.vue` | 页面/列表 | `notifications.inbox.read` | 无 |
| `InboxMessagesView.vue` | 发送 | `notifications.inbox.send` | 073 |
| `InboxMessagesView.vue` | 标记已读 | `notifications.inbox.mark_read` | 073 |
| `InboxMessagesView.vue` | 全部标记已读 | `notifications.inbox.mark_all_read` | 073 |

## W4：Jobs Host Definitions（074）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `HostJobsView.vue` | 页面 | `jobs.definitions.read` | 无 |
| `HostJobsView.vue` | 创建 | `jobs.definitions.create` | 074 |
| `HostJobsView.vue` | 编辑 | `jobs.definitions.update` | 074 |
| `HostJobsView.vue` | 禁用 | `jobs.definitions.disable` | 074 |
| `HostJobsView.vue` | 手动触发 | `jobs.definitions.trigger` | 074 |

## W4：Jobs Schedules（075）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `HostJobSchedulesView.vue` | 页面 | `jobs.schedules.read` | 无 |
| `HostJobSchedulesView.vue` | 创建 | `jobs.schedules.create` | 075 |
| `HostJobSchedulesView.vue` | 编辑 | `jobs.schedules.update` | 075 |
| `HostJobSchedulesView.vue` | 暂停 | `jobs.schedules.pause` | 075 |
| `HostJobSchedulesView.vue` | 恢复 | `jobs.schedules.resume` | 075 |

## W4：CodeGeneration Templates（076）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `CodeGenerationTemplatesView.vue` | 页面 | `codegen.templates.read` | 无 |
| `CodeGenerationTemplatesView.vue` | 创建 | `codegen.templates.create` | 076 |
| `CodeGenerationTemplatesView.vue` | 更新 | `codegen.templates.update` | 076 |
| `CodeGenerationTemplatesView.vue` | 删除 | `codegen.templates.delete` | 076 |
| `CodeGenerationPreviewsView.vue` | 加载模板到 Schema | `codegen.templates.read` | 无 |

## W4：SerialNumbers Rules（077）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `SerialNumberRulesView.vue` | 页面 | `serial_numbers.rules.read` | 无 |
| `SerialNumberRulesView.vue` | 创建 | `serial_numbers.rules.create` | 077 |
| `SerialNumberRulesView.vue` | 更新 | `serial_numbers.rules.update` | 077 |
| `SerialNumberRulesView.vue` | 启用 | `serial_numbers.rules.enable` | 077 |
| `SerialNumberRulesView.vue` | 禁用 | `serial_numbers.rules.disable` | 077 |
| `SerialNumberRulesView.vue` | 预览 | `serial_numbers.rules.preview` | 077 |

## W5：Document Items（078）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `HostDocumentItemsView.vue` | 页面 | `document.host_documents.read` | 无 |
| `HostDocumentItemsView.vue` | 创建 | `document.host_documents.create` | 078 |
| `HostDocumentItemsView.vue` | 更新 | `document.host_documents.update` | 078 |
| `HostDocumentItemsView.vue` | 上传新版本 | `document.host_documents.add_version` | 078 |
| `HostDocumentItemsView.vue` | 删除 | `document.host_documents.delete` | 无 |
| `HostDocumentItemsView.vue` | 恢复 | `document.host_documents.restore` | 078 |

## W5：Document Categories（079）

| Vue 入口 | 操作 | 权限码 | 迁移 |
| --- | --- | --- | --- |
| `DocumentCategoriesView.vue` | 页面 | `document.categories.read` | 079 |
| `DocumentCategoriesView.vue` | 创建 | `document.categories.create` | 079 |
| `DocumentCategoriesView.vue` | 更新 | `document.categories.update` | 079 |
| `DocumentCategoriesView.vue` | 删除 | `document.categories.delete` | 079 |

## W4–W5：仍需拆分的粗粒度操作权限（冻结清单）

下列 `.write` 权限仍通过 `LegacyCoarseActionPermissionRegistry.AllowedBindings` 冻结；W5 同时包含不以 `.write` 命名、但仍承载多个动作的 `delete/manage` 权限。后续门禁必须覆盖这些语义，新增 Endpoint 必须先扩展库存并指定目标拆分波次。

| 权限码 | Vue 入口（示例） | 波次 |
| --- | --- | --- |
| `document.tags.manage` | `DocumentTagsView.vue`（创建/编辑/删除共用） | W5 |

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
| `organization.units.write` | **已退役**：不可分配、不可出现在 Endpoint；由 062 展开为 `organization.units.create` / `update` / `disable` |
| `organization.positions.write` | **已退役**：不可分配、不可出现在 Endpoint；由 063 展开为 `organization.positions.create` / `update` / `disable` / `assign_unit` / `assign_position_level` |
| `organization.position_levels.write` | **已退役**：不可分配、不可出现在 Endpoint；由 064 展开为 `organization.position_levels.create` / `update` / `disable` |
| `organization.user_positions.write` | **已退役**：不可分配、不可出现在 Endpoint；由 065 展开为 `organization.user_positions.create` / `update` / `disable` |
| `organization.user_units.write` | **已退役**：不可分配、不可出现在 Endpoint；由 066 展开为 `organization.user_units.create` / `update` / `disable` |
| `settings.dict_types.write` | **已退役**：不可分配、不可出现在 Endpoint；由 067 展开为 `settings.dict_types.create` / `update` / `disable` |
| `settings.tenant_dict_types.write` | **已退役**：不可分配、不可出现在 Endpoint；由 068 展开为 `settings.tenant_dict_types.create` / `update` / `disable` |
| `settings.config.write` | **已退役**：不可分配、不可出现在 Endpoint；由 069 展开为 `settings.config.create` / `update` / `disable` |
| `settings.diagnostic_policy.write` | **已退役**：不可分配、不可出现在 Endpoint；由 070 展开为 `settings.diagnostic_policy.update` / `restore` |
| `files.files.write` | **已退役**：不可分配、不可出现在 Endpoint；由 071 展开为 `files.files.upload` / `delete`，并为存量 `read` 补齐 `download` |
| `notifications.announcements.write` | **已退役**：不可分配、不可出现在 Endpoint；由 072 展开为 `notifications.announcements.create` / `update` / `publish` |
| `notifications.inbox.write` | **已退役**：不可分配、不可出现在 Endpoint；由 073 展开为 `notifications.inbox.send`，并为存量 `read` 补齐 `mark_read` / `mark_all_read` |
| `jobs.definitions.write` | **已退役**：不可分配、不可出现在 Endpoint；由 074 展开为 `jobs.definitions.create` / `update` / `disable` / `trigger` |
| `jobs.schedules.write` | **已退役**：不可分配、不可出现在 Endpoint；由 075 展开为 `jobs.schedules.create` / `update` / `pause` / `resume` |
| `codegen.templates.write` | **已退役**：不可分配、不可出现在 Endpoint；由 076 展开为 `codegen.templates.create` / `update` / `delete` |
| `serial_numbers.rules.write` | **已退役**：不可分配、不可出现在 Endpoint；由 077 展开为 `serial_numbers.rules.create` / `update` / `enable` / `disable`；`serial_numbers.rules.read` 补齐 `preview` |
| `document.host_documents.write` | **已退役**：不可分配、不可出现在 Endpoint；由 078 展开为 `document.host_documents.create` / `update` / `add_version`；`document.host_documents.delete` 补齐 `restore` |
| `document.categories.manage` | **已退役**：不可分配、不可出现在 Endpoint；由 079 展开为 `document.categories.read` / `create` / `update` / `delete`；`document.host_documents.read` 补齐 `document.categories.read` |

## 本地 UI（无需权限码）

对话框取消/关闭、表单内本地校验提示、分页控件等仅影响客户端状态、不触发受保护 API 的控件标记为 `local-ui`，不进入授权目录。
