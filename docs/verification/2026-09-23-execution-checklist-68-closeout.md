# 执行清单 68 — Reporting 分组/定义/参数 Schema/发布版本与编辑页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **68**（依赖 **67**；`ReportingQueryPortCatalog` 静态端口；分组 + 定义草稿 CRUD；`ParameterSchema` 校验；`POST …/publish` 与 `…/versions`；`ReportingDefinitionsView` 编辑壳；**禁**客户端任意 SQL）。

## 选定切片

| 项 | 值 |
|----|-----|
| Query Port | `reporting.database_engine_version`、`reporting.schema_inventory`（审查 SQL 仅服务端） |
| 分组 API | `/api/v1/reporting/groups` CRUD |
| 定义 API | `/api/v1/reporting/definitions` CRUD + `publish` + `versions` |
| 目录 API | `GET /api/v1/reporting/query-ports`（元数据 + 参数定义，无 SQL 文本） |
| Vue | `ReportingDefinitionsView`（分组树/表、定义编辑、Query Port 选择、发布与版本列表） |
| 校验 | `ReportingDefinitionKeyValidator`、`ReportingParameterSchemaValidator`、`ReportingLayoutConfigParser` |

## 交付锚点

| 层 | 位置 |
|----|------|
| 服务 | `ReportingGroupManagementService` / `ReportingDefinitionManagementService` |
| 查询 | `ReportingGroupQueryService` / `ReportingDefinitionQueryService` / `ReportingQueryPortQueryService` |
| 领域 | `ReportingQueryPortCatalog`、`ReportingDefinitionJson` |
| 权限 | `reporting.groups.*`、`reporting.definitions.*`（含 `.publish`）、`reporting.query_ports.read` |
| 契约 | OpenAPI `ReportingGroups` / `ReportingDefinitions` / `ReportingQueryPorts` |
| 单元 | `ReportingQueryPortCatalogTests`、`ReportingDefinitionKeyValidatorTests`、`ReportingParameterSchemaValidatorTests`、`ReportingLayoutConfigParserTests` |
| 前端 | `api/reporting-definitions.ts`；Vitest `ReportingDefinitionsView.test.ts` |

## 清单 68 验收结论

- **已有**：草稿与发布版本分表快照；未知 `queryPortKey` → **422** `reporting.query_port.not_found`；数据源/分组存在性校验。
- **本槽**：`phase-c-68-reporting-definitions.spec.mjs`；`reporting-real-stack.mjs` 扩展（groups/definitions/query-ports）。
- **未验**：real-stack 全链路「创建数据源 → 定义 → publish → versions」（需 67 有效数据源）；有界执行/分页见 **69**（`ReportingExecuteView`）。

## 停止边界

- **69**：`POST …/definitions/{id}/execute` 有界执行、超时/取消、参数结果页；列权限与租户边界。
- **导出任务**（`ReportingExportTasksView`）不在本槽。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~Reporting` | **28/28**（`d40de4e6`） |
| `pnpm exec vitest run` `ReportingDefinitionsView.test.ts` | **1/1** |
| OpenAPI | `client-openapi-normalization-contract.test.mjs` 含 `reporting-definitions` |
| real-stack | `phase-c-68-reporting-definitions.spec.mjs`（需 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
