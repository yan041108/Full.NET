# OpenAPI 客户端迁移：Document Host Categories 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`document-host-categories`（`ui/admin/src/api/host-document-categories.ts`）
- 计划：[`2026-08-22-openapi-client-document-host-categories.md`](../superpowers/plans/2026-08-22-openapi-client-document-host-categories.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Document Host Categories 切片已通过完整 OpenAPI 客户端门禁。4 个 Operation 具备稳定 `operationId` 与主 Tag `DocumentHostCategories`；`host-document-categories.ts` 已收缩为薄适配层，多参数创建/更新签名保留。清单由 `pilot` 提升为 `generated`（现共 205 条）。

`GET /api/v1/document/host/categories/{categoryId}` 具备 `WithName("documentHostGetCategory")` 但未纳入 manifest（Vue API 模块未使用）。

下一默认项为 `host-document-items.ts`；禁止并行批量改写。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `documentHostListCategories` | `listDocumentCategories` |
| `documentHostCreateCategory` | `createDocumentCategory` |
| `documentHostUpdateCategory` | `updateDocumentCategory` |
| `documentHostDeleteCategory` | `deleteDocumentCategory` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 112/112，通过 |
| `npx vitest run src/api/host-document-categories.test.ts` | 2/2，通过 |

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
