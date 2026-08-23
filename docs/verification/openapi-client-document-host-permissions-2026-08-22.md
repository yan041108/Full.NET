# OpenAPI 客户端迁移：Document Host Permissions 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`document-host-permissions`（`ui/admin/src/api/document-permissions.ts`）
- 计划：[`2026-08-22-openapi-client-document-host-permissions.md`](../superpowers/plans/2026-08-22-openapi-client-document-host-permissions.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Document Host Permissions 切片已通过完整 OpenAPI 客户端门禁。2 个 Operation 具备稳定 `operationId` 与主 Tag `DocumentHostPermissions`；`document-permissions.ts` 已收缩为薄适配层，请求体验证保留。清单由 `pilot` 提升为 `generated`（现共 222 条）。

下一默认项为 `document-recycle-bin.ts`；禁止并行批量改写。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `documentHostListDocumentPermissions` | `getDocumentPermissionsByDocument` |
| `documentHostSetDocumentPermissions` | `setDocumentPermissions` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 114/114，通过 |
| `npx vitest run src/api/document-permissions.test.ts` | 2/2，通过 |

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
