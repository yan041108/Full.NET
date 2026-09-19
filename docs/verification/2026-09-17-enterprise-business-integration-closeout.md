# Enterprise 业务接入标准（F09–F11）收口

**范围**：`Presets.Enterprise`、`samples/enterprise-request` 样板 CRUD、工作流提交/回写子集、ImportExport 静态导入子集。

## 交付摘要

| 能力 | 状态 |
| --- | --- |
| F09 CRUD + demo 表 | 已落地 |
| F09 Vue | 已替换桩 |
| F10 提交 + Sink | 已落地 |
| F11 静态导入 | 已落地 |
| Reporting / Printing 集成 | 未验 |
| API 集成 Testcontainers | 已登记（CRUD + 无定义提交失败 + 租户定义发布后提交成功） |

## 预设

`EnterprisePresetModuleNames` = Platform + Webhooks + Workflow + ImportExport + Reporting + Printing + EnterpriseRequest。

## 验证

```bash
dotnet build src/Composition/Full.NET.Composition/Full.NET.Composition.csproj -c Release
dotnet test tests/Full.NET.UnitTests/Full.NET.UnitTests.csproj -c Release --filter "FullyQualifiedName~EnterpriseRequest|FullyQualifiedName~Enterprise_preset"
node --test samples/enterprise-request/tests/schema.test.mjs
node --test tests/deployment/foundation-preset-release.test.mjs
```

## 未验

- 明细行 API、Files 附件集成、Reporting/Printing、审批完成 Sink 端到端（Worker）、C02/C03 全矩阵。

## 2026-09-17 续作

- Vue：列表「提交审批」+ `submitForApproval` API。
- 模板：`preset=enterprise`（`template.json` + `template-options.test.mjs`）。
- 集成：`EnterpriseRequestAssertions` + SqlServer/MySql API 测试；ImportExport 目录含 `demo.enterprise_requests`；`Tenant_submit_for_approval_when_definition_published`（租户内发布 `demo.enterprise_request.approval` 后提交为 `Submitted`）；`Tenant_demo_enterprise_requests_csv_import`（CSV 装入 `.xlsx` 任务执行后列表可见）。
- 单元：`EnterpriseRequestWorkflowOutcomeServiceTests`（终态回写 Submitted→Approved 与门禁）。
- E2E：断言 `.generated-crud-view`。
- ArchitectureTests：230/230 通过（Release）；`samples/` 模块源路径已纳入 Dependency/GlobalSql/Naming 解析。