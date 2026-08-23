# OpenAPI 客户端迁移：Document Statistics 切片验证（2026-08-22）

- 决策：`Slice-passed`
- 资源组：`document-host-statistics`（`ui/admin/src/api/document-statistics.ts`）
- 计划：[`2026-08-22-openapi-client-document-host-statistics.md`](../superpowers/plans/2026-08-22-openapi-client-document-host-statistics.md)
- 比较基线：`dde01b32`
- 适用决策：[`ADR-0007`](../architecture/adr/ADR-0007-openapi-driven-client-generation-boundary.md)

## 结论

Document Statistics 切片已通过完整 OpenAPI 客户端门禁。1 个 Operation 具备稳定 `operationId` 与主 Tag `DocumentHostStatistics`；`document-statistics.ts` 已收缩为薄适配层。清单由 `pilot` 提升为 `generated`（现共 230 条）。

Document 模块 Vue 生产 API 已全部迁入 OpenAPI 生成客户端；`vue-client-coverage-v1.json` 所列 45 个生产 API 模块均已登记 manifest。

## 范围

| operationId | Vue 导出 |
| --- | --- |
| `documentHostGetDocumentStatistics` | `getDocumentStatistics` |

## 新鲜验证证据

| 命令 | 结果 |
| --- | --- |
| `pnpm openapi:client:generate -- --check` | 退出码 0，零漂移 |
| `pnpm test:openapi` | 117/117，通过 |
| `npx vitest run src/api/document-statistics.test.ts` | 1/1，通过 |

## 规则与 Skill 复盘

未发现新的规则冲突或稳定 Skill 缺口，不新增规则/Skill 候选。
