# 执行清单 52 — 数据库只读目录/元数据检查/迁移草案 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **52**（Host 只读表/视图目录、列元数据检查、双库迁移草案文本；**不**执行任意 SQL 或在线改表）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `/api/v1/code-generation/catalog`（`tables` / `views` / `objects` / `metadata` / `migration-draft`） |
| SQL 边界 | `CodeGenerationCatalogSql` **HostOnly**；与 `DatabaseCatalogQueries` / CodeGen CLI 共用查询文本 |
| 草案 | `CatalogMigrationDraftGenerator`（内存 SQL 文本 + warnings）；**无** DbUp 执行入口 |
| 列同步 | `column-sync`（更新生成模板列 UI，非 DDL）；深度 E2E 见 B-36 `host-code-generation-catalog-column-sync.spec.mjs` |
| Vue | `CodeGenerationCatalogView`（表/视图筛选、元数据表、双库草案展示；视图不显示「生成迁移草案」） |
| 权限 | `codegen.catalog.read`；租户上下文 **403**（集成 `CodeGenerationCatalogAssertions`） |
| 契约 | `contracts/openapi/code-generation-catalog-v1.json`；`tests/openapi/code-generation-contract.test.mjs` §目录 |

## 清单 52 验收结论

- **已有**：只读 information_schema 类扫描；安全表名校验；视图可读元数据但草案仅基础表。
- **本槽**：`phase-c-52-code-generation-catalog.spec.mjs`（目录/元数据/草案 API + 数据库目录页冒烟）。
- **未做（刻意）**：APIJSON/任意表读写；跨模块直连他库表；在线 DDL。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`CodeGenerationCatalogContract` / `CatalogMigrationDraftGenerator` | **4/4**（`d40de4e6`） |
| `node --test` `code-generation-contract.test.mjs`（含目录夹具） | **3/3** |
| real-stack | `phase-c-52-code-generation-catalog.spec.mjs`（需 Host；附着模式见 `codegeneration-attach-skip`） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
