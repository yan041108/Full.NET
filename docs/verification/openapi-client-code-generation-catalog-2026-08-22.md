# OpenAPI 客户端迁移：Code Generation Catalog 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`code-generation-catalog`（`ui/admin/src/api/code-generation-catalog.ts`）
- 计划：[`2026-08-22-openapi-client-code-generation-catalog.md`](../superpowers/plans/2026-08-22-openapi-client-code-generation-catalog.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Code Generation Catalog 切片已通过完整 OpenAPI 客户端门禁。3 个 Operation 具备稳定 `operationId` 与主 Tag `CodeGenerationCatalog`；`code-generation-catalog.ts` 已收缩为薄适配层。清单由 `pilot` 提升为 `generated`（现共 195 条）。

CodeGeneration `ui/admin/src/api` 队列已全部完成。下一默认项为 `serial-number-rules.ts`；禁止并行批量改写。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `codeGenerationListCatalogTables` | `listCodeGenerationCatalogTables` |
| `codeGenerationListCatalogColumns` | `listCodeGenerationCatalogColumns` |
| `codeGenerationSyncCatalogColumns` | `syncCodeGenerationCatalogColumns` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 通过 |
| `npx vitest run src/api/code-generation-catalog.test.ts` | 1/1，通过 |

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
