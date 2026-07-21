# Identity 菜单管理纵向切片验证记录

- 日期：2026-07-21
- 状态：**Build-verified**（本地 Integration 双库依赖 CI/Testcontainers）
- 计划：[`2026-07-21-identity-menu-management-vertical-slice.md`](../superpowers/plans/2026-07-21-identity-menu-management-vertical-slice.md)

## 交付摘要

| 项 | 说明 |
|---|---|
| 数据表 | `fn_identity_navigation`（迁移 012，SQL Server/MySQL） |
| API | `GET/POST /api/v1/identity/menus`、`GET/PUT /{id}`、`POST /{id}/disable` |
| 权限码 | `identity.menus.read` / `identity.menus.write` |
| 导航合并 | `GET /api/v1/navigation` 合并代码目录 + DB 活动自定义项 |
| 白名单 | `AdminNavigationWhitelist` 与 `navigation-catalog.ts` 同步 |
| OpenAPI 夹具 | `contracts/openapi/identity-host-menus-v1.json`、`pnpm test:openapi`、Integration OpenAPI 断言 |
| Mock parity E2E | `shell-parity.spec.mjs` 菜单列表/创建/禁用（**36** 项总门槛中的 1 场景 × 双端） |
| 真实栈冒烟 | `host-menus.spec.mjs`：管理员列表 + 受限账号 API/UI 403（**26** 项总门槛中的 2 场景 × 双端） |
| 客户端契约 | `packages/client-contracts/src/host-menus.ts` |

## 本地验证

| 命令 | 结果 |
|---|---|
| `dotnet build` | 通过 |
| `dotnet test` UnitTests | **315** 项通过 |
| `pnpm test:clients` | 通过（148 项管理端相关 + uni-app 96） |
| `node --test tests/openapi/*.test.mjs` | **6** 项通过 |

## 非目标（仍属后续切片）

- 按钮权限 CRUD、租户菜单覆盖、组织/数据范围
- DB 中发明新 permission code
- 翻译表（v1 使用 DB Title/Caption）
