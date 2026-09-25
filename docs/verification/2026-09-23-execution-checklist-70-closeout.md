# 执行清单 70 — Reporting 首种导出（Excel）与导出任务页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **70**（依赖 **69**；首种格式 **`excel`**（Open XML）；`ReportingExportTaskRunner` 租约/恢复；`ReportingExportPolicy` 行数/字节/公式注入防护；`GET …/download` 下载授权；`ReportingExportTasksView`；**无** HTML/PDF 本槽）。

## 选定切片

| 项 | 值 |
|----|-----|
| 格式 | `ReportingExportFormatKeys.Excel`（`html`/`pdf` → **422** `reporting.export_task.format_unsupported`） |
| API | `POST/GET /api/v1/reporting/export-tasks`；`GET …/{id}/download` |
| 背压 | `MaxExportRows=5000`、`MaxExportBytes=10MiB`、`FetchPageSize=200`、`MaxInputTextBytes=16MiB` |
| 渲染 | `ReportingExcelExportRenderer`（公式前缀转义）；`ReportingExportOutputStream` |
| 数据源 | `ReportingExportWorkbookSource` 分页复用 execute 路径 |
| Worker | `ReportingExportTaskHostedProcessor` |
| 租户 | 创建/下载 **必须** 可信 `TenantId`（`EnsureTenantContext`） |
| Vue | `ReportingExportTasksView`（新建 Excel 导出、列表、受控下载） |
| 权限 | `reporting.export_tasks.create` / `.read` / `.download` |

## 交付锚点

| 层 | 位置 |
|----|------|
| 服务 | `ReportingExportTaskManagementService`；`ReportingExportTaskQueryService` |
| 执行 | `ReportingExportTaskRunner`；`IReportingExportWorkbookSource` |
| 配置 | `ReportingExportOptions`（`ExecutionEnabled` 等） |
| 迁移 | `180_ReportingExportTask.sql`；`181_ReportingExportTaskPermission.sql` |
| 契约 | `ReportingExportContracts.cs`；OpenAPI `reportingCreateExportTask` / `reportingDownloadExportTask` |
| 单元 | `ReportingExcelExportRendererTests`、`ReportingExportTaskRecoveryTests` |
| Vitest | `ReportingExportTasksView.test.ts` |

## 清单 70 验收结论

- **已有**：创建时快照主体权限码；同步尝试 + Worker 租约恢复；失败任务下载 **422**。
- **本槽**：`phase-c-70-reporting-export-tasks.spec.mjs`；`reporting-real-stack.mjs` 导出 API 辅助（租户令牌）。
- **未验**：real-stack 全链路生成可打开 xlsx（需 67–69 可用发布定义与外部库）；PDF/HTML 另编号。

## 停止边界

- **71**：Printing 模板版本/数据绑定（与 Reporting 导出无耦合）。
- PDF 渲染、硬件打印不在本槽。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~Reporting` | **28/28**（`d40de4e6`） |
| `pnpm exec vitest run` `ReportingExportTasksView.test.ts` | **1/1** |
| OpenAPI | `client-generator-evaluation.test.mjs` 含 `reportingDownloadExportTask` |
| real-stack | `phase-c-70-reporting-export-tasks.spec.mjs`（需 Host + 租户上下文） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
