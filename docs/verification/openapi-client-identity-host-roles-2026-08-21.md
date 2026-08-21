# OpenAPI 客户端迁移：Identity Host Roles 切片验证（2026-08-21）

- 决策：`Slice-passed`
- 资源组：`identity-host-roles`（`ui/admin/src/api/roles.ts`）
- 计划：[`2026-08-21-openapi-client-identity-host-roles.md`](../superpowers/plans/2026-08-21-openapi-client-identity-host-roles.md)
- 比较基线：`2a6c3e23`（Task 1 代码变更前）
- 完成提交基线：`d14ec3f7`（Task 3 薄适配）
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Identity remaining 第 1 slice（Host Roles）已通过完整门禁。12 个 Operation 具备稳定 `operationId` 与主 Tag `IdentityHostRoles`，标准快照与生成物零漂移，`roles.ts` 已收缩为薄适配层。清单条目由 `pilot` 提升为 `generated`。允许按队列新建下一个单模块计划（默认 `menus.ts` / `identity-host-menus`）；禁止并行迁移其他资源组，禁止修改 `ui/admin-layui`。

## 范围与提交

| 提交 | 内容 |
| --- | --- |
| `2a6c3e23` | 建立 Identity Host Roles 迁移计划 |
| `1a5d7d9b` | 稳定 Roles/AuthorizationTree/FieldGrants 的 operationId、主 Tag 与 ProblemDetails |
| `f60c88e4` | 冻结标准快照、登记 12 条 pilot、同步生成物并允许快照加法扩容 |
| `d14ec3f7` | 将 `roles.ts` 收缩为生成 Operation 薄适配层 |
| （本 Verification） | 12 条清单升为 `generated` 并记录 `Slice-passed` |

| operationId | Vue 导出 |
| --- | --- |
| `identityGetAuthorizationTree` | `getAuthorizationTree` |
| `identityListFieldProjectionCatalog` | `getFieldProjectionCatalog` |
| `identityListHostRoles` | `listHostRoles` |
| `identityCreateHostRole` | `createHostRole` |
| `identityGetHostRole` | （无页面导出） |
| `identityUpdateHostRole` | `updateHostRole` |
| `identityReplaceHostRolePermissions` | `replaceHostRolePermissions` |
| `identityDisableHostRole` | `disableHostRole` |
| `identityGetHostRoleDataScope` | `getHostRoleDataScope` |
| `identityUpdateHostRoleDataScope` | `updateHostRoleDataScope` |
| `identityGetHostRoleFieldGrants` | `getHostRoleFieldGrants` |
| `identityReplaceHostRoleFieldGrants` | `replaceHostRoleFieldGrants` |

## 验证环境

Windows NT 10.0.19045.0、Node.js 24.12.0、pnpm 10.26.0、.NET SDK 10.0.400。

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 109/109，通过 |
| `pnpm --filter @fullnet/client-contracts test` | 137/137，通过 |
| `pnpm --filter @fullnet/client-contracts build` | 退出码 0 |
| `pnpm --filter @fullnet/admin exec vitest run src/api/roles.test.ts` | 5/5，通过 |
| `pnpm --filter @fullnet/admin test` | 125 文件 / 455 项，全部通过 |
| `pnpm --filter @fullnet/admin build` | 退出码 0 |
| `pnpm audit:clients` | 退出码 0 |
| `pnpm test:naming` | 30/30，通过 |
| `pnpm test:governance` | 38/38，通过 |
| `dotnet build Full.NET.slnx -c Release` | 退出码 0，0 warning、0 error |
| `pnpm test:integration:affected -- --base 2a6c3e23 --phase slice` | smoke 8/8 + Identity 30/30，SQL Server/MySQL 双 Provider，通过 |

说明：Task 4 的 verify snapshot 在 Task 3 已提交且工作区干净时创建，相对该 snapshot 无可验证 diff；切片 Integration 改用计划比较基线 `2a6c3e23`（Task 1 前）执行，覆盖本 slice 全部代码变更。

## 边界与未验证项

- 页面导出签名未改；`dataScopeKind` 与字段投影枚举仍经手写守卫收窄，因 OpenAPI 当前将部分枚举导出为 `string`/`number`。
- 未迁移 Menus、API Keys、会话、TOTP、Super Administrators、`me` 或 auth/session。
- 未修改 `ui/admin-layui`。
- 完整生成式 SDK / 全部 Vue API 迁移仍未完成，不因此把能力矩阵升格为“全量 Build-verified SDK”。

## 规则与 Skill 复盘

本轮风险由 ADR-0007、OpenAPI 兼容门禁与既有测试覆盖；未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
