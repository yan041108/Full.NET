# Tenancy Host 租户套餐目录验证记录

- 日期：2026-07-23
- 状态：**Build-verified**
- 计划：[`2026-07-23-tenancy-tenant-package-vertical-slice.md`](../superpowers/plans/2026-07-23-tenancy-tenant-package-vertical-slice.md)

## 交付范围

Host 作用域租户套餐目录：分页列表、创建、更新名称/描述、禁用；仍有租户绑定时禁用返回 `tenancy.tenant_package.in_use`；双库迁移 `fn_tenancy_tenant_package`；Vue/Layui 双管理端 UI；OpenAPI 夹具与 Mock/真实栈 E2E 脚本。

**补充（2026-07-24）**：套餐禁用引用保护见[计划](../superpowers/plans/2026-07-24-tenancy-tenant-package-disable-in-use-vertical-slice.md)。

**明确未交付**：租户与套餐绑定、过期/配额策略、Production 维护窗口命名升级实跑。

## 验证矩阵

| 门禁 | 结果 |
| --- | --- |
| Release 构建 | 0 警告 / 0 错误 |
| Integration `Host_tenant_package` | **2/2**（SQL Server + MySQL，含 OpenAPI 运行时断言） |
| `pnpm test:openapi` | **18/18** |
| `pnpm test:clients` | 通过（管理端 **170**：contracts **37**、i18n **8**、Vue **67**、Layui **58**） |
| Parity E2E 聚焦 | `套餐列表、创建与禁用` **2/2** |
| Parity E2E 全量门槛 | **50** 项（25 场景 × 双端） |
| 真实栈脚本 | `host-tenant-packages.spec.mjs`（2 场景 × 双端） |

## 关键路径

- API：`/api/v1/tenancy/tenant-packages`
- 权限：`tenancy.tenant_packages.read` / `tenancy.tenant_packages.write`
- Vue：`ui/admin/src/views/TenantPackagesView.vue`
- Layui：`ui/admin-layui/js/core/tenant-packages.js`
- OpenAPI：`contracts/openapi/tenancy-host-tenant-packages-v1.json`

Host 租户套餐目录已与 Host 租户管理切片对齐；租户绑定与开通可选套餐已完成；C2.1 完整对标（过期/配额）未完成，不得标为 `Verified`。
