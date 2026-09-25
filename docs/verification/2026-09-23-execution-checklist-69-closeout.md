# 执行清单 69 — Reporting 有界执行 / 分页 / 取消 / 超时与参数结果页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **69**（依赖 **68**；`POST …/definitions/{id}/execute`；`ReportingExecutionPolicy` 分页/超时/单元格上限；`ReportingDefinitionExecutionService` 失败即停；`ReportingExecuteView` 参数表单与结果表；列级权限 `reporting.executions.column.schema_name`；**非**导出任务 **70**）。

## 选定切片

| 项 | 值 |
|----|-----|
| API | `POST /api/v1/reporting/definitions/{definitionId}/execute?page=&pageSize=` |
| 请求 | `ExecuteReportingDefinitionRequest`（`versionNumber` + `parameters`） |
| 策略 | `DefaultPageSize=50`、`MaxPageSize=200`、`CommandTimeoutSeconds=30`、`MaxTopN=200` |
| 执行链 | 已发布版本 → `ReportingExecutionParameterBinder` → `ReportingExecutionSqlBuilder` → 外部只读会话 |
| 取消 | HTTP `CancellationToken` 传入 `ExecuteQueryAsync`（客户端断开即中止） |
| Vue | `ReportingExecuteView`（已发布定义筛选、`hasMore` 分页、`commandTimeoutSeconds` 元数据） |
| 权限 | `reporting.executions.run`；列 `reporting.executions.column.schema_name` |

## 交付锚点

| 层 | 位置 |
|----|------|
| 服务 | `ReportingDefinitionExecutionService` |
| 端点 | `Features/ExecuteDefinitions/Endpoint.cs` |
| 领域 | `ReportingExecutionPolicy`、`ReportingExecutionSqlBuilder`、`ReportingExecutionParameterBinder` |
| 连接 | `ReportingDataSourceConnectionFactory` / `IExternalDatabaseSession` |
| 前端 API | `api/reporting-executions.ts` |
| 契约 | `ReportingExecutionContracts.cs`；OpenAPI `reportingExecuteDefinition` |
| 单元 | `ReportingExecutionSqlBuilderTests`、`ReportingExecutionParameterBinderTests` |
| Vitest | `ReportingExecuteView.test.ts` |

## 清单 69 验收结论

- **已有**：未发布定义 **422** `reporting.definition.not_published`；SQL/连接失败 **422** `reporting.execution.failed`；无可见列 **403** `reporting.execution.columns_denied`。
- **本槽**：`phase-c-69-reporting-execute.spec.mjs`；`executeReportingDefinitionViaApi` 辅助。
- **未验**：real-stack 对已发布定义的稳定 **200**（需 67 可用外部只读库）；Host 租户边界在本模块为 Host-only 执行面。

## 停止边界

- **70**：Excel/HTML 导出与 `ReportingExportTasksView`；背压与下载授权。
- 任意客户端 SQL、跨模块业务授权替代仍 **禁止**。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~Reporting` | **28/28**（`d40de4e6`） |
| `pnpm exec vitest run` `ReportingExecuteView.test.ts` | **1/1** |
| OpenAPI | `OpenApiOperationIdentityRulesTests` 登记 `…/execute` |
| real-stack | `phase-c-69-reporting-execute.spec.mjs`（需 Host；可选已发布定义 + 数据源） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
