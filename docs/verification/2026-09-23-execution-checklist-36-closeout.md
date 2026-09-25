# 执行清单 36 — CodeGen catalog column-sync closeout（2026-09-23）

**范围**：清单 §7.B 编号 **36**（元数据增量同步：对照库列与已编辑 Schema 列配置，保留人工 UI、报告增删列；合并仅写回 Schema 编辑器，不触发 Apply）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `POST /api/v1/code-generation/catalog/column-sync`；`GET …/tables/{tableName}/columns` |
| 服务 | `CodeGenerationCatalogQueryService.SyncColumnsAsync` |
| 权限 | `codegen.catalog.read` |
| Vue | `CodeGenerationPreviewsView.vue`（`codegen-catalog-sync-*`） |
| 集成 | `CodeGenerationCatalogAssertions`（column-sync 保留 UI） |
| 前端单测 | `CodeGenerationPreviewsView.test.ts`、`code-generation-catalog.test.ts` |

## CG02 核验结论

- **已有**：刷新表结构差异预览 +「应用合并列到 Schema」；与清单「不丢人工列配置、不直接 Apply」一致。
- **本槽**：补强 real-stack 契约 E2E（原请求体误用 preview schema 字段，已改为 `tableName` + `columns`）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `pnpm exec vitest run` …`CodeGenerationPreviewsView.test.ts` + `code-generation-catalog.test.ts` | **10/10** |
| `dotnet test` …`CodeGenerationApiSqlServerTests` | 本机未跑（Docker/Testcontainers 不可用） |
| real-stack | `host-code-generation-catalog-column-sync.spec.mjs`（需 bootstrap CodeGen 工作区） |

**状态**：Build-verified；升 Verified 受 Gate0 / D-83 约束。
