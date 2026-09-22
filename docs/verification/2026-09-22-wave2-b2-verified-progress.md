# Wave 2 — B2 Verified 门禁进度（2026-09-22）

**规则**：无 fresh 双库 real-stack + WCAG 证据不得将 parity 行改为 `Verified`。本记录仅登记本机已执行项。

## 本机已执行（fresh）

| 项 | 命令/结果 | 说明 |
|----|-----------|------|
| 横切治理 | `pnpm test:governance` | 55/55 通过 |
| OpenAPI/客户端 | `pnpm test:openapi` | 175/175 通过（manifest 557 与 `identityRetireHostUser` 对齐） |
| Tenancy F08a | `dotnet test` …`TenantQuota_metric_id_reconciliation` | SqlServer + MySql 双库通过 |
| DataApproval 01 | `dotnet test` …`DataApprovalApiSqlServerTests` | 场景目录双库契约通过 |
| HTTP JSON 源生成 | `SerializationRulesTests.ProductionSerialization` | 修复 `ProvisionTenantMemberRequest` 登记后通过（2026-09-22 本机） |

## CI 观测

| 推送 | Workflow / Run | 结论（滚动更新） |
|------|----------------|------------------|
| `a9eff2df` | `ci` / `35708760050` | 进行中：`integration-matrix` 绿；`client-build-test`、`build-test`、`api-sqlserver`、`api-mysql`、`infrastructure`、`production-totp` 红；`real-stack-e2e*` 待完 |
| `a9eff2df` | `api-native-aot-linux` / `35708760161` | 进行中（`ProvisionTenantMemberRequest` JSON 修复后） |
| `f3ce3710` | `35698509441` | `api-native-aot-linux` 红（JSON 元数据，已修于 `9187daa5`） |

本机（`a9eff2df`）：Release build、`pnpm test:openapi` 175/175、`SerializationRulesTests` 绿；正在本地复跑 `pnpm test:integration:api:sqlserver`。

## 仍待环境（未升 Verified）

| ID | 模块 | 待执行 |
|----|------|--------|
| V1 | RBAC | program affected merge E2E 全绿 |
| V2 | SerialNumbers | `pnpm test:e2e:real` 流水号 spec fresh |
| V3 | Document | 双库 admin-real-stack + runtime `openapi:client:snapshot --update` |
| V4 | Tenancy/Org/Files | admin-parity Document WCAG 已绿；Tenancy/Org/Files 子集待扩路径（real-stack：`host-tenants`、`host-files` 等已有列表 spec） |
| V5 | Identity | 扩展 `host-users.spec.mjs` 退役用例在 CI real-stack 绿 |

## 新增 real-stack 规格（待 CI）

- `data-approval-scenarios.spec.mjs`
- `workflow-instances.spec.mjs`
- `host-users.spec.mjs` 退役场景
- `data-approval-requests.spec.mjs`（列表/详情）
- `workflow-todos-history.spec.mjs`（待办/已办页签）
- `workflow-forms.spec.mjs` 子表列配置发布冒烟
