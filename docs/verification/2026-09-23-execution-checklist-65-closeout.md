# 执行清单 65 — ImportExport 静态 Schema 预校验任务 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **65**（首个 **静态 Schema** 导入：`/schemas` 目录、工作表 **模板下载**、multipart **创建任务 + 同步预校验**、`ImportExportTasksView` 任务页；**MaxUploadBytes** / **MaxPreviewRows**；**无**公式执行；首批消费者 **`organization.tenant_positions` / `positions`**）。

## 选定切片

| 项 | 值 |
|----|-----|
| Schema | `organization.tenant_positions`（`TenantPositionsStaticImportSchemaHandler`） |
| 工作表 | `positions` |
| 配置 | `FullNet:ImportExport`（默认上传 1 MiB、预校验 ≤1000 行） |
| 租户 | 任务创建 **必须** 租户上下文（`EnsureTenantContext`） |
| 另注册 | `demo.enterprise_requests`（Enterprise Request 样板，非本槽主证据） |

## 交付锚点

| 层 | 位置 |
|----|------|
| Schema API | `GET /api/v1/import-export/schemas`；`…/worksheets/{key}/template` |
| 任务 API | `POST /api/v1/import-export/tasks`（上传即预校验） |
| 查询 | `GET` 列表/详情（`preview_succeeded` / `preview_failed`） |
| Vue | `ImportExportTasksView`（列表、详情抽屉、预校验统计文案） |
| 权限 | `import_export.static_schemas.read` / `.import_tasks.read` / `.create` |
| 集成 | `ImportExportTaskAssertions.VerifyImportTaskPreviewContractAsync` |
| 契约 | `import-export-v1.json` |

## 清单 65 验收结论

- **已有**：xlsx-only；闭合 Schema/Worksheet；预校验结果写入任务行（`ValidRowCount` / `InvalidRowCount` / `PreviewRowsJson`）。
- **本槽**：`phase-c-65-import-export-static-schema-preview.spec.mjs`；`import-export-real-stack.mjs`。
- **UI 说明**：「提交转换」按钮当前 **disabled**（任务由 API/集成路径创建）；列表/详情与 execute 按钮属任务页壳，**分批执行与错误回执下载** 见 **66**。
- **未验**：real-stack 全量 Worker 执行与错误 xlsx 下载（66）；Magicodes 级通用导出。

## 停止边界

- **66**：`execute` / `resume` / `retry`、`error-receipt` 下载与断点 UI。
- Identity 固定 Excel 职位导入（B-24 `/organization/positions/import`）与 ImportExport 平台 **并行**，不互相替代。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~ImportExport` | **10/10**（`d40de4e6`） |
| `node --test` `import-export-contract.test.mjs` | **1/1** |
| 集成 | `ImportExportApiSqlServerTests` / `ImportExportApiMySqlTests`（含预览契约） |
| real-stack | `phase-c-65-import-export-static-schema-preview.spec.mjs`（需 Host + 租户上下文） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
