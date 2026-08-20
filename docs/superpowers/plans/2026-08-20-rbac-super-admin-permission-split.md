# 实施计划：RBAC 超管 grant/revoke 拆分

> **For agentic workers:** 按 Task 顺序执行；RED → GREEN；单独提交。

**目标：** 拆分 `identity.super_administrators.manage` 为 grant/revoke，清空粗粒度豁免，同步 Vue/OpenAPI/迁移。

**规格：** [`2026-08-20-rbac-super-admin-permission-split-design.md`](../specs/2026-08-20-rbac-super-admin-permission-split-design.md)

**快照：** `pnpm test:task:start -- rbac-three-level-permissions-20260820`

## Task 1：RED 测试

- Architecture：断言 grant/revoke 绑定新码；`.manage` 退役且 AllowedBindings 不含超管。
- Unit：Catalog 含 grant/revoke。
- Vue：`SuperAdministratorsView` 分闸。
- OpenAPI fixture 更新预期失败直至 GREEN。

## Task 2：GREEN 实现

1. `IdentityAuthorizationContributor`：新增 Grant/Revoke 常量与 PermissionDefinition；移除 Manage。
2. `ManageSuperAdministrators/Endpoint.cs`：分别 Require 新权限。
3. 迁移 `099_SuperAdministratorGrantRevokePermissions.sql`（SqlServer + MySql）。
4. Vue / i18n / inventory / OpenAPI / client-contracts。
5. `LegacyCoarseActionPermissionRegistry`：Retired 加入 manage；清空 AllowedBindings。

## Task 3：验证

```powershell
pnpm test:inner -- --snapshot rbac-three-level-permissions-20260820
pnpm --filter @fullnet/admin test
pnpm test:openapi
git diff --check
```
