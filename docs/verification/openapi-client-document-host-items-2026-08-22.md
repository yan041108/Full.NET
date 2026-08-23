# OpenAPI 客户端迁移：Document Host Items 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`document-host-items`（`ui/admin/src/api/host-document-items.ts`）
- 计划：[`2026-08-22-openapi-client-document-host-items.md`](../superpowers/plans/2026-08-22-openapi-client-document-host-items.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Document Host Items 切片已通过完整 OpenAPI 客户端门禁。11 个 Operation 具备稳定 `operationId` 与主 Tag `DocumentHostItems`；`host-document-items.ts` 已收缩为薄适配层，blob 预览/下载、multipart 上传与 `openDocumentBlob` 保留。清单由 `pilot` 提升为 `generated`（现共 216 条）。

`GET /api/v1/document/host/items/{itemId}` 具备 `WithName("documentHostGetItem")` 但未纳入 manifest（Vue API 模块未使用）。

下一默认项为 `host-document-tags.ts`；禁止并行批量改写。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `documentHostListItems` | `listDocumentItems` |
| `documentHostCreateItem` | `createDocumentItem` |
| `documentHostUpdateItem` | `updateDocumentItem` |
| `documentHostListItemVersions` | `listDocumentVersions` |
| `documentHostAddItemVersion` | `addDocumentVersion` |
| `documentHostUploadItemVersion` | `uploadDocumentVersion` |
| `documentHostDownloadItemContent` | `downloadDocumentContent` |
| `documentHostPreviewItemContent` | `previewDocumentContent`（无 versionId） |
| `documentHostPreviewItemVersionContent` | `previewDocumentContent`（有 versionId） |
| `documentHostDeleteItem` | `deleteDocumentItem` |
| `documentHostRestoreItem` | `restoreDocumentItem` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 112/112，通过 |
| `npx vitest run src/api/host-document-items.test.ts` | 3/3，通过 |

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
