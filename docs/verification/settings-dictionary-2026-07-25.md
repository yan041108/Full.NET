# Settings Host 数据字典验证记录

- 日期：2026-07-25
- 状态：**Build-verified**
- 计划：[`2026-07-25-settings-dictionary-vertical-slice.md`](../superpowers/plans/2026-07-25-settings-dictionary-vertical-slice.md)

## 交付范围

首个 **Settings 模块**（`Full.NET.Modules.Settings` + `Full.NET.Modules.Settings.Contracts`）与 Host 作用域数据字典：

| 层级 | 内容 |
|---|---|
| 迁移 | `020_SettingsDictionary.sql`（SQL Server + MySQL）：`fn_settings_dict_type`、`fn_settings_dict_item` |
| 字典类型 API | 分页列表、详情、创建、更新（乐观锁）、禁用（存在启用字典项时拒绝） |
| 字典项 API | 按类型分页列表、创建、更新（乐观锁）、禁用；`(DictTypeId, Value)` 类型内唯一 |
| 权限与导航 | `settings.dict_types.read` / `settings.dict_types.write`；导航 `dict-types` → `/settings/dict-types` |
| 双端 UI | Vue `DictTypesView.vue`、Layui `dict-types.js`（字典类型列表/创建/编辑/禁用；**选型后内嵌字典项**列表/创建/编辑/禁用） |
| 共享契约 | `packages/client-contracts/src/settings-dict-types.ts` 运行时守卫（类型 + 项）+ 导航白名单 |
| OpenAPI | `contracts/openapi/settings-dict-types-v1.json` + 静态夹具测试 + Integration 运行时断言 |

**明确未交付**：租户级字典行、系统强类型配置键（`ISettingsStore`）、字典缓存失效策略、L5 字典展示文本翻译表。真实栈脚本已交付（`host-dict-types.spec.mjs`）。

## 验证矩阵（2026-07-25 新鲜输出）

| 门禁 | 结果 |
|---|---|
| `dotnet build -c Release`（全解决方案） | **0 警告 / 0 错误** |
| Architecture `--minimum-expected-tests 37` | **37/37** |
| Unit `--minimum-expected-tests 349` | **349/349**（首次全量运行暴露 1 项既有失败，已修复，见下） |
| Compatibility `--minimum-expected-tests 7` | **7/7** |
| `pnpm test:openapi` | **20/20**（+2 数据字典夹具契约） |
| `pnpm test:clients` 相关包 | contracts **40**、admin-i18n **8**、Vue **140**、Layui **81** 全通过（字典项增补后） |
| Vue 类型检查 `vue-tsc --noEmit` | 通过 |
| `client-contracts` `tsc -p` | 通过 |
| Parity E2E 聚焦 | 「字典类型…」**2/2**；「字典项列表、创建与禁用在两端保持一致」**2/2**（Vue + Layui，2026-07-25 增补） |
| Parity E2E 全量 | 预计 **78** 项（`shell-parity` **38** 含字典类型与字典项；其余场景未在本增补中重跑全量） |
| `pnpm test:naming` | **23/23**（先暴露 8 处未登记 `dynamic_sql`，已补登债务，见下） |
| `pnpm test:governance` | **7/7**（含四处 canonical 门槛与最新审计记录一致性） |
| `pnpm test:skills` | **52** 项契约检查通过（新增「对外 HTTP 契约冻结」场景） |
| `git diff --check` | 干净 |

.NET 套件与全部静态门禁均在本次修复后的最终代码状态重跑；客户端单测、类型检查与 Parity E2E 为 Task 4–5 的运行结果，此后前端与 E2E 代码未再改动。

**未执行**：Settings 双库 Integration（`SettingsApiSqlServerTests` / `SettingsApiMySqlTests`，含 403 授权、重复编码 409、乐观锁冲突、禁用含启用项拒绝、字典项生命周期与 OpenAPI 运行时断言）。当前机器缺容器运行时（Docker 未运行），脚本已交付并计入 CI Integration 门槛 **136**，由 `push main` 全量矩阵覆盖。因此不得标记为 `Verified`。

## 增补（2026-07-25，字典项双端 UI）

| 门禁 | 结果 |
|---|---|
| `pnpm --filter @fullnet/client-contracts test` | **40/40**（+1 字典项守卫） |
| `pnpm --filter @fullnet/admin-i18n test` | **8/8** |
| Vue `dict-types` 聚焦 | **7/7**（含项 API） |
| Layui `dict-types` 聚焦 | **2/2**（含选型创建项） |
| `vue-tsc --noEmit` | 通过 |
| Parity E2E 聚焦「字典项列表、创建与禁用」 | **2/2** |

计划：[`2026-07-25-settings-dict-items-ui-vertical-slice.md`](../superpowers/plans/2026-07-25-settings-dict-items-ui-vertical-slice.md)。

## 增补（2026-07-25，真实栈 E2E 脚本）

| 门禁 | 结果 |
|---|---|
| 脚本 | `tests/e2e/admin-real-stack/tests/host-dict-types.spec.mjs`（2 场景 × 双端） |
| 辅助 | `createSettingsDictTypeViaApi` / `createSettingsDictItemViaApi` |
| 真实栈门槛 | **46 → 50** |
| 新鲜实跑 | SQL Server 真实栈 **4/4**；MySQL 真实栈 **4/4**；Settings Integration **2/2** |

## 真实栈与 Integration 新鲜输出（2026-07-25）

| 命令 | 结果 |
|---|---|
| SqlServer `bootstrap` + `host-dict-types` | **4/4** 通过（约 16.6s） |
| MySql `FULLNET_E2E_DATABASE_PROVIDER=MySql bootstrap` + `host-dict-types` | Migrator 至 `020_SettingsDictionary`；**4/4** 通过（约 18.8s） |
| Integration `FullyQualifiedName~SettingsApi` | 首次因 `FindById`→`DictItemIdentityRecord` 映射 500；补 `FindIdentityById` 后 **2/2**（约 42s） |

## 关键路径

- API：`/api/v1/settings/dict-types`、`/api/v1/settings/dict-types/{dictTypeId}/items`、`/api/v1/settings/dict-items/{dictItemId}`
- 模块：`src/Modules/Full.NET.Modules.Settings/`（`Features/ManageHostDictTypes`、`Features/ManageHostDictItems`）
- 错误码：`SettingsErrorCodes`（`code_exists`、`not_found`、`version_conflict`、`items_active`、`value_exists`）+ `SettingsErrors.resx` / `SettingsErrors.en-US.resx`
- Vue：`ui/admin/src/views/DictTypesView.vue`、`ui/admin/src/api/dict-types.ts`
- Layui：`ui/admin-layui/js/core/dict-types.js`、`index.html` `data-route-view="dict-types"`
- 双端壳层分组：`shell.navGroup.settings`（`/settings` 前缀）
- Mock parity E2E：`shell-parity.spec.mjs`「字典类型列表、创建与禁用在两端保持一致」

## 顺带修复的既有缺陷

**1. `AuthorizationCatalogTests.Built_in_contributors_publish_the_initial_permission_set`**：本轮首次运行 Unit 全量（此前几轮为 filter 聚焦运行，命中 `--minimum-expected-tests` 冲突后未补跑全量）暴露该用例失败——2026-07-23 套餐切片新增的 `tenancy.tenant_packages.read` / `.write` 未登记到内置权限集合断言。已补齐期望列表，Unit 恢复 **349/349**。教训：filter 运行被门槛拒绝时必须补一次全量，不能视为已验证。

**2. `pnpm test:naming` 主干红灯**：`仓库 SQL 与登记的 C# 静态 SQL 不存在未登记命名债务` 报出 8 处未登记 `dynamic_sql`——MySQL `017_OutboxDeadLetter.sql`（已提交于 `89eb434`）与 `019_TenancyTenantPackageAssignment.sql`（本分支未提交）都用 `INFORMATION_SCHEMA` + `PREPARE` 实现可重入列/外键追加，却未按 004/005/006 的既有先例登记精确债务。已补登两条文件级条目（债务 **85 → 87**，`dynamic_sql` **14 → 16**），`pnpm test:naming` 恢复 **23/23**。本切片的 `020_SettingsDictionary.sql` 只用 `CREATE TABLE IF NOT EXISTS`，不产生该类债务。

**3. `accessibility-i18n.spec.mjs`**「320 CSS px 下不产生页面级水平溢出」在 Layui 项目稳定超时。以 `git stash` 对比确认与本切片无关：2026-07-24 Art 壳层改造在该用例加入了点击「系统管理员」用户菜单的断言，而该按钮只存在于 Vue 的 Art 壳层，Layui 的退出按钮直接位于侧栏。已按同文件既有的 `isVueAdminProject` 双端分支模式修正，Vue/Layui 双端通过。

**4. 字典项 `disable`/`update` 的 Dapper 映射**：管理服务把含 `CreatedAtUtc`/`UpdatedAtUtc` 的 `FindById` 结果物化为不含时间戳的 `DictItemIdentityRecord`，双库 Integration 在 disable 路径稳定 500。已新增 `FindIdentityById`（对齐字典类型模式），Integration **2/2**。Mock parity 未覆盖项禁用的真实 API，故此前未发现。

## 规则与 Skill 演进

| 载体 | 变更 | 触发证据 |
|---|---|---|
| [`development-quality.md`](../../rules/development-quality.md) §11.4 | 澄清为强制：聚焦运行被 `--minimum-expected-tests` 拒绝后必须补跑全量，禁止把聚焦或被拒绝的运行表述为全量通过 | 上述缺陷 1（既有失败潜伏两轮） |
| [`naming-conventions.md`](../../rules/naming-conventions.md) §10.6 | 新增强制条：MySQL `PREPARE` 可重入 DDL 必须同批登记精确 `dynamic_sql` 债务并运行 `pnpm test:naming` | 上述缺陷 2（017、019 两次独立出现，命中重复性门槛） |
| [`fullnet-module-delivery`](../../.agents/skills/fullnet-module-delivery/SKILL.md) delivery-map | 新增「新对外 HTTP 端点」映射行：夹具、`tests/openapi` 静态门禁与 Integration 运行时断言三处必须同时落地 | 本切片 Task 2–3 只建夹具未登记 node 测试，`pnpm test:openapi` 因此漏掉 Settings；已按 RED（契约新增场景失败）→ GREEN（52 项检查通过）流程修改 |

## 状态结论

Settings 数据字典类型与字典项已具备后端 API、双端 UI、Mock 对等 E2E、**SQL Server + MySQL 真实栈聚焦**与双库 Integration。**租户级字典**已交付 API + 双端 UI + OpenAPI + Parity + 真实栈（见下节增补）。L5 翻译与系统强类型配置未交付。状态保持 `Build-verified`，不得标为 `Verified`。

## 增补（2026-07-29，租户数据字典纵向切片）

| 层级 | 内容 |
|---|---|
| 迁移 | `033_SettingsTenantDictionaryScope.sql`（SqlServer 过滤唯一索引 + MySql COALESCE 函数索引） |
| API | `/api/v1/settings/tenant-dict-types`、`/api/v1/settings/tenant-dict-types/{typeId}/items`、`/api/v1/settings/tenant-dict-items/{id}` |
| 权限与导航 | `settings.tenant_dict_types.read` / `settings.tenant_dict_types.write`；导航 `tenant-dict-types` |
| 双端 UI | Vue `TenantDictTypesView.vue`、Layui `tenant-dict-types.js` |
| OpenAPI | `settings-tenant-dict-types-v1.json` + node 合同测试 **2/2**；Integration OpenAPI 校验编入 `SettingsTenantDictTypeManagementAssertions` |
| Parity E2E | shell-parity 租户字典类型 **2/2** + 字典项 **2/2** |
| 真实栈 E2E | `host-tenant-dict-types.spec.mjs` **4/4**（Vue + Layui × 管理员加载/创建 + viewer 403） |
| Integration | `Tenant_dict_type_management_follows_contract_*` 双库（含租户字典项生命周期） |

计划：[`2026-07-29-settings-tenant-dictionary-vertical-slice.md`](../superpowers/plans/2026-07-29-settings-tenant-dictionary-vertical-slice.md)。

### 双端权限体验收口（2026-07-29）

Layui 原实现只通过静态 `data-permission` 隐藏创建表单，控制器打开字典项面板时会重新显示表单，并向只有读权限的用户生成编辑、禁用按钮。服务端授权仍会拒绝越权请求，但与 Vue 的只读体验不一致。现由控制器动态读取 `settings.tenant_dict_types.write`，统一约束类型/字典项创建表单、动态写按钮和写事件。

| 命令 | 结果 |
|---|---|
| `pnpm --filter @fullnet/admin-layui exec vitest run tests/tenant-dict-types.test.js --maxWorkers=1` | **1/1** |
| `pnpm --filter @fullnet/admin-layui exec vitest run --maxWorkers=1` | **105/105** |
| `pnpm --filter @fullnet/admin typecheck` | 通过 |
| `pnpm --filter @fullnet/admin-layui build` | 通过 |
| `pnpm --filter @fullnet/admin-parity-e2e test -- --grep "租户字典" --workers=1` | **4/4** |
| `dotnet build src/Modules/Full.NET.Modules.Settings/Full.NET.Modules.Settings.csproj --no-restore -c Release` | **0 警告 / 0 错误** |

**未交付**：字典缓存失效、L5 字典展示文本翻译、强类型配置消费者。
