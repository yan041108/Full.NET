# 执行清单 24 — 职位 Excel/导入 closeout（2026-09-23）

**范围**：清单 §7.B 编号 **24**（职位模板、预校验、JSON/工作簿导入与逐行结果；**机构批量**为下一独立子切片，本 closeout 不宣称覆盖）。

## 交付锚点

| 层 | 位置 |
|----|------|
| API | `GET …/positions/import-template`、`POST …/import`、`POST …/import-file` |
| 工作簿 | `OrganizationPositionWorkbookCodec` |
| 静态导入 | `TenantPositionsStaticImportSchemaHandler`、`TenantPositionImportPreviewService` |
| Vue | `org-positions` API 模块与职位管理视图 |
| 契约 | `organization-tenant-positions-contract.test.mjs` |

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`OrganizationPositionWorkbookCodecTests` / `TenantPositionImportRecoveryTests` | 单元覆盖导入边界 |
| real-stack | `host-org-positions.spec.mjs` 新增「清单 24」模板 + JSON 导入；CI 待绿 |

**状态**：Build-verified（职位导入子切片）；机构批量仍标「部分」于 [B 区审计](2026-09-23-execution-checklist-b-zone-audit.md)。
