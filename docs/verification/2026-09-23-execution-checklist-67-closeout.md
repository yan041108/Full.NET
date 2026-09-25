# 执行清单 67 — Reporting 数据源安全配置 / 测试 API / 数据源页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **67**（RP01 首切片：结构化连接字段 + `ReportingDataSourceSecretProtector` 保护密码；列表脱敏；`POST …/test` 只读探活 `SELECT 1`；`ReportingDataSourcesView`；**禁**任意客户端连接串越界；定义/执行/导出见 **68+**）。

## 选定切片

| 项 | 值 |
|----|-----|
| 提供程序 | `sql_server` / `mysql` 白名单 |
| 凭据 | 创建必填密码；更新可空表示保留；持久化 `PasswordProtected`（Data Protection） |
| 列表 | `MaskedServerEndpoint` / `MaskedDatabaseName` / `MaskedUsername` |
| 详情 | 结构化主机/库/账号；`HasPassword`；**无**密码回显 |
| 测试 | `ReportingDataSourceConnectionTester` + `Full.NET-Reporting-Test` 应用名；成功消息提示只读账号 |

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `GET/POST/PUT/DELETE /api/v1/reporting/data-sources`；`…/disable`；`…/test` |
| 服务 | `ReportingDataSourceManagementService`；`ReportingDataSourceQueryService`；`ReportingDataSourceOperationsService` |
| 校验 | `ReportingDataSourceFieldValidator`（拒绝 `;` `=` 等连接串分隔符） |
| 脱敏 | `ReportingDataSourceMasking` + `ReportingDataSourceMapper.MapListItem` |
| Vue | `ReportingDataSourcesView`（CRUD 对话框、测试、禁用/删除） |
| 权限 | `reporting.data_sources.read` / `.create` / `.update` / `.delete` / `.test` |
| 迁移 | `176_ReportingDataSourcePermission.sql`（双库） |
| 契约 | OpenAPI `ReportingDataSources`；`OpenApiOperationIdentityRulesTests` 路径登记 |
| 单元 | `ReportingDataSourceMaskingTests`、`ReportingDataSourceFieldValidatorTests`、`ReportingExternalConnectionMapperTests`、`ReportingAuthorizationContributorTests` 等 |

## 清单 67 验收结论

- **已有**：Host 级分页列表；密码加密存储；连接测试写回 `LastTestStatusKey` / `LastTestMessage`。
- **本槽**：`phase-c-67-reporting-data-sources.spec.mjs`；`reporting-real-stack.mjs`（列表脱敏、422 非法主机、test 404）。
- **未验**：real-stack 对真实外部库的成功 `test`（需运维只读账号）；双库集成纵向 CRUD（无专用 `ImportExport` 式 Assertions，依赖单元 + E2E 契约）。

## 停止边界

- **68**：分组/定义/参数 Schema/发布版本与编辑页；静态 Query Port，**禁**任意 SQL。
- 报表执行、导出任务 UI/API 不在本槽（`ReportingExecuteView` / `ReportingExportTasksView` 另编号）。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~Reporting` | **28/28**（`d40de4e6`） |
| `pnpm exec vitest run` `ReportingDataSourcesView.test.ts` | **1/1** |
| OpenAPI | `client-openapi-normalization-contract.test.mjs` 含 `reporting-data-sources` 导航键 |
| real-stack | `phase-c-67-reporting-data-sources.spec.mjs`（需 Host） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
